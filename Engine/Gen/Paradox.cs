using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>패러독스 배틀: 만나기 어려운 6티어 / ?티어와 싸워 이기면 그 정보가 담긴 유전자 쐐기를 얻는다. 쐐기는 100% 얻는다.</summary>
    public static class Paradox
    {
        public const int FoeLevel = 150;         // 상대는 Lv.150 고정(내 림켓몬은 원래 레벨 그대로 → 위험이 있다)
        public const int BringMax = 3;
        /// <summary>상대 후보: 종족값 600 이상인 기본 종족 + 폼(폼은 별개 종으로 나온다).</summary>
        static List<SpeciesInfo> pool;
        public static IList<SpeciesInfo> Pool
        {
            get
            {
                if (pool == null)
                {
                    pool = SpeciesCatalog.OfTier(EngineSettings.TierCount).ToList();
                    foreach (var f in SpeciesCatalog.Forms) if (f.Tier == EngineSettings.TierCount && !ItemForms.IsItemForm(f.Id) && !pool.Contains(f)) pool.Add(f);      // 도구로 바뀌는 모습(아르세우스 플레이트·실버디 메모리·오리진폼)은 따로 나오지 않는다
                }
                return pool;
            }
        }
        /// <summary>후보 하나가 나올 확률: 각각 1%(후보가 100종을 넘으면 똑같이 나눔). 나머지는 전부 ?티어.</summary>
        public static double SixthEach { get { return Math.Min(0.01, 1.0 / Math.Max(1, Pool.Count)); } }
        public static double MysteryShare { get { return Math.Max(0.0, 1.0 - SixthEach * Pool.Count); } }

        /// <summary>이번 상대. 6티어 종족 중 하나(균등) 또는 ?티어. 쐐기 본체이기도 하다(레벨 150, 개체값 6V).</summary>
        public static RimketCreature MakeFoe(IRng rng, EngineSettings s, bool? mystery = null)
        {
            var pool = Pool;
            int idx = mystery == true ? -1 : mystery == false ? rng.Next(0, pool.Count) : (int)Math.Floor(rng.NextDouble() / SixthEach);
            RimketCreature c;
            if (idx < 0 || idx >= pool.Count) c = MysteryGen.Create(FoeLevel, rng, s);
            else c = SpawnRoller.FromSpecies(pool[idx], FoeLevel, rng, s);
            c.FullHeal();
            return c;
        }

        /// <summary>쐐기로 쓸 수 있는 개체(깨끗한 복사본).</summary>
        public static RimketCreature MakeWedge(RimketCreature foe)
        {
            var w = foe.Clone();
            w.Sp = new int[6]; w.Exp = RimketCreature.ExpForLevel(w.Level); w.Item = null;
            w.RecalculateStats(); w.FullHeal();
            return w;
        }

        public static bool CanReceive(RimketCreature target) { return target != null; }     // 사람 림켓몬(마스터볼로 잡은 사람)에게도 쓸 수 있다

        /// <summary>쐐기의 정보로 대상을 완전히 덮어쓴다: 종족·타입·종족값·개체값·성격·성별·특성·기술·티어. 레벨·경험치·친밀도·도구·이름·그림(림월드 동물)은 그대로.</summary>
        public static bool Apply(RimketCreature target, RimketCreature wedge)
        {
            if (!CanReceive(target) || wedge == null) return false;
            if (string.IsNullOrEmpty(target.WedgeBackup)) target.WedgeBackup = Backup(target);      // 처음 쐐기를 쓸 때의 원래 모습(쐐기를 바꿔 끼워도 원래 것은 유지)
            bool hasShell = target.PawnId != 0 || (target.SpeciesId != null && target.SpeciesId != target.Species);      // 림월드 동물·메카·사람 몸(껍데기)은 그대로 둔다 → 방생하면 원래 몸으로 나간다
            target.Species = wedge.Species; if (!hasShell) target.SpeciesId = wedge.SpeciesId; target.Neutral = false;      // 사람도 이 순간부터 속성을 가진다
            target.Tier = wedge.Tier; target.BaseTier = wedge.BaseTier;
            target.Type1 = wedge.Type1; target.Type2 = wedge.Type2;
            target.BaseHp = wedge.BaseHp; target.BaseAtk = wedge.BaseAtk; target.BaseDef = wedge.BaseDef;
            target.BaseSpA = wedge.BaseSpA; target.BaseSpD = wedge.BaseSpD; target.BaseSpe = wedge.BaseSpe;
            target.Iv = (int[])wedge.Iv.Clone(); target.Sp = (int[])wedge.Sp.Clone();
            target.Nature = wedge.Nature; target.Gender = wedge.Gender; target.Ability = wedge.Ability;
            target.Moves = wedge.Moves.Select(m => new MoveSlot(m.Move)).ToList();
            target.RecalculateStats(); target.FullHeal();
            return true;
        }

        public static bool HasWedge(RimketCreature c) { return c != null && !string.IsNullOrEmpty(c.WedgeBackup); }

        /// <summary>쐐기를 뺀다: 원래 정보로 되돌리고, 지금 들어 있던 쐐기를 돌려준다(가방에 다시 넣을 수 있게). 실패하면 null.</summary>
        public static RimketCreature Remove(RimketCreature target)
        {
            if (!HasWedge(target)) return null;
            var wedge = MakeWedge(target);
            wedge.WedgeBackup = null; SpeciesInfo sp; wedge.Name = SpeciesCatalog.TryGet(target.Species, out sp) ? sp.Name : (MysteryGen.IsMystery(target.Species) ? "???" : target.Species);
            if (!Restore(target, target.WedgeBackup)) return null;
            target.WedgeBackup = null;
            target.RecalculateStats(); target.FullHeal();
            return wedge;
        }

        static string J(int[] a) { return string.Join(",", a.Select(v => v.ToString()).ToArray()); }

        public static string Backup(RimketCreature c)
        {
            return string.Join("|", new[] {
                c.Species ?? "", c.SpeciesId ?? "", c.Neutral ? "1" : "0", c.Tier.ToString(), c.BaseTier.ToString(),
                ((int)c.Type1).ToString(), c.Type2.HasValue ? ((int)c.Type2.Value).ToString() : "-1",
                J(new[] { c.BaseHp, c.BaseAtk, c.BaseDef, c.BaseSpA, c.BaseSpD, c.BaseSpe }), J(c.Iv), J(c.Sp),
                c.Nature.ToString(), c.Gender.ToString(), c.Ability ?? "", string.Join(",", c.Moves.Select(m => m.Move.Id).ToArray()) });
        }

        static bool Restore(RimketCreature c, string b)
        {
            try
            {
                var p = b.Split('|');
                if (p.Length < 14) return false;
                Func<string, int[]> ints = s => s.Split(',').Select(int.Parse).ToArray();
                var bs = ints(p[7]); var iv = ints(p[8]); var sp = ints(p[9]);
                if (bs.Length != 6 || iv.Length != 6 || sp.Length != 6) return false;
                var moves = new List<MoveSlot>();
                foreach (var id in p[13].Split(',')) { if (id.Length == 0) continue; try { moves.Add(new MoveSlot(MoveCatalog.Get(id))); } catch (Exception) { } }
                c.Species = p[0]; c.SpeciesId = p[1]; c.Neutral = p[2] == "1";
                c.Tier = int.Parse(p[3]); c.BaseTier = int.Parse(p[4]);
                c.Type1 = (ElementType)int.Parse(p[5]); int t2 = int.Parse(p[6]); c.Type2 = t2 < 0 ? (ElementType?)null : (ElementType)t2;
                c.BaseHp = bs[0]; c.BaseAtk = bs[1]; c.BaseDef = bs[2]; c.BaseSpA = bs[3]; c.BaseSpD = bs[4]; c.BaseSpe = bs[5];
                c.Iv = iv; c.Sp = sp; c.Nature = int.Parse(p[10]); c.Gender = int.Parse(p[11]); c.Ability = p[12].Length == 0 ? null : p[12];
                c.Moves = moves;
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
