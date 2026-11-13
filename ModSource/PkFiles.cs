using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Rimketmon
{
    /// <summary>
    /// PkAnim 폴더의 작은 텍스트 파일 6천여 개(프레임 표·색 바꾸기 표)를 한 파일로 묶은 것을 읽는다: PkAnim/pack.gz
    /// 형식(gzip 안의 UTF-8): 파일마다 "@이름(.txt 뺀 것)\n" 다음에 원래 내용. Tools/PackPkAnimBundle.py가 만든다.
    /// 묶음이 없으면 예전처럼 낱개 파일(PkAnim/이름.txt)을 읽는다.
    /// </summary>
    internal static class PkFiles
    {
        static byte[] data; static Dictionary<string, KeyValuePair<int, int>> index; static bool tried;

        static string Root { get { return Path.Combine(RimketmonMod.Instance.Content.RootDir, "PkAnim"); } }

        static void Load()
        {
            if (tried) return;
            tried = true;
            try
            {
                string path = Path.Combine(Root, "pack.gz");
                if (!File.Exists(path)) return;
                using (var f = File.OpenRead(path)) using (var gz = new GZipStream(f, CompressionMode.Decompress)) using (var ms = new MemoryStream())
                {
                    gz.CopyTo(ms); data = ms.ToArray();
                }
                index = new Dictionary<string, KeyValuePair<int, int>>();
                int i = 0, n = data.Length; string name = null; int start = 0;
                while (i < n)
                {
                    if (data[i] == (byte)'@' && (i == 0 || data[i - 1] == (byte)'\n'))
                    {
                        if (name != null) index[name] = new KeyValuePair<int, int>(start, i - start);
                        int e = Array.IndexOf(data, (byte)'\n', i); if (e < 0) e = n;
                        name = Encoding.UTF8.GetString(data, i + 1, e - i - 1).TrimEnd('\r');
                        start = Math.Min(n, e + 1); i = start; continue;
                    }
                    int nl = Array.IndexOf(data, (byte)'\n', i); i = nl < 0 ? n : nl + 1;
                }
                if (name != null) index[name] = new KeyValuePair<int, int>(start, n - start);
            }
            catch (Exception e) { Verse.Log.Warning("[림켓몬] PkAnim 묶음을 읽지 못해 낱개 파일을 씁니다: " + e.Message); data = null; index = null; }
        }

        /// <summary>name = 확장자 뺀 이름(예: f_PIKACHU, pal_f_PIKACHU, shiny_2).</summary>
        public static bool Exists(string name)
        {
            Load();
            if (index != null) return index.ContainsKey(name);
            return File.Exists(Path.Combine(Root, name + ".txt"));
        }

        /// <summary>파일 내용 전체(없으면 null).</summary>
        public static string Text(string name)
        {
            Load();
            if (index != null)
            {
                KeyValuePair<int, int> r;
                return index.TryGetValue(name, out r) ? Encoding.UTF8.GetString(data, r.Key, r.Value) : null;
            }
            string p = Path.Combine(Root, name + ".txt");
            return File.Exists(p) ? File.ReadAllText(p) : null;
        }

        public static string[] Lines(string name)
        {
            var t = Text(name);
            if (t == null) return null;
            var l = t.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < l.Length; i++) l[i] = l[i].TrimEnd('\r');
            return l;
        }
    }
}
