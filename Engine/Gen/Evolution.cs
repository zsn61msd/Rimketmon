using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>진화 조건 한 줄(EvoData). Kind: Level / Item(돌 쓰기) / Friendship / Move / Hold(지니고 레벨업) / Stat / PartySpecies / PartyType.</summary>
    public sealed class EvoCond
    {
        public string From, To, Kind, Param, Time, Gender; public int Level; public bool Consume;
        /// <summary>도감·안내용 한국어 설명.</summary>
        public string Describe()
        {
            string t = Time == "Day" ? "낮에 " : Time == "Night" ? "밤에 " : "";
            string g = Gender == "M" ? "수컷이 " : Gender == "F" ? "암컷이 " : "";
            switch (Kind)
            {
                case "Item": return g + ItemCatalog.NameOf(Param) + " 사용";
                case "Friendship": return t + "친밀도 160 이상으로 레벨업" + (Param != "" ? " (" + TypeChart.KoreanName(TypeOf(Param)) + " 기술을 알 때)" : "");
                case "Move": { Move m; return (MoveCatalog.TryGet(Param, out m) ? m.Name : Param) + "을(를) 알고 레벨업"; }
                case "Hold": return t + ItemCatalog.NameOf(Param) + "을(를) 지니고 레벨업";
                case "Stat": return "Lv." + Level + " 이상, " + (Param == "ATK" ? "공격 > 방어" : Param == "DEF" ? "방어 > 공격" : "공격 = 방어");
                case "PartySpecies": { SpeciesInfo sp; return "파티에 " + (SpeciesCatalog.TryGet(Param, out sp) ? sp.Name : Param) + "이(가) 있을 때 레벨업"; }
                case "PartyType": return "Lv." + Level + " 이상, 파티에 " + TypeChart.KoreanName(TypeOf(Param)) + " 타입이 있을 때";
                default: return t + g + "Lv." + Level;
            }
        }
        public static ElementType TypeOf(string upper)
        {
            ElementType e; return Enum.TryParse(upper, true, out e) ? e : ElementType.Normal;
        }
    }

    /// <summary>
    /// 진화: 포켓몬 모습인 개체(그림 id = 종족 id)만 진화한다. 림월드 동물 모습의 림켓몬·사람·?티어는 진화하지 않는다.
    /// 조건은 원작 그대로(레벨·진화의 돌·친밀도·시간·성별·기술·지닌 도구·파티). 통신교환·아름다움·이벤트처럼 여기서 할 수 없는 방식은 레벨 진화로 바꿨다(EvoData).
    /// 갈래가 여럿이면(이브이 등) 플레이어가 고른다.
    /// </summary>
    public static class Evolution
    {
        /// <summary>지금이 밤인가(림월드 지도 시각). 모드가 넣어 준다. 없으면 낮.</summary>
        public static Func<bool> IsNight;
        public const int FriendshipNeed = 160;

        private static Dictionary<string, List<EvoCond>> conds;
        public static List<EvoCond> CondsOf(string from)
        {
            if (conds == null)
            {
                var d = new Dictionary<string, List<EvoCond>>();
                foreach (var row in EvoData.Rows)
                {
                    var f = row.Split('|');
                    var c = new EvoCond { From = f[0], To = f[1], Kind = f[2], Param = f[3], Level = int.Parse(f[4]), Time = f[5], Gender = f[6], Consume = f[7] == "1" };
                    SpeciesInfo sp, to;
                    if (!SpeciesCatalog.TryGet(c.From, out sp) || !SpeciesCatalog.TryGet(c.To, out to)) continue;
                    if (c.Kind == "Move" && !Training.Learnable(c.From).Any(m => m.Id == c.Param) && !sp.Learnset.Any(m => m.Value.Id == c.Param))
                    {   // 배울 수 없는 기술이 조건이면 레벨 진화로
                        var lv = sp.Evolutions.FirstOrDefault(e => e.Key == c.To);
                        c.Kind = "Level"; c.Param = ""; c.Level = lv.Key != null ? lv.Value : 36;
                    }
                    List<EvoCond> l; if (!d.TryGetValue(c.From, out l)) d[c.From] = l = new List<EvoCond>();
                    l.Add(c);
                }
                conds = d;
            }
            List<EvoCond> r; return conds.TryGetValue(from ?? "", out r) ? r : new List<EvoCond>();
        }

        public static bool CanEvolveKind(RimketCreature c)
        {
            return c != null && !c.Neutral && c.PawnId == 0 && !string.IsNullOrEmpty(c.Species) && c.SpeciesId == c.Species && !MysteryGen.IsMystery(c.Species);
        }

        /// <summary>이 조건을 지금 만족하는가. usedItem: 지금 쓰는 진화의 돌(돌 진화는 이때만). party: 같은 파티(파티 조건용).</summary>
        public static bool Met(EvoCond e, RimketCreature c, string usedItem = null, IList<RimketCreature> party = null)
        {
            if (e.Gender == "M" && c.Gender != Genders.Male) return false;
            if (e.Gender == "F" && c.Gender != Genders.Female) return false;
            if (e.Kind == "Item") return usedItem != null && usedItem == e.Param;
            if (usedItem != null) return false;      // 돌을 쓸 때는 돌 진화만
            if (e.Time != "")
            {
                bool night = IsNight != null && IsNight();
                if ((e.Time == "Night") != night) return false;
            }
            switch (e.Kind)
            {
                case "Level": return c.Level >= e.Level;
                case "Friendship":
                    if (c.Friendship < FriendshipNeed) return false;
                    if (e.Param != "") { var t = EvoCond.TypeOf(e.Param); return c.Moves.Any(m => m.Move.Type == t); }
                    return true;
                case "Move": return c.Moves.Any(m => m.Move.Id == e.Param);
                case "Hold": return c.Item == e.Param;
                case "Stat": return c.Level >= e.Level && (e.Param == "ATK" ? c.Atk > c.Def : e.Param == "DEF" ? c.Def > c.Atk : c.Atk == c.Def);
                case "PartySpecies": return party != null && party.Any(p => p != c && p != null && p.Species == e.Param);
                case "PartyType": { var t = EvoCond.TypeOf(e.Param); return c.Level >= e.Level && party != null && party.Any(p => p != c && p != null && !p.Neutral && p.Types.Contains(t)); }
            }
            return false;
        }

        /// <summary>지금 만족하는 진화 조건들(갈래마다 하나). 진화 멈춤을 켜 두었으면 비어 있다.</summary>
        public static List<EvoCond> Ready(RimketCreature c, string usedItem = null, IList<RimketCreature> party = null)
        {
            var l = new List<EvoCond>();
            if (!CanEvolveKind(c) || c.NoEvolve || c.IsFainted) return l;
            foreach (var e in CondsOf(c.Species))
                if (Met(e, c, usedItem, party) && !l.Any(x => x.To == e.To)) l.Add(e);
            return l;
        }

        /// <summary>지금 진화할 수 있는 종족들(중복 제거). 돌 진화는 빠진다(돌을 쓸 때 따로 묻는다).</summary>
        public static List<SpeciesInfo> Candidates(RimketCreature c, IList<RimketCreature> party = null)
        {
            var l = new List<SpeciesInfo>();
            foreach (var e in Ready(c, null, party)) { SpeciesInfo sp; if (SpeciesCatalog.TryGet(e.To, out sp) && !l.Contains(sp)) l.Add(sp); }
            return l;
        }

        /// <summary>이 진화의 돌(쓰는 도구)로 진화할 수 있는 종족(없으면 null).</summary>
        public static SpeciesInfo ByItem(RimketCreature c, string item)
        {
            var e = Ready(c, item).FirstOrDefault();
            SpeciesInfo sp; return e != null && SpeciesCatalog.TryGet(e.To, out sp) ? sp : null;
        }

        /// <summary>진화의 돌처럼 "쓰는" 진화 도구인가.</summary>
        public static bool IsUseItem(string item)
        {
            if (item == null) return false;
            if (useItems == null) useItems = new HashSet<string>(EvoData.Rows.Select(r => r.Split('|')).Where(f => f[2] == "Item").Select(f => f[3]));
            return useItems.Contains(item);
        }
        private static HashSet<string> useItems;

        /// <summary>진화에 쓰이는 도구 전부(돌·지니는 도구). 뽑기·가방에 넣을 수 있게.</summary>
        public static IEnumerable<string> AllEvoItems()
        {
            return EvoData.Rows.Select(r => r.Split('|')).Where(f => f[2] == "Item" || f[2] == "Hold").Select(f => f[3]).Distinct();
        }

        /// <summary>진화한 뒤: 지니고 진화하는 도구(통신교환 도구 등)는 사라진다.</summary>
        public static void AfterEvolve(RimketCreature c, string from, string to)
        {
            var e = CondsOf(from).FirstOrDefault(x => x.To == to && x.Kind == "Hold" && x.Consume && c.Item == x.Param);
            if (e != null) c.Item = null;
        }

        /// <summary>진화시킨다: 종족·타입·종족값을 바꾸고, 특성은 같은 칸(없으면 첫 칸)을 쓴다. 닉네임이 없으면 이름도 바뀐다. 줄어든 HP 비율은 그대로 두고 최대 HP 증가분만큼 HP가 는다(원작과 같다).</summary>
        public static void Apply(RimketCreature c, SpeciesInfo to)
        {
            SpeciesInfo from; SpeciesCatalog.TryGet(c.Species, out from);
            int abilIdx = from != null ? from.Abilities.IndexOf(c.Ability) : -1;
            bool nick = from != null && c.Name != from.Name && c.Name != from.KoName;
            int oldMax = c.MaxHp;
            c.Species = to.Id; c.SpeciesId = to.Id;
            if (!nick) c.Name = to.Name;
            c.Type1 = to.Type1; c.Type2 = to.Type2;
            c.BaseHp = to.Hp; c.BaseAtk = to.Atk; c.BaseDef = to.Def; c.BaseSpA = to.SpA; c.BaseSpD = to.SpD; c.BaseSpe = to.Spe;
            int newTier = to.Tier;
            if (c.Tier == c.BaseTier) c.Tier = newTier; else c.Tier = Math.Max(c.Tier, newTier);
            c.BaseTier = newTier;
            if (to.Abilities.Count > 0) c.Ability = to.Abilities[abilIdx >= 0 && abilIdx < to.Abilities.Count ? abilIdx : 0];
            c.RecalculateStats();
            if (c.Hp > 0) c.Hp = Math.Min(c.MaxHp, c.Hp + Math.Max(0, c.MaxHp - oldMax));
            // 진화하며 배우는 기술: 새 종족이 지금 레벨 이하에서 배우는 기술 중 모르는 것을 빈 칸에 넣는다(칸이 다 찼으면 넣지 않는다)
            foreach (var lm in to.Learnset)
            {
                if (c.Moves.Count >= 4) break;
                if (lm.Key > c.Level || lm.Key != c.Level && lm.Key != 1) continue;
                if (c.Moves.Any(m => m.Move.Id == lm.Value.Id)) continue;
                c.Moves.Add(new MoveSlot(lm.Value));
            }
        }
    }
}
