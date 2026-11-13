using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rimketmon.Engine;

/// <summary>
/// 모의대결: '미리 계산하기'(몇 턴 앞을 시뮬레이션해 보고 고르는) 학습 AI vs 지금 학습 AI.
/// 사용: dotnet run -- --lookahead <ai_policy.bin> [판 수=200] [후보당 시뮬 수=8] [깊이(턴)=4]
/// - 양쪽 모두 같은 정책 파일. 한쪽만 고를 때 미리 계산한다. 판의 절반은 자리를 바꿔 진영 차이를 없앤다.
/// - 미리 계산할 때 '앞으로 나올 주사위'는 모른다: 지금까지의 행동을 같은 시드로 다시 재생해 현재 상태를 만든 뒤, 난수만 새것으로 바꿔 굴린다.
/// - 상대가 이번 턴에 무엇을 낼지는 모른다: 시뮬레이션 안에서 상대는 같은 정책으로 확률대로 고른다.
/// </summary>
internal static class Lookahead
{
    /// <summary>안쪽 난수를 바꿔 끼울 수 있는 난수(재생할 때는 원래 시드, 시뮬레이션할 때는 새 난수).</summary>
    sealed class SwitchRng : IRng
    {
        public IRng Inner;
        public SwitchRng(IRng inner) { Inner = inner; }
        public double NextDouble() { return Inner.NextDouble(); }
        public int Next(int a, int b) { return Inner.Next(a, b); }
    }

    sealed class Match
    {
        public int Seed; public EngineSettings S;
        public List<RimketCreature> Mine0, Foe0;      // 처음 상태(재생용 원본)
        public List<KeyValuePair<BattleAction?, BattleAction?>> Log = new List<KeyValuePair<BattleAction?, BattleAction?>>();

        public RimketBattle Build(out SwitchRng sr)
        {
            sr = new SwitchRng(new SystemRng(Seed));
            var b = RimketBattle.OnlineBattle(sr, S, "P", Mine0.Select(c => c.Clone()).ToList(), "F", Foe0.Select(c => c.Clone()).ToList());
            foreach (var kv in Log) b.OnlineAct(kv.Key, kv.Value);
            return b;
        }
    }

    sealed class Side
    {
        public AiEncoder Enc; public bool Foe;
        public Side(EngineSettings s, bool foe, List<RimketCreature> my6, List<RimketCreature> opp6, List<int> picks)
        {
            Foe = foe; Enc = new AiEncoder(s, foe); Enc.SetPreview(my6, opp6); Enc.SetPicks(picks);
        }
        public bool Needs(RimketBattle b)
        {
            if (b.IsOver) return false;
            bool ps = b.PendingSwitch, fs = b.PendingFoeSwitch;
            if (Foe) return !ps || fs;      // 내 쪽(플레이어)만 교체를 고르는 때에는 Foe는 낼 것이 없다
            return !fs || ps;
        }
    }

    static BattleAction? Policy(NeuralPolicy pol, Side sd, RimketBattle b, double temp, IRng rng)
    {
        var mask = sd.Enc.Mask(b); if (!mask.Any(x => x)) return Fallback(sd, b);
        int a = pol.Choose(sd.Enc.Encode(b), mask, temp, rng);
        var act = a >= 0 ? sd.Enc.ToAction(b, a) : null;
        return act ?? Fallback(sd, b);
    }

    static BattleAction? Fallback(Side sd, RimketBattle b)
    {
        var l = (sd.Foe ? b.FoeLegalActions() : b.LegalActions()).Where(x => x.Kind != ActionKind.Disconnect).ToList();
        return l.Count > 0 ? l[0] : (BattleAction?)null;
    }

    /// <summary>side 입장의 형세(0~1): 끝났으면 승패, 아니면 남은 체력 비율 차이.</summary>
    static double Value(RimketBattle b, bool foe)
    {
        if (b.IsOver)
        {
            if (b.Draw) return 0.5;
            bool pWin = b.Result.End == BattleEnd.Victory;
            return (foe ? !pWin : pWin) ? 1.0 : 0.0;
        }
        double me = 0, op = 0;
        foreach (var c in b.Player.Party) me += c.IsFainted ? 0 : (double)c.Hp / Math.Max(1, c.MaxHp);
        foreach (var c in b.Foe.Party) op += c.IsFainted ? 0 : (double)c.Hp / Math.Max(1, c.MaxHp);
        double v = 0.5 + 0.5 * (me - op) / 3.0;      // 플레이어 입장
        return foe ? 1.0 - v : v;
    }

    /// <summary>미리 계산하기: 고를 수 있는 행동마다 n번씩 depth턴 앞까지 굴려 평균 형세가 가장 좋은 것을 고른다.</summary>
    static BattleAction? Plan(NeuralPolicy pol, Match m, Side me, Side other, RimketBattle real, int n, int depth, Random r)
    {
        var mask = me.Enc.Mask(real);
        var cands = new List<int>(); for (int i = 0; i < mask.Length; i++) if (mask[i]) cands.Add(i);
        if (cands.Count == 0) return Fallback(me, real);
        if (cands.Count == 1) return me.Enc.ToAction(real, cands[0]);
        var logits = pol.Logits(me.Enc.Encode(real));
        double best = double.NegativeInfinity; int bestA = cands[0];
        foreach (int a in cands)
        {
            double sum = 0; int cnt = 0;
            for (int k = 0; k < n; k++)
            {
                SwitchRng sr; RimketBattle sim;
                try { sim = m.Build(out sr); } catch (Exception) { continue; }
                var simRng = new SystemRng(r.Next()); sr.Inner = simRng;
                try
                {
                    var mine = me.Enc.ToAction(sim, a); if (!mine.HasValue) break;
                    var opp = other.Needs(sim) ? Policy(pol, other, sim, 1.0, simRng) : null;      // 상대가 이번에 무엇을 낼지 모른다 → 정책 확률대로
                    if (me.Foe) sim.OnlineAct(opp, mine); else sim.OnlineAct(mine, opp);
                    int turns = 0, guard = 0;
                    int startTurn = sim.Turn;
                    while (!sim.IsOver && sim.Turn - startTurn < depth && guard++ < 40)
                    {
                        var pa = (me.Foe ? other : me).Needs(sim) ? Policy(pol, me.Foe ? other : me, sim, 0.3, simRng) : null;
                        var fa = (me.Foe ? me : other).Needs(sim) ? Policy(pol, me.Foe ? me : other, sim, 0.3, simRng) : null;
                        if (!pa.HasValue && !fa.HasValue) break;
                        if (!sim.OnlineAct(pa, fa)) break;
                        turns++;
                    }
                    sum += Value(sim, me.Foe); cnt++;
                }
                catch (Exception) { }
            }
            if (cnt == 0) continue;
            double score = sum / cnt + 0.002 * logits[a];      // 비기면 정책이 좋아하는 쪽
            if (score > best) { best = score; bestA = a; }
        }
        return me.Enc.ToAction(real, bestA) ?? Fallback(me, real);
    }

    // ------------------------------------------------------------------ 랭크AI 상대

    sealed class RMatch
    {
        public int Seed; public EngineSettings S;
        public List<RimketCreature> Mine0, Foe0;
        public List<BattleAction> Log = new List<BattleAction>();
        public RimketBattle Build(out SwitchRng sr)
        {
            sr = new SwitchRng(new SystemRng(Seed));
            var b = RimketBattle.RankedBattle(sr, S, "P", Mine0.Select(c => c.Clone()).ToList(), "F", Foe0.Select(c => c.Clone()).ToList());
            b.FoeAiMode = 0;      // 랭크AI
            foreach (var a in Log) b.Act(a);
            return b;
        }
    }

    /// <summary>랭크AI 상대 미리 계산: 시뮬레이션 안의 상대는 랭크AI가 아니라 우리 정책으로 흉내 낸다(실제 게임에서 사람 상대를 흉내 내는 것과 같은 조건 — 상대 알고리즘을 훔쳐보지 않는다).</summary>
    static BattleAction? PlanVsRanked(NeuralPolicy pol, RMatch m, Side me, Side foeModel, RimketBattle real, int n, int depth, Random r)
    {
        var mask = me.Enc.Mask(real);
        var cands = new List<int>(); for (int i = 0; i < mask.Length; i++) if (mask[i]) cands.Add(i);
        if (cands.Count == 0) return Fallback(me, real);
        if (cands.Count == 1) return me.Enc.ToAction(real, cands[0]);
        var logits = pol.Logits(me.Enc.Encode(real));
        double best = double.NegativeInfinity; int bestA = cands[0];
        foreach (int a in cands)
        {
            double sum = 0; int cnt = 0;
            for (int k = 0; k < n; k++)
            {
                SwitchRng sr; RimketBattle sim;
                try { sim = m.Build(out sr); } catch (Exception) { continue; }
                var simRng = new SystemRng(r.Next()); sr.Inner = simRng;
                bool first = true;
                sim.FoeBrain = bb => { var x = Policy(pol, foeModel, bb, first ? 1.0 : 0.3, simRng); first = false; return x; };
                try
                {
                    var mine = me.Enc.ToAction(sim, a); if (!mine.HasValue) break;
                    sim.Act(mine.Value);
                    int startTurn = sim.Turn, guard = 0;
                    while (!sim.IsOver && sim.Turn - startTurn < depth && guard++ < 40)
                    {
                        var pa = Policy(pol, me, sim, 0.3, simRng); if (!pa.HasValue) break;
                        sim.Act(pa.Value);
                    }
                    sum += Value(sim, false); cnt++;
                }
                catch (Exception) { }
            }
            if (cnt == 0) continue;
            double score = sum / cnt + 0.002 * logits[a];
            if (score > best) { best = score; bestA = a; }
        }
        return me.Enc.ToAction(real, bestA) ?? Fallback(me, real);
    }

    // ------------------------------------------------------------------ 개선판: 상대 예측 넓히기 + 형세 판단 개선

    /// <summary>
    /// 형세(플레이어 입장 0~1). 남은 체력·마릿수(60%) + 지금 나와 있는 둘의 맞대결(25%: 몇 번 때려야 쓰러뜨리나·누가 먼저인가)
    /// + 능력치 단계 차이(10%) + 상태이상(5%). 끝난 판은 승패 그대로.
    /// </summary>
    static double Value2(RimketBattle b, EngineSettings s)
    {
        if (b.IsOver) { if (b.Draw) return 0.5; return b.Result.End == BattleEnd.Victory ? 1.0 : 0.0; }
        double me = 0, op = 0;
        foreach (var c in b.Player.Party) me += c.IsFainted ? 0 : (double)c.Hp / Math.Max(1, c.MaxHp);
        foreach (var c in b.Foe.Party) op += c.IsFainted ? 0 : (double)c.Hp / Math.Max(1, c.MaxHp);
        double matter = (me - op) / 3.0, matchup = 0, stage = 0, status = 0;
        var pf = b.Player.Active; var ff = b.Foe.Active;
        if (pf != null && ff != null && !pf.Fainted && !ff.Fainted && !pf.IsPerson && !ff.IsPerson)
        {
            double myDeal = BestDeal(pf, ff, s), opDeal = BestDeal(ff, pf, s);
            double myTtk = myDeal > 0 ? Math.Ceiling(1.0 / Math.Min(1.0, myDeal)) : 9, opTtk = opDeal > 0 ? Math.Ceiling(1.0 / Math.Min(1.0, opDeal)) : 9;
            bool faster = pf.Speed > ff.Speed;
            double edge = opTtk - myTtk + (myTtk == opTtk ? (faster ? 0.5 : -0.5) : 0);
            matchup = Math.Max(-1.0, Math.Min(1.0, edge / 3.0));
            int sp = 0, so = 0; for (int i = Stat.Atk; i <= Stat.Spe; i++) { sp += pf.Stage[i]; so += ff.Stage[i]; }
            stage = Math.Max(-1.0, Math.Min(1.0, (sp - so) / 8.0));
            status = (ff.Status != StatusCondition.None ? 0.5 : 0) - (pf.Status != StatusCondition.None ? 0.5 : 0);
        }
        double v = 0.60 * matter + 0.25 * matchup + 0.10 * stage + 0.05 * status;
        return Math.Max(0.0, Math.Min(1.0, 0.5 + 0.5 * v));
    }

    /// <summary>a가 지금 d에게 한 번에 줄 수 있는 가장 큰 피해(d의 남은 체력 대비).</summary>
    static double BestDeal(Fighter a, Fighter d, EngineSettings s)
    {
        double best = 0, hp = Math.Max(1, d.C.Hp);
        foreach (var slot in a.C.Moves)
        {
            if (!slot.HasPP || !slot.Move.IsDamaging) continue;
            bool phys = slot.Move.Category == MoveCategory.Physical;
            double dmg = RankedAI.Damage(a.C, slot.Move, d.C, DamageCalc.StageMult(a.Stage[phys ? Stat.Atk : Stat.SpA]), DamageCalc.StageMult(d.Stage[phys ? Stat.Def : Stat.SpD]), s);
            best = Math.Max(best, dmg / hp);
        }
        return best;
    }

    static BattleAction? RandomLegalFoe(RimketBattle b, IRng rng)
    {
        var l = b.FoeLegalActions().Where(x => x.Kind != ActionKind.Disconnect).ToList();
        return l.Count > 0 ? l[rng.Next(0, l.Count)] : (BattleAction?)null;
    }

    /// <summary>
    /// 개선판 미리 계산. 시뮬레이션마다 상대가 어떻게 둘지 가정을 바꾼다(mix: 정책·규칙AI·무작위의 비중).
    /// 규칙AI 가정 = 엔진의 기존 상대 AI가 그대로 두게 둔다(FoeBrain 없음).
    /// </summary>
    static BattleAction? PlanVsRanked2(NeuralPolicy pol, RMatch m, Side me, Side foeModel, RimketBattle real, int n, int depth, Random r, double[] mix, EngineSettings s)
    {
        var mask = me.Enc.Mask(real);
        var cands = new List<int>(); for (int i = 0; i < mask.Length; i++) if (mask[i]) cands.Add(i);
        if (cands.Count == 0) return Fallback(me, real);
        if (cands.Count == 1) return me.Enc.ToAction(real, cands[0]);
        var logits = pol.Logits(me.Enc.Encode(real));
        double best = double.NegativeInfinity; int bestA = cands[0];
        double tot = mix.Sum();
        foreach (int a in cands)
        {
            double sum = 0; int cnt = 0;
            for (int k = 0; k < n; k++)
            {
                SwitchRng sr; RimketBattle sim;
                try { sim = m.Build(out sr); } catch (Exception) { continue; }
                var simRng = new SystemRng(r.Next()); sr.Inner = simRng;
                // 이번 시뮬레이션에서 상대가 어떤 식으로 둔다고 볼지: 시뮬 번호로 고르게 나눈다(후보끼리 같은 조합을 쓰게)
                double pick = (k + 0.5) / n * tot; int mode = 0; double acc = 0;
                for (int i = 0; i < mix.Length; i++) { acc += mix[i]; if (pick < acc) { mode = i; break; } }
                bool first = true;
                if (mode == 0) sim.FoeBrain = bb => { var x = Policy(pol, foeModel, bb, first ? 1.0 : 0.3, simRng); first = false; return x; };
                else if (mode == 2) sim.FoeBrain = bb => RandomLegalFoe(bb, simRng);
                // mode 1: 규칙AI(엔진 기본 상대 AI)
                try
                {
                    var mine = me.Enc.ToAction(sim, a); if (!mine.HasValue) break;
                    sim.Act(mine.Value);
                    int startTurn = sim.Turn, guard = 0;
                    while (!sim.IsOver && sim.Turn - startTurn < depth && guard++ < 40)
                    {
                        var pa = Policy(pol, me, sim, 0.3, simRng); if (!pa.HasValue) break;
                        sim.Act(pa.Value);
                    }
                    sum += Value2(sim, s); cnt++;
                }
                catch (Exception) { }
            }
            if (cnt == 0) continue;
            double score = sum / cnt + 0.002 * logits[a];
            if (score > best) { best = score; bestA = a; }
        }
        return me.Enc.ToAction(real, bestA) ?? Fallback(me, real);
    }

    /// <summary>개선판 두 가지를 랭크AI와 붙인다. 0: 예측=정책70·무작위30, 1: 예측=정책50·규칙AI30·무작위20. 둘 다 형세 판단 개선.</summary>
    public static bool OnlyA;
    public static int RunVsRanked2(string policyPath, int games, int n, int depth)
    {
        AiEncoder.FullInfo = true;
        var pol = NeuralPolicy.Load(policyPath); var s = new EngineSettings();
        var mixes = new[] { new[] { 0.7, 0.0, 0.3 }, new[] { 0.5, 0.3, 0.2 } };
        string[] names = { "개선 A(정책+무작위 예측)", "개선 B(정책+규칙+무작위 예측)" };
        Console.WriteLine("랭크AI 상대 / 팀 " + games + ", 후보당 시뮬 " + n + ", 깊이 " + depth + "턴");
        int[] wins = new int[2], played = new int[2]; int errors = 0, done = 0;
        var t0 = DateTime.Now; object lk = new object();
        Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, g =>
        {
            int seed = 50000 + g;
            for (int arm = 0; arm < (OnlyA ? 1 : 2); arm++)
            {
                try
                {
                    var rng = new SystemRng(seed);
                    string fmt = RankedPoolCatalog.Formats[seed % 6];
                    var p6 = RankedBuilder.Build(fmt, 6, RankedLadder.BattleLevel, rng, s); var f6 = RankedBuilder.Build(fmt, 6, RankedLadder.BattleLevel, rng, s);
                    var pp = RankedRules.PickForFoe(p6, f6, new SystemRng(seed * 7 + 1), s); var fp = RankedRules.PickForFoe(f6, p6, new SystemRng(seed * 7 + 2), s);
                    var mine = RankedRules.Brought(p6, pp, RankedLadder.BattleLevel); var foe = RankedRules.Brought(f6, fp, RankedLadder.BattleLevel);
                    foreach (var c in mine.Concat(foe)) c.FullHeal();
                    var m = new RMatch { Seed = seed * 31 + 5, S = s, Mine0 = mine.Select(c => c.Clone()).ToList(), Foe0 = foe.Select(c => c.Clone()).ToList() };
                    SwitchRng sr; var b = m.Build(out sr);
                    var P = new Side(s, false, p6, f6, pp); var Fm = new Side(s, true, f6, p6, fp);
                    var r = new Random(seed * 13); int guard = 0;
                    while (!b.IsOver && guard++ < 300)
                    {
                        var pa = PlanVsRanked2(pol, m, P, Fm, b, n, depth, r, mixes[arm], s);
                        if (!pa.HasValue) break;
                        b.Act(pa.Value); m.Log.Add(pa.Value);
                    }
                    bool won = b.IsOver && !b.Draw && b.Result.End == BattleEnd.Victory;
                    lock (lk) { played[arm]++; if (won) wins[arm]++; }
                }
                catch (Exception ex) { lock (lk) { errors++; if (errors <= 2) Console.WriteLine("오류: " + ex.Message); } }
            }
            lock (lk) { done++; if (done % 50 == 0) Console.WriteLine("  " + done + "팀: " + names[0] + " " + wins[0] + "승, " + names[1] + " " + wins[1] + "승  (" + (DateTime.Now - t0).TotalSeconds.ToString("0") + "초)"); }
        });
        for (int i = 0; i < (OnlyA ? 1 : 2); i++)
        {
            double p = played[i] > 0 ? (double)wins[i] / played[i] : 0, se = played[i] > 0 ? Math.Sqrt(p * (1 - p) / played[i]) : 0;
            Console.WriteLine(names[i] + ": " + wins[i] + "/" + played[i] + " = " + (p * 100).ToString("0.0") + "% (±" + (se * 196).ToString("0.0") + "%p)");
        }
        Console.WriteLine("오류 " + errors);
        return 0;
    }

    /// <summary>랭크AI 상대 승률: 같은 팀·같은 시드로 '지금 AI'와 '미리 계산 AI'를 각각 한 판씩.</summary>
    public static int RunVsRanked(string policyPath, int games, int n, int depth)
    {
        AiEncoder.FullInfo = true;
        var pol = NeuralPolicy.Load(policyPath); var s = new EngineSettings();
        Console.WriteLine("랭크AI 상대 / 판 " + games + "(팀마다 지금 AI 1판 + 미리 계산 AI 1판), 후보당 시뮬 " + n + ", 깊이 " + depth + "턴");
        int[] wins = new int[2], played = new int[2]; int bothDiff = 0, laOnly = 0, plainOnly = 0, errors = 0, done = 0;
        var t0 = DateTime.Now; object lk = new object();
        Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, g =>
        {
            int seed = 50000 + g; bool[] won = new bool[2]; bool ok = true;
            for (int arm = 0; arm < 2; arm++)
            {
                try
                {
                    var rng = new SystemRng(seed);
                    string fmt = RankedPoolCatalog.Formats[seed % 6];
                    var p6 = RankedBuilder.Build(fmt, 6, RankedLadder.BattleLevel, rng, s); var f6 = RankedBuilder.Build(fmt, 6, RankedLadder.BattleLevel, rng, s);
                    var pp = RankedRules.PickForFoe(p6, f6, new SystemRng(seed * 7 + 1), s); var fp = RankedRules.PickForFoe(f6, p6, new SystemRng(seed * 7 + 2), s);
                    var mine = RankedRules.Brought(p6, pp, RankedLadder.BattleLevel); var foe = RankedRules.Brought(f6, fp, RankedLadder.BattleLevel);
                    foreach (var c in mine.Concat(foe)) c.FullHeal();
                    var m = new RMatch { Seed = seed * 31 + 5, S = s, Mine0 = mine.Select(c => c.Clone()).ToList(), Foe0 = foe.Select(c => c.Clone()).ToList() };
                    SwitchRng sr; var b = m.Build(out sr);
                    var P = new Side(s, false, p6, f6, pp); var Fm = new Side(s, true, f6, p6, fp);
                    var r = new Random(seed * 13); int guard = 0;
                    while (!b.IsOver && guard++ < 300)
                    {
                        var pa = arm == 1 ? PlanVsRanked(pol, m, P, Fm, b, n, depth, r) : Policy(pol, P, b, 0.0, null);
                        if (!pa.HasValue) break;
                        b.Act(pa.Value); m.Log.Add(pa.Value);
                        if (g < 3 && arm == 1 && m.Log.Count % 3 == 0) { SwitchRng s2; var rb = m.Build(out s2); if (OnlineProtocol.StateHash(rb) != OnlineProtocol.StateHash(b)) lock (lk) Console.WriteLine("  재생 불일치! 팀 " + g); }
                    }
                    won[arm] = b.IsOver && !b.Draw && b.Result.End == BattleEnd.Victory;
                }
                catch (Exception ex) { ok = false; lock (lk) { errors++; if (errors <= 2) Console.WriteLine("오류: " + ex.Message); } }
            }
            lock (lk)
            {
                done++;
                if (ok) { for (int i = 0; i < 2; i++) { played[i]++; if (won[i]) wins[i]++; } if (won[1] && !won[0]) laOnly++; if (won[0] && !won[1]) plainOnly++; }
                if (done % 50 == 0) Console.WriteLine("  " + done + "팀: 지금 AI " + wins[0] + "승, 미리 계산 AI " + wins[1] + "승  (" + (DateTime.Now - t0).TotalSeconds.ToString("0") + "초)");
            }
        });
        for (int i = 0; i < 2; i++)
        {
            double p = played[i] > 0 ? (double)wins[i] / played[i] : 0, se = played[i] > 0 ? Math.Sqrt(p * (1 - p) / played[i]) : 0;
            Console.WriteLine((i == 0 ? "지금 AI     " : "미리 계산 AI") + ": " + wins[i] + "/" + played[i] + " = " + (p * 100).ToString("0.0") + "% (±" + (se * 196).ToString("0.0") + "%p)");
        }
        Console.WriteLine("같은 팀에서 미리 계산만 이긴 판 " + laOnly + ", 지금 AI만 이긴 판 " + plainOnly + ", 오류 " + errors);
        return 0;
    }

    public static int Run(string policyPath, int games, int n, int depth)
    {
        AiEncoder.FullInfo = true;
        var pol = NeuralPolicy.Load(policyPath); var s = new EngineSettings();
        Console.WriteLine("정책: 입력 " + pol.InputSize + ", 행동 " + pol.OutputSize + " / 판 " + games + ", 후보당 시뮬 " + n + ", 깊이 " + depth + "턴");
        int laWins = 0, plainWins = 0, draws = 0, errors = 0, done = 0; long decisions = 0;
        var t0 = DateTime.Now; object lk = new object();
        Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, g =>
        {
            int seed = 1000 + g / 2;      // 같은 팀·같은 시드로 자리만 바꿔 두 판씩
            bool laIsFoe = g % 2 == 1;
            try
            {
                var rng = new SystemRng(seed);
                string fmt = RankedPoolCatalog.Formats[seed % 6];
                var p6 = RankedBuilder.Build(fmt, 6, RankedLadder.BattleLevel, rng, s); var f6 = RankedBuilder.Build(fmt, 6, RankedLadder.BattleLevel, rng, s);
                var pp = RankedRules.PickForFoe(p6, f6, new SystemRng(seed * 7 + 1), s); var fp = RankedRules.PickForFoe(f6, p6, new SystemRng(seed * 7 + 2), s);
                var mine = RankedRules.Brought(p6, pp, RankedLadder.BattleLevel); var foe = RankedRules.Brought(f6, fp, RankedLadder.BattleLevel);
                foreach (var c in mine.Concat(foe)) c.FullHeal();
                var m = new Match { Seed = seed * 31 + 5, S = s, Mine0 = mine.Select(c => c.Clone()).ToList(), Foe0 = foe.Select(c => c.Clone()).ToList() };
                SwitchRng sr; var b = m.Build(out sr);
                var P = new Side(s, false, p6, f6, pp); var F = new Side(s, true, f6, p6, fp);
                var r = new Random(seed * 13 + (laIsFoe ? 1 : 0)); var polRng = new SystemRng(seed * 17 + 3);
                int guard = 0; long dec = 0;
                while (!b.IsOver && guard++ < 300)
                {
                    BattleAction? pa = null, fa = null;
                    if (P.Needs(b)) { pa = !laIsFoe ? Plan(pol, m, P, F, b, n, depth, r) : Policy(pol, P, b, 0.3, polRng); if (!laIsFoe) dec++; }
                    if (F.Needs(b)) { fa = laIsFoe ? Plan(pol, m, F, P, b, n, depth, r) : Policy(pol, F, b, 0.3, polRng); if (laIsFoe) dec++; }
                    if (!pa.HasValue && !fa.HasValue) break;
                    if (!b.OnlineAct(pa, fa)) break;
                    m.Log.Add(new KeyValuePair<BattleAction?, BattleAction?>(pa, fa));
                    if (g < 4 && m.Log.Count % 3 == 0)      // 재생 점검: 같은 시드로 다시 재생하면 지금과 똑같은 상태여야 한다
                    {
                        SwitchRng sr2; var rb = m.Build(out sr2);
                        if (OnlineProtocol.StateHash(rb) != OnlineProtocol.StateHash(b)) lock (lk) Console.WriteLine("  재생 불일치! 판 " + g + " 행동 " + m.Log.Count);
                    }
                }
                lock (lk)
                {
                    decisions += dec; done++;
                    if (!b.IsOver || b.Draw) draws++;
                    else { bool pWin = b.Result.End == BattleEnd.Victory; if (pWin != laIsFoe) laWins++; else plainWins++; }
                    if (done % 20 == 0) Console.WriteLine("  " + done + "판: 미리 계산 " + laWins + " / 지금 AI " + plainWins + " / 무·미완 " + draws + "  (" + (DateTime.Now - t0).TotalSeconds.ToString("0") + "초)");
                }
            }
            catch (Exception ex) { lock (lk) { errors++; done++; if (errors <= 2) Console.WriteLine("오류: " + ex.Message); } }
        });
        int decided = laWins + plainWins;
        double p = decided > 0 ? (double)laWins / decided : 0, se = decided > 0 ? Math.Sqrt(p * (1 - p) / decided) : 0;
        Console.WriteLine("결과: 미리 계산 AI " + laWins + "승 / 지금 AI " + plainWins + "승 / 무·미완 " + draws + " / 오류 " + errors);
        Console.WriteLine("미리 계산 AI 승률 " + (p * 100).ToString("0.0") + "% (±" + (se * 196).ToString("0.0") + "%p, 95%)  · 한 번 고르는 데 평균 " + ((DateTime.Now - t0).TotalMilliseconds * Environment.ProcessorCount / Math.Max(1, decisions)).ToString("0") + "ms(코어 1개 기준)");
        return 0;
    }
}
