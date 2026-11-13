using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>림월드 동물의 실제 색(색필터)을 읽고 저장하는 도우미. "RRGGBB|RRGGBB" 문자열로 림켓몬에 붙는다.</summary>
    internal static class RimketLook
    {
        /// <summary>폰이 지금 그려지는 몸 그래픽의 색(1색, 2색). 읽을 수 없으면 null.</summary>
        public static string Capture(Pawn p)
        {
            try
            {
                if (p == null || p.Drawer == null || p.Drawer.renderer == null) return null;
                var r = p.Drawer.renderer;
                r.EnsureGraphicsInitialized();
                var g = r.BodyGraphic;
                if (g == null) return null;
                // 말처럼 '대체 그림'(Horse1~6)으로 색이 정해지는 동물: 림월드에 직접 물어 그 그림 경로를 적는다(엔진마다 BodyGraphic 경로가 기본 그림일 수 있음)
                try
                {
                    AlternateGraphic ag; int idx;
                    if (PawnGraphicUtils.TryGetAlternate(p, out ag, out idx) && ag != null)
                    {
                        var ab = ag.GetGraphic(g);
                        if (ab != null && !string.IsNullOrEmpty(ab.path)) return Hex(ab.Color) + "|" + Hex(ab.ColorTwo) + "|" + ab.path + "|c";
                    }
                }
                catch (System.Exception) { }
                // 색만으로는 부족하다: 같은 종이라도 개체마다 다른 그림(무늬)을 쓰는 동물이 있어서, 그림 경로까지 적어 둔다
                return Hex(g.Color) + "|" + Hex(g.ColorTwo) + (string.IsNullOrEmpty(g.path) ? "" : "|" + g.path + "|c");      // c: 대체 그림까지 확인하고 적음
            }
            catch (System.Exception) { return null; }
        }

        /// <summary>
        /// 그림 경로가 적혀 있지 않은 동물 림켓몬(사냥·보상·개발자 메뉴·예전 저장 등)에 무늬를 한 번 정해 적는다.
        /// 원래 색은 알 수 없으므로 림월드와 같은 확률로 대체 그림 중 하나(또는 기본 그림)를 고르고, 그 뒤로는 고정된다.
        /// </summary>
        public static void EnsureLook(RimketCreature c)
        {
            if (c == null || c.Neutral || c.PawnId != 0 || string.IsNullOrEmpty(c.SpeciesId)) return;
            string had = Path(c.Look);
            if (had != null && c.Look.Split('|').Length >= 4) return;      // 확인된 무늬(c)·이미 정한 무늬(r)
            try
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(c.SpeciesId);
                if (def == null || def.race == null || !def.race.Animal) return;
                var kind = def.race.AnyPawnKind;
                if (kind == null || kind.alternateGraphics == null || kind.alternateGraphics.Count == 0 || kind.lifeStages == null || kind.lifeStages.Count == 0) return;
                var data = kind.lifeStages[kind.lifeStages.Count - 1].bodyGraphicData;
                if (data == null) return;
                if (had != null && had != data.texPath) return;      // 대체 그림 경로가 이미 적혀 있으면 그대로
                string path = data.texPath; Color col = data.color, col2 = data.colorTwo;
                if (Rand.Value < kind.alternateGraphicChance)
                {
                    var alt = kind.alternateGraphics.RandomElementByWeight(a => a.Weight);
                    var ag = alt != null ? alt.GetGraphic(data.Graphic) : null;
                    if (ag != null && !string.IsNullOrEmpty(ag.path)) { path = ag.path; col = ag.Color; col2 = ag.ColorTwo; }
                }
                if (string.IsNullOrEmpty(path)) return;
                c.Look = Hex(col) + "|" + Hex(col2) + "|" + path + "|r";      // r: 기록이 없어 새로 정한 무늬(이후 고정)
            }
            catch (System.Exception) { }
        }

        /// <summary>림켓몬에 색을 붙인다(없으면 그대로).</summary>
        public static void Apply(RimketCreature c, Pawn p)
        {
            var s = Capture(p);
            if (c != null && s != null) c.Look = s;
        }

        static string Hex(Color c)
        {
            return ((int)(Mathf.Clamp01(c.r) * 255f + 0.5f)).ToString("X2") + ((int)(Mathf.Clamp01(c.g) * 255f + 0.5f)).ToString("X2") + ((int)(Mathf.Clamp01(c.b) * 255f + 0.5f)).ToString("X2");
        }

        /// <summary>잡을 때 적어 둔 그림 경로(없으면 null).</summary>
        public static string Path(string look)
        {
            if (string.IsNullOrEmpty(look)) return null;
            var parts = look.Split('|');
            return parts.Length >= 3 && parts[2].Length > 0 ? parts[2] : null;
        }

        public static bool TryParseTwo(string look, out Color col)
        {
            col = Color.white;
            if (string.IsNullOrEmpty(look)) return false;
            var parts = look.Split('|');
            if (parts.Length < 3 || parts[1].Length != 6) return false;
            try { col = new Color(System.Convert.ToInt32(parts[1].Substring(0, 2), 16) / 255f, System.Convert.ToInt32(parts[1].Substring(2, 2), 16) / 255f, System.Convert.ToInt32(parts[1].Substring(4, 2), 16) / 255f, 1f); return true; }
            catch (System.Exception) { return false; }
        }

        public static bool TryParse(string look, out Color col)
        {
            col = Color.white;
            if (string.IsNullOrEmpty(look)) return false;
            var parts = look.Split('|');
            string first = parts[0];
            if (first.Length != 6) return false;
            // 예전 저장(그림 경로 없이 색만 적던 때)의 흰색은 믿지 않는다: 무늬가 다른 그림의 색이었을 수 있어 기본 그림에 씌우면 하얗게 나온다
            if (parts.Length < 3 && first == "FFFFFF") return false;
            try
            {
                col = new Color(System.Convert.ToInt32(first.Substring(0, 2), 16) / 255f, System.Convert.ToInt32(first.Substring(2, 2), 16) / 255f, System.Convert.ToInt32(first.Substring(4, 2), 16) / 255f, 1f);
                return true;
            }
            catch (System.Exception) { return false; }
        }
    }
}
