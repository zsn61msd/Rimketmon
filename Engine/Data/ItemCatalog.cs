using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    public sealed class ItemInfo
    {
        public string Id, En, Ko, DescEn, DescKo, Flags;
        public int Pocket, Price, Fling, NgPower;
        public ElementType? NgType;
        public bool IsBerry;
        public bool HasFlag(string f) { return Flags != null && ("," + Flags + ",").Contains("," + f + ","); }
        /// <summary>지닐 수 있는 전투용 도구인가(열쇠/몬스터볼/우편/기술머신 등은 제외).</summary>
        public bool Holdable { get { return !HasFlag("KeyItem") && !HasFlag("PokeBall") && !HasFlag("IconMail") && !HasFlag("Fossil") && !HasFlag("Apricorn"); } }
        public string Name { get { return Ko.Length > 0 ? Ko : En; } }
    }

    /// <summary>PBS 도구 데이터(Tools/GenItemData.py가 만든 ItemData.Raw)를 조회하는 카탈로그와, 전투에서 쓰는 도구 분류표.</summary>
    public static class ItemCatalog
    {
        private static Dictionary<string, ItemInfo> all;
        private static Dictionary<string, ItemInfo> byKo;

        private static void Init()
        {
            if (all != null) return;
            var d = new Dictionary<string, ItemInfo>(); var k = new Dictionary<string, ItemInfo>();
            foreach (var r in ItemData.Raw)
            {
                var it = new ItemInfo
                {
                    Id = (string)r[0], En = (string)r[1], Ko = (string)r[2], Pocket = (int)r[3], Price = (int)r[4], Fling = (int)r[5],
                    NgPower = (int)r[7], IsBerry = (bool)r[8], Flags = (string)r[9], DescEn = (string)r[10], DescKo = (string)r[11]
                };
                string nt = (string)r[6];
                if (nt.Length > 0) { ElementType t; if (Enum.TryParse(nt, true, out t)) it.NgType = t; }
                d[it.Id] = it; if (it.Ko.Length > 0) k[it.Ko] = it;
            }
            byKo = k; all = d;
        }

        public static ItemInfo Get(string id) { Init(); ItemInfo it; return id != null && all.TryGetValue(id, out it) ? it : null; }
        public static ItemInfo ByName(string ko) { Init(); ItemInfo it; return ko != null && byKo.TryGetValue(ko, out it) ? it : null; }
        public static IEnumerable<ItemInfo> All { get { Init(); return all.Values; } }
        public static string NameOf(string id)
        {
            var it = Get(id); if (it == null) return id;
            if (!Lang.English) return it.Name;
            var t = Lang.ItemText(id); if (t != null && t.Length > 1 && t[1].Length > 0) return t[1];
            return it.En.Length > 0 ? it.En : it.Name;
        }
        /// <summary>지금 언어의 도구 설명.</summary>
        public static string DescOf(ItemInfo it)
        {
            if (it == null) return "";
            if (!Lang.English) return it.DescKo;
            var t = Lang.ItemText(it.Id); if (t != null && t.Length > 2 && t[2].Length > 0) return t[2];
            return it.DescEn.Length > 0 ? it.DescEn : it.DescKo;
        }
        public static bool IsBerry(string id) { var it = Get(id); return it != null && it.IsBerry; }

        // ---------------------------------------------------------------- 분류표
        public static readonly Dictionary<string, ElementType> TypeBoost = new Dictionary<string, ElementType>
        {
            {"SILKSCARF", ElementType.Normal}, {"CHARCOAL", ElementType.Fire}, {"MYSTICWATER", ElementType.Water}, {"MAGNET", ElementType.Electric},
            {"MIRACLESEED", ElementType.Grass}, {"NEVERMELTICE", ElementType.Ice}, {"BLACKBELT", ElementType.Fighting}, {"POISONBARB", ElementType.Poison},
            {"SOFTSAND", ElementType.Ground}, {"SHARPBEAK", ElementType.Flying}, {"TWISTEDSPOON", ElementType.Psychic}, {"SILVERPOWDER", ElementType.Bug},
            {"HARDSTONE", ElementType.Rock}, {"SPELLTAG", ElementType.Ghost}, {"DRAGONFANG", ElementType.Dragon}, {"BLACKGLASSES", ElementType.Dark},
            {"METALCOAT", ElementType.Steel}, {"FAIRYFEATHER", ElementType.Fairy},
            {"SEAINCENSE", ElementType.Water}, {"WAVEINCENSE", ElementType.Water}, {"ROSEINCENSE", ElementType.Grass}, {"ODDINCENSE", ElementType.Psychic}, {"ROCKINCENSE", ElementType.Rock},
            {"FLAMEPLATE", ElementType.Fire}, {"SPLASHPLATE", ElementType.Water}, {"ZAPPLATE", ElementType.Electric}, {"MEADOWPLATE", ElementType.Grass}, {"ICICLEPLATE", ElementType.Ice},
            {"FISTPLATE", ElementType.Fighting}, {"TOXICPLATE", ElementType.Poison}, {"EARTHPLATE", ElementType.Ground}, {"SKYPLATE", ElementType.Flying}, {"MINDPLATE", ElementType.Psychic},
            {"INSECTPLATE", ElementType.Bug}, {"STONEPLATE", ElementType.Rock}, {"SPOOKYPLATE", ElementType.Ghost}, {"DRACOPLATE", ElementType.Dragon}, {"DREADPLATE", ElementType.Dark},
            {"IRONPLATE", ElementType.Steel}, {"PIXIEPLATE", ElementType.Fairy}
        };

        public static readonly Dictionary<string, ElementType> Memory = new Dictionary<string, ElementType>();
        public static readonly Dictionary<string, ElementType> Gem = new Dictionary<string, ElementType>();
        public static readonly Dictionary<string, ElementType> ResistBerry = new Dictionary<string, ElementType>
        {
            {"OCCABERRY", ElementType.Fire}, {"PASSHOBERRY", ElementType.Water}, {"WACANBERRY", ElementType.Electric}, {"RINDOBERRY", ElementType.Grass}, {"YACHEBERRY", ElementType.Ice},
            {"CHOPLEBERRY", ElementType.Fighting}, {"KEBIABERRY", ElementType.Poison}, {"SHUCABERRY", ElementType.Ground}, {"COBABERRY", ElementType.Flying}, {"PAYAPABERRY", ElementType.Psychic},
            {"TANGABERRY", ElementType.Bug}, {"CHARTIBERRY", ElementType.Rock}, {"KASIBBERRY", ElementType.Ghost}, {"HABANBERRY", ElementType.Dragon}, {"COLBURBERRY", ElementType.Dark},
            {"BABIRIBERRY", ElementType.Steel}, {"CHILANBERRY", ElementType.Normal}, {"ROSELIBERRY", ElementType.Fairy}
        };

        static ItemCatalog()
        {
            foreach (var kv in new Dictionary<string, ElementType> { {"FIRE", ElementType.Fire}, {"WATER", ElementType.Water}, {"ELECTRIC", ElementType.Electric}, {"GRASS", ElementType.Grass}, {"ICE", ElementType.Ice},
                {"FIGHTING", ElementType.Fighting}, {"POISON", ElementType.Poison}, {"GROUND", ElementType.Ground}, {"FLYING", ElementType.Flying}, {"PSYCHIC", ElementType.Psychic}, {"BUG", ElementType.Bug},
                {"ROCK", ElementType.Rock}, {"GHOST", ElementType.Ghost}, {"DRAGON", ElementType.Dragon}, {"DARK", ElementType.Dark}, {"STEEL", ElementType.Steel}, {"FAIRY", ElementType.Fairy} })
            { Memory[kv.Key + "MEMORY"] = kv.Value; Gem[kv.Key + "GEM"] = kv.Value; }
            Gem["NORMALGEM"] = ElementType.Normal;
        }


        /// <summary>전투에서 실제로 효과가 있는 도구 id 전체(상점/가방 UI의 대상).</summary>
        private static HashSet<string> supported;
        public static HashSet<string> Supported { get { if (supported == null) supported = BuildSupported(); return supported; } }
        /// <summary>상점에서 파는 도구(종족 전용/플레이트/메모리 같은 특수 도구는 제외).</summary>
        /// <summary>지니면 모습이 바뀌는 도구(ItemForms): 플레이트·메모리·백금옥·큰금강옥·큰백옥·큰백금옥.</summary>
        public static HashSet<string> FormItems
        {
            get
            {
                return formItems ?? (formItems = new HashSet<string>(new[] { "GRISEOUSORB", "GRISEOUSCORE", "ADAMANTCRYSTAL", "LUSTROUSGLOBE" }
                    .Concat(TypeBoost.Keys.Where(k => k.EndsWith("PLATE"))).Concat(Memory.Keys).Where(k => Get(k) != null)));
            }
        }
        private static HashSet<string> formItems;

        public static bool Shoppable(string id)
        {
            var it = Get(id); if (it == null) return false;
            if (FormItems.Contains(id) || Evolution.AllEvoItems().Contains(id)) return true;      // 모습을 바꾸는 도구·진화 도구(돌·금속코트 등)는 뽑기로 얻는다
            if (!Supported.Contains(id) || it.Price <= 0) return false;
            if (id == "ADAMANTORB" || id == "LUSTROUSORB") return false;      // 효과 없는 옛 도구
            return true;
        }
        /// <summary>상점 가격 기준값. 원작(팬게임) 가격 그대로(최소 5) → RankedLadder.ShopBp가 BP로 눌러 준다.</summary>
        /// 나무열매는 원작에선 싼 소모품이지만, 여기서는 지닌 도구처럼 전투가 끝나면 돌아오므로 일반 도구의 보통 가격(중앙값)으로 판다.
        public static int SilverPrice(string id)
        {
            var it = Get(id); if (it == null) return 0;
            if (it.IsBerry) return BerrySilver;
            return Math.Max(5, it.Price);
        }

        private static int berrySilver = -1;
        /// <summary>열매 한 개의 가격(은) = 상점에서 파는 일반 도구 가격의 중앙값.</summary>
        public static int BerrySilver
        {
            get
            {
                if (berrySilver >= 0) return berrySilver;
                var l = new List<int>();
                foreach (var x in All) if (!x.IsBerry && Shoppable(x.Id)) l.Add(Math.Max(5, x.Price));
                l.Sort();
                return berrySilver = l.Count == 0 ? 4000 : l[l.Count / 2];
            }
        }

        private static HashSet<string> BuildSupported()
        {
            var h = new HashSet<string>
            {
                "LEFTOVERS","BLACKSLUDGE","STICKYBARB","FLAMEORB","TOXICORB","LIFEORB","EXPERTBELT","MUSCLEBAND","WISEGLASSES","PUNCHINGGLOVE",
                "CHOICEBAND","CHOICESPECS","CHOICESCARF","FOCUSSASH","FOCUSBAND","ASSAULTVEST","EVIOLITE","ROCKYHELMET","AIRBALLOON","IRONBALL","SHELLBELL",
                "KINGSROCK","RAZORFANG","SCOPELENS","RAZORCLAW","WIDELENS","ZOOMLENS","BRIGHTPOWDER","LAXINCENSE","QUICKCLAW","LAGGINGTAIL","FULLINCENSE",
                "QUICKPOWDER","METALPOWDER","THICKCLUB","LIGHTBALL","LUCKYPUNCH","LEEK","DEEPSEATOOTH","DEEPSEASCALE","ADAMANTORB","LUSTROUSORB","GRISEOUSORB",
                "WEAKNESSPOLICY","ABSORBBULB","CELLBATTERY","LUMINOUSMOSS","SNOWBALL","REDCARD","EJECTBUTTON","WHITEHERB","MENTALHERB","POWERHERB","BERRYJUICE",
                "ELECTRICSEED","GRASSYSEED","MISTYSEED","PSYCHICSEED","ROOMSERVICE","HEAVYDUTYBOOTS","SAFETYGOGGLES","SHEDSHELL","LIGHTCLAY","DESTINYKNOT",
                "HEATROCK","DAMPROCK","SMOOTHROCK","ICYROCK","MACHOBRACE","POWERWEIGHT","POWERBRACER","POWERBELT","POWERLENS","POWERBAND","POWERANKLET",
                "ORANBERRY","SITRUSBERRY","FIGYBERRY","WIKIBERRY","MAGOBERRY","AGUAVBERRY","IAPAPABERRY","CHERIBERRY","CHESTOBERRY","PECHABERRY","RAWSTBERRY","ASPEARBERRY",
                "PERSIMBERRY","LUMBERRY","LIECHIBERRY","GANLONBERRY","SALACBERRY","PETAYABERRY","APICOTBERRY","STARFBERRY","LANSATBERRY","MICLEBERRY","CUSTAPBERRY",
                "LEPPABERRY","ENIGMABERRY","JABOCABERRY","ROWAPBERRY","KEEBERRY","MARANGABERRY"
            };
            foreach (var k in TypeBoost.Keys) h.Add(k);
            foreach (var k in Gem.Keys) h.Add(k);
            foreach (var k in Memory.Keys) h.Add(k);
            foreach (var k in ResistBerry.Keys) h.Add(k);
            h.RemoveWhere(x => Get(x) == null);
            return h;
        }

        /// <summary>하이퍼 열매(HYPERxxxBERRY)는 일반 열매와 같은 효과(두 배 용량이 아닌 즉시 발동형)라 원래 열매 id로 본다.</summary>
        public static string BaseBerry(string id)
        {
            return id != null && id.StartsWith("HYPER") && id.EndsWith("BERRY") ? id.Substring(5) : id;
        }

        /// <summary>스스로 떼어낼 수 없는(빼앗기거나 던질 수 없는) 도구인가. species는 지닌 쪽 종족.</summary>
        public static bool Locked(string id, string species)
        {
            var it = Get(id);
            if (it == null) return false;
            species = SpeciesCatalog.BaseOf(species);      // 도구로 바뀐 모습(아르세우스(불꽃) 등)도 원래 종족으로 본다
            if (it.HasFlag("MegaStone") || it.HasFlag("ZCrystal")) return true;
            if (id == "BOOSTERENERGY" && false) return true;
            if (id == "GRISEOUSORB" || id == "GRISEOUSCORE") return species == "GIRATINA";
            if (id == "ADAMANTCRYSTAL") return species == "DIALGA";
            if (id == "LUSTROUSGLOBE") return species == "PALKIA";
            if (TypeBoost.ContainsKey(id) && id.EndsWith("PLATE")) return species == "ARCEUS";
            if (id == "LEGENDPLATE" || id == "BLANKPLATE") return species == "ARCEUS";
            if (Memory.ContainsKey(id)) return species == "SILVALLY";
            if (id == "RUSTEDSWORD") return species == "ZACIAN";
            if (id == "RUSTEDSHIELD") return species == "ZAMAZENTA";
            if (id.EndsWith("DRIVE") && species == "GENESECT") return true;
            if (id == "WELLSPRINGMASK" || id == "HEARTHFLAMEMASK" || id == "CORNERSTONEMASK") return species == "OGERPON";
            return false;
        }
    }
}
