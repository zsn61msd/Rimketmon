using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 미리 계산하는 학습 AI(개선 A): 상대(Foe) 쪽이 고를 수 있는 행동마다 몇 턴 앞(Depth)을 여러 번(Sims) 굴려 보고 형세가 가장 좋은 행동을 고른다.
    /// - 앞으로 나올 주사위는 모른다: 처음 파티·시드·지금까지의 행동으로 지금 상태를 다시 만든 뒤(재생), 난수만 새것으로 바꿔 굴린다.
    /// - 플레이어가 어떻게 둘지는 모른다: 시뮬레이션마다 '같은 정책으로 둔다'(70%) / '아무거나 둔다'(30%)로 가정을 나눈다.
    /// - 형세: 남은 체력·마릿수 60% + 맞대결 25% + 능력치 단계 10% + 상태이상 5%.
    /// - 플레이어가 고민하는 동안(행동이 들어올 때마다) 다음 결정을 백그라운드에서 미리 계산해 둔다.
    /// 쓰려면: 전투를 seed로 만든 SystemRng로 만들고, Attach로 FoeBrain과 행동 기록을 연결한다.
    /// 재생이 어긋날 수 있는 일(전투 중 아이템 사용 등)이 생기면 그 판은 그냥 정책으로 둔다.
    /// </summary>
    public sealed class LookaheadFoe
    {
        public int Depth = 4, Sims = 8;
        public double[] Mix = { 0.7, 0.3 };      // 플레이어 가정: 정책 / 무작위
        public double Temperature = 0.3;
        /// <summary>미리 계산이 아직 안 끝났으면 이만큼(ms)까지 기다린다. 넘으면 정책으로 바로 고른다.</summary>
        public int WaitMs = 4000;
        public int Planned, Fallbacks;
        /// <summary>지금까지 상대가 낸 결정(재생 점검용 복사본).</summary>
        public List<BattleAction?> FoeDecisions { get { lock (lk) return foeLog.ToList(); } }

        readonly NeuralPolicy pol; readonly EngineSettings s; readonly int seed;
        readonly List<RimketCreature> mine0, foe0;
        readonly Action<RimketBattle> configure;
        readonly List<BattleAction> playerLog = new List<BattleAction>();
        readonly List<BattleAction?> foeLog = new List<BattleAction?>();
        readonly object lk = new object();
        readonly LearnedFoe plain;
        bool broken;
        Task<BattleAction?> pending; int pendingP = -1, pendingF = -1;
        CancellationTokenSource cts;
        readonly Random seeds;

        /// <param name="configure">재생·시뮬레이션용 전투에 실제 전투와 같은 설정(실수율·항복 등)을 넣는다.</param>
        public LookaheadFoe(NeuralPolicy pol, EngineSettings s, int seed, IList<RimketCreature> playerParty, IList<RimketCreature> foeParty, Action<RimketBattle> configure, int planSeed)
        {
            this.pol = pol; this.s = s; this.seed = seed; this.configure = configure;
            mine0 = playerParty.Select(c => c.Clone()).ToList(); foe0 = foeParty.Select(c => c.Clone()).ToList();
            plain = new LearnedFoe(pol, s, new SystemRng(planSeed ^ 0x5A5A)) { Temperature = Temperature };
            seeds = new Random(planSeed);
        }

        /// <summary>전투에 붙인다: FoeBrain, 행동 기록, 행동 뒤 미리 계산.</summary>
        public void Attach(RimketBattle b)
        {
            b.FoeBrain = Decide;
            b.ActRecorder = a => { lock (lk) playerLog.Add(a); };
            b.Unreplayable = () => { broken = true; };
            b.AfterAct = Prepare;
            Prepare(b);
        }

        /// <summary>재생용 전투(처음 상태 + 기록된 행동). foeLog를 다 쓰면 then이 상대를 조종한다.</summary>
        RimketBattle Replay(List<BattleAction> pl, List<BattleAction?> fl, out SwitchRng sr, Func<RimketBattle, BattleAction?> then)
        {
            sr = new SwitchRng(new SystemRng(seed));
            var b = RimketBattle.RankedBattle(sr, s, "P", mine0.Select(c => c.Clone()).ToList(), "F", foe0.Select(c => c.Clone()).ToList());
            configure?.Invoke(b);
            int fi = 0;
            b.FoeBrain = bb => fi < fl.Count ? fl[fi++] : then?.Invoke(bb);
            foreach (var a in pl) b.Act(a);
            return b;
        }

        /// <summary>지금 상태에서 다음 결정을 백그라운드로 미리 계산한다(이미 하고 있으면 그대로).</summary>
        public void Prepare(RimketBattle real)
        {
            if (broken || real == null || real.IsOver) return;
            List<BattleAction> pl; List<BattleAction?> fl;
            lock (lk)
            {
                if (pending != null && pendingP == playerLog.Count && pendingF == foeLog.Count) return;
                pl = playerLog.ToList(); fl = foeLog.ToList();
                cts?.Cancel(); cts = new CancellationTokenSource();
                pendingP = pl.Count; pendingF = fl.Count;
                int rs = seeds.Next(); var tok = cts.Token;
                pending = Task.Run(() => Plan(pl, fl, new Random(rs), tok));
            }
        }

        /// <summary>FoeBrain: 미리 계산한 답을 쓴다(아직이면 조금 기다리고, 그래도 없으면 정책).</summary>
        public BattleAction? Decide(RimketBattle real)
        {
            BattleAction? act = null;
            if (!broken)
            {
                Task<BattleAction?> t = null;
                // Act 안에서 불린다: 이번 행동은 이미 playerLog에 들어가 있으므로, 그 전 상태(playerLog.Count - 1)에서 계산한 답이 맞다
                lock (lk) { if (pending != null && pendingP == playerLog.Count - 1 && pendingF == foeLog.Count) t = pending; }
                if (t != null)
                {
                    try { if (t.Wait(WaitMs)) act = t.Result; } catch (Exception) { act = null; }
                }
                if (act.HasValue) Planned++;
            }
            if (!act.HasValue) { Fallbacks++; act = plain.Decide(real); }
            lock (lk) foeLog.Add(act);
            return act;
        }

        /// <summary>계획: 기록된 상태에서 상대 쪽 후보 행동마다 Sims번 Depth턴을 굴려 형세 평균이 가장 높은 것.</summary>
        BattleAction? Plan(List<BattleAction> pl, List<BattleAction?> fl, Random r, CancellationToken tok)
        {
            try
            {
                SwitchRng sr0;
                // 지금 상태: 플레이어의 다음 행동은 모르므로, 지금 상태의 후보는 '다음 FoeBrain 호출'에서 낼 행동이다
                var now = Replay(pl, fl, out sr0, null);
                if (now.IsOver) return null;
                var me = new AiEncoder(s, true) { Window = true };
                me.SetPreview(now.Foe.Party, now.Player.Party); me.SetPicks(Enumerable.Range(0, Math.Min(now.Foe.Party.Count, AiEncoder.MaxMons)).ToList());
                var mask = me.Mask(now);
                var cands = new List<int>(); for (int i = 0; i < mask.Length; i++) if (mask[i]) cands.Add(i);
                if (cands.Count == 0) return null;
                if (cands.Count == 1) return me.ToAction(now, cands[0]);
                var logits = pol.Logits(me.Encode(now));
                double best = double.NegativeInfinity; int bestA = cands[0];
                double tot = Mix.Sum();
                foreach (int a in cands)
                {
                    double sum = 0; int cnt = 0;
                    for (int k = 0; k < Sims; k++)
                    {
                        if (tok.IsCancellationRequested) return null;
                        double pick = (k + 0.5) / Sims * tot; int mode = Mix.Length - 1; double acc = 0;
                        for (int i = 0; i < Mix.Length; i++) { acc += Mix[i]; if (pick < acc) { mode = i; break; } }
                        var simRng = new SystemRng(r.Next());
                        var foeEnc = new AiEncoder(s, true) { Window = true };
                        var plEnc = new AiEncoder(s, false) { Window = true };
                        bool firstFoe = true, firstPl = true;
                        SwitchRng sr; RimketBattle sim;
                        try
                        {
                            sim = Replay(pl, fl, out sr, bb =>
                            {
                                if (firstFoe) { firstFoe = false; return foeEnc.ToAction(bb, a); }
                                return PolicyAct(foeEnc, bb, Temperature, simRng, true);
                            });
                        }
                        catch (Exception) { continue; }
                        sr.Inner = simRng;
                        foeEnc.SetPreview(sim.Foe.Party, sim.Player.Party); foeEnc.SetPicks(Enumerable.Range(0, Math.Min(sim.Foe.Party.Count, AiEncoder.MaxMons)).ToList());
                        plEnc.SetPreview(sim.Player.Party, sim.Foe.Party); plEnc.SetPicks(Enumerable.Range(0, Math.Min(sim.Player.Party.Count, AiEncoder.MaxMons)).ToList());
                        try
                        {
                            int startTurn = sim.Turn, guard = 0;
                            while (!sim.IsOver && (sim.Turn - startTurn < Depth || firstFoe) && guard++ < 40)
                            {
                                BattleAction? pa = mode == 0 ? PolicyAct(plEnc, sim, firstPl ? 1.0 : Temperature, simRng, false) : RandomLegal(sim, simRng);
                                firstPl = false;
                                if (!pa.HasValue || !sim.Act(pa.Value)) break;
                            }
                            if (firstFoe) continue;      // 이 후보가 한 번도 쓰이지 않았다
                            sum += 1.0 - Value2(sim); cnt++;
                        }
                        catch (Exception) { }
                    }
                    if (cnt == 0) continue;
                    double score = sum / cnt + 0.002 * logits[a];
                    if (score > best) { best = score; bestA = a; }
                }
                return me.ToAction(now, bestA);
            }
            catch (Exception) { return null; }
        }

        BattleAction? PolicyAct(AiEncoder enc, RimketBattle b, double temp, IRng rng, bool foe)
        {
            var mask = enc.Mask(b);
            if (!mask.Any(x => x)) return foe ? null : RandomLegal(b, rng);
            int a = pol.Choose(enc.Encode(b), mask, temp, rng);
            var act = a >= 0 ? enc.ToAction(b, a) : null;
            if (act.HasValue || foe) return act;
            return RandomLegal(b, rng);
        }

        static BattleAction? RandomLegal(RimketBattle b, IRng rng)
        {
            var l = b.LegalActions().Where(x => x.Kind != ActionKind.Disconnect && x.Kind != ActionKind.UseItem).ToList();
            return l.Count > 0 ? l[rng.Next(0, l.Count)] : (BattleAction?)null;
        }

        /// <summary>형세(플레이어 입장 0~1).</summary>
        double Value2(RimketBattle b)
        {
            if (b.IsOver) { if (b.Draw) return 0.5; return b.Result.End == BattleEnd.Victory ? 1.0 : 0.0; }
            double me = 0, op = 0;
            foreach (var c in b.Player.Party) me += c.IsFainted ? 0 : (double)c.Hp / Math.Max(1, c.MaxHp);
            foreach (var c in b.Foe.Party) op += c.IsFainted ? 0 : (double)c.Hp / Math.Max(1, c.MaxHp);
            double matter = (me - op) / 3.0, matchup = 0, stage = 0, status = 0;
            var pf = b.Player.Active; var ff = b.Foe.Active;
            if (pf != null && ff != null && !pf.Fainted && !ff.Fainted && !pf.IsPerson && !ff.IsPerson)
            {
                double myDeal = BestDeal(pf, ff), opDeal = BestDeal(ff, pf);
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

        double BestDeal(Fighter a, Fighter d)
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
    }

    /// <summary>안쪽 난수를 바꿔 끼울 수 있는 난수(재생할 때는 원래 시드, 시뮬레이션할 때는 새 난수).</summary>
    public sealed class SwitchRng : IRng
    {
        public IRng Inner;
        public SwitchRng(IRng inner) { Inner = inner; }
        public double NextDouble() { return Inner.NextDouble(); }
        public int Next(int a, int b) { return Inner.Next(a, b); }
    }
}
