using System;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 림켓몬 없이 접속할 때의 경고창. 정착민 스탯, 상대 정보, 엔진 시뮬레이션으로 추정한 위험도를 보여주고
    /// 플레이어가 직접 접속할지 취소할지 결정하게 한다.
    /// </summary>
    public class Window_RimketWarning : Window
    {
        private readonly RimketCreature wild;
        private readonly ColonistCombatant col;
        private readonly EngineSettings settings;
        private readonly Action onConfirm;
        private readonly RiskEstimate risk;

        public Window_RimketWarning(RimketCreature wild, ColonistCombatant col, EngineSettings settings, Action onConfirm)
        {
            this.wild = wild;
            this.col = col;
            this.settings = settings;
            this.onConfirm = onConfirm;
            // 게임 난수 상태를 건드리지 않도록 별도 난수로 추정한다.
            this.risk = RiskEstimator.Estimate(wild, col, settings, 400, new SystemRng());

            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            doCloseX = false;
            doCloseButton = false;
        }

        public override Vector2 InitialSize { get { return new Vector2(620f, 560f); } }

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            GUI.color = new Color(1f, 0.75f, 0.3f);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 34f), "⚠ 싸울 림켓몬 없음");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 38f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 44f),
                col.Name + "이(가) 직접 싸웁니다.");
            y += 52f;

            float half = (inRect.width - 12f) / 2f;
            DrawColonist(new Rect(inRect.x, y, half, 190f));
            DrawWild(new Rect(inRect.x + half + 12f, y, half, 190f));
            y += 200f;

            DrawRisk(new Rect(inRect.x, y, inRect.width, 120f));

            float bw = (inRect.width - 12f) / 2f;
            if (Widgets.ButtonText(new Rect(inRect.x, inRect.yMax - 36f, bw, 36f), "취소 (볼은 소모되지 않음)"))
            {
                Close();
            }
            GUI.color = risk.DeathRate > 0.5 ? new Color(1f, 0.5f, 0.5f) : Color.white;
            if (Widgets.ButtonText(new Rect(inRect.x + bw + 12f, inRect.yMax - 36f, bw, 36f), "위험을 감수하고 접속"))
            {
                Close();
                onConfirm();
            }
            GUI.color = Color.white;
        }

        private void DrawColonist(Rect r)
        {
            Widgets.DrawMenuSection(r);
            var i = r.ContractedBy(8f);
            Row(ref i, "정착민", col.Name, true);
            Row(ref i, "정신감응력", col.PsychicSensitivity.ToString("P0"));
            Row(ref i, "최대 HP", col.MaxHp.ToString());
            Row(ref i, "초능력 레벨", col.PsylinkLevel.ToString());
            Row(ref i, "총 스탯 / 각 스탯", col.Pool.ToString("F0") + " / " + col.StatValue + " (상한 120)");
            Row(ref i, "전투 레벨", "Lv." + col.Level);
            Row(ref i, "기본 공격", "물리 · 위력 " + col.BasicAttackPower);
        }

        private void DrawWild(Rect r)
        {
            Widgets.DrawMenuSection(r);
            var i = r.ContractedBy(8f);
            string types = TypeChart.KoreanName(wild.Type1) + (wild.Type2.HasValue ? "/" + TypeChart.KoreanName(wild.Type2.Value) : "");
            Row(ref i, "상대", "야생 " + wild.Name, true);
            Row(ref i, "티어", wild.Tier + (wild.IsUpgraded ? " ★특이 개체" : ""));
            Row(ref i, "속성", types);
            Row(ref i, "레벨", "Lv." + wild.Level);
            Row(ref i, "HP", wild.MaxHp.ToString());
            Row(ref i, "공격 / 특공", wild.Atk + " / " + wild.SpA);
            Row(ref i, "방어 / 특방 / 속도", wild.Def + " / " + wild.SpD + " / " + wild.Spe);
        }

        private void DrawRisk(Rect r)
        {
            Widgets.DrawMenuSection(r);
            var i = r.ContractedBy(8f);
            Row(ref i, "예상 결과 (모의전 " + risk.Trials + "회)", "", true);
            Row(ref i, "승리", risk.WinRate.ToString("P0"));
            Row(ref i, "접속 해제로 이탈", risk.DisconnectRate.ToString("P0") + "  (평균 뇌 손상 " + risk.AverageBrainDamage.ToString("P0") + ")");

            GUI.color = risk.DeathRate > 0.5 ? new Color(1f, 0.4f, 0.4f) : (risk.DeathRate > 0.2 ? new Color(1f, 0.7f, 0.3f) : Color.white);
            Row(ref i, settings.LethalDefeat ? "패배 시 사망" : "패배 시 혼수·중증 손상", risk.DefeatRate.ToString("P0"));
            GUI.color = Color.white;
            Row(ref i, "영구 후유증이 남을 확률", risk.ScarRate.ToString("P0"));
        }

        private static void Row(ref Rect area, string label, string value, bool header = false)
        {
            const float h = 24f;
            if (header)
            {
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(area.x, area.y, area.width, h), label + (value.Length > 0 ? ": " + value : ""));
            }
            else
            {
                Widgets.Label(new Rect(area.x, area.y, area.width * 0.45f, h), label);
                Widgets.Label(new Rect(area.x + area.width * 0.45f, area.y, area.width * 0.55f, h), value);
            }
            area.y += h;
        }
    }
}
