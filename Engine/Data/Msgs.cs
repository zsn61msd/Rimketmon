using System;
using System.Collections.Generic;
using System.Text;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 한국어 조사 처리 + PokeRogue 번역 템플릿 조회.
    /// 템플릿 형식: "{{pokemonName}}[[는]] 반동으로 …"  →  Msgs.T("move-trigger.hitWithRecoil", "pokemonName", "피카츄")
    /// 코드에 직접 쓴 "은(는)", "이(가)", "을(를)", "와(과)", "(으)로" 도 Josa()로 정리한다.
    /// </summary>
    public static class Msgs
    {
        static Dictionary<string, string> table;

        static Dictionary<string, string> Table
        {
            get
            {
                if (table != null) return table;
                var t = new Dictionary<string, string>();
                foreach (var r in Lang.English ? EnData.Msgs : MsgData.Rows)
                {
                    int i = r.IndexOf('\t');
                    if (i > 0) t[r.Substring(0, i)] = r.Substring(i + 1);
                }
                return table = t;
            }
        }

        public static void ResetTable() { table = null; }

        public static bool Has(string key) { return Table.ContainsKey(key); }

        /// <summary>키가 없으면 빈 문자열.</summary>
        public static string T(string key, params string[] kv)
        {
            string s;
            if (!Table.TryGetValue(key, out s)) return "";
            var sb = new StringBuilder(s);
            for (int i = 0; i + 1 < kv.Length; i += 2) sb.Replace("{{" + kv[i] + "}}", kv[i + 1]);
            return Josa(sb.ToString());
        }

        // ---- 받침 판정 ----
        static bool HasBatchim(char c, out bool rieul)
        {
            rieul = false;
            if (c >= 0xAC00 && c <= 0xD7A3)
            {
                int jong = (c - 0xAC00) % 28;
                rieul = jong == 8;
                return jong != 0;
            }
            if (c >= '0' && c <= '9') { rieul = c == '1' || c == '8'; return "013678".IndexOf(c) >= 0; }   // 영/일/삼/육/칠/팔
            return false;
        }

        static char LastWordChar(string s, int end)
        {
            for (int i = end - 1; i >= 0; i--)
            {
                char c = s[i];
                if (c == ' ' || c == ')' || c == ']' || c == '!' || c == '?' || c == '.' || c == '"' || c == '\'' || c == '~') continue;
                return c;
            }
            return ' ';
        }

        static readonly string[][] Pairs =
        {
            // 코드 표기, 받침 있음, 받침 없음, (ㄹ 받침 예외)
            new[]{ "은(는)", "은", "는", null },
            new[]{ "이(가)", "이", "가", null },
            new[]{ "을(를)", "을", "를", null },
            new[]{ "와(과)", "과", "와", null },
            new[]{ "(으)로", "으로", "로", "로" },
            new[]{ "[[는]]", "은", "는", null }, new[]{ "[[은]]", "은", "는", null },
            new[]{ "[[가]]", "이", "가", null }, new[]{ "[[이]]", "이", "가", null },
            new[]{ "[[를]]", "을", "를", null }, new[]{ "[[을]]", "을", "를", null },
            new[]{ "[[와]]", "과", "와", null }, new[]{ "[[과]]", "과", "와", null },
            new[]{ "[[로]]", "으로", "로", "로" }, new[]{ "[[으로]]", "으로", "로", "로" },
            new[]{ "[[야]]", "아", "야", null }, new[]{ "[[아]]", "아", "야", null },
        };

        public static string Josa(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf('(') < 0 && s.IndexOf("[[", StringComparison.Ordinal) < 0) return s;
            if (Lang.English) { foreach (var p in Pairs) s = s.Replace(p[0], ""); return s; }    // 영어에는 조사가 없다
            foreach (var p in Pairs)
            {
                int at = 0;
                while ((at = s.IndexOf(p[0], at, StringComparison.Ordinal)) >= 0)
                {
                    bool rieul;
                    bool has = HasBatchim(LastWordChar(s, at), out rieul);
                    string rep = has ? (rieul && p[3] != null ? p[3] : p[1]) : p[2];
                    s = s.Substring(0, at) + rep + s.Substring(at + p[0].Length);
                    at += rep.Length;
                }
            }
            return s;
        }
    }
}
