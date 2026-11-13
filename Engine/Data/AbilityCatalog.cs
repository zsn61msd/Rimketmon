using System;
using System.Collections.Generic;

namespace Rimketmon.Engine
{
    public sealed class AbilityInfo
    {
        public string Id;
        public string Name;
        public string Description;
        public string KoName, KoDescription;
        /// <summary>false면 이름/설명만 표시되고 전투에서는 효과가 없다(날씨/도구/더블배틀 전용 특성 등).</summary>
        public bool Implemented;
        public bool Inert;   // 구현은 되어 있으나 이 모드에서는 효과가 없다(도구/성별/더블배틀 전제)
    }

    public static class AbilityCatalog
    {
        private static readonly Dictionary<string, AbilityInfo> byId = new Dictionary<string, AbilityInfo>();

        /// <summary>전투 엔진이 실제로 처리하는 특성. 이 목록에 없는 특성은 이름과 설명만 보인다.</summary>
        public static readonly HashSet<string> Implemented = new HashSet<string>
        {
            // 등장 시
            "BATTERY","COSTAR","CURIOUSMEDICINE","FRIENDGUARD","HEALER","HOSPITALITY","MINUS","PLUS","POWEROFALCHEMY","POWERSPOT","PROPELLERTAIL","RECEIVER","STALWART","SYMBIOSIS","TELEPATHY",
            "INTIMIDATE","PRESSURE","MOLDBREAKER","TERAVOLT","TURBOBLAZE",
            // 능력치/명중
            "HUGEPOWER","PUREPOWER","GUTS","HUSTLE","DEFEATIST","QUICKFEET","MARVELSCALE","COMPOUNDEYES","NOGUARD",
            "UNAWARE","SIMPLE","CONTRARY","TRUANT","TOXICBOOST","FLAREBOOST",
            // 공격 보정
            "TECHNICIAN","IRONFIST","STRONGJAW","TOUGHCLAWS","MEGALAUNCHER","RECKLESS","SHEERFORCE","ADAPTABILITY",
            "TINTEDLENS","SNIPER","SKILLLINK","SUPERLUCK","SERENEGRACE","PIXILATE","AERILATE","REFRIGERATE","GALVANIZE",
            "BLAZE","OVERGROW","TORRENT","SWARM","TRANSISTOR","DRAGONSMAW","STEELWORKER","WATERBUBBLE","ANALYTIC",
            "PUNKROCK","SHARPNESS","ROCKYPAYLOAD",
            // 방어 보정/면역
            "THICKFAT","HEATPROOF","DRYSKIN","FLASHFIRE","WATERABSORB","VOLTABSORB","LIGHTNINGROD","STORMDRAIN","MOTORDRIVE",
            "SAPSIPPER","LEVITATE","EARTHEATER","SOUNDPROOF","BULLETPROOF","MULTISCALE","SHADOWSHIELD","FILTER","SOLIDROCK",
            "PRISMARMOR","WONDERGUARD","FURCOAT","ICESCALES","FLUFFY","PURIFYINGSALT","STURDY","BATTLEARMOR","SHELLARMOR",
            // 상태이상/능력 하락 방지
            "LIMBER","INSOMNIA","VITALSPIRIT","SWEETVEIL","IMMUNITY","PASTELVEIL","WATERVEIL","MAGMAARMOR","OWNTEMPO",
            "INNERFOCUS","SHIELDDUST","CLEARBODY","WHITESMOKE","FULLMETALBODY","HYPERCUTTER","KEENEYE","BIGPECKS","OBLIVIOUS","SCRAPPY",
            // 접촉/피격 반응
            "STATIC","POISONPOINT","FLAMEBODY","EFFECTSPORE","ROUGHSKIN","IRONBARBS","GOOEY","TANGLINGHAIR","AFTERMATH",
            "ANGERPOINT","WEAKARMOR","JUSTIFIED","STAMINA","RATTLED","STEAMENGINE","THERMALEXCHANGE","DEFIANT","COMPETITIVE",
            "POISONTOUCH","STENCH","SYNCHRONIZE","WELLBAKEDBODY",
            // 매 턴/교체/격추
            "SPEEDBOOST","SHEDSKIN","MOODY","BADDREAMS","POISONHEAL","MAGICGUARD","ROCKHEAD","LIQUIDOOZE","NATURALCURE","REGENERATOR",
            "MOXIE","CHILLINGNEIGH","GRIMNEIGH","BEASTBOOST","EARLYBIRD",
            // 교체 봉쇄
            "ARENATRAP","SHADOWTAG","MAGNETPULL",
            // 특성 전체 구현(RimketBattle.Abil.cs)
            "HEAVYMETAL","LIGHTMETAL","ILLUSION",
            "DRIZZLE",
            "DAMP",
            "SANDVEIL",
            "CLOUDNINE",
            "COLORCHANGE",
            "SUCTIONCUPS",
            "SWIFTSWIM",
            "CHLOROPHYLL",
            "ILLUMINATE",
            "TRACE",
            "RAINDISH",
            "SANDSTREAM",
            "RUNAWAY",
            "PICKUP",
            "CUTECHARM",
            "PLUS",
            "MINUS",
            "FORECAST",
            "STICKYHOLD",
            "DROUGHT",
            "AIRLOCK",
            "TANGLEDFEET",
            "RIVALRY",
            "STEADFAST",
            "SNOWCLOAK",
            "GLUTTONY",
            "UNBURDEN",
            "DOWNLOAD",
            "HYDRATION",
            "SOLARPOWER",
            "NORMALIZE",
            "STALL",
            "LEAFGUARD",
            "KLUTZ",
            "ANTICIPATION",
            "FOREWARN",
            "SLOWSTART",
            "ICEBODY",
            "SNOWWARNING",
            "HONEYGATHER",
            "FRISK",
            "MULTITYPE",
            "FLOWERGIFT",
            "PICKPOCKET",
            "UNNERVE",
            "CURSEDBODY",
            "HEALER",
            "FRIENDGUARD",
            
            
            "HARVEST",
            "TELEPATHY",
            "OVERCOAT",
            "SANDRUSH",
            "WONDERSKIN",
            "IMPOSTER",
            "INFILTRATOR",
            "MUMMY",
            "MAGICBOUNCE",
            "PRANKSTER",
            "SANDFORCE",
            "ZENMODE",
            "VICTORYSTAR",
            "AROMAVEIL",
            "FLOWERVEIL",
            "CHEEKPOUCH",
            "PROTEAN",
            "MAGICIAN",
            "STANCECHANGE",
            "GALEWINGS",
            "GRASSPELT",
            "SYMBIOSIS",
            "PARENTALBOND",
            "DARKAURA",
            "FAIRYAURA",
            "AURABREAK",
            "PRIMORDIALSEA",
            "DESOLATELAND",
            "DELTASTREAM",
            "WIMPOUT",
            "EMERGENCYEXIT",
            "WATERCOMPACTION",
            "MERCILESS",
            "SHIELDSDOWN",
            "STAKEOUT",
            "BERSERK",
            "SLUSHRUSH",
            "LONGREACH",
            "LIQUIDVOICE",
            "TRIAGE",
            "SURGESURFER",
            "SCHOOLING",
            "DISGUISE",
            "BATTLEBOND",
            "POWERCONSTRUCT",
            "CORROSION",
            "COMATOSE",
            "QUEENLYMAJESTY",
            "INNARDSOUT",
            "DANCER",
            "BATTERY",
            "DAZZLING",
            "SOULHEART",
            "RECEIVER",
            "POWEROFALCHEMY",
            "RKSSYSTEM",
            "ELECTRICSURGE",
            "PSYCHICSURGE",
            "MISTYSURGE",
            "GRASSYSURGE",
            "NEUROFORCE",
            "INTREPIDSWORD",
            "DAUNTLESSSHIELD",
            "LIBERO",
            "BALLFETCH",
            "COTTONDOWN",
            "PROPELLERTAIL",
            "MIRRORARMOR",
            "GULPMISSILE",
            "STALWART",
            "SANDSPIT",
            "RIPEN",
            "ICEFACE",
            "POWERSPOT",
            "MIMICRY",
            "SCREENCLEANER",
            "STEELYSPIRIT",
            "PERISHBODY",
            "WANDERINGSPIRIT",
            "GORILLATACTICS",
            "NEUTRALIZINGGAS",
            "HUNGERSWITCH",
            "QUICKDRAW",
            "UNSEENFIST",
            "CURIOUSMEDICINE",
            "ASONECHILLINGNEIGH",
            "ASONEGRIMNEIGH",
            "ANGERSHELL",
            "ARMORTAIL",
            "BEADSOFRUIN",
            "COMMANDER",
            "COSTAR",
            "CUDCHEW",
            "ELECTROMORPHOSIS",
            "GOODASGOLD",
            "GUARDDOG",
            "HADRONENGINE",
            "LINGERINGAROMA",
            "MYCELIUMMIGHT",
            "OPPORTUNIST",
            "ORICHALCUMPULSE",
            "PROTOSYNTHESIS",
            "QUARKDRIVE",
            "SEEDSOWER",
            "SUPREMEOVERLORD",
            "SWORDOFRUIN",
            "TABLETSOFRUIN",
            "TOXICDEBRIS",
            "VESSELOFRUIN",
            "WINDPOWER",
            "WINDRIDER",
            "ZEROTOHERO",
            "SUPERSWEETSYRUP",
            "HOSPITALITY",
            "TOXICCHAIN",
            "MINDSEYE",
            "EMBODYASPECT",
            "EMBODYASPECT_1",
            "EMBODYASPECT_2",
            "EMBODYASPECT_3",
            "POISONPUPPETEER",
            "TERASHIFT",
            "TERASHELL",
            "TERAFORMZERO",
            "MEGASOL",
            "DRAGONIZE",
            "PIERCINGDRILL",
            "SPICYSPRAY",
            "FIREMANE",
            "EELEVATE",
        };

        /// <summary>구현은 되어 있지만 이 모드에서는 효과가 없는 특성(도구·성별·더블배틀·전투 밖 효과를 전제로 한다).</summary>
        public static readonly HashSet<string> Inert = new HashSet<string>
        {
            "BALLFETCH", "COMMANDER", "HONEYGATHER", "RUNAWAY"
        };

        static AbilityCatalog()
        {
            foreach (var row in GameData.AbilityRows)
            {
                var f = row.Split('|');
                byId[f[0]] = new AbilityInfo { Id = f[0], Name = f[1], KoName = f[1], Description = f.Length > 2 ? f[2] : "", KoDescription = f.Length > 2 ? f[2] : "", Implemented = Implemented.Contains(f[0]), Inert = Inert.Contains(f[0]) };
            }
        }

        public static void ApplyLanguage()
        {
            foreach (var a in byId.Values)
            {
                var en = Lang.English ? Lang.EnAbility(a.Id) : null;
                a.Name = en != null && en[1].Length > 0 ? en[1] : a.KoName;
                a.Description = en != null && en.Length > 2 && en[2].Length > 0 ? en[2] : a.KoDescription;
            }
        }

        public static int Count { get { return byId.Count; } }
        public static IEnumerable<AbilityInfo> All { get { return byId.Values; } }
        public static bool TryGet(string id, out AbilityInfo a)
        {
            a = null;
            return id != null && byId.TryGetValue(id, out a);
        }

        public static string NameOf(string id)
        {
            AbilityInfo a;
            return TryGet(id, out a) ? a.Name : (id ?? "-");
        }
    }
}
