using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 평평한 객체(림켓몬·정착민 전투원·엔진 설정 등)를 문자열로 바꾸고 되돌린다. 모든 인스턴스 필드(비공개 포함)를 담는다.
    /// 지원: 문자열·정수·실수·bool·열거형(Nullable 포함)·int[]·double[]·string[]·List&lt;string&gt;·List&lt;MoveSlot&gt;. 그 밖의 형식은 건너뛴다(Skipped에 남김).
    /// 세이브 중 배틀 이어 하기에서 '시작 상태'를 정확히 남기는 데 쓴다(엔진 테스트로 왕복이 같은지 확인).
    /// </summary>
    public static class FlatCodec
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public static readonly HashSet<string> Skipped = new HashSet<string>();

        static IEnumerable<FieldInfo> Fields(Type t)
        {
            for (var x = t; x != null && x != typeof(object); x = x.BaseType)
                foreach (var f in x.GetFields(F | BindingFlags.DeclaredOnly))
                    if (!f.IsInitOnly && !f.IsLiteral) yield return f;
        }

        public static string Encode(object o, IEnumerable<string> only = null)
        {
            if (o == null) return "";
            var set = only != null ? new HashSet<string>(only) : null;
            var sb = new StringBuilder();
            foreach (var f in Fields(o.GetType()))
            {
                string name = f.Name; if (set != null && !set.Contains(Clean(name))) continue;
                string v; if (!TryEnc(f.FieldType, f.GetValue(o), out v)) { Skipped.Add(o.GetType().Name + "." + name); continue; }
                sb.Append(Esc(name)).Append('=').Append(v == null ? "\u0001" : Esc(v)).Append('\u0002');
            }
            return sb.ToString();
        }

        public static void Decode(object o, string text)
        {
            if (o == null || string.IsNullOrEmpty(text)) return;
            var map = new Dictionary<string, string>();
            foreach (var part in text.Split('\u0002'))
            {
                if (part.Length == 0) continue;
                int i = part.IndexOf('='); if (i < 0) continue;
                string val = part.Substring(i + 1);
                map[Unesc(part.Substring(0, i))] = val == "\u0001" ? null : Unesc(val);
            }
            foreach (var f in Fields(o.GetType()))
            {
                string v; if (!map.TryGetValue(f.Name, out v)) continue;
                object r; if (TryDec(f.FieldType, v, out r)) f.SetValue(o, r);
            }
        }

        /// <summary>자동 속성의 숨은 필드 이름(&lt;Name&gt;k__BackingField) → Name.</summary>
        static string Clean(string n) { return n.StartsWith("<") ? n.Substring(1, n.IndexOf('>') - 1) : n; }

        static string Esc(string s) { return s.Replace("\\", "\\\\").Replace("\u0002", "\\b").Replace("=", "\\e"); }
        static string Unesc(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length) { char c = s[++i]; sb.Append(c == 'b' ? '\u0002' : c == 'e' ? '=' : c); }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static bool TryEnc(Type t, object v, out string s)
        {
            s = null;
            var u = Nullable.GetUnderlyingType(t);
            if (u != null) { if (v == null) return true; t = u; }
            if (t == typeof(string)) { s = (string)v; return true; }
            if (t.IsEnum) { s = Convert.ToInt64(v).ToString(Inv); return true; }
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)) { s = Convert.ToInt64(v).ToString(Inv); return true; }
            if (t == typeof(bool)) { s = (bool)v ? "1" : "0"; return true; }
            if (t == typeof(double)) { s = ((double)v).ToString("R", Inv); return true; }
            if (t == typeof(float)) { s = ((float)v).ToString("R", Inv); return true; }
            if (t == typeof(int[])) { if (v != null) s = string.Join(",", (int[])v); return true; }
            if (t == typeof(double[])) { if (v != null) s = string.Join(",", ((double[])v).Select(d => d.ToString("R", Inv))); return true; }
            if (t == typeof(string[])) { if (v != null) s = string.Join("\u0003", ((string[])v).Select(x => x ?? "\u0004")); return true; }
            if (t == typeof(List<string>)) { if (v != null) s = string.Join("\u0003", ((List<string>)v).Select(x => x ?? "\u0004")); return true; }
            if (t == typeof(List<MoveSlot>))
            {
                if (v != null) s = string.Join("\u0003", ((List<MoveSlot>)v).Select(m => (m.Move != null ? m.Move.Id : "") + ":" + m.PP));
                return true;
            }
            return false;
        }

        static bool TryDec(Type t, string s, out object r)
        {
            r = null;
            var u = Nullable.GetUnderlyingType(t);
            if (u != null) { if (s == null) return true; t = u; }
            if (t == typeof(string)) { r = s; return true; }
            if (s == null) { return !t.IsValueType; }
            if (t.IsEnum) { r = Enum.ToObject(t, long.Parse(s, Inv)); return true; }
            if (t == typeof(int)) { r = int.Parse(s, Inv); return true; }
            if (t == typeof(long)) { r = long.Parse(s, Inv); return true; }
            if (t == typeof(short)) { r = short.Parse(s, Inv); return true; }
            if (t == typeof(byte)) { r = byte.Parse(s, Inv); return true; }
            if (t == typeof(bool)) { r = s == "1"; return true; }
            if (t == typeof(double)) { r = double.Parse(s, Inv); return true; }
            if (t == typeof(float)) { r = float.Parse(s, Inv); return true; }
            if (t == typeof(int[])) { r = s.Length == 0 ? new int[0] : s.Split(',').Select(x => int.Parse(x, Inv)).ToArray(); return true; }
            if (t == typeof(double[])) { r = s.Length == 0 ? new double[0] : s.Split(',').Select(x => double.Parse(x, Inv)).ToArray(); return true; }
            if (t == typeof(string[])) { r = s.Length == 0 ? new string[0] : s.Split('\u0003').Select(x => x == "\u0004" ? null : x).ToArray(); return true; }
            if (t == typeof(List<string>)) { r = s.Length == 0 ? new List<string>() : s.Split('\u0003').Select(x => x == "\u0004" ? null : x).ToList(); return true; }
            if (t == typeof(List<MoveSlot>))
            {
                var l = new List<MoveSlot>();
                if (s.Length > 0)
                    foreach (var part in s.Split('\u0003'))
                    {
                        int i = part.LastIndexOf(':'); string id = part.Substring(0, i);
                        Move m; try { m = MoveCatalog.Get(id); } catch (Exception) { continue; }
                        l.Add(new MoveSlot(m) { PP = int.Parse(part.Substring(i + 1), Inv) });
                    }
                r = l; return true;
            }
            return false;
        }

        // ---- 편의
        public static string EncodeCreature(RimketCreature c) { return Encode(c); }
        public static RimketCreature DecodeCreature(string s) { if (string.IsNullOrEmpty(s)) return null; var c = new RimketCreature(); Decode(c, s); return c; }
        /// <summary>살아 있는 림켓몬 객체에 저장된 상태를 그대로 덮어쓴다(다른 곳이 이 객체를 가리키고 있어도 그대로 유지).</summary>
        public static void Overwrite(RimketCreature live, string s) { if (live != null && !string.IsNullOrEmpty(s)) Decode(live, s); }
    }
}
