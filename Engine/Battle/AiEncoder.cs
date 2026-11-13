using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 강화학습/신경망 AI용 관측 인코더(플레이어 시점, 단식 3마리). 종족 이름이 아니라 능력치·타입·기술 특성만 숫자로 바꾼다.
    /// 그래서 처음 보는 림켓몬(?티어 포함)도 같은 방식으로 다룬다. 상대에게 아직 공개되지 않은 정보(안 나온 상대 림켓몬, 안 쓴 기술)는 가린다.
    /// 행동: 0~3 = 내 기술 칸, 4~(3+MaxBench) = 교체(벤치를 파티 순서대로 센 번호).
    /// </summary>
    public sealed class AiEncoder
    {
        public const int MaxMons = 3, MaxBench = MaxMons - 1, Actions = 4 + MaxBench;
        const int Types = 18, EffectFlags = 12;
        public const int MoveSize = 5 + 3 + Types + 1 + 1 + EffectFlags + 1;       // 41
        public const int BaseMonSize = 5 + 6 + Types * 2 + 6 + 7 + 4 * MoveSize;   // 5+6+36+6+7+164 = 224
        // 특성·지닌 도구: 랭크 파티풀에 나오는 것들을 정렬해 번호를 매기고 원-핫으로 넣는다(풀에 없는 것은 0 = 알 수 없음).
        static readonly Dictionary<string, int> abilityIdx = BuildVocab(true), itemIdx = BuildVocab(false);
        public static readonly int AbilityCount = abilityIdx.Count, ItemCount = itemIdx.Count;
        public static readonly int MonSize = BaseMonSize + AbilityCount + ItemCount;
        public const int FieldSize = 9 + 5 + 2 + 8 * 2 + 1;                        // 33
        /// <summary>전투 부분의 크기(내 3 + 상대 3 + 필드).</summary>
        public static readonly int BattleSize = MonSize * MaxMons * 2 + FieldSize;
        // 선출(팀 프리뷰) 부분: [선출 단계인가 1] + 내 6마리 × (전체 정보 + 골랐나 1 + 고른 순서 3) + 상대 6마리 × (있음 1 + 타입 36 + 최대HP 1)
        public const int TeamSize = 6, OwnPickExtra = 4, FoePreviewSize = 1 + Types * 2 + 1;
        public static readonly int OwnPreviewSize = MonSize + OwnPickExtra;
        public static readonly int PreviewSize = 1 + TeamSize * OwnPreviewSize + TeamSize * FoePreviewSize;
        public static readonly int Size = BattleSize + PreviewSize;

        static Dictionary<string, int> BuildVocab(bool ability)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var f in RankedPoolCatalog.Formats) foreach (var p in RankedPoolCatalog.Pool(f))
                foreach (var kv in ability ? p.Abilities : p.Items) if (!string.IsNullOrEmpty(kv.Key)) set.Add(kv.Key);
            var d = new Dictionary<string, int>(); foreach (var k in set) d[k] = d.Count;
            return d;
        }

        readonly EngineSettings s;
        /// <summary>true면 화면에 안 보이는 상대 정보(정확한 공·방·특공·특방·속도)를 가린다. 화면에 보이는 것(타입·레벨·HP·상태·쓴 기술)만 쓴다. 학습과 실제 사용에서 같은 값이어야 한다.</summary>
        public bool HideFoeStats = true;
        /// <summary>
        /// true면 상대의 능력치·기술·특성·도구를 전부 본다. 랭크AI(규칙 AI)가 원래 이 정보를 다 보고 판단하므로,
        /// "같은 조건"으로 겨루는 모델을 학습할 때 켠다(학습 환경에서 환경 변수 RIMKET_FULLINFO=1). 기본은 화면에 보이는 것만.
        /// </summary>
        public static bool FullInfo;
        bool Hidden { get { return HideFoeStats && !FullInfo; } }
        float typical = 100f;      // 가린 상대 능력치를 대신하는 '이 정도겠지' 값(내 파티 능력치 평균)
        readonly HashSet<RimketCreature> foeSeen = new HashSet<RimketCreature>();

        /// <summary>true면 엔진의 Foe 쪽 입장에서 본다(자기학습의 상대, 모드에서 랭크 상대 AI로 쓸 때). 관측·마스크·행동이 모두 Foe 기준이 된다.</summary>
        public bool AsFoe;
        /// <summary>파티가 3마리보다 많은 전투(트레이너전)용: 벤치를 '싸울 수 있는 것 먼저'로 세어, 정책이 보는 3칸에 쓸 수 있는 림켓몬이 들어오게 한다. 학습(3대3)에서는 끈다.</summary>
        public bool Window;
        BattleSide Me(RimketBattle b) { return AsFoe ? b.Foe : b.Player; }
        BattleSide Opp(RimketBattle b) { return AsFoe ? b.Player : b.Foe; }
        IList<BattleAction> Legal(RimketBattle b) { return AsFoe ? b.FoeLegalActions() : b.LegalActions(); }
        string Blocked(RimketBattle b, int i) { return AsFoe ? b.MoveBlockedFor(b.Foe, i) : b.MoveBlocked(i); }

        public AiEncoder(EngineSettings settings, bool asFoe = false) { s = settings; AsFoe = asFoe; }

        // ------------------------------------------------------------------ 행동 매핑

        List<int> BenchIndices(RimketBattle b)
        {
            var l = new List<int>(); var me = Me(b);
            for (int i = 0; i < me.Party.Count; i++)
                if (me.Active == null || me.Party[i] != me.Active.C) l.Add(i);
            if (Window) { var alive = l.FindAll(i => !me.Party[i].IsFainted); alive.AddRange(l.FindAll(i => me.Party[i].IsFainted)); return alive; }
            return l;
        }

        /// <summary>합법 행동 마스크(길이 Actions). 하나도 없으면 첫 합법 행동을 켠다.</summary>
        public bool[] Mask(RimketBattle b)
        {
            var m = new bool[Actions]; var bench = BenchIndices(b);
            foreach (var a in Legal(b))
            {
                if (a.Kind == ActionKind.UseMove && a.Index < 4 && Blocked(b, a.Index) == null) m[a.Index] = true;
                else if (a.Kind == ActionKind.Struggle || a.Kind == ActionKind.BasicAttack) m[0] = true;
                else if (a.Kind == ActionKind.Switch) { int k = bench.IndexOf(a.Index); if (k >= 0 && k < MaxBench) m[4 + k] = true; }
            }
            bool any = false; foreach (var v in m) any |= v;
            if (!any) m[0] = true;
            return m;
        }

        /// <summary>행동 번호 → 엔진 행동. 합법이 아니면 null.</summary>
        public BattleAction? ToAction(RimketBattle b, int a)
        {
            var bench = BenchIndices(b);
            foreach (var x in Legal(b))
            {
                if (a < 4)
                {
                    if (x.Kind == ActionKind.UseMove && x.Index == a && Blocked(b, x.Index) == null) return x;
                    if (a == 0 && (x.Kind == ActionKind.Struggle || x.Kind == ActionKind.BasicAttack)) return x;
                }
                else if (x.Kind == ActionKind.Switch && a - 4 < bench.Count && bench[a - 4] == x.Index) return x;
            }
            return null;
        }

        // ------------------------------------------------------------------ 선출(팀 프리뷰)
        // 선출 단계의 행동: 0~5 = 내 6마리 중 하나를 고른다(순서대로 3번, 첫 번째가 선봉). 전투 행동과 같은 6칸을 같이 쓴다.

        IList<RimketCreature> pvMine, pvOpp; float[] pvCache; readonly List<int> pvPicks = new List<int>();

        /// <summary>양쪽 6마리(내 쪽, 상대 쪽 순서)를 알려 준다. 선출 단계와 전투 중 관측의 프리뷰 부분에 쓰인다.</summary>
        public void SetPreview(IList<RimketCreature> mySix, IList<RimketCreature> oppSix)
        {
            pvMine = mySix; pvOpp = oppSix; pvPicks.Clear(); pvCache = null;
            double sum = 0; int n = 0; foreach (var c in mySix) { sum += c.Atk + c.Def + c.SpA + c.SpD + c.Spe; n += 5; }
            typical = n > 0 ? (float)(sum / n) : 100f;
        }

        /// <summary>선출을 확정했다고 알려 준다(전투 중 관측에 '내가 고른 것'이 표시된다).</summary>
        public void SetPicks(IList<int> picks) { pvPicks.Clear(); pvPicks.AddRange(picks); pvCache = null; }

        static bool Completable(IList<RimketCreature> six, List<int> chosen, int need, int from)
        {
            if (chosen.Count >= need) return true;
            for (int i = from; i < six.Count; i++)
            {
                if (chosen.Contains(i) || RankedRules.ClauseViolation(six, chosen, i) != null) continue;
                chosen.Add(i); bool ok = Completable(six, chosen, need, i + 1); chosen.RemoveAt(chosen.Count - 1);
                if (ok) return true;
            }
            return false;
        }

        /// <summary>선출 단계의 합법 행동: 아직 안 골랐고, 종족·도구 중복이 아니며, 고른 뒤에도 3마리를 채울 수 있는 것.</summary>
        public bool[] PickMask(IList<int> picked)
        {
            var m = new bool[Actions]; var ch = new List<int>(picked);
            for (int i = 0; i < pvMine.Count && i < Actions; i++)
            {
                if (ch.Contains(i) || RankedRules.ClauseViolation(pvMine, ch, i) != null) continue;
                ch.Add(i); m[i] = Completable(pvMine, ch, RankedRules.BringCount, 0); ch.RemoveAt(ch.Count - 1);
            }
            return m;
        }

        /// <summary>선출 단계의 관측(전투 부분은 0).</summary>
        public float[] EncodePick(IList<int> picked)
        {
            var o = new float[Size];
            Preview(o, BattleSize, picked, true);
            return o;
        }

        void Preview(float[] o, int start, IList<int> picked, bool pickPhase)
        {
            int p = start;
            o[p++] = pickPhase ? 1f : 0f;
            for (int i = 0; i < TeamSize; i++)
            {
                int q = p; var c = i < pvMine.Count ? pvMine[i] : null;
                Mon(o, ref q, c, null, true, false, null, true);
                q = p + MonSize;
                int ord = picked.IndexOf(i);
                if (ord >= 0) { o[q] = 1f; if (ord < 3) o[q + 1 + ord] = 1f; }
                p += OwnPreviewSize;
            }
            for (int i = 0; i < TeamSize; i++)
            {
                var c = i < pvOpp.Count ? pvOpp[i] : null;
                if (c != null)
                {
                    o[p] = 1f;
                    if (!c.Neutral) { o[p + 1 + (int)c.Type1] = 1f; if (c.Type2.HasValue) o[p + 1 + Types + (int)c.Type2.Value] = 1f; }
                    o[p + 1 + Types * 2] = Math.Min(2f, c.MaxHp / 400f);
                }
                p += FoePreviewSize;
            }
            if (p != start + PreviewSize) throw new InvalidOperationException("PreviewSize 불일치 " + (p - start));
        }

        // ------------------------------------------------------------------ 관측

        public float[] Encode(RimketBattle b)
        {
            var o = new float[Size]; int p = 0;
            if (pvMine != null)
            {
                // 프리뷰 부분은 판이 진행되는 동안 변하지 않으므로 한 번만 만들어 복사한다
                if (pvCache == null) { pvCache = new float[PreviewSize]; var tmp = new float[Size]; Preview(tmp, BattleSize, pvPicks, false); Array.Copy(tmp, BattleSize, pvCache, 0, PreviewSize); }
                Array.Copy(pvCache, 0, o, BattleSize, PreviewSize);
            }
            var me = Me(b); var foe = Opp(b);
            { double sum = 0; int n = 0; foreach (var c in me.Party) { sum += c.Atk + c.Def + c.SpA + c.SpD + c.Spe; n += 5; } typical = n > 0 ? (float)(sum / n) : 100f; }
            var meC = Order(me.Party, me.Active != null ? me.Active.C : null, Window);
            var foeActive = foe.Active != null ? foe.Active.C : null;
            if (foeActive != null) foeSeen.Add(foeActive);
            var foeC = Order(foe.Party, foeActive, Window);
            var myActive = me.Active != null ? me.Active.C : null;
            for (int i = 0; i < MaxMons; i++)
            {
                var c = i < meC.Count ? meC[i] : null;
                Mon(o, ref p, c, b, true, i == 0, foeActive, true);
            }
            for (int i = 0; i < MaxMons; i++)
            {
                var c = i < foeC.Count ? foeC[i] : null;
                bool seen = c != null && foeSeen.Contains(c);
                Mon(o, ref p, seen ? c : null, b, false, i == 0, myActive, seen);
            }
            Field(o, ref p, b, me, foe);
            return o;
        }

        static List<RimketCreature> Order(IList<RimketCreature> party, RimketCreature active, bool window = false)
        {
            var l = new List<RimketCreature>(); if (active != null) l.Add(active);
            if (window)
            {
                foreach (var c in party) if (c != active && !c.IsFainted) l.Add(c);
                foreach (var c in party) if (c != active && c.IsFainted) l.Add(c);
                return l;
            }
            foreach (var c in party) if (c != active) l.Add(c);
            return l;
        }

        void Mon(float[] o, ref int p, RimketCreature c, RimketBattle b, bool own, bool active, RimketCreature other, bool known)
        {
            int start = p;
            if (c == null) { p = start + MonSize; return; }
            o[p++] = 1f; o[p++] = c.IsFainted ? 1f : 0f; o[p++] = active ? 1f : 0f;
            o[p++] = c.MaxHp > 0 ? (float)c.Hp / c.MaxHp : 0f; o[p++] = c.Level / 100f;
            bool hide = !own && Hidden;
            o[p++] = Math.Min(2f, c.MaxHp / 400f);
            if (hide) p += 5;      // 상대 공·방·특공·특방·속도는 화면에 없다
            else { o[p++] = Math.Min(2f, c.Atk / 300f); o[p++] = Math.Min(2f, c.Def / 300f); o[p++] = Math.Min(2f, c.SpA / 300f); o[p++] = Math.Min(2f, c.SpD / 300f); o[p++] = Math.Min(2f, c.Spe / 300f); }
            if (!c.Neutral)
            {
                o[p + (int)c.Type1] = 1f;
                if (c.Type2.HasValue) o[p + Types + (int)c.Type2.Value] = 1f;
            }
            p += Types * 2;
            var f = b != null ? b.PeekFighter(c) : null;
            if (f != null && f.Status != StatusCondition.None) o[p + (int)f.Status - 1] = 1f;
            p += 6;
            for (int k = 0; k < 7; k++) o[p + k] = active && f != null ? f.Stage[k] / 6f : 0f;
            p += 7;
            for (int i = 0; i < 4; i++)
            {
                if (i < c.Moves.Count && (own || FullInfo || (f != null && f.UsedMoves.Contains(c.Moves[i].Move.Name))))
                    MoveFeat(o, ref p, c.Moves[i], c, other, own);
                else p += MoveSize;
            }
            if (p != start + BaseMonSize) throw new InvalidOperationException("MonSize 불일치 " + (p - start));
            // 특성·도구: 내 쪽은 항상, 상대는 화면에 드러난 것만(특성은 발동 알림이 나온 뒤, 도구는 가림)
            string ab = f != null ? f.Ability : c.Ability, it = f != null ? f.HeldItem : c.Item;
            bool showAb = own || !Hidden || (f != null && f.AbilityShown), showIt = own || !Hidden;
            int ai, ii;
            if (showAb && ab != null && abilityIdx.TryGetValue(ab, out ai)) o[p + ai] = 1f;
            if (showIt && it != null && itemIdx.TryGetValue(it, out ii)) o[p + AbilityCount + ii] = 1f;
            p += AbilityCount + ItemCount;
        }

        // 가린 상대 능력치 대신 typical을 쓰도록 하는 배율(Damage가 능력치에 곱하는 값)
        double AtkAdj(RimketCreature user, Move m) { if (!Hidden) return 1.0; double v = m.Category == MoveCategory.Physical ? user.Atk : user.SpA; return typical / Math.Max(1.0, v); }
        double DefAdj(RimketCreature target, Move m) { if (!Hidden) return 1.0; double v = m.Category == MoveCategory.Physical ? target.Def : target.SpD; return typical / Math.Max(1.0, v); }

        void MoveFeat(float[] o, ref int p, MoveSlot slot, RimketCreature user, RimketCreature target, bool own)
        {
            var m = slot.Move; int start = p;
            o[p++] = 1f;
            o[p++] = Math.Min(2f, m.Power / 150f); o[p++] = (m.Accuracy <= 0 ? 100 : m.Accuracy) / 100f;
            o[p++] = m.Priority / 3f;
            o[p++] = !own || m.MaxPP <= 0 ? 1f : (float)slot.PP / m.MaxPP;
            o[p + (int)m.Category] = 1f; p += 3;
            if (!m.Typeless) o[p + (int)m.Type] = 1f;
            p += Types;
            o[p++] = m.IsDamaging ? 1f : 0f;
            o[p++] = target != null && m.IsDamaging && target.MaxHp > 0 ? (float)Math.Min(2.0, RankedAI.Damage(user, m, target, own ? 1.0 : AtkAdj(user, m), own ? DefAdj(target, m) : 1.0, s) / Math.Max(1, target.Hp)) : 0f;
            bool status = false, boostSelf = false, dropFoe = false;
            foreach (var e in m.Effects)
            {
                if (e.Kind >= EffectKind.Burn && e.Kind <= EffectKind.Sleep) status = true;
                if (e.Kind == EffectKind.StatChange) { if (e.OnUser && e.Amount > 0) boostSelf = true; if (!e.OnUser && e.Amount < 0) dropFoe = true; }
            }
            o[p++] = status ? 1f : 0f; o[p++] = boostSelf ? 1f : 0f; o[p++] = dropFoe ? 1f : 0f;
            o[p++] = m.Has(EffectKind.Drain) ? 1f : 0f; o[p++] = m.Has(EffectKind.Recoil) || m.Has(EffectKind.RecoilHalfMax) ? 1f : 0f;
            o[p++] = m.Has(EffectKind.MultiHit) ? 1f : 0f; o[p++] = m.Has(EffectKind.Recharge) || m.Has(EffectKind.TwoTurn) ? 1f : 0f;
            o[p++] = m.Has(EffectKind.Explode) ? 1f : 0f; o[p++] = m.Has(EffectKind.Flinch) ? 1f : 0f;
            o[p++] = m.Has(EffectKind.TrapTarget) || m.Has(EffectKind.Bind) ? 1f : 0f;
            o[p++] = m.Fn != "None" ? 1f : 0f;
            o[p++] = m.EffectChance / 100f;
            o[p++] = m.Target == MoveTarget.User || m.Target == MoveTarget.UserSide ? 1f : 0f;
            if (p != start + MoveSize) throw new InvalidOperationException("MoveSize 불일치 " + (p - start));
        }

        static void Field(float[] o, ref int p, RimketBattle b, BattleSide me, BattleSide foe)
        {
            int start = p;
            o[p + (int)b.CurrentWeather] = 1f; p += 9;
            o[p + (int)b.CurrentTerrain] = 1f; p += 5;
            o[p++] = b.TrickRoomActive ? 1f : 0f; o[p++] = b.GravityActive ? 1f : 0f;
            foreach (var side in new[] { me, foe })
            {
                var c = side.Cond;
                o[p++] = c.Reflect > 0 ? 1f : 0f; o[p++] = c.LightScreen > 0 ? 1f : 0f; o[p++] = c.AuroraVeil > 0 ? 1f : 0f; o[p++] = c.Tailwind > 0 ? 1f : 0f;
                o[p++] = c.Spikes / 3f; o[p++] = c.ToxicSpikes / 2f; o[p++] = c.StealthRock ? 1f : 0f; o[p++] = c.StickyWeb ? 1f : 0f;
            }
            o[p++] = Math.Min(1f, b.Turn / 50f);
            if (p != start + FieldSize) throw new InvalidOperationException("FieldSize 불일치 " + (p - start));
        }
    }
}
