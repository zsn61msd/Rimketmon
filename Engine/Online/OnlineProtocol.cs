using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Rimketmon.Engine
{
    /// <summary>온라인 대전용 시드 고정 난수(SplitMix64). System.Random과 달리 어느 컴퓨터에서든 같은 시드면 같은 수열이다.</summary>
    public sealed class SeededRng : IRng
    {
        private ulong x;
        public SeededRng(int seed) { x = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x1234567UL; }
        private ulong NextU64()
        {
            ulong z = (x += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        public double NextDouble() { return (NextU64() >> 11) * (1.0 / 9007199254740992.0); }
        public int Next(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextDouble() * (maxExclusive - minInclusive));
        }
    }

    /// <summary>온라인 대전의 데이터 약속: 림켓몬 직렬화/검증, 행동 부호, 방 코드, 버전 지문.</summary>
    public static class OnlineProtocol
    {
        public const int BattleLevel = RankedLadder.BattleLevel;
        public const int DoubleBringCount = 4;

        /// <summary>모드 빌드가 같은지 확인하는 지문(어셈블리 고유 번호). 다르면 규칙이 어긋날 수 있어 대전을 막는다.</summary>
        public static string Fingerprint
        {
            get { return typeof(OnlineProtocol).Assembly.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 12); }
        }

        public static int BringCount(bool dbl) { return dbl ? DoubleBringCount : RankedRules.BringCount; }

        /// <summary>서로 다른 종족·도구 n마리를 고를 수 있는 파티인가(싱글 3, 더블 4).</summary>
        public static bool CanForm(IList<RimketCreature> party, int n)
        {
            return Form(party, n, new List<int>(), 0);
        }

        private static bool Form(IList<RimketCreature> party, int n, List<int> chosen, int from)
        {
            if (chosen.Count == n) return true;
            for (int i = from; i < party.Count; i++)
            {
                if (RankedRules.ClauseViolation(party, chosen, i) != null) continue;
                chosen.Add(i);
                bool ok = Form(party, n, chosen, i + 1);
                chosen.RemoveAt(chosen.Count - 1);
                if (ok) return true;
            }
            return false;
        }

        /// <summary>온라인에 낼 수 없는 림켓몬이 파티에 있으면 이유 문장(없으면 null). 상대 쪽 검사와 같은 규칙을 먼저 적용한다.</summary>
        public static string TeamProblem(IList<RimketCreature> party)
        {
            foreach (var c in party)
            {
                string err; var back = Decode(Encode(c), out err);
                if (back == null) return c.Name + ": " + err;
            }
            return null;
        }

        // ------------------------------------------------------------------ 방 코드

        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";     // 헷갈리는 글자(I, O, 0, 1)는 뺀다
        public const int CodeLength = 6;

        public static string NewCode(IRng rng)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < CodeLength; i++) sb.Append(CodeAlphabet[rng.Next(0, CodeAlphabet.Length)]);
            return sb.ToString();
        }

        /// <summary>입력한 코드를 정리한다(공백 제거, 대문자). 형식이 틀리면 null.</summary>
        public static string NormalizeCode(string s)
        {
            if (s == null) return null;
            var sb = new StringBuilder();
            foreach (char ch in s.ToUpperInvariant()) { if (ch == ' ' || ch == '-') continue; sb.Append(ch); }
            string c = sb.ToString();
            if (c.Length != CodeLength) return null;
            foreach (char ch in c) if (CodeAlphabet.IndexOf(ch) < 0) return null;
            return c;
        }

        // ------------------------------------------------------------------ 림켓몬 직렬화

        private static string Clean(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (char ch in s) sb.Append(ch == '|' || ch == ';' || ch == '=' || ch == ',' || ch == ':' || ch == '\n' || ch == '\r' ? ' ' : ch);
            return sb.ToString();
        }

        public static string Encode(RimketCreature c)
        {
            var kv = new List<string>
            {
                "sp=" + Clean(c.SpeciesId), "s=" + Clean(c.Species), "n=" + Clean(c.Name), "g=" + c.Gender,
                "i=" + Clean(c.Item), "a=" + Clean(c.Ability), "t=" + c.Tier, "bt=" + c.BaseTier, "nat=" + c.Nature,
                "t1=" + (int)c.Type1, "t2=" + (c.Type2.HasValue ? ((int)c.Type2.Value).ToString() : "-"),
                "bs=" + string.Join(",", new[] { c.BaseHp, c.BaseAtk, c.BaseDef, c.BaseSpA, c.BaseSpD, c.BaseSpe }),
                "iv=" + string.Join(",", c.Iv), "pt=" + string.Join(",", c.Sp), "lk=" + Clean(c.Look), "nu=" + (c.Neutral ? 1 : 0), "wd=" + (Paradox.HasWedge(c) ? 1 : 0),
                "mv=" + string.Join(",", c.Moves.Select(m => Clean(m.Move.Id) + ":" + m.PP))
            };
            return string.Join(";", kv);
        }

        private static int[] Ints(string s, int n)
        {
            var p = s.Split(',');
            if (p.Length != n) return null;
            var r = new int[n];
            for (int i = 0; i < n; i++) if (!int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out r[i])) return null;
            return r;
        }

        /// <summary>받은 림켓몬을 검사해서 만든다. 문제가 있으면 null과 이유. 규칙 밖 값(능력 포인트 초과 등)은 거부한다.</summary>
        public static RimketCreature Decode(string data, out string error)
        {
            error = null;
            var d = new Dictionary<string, string>();
            foreach (var part in data.Split(';')) { int e = part.IndexOf('='); if (e > 0) d[part.Substring(0, e)] = part.Substring(e + 1); }
            Func<string, string> g = k => d.ContainsKey(k) ? d[k] : "";
            Func<string, int, int> gi = (k, def) => { int v; return int.TryParse(g(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def; };
            var c = new RimketCreature
            {
                SpeciesId = g("sp"), Species = g("s"), Name = g("n"), Gender = gi("g", 2), Item = g("i").Length == 0 ? null : g("i"),
                Ability = g("a").Length == 0 ? null : g("a"), Tier = gi("t", 0), BaseTier = gi("bt", 0), Nature = gi("nat", 0), Look = g("lk"), Neutral = g("nu") == "1",
                WedgeBackup = g("wd") == "1" ? "remote" : null,      // 상대 쪽 쐐기 표시용(되돌리기 정보는 보내지 않는다)
                Level = BattleLevel
            };
            int t1 = gi("t1", -1);
            if (t1 < 0 || t1 >= Enum.GetValues(typeof(ElementType)).Length) { error = "타입 정보 오류"; return null; }
            c.Type1 = (ElementType)t1;
            if (g("t2") != "-") { int t2 = gi("t2", -1); if (t2 < 0 || t2 >= Enum.GetValues(typeof(ElementType)).Length) { error = "타입 정보 오류"; return null; } c.Type2 = (ElementType)t2; }
            var bs = Ints(g("bs"), 6); var iv = Ints(g("iv"), 6); var pt = Ints(g("pt"), 6);
            if (bs == null || iv == null || pt == null) { error = "능력치 정보 오류"; return null; }
            if (bs.Any(v => v < 1 || v > 255)) { error = "종족값 범위 오류"; return null; }
            if (iv.Any(v => v < 0 || v > 31)) { error = "개체값 범위 오류"; return null; }
            if (pt.Any(v => v < 0 || v > RimketCreature.SpStatMax) || pt.Sum() > RimketCreature.SpTotalMax) { error = "능력 포인트가 규칙을 넘는다"; return null; }
            c.BaseHp = bs[0]; c.BaseAtk = bs[1]; c.BaseDef = bs[2]; c.BaseSpA = bs[3]; c.BaseSpD = bs[4]; c.BaseSpe = bs[5];
            c.Iv = iv; c.Sp = pt;
            if (string.IsNullOrEmpty(c.Species) || string.IsNullOrEmpty(c.Name)) { error = "종족 정보 없음"; return null; }
            if (c.Gender < 0 || c.Gender > 2) { error = "성별 오류"; return null; }
            if (c.Nature < 0 || c.Nature > 24) { error = "성격 오류"; return null; }
            // 사람·메카노이드·모드 종족·?티어 모두 낼 수 있다(그림은 따로 보낸다). 전투에 쓰는 값은 이 메시지에 다 들어 있다
            if (c.Tier < 0 || c.Tier > EngineSettings.TierCount || c.BaseTier < 0 || c.BaseTier > EngineSettings.TierCount) { error = "티어 오류"; return null; }
            SpeciesInfo sp;
            bool person = c.Species == "PERSON";
            if (!person && !SpeciesCatalog.TryGet(c.Species, out sp) && !MysteryGen.IsMystery(c.Species)) { error = "내 쪽에 없는 종족(" + c.Species + ") — 서로 같은 버전이어야 한다"; return null; }
            if (c.Item != null && ItemCatalog.Get(c.Item) == null) { error = "알 수 없는 도구"; return null; }
            AbilityInfo ab;
            if (c.Ability != null && !AbilityCatalog.TryGet(c.Ability, out ab)) { error = "알 수 없는 특성"; return null; }
            var mv = g("mv");
            if (mv.Length == 0) { error = "기술이 없다"; return null; }
            foreach (var m in mv.Split(','))
            {
                var p = m.Split(':'); int pp;
                Move mo;
                if (p.Length != 2 || !int.TryParse(p[1], out pp) || !MoveCatalog.TryGet(p[0], out mo)) { error = "알 수 없는 기술(" + m + ")"; return null; }
                c.Moves.Add(new MoveSlot(mo) { PP = mo.MaxPP });
            }
            if (c.Moves.Count > 4) { error = "기술이 너무 많다"; return null; }
            c.RecalculateStats(); c.Hp = c.MaxHp;
            return c;
        }

        // ------------------------------------------------------------------ 행동 부호

        public static string EncodeAction(BattleAction? a)
        {
            if (!a.HasValue) return "-";
            return (int)a.Value.Kind + "." + a.Value.Index + "." + a.Value.Target;
        }

        public static bool DecodeAction(string s, out BattleAction? a)
        {
            a = null;
            if (s == "-") return true;
            var p = s.Split('.'); int k, i, t;
            if (p.Length != 3 || !int.TryParse(p[0], out k) || !int.TryParse(p[1], out i) || !int.TryParse(p[2], out t)) return false;
            if (k < 0 || k > (int)ActionKind.UseItem || k == (int)ActionKind.UseItem) return false;      // 온라인은 가방 아이템 금지
            a = new BattleAction((ActionKind)k, i, t);
            return true;
        }

        /// <summary>두 컴퓨터의 전투 상태가 같은지 비교하는 짧은 해시.</summary>
        public static string StateHash(RimketBattle b)
        {
            uint h = 2166136261;
            Action<int> add = v => { unchecked { h = (h ^ (uint)v) * 16777619; } };
            add(b.Turn); add(b.Log.Count);
            foreach (var side in new[] { b.Player, b.Foe })
                foreach (var c in side.Party)
                {
                    add(c.Hp); var f = b.PeekFighter(c); add(f == null ? -1 : (int)f.Status);
                    foreach (var m in c.Moves) add(m.PP);
                }
            return h.ToString("x8");
        }
    }
}
