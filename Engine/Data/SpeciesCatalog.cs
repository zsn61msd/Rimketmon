using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>레퍼런스 종족 한 종의 데이터(종족값, 타입, 등장 가중치, 습득 기술).</summary>
    public sealed class SpeciesInfo
    {
        public string Id;              // 내부 id (예: BULBASAUR) — 이미지/데이터 조회용
        public string Name;            // 표시 이름(현재 언어). 트레이너의 포켓몬 표시에만 쓴다
        public string KoName;
        public ElementType Type1;
        public ElementType? Type2;
        public int Hp, Atk, Def, SpA, SpD, Spe;
        public double WeightKg = 10;   // 체중(kg): 무게 비례 기술
        public double Weight;          // 야생 등장 가중치 (PokeRogue 바이옴 등급에서 계산)
        public List<KeyValuePair<string, int>> Evolutions = new List<KeyValuePair<string, int>>();   // 트레이너 파티의 진화 단계 결정용
        public List<string> Abilities = new List<string>();   // 일반 특성(숨겨진 특성 제외)
        public List<KeyValuePair<int, Move>> Learnset = new List<KeyValuePair<int, Move>>();

        public bool HasTypeOf(ElementType t) { return Type1 == t || (Type2.HasValue && Type2.Value == t); }
        public int Bst { get { return Hp + Atk + Def + SpA + SpD + Spe; } }
        /// <summary>폼이면 원래 종족 id(알로라 라이츄 → RAICHU, 워시로토무 → ROTOM).</summary>
        public string BaseForm;
        /// <summary>지역폼(별개 종족 번호를 가진 폼)인가.</summary>
        public bool IsRegional;
        public bool Mystery;           // ?티어: 시드로 생성한 올랜덤 종족(MysteryGen)
        public int Tier { get { return Mystery ? 0 : SpeciesCatalog.TierOfBst(Bst); } }
    }

    public static class SpeciesCatalog
    {
        private static readonly List<SpeciesInfo> all = new List<SpeciesInfo>();
        private static readonly Dictionary<string, SpeciesInfo> byId = new Dictionary<string, SpeciesInfo>();
        private static readonly List<SpeciesInfo>[] byTier = new List<SpeciesInfo>[EngineSettings.TierCount + 1];

        /// <summary>종족값 총합 → 티어. 1~5는 5등분(5티어는 최대 600 미만), 6티어는 600 이상.</summary>
        public static int TierOfBst(int bst)
        {
            var s = EngineSettings.BstTierUpperExclusive;
            for (int i = 0; i < s.Length; i++) if (bst < s[i]) return i + 1;
            return EngineSettings.TierCount;
        }

        /// <summary>언어가 바뀌면 이름을 다시 정한다.</summary>
        public static void ApplyLanguage()
        {
            foreach (var sp in all)
            {
                var en = Lang.English ? Lang.EnSpecies(sp.Id) : null;
                sp.Name = en != null && en.Length > 1 && en[1].Length > 0 ? en[1] : sp.KoName;
            }
        }

        static SpeciesCatalog()
        {
            for (int t = 0; t <= EngineSettings.TierCount; t++) byTier[t] = new List<SpeciesInfo>();
            foreach (var row in GameData.SpeciesRows)
            {
                var sp = Parse(row);
                all.Add(sp);
                byId[sp.Id] = sp;
                byTier[sp.Tier].Add(sp);
            }
            // 폼(지역폼·로토무 가전 등): 별개의 완성 형태 종족. 사냥에서만 나오므로 전체 목록(랭크·관장·방문자 풀)에는 넣지 않는다
            var baseOf = new Dictionary<string, string[]>();
            foreach (var row in GameData.FormBaseRows) { var f = row.Split('|'); baseOf[f[0]] = f; }
            foreach (var row in GameData.FormSpeciesRows)
            {
                var sp = Parse(row);
                string[] b;
                if (baseOf.TryGetValue(sp.Id, out b))
                {
                    sp.BaseForm = b[1];
                    sp.IsRegional = b[3].IndexOf('-') < 0;
                    if (!sp.IsRegional) { List<string> l; if (!formsOf.TryGetValue(b[1], out l)) formsOf[b[1]] = l = new List<string>(); l.Add(sp.Id); }
                }
                forms.Add(sp);
                byId[sp.Id] = sp;
            }
        }

        /// <summary>세이브에 남은 개체의 종족이 지금 데이터에 없으면(데이터에서 빠진 종) 대신할 포켓몬:
        /// 개체에 저장된 첫 타입이 같고 종족값 합계가 가장 가까운 종(같으면 id 순). 아는 종족·사람·?티어면 null.</summary>
        public static SpeciesInfo ReplacementFor(string id, ElementType type1, int bst)
        {
            SpeciesInfo known;
            if (string.IsNullOrEmpty(id) || id == "PERSON" || MysteryGen.IsMystery(id) || TryGet(id, out known)) return null;
            if (bst <= 0) bst = 400;
            return all.Where(s => !s.Mystery && s.Type1 == type1).OrderBy(s => Math.Abs(s.Bst - bst)).ThenBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault()
                ?? all.OrderBy(s => Math.Abs(s.Bst - bst)).First();
        }

        private static readonly List<SpeciesInfo> forms = new List<SpeciesInfo>();
        private static readonly Dictionary<string, List<string>> formsOf = new Dictionary<string, List<string>>();
        /// <summary>폼 종족들(지역폼 포함).</summary>
        public static IList<SpeciesInfo> Forms { get { return forms; } }
        /// <summary>같은 종의 다른 모습(로토무 가전 등, 지역폼 제외). 없으면 빈 목록.</summary>
        public static IList<string> FormsOf(string id) { List<string> l; return id != null && formsOf.TryGetValue(id, out l) ? (IList<string>)l : new string[0]; }
        /// <summary>도감·울음소리·지도 그림에서 쓰는 원래 종족 id(폼이 아니면 그대로).</summary>
        public static string BaseOf(string id) { SpeciesInfo sp; return TryGet(id, out sp) && sp.BaseForm != null ? sp.BaseForm : id; }

        private static SpeciesInfo Parse(string row)
        {

            var f = row.Split('|');
            var sp = new SpeciesInfo
            {
                Id = f[0], Name = f[1], KoName = f[1], Type1 = (ElementType)int.Parse(f[2]),
                Type2 = f[3] == "-1" ? (ElementType?)null : (ElementType)int.Parse(f[3]),
                Hp = int.Parse(f[4]), Atk = int.Parse(f[5]), Def = int.Parse(f[6]),
                SpA = int.Parse(f[7]), SpD = int.Parse(f[8]), Spe = int.Parse(f[9]),
                Weight = double.Parse(f[10], System.Globalization.CultureInfo.InvariantCulture)
            };
            if (f.Length > 13 && f[13].Length > 0)
                foreach (var ev in f[13].Split(';'))
                {
                    int c2 = ev.IndexOf(':');
                    sp.Evolutions.Add(new KeyValuePair<string, int>(ev.Substring(0, c2), int.Parse(ev.Substring(c2 + 1))));
                }
            if (f.Length > 14) double.TryParse(f[14], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sp.WeightKg);
            if (f.Length > 12 && f[12].Length > 0) sp.Abilities.AddRange(f[12].Split(';'));
            if (f[11].Length > 0)
                foreach (var e in f[11].Split(';'))
                {
                    int c = e.IndexOf(':');
                    Move m;
                    if (MoveCatalog.TryGet(e.Substring(c + 1), out m))
                        sp.Learnset.Add(new KeyValuePair<int, Move>(int.Parse(e.Substring(0, c)), m));
                }
                        return sp;
        }

        public static int Count { get { return all.Count; } }
        public static IList<SpeciesInfo> All { get { return all; } }
        public static IList<SpeciesInfo> OfTier(int tier) { return byTier[MathUtil.Clamp(tier, 1, EngineSettings.TierCount)]; }
        public static SpeciesInfo Get(string id) { SpeciesInfo sp; if (TryGet(id, out sp)) return sp; throw new KeyNotFoundException(id); }
        public static bool TryGet(string id, out SpeciesInfo sp)
        {
            if (id != null && byId.TryGetValue(id, out sp)) return true;
            sp = id != null && id.StartsWith(MysteryGen.IdPrefix) ? MysteryGen.FromId(id) : null;     // ?티어는 id(시드)만으로 다시 만들어 낸다
            return sp != null;
        }
    }
}
