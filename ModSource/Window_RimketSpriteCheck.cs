#if !RIMKET_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace Rimketmon
{
    /// <summary>
    /// 개발자용: 게임에 있는 모든 동물(코어·DLC·다른 모드)을 배틀 화면과 똑같은 위치·크기 계산으로 하나씩 그려 보며 검사한다.
    /// 상대 자리(236, 84)와 내 자리(102, 발 위치 자동)에 그리고, 보이는 부분(투명 여백 제외)을 상자로 표시한다.
    /// 자동 판정: 그림 없음 / 픽셀 못 읽음 의심 / 화면 밖 / 너무 작음·큼 / 가운데 어긋남 / 내 쪽이 메시지 창에 가려짐.
    /// 결과는 세이브 폴더의 Rimketmon_sprite_check.txt 로 저장된다.
    /// </summary>
    internal sealed class Window_RimketSpriteCheck : PrWindow
    {
        sealed class Row { public ThingDef Def; public string Mod; public List<string> Issues = new List<string>(); public float FoeH, FoeW, AllyH, AllyVis; }

        private readonly List<Row> rows = new List<Row>();
        private int idx; private bool onlyBad; private string savedTo;

        public Window_RimketSpriteCheck()
        {
            foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading)
                if (RimketMech.IsCatchableRace(d) && !d.IsCorpse) rows.Add(Analyze(d));
            rows.Sort((a, b) => (b.Issues.Count > 0).CompareTo(a.Issues.Count > 0) != 0 ? (b.Issues.Count > 0).CompareTo(a.Issues.Count > 0) : string.Compare(a.Def.defName, b.Def.defName, StringComparison.Ordinal));
            savedTo = Save();
        }

        public const float FoeX = 236f, FoeY = 84f, AllyX = 102f, MsgTop = 132f;

        /// <summary>배틀 화면(Window_RimketBattle.AllyFootY)과 같은 계산.</summary>
        public static float AllyFoot(MonPic pic)
        {
            if (!pic.Valid) return 148f;
            float h = RimSprite.Bounds(pic.Tex)[3];
            return 148f - Mathf.Clamp((64f - h * pic.Scale) * 0.5f, 0f, 17f);
        }

        /// <summary>보이는 부분의 화면 상자(x, y, w, h). 배틀 그리기(c.Img: 가운데-아래 기준, Pad만큼 내림, OffX만큼 옮김)와 같은 계산.</summary>
        public static Rect ContentRect(MonPic pic, float x, float footY)
        {
            var b = RimSprite.Bounds(pic.Tex);
            float sc = pic.Scale, w = pic.Tex.width * sc, h = pic.Tex.height * sc;
            float left = x + pic.OffX - w / 2f, top = footY + pic.Pad - h;
            float bx = pic.Flip ? pic.Tex.width - b[0] - b[2] : b[0];
            return new Rect(left + bx * sc, top + b[1] * sc, b[2] * sc, b[3] * sc);
        }

        static Row Analyze(ThingDef d)
        {
            var r = new Row { Def = d, Mod = d.modContentPack != null ? d.modContentPack.Name : "?" };
            try
            {
                var foe = RimSprite.Get(d.defName, false);
                var ally = RimSprite.Get(d.defName, true);
                if (!foe.Valid || !ally.Valid) { r.Issues.Add("그림 없음(이름표로 나옴)"); return r; }
                var b = RimSprite.Bounds(foe.Tex);
                if (b[0] == 0 && b[1] == 0 && b[2] == foe.Tex.width && b[3] == foe.Tex.height && foe.Tex.width > 8) r.Issues.Add("투명 여백 0(픽셀 읽기 실패 의심)");
                var fr = ContentRect(foe, FoeX, FoeY);
                float afy = AllyFoot(ally);
                var ar = ContentRect(ally, AllyX, afy);
                r.FoeH = fr.height; r.FoeW = fr.width; r.AllyH = ar.height;
                r.AllyVis = ar.height > 0f ? Mathf.Clamp01((MsgTop - ar.y) / ar.height) : 0f;
                if (fr.xMin < 0f || fr.xMax > 320f || fr.yMin < 0f) r.Issues.Add("상대 쪽 화면 밖");
                if (ar.xMin < 0f || ar.xMax > 320f || ar.yMin < 0f) r.Issues.Add("내 쪽 화면 밖");
                if (fr.height < 14f) r.Issues.Add("너무 작음(" + fr.height.ToString("0") + ")");
                if (fr.height > 90f) r.Issues.Add("너무 큼(" + fr.height.ToString("0") + ")");
                if (Mathf.Abs(fr.center.x - FoeX) > 2f) r.Issues.Add("상대 쪽 가운데 어긋남(" + (fr.center.x - FoeX).ToString("0.0") + ")");
                if (Mathf.Abs(ar.center.x - AllyX) > 2f) r.Issues.Add("내 쪽 가운데 어긋남(" + (ar.center.x - AllyX).ToString("0.0") + ")");
                if (Mathf.Abs(fr.yMax - FoeY) > 1.5f) r.Issues.Add("상대 쪽 발이 발판에서 뜸/묻힘(" + (fr.yMax - FoeY).ToString("0.0") + ")");
                if (r.AllyVis < 0.5f) r.Issues.Add("내 쪽이 메시지 창에 가려짐(보이는 비율 " + (r.AllyVis * 100f).ToString("0") + "%)");
            }
            catch (Exception e) { r.Issues.Add("오류: " + e.GetType().Name + " " + e.Message); }
            return r;
        }

        string Save()
        {
            try
            {
                var sb = new StringBuilder();
                int bad = rows.Count(x => x.Issues.Count > 0);
                sb.AppendLine("림켓몬 동물 배틀 그림 검사  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                sb.AppendLine("동물 " + rows.Count + "종, 문제 " + bad + "종");
                sb.AppendLine();
                foreach (var r in rows)
                    sb.AppendLine((r.Issues.Count > 0 ? "[X] " : "[O] ") + r.Def.defName + " (" + r.Def.LabelCap + ", " + r.Mod + ")  상대 높이 " + r.FoeH.ToString("0") + " 폭 " + r.FoeW.ToString("0") + " / 내 쪽 높이 " + r.AllyH.ToString("0") + " 보임 " + (r.AllyVis * 100f).ToString("0") + "%" + (r.Issues.Count > 0 ? "  → " + string.Join(", ", r.Issues) : ""));
                string path = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "Rimketmon_sprite_check.txt");
                System.IO.File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                Log.Message("[림켓몬] 동물 그림 검사: " + rows.Count + "종 중 문제 " + bad + "종 → " + path);
                return path;
            }
            catch (Exception e) { Log.Warning("[림켓몬] 검사 결과 저장 실패: " + e.Message); return null; }
        }

        List<int> View()
        {
            var l = new List<int>();
            for (int i = 0; i < rows.Count; i++) if (!onlyBad || rows[i].Issues.Count > 0) l.Add(i);
            return l;
        }

        protected override void DrawCanvas(Canvas g)
        {
            g.Img(PR.Tex("arena/plains_bg") ?? PR.Tex("arena/default_bg"), 0f, 0f, 0f, 0f);
            g.Img(PR.Tex("arena/plains_b"), 20f, 0f, 0f, 0f);
            g.Img(PR.Tex("arena/plains_a"), 0f, 0f, 0f, 0f);
            var view = View();
            if (view.Count == 0) { g.Label("문제 있는 동물 없음 (F: 전체 보기)", 160f, 80f, 10f, Canvas.White, Canvas.WhiteShadow, TextAnchor.MiddleCenter); }
            else
            {
                int vi = Mathf.Clamp(view.IndexOf(idx), 0, view.Count - 1); idx = view[vi];
                var r = rows[idx];
                var foe = RimSprite.Get(r.Def.defName, false);
                var ally = RimSprite.Get(r.Def.defName, true);
                if (foe.Valid) DrawPic(g, foe, FoeX, FoeY);
                else g.Label(r.Def.LabelCap, FoeX, FoeY - 40f, 12f, Canvas.White, Canvas.WhiteShadow, TextAnchor.MiddleCenter);
                if (ally.Valid) DrawPic(g, ally, AllyX, AllyFoot(ally));
                // 기준선: 발판 높이와 가운데
                Line(g, FoeX - 30f, FoeY, 60f, 0.5f, new Color(0f, 1f, 1f, 0.7f)); Line(g, FoeX, FoeY - 4f, 0.5f, 8f, new Color(0f, 1f, 1f, 0.7f));
                Line(g, AllyX, 120f, 0.5f, 12f, new Color(0f, 1f, 1f, 0.7f));
                // 메시지 창 자리(이 아래는 배틀 중 가려진다)
                g.Fill(0f, MsgTop, 320f, 180f - MsgTop, new Color(0f, 0f, 0.1f, 0.78f));
                bool bad = r.Issues.Count > 0;
                g.Label((vi + 1) + "/" + view.Count + "  " + r.Def.LabelCap + "  (" + r.Def.defName + ")", 4f, 3f, 8f, bad ? new Color(1f, 0.55f, 0.5f) : new Color(0.6f, 1f, 0.6f), Canvas.WhiteShadow);
                g.Label(r.Mod, 4f, 13f, 6.5f, Canvas.White, Canvas.WhiteShadow);
                g.Wrap(bad ? "문제: " + string.Join(", ", r.Issues) : "정상", 4f, MsgTop + 3f, 312f, 7f, bad ? new Color(1f, 0.7f, 0.6f) : new Color(0.7f, 1f, 0.7f), Canvas.WhiteShadow);
            }
            int badAll = rows.Count(x => x.Issues.Count > 0);
            g.Label("전체 " + rows.Count + "종 · 문제 " + badAll + "종" + (onlyBad ? " · 문제만 보기" : ""), 316f, 3f, 7f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperRight);
            g.Label("←/→ 넘기기(Shift 10칸) · F 문제만/전체 · Esc 닫기", 160f, 168f, 6.5f, Canvas.White, Canvas.WhiteShadow, TextAnchor.UpperCenter);
            if (savedTo != null) g.Label("결과 파일: " + savedTo, 160f, 159f, 5.5f, new Color(0.8f, 0.8f, 0.8f), Canvas.WhiteShadow, TextAnchor.UpperCenter);

            int step = ShiftHeld ? 10 : 1;
            if (view.Count > 0)
            {
                int vi = Mathf.Clamp(view.IndexOf(idx), 0, view.Count - 1);
                if (LeftKey() || Clicked(0f, 30f, 40f, 90f)) idx = view[(vi - step % view.Count + view.Count) % view.Count];
                else if (RightKey() || Clicked(280f, 30f, 40f, 90f)) idx = view[(vi + step) % view.Count];
            }
            if (Key(KeyCode.F)) onlyBad = !onlyBad;
            if (CancelKey()) Close();
        }

        static void DrawPic(Canvas g, MonPic pic, float x, float footY)
        {
            g.Tint = pic.Tint.a > 0f ? pic.Tint : Color.white;
            g.Img(pic.Tex, x + pic.OffX, footY + pic.Pad, 0.5f, 1f, pic.Scale, 1f, pic.Flip, 0f);
            g.Tint = Color.white;
            var cr = ContentRect(pic, x, footY);
            var col = new Color(1f, 1f, 0f, 0.8f);
            Line(g, cr.x, cr.y, cr.width, 0.5f, col); Line(g, cr.x, cr.yMax, cr.width, 0.5f, col);
            Line(g, cr.x, cr.y, 0.5f, cr.height, col); Line(g, cr.xMax, cr.y, 0.5f, cr.height, col);
        }

        static void Line(Canvas g, float x, float y, float w, float h, Color c) { g.Fill(x, y, w, h, c); }
    }
}
#endif
