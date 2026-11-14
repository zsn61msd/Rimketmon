using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 모드 옵션. 엔진 설정(EngineSettings)으로 변환해서 쓴다.
    /// 연출/표시 항목은 PokeRogue의 설정 화면(게임 속도, HP 바 속도, 데미지 숫자, 기술 애니메이션, 타입 힌트, 기술 정보, 수동 메시지 넘김, 음량 등)을 참고했다.
    /// </summary>
    public class RimketmonSettings : ModSettings
    {
        // ---- 규칙 ----
        public bool lethalDefeat = true;            // 패배(HP 0) 시 정착민 사망. 끄면 혼수 + 중증 영구 손상
        public bool captureGuaranteed = true;
        public bool clampTierOverflow = true;
        public int partyLimit = 6;
        public bool gymLeaderDiesOnDefeat = true;   // 관장 본인까지 쓰러뜨리면 그 관장은 죽는다(재도전 불가)

        // ---- 이벤트·상대 성향(전부 사용자가 조절) ----
        public float visitorChallengeChance = 0.15f;  // 방문자 무리 하나가 나에게 승부를 걸어올 확률
        public int visitorFoeCount = 3;               // 방문자가 데려오는 림켓몬 수
        public float visitorPokemonShare = 0.5f;      // 포켓몬을 데려올 때 그중 포켓몬의 비율(나머지는 림켓몬)
        public bool visitorPokemon = false;           // 방문자가 림월드 동물뿐 아니라 포켓몬도 데려온다
        public int visitorSilver = 120;               // 방문자 승부 상금(기본값, 여기에 0~60 + 배지당 40이 더해진다)
        public float visitorFightChance = 0.05f;      // 방문자끼리 지도 위에서 승부를 벌일 확률(방문 중 약 40분마다 판정)
        public bool darkChallengeEnabled = true;      // 어둠의 림켓몬 배틀 이벤트
        public float foeMistakeScale = 1f;            // 랭크 상대의 실수 빈도 배율(0 = 실수 안 함)
        public float rankedRimworldShare = 0.4f;      // 랭크 상대 중 림월드 사람(실제 월드의 인물) 비율
        public string onlineName = "";               // 온라인 대전에서 쓸 내 이름
        public string onlinePic = "";                // 온라인 대전에서 상대에게 보여 줄 내 그림(PNG base64, 이름을 정할 때 찍는다)
        public int huntCostBp = 300;                  // 포켓몬 사냥 입장 비용(BP)
        public int darkTauntBp = 500;                 // 통신기 도발 방송 비용(BP)
        public bool envField = true;                  // 싸우는 자리의 날씨·지형으로 시작 날씨/필드
        public bool learnedAi = true;                 // 어둠의 배틀·관장·사천왕·챔피언·랭크 상위권은 학습된 AI가 조종(Data/ai_policy.bin이 있을 때)
        public bool foeSurrender = true;              // 랭크 상대가 항복하기도 한다
        public float foeTiltScale = 1f;               // 민망함·급소로 인한 항복 빈도 배율
        public float thinkTimeScale = 1f;             // 상대 생각 시간 배율
        public bool rankedTimer = true;               // 랭크배틀 시간 제한(선출 90초·한 턴 45초). 끄면 시간 표시·시간 초과 자동 선택 없음(온라인은 그대로)

        // ---- 배틀 연출(PokeRogue 설정 참고) ----
        public int language = -1;                   // 0 한국어 / 1 English / -1 아직 안 정함(림월드 언어를 따른다: 한국어면 한국어, 아니면 영어)
        public string languageCode = "";            // "ko" / "en" / 다른 언어 묶음 코드. 비어 있으면 아직 안 정함
        public bool languageAsked;                  // 처음 몬스터볼 아이콘을 눌렀을 때 언어를 물어봤는가
        private bool speedReset;
        public int gameSpeed = 1;                   // 0 느림 / 1 보통 / 2 빠름 / 3 터보
        public int hpBarSpeed = 0;                  // 0 보통 / 1 빠름 / 2 매우 빠름 / 3 즉시
        public bool trainerArt = true;              // art 폴더에 일러스트가 있으면 도트 대신 쓴다(지금은 넣은 그림 없음)
        public bool manualMessageClear = true;      // 메시지를 클릭해야 다음으로 넘어간다(기본: 자동으로 넘어가지 않음)
        private bool defaultsV2;
        public bool damageNumbers = true;           // 맞은 쪽에 피해 숫자를 띄운다
        public bool moveAnimations = true;          // 피격 흔들림/번쩍임
        public bool typeHints = true;               // 기술 버튼에 상성(▲▼) 표시
        public bool moveInfo = true;                // 기술 버튼에 위력/명중/PP 표시
        public bool showAbilities = true;           // 내 림켓몬의 특성 표시
        public bool showFoeHp = true;               // 상대 HP 바 옆에 남은 HP/최대 HP 수치를 실시간으로 표시
        public bool showFoeMoves = true;            // 상대가 쓴 적 있는 기술 목록을 이름표 아래에 표시
        public bool transitionEffect = true;        // 진입 시 화면 울렁임
        public bool lowHpBeep = true;               // 내 림켓몬의 체력이 낮을 때 경고음
        public bool humanTiming = true;             // 랭크 상대가 사람처럼 생각하는 시간을 쓴다
        public bool showBgmName = true;
        public float iconX = -1f, iconY = -1f;      // 우하단 림켓몬 아이콘 위치(우클릭 드래그로 이동, -1이면 기본 위치)             // 재생 중인 BGM 표시

        // ---- 소리 ----
        public bool bgmEnabled = true;
        public bool sfxEnabled = true;
        public float masterVolume = 1.0f;
        public float bgmVolume = 0.6f;
        public float sfxVolume = 0.8f;

        public static readonly float[] GameSpeedMult = { 0.7f, 1.0f, 1.7f, 3.0f };
        public static readonly string[] GameSpeedNames = { "느림", "보통", "빠름", "터보" };
        public static readonly float[] HpBarRate = { 0.9f, 1.8f, 3.6f, 1000f };
        public static readonly string[] HpBarNames = { "보통", "빠름", "매우 빠름", "즉시" };

        public float Speed { get { return GameSpeedMult[Mathf.Clamp(gameSpeed, 0, 3)]; } }
        public float HpRate { get { return HpBarRate[Mathf.Clamp(hpBarSpeed, 0, 3)]; } }

        public EngineSettings ToEngine()
        {
            var s = new EngineSettings();
            s.LethalDefeat = lethalDefeat;
            s.CaptureMode = captureGuaranteed ? CaptureMode.Guaranteed : CaptureMode.Chance;
            s.OverflowMode = TierOverflowMode.ReturnToBase;      // 초과분을 최대 티어로 고정하는 기능은 껐다(설정에서도 뺌): 상향이 5티어를 넘으면 기본 티어로 돌아간다
            return s;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lethalDefeat, "lethalDefeat", true);
            Scribe_Values.Look(ref captureGuaranteed, "captureGuaranteed", true);
            Scribe_Values.Look(ref clampTierOverflow, "clampTierOverflow", true);
            Scribe_Values.Look(ref partyLimit, "partyLimit", 6);
            Scribe_Values.Look(ref gymLeaderDiesOnDefeat, "gymLeaderDiesOnDefeat", true);
            Scribe_Values.Look(ref visitorChallengeChance, "visitorChallengeChance", 0.15f);
            Scribe_Values.Look(ref visitorFoeCount, "visitorFoeCount", 3);
            Scribe_Values.Look(ref visitorSilver, "visitorSilver", 120);
            Scribe_Values.Look(ref visitorPokemon, "visitorPokemon2", false);      // 기본 꺼짐(저장 이름을 바꿔 한 번 초기화)
            Scribe_Values.Look(ref visitorPokemonShare, "visitorPokemonShare", 0.5f);
            Scribe_Values.Look(ref visitorFightChance, "visitorFightChance", 0.05f);
            Scribe_Values.Look(ref darkChallengeEnabled, "darkChallengeEnabled", true);
            Scribe_Values.Look(ref foeMistakeScale, "foeMistakeScale", 1f);
            Scribe_Values.Look(ref foeSurrender, "foeSurrender", true);
            Scribe_Values.Look(ref learnedAi, "learnedAi", true);
            Scribe_Values.Look(ref envField, "envField", true);
            Scribe_Values.Look(ref darkTauntBp, "darkTauntBp500", 500);      // 기본값을 500으로 바꾸며 저장 이름도 바꿨다(예전 100이 남지 않게)
            Scribe_Values.Look(ref huntCostBp, "huntCostBp", 300);
            Scribe_Values.Look(ref onlineName, "onlineName", "");
            Scribe_Values.Look(ref onlinePic, "onlinePic", "");
            Scribe_Values.Look(ref rankedRimworldShare, "rankedRimworldShare", 0.4f);
            Scribe_Values.Look(ref foeTiltScale, "foeTiltScale", 1f);
            Scribe_Values.Look(ref thinkTimeScale, "thinkTimeScale", 1f);
            Scribe_Values.Look(ref rankedTimer, "rankedTimer", true);
            Scribe_Values.Look(ref language, "language", -1);
            Scribe_Values.Look(ref languageAsked, "languageAsked", false);
            Scribe_Values.Look(ref languageCode, "languageCode", "");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && string.IsNullOrEmpty(languageCode) && language >= 0) languageCode = language == 1 ? "en" : "ko";      // 예전 설정 옮기기
            Scribe_Values.Look(ref gameSpeed, "gameSpeed", 1);
            Scribe_Values.Look(ref speedReset, "speedReset", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars && !speedReset) { gameSpeed = 1; speedReset = true; }   // 예전에 터보로 저장된 값을 보통(1배속)으로 되돌린다
            Scribe_Values.Look(ref hpBarSpeed, "hpBarSpeed", 0);
            Scribe_Values.Look(ref manualMessageClear, "manualMessageClear", true);
            Scribe_Values.Look(ref trainerArt, "trainerArt", true);
            Scribe_Values.Look(ref defaultsV2, "defaultsV2", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars && !defaultsV2) { gameSpeed = 1; hpBarSpeed = 0; manualMessageClear = true; defaultsV2 = true; }   // 기본값 변경(11/12): 속도 둘 다 보통, 메시지 자동 넘김 끔
            Scribe_Values.Look(ref damageNumbers, "damageNumbers", true);
            Scribe_Values.Look(ref moveAnimations, "moveAnimations", true);
            Scribe_Values.Look(ref typeHints, "typeHints", true);
            Scribe_Values.Look(ref moveInfo, "moveInfo", true);
            Scribe_Values.Look(ref showAbilities, "showAbilities", true);
            Scribe_Values.Look(ref showFoeMoves, "showFoeMoves2", true);      // 기본 켜짐(저장 이름을 바꿔 한 번 켠다)
            Scribe_Values.Look(ref transitionEffect, "transitionEffect", true);
            Scribe_Values.Look(ref humanTiming, "humanTiming", true);
            Scribe_Values.Look(ref lowHpBeep, "lowHpBeep", true);
            Scribe_Values.Look(ref showBgmName, "showBgmName", true);
            Scribe_Values.Look(ref iconX, "iconX", -1f);
            Scribe_Values.Look(ref iconY, "iconY", -1f);
            Scribe_Values.Look(ref bgmEnabled, "bgmEnabled", true);
            Scribe_Values.Look(ref sfxEnabled, "sfxEnabled", true);
            Scribe_Values.Look(ref masterVolume, "masterVolume", 1.0f);
            Scribe_Values.Look(ref bgmVolume, "bgmVolume", 0.6f);
            Scribe_Values.Look(ref sfxVolume, "sfxVolume", 0.8f);
        }
    }

    /// <summary>언어 목록이 다 읽힌 뒤 한 번 더: 아직 언어를 안 정했으면 림월드 언어를 따른다.</summary>
    [StaticConstructorOnStartup]
    internal static class RimketLangInit
    {
        static RimketLangInit()
        {
            RimketmonMod.ResolveLanguage();
            // 진화의 낮/밤 조건: 지금 보고 있는 지도(없으면 본진)의 시각. 6시~18시 낮
            Rimketmon.Engine.Evolution.IsNight = () =>
            {
                try { var m = Find.CurrentMap ?? Find.AnyPlayerHomeMap; if (m == null) return false; int h = GenLocalDate.HourOfDay(m); return h < 6 || h >= 18; }
                catch (System.Exception) { return false; }
            };
        }
    }

    public class RimketmonMod : Mod
    {
        public static RimketmonSettings Settings;
        public static RimketmonMod Instance;
        private Vector2 scroll;

        public RimketmonMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<RimketmonSettings>();
            ResolveLanguage();
            Shinies.HasTier = PkAnim.HasShinyTier;      // 레어·에픽 이로치 그림이 있는 종만 그 등급이 나온다
        }

        private static bool packsLoaded;
        /// <summary>모드 폴더의 Languages/&lt;언어&gt;/Rimketmon/ 언어 묶음을 읽는다(한 번).</summary>
        public static void LoadLanguagePacks()
        {
            if (packsLoaded || Instance == null) return;
            packsLoaded = true;
            try
            {
                int n = Lang.LoadFolder(System.IO.Path.Combine(Instance.Content.RootDir, "Languages"));
                if (n == 0) Log.Warning("[림켓몬] 언어 묶음(Languages/*/Rimketmon/language.txt)을 찾지 못해 한국어로만 표시합니다.");
            }
            catch (System.Exception ex) { Log.Warning("[림켓몬] 언어 묶음 읽기 실패: " + ex.Message); }
        }

        /// <summary>고를 수 있는 언어: 한국어(원문) + 읽어 들인 언어 묶음.</summary>
        public static System.Collections.Generic.List<KeyValuePair<string, string>> LanguageChoices()
        {
            LoadLanguagePacks();
            var l = new System.Collections.Generic.List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("ko", "한국어") };
            foreach (var p in Lang.Packs) l.Add(new KeyValuePair<string, string>(p.Code, p.Name));
            return l;
        }

        /// <summary>언어를 아직 정하지 않았으면 림월드 언어를 따른다(같은 폴더 이름의 언어 묶음 → 한국어면 한국어 → 그 밖은 영어).</summary>
        public static void ResolveLanguage()
        {
            var s = Settings; if (s == null) return;
            LoadLanguagePacks();
            if (string.IsNullOrEmpty(s.languageCode))
            {
                string code = "en";
                try
                {
                    var al = LanguageDatabase.activeLanguage;
                    string f = al != null ? (al.folderName ?? "") : (Prefs.LangFolderName ?? "");
                    if (f.StartsWith("Korean", System.StringComparison.OrdinalIgnoreCase)) code = "ko";
                    else
                    {
                        var hit = Lang.Packs.FirstOrDefault(p => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(p.Dir)) == f);
                        if (hit != null) code = hit.Code;
                    }
                }
                catch (System.Exception) { }
                Lang.Set(code);
                return;     // 정해진 값은 저장하지 않는다(물어보거나 설정에서 고를 때 저장)
            }
            Lang.Set(s.languageCode);
        }

        private static void PickLanguage(string code)
        {
            var s = Settings; if (s == null) return;
            s.languageCode = code; s.language = code == "ko" ? 0 : 1; s.languageAsked = true; Lang.Set(code);
        }

        /// <summary>처음 몬스터볼 아이콘을 눌렀을 때 한 번만 언어를 묻고, 고른 뒤 then을 실행한다.</summary>
        public static bool AskLanguageOnce(System.Action then)
        {
            var s = Settings;
            if (s == null || s.languageAsked) return false;
            ResolveLanguage();
            var choices = LanguageChoices();
            var opts = new System.Collections.Generic.List<FloatMenuOption>();
            foreach (var c in choices) { string code = c.Key; opts.Add(new FloatMenuOption(c.Value, () => { PickLanguage(code); s.Write(); if (then != null) then(); })); }
            var dlg = new Dialog_MessageBox("림켓몬에서 쓸 언어를 고르세요.\nChoose the language for Rimketmon.\n\n(나중에 설정에서 바꿀 수 있습니다 / You can change it later in settings.)",
                "언어 고르기 / Choose", () => Find.WindowStack.Add(new FloatMenu(opts) { vanishIfMouseDistant = false }));
            dlg.doCloseX = false; dlg.closeOnClickedOutside = false; dlg.closeOnCancel = false;
            Find.WindowStack.Add(dlg);
            return true;
        }

        public override string SettingsCategory() { return "RimKéMoN"; }

        private static void Choice(Listing_Standard l, string label, int value, System.Action<int> set, string[] names)
        {
            var r = l.GetRect(28f);
            Widgets.Label(new Rect(r.x, r.y, r.width * 0.55f, r.height), Lang.Tr(label));
            if (Widgets.ButtonText(new Rect(r.x + r.width * 0.55f, r.y, r.width * 0.45f, r.height), Lang.Tr(names[Mathf.Clamp(value, 0, names.Length - 1)])))
            {
                var opts = new System.Collections.Generic.List<FloatMenuOption>();
                for (int i = 0; i < names.Length; i++) { int v = i; opts.Add(new FloatMenuOption(Lang.Tr(names[i]), () => set(v))); }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            l.Gap(2f);
        }

        private static void Slider(Listing_Standard l, string label, ref float value)
        {
            l.Label(Lang.Tr(label) + ": " + value.ToString("P0"));
            value = l.Slider(value, 0f, 1f);
        }

        private static void Pct(Listing_Standard l, string label, ref float value, float max, string tip = null)
        {
            var r = l.Label(Lang.Tr(label) + ": " + value.ToString("P0"));
            if (tip != null) TooltipHandler.TipRegion(r, Lang.Tr(tip));
            value = Mathf.Round(l.Slider(value, 0f, max) * 100f) / 100f;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var s = Settings;
            var view = new Rect(0f, 0f, inRect.width - 20f, 1700f);
            Widgets.BeginScrollView(inRect, ref scroll, view);
            var l = new Listing_Standard();
            l.Begin(view);

            {
                var langs = LanguageChoices();
                int li = Mathf.Max(0, langs.FindIndex(x => x.Key == Lang.Code));
                Choice(l, "언어", li, v => PickLanguage(langs[v].Key), langs.Select(x => x.Value).ToArray());
            }
            l.GapLine();

            l.Label(Lang.Tr("■ 규칙"));
            l.CheckboxLabeled(Lang.Tr("패배 시 정착민 사망 (야생·어둠의 배틀. 체육관·랭크는 해당 없음)"), ref s.lethalDefeat);
            l.CheckboxLabeled(Lang.Tr("승리 후 포획 항상 성공"), ref s.captureGuaranteed);
            l.Label(Lang.Tr("전투에 데려가는 림켓몬 수: ") + s.partyLimit);
            s.partyLimit = (int)l.Slider(s.partyLimit, 1, 6);
            l.GapLine();

            l.Label(Lang.Tr("■ 이벤트"));
            Pct(l, "방문자가 승부를 걸어올 확률 (방문 무리 하나당)", ref s.visitorChallengeChance, 1f, "0%면 걸어오지 않는다. 림켓링크 착용자가 다쳤거나 아프거나 의식이 없으면 확률과 상관없이 걸어오지 않는다.");
            l.Label(Lang.Tr("방문자가 데려오는 림켓몬 수: ") + s.visitorFoeCount);
            s.visitorFoeCount = (int)l.Slider(s.visitorFoeCount, 1, 6);
            l.CheckboxLabeled(Lang.Tr("방문자가 포켓몬도 데려온다"), ref s.visitorPokemon, Lang.Tr("꺼져 있으면 방문자는 림월드 동물만 데려온다. 켜면 아래 비율만큼 포켓몬이, 나머지는 림켓몬(림월드 동물)이 섞여 나온다(나와의 승부, 방문자끼리의 승부 모두). 2마리 이상이면 둘이 반드시 섞인다."));
            if (s.visitorPokemon)
            {
                l.Label(Lang.Tr("방문자 파티 중 포켓몬 비율") + ": " + Mathf.RoundToInt(s.visitorPokemonShare * 100f) + "%");
                s.visitorPokemonShare = Mathf.Round(l.Slider(s.visitorPokemonShare, 0f, 1f) * 20f) / 20f;
            }
            l.Label(Lang.Tr("방문자 승부 상금(은): ") + s.visitorSilver + Lang.Tr(" + 0~60 + 배지당 40"));
            s.visitorSilver = (int)(l.Slider(s.visitorSilver, 0, 1000) / 10f) * 10;
            Pct(l, "방문자끼리 승부를 벌일 확률 (방문 중 약 40분마다)", ref s.visitorFightChance, 0.5f, "방문자 두 명이 지도 위에서 림켓몬을 꺼내 승부한다(구경용). 0%면 일어나지 않는다.");
            l.CheckboxLabeled(Lang.Tr("어둠의 림켓몬 배틀 이벤트"), ref s.darkChallengeEnabled, Lang.Tr("끄면 새로 찾아오지 않는다. 이미 와 있는 트레이너는 그대로다."));
            l.GapLine();

            l.Label(Lang.Tr("■ 랭크 상대의 성향"));
            Pct(l, "실수 빈도", ref s.foeMistakeScale, 2f, "100%가 기본(1000점 이하 30% → 1500점 이상 3%). 0%면 실수하지 않는다.");
            l.CheckboxLabeled(Lang.Tr("상대가 항복하기도 한다"), ref s.foeSurrender);
            l.CheckboxLabeled(Lang.Tr("싸우는 자리의 날씨·지형이 전투 날씨/필드가 된다"), ref s.envField);
            l.Label(Lang.Tr("포켓몬 사냥 입장 비용") + ": " + s.huntCostBp + " BP");
            s.huntCostBp = Mathf.RoundToInt(l.Slider(s.huntCostBp, 0f, 2000f) / 10f) * 10;
            l.Label(Lang.Tr("통신기 도발 방송 비용") + ": " + s.darkTauntBp + " BP");
            s.darkTauntBp = Mathf.RoundToInt(l.Slider(s.darkTauntBp, 0f, 3000f) / 10f) * 10;
            l.Label(Lang.Tr("랭크 상대 중 림월드 사람 비율") + ": " + Mathf.RoundToInt(s.rankedRimworldShare * 100f) + "%");
            s.rankedRimworldShare = Mathf.Round(l.Slider(s.rankedRimworldShare, 0f, 1f) * 20f) / 20f;
            l.CheckboxLabeled(Lang.Tr("어둠의 배틀·관장·사천왕·챔피언·랭크 상위권(1800점 이상)은 학습된 AI 사용") + (LearnedAi.Available ? "" : Lang.Tr(" (AI 파일 없음 — 기존 AI 사용)")), ref s.learnedAi);
            Pct(l, "감정적인 항복 빈도 (실수 뒤 민망함·급소)", ref s.foeTiltScale, 3f, "0%면 가망이 없을 때만 항복한다.");
            l.CheckboxLabeled(Lang.Tr("상대의 생각 시간"), ref s.humanTiming);
            Pct(l, "생각 시간 길이", ref s.thinkTimeScale, 3f, "상대가 기술·선출·다음 림켓몬을 고르는 데 쓰는 시간의 배율.");
            l.CheckboxLabeled(Lang.Tr("랭크배틀 시간 제한 (선출 90초 · 한 턴 45초)"), ref s.rankedTimer, Lang.Tr("끄면 시간이 표시되지 않고, 시간이 지나도 자동으로 고르지 않는다. 온라인 배틀은 상대와 맞춰야 해서 항상 켜져 있다."));
            l.GapLine();

            l.Label(Lang.Tr("■ 배틀 연출"));
            Choice(l, Lang.Tr("게임 속도"), s.gameSpeed, v => s.gameSpeed = v, RimketmonSettings.GameSpeedNames);
            Choice(l, Lang.Tr("HP 바 속도"), s.hpBarSpeed, v => s.hpBarSpeed = v, RimketmonSettings.HpBarNames);
            if (l.ButtonTextLabeled(Lang.Tr("화면 오른쪽 아래 림켓몬 버튼 위치"), Lang.Tr("기본 위치로 되돌리기"))) { s.iconX = s.iconY = -1f; s.Write(); }      // 엉뚱한 곳에 옮겼을 때
            l.CheckboxLabeled(Lang.Tr("메시지 클릭으로 넘기기"), ref s.manualMessageClear);
            l.CheckboxLabeled(Lang.Tr("피해 숫자 표시"), ref s.damageNumbers);
            l.CheckboxLabeled(Lang.Tr("상대 HP 수치 표시"), ref s.showFoeHp);
            l.CheckboxLabeled(Lang.Tr("기술 애니메이션"), ref s.moveAnimations);
            l.CheckboxLabeled(Lang.Tr("기술 상성 힌트 표시"), ref s.typeHints);
            l.CheckboxLabeled(Lang.Tr("기술 위력/명중/PP 표시"), ref s.moveInfo);
            l.CheckboxLabeled(Lang.Tr("배틀 진입 연출"), ref s.transitionEffect);
            l.GapLine();

            l.Label(Lang.Tr("■ 소리"));
            l.CheckboxLabeled(Lang.Tr("배틀 BGM 재생"), ref s.bgmEnabled);
            l.CheckboxLabeled(Lang.Tr("배틀 효과음 재생"), ref s.sfxEnabled);
            l.CheckboxLabeled(Lang.Tr("체력이 낮을 때 경고음"), ref s.lowHpBeep);
            Slider(l, Lang.Tr("전체 볼륨"), ref s.masterVolume);
            Slider(l, Lang.Tr("BGM 볼륨"), ref s.bgmVolume);
            Slider(l, Lang.Tr("효과음 볼륨"), ref s.sfxVolume);

            if (Current.ProgramState == ProgramState.Playing && Current.Game != null)
            {
                l.GapLine();
                l.Label(Lang.Tr("■ 모드 제거"));
                if (RimketUninstall.Removed) l.Label(Lang.Tr("이 세이브에서는 제거된 상태입니다. 우하단 몬스터볼 아이콘을 누르면 다시 설치할 수 있습니다."));
                else if (l.ButtonText(Lang.Tr("이 세이브에서 림켓몬 제거…"))) RimketUninstall.Ask();
            }
            l.End();
            Widgets.EndScrollView();
            base.DoSettingsWindowContents(inRect);
        }
    }

    /// <summary>엔진의 IRng를 림월드 난수(Rand)에 연결.</summary>
    public sealed class GameRng : IRng
    {
        public double NextDouble() { return Rand.Value; }
        public int Next(int minInclusive, int maxExclusive) { return Rand.Range(minInclusive, maxExclusive); }
    }

    [DefOf]
    public static class RimketmonDefOf
    {
        public static DamageDef RimketNeuralOverload;
        public static ThingDef RimketBall;
        public static AbilityDef RimketThrow;
        public static AbilityDef RimketThrowMaster;
        public static AbilityDef RimketSummon;
        public static SoundDef Rimket_Enter;
        public static SoundDef Rimket_HitNormal;
        public static SoundDef Rimket_HitSuper;
        public static SoundDef Rimket_HitWeak;

        static RimketmonDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(RimketmonDefOf)); }
    }

    /// <summary>
    /// 특정 동물 Def의 기본 티어를 수동으로 지정하고 싶을 때 XML로 붙이는 확장(다른 모드 호환 패치용). 0이면 자동 규칙.
    /// 종족/타입/기술은 레퍼런스 포켓몬에서 오므로 여기서 지정하지 않는다.
    /// </summary>
    public class RimketOverride : DefModExtension
    {
        public int tier = 0;       // 1~6, 0이면 자동
    }
}
