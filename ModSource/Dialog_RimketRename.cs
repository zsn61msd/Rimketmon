using System;
using UnityEngine;
using Verse;

namespace Rimketmon
{
    /// <summary>센터 이름 짓기 아저씨: 림켓몬 이름 입력 창(최대 12자, 빈칸이면 바꾸지 않음).</summary>
    internal class Dialog_RimketRename : Window
    {
        const int MaxLen = 12;
        string name; readonly Action<string> done; readonly Action cancel; bool focused, finished;

        public Dialog_RimketRename(string cur, Action<string> done, Action cancel)
        {
            name = cur ?? ""; this.done = done; this.cancel = cancel;
            doCloseX = true; forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false;
            layer = WindowLayer.Super;      // 림켓몬 메뉴(최상단 층) 앞에 떠야 보이고 입력된다
        }

        public override Vector2 InitialSize { get { return new Vector2(320f, 150f); } }

        public override void DoWindowContents(Rect r)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 0f, r.width, 28f), "새 이름 (최대 " + MaxLen + "자)");
            GUI.SetNextControlName("RimketRenameField");
            string t = Widgets.TextField(new Rect(0f, 32f, r.width, 32f), name);
            if (t.Length > MaxLen) t = t.Substring(0, MaxLen);
            name = t;
            if (!focused) { UI.FocusControl("RimketRenameField", this); focused = true; }
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (Widgets.ButtonText(new Rect(0f, r.height - 34f, r.width / 2f - 4f, 34f), "확인") || enter)
            {
                if (enter) Event.current.Use();
                finished = true; Close();
                done?.Invoke(name.Trim());
            }
            if (Widgets.ButtonText(new Rect(r.width / 2f + 4f, r.height - 34f, r.width / 2f - 4f, 34f), "취소")) Close();
        }

        public override void PostClose()
        {
            base.PostClose();
            if (!finished) cancel?.Invoke();
        }
    }
}
