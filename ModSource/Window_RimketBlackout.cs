using System;
using UnityEngine;
using Verse;

namespace Rimketmon
{
    /// <summary>승부에 졌을 때: 화면이 까매지고 "○○은(는) 눈앞이 깜깜해졌다!"가 찍힌 뒤 다음 장면(센터에서 깨어나기)으로 넘어간다.</summary>
    internal sealed class Window_RimketBlackout : PrWindow
    {
        private readonly string text; private readonly Action after;
        private readonly float t0; private bool done;
        private const float FadeSec = 0.8f, TypeSec = 0.05f, HoldSec = 1.8f;

        public Window_RimketBlackout(string text, Action after)
        {
            this.text = text; this.after = after; t0 = Time.realtimeSinceStartup;
            RimketAudio.StopBgm();
        }

        protected override void DrawCanvas(Canvas g)
        {
            float t = Time.realtimeSinceStartup - t0;
            g.Fill(0f, 0f, 320f, 180f, Color.black);
            int chars = Mathf.Clamp(Mathf.FloorToInt((t - FadeSec) / TypeSec), 0, text.Length);
            if (chars > 0) g.Label(text.Substring(0, chars), 160f, 82f, 12f, Canvas.White, new Color(0.2f, 0.2f, 0.2f), TextAnchor.UpperCenter);
            float typedAt = FadeSec + text.Length * TypeSec;
            bool skip = t > typedAt + 0.3f && (AcceptKey() || Clicked(0f, 0f, 320f, 180f));
            if (!done && (t > typedAt + HoldSec || skip)) { done = true; Close(); }
        }

        public override void PostClose()
        {
            base.PostClose();
            Window_RimketBattle.LastCloseFrame = Time.frameCount;
            if (after != null) { try { after(); } catch (Exception ex) { Log.Warning("[림켓몬] 깨어나기 장면: " + ex.Message); } }
        }
    }
}
