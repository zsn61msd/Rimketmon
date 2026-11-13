using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using RimWorld.Planet;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 포획한 림켓몬 저장소. 맵 위의 Pawn이 아니라 가벼운 데이터 객체로 보관한다(볼 안에 있는 상태).
    /// 앞에서부터 partyLimit마리가 전투에 참가한다. 치료는 림켓몬 센터(관리창)에서 한다.
    /// </summary>
    [StaticConstructorOnStartup]
    public partial class PartyComponent : GameComponent
    {
        private List<ExposableCreature> all = new List<ExposableCreature>();
        private List<string> beatenLeaders = new List<string>();
        /// <summary>마스터볼로 포획한 폰(유전자 쐐기 결합에 쓸 예정). 월드 폰으로 보관된다.</summary>
        private List<Pawn> capturedPawns = new List<Pawn>();
        /// <summary>마스터볼로 잡은 사람 림켓몬의 실제 폰(없으면 null).</summary>
        public Pawn PersonPawn(RimketCreature c)
        {
            if (c == null || c.PawnId == 0) return null;
            return CapturedPawns.FirstOrDefault(p => p.thingIDNumber == c.PawnId);
        }
        public List<Pawn> CapturedPawns { get { capturedPawns.RemoveAll(x => x == null); return capturedPawns; } }

        /// <summary>획득한 배지 수. 야생 레벨 범위(배지 N개 → N*10+1 ~ N*10+10)를 정한다.</summary>
        private int badgeCount;
        public const int MaxBadges = 8;
        /// <summary>배지는 최대 8개. 8개를 모은 뒤 챔피언전에서 이겨야 레벨 상한이 100까지 열린다.</summary>
        public int Badges { get { return Math.Min(MaxBadges, Math.Max(badgeCount, beatenLeaders.Count)); } }
        private bool championBeaten;
        public bool ChampionBeaten { get { return championBeaten && Badges >= MaxBadges; } }
        /// <summary>야생/관장 레벨대를 정하는 단계(0~9). 배지 수 + 챔피언 승리 1.</summary>
        public int Stage { get { return Badges + (ChampionBeaten ? 1 : 0); } }
        public int LevelCap { get { return Math.Min(100, (Stage + 1) * 10); } }
        /// <summary>다음 관장전이 챔피언전인가(배지 8개를 모았고 아직 챔피언을 못 이겼다).</summary>
        public bool ChampionNext { get { return Badges >= MaxBadges && !ChampionBeaten; } }
        /// <summary>다음 관장전 상대의 단계. 챔피언전은 한 단계 위(91~100)로 싸운다.</summary>
        public int NextBattleStage { get { return ChampionNext ? MaxBadges + 1 : Stage; } }
        /// <summary>개발자 모드로 배지/레벨을 바꾸면 켜진다: 다음 진짜 관장 승리 전까지 림켓몬 레벨을 상한으로 강제하지 않는다.</summary>
        public bool levelHold;
        public void DebugAddBadge() { levelHold = true; if (Badges < MaxBadges) badgeCount = Badges + 1; else championBeaten = true; }
        public void DebugSetBadges(int n) { levelHold = true; badgeCount = Math.Max(0, Math.Min(MaxBadges, n)); beatenLeaders.Clear(); if (badgeCount < MaxBadges) championBeaten = false; }
        public void DebugSetChampion(bool v) { levelHold = true; if (v) badgeCount = MaxBadges; championBeaten = v; }
        /// <summary>개발자 모드: 림켓몬 레벨을 직접 정한다(1~100). 사람도 된다.</summary>
        public void DebugSetLevel(RimketCreature c, int lv)
        {
            if (c == null) return;
            levelHold = true; lv = Math.Max(1, Math.Min(100, lv));
            float ratio = c.MaxHp > 0 ? (float)c.Hp / c.MaxHp : 1f;
            c.Level = lv; c.Exp = RimketCreature.ExpForLevel(lv); c.RecalculateStats();
            c.Hp = c.Hp <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(ratio * c.MaxHp), 1, c.MaxHp);
        }
        /// <summary>관장전 승리. 이세계(평행세계)의 트레이너라 같은 관장과 다시 싸워도 되므로, 이길 때마다 배지가 1개 늘어난다.</summary>
        public void RecordVictory(string id, bool champion = false)
        {
            levelHold = false;
            if (champion) { if (Badges >= MaxBadges) championBeaten = true; }      // 챔피언은 리그에서만(배지 8개 뒤의 관장 승리는 재대결일 뿐)
            else if (Badges < MaxBadges) badgeCount = Badges + 1;
            if (!beatenLeaders.Contains(id)) beatenLeaders.Add(id);   // 기록용(재도전 제한 없음)
        }

        // ---- 포켓몬 리그(배지 8개 이후): 사천왕 4명을 연달아 이기고 챔피언에게 도전한다. 한 번이라도 지면 처음부터
        private int eliteWins;
        private List<string> leagueLineup = new List<string>();
        /// <summary>리그 도전 중인가(배지 8개, 아직 챔피언을 못 이겼다).</summary>
        public bool InLeague { get { return Badges >= MaxBadges; } }      // 배지 8개면 리그에 도전할 수 있다(챔피언을 이긴 뒤에도 재도전 가능)
        public int EliteWins { get { return eliteWins; } }
        public bool ChampionTurn { get { return InLeague && eliteWins >= 4; } }
        public List<string> LeagueLineup()
        {
            if (leagueLineup == null) leagueLineup = new List<string>();
            if (leagueLineup.Count != 5 || leagueLineup.Any(id => LeagueCatalog.Get(id) == null)) { leagueLineup = LeagueCatalog.NewLineup(new GameRng()); eliteWins = 0; }
            return leagueLineup;
        }
        public GymLeader LeagueOpponent() { var l = LeagueLineup(); return LeagueCatalog.Get(l[Math.Min(4, eliteWins)]); }
        public void EliteWin() { eliteWins = Math.Min(4, eliteWins + 1); }
        /// <summary>리그에서 졌다: 사천왕부터 다시(다음 도전은 새 얼굴들).</summary>
        public void LeagueReset() { eliteWins = 0; leagueLineup = new List<string>(); }

        /// <summary>패러독스 배틀에서 얻은 유전자 쐐기(6티어/?티어 개체 정보). 림켓몬에 쓰면 그 정보로 완전히 덮어쓴다.</summary>
        private List<ExposableCreature> wedges = new List<ExposableCreature>();
        public List<ExposableCreature> Wedges { get { if (wedges == null) wedges = new List<ExposableCreature>(); return wedges; } }
        public int paradoxWins, paradoxLosses;
        /// <summary>세레나 튜토리얼을 이미 보여 줬는가(처음 메뉴를 열 때 한 번만 자동으로 켠다).</summary>
        public bool tutorialSeen;
        /// <summary>챔피언 배지 토글: 켜면 맵의 모든 동물 머리 위에 티어를 표시한다.</summary>
        public bool showTiers;
        /// <summary>종족 이름 표시: 개발자 모드이거나, 챔피언 배지 토글이 켜져 있을 때.</summary>
        public bool SpeciesNamesOn { get { return RimketBuild.Dev || (showTiers && ChampionBeaten); } }

        /// <summary>PC 보관함: 40개 박스 × 30칸. 박스 안 칸 위치는 BoxSlot에 저장한다(비어 있는 칸이 있어도 된다).</summary>
        private List<ExposableCreature> box = new List<ExposableCreature>();
        public const int PartyMax = 6;
        public const int BoxCount = 100, BoxCols = 6, BoxRows = 5, BoxPerPage = BoxCols * BoxRows, BoxTotal = BoxCount * BoxPerPage;
        public IList<ExposableCreature> Box { get { return box; } }

        /// <summary>칸 번호가 없거나 겹치는 보관함 포켓몬(예전 세이브)에 빈 칸을 차례로 배정한다.</summary>
        public void NormalizeBox()
        {
            var used = new HashSet<int>();
            foreach (var e in box) if (e.BoxSlot >= 0 && e.BoxSlot < BoxTotal && used.Add(e.BoxSlot)) continue; else e.BoxSlot = -1;
            int next = 0;
            foreach (var e in box)
            {
                if (e.BoxSlot >= 0) continue;
                while (used.Contains(next)) next++;
                e.BoxSlot = next; used.Add(next);
            }
            foreach (var e in all) e.BoxSlot = -1;
        }
        public ExposableCreature BoxAt(int slot) { for (int i = 0; i < box.Count; i++) if (box[i].BoxSlot == slot) return box[i]; return null; }
        public int FirstFreeSlot(int from = 0)
        {
            var used = new HashSet<int>(box.Select(e => e.BoxSlot));
            for (int n = 0; n < BoxTotal; n++) { int s2 = (from + n) % BoxTotal; if (!used.Contains(s2)) return s2; }
            return -1;
        }
        public bool BoxFull { get { return box.Count >= BoxTotal; } }
        public void Remove(ExposableCreature c) { all.Remove(c); box.Remove(c); }

        // 쐐기를 적용한 사람을 풀어 주면 그 림켓몬 정보(쐐기 종족·능력)를 사람 ID로 기억해 둔다 → 다시 잡으면 그대로 돌아온다(?쐐기면 지도에서 글리치도 계속)
        private Dictionary<string, ExposableCreature> releasedPeople = new Dictionary<string, ExposableCreature>();
        private List<string> releasedKeys; private List<ExposableCreature> releasedVals;
        public void RememberReleasedPerson(Pawn p, ExposableCreature e) { if (p != null && e != null) releasedPeople[p.ThingID] = e; }
        public ExposableCreature TakeReleasedPerson(Pawn p)
        {
            ExposableCreature e;
            if (p == null || !releasedPeople.TryGetValue(p.ThingID, out e)) return null;
            releasedPeople.Remove(p.ThingID); return e;
        }
        public ExposableCreature PeekReleasedPerson(Pawn p) { ExposableCreature e; return p != null && releasedPeople.TryGetValue(p.ThingID, out e) ? e : null; }
        public bool Deposit(ExposableCreature c, int slot = -1)
        {
            if (all.Count <= 1 || !all.Contains(c)) return false;
            if (slot < 0 || BoxAt(slot) != null) slot = FirstFreeSlot();
            if (slot < 0) return false;
            all.Remove(c); c.BoxSlot = slot; box.Add(c); return true;
        }
        public bool Withdraw(ExposableCreature c, int partyIndex = -1)
        {
            if (all.Count >= PartyMax || !box.Remove(c)) return false;
            c.BoxSlot = -1;
            if (partyIndex < 0 || partyIndex > all.Count) all.Add(c); else all.Insert(partyIndex, c);
            return true;
        }
        public void SwapWithParty(ExposableCreature fromBox, ExposableCreature fromParty)
        {
            int bi = box.IndexOf(fromBox), pi = all.IndexOf(fromParty);
            if (bi < 0 || pi < 0) return;
            fromParty.BoxSlot = fromBox.BoxSlot; fromBox.BoxSlot = -1;
            box[bi] = fromParty; all[pi] = fromBox;
        }
        /// <summary>보관함 안에서 칸을 옮긴다. 목적지에 다른 포켓몬이 있으면 서로 바꾼다.</summary>
        public void MoveInBox(ExposableCreature c, int slot)
        {
            if (!box.Contains(c) || slot < 0 || slot >= BoxTotal) return;
            var other = BoxAt(slot);
            if (other == c) return;
            if (other != null) other.BoxSlot = c.BoxSlot;
            c.BoxSlot = slot;
        }
        /// <summary>파티 안에서 순서를 바꾼다(목적지에 있던 포켓몬과 자리를 바꿈).</summary>
        public void MoveInParty(ExposableCreature c, int idx)
        {
            int i = all.IndexOf(c); if (i < 0) return;
            idx = Math.Max(0, Math.Min(all.Count - 1, idx));
            var t = all[idx]; all[idx] = c; all[i] = t;
        }
        public static readonly string[] SortNames = { "도감 순", "이름 순", "속성 순", "종족값 순", "레벨 순" };
        /// <summary>보관함 정렬: 정렬 기준대로 0번 칸부터 빈칸 없이 채운다.</summary>
        public void SortBox(int mode)
        {
            Func<ExposableCreature, object> key;
            Func<ExposableCreature, int> dex = e => { int i = 0; foreach (var sp in SpeciesCatalog.All) { if (sp.Id == e.C.Species) return i; i++; } return 99999; };
            switch (mode)
            {
                case 1: key = e => e.C.Name; break;
                case 2: key = e => (int)e.C.Type1 * 100 + (e.C.Type2.HasValue ? (int)e.C.Type2.Value : 99); break;
                case 3: key = e => -(e.C.MaxHp + e.C.Atk + e.C.Def + e.C.SpA + e.C.SpD + e.C.Spe); break;
                case 4: key = e => -e.C.Level; break;
                default: key = e => dex(e); break;
            }
            var sorted = box.OrderBy(key).ThenBy(e => e.C.Name).ThenBy(e => e.BoxSlot).ToList();
            for (int i = 0; i < sorted.Count; i++) sorted[i].BoxSlot = i;
        }
        public void MoveUp(ExposableCreature c) { int i = all.IndexOf(c); if (i > 0) { all.RemoveAt(i); all.Insert(i - 1, c); } }
        public void MoveDown(ExposableCreature c) { int i = all.IndexOf(c); if (i >= 0 && i < all.Count - 1) { all.RemoveAt(i); all.Insert(i + 1, c); } }
        public void HealAll() { foreach (var e in all) e.C.FullHeal(); }
        public int TotalCount { get { return all.Count + box.Count; } }

        public PartyComponent(Game game) { }

        /// <summary>몬스터볼 연구를 끝내면 림켓링크 퀘스트를 한 번 연다.</summary>
        private bool linkQuestOffered;
        private int linkQuestDue = -1;
        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (Find.TickManager.TicksGame % 600 != 0 || RimketUninstall.Removed) return;
            if (linkQuestOffered) RimketLink.TickQuests();
            // 림켓링크가 아무 데도 없으면(첫 연구 직후 포함) 반나절~하루 안에 퀘스트가 다시 열린다
            if (RimketLink.ResearchDone && (Find.TickManager.TicksGame % 3000 == 0 || !linkQuestOffered))
            {
                bool exists;
                try { exists = RimketLink.ExistsAnywhere(); } catch (System.Exception ex) { Log.ErrorOnce("[림켓몬] 림켓링크 찾기: " + ex, 0x524C4E4B); exists = true; }
                if (exists) linkQuestDue = -1;
                else if (linkQuestDue < 0) linkQuestDue = Find.TickManager.TicksGame + (linkQuestOffered ? Rand.RangeInclusive(GenDate.TicksPerDay / 4, GenDate.TicksPerDay) : 600);
            }
            if (linkQuestDue >= 0 && Find.TickManager.TicksGame >= linkQuestDue)
            {
                linkQuestDue = -1;
                if (!RimketLink.ExistsAnywhere()) { linkQuestOffered = true; RimketLink.OfferQuest(); }
            }
            try { TickRanked(); } catch (System.Exception ex) { Log.ErrorOnce("[림켓몬] 랭크 순위표: " + ex, 0x52414E4B); }
        }
        public void DebugOfferLinkQuest() { linkQuestOffered = true; RimketLink.OfferQuest(); }
        public void DebugCrashNow() { linkQuestOffered = true; RimketLink.DebugCrashNow(); }

        public static PartyComponent Instance
        {
            get { return Current.Game != null ? Current.Game.GetComponent<PartyComponent>() : null; }
        }

        public IList<ExposableCreature> All { get { EnforceLevels(); return all; } }

        /// <summary>레벨은 경험치로 정상적으로 오른다(상한은 배지 수). 여기서는 기술 칸이 비어 있으면 현재 레벨 기준으로 채우기만 한다.</summary>
        public void EnforceLevels()
        {
            var settings = RimketmonMod.Settings.ToEngine();
            foreach (var e in all.Concat(box))
            {
                var c = e.C;
                if (c == null || c.Neutral) continue;
                SpeciesInfo sp;
                if (SpeciesCatalog.TryGet(c.Species, out sp) && c.Moves.Count < settings.MoveSlots)
                    foreach (var slot in SpawnRoller.PickMoves(sp, c.Level, settings))
                    {
                        if (c.Moves.Count >= settings.MoveSlots) break;
                        if (!c.Moves.Any(m => m.Move.Id == slot.Move.Id)) c.Moves.Add(slot);
                    }
            }
        }

        /// <summary>파티 전체(최대 6마리). 랭크전처럼 파티 제한 설정과 상관없는 곳에서 쓴다.</summary>
        public List<RimketCreature> PartyList() { EnforceLevels(); return all.Take(PartyMax).Select(e => e.C).ToList(); }

        /// <summary>전투에 내보낼 파티(앞에서 partyLimit마리).</summary>
        public List<RimketCreature> ActiveParty()
        {
            EnforceLevels();
            return all.Take(Math.Max(1, RimketmonMod.Settings.partyLimit)).Select(e => e.C).ToList();
        }

        /// <summary>새 림켓몬: 파티에 자리가 있으면 파티로, 가득 차 있으면 자동으로 PC 보관함으로 보낸다.</summary>
        public void Add(RimketCreature c)
        {
            var e = new ExposableCreature { C = c };
            DexCatch(c);
            if (all.Count < PartyMax) all.Add(e);
            else { e.BoxSlot = FirstFreeSlot(); box.Add(e); Messages.Message(c.Name + "은(는) 파티가 가득 차 PC로 보내졌다.", MessageTypeDefOf.NeutralEvent, false); }
        }


        // ------------------------------------------------------------------ 우하단 작은 아이콘 (림켓몬 관리 메뉴 열기)

        private static readonly Color[] TierColors = { new Color(0.8f, 0.8f, 0.85f), new Color(0.55f, 0.9f, 0.55f), new Color(0.45f, 0.8f, 1f), new Color(0.75f, 0.6f, 1f), new Color(1f, 0.65f, 0.3f), new Color(1f, 0.4f, 0.4f), new Color(1f, 0.9f, 0.3f) };
        private void DrawTierLabels()
        {
            var map = Find.CurrentMap; if (map == null) return;
            var old = Text.Font; var oldA = Text.Anchor; Text.Font = GameFont.Tiny;
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.Dead || p.RaceProps == null || !RimketMech.IsCatchableRace(p.def)) continue;
                var comp = p.TryGetComp<CompRimket>();
                if (comp == null || comp.data == null || comp.data.C == null) continue;
                var pos = GenMapUI.LabelDrawPosFor(p, -0.55f);
                if (pos.x < -40f || pos.y < -20f || pos.x > UI.screenWidth + 40f || pos.y > UI.screenHeight + 20f) continue;
                var c = comp.data.C;
                SpeciesInfo sp0; string spn = SpeciesCatalog.TryGet(c.Species, out sp0) ? sp0.Name : c.Species;
                GenMapUI.DrawThingLabel(pos, c.TierText + "티어 " + spn, TierColors[Mathf.Clamp(c.Tier, 0, TierColors.Length - 1)]);
            }
            Text.Font = old; Text.Anchor = oldA;
        }

        private static Texture2D iconTex;
        private bool dragging;

        public override void GameComponentOnGUI()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || WorldRendererUtility.WorldRendered) return;
            if (Find.WindowStack.WindowOfType<Window_RimketMenu>() != null) return;
            bool removed = RimketUninstall.Removed;
            if (showTiers && ChampionBeaten && !removed) DrawTierLabels();
            var s = RimketmonMod.Settings;
            if (iconTex == null) iconTex = ContentFinder<Texture2D>.Get("UI/RimketIcon", false) ?? BaseContent.BadTex;

            float size = 28f;
            float x = s.iconX >= 0f ? s.iconX : UI.screenWidth - 232f;
            float y = s.iconY >= 0f ? s.iconY : UI.screenHeight - 68f;
            x = Mathf.Clamp(x, 0f, UI.screenWidth - size);
            y = Mathf.Clamp(y, 0f, UI.screenHeight - size);
            var r = new Rect(x, y, size, size);

            bool over = Mouse.IsOver(r);
            // 배경·테두리 없이 회색 볼 모양만 그린다. 마우스를 올리면 밝아진다(림월드 하단 아이콘과 같은 방식).
            GUI.color = removed ? new Color(0.6f, 0.6f, 0.6f, over ? 0.9f : 0.55f) : over ? new Color(1.15f, 1.15f, 1.15f, 1f) : new Color(0.92f, 0.92f, 0.92f, 1f);      // 제거된 상태면 흐리게   // 그림 자체가 회색+검은 테두리라 색을 입히지 않고 밝기만 바꾼다
            GUI.DrawTexture(r, iconTex);
            GUI.color = Color.white;
            if (over) TooltipHandler.TipRegion(r, removed ? "림켓몬 (제거됨 · 눌러서 다시 설치)" : "림켓몬\n(우클릭 끌기: 옮기기 · 우클릭 두 번: 원래 자리로)");

            var e = Event.current;
            if (e.type == EventType.MouseDown && over && e.button == 1 && e.clickCount >= 2) { s.iconX = s.iconY = -1f; s.Write(); dragging = false; e.Use(); return; }      // 우클릭 두 번: 기본 위치로
            if (e.type == EventType.MouseDown && over && e.button == 1) { dragging = true; e.Use(); }
            else if (dragging && e.type == EventType.MouseDrag)
            {
                s.iconX = Mathf.Clamp(x + e.delta.x, 0f, UI.screenWidth - size);
                s.iconY = Mathf.Clamp(y + e.delta.y, 0f, UI.screenHeight - size);
                e.Use();
            }
            else if (dragging && e.type == EventType.MouseUp) { dragging = false; s.Write(); e.Use(); }
            else if (e.type == EventType.MouseDown && over && e.button == 0)
            {
                e.Use();
                if (removed) { RimketUninstall.AskReinstall(); return; }
                if (!RimketmonMod.AskLanguageOnce(() => Find.WindowStack.Add(new Window_RimketMenu())))
                    Find.WindowStack.Add(new Window_RimketMenu());
            }
        }

        // ---- 도구 가방(지닌 도구로 줄 수 있는 도구 보관함)
        public Dictionary<string, int> bag = new Dictionary<string, int>();
        public int BagCount(string id) { int n; return id != null && bag.TryGetValue(id, out n) ? n : 0; }
        public void BagAdd(string id, int n = 1) { if (id == null || n <= 0) return; bag[id] = BagCount(id) + n; }
        public bool BagTake(string id) { int n = BagCount(id); if (n <= 0) return false; if (n == 1) bag.Remove(id); else bag[id] = n - 1; return true; }

        // ---- 랭크전: 점수와 BP(포켓몬 활동 전용 재화)
        public int rating = RankedLadder.StartRating;
        public int bp = 0;
        public bool bpStarterGiven;
        public int rankedGames, rankedWins, rankedLosses, winStreak, bestRating = RankedLadder.StartRating, bestRankIdx;
        public const int StarterBp = RankedLadder.StarterBp;

        /// <summary>처음 한 번 시작 BP를 준다(랭크전을 하려면 볼부터 사야 하므로).</summary>
        public void EnsureStarterBp() { if (!bpStarterGiven) { bpStarterGiven = true; bp += StarterBp; } }
        public bool SpendBp(int n) { if (bp < n) return false; bp -= n; return true; }

        /// <summary>랭크전 결과 반영. 점수 변동·BP 보상·등급 상승 보너스를 돌려준다.</summary>
                public void RecordRanked(bool won, int oppRating, out int delta, out int bpGain, out bool rankUp)
        {
            delta = RankedLadder.Delta(rating, oppRating, won, rankedGames);
            if (won) { winStreak++; rankedWins++; } else { winStreak = 0; rankedLosses++; }
            bpGain = RankedLadder.BpReward(won, winStreak, rating, oppRating);
            rating = RankedLadder.Apply(rating, delta);
            rankedGames++; seasonGames++;
            if (rating > bestRating) bestRating = rating;
            int idx = RankedLadder.RankIndex(rating);
            rankUp = idx > bestRankIdx;
            if (rankUp) { bestRankIdx = idx; bpGain += RankedLadder.RankUpBonus; }
            bp += bpGain;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            ExposeExtra();
            ExposeRanked();
            Scribe_Values.Look(ref rating, "rankRating", RankedLadder.StartRating);
            Scribe_Values.Look(ref bp, "bp", 0);
            Scribe_Values.Look(ref bpStarterGiven, "bpStarterGiven", false);
            Scribe_Values.Look(ref rankedGames, "rankedGames", 0);
            Scribe_Values.Look(ref rankedWins, "rankedWins", 0);
            Scribe_Values.Look(ref rankedLosses, "rankedLosses", 0);
            Scribe_Values.Look(ref winStreak, "winStreak", 0);
            Scribe_Values.Look(ref bestRating, "bestRating", RankedLadder.StartRating);
            Scribe_Values.Look(ref bestRankIdx, "bestRankIdx", 0);
            Scribe_Collections.Look(ref all, "rimketParty", LookMode.Deep);
            Scribe_Collections.Look(ref box, "rimketBox", LookMode.Deep);
            Scribe_Collections.Look(ref wedges, "paradoxWedges", LookMode.Deep);
            Scribe_Collections.Look(ref releasedPeople, "releasedPeople", LookMode.Value, LookMode.Deep, ref releasedKeys, ref releasedVals);
            if (releasedPeople == null) releasedPeople = new Dictionary<string, ExposableCreature>();
            Scribe_Values.Look(ref tutorialSeen, "tutorialSeen", false);
            Scribe_Values.Look(ref showTiers, "showTiers", false);
            Scribe_Values.Look(ref levelHold, "levelHold", false);
            Scribe_Values.Look(ref paradoxWins, "paradoxWins", 0);
            Scribe_Values.Look(ref paradoxLosses, "paradoxLosses", 0);
            if (wedges == null) wedges = new List<ExposableCreature>();
            if (box == null) box = new List<ExposableCreature>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && all != null)      // 예전 세이브: 6마리를 넘는 건 PC로
                while (all.Count > PartyMax) { box.Add(all[PartyMax]); all.RemoveAt(PartyMax); }
            if (Scribe.mode == LoadSaveMode.PostLoadInit) NormalizeBox();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)      // 도구로 바뀌는 모습: 지닌 도구에 맞춘다(예전 세이브 포함)
                foreach (var e in (all ?? new List<ExposableCreature>()).Concat(box)) if (e != null && e.C != null) ItemForms.Sync(e.C);
            Scribe_Collections.Look(ref beatenLeaders, "beatenLeaders", LookMode.Value);
            Scribe_Collections.Look(ref bag, "itemBag", LookMode.Value, LookMode.Value);
            if (bag == null) bag = new Dictionary<string, int>();
            Scribe_Values.Look(ref linkQuestOffered, "linkQuestOffered", false);
            Scribe_Values.Look(ref linkQuestDue, "linkQuestDue", -1);
            Scribe_Values.Look(ref badgeCount, "badgeCount", 0);
            Scribe_Values.Look(ref championBeaten, "championBeaten", false);
            Scribe_Values.Look(ref eliteWins, "eliteWins", 0);
            Scribe_Collections.Look(ref leagueLineup, "leagueLineup", LookMode.Value);
            if (leagueLineup == null) leagueLineup = new List<string>();
            Scribe_Collections.Look(ref capturedPawns, "capturedPawns", LookMode.Reference);
            if (capturedPawns == null) capturedPawns = new List<Pawn>();
            if (all == null) all = new List<ExposableCreature>();
            if (beatenLeaders == null) beatenLeaders = new List<string>();
        }
    }
}
