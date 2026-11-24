using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 폼 팀 대응 학습(전문가 따라 하기)용 판 돌리기. 플레이어 쪽 = 폼 팀(1~3마리 메가·원시·거다이맥스·다이맥스, 규칙 AI가 조종),
    /// 상대 쪽 = 기본 랭크 팀(시험하는 AI). 판 g의 팀·시드는 g로만 정해진다(같은 g면 어느 AI든 같은 판).
    /// - Bench: 상대 쪽 승률(rule / learned / look).
    /// - Collect: 상대 쪽은 미리 계산 AI가 두고, 매 결정의 관측(폼 입력 포함)·마스크·고른 행동을 기록한다.
    /// </summary>
    public static class FormBench
    {
        public sealed class Game { public List<RimketCreature> Mine, Foe; public int Seed, Forms; }

        public static Game Make(int g, EngineSettings s)
        {
            var rng = new SystemRng(50000 + g); string fmt = RankedPoolCatalog.Formats[g % 6];
            var p6 = RankedBuilder.Build(fmt, 6, RankedLadder.BattleLevel, rng, s); var f6 = RankedBuilder.Build(fmt, 6, RankedLadder.BattleLevel, rng, s);
            var mine = RankedRules.Brought(p6, new List<int> { 0, 1, 2 }, RankedLadder.BattleLevel); var foe = RankedRules.Brought(f6, new List<int> { 0, 1, 2 }, RankedLadder.BattleLevel);
            int forms = 0;
            var can = mine.Where(c => FormChange.Options(c).Count > 0 || FormChange.CanDynamax(c)).ToList();
            if (can.Count > 0)
            {
                int k = rng.Next(1, can.Count + 1);
                foreach (var t in can.OrderBy(_ => rng.Next(0, 1000)).Take(k))
                {
                    var o = FormChange.Options(t); bool dm = FormChange.CanDynamax(t); int pk = rng.Next(0, o.Count + (dm ? 1 : 0));
                    if (pk < o.Count) FormChange.Apply(t, o[pk]); else { t.Dynamax = true; t.RecalculateStats(); }
                    forms++;
                }
            }
            foreach (var c in mine.Concat(foe)) c.FullHeal();
            return new Game { Mine = mine, Foe = foe, Seed = 7000 + g, Forms = forms };
        }

        /// <summary>플레이어 쪽(폼 팀) 조종: 랭크 규칙 AI를 거울로 쓴다. eps 확률로 무작위(자료 다양성).</summary>
        public static BattleAction PlayerMove(RimketBattle b, EngineSettings s, IRng pr, ref int lastSw, ref int sws, double eps)
        {
            var l = b.LegalActions().Where(x => x.Kind != ActionKind.Disconnect && x.Kind != ActionKind.UseItem).ToList();
            if (l.Count == 0) return new BattleAction(ActionKind.Struggle);
            if (eps > 0 && pr.NextDouble() < eps) return l[pr.Next(0, l.Count)];
            var me = b.Player.Active; var tg = b.Foe.Active;
            if (l.All(x => x.Kind == ActionKind.Switch))
            {
                if (tg == null) return l[0];
                return l.OrderByDescending(x => b.Player.Party[x.Index].Moves.Where(ms => ms.Move.IsDamaging).Select(ms => RankedAI.Damage(b.Player.Party[x.Index], ms.Move, tg.C, 1, 1, s)).DefaultIfEmpty(0).Max()).First();
            }
            if (me == null || tg == null || me.Fainted) return l[0];
            int sw = RankedAI.ChooseSwitch(me, tg, b.Player.Party, s, pr, lastSw == b.Turn - 1, sws);
            if (sw >= 0 && l.Any(x => x.Kind == ActionKind.Switch && x.Index == sw)) { lastSw = b.Turn; sws++; return new BattleAction(ActionKind.Switch, sw); }
            var ch = RankedAI.ChooseMove(me, tg, s, pr, m => l.Any(x => x.Kind == ActionKind.UseMove && me.C.Moves[x.Index].Move == m), m => 0.0);
            int idx = ch != null && ch.Slot != null ? me.C.Moves.IndexOf(ch.Slot) : -1;
            foreach (var x in l) if (x.Kind == ActionKind.UseMove && x.Index == idx) return x;
            foreach (var x in l) if (x.Kind != ActionKind.Switch) return x;
            return l[0];
        }

        /// <summary>한 판. 상대 쪽이 이기면 true. rec가 있으면 상대 쪽 결정마다 (관측, 마스크, 행동)을 넘긴다.</summary>
        public static bool? Play(int g, string mode, NeuralPolicy pol, EngineSettings s, int sims, int depth, double eps, Action<float[], bool[], int> rec)
        {
            var gm = Make(g, s);
            var m0 = gm.Mine.Select(c => c.Clone()).ToList(); var f0 = gm.Foe.Select(c => c.Clone()).ToList();
            var b = RimketBattle.RankedBattle(new SystemRng(gm.Seed), s, "P", gm.Mine, "F", gm.Foe);
            Func<RimketBattle, BattleAction?> brain = null;
            if (mode == "learned") brain = new LearnedFoe(pol, s, new SystemRng(gm.Seed + 1)) { Temperature = 0 }.Decide;
            else if (mode == "look") { var la = new LookaheadFoe(pol, s, gm.Seed, m0, f0, null, gm.Seed + 2) { Sims = sims, Depth = depth, WaitMs = 600000 }; la.Attach(b); brain = b.FoeBrain; }
            AiEncoder recEnc = null;
            if (rec != null)
            {
                recEnc = new AiEncoder(s, true) { Window = true, FormObs = true };
                recEnc.SetPreview(b.Foe.Party, b.Player.Party);
                var picks = new List<int>(); for (int i = 0; i < b.Foe.Party.Count && i < AiEncoder.MaxMons; i++) picks.Add(i);
                recEnc.SetPicks(picks);
            }
            if (brain != null)
                b.FoeBrain = bb =>
                {
                    var act = brain(bb);
                    if (rec != null && act.HasValue)
                    {
                        var mask = recEnc.Mask(bb); int ai = -1;
                        for (int a = 0; a < AiEncoder.Actions && ai < 0; a++) if (mask[a]) { var t = recEnc.ToAction(bb, a); if (t.HasValue && Same(t.Value, act.Value)) ai = a; }
                        if (ai >= 0) rec(recEnc.Encode(bb), mask, ai);
                    }
                    return act;
                };
            var pr = new SystemRng(gm.Seed + 3); int guard = 0, lastSw = -9, sws = 0;
            while (!b.IsOver && guard++ < 300)
            {
                var act = PlayerMove(b, s, pr, ref lastSw, ref sws, eps);
                if (!b.Act(act))
                {
                    var l = b.LegalActions().Where(x => x.Kind != ActionKind.Disconnect && x.Kind != ActionKind.UseItem).ToList();
                    if (l.Count == 0 || !b.Act(l[pr.Next(0, l.Count)])) break;
                }
            }
            if (!b.IsOver) return null;
            return b.Player.Party.All(c => c.Hp <= 0);
        }

        // ------------------------------------------------------------------ 선출 효과 측정
        /// <summary>양쪽 6마리. 플레이어 쪽 앞 3마리(낼 3마리) 중 1~3마리가 폼(상대 AI는 프리뷰로 6마리 전부·폼을 본다).</summary>
        public static void MakeSix(int g, EngineSettings s, out List<RimketCreature> p6, out List<RimketCreature> f6)
        {
            var rng = new SystemRng(50000 + g); string fmt = RankedPoolCatalog.Formats[g % 6];
            p6 = RankedBuilder.Build(fmt, 6, RankedLadder.BattleLevel, rng, s); f6 = RankedBuilder.Build(fmt, 6, RankedLadder.BattleLevel, rng, s);
            var can = p6.Take(3).Where(c => FormChange.Options(c).Count > 0 || FormChange.CanDynamax(c)).ToList();
            if (can.Count > 0)
            {
                int k = rng.Next(1, can.Count + 1);
                foreach (var t in can.OrderBy(_ => rng.Next(0, 1000)).Take(k))
                {
                    var o = FormChange.Options(t); bool dm = FormChange.CanDynamax(t); int pk = rng.Next(0, o.Count + (dm ? 1 : 0));
                    if (pk < o.Count) FormChange.Apply(t, o[pk]); else { t.Dynamax = true; t.RecalculateStats(); }
                }
            }
        }

        /// <summary>상대 AI 선출. fixed=앞 3마리, rule=모드의 랭크 선출(PickForFoe), policy=학습 AI 선출(온도 0).</summary>
        public static List<int> FoePick(string how, List<RimketCreature> f6, List<RimketCreature> p6, NeuralPolicy pol, EngineSettings s, int seed)
        {
            if (how == "rule") return RankedRules.PickForFoe(f6, p6, new SystemRng(seed + 11), s);
            if (how == "policy")
            {
                var enc = new AiEncoder(s, true) { FormObs = LearnedFoe.IsFormPolicy(pol) }; enc.SetPreview(f6, p6);
                var picked = new List<int>(); var r = new SystemRng(seed + 12);
                for (int k = 0; k < 3; k++) { int a = pol.Choose(enc.EncodePick(picked), enc.PickMask(picked), 0, r); if (a < 0 || picked.Contains(a)) break; picked.Add(a); }
                if (picked.Count == 3) return picked;
            }
            return new List<int> { 0, 1, 2 };
        }

        /// <summary>정한 선출로 한 판(상대 쪽 = 학습 AI 또는 미리 계산 AI). 상대 쪽이 이기면 true.</summary>
        public static bool? PlayPicked(int g, List<int> foePicks, string mode, NeuralPolicy pol, EngineSettings s, int sims, int depth)
        {
            List<RimketCreature> p6, f6; MakeSix(g, s, out p6, out f6);
            var mine = RankedRules.Brought(p6, new List<int> { 0, 1, 2 }, RankedLadder.BattleLevel); var foe = RankedRules.Brought(f6, foePicks, RankedLadder.BattleLevel);
            foreach (var c in mine.Concat(foe)) c.FullHeal();
            int seed = 7000 + g;
            var m0 = mine.Select(c => c.Clone()).ToList(); var f0 = foe.Select(c => c.Clone()).ToList();
            var b = RimketBattle.RankedBattle(new SystemRng(seed), s, "P", mine, "F", foe);
            if (mode == "learned") b.FoeBrain = new LearnedFoe(pol, s, new SystemRng(seed + 1)) { Temperature = 0 }.Decide;
            else if (mode == "look") new LookaheadFoe(pol, s, seed, m0, f0, null, seed + 2) { Sims = sims, Depth = depth, WaitMs = 600000 }.Attach(b);
            var pr = new SystemRng(seed + 3); int guard = 0, lastSw = -9, sws = 0;
            while (!b.IsOver && guard++ < 300)
            {
                var act = PlayerMove(b, s, pr, ref lastSw, ref sws, 0);
                if (!b.Act(act))
                {
                    var l = b.LegalActions().Where(x => x.Kind != ActionKind.Disconnect && x.Kind != ActionKind.UseItem).ToList();
                    if (l.Count == 0 || !b.Act(l[pr.Next(0, l.Count)])) break;
                }
            }
            if (!b.IsOver) return null;
            return b.Player.Party.All(c => c.Hp <= 0);
        }

        /// <summary>선출 방식별 승률. how = fixed / rule / policy / best(20가지 조합 중 이긴 게 하나라도 있으면 승 = 선출로 얻을 수 있는 한계).</summary>
        public static double BenchPick(string how, string mode, NeuralPolicy pol, EngineSettings s, int from, int n, int sims, int depth, int threads, Action<string> report)
        {
            int win = 0, done = 0;
            var combos = new List<List<int>>();
            for (int a = 0; a < 6; a++) for (int b2 = 0; b2 < 6; b2++) for (int c = 0; c < 6; c++) if (a < b2 && b2 < c) combos.Add(new List<int> { a, b2, c });
            Parallel.For(from, from + n, new ParallelOptions { MaxDegreeOfParallelism = threads }, g =>
            {
                bool ok = false;
                try
                {
                    if (how == "best") { foreach (var cb in combos) if (PlayPicked(g, cb, mode, pol, s, sims, depth) == true) { ok = true; break; } }
                    else
                    {
                        List<RimketCreature> p6, f6; MakeSix(g, s, out p6, out f6);
                        ok = PlayPicked(g, FoePick(how, f6, p6, pol, s, 7000 + g), mode, pol, s, sims, depth) == true;
                    }
                }
                catch (Exception) { ok = false; }
                if (ok) Interlocked.Increment(ref win);
                int d = Interlocked.Increment(ref done);
                if (report != null && d % 100 == 0) report(how + " " + d + "/" + n + " win " + win);
            });
            return win / (double)n;
        }

        static bool Same(BattleAction x, BattleAction y) { return x.Kind == y.Kind && (x.Kind == ActionKind.Struggle || x.Kind == ActionKind.BasicAttack || x.Index == y.Index); }

        /// <summary>판 [from, from+n)을 threads개로 동시에 돌려 상대 쪽 승률. 진행을 report로 알린다.</summary>
        public static double Bench(string mode, NeuralPolicy pol, EngineSettings s, int from, int n, int sims, int depth, int threads, Action<string> report)
        {
            int win = 0, done = 0;
            Parallel.For(from, from + n, new ParallelOptions { MaxDegreeOfParallelism = threads }, g =>
            {
                bool? r; try { r = Play(g, mode, pol, s, sims, depth, 0, null); } catch (Exception) { r = null; }
                if (r == true) Interlocked.Increment(ref win);
                int d = Interlocked.Increment(ref done);
                if (report != null && d % 100 == 0) report(mode + " " + d + "/" + n + " 승 " + win);
            });
            return win / (double)n;
        }

        /// <summary>미리 계산 AI의 결정을 기록한다. 형식(리틀엔디언): 레코드마다 [uint16 행동, uint16 마스크 비트, int32 0이 아닌 칸 수 k, k × (int32 칸, float32 값)].</summary>
        public static int Collect(NeuralPolicy pol, EngineSettings s, int from, int n, int sims, int depth, double eps, int threads, string path, Action<string> report)
        {
            int recs = 0, done = 0; var lk = new object();
            using (var f = new BinaryWriter(File.Create(path)))
            {
                Parallel.For(from, from + n, new ParallelOptions { MaxDegreeOfParallelism = threads }, g =>
                {
                    var buf = new List<Tuple<float[], bool[], int>>();
                    try { Play(g, "look", pol, s, sims, depth, eps, (o, m, a) => buf.Add(Tuple.Create(o, m, a))); } catch (Exception) { buf.Clear(); }
                    lock (lk)
                    {
                        foreach (var t in buf)
                        {
                            int mb = 0; for (int i = 0; i < t.Item2.Length; i++) if (t.Item2[i]) mb |= 1 << i;
                            f.Write((ushort)t.Item3); f.Write((ushort)mb);
                            var o = t.Item1; int k = 0; for (int i = 0; i < o.Length; i++) if (o[i] != 0f) k++;
                            f.Write(k); for (int i = 0; i < o.Length; i++) if (o[i] != 0f) { f.Write(i); f.Write(o[i]); }
                            recs++;
                        }
                        done++;
                        if (report != null && done % 100 == 0) report("수집 " + done + "/" + n + "판, 결정 " + recs + "개");
                    }
                });
            }
            return recs;
        }
    }
}
