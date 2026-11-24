using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Rimketmon.Engine;

/// <summary>
/// 림켓몬 랭크 배틀 학습 환경(림월드 없이 엔진만 돌린다). 표준 입력으로 명령을 받고 표준 출력으로 이진 응답을 준다.
///
/// [한 판씩] (평가·점검용)
///   info                      → [int obs][int actions]
///   reset <seed> <oppMode> <shape> [정책파일]   oppMode: 0=랭크AI 1=기존AI 2=무작위 3=자기학습(정책파일의 신경망이 상대)
///   step <action>
///   응답: [float32 × obs][byte × actions 마스크][float32 보상][byte done][byte win][int32 턴]
///
/// [묶음] (학습용: 환경 N개를 이 프로세스 안에서 스레드로 동시에 돌린다 → 파이썬과는 한 번에 주고받는다)
///   binit <N> <seed> <shape> <selfFrac> [정책폴더]   → [int obs][int actions], 그리고 모든 환경을 시작한 첫 관측(아래 형식에서 보상·done·win 제외)
///   bstep <a0> ... <aN-1>
///   응답: [float32 × N×obs][byte × N×actions][float32 × N 보상][byte × N done][byte × N win]
///   끝난 환경은 곧바로 새 판을 시작하고, 그 새 판의 첫 관측이 실린다(보상·done·win은 방금 끝난 판의 것).
///   상대는 환경이 스스로 고른다: 정책폴더가 있으면 selfFrac 비율로 그 폴더의 정책(절반은 최신, 절반은 아무거나), 나머지는 랭크AI·기존AI.
/// </summary>
static class Program
{
    static readonly string[] Formats = { "pu", "nu", "ru", "uu", "ou", "ubers" };
    static readonly EngineSettings settings = new EngineSettings();
    /// <summary>폼 전용 AI 학습: 상대 팀(foe6)의 1~6마리를 메가·원시·거다이맥스·다이맥스로 바꾼다(RIMKET_FORMFOE=1). 학습 AI 쪽은 기본 조합 그대로.</summary>
    static readonly bool FormFoe = Environment.GetEnvironmentVariable("RIMKET_FORMFOE") == "1";
    /// <summary>관측 끝에 폼 입력을 붙인다(RIMKET_FORMOBS=1, 폼 전용 AI). 기존 AI 평가 때는 끈다.</summary>
    static readonly bool FormObs = Environment.GetEnvironmentVariable("RIMKET_FORMOBS") == "1";
    static readonly int ObsN = AiEncoder.SizeFor(FormObs);

    /// <summary>팀의 1~6마리(바꿀 수 있는 것 중)를 무작위 폼으로 바꾼다. 같은 종류(메가 등) 여러 마리도 허용.</summary>
    static void InjectForms(List<RimketCreature> team, SystemRng rng)
    {
        var can = team.Where(c => FormChange.Options(c).Count > 0 || FormChange.CanDynamax(c)).ToList();
        if (can.Count == 0) return;
        int n = rng.Next(1, can.Count + 1);
        for (int k = 0; k < n; k++)
        {
            int j = rng.Next(k, can.Count); var t = can[j]; can[j] = can[k]; can[k] = t;
            var opts = FormChange.Options(t); bool dmax = FormChange.CanDynamax(t);
            int pick = rng.Next(0, opts.Count + (dmax ? 1 : 0));
            if (pick < opts.Count) FormChange.Apply(t, opts[pick]); else { t.Dynamax = true; t.RecalculateStats(); }
            t.FullHeal();
        }
    }
    static readonly bool AutoPick = Environment.GetEnvironmentVariable("RIMKET_AUTOPICK") == "1";

    /// <summary>환경 하나(한 판의 진행 상태).</summary>
    sealed class Env
    {
        RimketBattle b; AiEncoder enc; double shape; int lastOwn, lastFoe, steps, curSeed; bool done = true;
        List<RimketCreature> mine, foe;
        NeuralPolicy oppPolicy; AiEncoder oppEnc; IRng oppRng;      // 자기학습: 상대(Foe)를 조종하는 과거 시점의 정책
        public float[] Obs = new float[ObsN]; public bool[] Mask = new bool[AiEncoder.Actions];
        public float Reward; public bool Done, Win; public int Turn;
        public bool Started { get { return b != null || picking; } }
        // 멈춤 감시용: 지금 처리 중인 판의 정보와 시작 시각(쉬는 중이면 0)
        public long BusySince; public string BusyWhat = ""; public bool HangLogged;
        public string Describe() { return "시드 " + curSeed + ", 걸음 " + steps + ", " + BusyWhat + ", 상대 " + (oppPolicy != null ? "자기학습" : "AI " + (b != null ? b.FoeAiMode : -1)); }

        // 선출 단계: 판을 시작하면 먼저 내 6마리 중 3마리를 한 마리씩(3걸음) 고른다. 그다음 상대가 고르고 전투가 시작된다.
        string curFmt;
        List<RimketCreature> mine6, foe6; readonly List<int> picks = new List<int>(); bool picking; SystemRng gameRng; int gameMode;

        public void Reset(int seed, int mode, double shapeW, string policyPath)
        {
            curSeed = seed; shape = shapeW; Reward = 0; Win = false; steps = 0;
            BusyWhat = "새 판 준비(상대 모드 " + mode + (policyPath != null ? " " + Path.GetFileName(policyPath) : "") + ")";
            var rng = new SystemRng(seed); gameRng = rng; gameMode = mode;
            string fmt = Formats[rng.Next(0, Formats.Length)]; curFmt = fmt;       // 두 팀은 같은 포맷에서 뽑는다(랭크 매치처럼 비슷한 급끼리 → 팀 운 감소)
            mine6 = RankedBuilder.Build(fmt, RankedRules.PartySize, RankedLadder.BattleLevel, rng, settings);
            foe6 = RankedBuilder.Build(fmt, RankedRules.PartySize, RankedLadder.BattleLevel, rng, settings);
            if (FormFoe) InjectForms(foe6, rng);
            oppPolicy = mode == 3 && policyPath != null ? LoadPolicy(policyPath) : null;
            b = null; mine = null; foe = null; done = false; lastOwn = 0; lastFoe = 0; picks.Clear();
            enc = new AiEncoder(settings) { FormObs = FormObs }; enc.SetPreview(mine6, foe6);
            var mask = enc.PickMask(picks);
            if (AutoPick) { picks.AddRange(RankedRules.PickForFoe(mine6, foe6, rng, settings)); StartBattle(); return; }      // 점검용: 선출을 규칙에 맡긴다
            if (!mask.Any(x => x)) { picks.AddRange(RankedRules.PickForFoe(mine6, foe6, rng, settings)); StartBattle(); return; }      // 고를 수 있는 조합이 없는 드문 팀: 규칙으로 정한다
            picking = true;
            Obs = enc.EncodePick(picks); Mask = mask; Done = false; Turn = 0;
        }

        void PickStep(int a)
        {
            var mask = enc.PickMask(picks);
            if (a < 0 || a >= mask.Length || !mask[a]) { a = Array.IndexOf(mask, true); Reward -= 0.02f; }
            picks.Add(a);
            if (picks.Count < RankedRules.BringCount) { Obs = enc.EncodePick(picks); Mask = enc.PickMask(picks); Done = false; Turn = 0; return; }
            StartBattle();
        }

        /// <summary>상대의 선출: 자기학습이면 상대 정책이 (자기 입장의 프리뷰를 보고) 고르고, 아니면 기존 규칙.</summary>
        List<int> FoePicks()
        {
            if (oppPolicy == null) return RankedRules.PickForFoe(foe6, mine6, gameRng, settings);
            var l = new List<int>();
            while (l.Count < RankedRules.BringCount)
            {
                var mask = oppEnc.PickMask(l);
                if (!mask.Any(x => x)) return RankedRules.PickForFoe(foe6, mine6, gameRng, settings);
                int a = oppPolicy.Choose(oppEnc.EncodePick(l), mask, 1.0, oppRng);
                if (a < 0 || !mask[a]) a = Array.IndexOf(mask, true);
                l.Add(a);
            }
            return l;
        }

        void StartBattle()
        {
            picking = false; bool crashed = false;
            try
            {
                if (oppPolicy != null) { oppEnc = new AiEncoder(settings, true) { FormObs = FormObs }; oppEnc.SetPreview(foe6, mine6); oppRng = new SystemRng(curSeed ^ 0x5bd1e995); }
                var foePicks = FoePicks();
                mine = RankedRules.Brought(mine6, picks, RankedLadder.BattleLevel);
                foe = RankedRules.Brought(foe6, foePicks, RankedLadder.BattleLevel);
                foreach (var c in mine.Concat(foe)) c.FullHeal();
                enc.SetPicks(picks);
                if (oppPolicy != null)
                {
                    oppEnc.SetPicks(foePicks);
                    b = RimketBattle.OnlineBattle(gameRng, settings, "나", mine, "상대", foe);
                    FoeAuto();
                }
                else { b = RimketBattle.RankedBattle(gameRng, settings, "나", mine, "상대", foe); b.FoeAiMode = gameMode == 3 ? 0 : gameMode; }
            }
            catch (Exception ex) { crashed = true; LogCrash(ex, curSeed, mine ?? mine6, foe ?? foe6); }
            Finish(crashed);
        }

        public void Step(int a)
        {
            Reward = 0; Win = false; bool crashed = false; BusyWhat = "행동 " + a;
            if (picking) { BusyWhat = "선출 " + a; PickStep(a); return; }
            if (b != null && !done)
            {
                var act = enc.ToAction(b, a);
                if (!act.HasValue) { var m = enc.Mask(b); a = Array.IndexOf(m, true); act = enc.ToAction(b, a); Reward -= 0.02f; }   // 불법 행동은 첫 합법 행동으로 바꾸고 아주 작은 벌점
                if (act.HasValue)
                {
                    try
                    {
                        if (steps == 2 && Environment.GetEnvironmentVariable("RIMKET_FAULT") == "1") throw new InvalidOperationException("시험용 가짜 예외");
                        if (oppPolicy != null) SelfPlayStep(act.Value); else b.Act(act.Value);
                    }
                    catch (Exception ex) { crashed = true; LogCrash(ex, curSeed, mine, foe); }
                }
                steps++;
            }
            Finish(crashed);
        }

        void Finish(bool crashed)
        {
            if (crashed)
            {
                // 엔진 예외로 끝난 판: 0 관측, 첫 행동만 합법, 보상 0, 종료. 밤샘 학습이 드문 엔진 버그 하나로 멈추지 않게 한다.
                done = true; try { if (b != null) Quit(); } catch (Exception) { }
                Array.Clear(Obs, 0, Obs.Length); for (int i = 0; i < Mask.Length; i++) Mask[i] = i == 0;
                Reward = 0; Done = true; Win = false; Turn = 0;
                return;
            }
            int own = b.Player.Party.Count(c => c.IsFainted), fo = b.Foe.Party.Count(c => c.IsFainted);
            if (shape > 0) Reward += (float)(shape * ((fo - lastFoe) - (own - lastOwn)));
            lastOwn = own; lastFoe = fo;
            if (!b.IsOver && steps > 400) Quit();       // 안전장치: 너무 길면 항복 처리
            if (b.IsOver)
            {
                done = true; Win = b.Result.End == BattleEnd.Victory && !b.Draw; Reward += b.Draw ? 0f : Win ? 1f : -1f;
                // 팀 구성 통계: 전투에 나간 양쪽 3마리씩의 승패를 적는다(무승부·시간 초과 항복은 제외)
                if (meta != null && !b.Draw && steps <= 400 && mine != null && foe != null)
                    lock (meta) { foreach (var c in mine) meta.Record(curFmt, c, Win); foreach (var c in foe) meta.Record(curFmt, c, !Win); }
            }
            Obs = enc.Encode(b); Mask = enc.Mask(b); Done = done; Turn = b.Turn;
        }

        void Quit()
        {
            var dc = new BattleAction(ActionKind.Disconnect);
            if (b.Online) b.OnlineAct(dc, null); else b.Act(dc);
        }

        /// <summary>상대 정책이 Foe 입장에서 고른 행동(확률대로 뽑는다 → 매번 같은 수만 두지 않는다).</summary>
        BattleAction FoeChoice()
        {
            var mask = oppEnc.Mask(b);
            int a = oppPolicy.Choose(oppEnc.Encode(b), mask, 1.0, oppRng);
            var act = oppEnc.ToAction(b, a);
            if (!act.HasValue) act = oppEnc.ToAction(b, Array.IndexOf(mask, true));
            if (!act.HasValue) { var legal = b.FoeLegalActions().Where(x => x.Kind != ActionKind.Disconnect).ToList(); if (legal.Count > 0) act = legal[0]; }
            if (!act.HasValue) throw new InvalidOperationException("상대 정책이 낼 수 있는 행동이 없다");
            return act.Value;
        }

        /// <summary>상대만 교체를 골라야 하는 동안은 환경이 대신 진행한다(학습 쪽에는 자기 차례만 보인다).</summary>
        void FoeAuto()
        {
            int guard = 0;
            while (!b.IsOver && b.PendingFoeSwitch && !b.PendingSwitch && guard++ < 20)
                if (!b.OnlineAct(null, FoeChoice())) throw new InvalidOperationException("상대 교체가 거부되었다");
        }

        void SelfPlayStep(BattleAction my)
        {
            BattleAction? fa = null;
            if (!b.PendingSwitch || b.PendingFoeSwitch) fa = FoeChoice();       // 내가 교체만 고르는 때에는 상대 행동이 없다
            if (!b.OnlineAct(my, fa)) throw new InvalidOperationException("자기학습 행동이 거부되었다: " + my + " / " + fa);
            FoeAuto();
        }
    }

    /// <summary>
    /// 폼 팀 대응 학습 도구(distill.py가 부른다). 모두 --full-info 조건.
    ///   --collect 정책.bin 시작판 판수 출력.bin [sims depth eps threads]  : 미리 계산 AI의 결정 기록
    ///   --bench   정책.bin 시작판 판수 rule|learned|look [sims depth threads] : 상대 쪽(기본 팀) 승률
    /// </summary>
    static int FormTool(string[] a)
    {
        AiEncoder.FullInfo = true;
        var s = new EngineSettings(); var pol = NeuralPolicy.Load(a[1]); int from = int.Parse(a[2]), n = int.Parse(a[3]);
        int th = Environment.ProcessorCount;
        Action<string> rep = m => Console.Error.WriteLine(m);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (a[0] == "--collect")
        {
            int sims = a.Length > 5 ? int.Parse(a[5]) : 24, depth = a.Length > 6 ? int.Parse(a[6]) : 4;
            double eps = a.Length > 7 ? double.Parse(a[7], CultureInfo.InvariantCulture) : 0.1; if (a.Length > 8) th = int.Parse(a[8]);
            int r = FormBench.Collect(pol, s, from, n, sims, depth, eps, th, a[4], rep);
            Console.WriteLine("RESULT collect " + r + " " + sw.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture));
        }
        else if (a[0] == "--pick")
        {
            // --pick 정책.bin 시작판 판수 fixed|rule|policy|best learned|look [sims depth threads]
            int sims = a.Length > 6 ? int.Parse(a[6]) : 24, depth = a.Length > 7 ? int.Parse(a[7]) : 4; if (a.Length > 8) th = int.Parse(a[8]);
            double wr = FormBench.BenchPick(a[4], a[5], pol, s, from, n, sims, depth, th, rep);
            Console.WriteLine("RESULT pick " + a[4] + " " + a[5] + " " + wr.ToString("F4", CultureInfo.InvariantCulture) + " " + sw.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture));
        }
        else
        {
            int sims = a.Length > 5 ? int.Parse(a[5]) : 24, depth = a.Length > 6 ? int.Parse(a[6]) : 4; if (a.Length > 7) th = int.Parse(a[7]);
            double wr = FormBench.Bench(a[4], pol, s, from, n, sims, depth, th, rep);
            Console.WriteLine("RESULT bench " + a[4] + " " + wr.ToString("F4", CultureInfo.InvariantCulture) + " " + sw.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture));
        }
        return 0;
    }

    static int Main(string[] args)
    {
        AiEncoder.FullInfo = Environment.GetEnvironmentVariable("RIMKET_FULLINFO") == "1";      // 상대 정보를 전부 보는 모델(랭크AI와 같은 조건)
        RimketBattle.QuietLog = Environment.GetEnvironmentVariable("RIMKET_LOG") != "1";      // 학습 중에는 배틀 문장을 만들지 않는다(결과는 같음, 속도용)
        if (args.Length > 0 && (args[0] == "--collect" || args[0] == "--bench" || args[0] == "--pick")) return FormTool(args);
        var input = Console.In;
        var outp = new BinaryWriter(new BufferedStream(Console.OpenStandardOutput(), 1 << 20));
        var single = new Env();
        Env[] envs = null; SystemRng[] pick = null; double bShape = 0, selfFrac = 0; string poolDir = null;
        string line;
        while ((line = input.ReadLine()) != null)
        {
            var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) continue;
            if (t[0] == "quit") { SaveMeta(); break; }
            if (t[0] == "info") { outp.Write(ObsN); outp.Write(AiEncoder.Actions); outp.Flush(); continue; }

            if (t[0] == "binit")
            {
                // binit <N> <seed> <shape> <selfFrac>|<공유메모리 파일>|<정책폴더>   (뒤의 둘은 비어 있을 수 있다)
                var parts = line.Split('|'); t = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int n = int.Parse(t[1]); int seed = int.Parse(t[2]);
                bShape = double.Parse(t[3], CultureInfo.InvariantCulture); selfFrac = double.Parse(t[4], CultureInfo.InvariantCulture);
                string shmPath = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : null;
                poolDir = parts.Length > 2 && parts[2].Trim().Length > 0 ? parts[2].Trim() : null;
                metaPath = parts.Length > 3 && parts[3].Trim().Length > 0 ? parts[3].Trim() : null;
                if (metaPath != null) { try { meta = TeamMeta.Load(metaPath); } catch (Exception) { meta = new TeamMeta(); } }
                if (shmPath != null) OpenShm(shmPath, n);
                // 먼저 한 스레드로 몇 판 돌려 엔진의 지연 초기화(각종 표)를 끝내 둔다
                { var w = new Env(); var wr = new SystemRng(7); for (int g = 0; g < 12; g++) { w.Reset(900000 + g, g % 3, 0, null); int guard = 0; while (!w.Done && guard++ < 500) w.Step(Array.IndexOf(w.Mask, true)); } }
                envs = new Env[n]; pick = new SystemRng[n];
                for (int i = 0; i < n; i++) { envs[i] = new Env(); pick[i] = new SystemRng(seed * 7919 + i * 104729 + 13); }
                var e0 = envs; var p0 = pick;
                Parallel.For(0, n, i => NewGame(e0[i], p0[i], bShape, selfFrac, poolDir));
                StartWatchdog(envs);
                outp.Write(ObsN); outp.Write(AiEncoder.Actions);
                WriteBatch(outp, envs, false);
                continue;
            }
            if (t[0] == "bstep" && envs != null)
            {
                int n = envs.Length; var acts = new int[n];
                for (int i = 0; i < n; i++) acts[i] = i + 1 < t.Length ? int.Parse(t[i + 1]) : 0;
                var rew = new float[n]; var dn = new bool[n]; var wn = new bool[n];
                var e0 = envs; var p0 = pick;
                Parallel.For(0, n, i =>
                {
                    var e = e0[i];
                    e.BusySince = DateTime.UtcNow.Ticks;
                    e.Step(acts[i]);
                    rew[i] = e.Reward; dn[i] = e.Done; wn[i] = e.Win;
                    if (e.Done) NewGame(e, p0[i], bShape, selfFrac, poolDir);        // 끝났으면 바로 새 판(관측은 새 판의 것)
                    e.BusySince = 0;
                });
                WriteBatch(outp, envs, true, rew, dn, wn);
                continue;
            }

            if (t[0] == "reset")
            {
                int seed = int.Parse(t[1]); int mode = int.Parse(t[2]); double shape = double.Parse(t[3], CultureInfo.InvariantCulture);
                single.Reset(seed, mode, shape, t.Length > 4 ? string.Join(" ", t.Skip(4)) : null);
            }
            else if (t[0] == "step") single.Step(int.Parse(t[1]));
            if (!single.Started) { outp.Flush(); continue; }
            foreach (var v in single.Obs) outp.Write(v);
            foreach (var m in single.Mask) outp.Write((byte)(m ? 1 : 0));
            outp.Write(single.Reward); outp.Write((byte)(single.Done ? 1 : 0)); outp.Write((byte)(single.Win ? 1 : 0)); outp.Write(single.Turn);
            outp.Flush();
        }
        return 0;
    }

    // 팀 구성 통계(묶음 모드에서 정책폴더 옆에 저장). 학습을 여러 번 나눠 돌려도 이어서 쌓인다.
    static TeamMeta meta; static string metaPath; static int tick;
    static void SaveMeta()
    {
        if (meta == null || metaPath == null) return;
        try { lock (meta) meta.Save(metaPath); } catch (Exception) { }
    }

    /// <summary>멈춤 감시: 한 걸음이 30초 넘게 안 끝나는 환경이 있으면 engine_hang_PID.log에 그 판의 시드를 적는다(재현·디버깅용).</summary>
    static void StartWatchdog(Env[] envs)
    {
        var th = new System.Threading.Thread(() =>
        {
            while (true)
            {
                System.Threading.Thread.Sleep(10000);
                if (++tick % 6 == 0) SaveMeta();          // 팀 구성 통계는 1분마다 저장
                long now = DateTime.UtcNow.Ticks;
                for (int i = 0; i < envs.Length; i++)
                {
                    var e = envs[i]; long since = e.BusySince;
                    if (since == 0) { e.HangLogged = false; continue; }
                    if (e.HangLogged || (now - since) < TimeSpan.FromSeconds(30).Ticks) continue;
                    e.HangLogged = true;
                    try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "engine_hang_" + Environment.ProcessId + ".log"), DateTime.Now + " 환경 " + i + " 멈춤: " + e.Describe() + "\n"); } catch (Exception) { }
                }
            }
        });
        th.IsBackground = true; th.Start();
    }

    /// <summary>묶음 모드의 새 판: 시드와 상대를 환경이 스스로 고른다.</summary>
    static void NewGame(Env e, SystemRng r, double shape, double selfFrac, string poolDir)
    {
        int seed = r.Next(0, int.MaxValue); int mode; string policy = null;
        if (poolDir != null && r.NextDouble() < selfFrac)
        {
            var files = PoolFiles(poolDir);
            if (files.Length > 0) { policy = r.NextDouble() < 0.5 ? files[files.Length - 1] : files[r.Next(0, files.Length)]; mode = 3; }
            else mode = 0;
        }
        else if (poolDir != null) mode = new[] { 0, 0, 1 }[r.Next(0, 3)];
        else mode = new[] { 0, 0, 1, 2 }[r.Next(0, 4)];
        e.Reset(seed, mode, shape, policy);
    }

    static string[] poolCache = new string[0]; static DateTime poolAt = DateTime.MinValue; static readonly object poolLock = new object();
    /// <summary>정책 폴더의 파일 목록(이름순 = 오래된 것부터). 2초에 한 번만 다시 읽는다.</summary>
    static string[] PoolFiles(string dir)
    {
        lock (poolLock)
        {
            if ((DateTime.UtcNow - poolAt).TotalSeconds < 2) return poolCache;
            poolAt = DateTime.UtcNow;
            try { var f = Directory.GetFiles(dir, "*.bin"); Array.Sort(f, StringComparer.Ordinal); poolCache = f; } catch (Exception) { }
            return poolCache;
        }
    }

    // 공유 메모리(파일 매핑): 관측처럼 큰 데이터는 파이프 대신 여기에 써서 파이썬이 바로 읽는다. 파이프로는 "다 썼다"는 1바이트만 보낸다.
    // 배치: [float32 × N×obs][byte × N×actions][float32 × N 보상][byte × N done][byte × N win]
    static System.IO.MemoryMappedFiles.MemoryMappedFile shm; static System.IO.MemoryMappedFiles.MemoryMappedViewStream shmView; static byte[] shmBuf;
    static void OpenShm(string path, int n)
    {
        long cap = (long)n * ObsN * 4 + (long)n * AiEncoder.Actions + n * 4 + n + n;
        var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        fs.SetLength(cap);
        shm = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(fs, null, cap, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false);
        shmView = shm.CreateViewStream(0, cap);
        shmBuf = new byte[cap];
    }

    static void WriteShm(BinaryWriter outp, Env[] envs, bool withResult, float[] rew, bool[] dn, bool[] wn)
    {
        int n = envs.Length, size = ObsN, acts = AiEncoder.Actions;
        int mo = n * size * 4, ro = mo + n * acts, d0 = ro + n * 4, w0 = d0 + n;
        var buf = shmBuf;
        Parallel.For(0, n, i =>
        {
            Buffer.BlockCopy(envs[i].Obs, 0, buf, i * size * 4, size * 4);
            var m = envs[i].Mask; for (int k = 0; k < acts; k++) buf[mo + i * acts + k] = (byte)(m[k] ? 1 : 0);
        });
        if (withResult)
        {
            Buffer.BlockCopy(rew, 0, buf, ro, n * 4);
            for (int i = 0; i < n; i++) { buf[d0 + i] = (byte)(dn[i] ? 1 : 0); buf[w0 + i] = (byte)(wn[i] ? 1 : 0); }
        }
        shmView.Position = 0; shmView.Write(buf, 0, buf.Length);
        outp.Write((byte)1); outp.Flush();
    }

    static byte[] obsBytes = new byte[0];
    static void WriteBatch(BinaryWriter outp, Env[] envs, bool withResult, float[] rew = null, bool[] dn = null, bool[] wn = null)
    {
        if (shmView != null) { WriteShm(outp, envs, withResult, rew, dn, wn); return; }
        int n = envs.Length, size = ObsN;
        if (obsBytes.Length != n * size * 4) obsBytes = new byte[n * size * 4];
        for (int i = 0; i < n; i++) Buffer.BlockCopy(envs[i].Obs, 0, obsBytes, i * size * 4, size * 4);
        outp.Write(obsBytes);
        for (int i = 0; i < n; i++) foreach (var m in envs[i].Mask) outp.Write((byte)(m ? 1 : 0));
        if (withResult)
        {
            for (int i = 0; i < n; i++) outp.Write(rew[i]);
            for (int i = 0; i < n; i++) outp.Write((byte)(dn[i] ? 1 : 0));
            for (int i = 0; i < n; i++) outp.Write((byte)(wn[i] ? 1 : 0));
        }
        outp.Flush();
    }

    // ------------------------------------------------------------------ 상대 정책 파일

    static readonly Dictionary<string, NeuralPolicy> policies = new Dictionary<string, NeuralPolicy>();
    static readonly List<string> policyOrder = new List<string>();
    /// <summary>정책 파일을 읽는다(최근 12개만 기억). 못 읽으면 null → 그 판은 랭크AI 상대로 돌린다.</summary>
    static NeuralPolicy LoadPolicy(string path)
    {
        lock (policies)
        {
            NeuralPolicy p;
            if (policies.TryGetValue(path, out p)) return p;
            try { p = NeuralPolicy.Load(path); if (p.InputSize != ObsN || p.OutputSize != AiEncoder.Actions) return null; }
            catch (Exception) { return null; }
            policies[path] = p; policyOrder.Add(path);
            while (policyOrder.Count > 12) { policies.Remove(policyOrder[0]); policyOrder.RemoveAt(0); }
            return p;
        }
    }

    // 엔진 예외 기록: 프로세스별 파일(engine_errors_PID.log)에 같은 종류는 3번까지만 자세히(스택·시드·팀) 적는다. 게임 디버깅용.
    static readonly Dictionary<string, int> crashCount = new Dictionary<string, int>();
    static void LogCrash(Exception ex, int seed, List<RimketCreature> mine, List<RimketCreature> foe)
    {
        lock (crashCount)
        {
            try
            {
                string top = ex.StackTrace == null ? "" : ex.StackTrace.Split('\n')[0].Trim();
                string key = ex.GetType().Name + "|" + ex.Message + "|" + top;
                int n; crashCount.TryGetValue(key, out n); crashCount[key] = ++n;
                if (n > 3) return;
                Func<List<RimketCreature>, string> d = l => l == null ? "-" : string.Join(" / ", l.Select(c => c.SpeciesId + " [" + c.Ability + (string.IsNullOrEmpty(c.Item) ? "" : "," + c.Item) + "] " + string.Join(",", c.Moves.Select(m => m.Move.Id))));
                string path = Path.Combine(AppContext.BaseDirectory, "engine_errors_" + Environment.ProcessId + ".log");
                File.AppendAllText(path, "==== " + DateTime.Now + " (같은 종류 " + n + "번째)\n시드 " + seed + "\n내 팀: " + d(mine) + "\n상대 팀: " + d(foe) + "\n" + ex + "\n\n");
            }
            catch (Exception) { }
        }
    }
}
