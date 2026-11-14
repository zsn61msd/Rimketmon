using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 언어. 게임 안 문구의 원문은 한국어이고, 다른 언어는 Languages/&lt;언어 폴더&gt;/Rimketmon/ 의 글자 파일(언어 묶음)로 바꿔 보여 준다.
    ///   language.txt  code=en / name=English               (필수)
    ///   ui.txt        한국어 원문 TAB 번역                      (화면·전투 문구 조각)
    ///   ui_exact.txt  한국어 원문 TAB 번역                      (짧은 낱말: 문장 전체가 같을 때만)
    ///   patterns.txt  한국어 정규식 TAB 번역($1…)              (문장 틀)
    ///   species.txt / moves.txt / abilities.txt / items.txt  키 TAB 이름 [TAB 설명]
    ///   natures.txt   성격 25개(순서대로) / messages.txt  포켓로그 메시지 키 TAB 문장
    /// 어느 파일이 없거나 줄이 빠져 있으면 영어 묶음(있으면) → 한국어 원문 순서로 대신 쓴다. 새 언어는 English 폴더를 복사해 고치면 된다.
    /// </summary>
    public static class Lang
    {
        public sealed class Pack
        {
            public string Code, Name, Dir;
            public readonly Dictionary<string, string> Ui = new Dictionary<string, string>(), Exact = new Dictionary<string, string>();
            public readonly List<KeyValuePair<System.Text.RegularExpressions.Regex, string>> Patterns = new List<KeyValuePair<System.Text.RegularExpressions.Regex, string>>();
            public readonly Dictionary<string, string[]> Species = new Dictionary<string, string[]>(), Moves = new Dictionary<string, string[]>(), Abilities = new Dictionary<string, string[]>(), Items = new Dictionary<string, string[]>();
            public string[] Natures; public readonly List<string> Messages = new List<string>();
            internal string[] srcs;
        }

        private static readonly List<Pack> packs = new List<Pack>();
        private static Pack cur;
        public static IList<Pack> Packs { get { return packs; } }
        /// <summary>지금 언어 코드("ko" = 원문 한국어).</summary>
        public static string Code { get { return cur == null ? "ko" : cur.Code; } }
        /// <summary>한국어가 아닌 언어로 보여 주는 중(번역 중).</summary>
        public static bool English { get { return cur != null; } }
        public static event Action Changed;

        /// <summary>언어를 고른다. "ko"(또는 없는 코드)면 한국어 원문.</summary>
        public static void Set(string code)
        {
            var p = string.IsNullOrEmpty(code) || code == "ko" ? null : packs.FirstOrDefault(x => x.Code == code);
            if (p == cur && applied) return;
            cur = p; applied = true;
            SpeciesCatalog.ApplyLanguage(); MoveCatalog.ApplyLanguage(); AbilityCatalog.ApplyLanguage(); Msgs.ResetTable();
            if (Changed != null) Changed();
        }
        /// <summary>예전 방식(영어 켜기/끄기).</summary>
        public static void Set(bool english) { Set(english ? "en" : "ko"); }
        private static bool applied;

        // ---------------------------------------------------------------- 언어 묶음 읽기
        /// <summary>Languages 폴더 아래 각 언어 폴더의 Rimketmon/language.txt 를 찾아 읽는다. 읽은 묶음 수.</summary>
        public static int LoadFolder(string languagesRoot)
        {
            int n = 0;
            if (string.IsNullOrEmpty(languagesRoot) || !Directory.Exists(languagesRoot)) return 0;
            foreach (var d in Directory.GetDirectories(languagesRoot).OrderBy(x => x))
            {
                string dir = Path.Combine(d, "Rimketmon");
                if (!File.Exists(Path.Combine(dir, "language.txt"))) continue;
                try { var p = LoadPack(dir); if (p != null) { packs.RemoveAll(x => x.Code == p.Code); packs.Add(p); n++; } }
                catch (Exception) { }
            }
            if (cur != null) { var again = packs.FirstOrDefault(x => x.Code == cur.Code); if (again != null) cur = again; }
            return n;
        }

        private static IEnumerable<string[]> Rows(string dir, string file)
        {
            string f = Path.Combine(dir, file);
            if (!File.Exists(f)) yield break;
            foreach (var raw in File.ReadAllLines(f, Encoding.UTF8))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                var parts = raw.TrimEnd('\r').Split('\t');
                for (int i = 0; i < parts.Length; i++) parts[i] = Unescape(parts[i]);
                yield return parts;
            }
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char e = s[++i];
                    sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static Pack LoadPack(string dir)
        {
            var p = new Pack { Dir = dir };
            foreach (var line in File.ReadAllLines(Path.Combine(dir, "language.txt"), Encoding.UTF8))
            {
                int eq = line.IndexOf('='); if (eq <= 0 || line.TrimStart().StartsWith("#")) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                if (k == "code") p.Code = v; else if (k == "name") p.Name = v;
            }
            if (string.IsNullOrEmpty(p.Code) || p.Code == "ko") return null;
            if (string.IsNullOrEmpty(p.Name)) p.Name = p.Code;
            foreach (var r in Rows(dir, "ui.txt")) if (r.Length >= 2 && r[0].Length > 0) p.Ui[r[0]] = r[1];
            foreach (var r in Rows(dir, "ui_exact.txt")) if (r.Length >= 2 && r[0].Length > 0) p.Exact[r[0].Trim()] = r[1].Trim();
            foreach (var r in Rows(dir, "patterns.txt")) if (r.Length >= 2 && r[0].Length > 0)
                try { p.Patterns.Add(new KeyValuePair<System.Text.RegularExpressions.Regex, string>(new System.Text.RegularExpressions.Regex(r[0]), r[1])); } catch (Exception) { }
            foreach (var r in Rows(dir, "species.txt")) if (r.Length >= 2) p.Species[r[0]] = r;
            foreach (var r in Rows(dir, "moves.txt")) if (r.Length >= 2) p.Moves[r[0]] = r;
            foreach (var r in Rows(dir, "abilities.txt")) if (r.Length >= 2) p.Abilities[r[0]] = r;
            foreach (var r in Rows(dir, "items.txt")) if (r.Length >= 2) p.Items[r[0]] = r;
            var nat = Rows(dir, "natures.txt").Select(r => r[0]).ToArray(); if (nat.Length == 25) p.Natures = nat;
            foreach (var r in Rows(dir, "messages.txt")) if (r.Length >= 2) p.Messages.Add(r[0] + "\t" + r[1]);
            p.srcs = p.Ui.Keys.OrderByDescending(k => k.Length).ToArray();
            return p;
        }

        // 지금 언어에 없으면 영어 묶음으로(둘 다 없으면 null → 한국어 원문)
        private static Pack En { get { return packs.FirstOrDefault(x => x.Code == "en"); } }
        private static string[] Find(Func<Pack, Dictionary<string, string[]>> pick, string key)
        {
            if (cur == null) return null;
            string[] r;
            if (pick(cur).TryGetValue(key, out r)) return r;
            var en = En; if (en != null && en != cur && pick(en).TryGetValue(key, out r)) return r;
            return null;
        }
        public static string[] EnSpecies(string id) { return Find(p => p.Species, Key(id)); }
        public static string[] EnMove(string id) { return Find(p => p.Moves, Key(id)); }
        public static string[] EnAbility(string id) { return Find(p => p.Abilities, Key(id)); }
        /// <summary>도구 이름·설명(번역 묶음에 있으면). [0]=id [1]=이름 [2]=설명</summary>
        public static string[] ItemText(string id) { return Find(p => p.Items, id); }
        public static string[] NatureNames { get { if (cur == null) return null; if (cur.Natures != null) return cur.Natures; var en = En; return en != null ? en.Natures : null; } }
        public static IList<string> MessageRows { get { if (cur == null) return null; if (cur.Messages.Count > 0) return cur.Messages; var en = En; return en != null && en.Messages.Count > 0 ? en.Messages : null; } }

        // ---- 영어 이름/설명 조회 (id는 영문 소문자만 남긴 키)
        public static string Key(string id)
        {
            var sb = new StringBuilder();
            foreach (char c in id.ToLowerInvariant()) if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
            string k = sb.ToString();
            string alias;
            return KeyAlias.TryGetValue(k, out alias) ? alias : k;
        }
        /// <summary>게임 데이터 id와 영어 표의 키가 다른 항목.</summary>
        private static readonly Dictionary<string, string> KeyAlias = new Dictionary<string, string>
        {
            { "nidoranfe", "nidoranf" }, { "nidoranma", "nidoranm" },
            { "asonechillingneigh", "asoneglastrier" }, { "asonegrimneigh", "asonespectrier" }, { "battlebond2", "battlebond" },
            { "embodyaspect", "embodyaspectteal" }, { "embodyaspect1", "embodyaspectwellspring" }, { "embodyaspect2", "embodyaspecthearthflame" }, { "embodyaspect3", "embodyaspectcornerstone" },
        };
        /// <summary>번역 중이면 문장 조각을 치환한다. 한국어 모드나 한글이 없는 글자는 그대로.</summary>
        public static string Tr(string s)
        {
            if (cur == null || string.IsNullOrEmpty(s)) return s;
            bool hasKo = false;
            foreach (char c in s) if (c >= 0xAC00 && c <= 0xD7A3) { hasKo = true; break; }
            if (!hasKo) return s;
            string r = TrWith(cur, s);
            var en = En;
            if (en != null && en != cur && HasKorean(r)) r = TrWith(en, r);      // 이 언어에 없는 조각은 영어로라도
            return r;
        }

        public static bool HasKorean(string s) { foreach (char c in s) if (c >= 0xAC00 && c <= 0xD7A3) return true; return false; }

        private static string TrWith(Pack p, string s)
        {
            string r;
            if (p.Ui.TryGetValue(s, out r)) return r;
            string t = s.Trim();
            if (p.Exact.TryGetValue(t, out r)) return s.Substring(0, s.IndexOf(t[0])) + r + s.Substring(s.IndexOf(t[0]) + t.Length);      // 짧은 낱말: 앞뒤 공백은 그대로
            foreach (var kv in p.Patterns) if (kv.Key.IsMatch(s)) { s = kv.Key.Replace(s, kv.Value); break; }
            // 긴 조각부터 한 번씩 치환 (이미 번역된 부분은 한글이 없어 영향 없음)
            var sb = new StringBuilder(s);
            foreach (var k in p.srcs) if (s.Contains(k)) sb.Replace(k, "\u0001" + p.Ui[k] + "\u0002");
            string o = sb.ToString().Replace("\u0001", "").Replace("\u0002", "");
            while (o.Contains("  ")) o = o.Replace("  ", " ");
            return o.Replace(" !", "!").Replace(" ,", ",").Replace(" .", ".").Replace(" '", "'");
        }
    }
}
