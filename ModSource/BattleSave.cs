using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 세이브 중 배틀 이어 하기에 필요한 '배틀 명세'. 저장할 수 있는 값만 담는다(코드 조각·창 같은 것은 없음).
    /// 모든 배틀은 BattleFlow.OpenWindow 한 곳에서 창을 여는데, 그때 이 명세가 배틀에 붙는다.
    /// 끝난 뒤 처리는 Kind와 Args로 BattleSave.Finisher가 다시 만든다.
    /// </summary>
    public sealed class BattleSpec : IExposable
    {
        public string Kind, Ctor, FoeName, PlayerName;
        public Dictionary<string, string> Args = new Dictionary<string, string>();
        public Pawn Pawn, FoePawn, Target, Me;
        // 시작 상태(엔진 FlatCodec 문자열) — 세이브 파일에는 Base64로
        public List<string> StartPlayer = new List<string>(), StartFoe = new List<string>();
        public string StartFoePerson, Colonist, Settings, Flags;
        public List<int> LiveIdx = new List<int>();
        // 진행 기록
        public List<string> JEntries = new List<string>(), JFoe = new List<string>();
        public string JDraws;
        // 창·진행 상태
        public bool Minimized, Frozen;
        public string Arena, TrainerSprite, ForcedTrack, Tower, Hunt;

        public string Arg(string k, string def = null) { string v; return Args != null && Args.TryGetValue(k, out v) ? v : def; }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Kind, "kind"); Scribe_Values.Look(ref Ctor, "ctor");
            Scribe_Values.Look(ref FoeName, "foeName"); Scribe_Values.Look(ref PlayerName, "playerName");
            Scribe_Collections.Look(ref Args, "args", LookMode.Value, LookMode.Value);
            Scribe_References.Look(ref Pawn, "pawn"); Scribe_References.Look(ref FoePawn, "foePawn");
            Scribe_References.Look(ref Target, "target"); Scribe_References.Look(ref Me, "me");
            B64List(ref StartPlayer, "startPlayer"); B64List(ref StartFoe, "startFoe");
            B64(ref StartFoePerson, "startFoePerson"); B64(ref Colonist, "colonist"); B64(ref Settings, "settings"); B64(ref Flags, "flags");
            Scribe_Collections.Look(ref LiveIdx, "liveIdx", LookMode.Value);
            B64List(ref JEntries, "jEntries"); B64List(ref JFoe, "jFoe"); B64(ref JDraws, "jDraws");
            Scribe_Values.Look(ref Minimized, "minimized"); Scribe_Values.Look(ref Frozen, "frozen");
            Scribe_Values.Look(ref Arena, "arena"); Scribe_Values.Look(ref TrainerSprite, "trainerSprite"); Scribe_Values.Look(ref ForcedTrack, "forcedTrack");
            B64(ref Tower, "tower"); B64(ref Hunt, "hunt");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Args == null) Args = new Dictionary<string, string>();
                if (LiveIdx == null) LiveIdx = new List<int>();
                if (StartPlayer == null) StartPlayer = new List<string>(); if (StartFoe == null) StartFoe = new List<string>();
                if (JEntries == null) JEntries = new List<string>(); if (JFoe == null) JFoe = new List<string>();
            }
        }

        // XML에는 제어 문자를 넣을 수 없어서(FlatCodec은 구분자로 쓴다) Base64로 감싼다
        internal static void B64Pub(ref string s, string label) { B64(ref s, label); }
        internal static void B64ListPub(ref List<string> l, string label) { B64List(ref l, label); }
        static void B64(ref string s, string label)
        {
            string enc = Scribe.mode == LoadSaveMode.Saving && s != null ? Convert.ToBase64String(Encoding.UTF8.GetBytes(s)) : null;
            Scribe_Values.Look(ref enc, label);
            if (Scribe.mode == LoadSaveMode.LoadingVars) s = enc != null ? Encoding.UTF8.GetString(Convert.FromBase64String(enc)) : null;
        }
        static void B64List(ref List<string> l, string label)
        {
            List<string> enc = Scribe.mode == LoadSaveMode.Saving && l != null ? l.Select(x => Convert.ToBase64String(Encoding.UTF8.GetBytes(x ?? ""))).ToList() : null;
            Scribe_Collections.Look(ref enc, label, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars) l = enc != null ? enc.Select(x => Encoding.UTF8.GetString(Convert.FromBase64String(x))).ToList() : new List<string>();
        }
    }

    public static class BattleSave
    {
        /// <summary>배틀 진행을 기록하는 무작위(배틀을 만들 때 넣는다). 기존 무작위를 감싸 그대로 이어 쓴다.</summary>
        public static IRng Rng(IRng inner) { return new JournalRng(inner, new BattleJournal()); }

        // ---- 시작하는 쪽이 OpenWindow 직전에 채우는 값
        static string pendingKind; static readonly Dictionary<string, string> pendingArgs = new Dictionary<string, string>();
        static Pawn pendingTarget, pendingMe;
        /// <summary>다음에 여는 배틀의 종류와 끝 처리에 필요한 값.</summary>
        public static void Begin(string kind, Pawn target = null, Pawn me = null, params string[] kv)
        {
            pendingKind = kind; pendingTarget = target; pendingMe = me;
            for (int i = 0; i + 1 < kv.Length; i += 2) pendingArgs[kv[i]] = kv[i + 1];
        }
        /// <summary>Begin보다 먼저 값만 넣어 둘 때(랭크 메뉴처럼 끝 처리 값이 다른 곳에서 정해질 때).</summary>
        public static void SetArgs(params string[] kv) { for (int i = 0; i + 1 < kv.Length; i += 2) pendingArgs[kv[i]] = kv[i + 1]; }

        static readonly Dictionary<RimketBattle, int> lookSeeds = new Dictionary<RimketBattle, int>();
        public static void NoteLookahead(RimketBattle b, int seed) { if (b != null) lookSeeds[b] = seed; }

        static readonly string[] FlagNames = {
            "HuntMode", "DeferExp", "ExpShare", "PlayerLevelCap", "FoeAiMode", "FoeMistakeRate", "FoeMaySurrender", "AllowFoeSurrender",
            "NoItems", "BlackoutOnDefeat", "FoeTilt", "FoeRating", "GymBattle", "LeagueBattle", "Ranked", "Paradox" };

        /// <summary>OpenWindow에서: 배틀에 명세를 만든다(진행 기록이 있는 배틀만, 온라인 제외). 창을 열기 전 설정이 끝난 상태.</summary>
        internal static BattleSpec Capture(RimketBattle b, Pawn pawn, Pawn foePawn)
        {
            string kind = pendingKind; var args = new Dictionary<string, string>(pendingArgs); var target = pendingTarget; var me = pendingMe;
            pendingKind = null; pendingArgs.Clear(); pendingTarget = null; pendingMe = null;
            if (kind == null || b == null || b.Journal == null || b.Online || b.StartPlayer == null) return null;
            var pc = PartyComponent.Instance;
            var s = new BattleSpec
            {
                Kind = kind, Args = args, Pawn = pawn, FoePawn = foePawn, Target = target, Me = me,
                Ctor = b.Paradox ? "paradox" : b.Ranked ? "ranked" : b.IsTrainer ? "trainer" : "wild",
                FoeName = b.Foe != null ? b.Foe.Name : null, PlayerName = b.Player != null ? b.Player.Name : null,
                StartPlayer = b.StartPlayer.ToList(), StartFoe = b.StartFoe.ToList(), StartFoePerson = b.StartFoePerson,
                Colonist = b.StartColonist, Settings = b.StartSettings,
                Flags = FlatCodec.Encode(b, FlagNames),
                Frozen = RimketTime.IsFrozen(b),
                TrainerSprite = b.IsTrainer && b.Foe != null ? Window_RimketBattle.FindTrainerSprite(b.Foe.Name) : null,
                ForcedTrack = RimketAudio.ForcedTrack,
                Arena = Window_RimketBattle.ArenaOverride,
            };
            s.Args["balls"] = string.Join(",", b.Balls);
            int seed; if (lookSeeds.TryGetValue(b, out seed)) { s.Args["brain"] = "look"; s.Args["seed"] = seed.ToString(); lookSeeds.Remove(b); }
            else if (b.FoeBrain != null) s.Args["brain"] = "learned";
            // 파티 원본(살아 있는 객체)인지: 이어 할 때 그 객체에 시작 상태를 덮어써서 배틀이 다시 그 객체를 쓰게 한다
            foreach (var c in b.Player.Party) { int li = -1; if (pc != null) for (int k = 0; k < pc.All.Count; k++) if (pc.All[k].C == c) { li = k; break; } s.LiveIdx.Add(li); }
            if (kind == "wild" && target != null) s.Args["foeLive"] = "1";      // 야생: 상대는 그 동물에 붙은 림켓몬 객체
            return s;
        }

        /// <summary>세이브 직전: 지금 떠 있는 배틀 창의 진행 기록과 진행 상태를 명세에 담는다. 없으면 null.</summary>
        internal static BattleSpec ForSave()
        {
            var w = Find.WindowStack != null ? Find.WindowStack.WindowOfType<Window_RimketBattle>() : null;
            if (w == null || w.Spec == null || w.Battle == null || w.Battle.Journal == null || w.Battle.Online) return null;
            var s = w.Spec; var j = w.Battle.Journal;
            s.JEntries = j.Entries.ToList(); s.JFoe = j.Foe.ToList(); s.JDraws = j.DrawsText();
            s.Minimized = RimketClock.Paused;
            s.Tower = s.Kind == "tower" ? TowerRun.SaveState() : null;
            s.Hunt = s.Kind == "hunt" ? HuntRun.SaveState() : null;
            if (s.Kind == "tower") s.Me = TowerRun.RunPawn; else if (s.Kind == "hunt") s.Me = HuntRun.RunPawn;
            return s;
        }

        /// <summary>불러온 뒤: 같은 배틀을 다시 만들고 기록을 재생해 저장한 순간부터 이어 간다.</summary>
        internal static void Resume(BattleSpec s)
        {
            var pc = PartyComponent.Instance;
            if (s == null || pc == null) return;
            try
            {
                var settings = new EngineSettings(); FlatCodec.Decode(settings, s.Settings);
                var colonist = new ColonistCombatant(); FlatCodec.Decode(colonist, s.Colonist);
                // 내 쪽: 파티 원본은 그 객체에 시작 상태를 덮어쓴다(재생이 끝나면 저장 순간 상태로 돌아온다)
                var mine = new List<RimketCreature>();
                for (int i = 0; i < s.StartPlayer.Count; i++)
                {
                    int li = i < s.LiveIdx.Count ? s.LiveIdx[i] : -1;
                    if (li >= 0 && li < pc.All.Count) { FlatCodec.Overwrite(pc.All[li].C, s.StartPlayer[i]); mine.Add(pc.All[li].C); }
                    else mine.Add(FlatCodec.DecodeCreature(s.StartPlayer[i]));
                }
                var foe = new List<RimketCreature>();
                for (int i = 0; i < s.StartFoe.Count; i++)
                {
                    RimketCreature live = null;
                    if (i == 0 && s.Arg("foeLive") == "1" && s.Target != null) { var comp = s.Target.TryGetComp<CompRimket>(); if (comp != null && comp.data != null) live = comp.data.C; }
                    if (live != null) { FlatCodec.Overwrite(live, s.StartFoe[i]); foe.Add(live); }
                    else foe.Add(FlatCodec.DecodeCreature(s.StartFoe[i]));
                }
                var person = FlatCodec.DecodeCreature(s.StartFoePerson);
                var mine0 = s.StartPlayer.Select(FlatCodec.DecodeCreature).ToList(); var foe0 = s.StartFoe.Select(FlatCodec.DecodeCreature).ToList();

                var j = new BattleJournal { Entries = s.JEntries.ToList(), Foe = s.JFoe.ToList() }; j.SetDrawsText(s.JDraws);
                int seed; bool look = s.Arg("brain") == "look" && int.TryParse(s.Arg("seed"), out seed);
                int.TryParse(s.Arg("seed"), out seed);
                IRng inner = look ? (IRng)new SystemRng(seed) : new GameRng();
                var rng = new JournalRng(inner, j, look);      // 미리 계산 AI: 시드 난수를 저장 순간 위치까지 같이 굴려 이어서 같은 흐름
                RimketBattle b;
                switch (s.Ctor)
                {
                    case "paradox": b = RimketBattle.ParadoxBattle(rng, settings, mine, foe[0]); break;
                    case "ranked": b = RimketBattle.RankedBattle(rng, settings, s.PlayerName, mine, s.FoeName, foe); break;
                    case "trainer": b = RimketBattle.Trainer(rng, settings, mine, colonist, s.FoeName, foe, person); break;
                    default: b = new RimketBattle(rng, settings, mine, foe[0], colonist); break;
                }
                FlatCodec.Decode(b, s.Flags);
                var balls = (s.Arg("balls") ?? "").Split(',');
                for (int t = 0; t < 4 && t < balls.Length; t++) { int n; if (int.TryParse(balls[t], out n)) b.Balls[t] = n; }

                // 진행 상태(타워·사냥)와 화면 설정
                if (s.Kind == "tower") TowerRun.LoadState(s.Tower, s.Me);
                if (s.Kind == "hunt") HuntRun.LoadState(s.Hunt, s.Me);
                if (s.TrainerSprite != null && s.FoeName != null) Window_RimketBattle.TrainerSpriteOverride[s.FoeName] = s.TrainerSprite;
                if (s.ForcedTrack != null) RimketAudio.ForcedTrack = s.ForcedTrack;
                if (s.Frozen) RimketTime.MarkFrozen(b);

                // 상대 두뇌: 재생 중에는 기록된 결정을 쓰고(경로만 같게 두뇌를 달아 둔다), 끝나면 이어서 실제로 생각한다
                LookaheadFoe la = null;
                string flags = s.Flags;
                if (look) la = LearnedAi.AttachLookahead(b, settings, seed, mine0, foe0, bb => FlatCodec.Decode(bb, flags));
                else if (s.Arg("brain") == "learned") LearnedAi.Attach(b, settings);
                if (b.FoeBrain == null && j.Foe.Count > 0) b.FoeBrain = bb => null;      // 기록에 두뇌 결정이 있으면 경로를 같게

                var before = Window_RimketEvolution.Snapshot(pc.ActiveParty());
                bool ok = BattleJournal.Replay(b, j);
                if (la != null) la.PrimeFoe(j.Foe.Select(BattleJournal.Dec));
                if (!ok) Log.Warning("[림켓몬] 이어 하기: 기록 일부를 다시 하지 못했습니다(" + s.Kind + "). 가능한 곳까지 이어 갑니다.");

                var fin = Finisher(s);
                Action<RimketBattle> onFinished = bb => { fin(bb); if (!bb.Online && !bb.Ranked && !bb.Paradox) { try { Window_RimketEvolution.AfterBattle(before); } catch (Exception ex) { Log.Warning("[림켓몬] 진화: " + ex.Message); } } };
                if (s.Arena != null) Window_RimketBattle.ArenaOverride = s.Arena;
                var w = new Window_RimketBattle(b, s.Pawn, onFinished) { FoePawn = s.FoePawn, Spec = s, Resumed = true };
                Find.WindowStack.Add(w);
                if (s.Minimized) RimketClock.Pause();
                Messages.Message(Lang.Tr("림켓몬 배틀을 저장한 곳부터 이어 갑니다."), MessageTypeDefOf.NeutralEvent, false);
            }
            catch (Exception ex)
            {
                Log.Error("[림켓몬] 저장된 배틀을 이어 가지 못했습니다(" + s.Kind + "): " + ex);
                Messages.Message(Lang.Tr("저장된 림켓몬 배틀을 이어 가지 못했습니다. 배틀은 취소됩니다."), MessageTypeDefOf.RejectInput, false);
            }
        }

        /// <summary>끝난 뒤 처리(종류별). 저장한 값만으로 만든다.</summary>
        static Action<RimketBattle> Finisher(BattleSpec s)
        {
            bool A(string k) { return s.Arg(k) == "1"; }
            switch (s.Kind)
            {
                case "wild": return b => BattleFlow.ResolveWild(b, s.Pawn, s.Target);
                case "devwild": return b => { if (b.Result.Captured) PartyComponent.Instance.Add(b.Wild); };
                case "gym":
                    {
                        GymLeader leader = null;
                        try { leader = A("league") ? LeagueCatalog.Get(s.Arg("leader")) : GymCatalog.Get(s.Arg("leader")); } catch (Exception) { }
                        if (leader == null) try { leader = LeagueCatalog.Get(s.Arg("leader")); } catch (Exception) { }
                        return b => BattleFlow.ResolveGym(b, s.Pawn, leader, A("champion"), A("league"), A("rematch"));
                    }
                case "ranked":
                    {
                        int or, old; int.TryParse(s.Arg("oppRating"), out or); int.TryParse(s.Arg("oldRating"), out old);
                        return b => { BattleFlow.LastFoeSurrender = b.Result.FoeSurrendered ? b.Result.FoeSurrenderReason : null; Window_RimketMenu.FinishRanked(s.Arg("oppName"), s.Arg("oppCountry"), A("board"), or, old, b.Result.End == BattleEnd.Victory); };
                    }
                case "paradox": return b => Window_RimketMenu.FinishParadox(FlatCodec.DecodeCreature(s.StartFoe.FirstOrDefault()), b.Result.End == BattleEnd.Victory);
                case "challenge":
                    {
                        IntVec3 pos = IntVec3.Invalid; try { if (!string.IsNullOrEmpty(s.Arg("pos"))) pos = IntVec3.FromString(s.Arg("pos")); } catch (Exception) { }
                        var map = s.FoePawn != null ? s.FoePawn.MapHeld : s.Pawn != null ? s.Pawn.MapHeld : Find.AnyPlayerHomeMap;
                        return b => BattleFlow.ResolveChallengePublic(b, s.Pawn, s.FoePawn, map, pos, A("apocriton"));
                    }
                case "mystery": return b => BattleFlow.ResolveMysteryPublic(b, s.FoePawn);
                case "visitor": { int bd; int.TryParse(s.Arg("badges"), out bd); return b => BattleFlow.ResolveVisitorPublic(b, s.FoePawn, s.Me, bd); }
                case "hunt": return b => HuntRun.ResolvePublic(b);
                case "tower": { int f; int.TryParse(s.Arg("floor"), out f); return b => TowerRun.ResolvePublic(b, f); }
            }
            return b => { };
        }

        // ------------------------------------------------------------------ 배틀 밖 상태(최소화한 메뉴·타워 결과 화면)
        internal static bool MenuMinimized() { return RimketClock.Paused && Find.WindowStack != null && Find.WindowStack.WindowOfType<Window_RimketBattle>() == null && Find.WindowStack.WindowOfType<Window_RimketMenu>() != null; }

        /// <summary>같은 실행 중 다른 세이브를 불러올 때: 이전 판의 정적 상태를 모두 비운다.</summary>
        internal static void ResetSession()
        {
            RimketClock.Resume();
            TowerRun.Reset(); HuntRun.Reset();
            RimketTime.WorldFocus = false;
            pendingKind = null; pendingArgs.Clear(); lookSeeds.Clear();
        }
    }
}
