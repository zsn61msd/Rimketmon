using System;
using RimWorld;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 랭크전 순위표와 시즌. 순위표 사람들은 게임 하루마다 점수가 변하고, 1년(60일)마다 시즌이 끝난다.
    /// 시즌이 끝나면 등급에 따라 BP를 받고, 나와 순위표 모두 세부 등급이 한 단계만 내려간다.
    /// </summary>
    public partial class PartyComponent
    {
        private string boardData;
        private RankedBoard board;
        public int seasonStartTick = -1, lastBoardDay = -1, seasonGames, lastSeasonReward, lastSeasonRank, lastSeasonOldRating = -1;

        public RankedBoard Board
        {
            get
            {
                if (board == null) { board = RankedBoard.Deserialize(boardData) ?? RankedBoard.Create(new GameRng()); board.Upgrade(new GameRng()); }
                return board;
            }
        }

        public static int SeasonTicks { get { return GenDate.TicksPerYear; } }

        /// <summary>이번 시즌이 끝나기까지 남은 날(게임 일).</summary>
        public int SeasonDaysLeft()
        {
            if (seasonStartTick < 0) return GenDate.DaysPerYear;
            int left = seasonStartTick + SeasonTicks - Find.TickManager.TicksGame;
            return Math.Max(0, (left + GenDate.TicksPerDay - 1) / GenDate.TicksPerDay);
        }

        /// <summary>600틱마다 호출: 하루가 바뀌면 순위표를 하루치 움직이고, 1년이 지나면 시즌을 넘긴다.</summary>
        private void TickRanked()
        {
            int now = Find.TickManager.TicksGame;
            if (seasonStartTick < 0) seasonStartTick = now;
            int day = now / GenDate.TicksPerDay;
            if (lastBoardDay < 0) lastBoardDay = day;
            if (day > lastBoardDay)
            {
                var rng = new GameRng();
                int n = Math.Min(10, day - lastBoardDay);     // 오래 비웠어도 최대 10일치만
                RefreshRankedPawns();
                for (int i = 0; i < n; i++) Board.SimulateDay(rng);
                lastBoardDay = day;
            }
            if (now - seasonStartTick >= SeasonTicks) EndSeason();
        }

        /// <summary>순위표에 오른 실제 림월드 사람 찾기(월드·지도·포획 보관 전부). 없으면 null.</summary>
        public static Pawn FindRankedPawn(string thingId)
        {
            if (string.IsNullOrEmpty(thingId)) return null;
            try
            {
                foreach (var p in Find.WorldPawns.AllPawnsAliveOrDead) if (p.ThingID == thingId) return p;
                foreach (var map in Find.Maps) foreach (var p in map.mapPawns.AllPawns) if (p.ThingID == thingId) return p;
                foreach (var map in Find.Maps) foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)) { var c = t as Corpse; if (c != null && c.InnerPawn != null && c.InnerPawn.ThingID == thingId) return c.InnerPawn; }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>그 사람이 지금 랭크전을 할 수 없는 상태인가: 사망·행방불명·포획(볼·죄수·노예)·납치. 표시용 낱말도 준다.</summary>
        public static bool RankedPawnDown(Pawn p, out string why)
        {
            why = null;
            if (p == null) { why = "행방불명"; return true; }
            if (p.Dead) { why = "사망"; return true; }
            if (Instance != null && Instance.CapturedPawns.Contains(p)) { why = "포획됨"; return true; }
            if (p.IsPrisoner || p.IsSlave) { why = "포획됨"; return true; }
            try { if (Find.WorldPawns.Contains(p) && Find.WorldPawns.GetSituation(p).ToString().Contains("Kidnapped")) { why = "납치됨"; return true; } } catch (Exception) { }
            return false;
        }

        /// <summary>순위표의 실제 사람 상태를 확인해 점수 변동을 멈추거나 다시 푼다.</summary>
        public void RefreshRankedPawns()
        {
            try
            {
                foreach (var m in Board.Members)
                {
                    if (m.PawnId == null) continue;
                    string why; m.Frozen = RankedPawnDown(FindRankedPawn(m.PawnId), out why);
                }
            }
            catch (Exception ex) { Log.Warning("[림켓몬] 순위표 사람 상태 확인: " + ex.Message); }
        }

        /// <summary>시즌 종료: 보상 → 한 단계 내리기 → 새 시즌.</summary>
        public void EndSeason()
        {
            var b = Board;
            int rank = b.PlayerRank(rating);
            int reward = RankedBoard.SeasonRewardBp(rating, rank, seasonGames);
            int old = rating;
            string oldName = RankedLadder.RankName(old);
            bp += reward;
            rating = RankedBoard.SoftReset(rating);
            lastSeasonReward = reward; lastSeasonRank = rank; lastSeasonOldRating = old;
            int season = b.Season;
            b.NewSeason(new GameRng());
            seasonGames = 0; winStreak = 0;
            seasonStartTick = Find.TickManager.TicksGame;
            string text = "랭크전 시즌 " + season + "이(가) 끝났습니다.\n\n"
                + "최종 등급: " + oldName + " (" + old + "점)\n"
                + "최종 순위: " + rank + "위" + (rank <= RankedBoard.ShowTop ? " (순위표 상위 " + RankedBoard.ShowTop + "명 안!)" : "") + "\n"
                + (reward > 0 ? "시즌 보상: " + reward + " BP" + (RankedBoard.TopBonus(rank) > 0 ? " (순위 보너스 " + RankedBoard.TopBonus(rank) + " 포함)" : "") : "이번 시즌에는 랭크전을 하지 않아 보상이 없습니다.") + "\n\n"
                + "시즌 " + b.Season + " 시작: 등급이 한 단계 내려갑니다.\n" + oldName + " → " + RankedLadder.RankName(rating) + " (" + rating + "점)";
            try { Find.LetterStack.ReceiveLetter(Lang.Tr("랭크전 시즌 종료"), Lang.Tr(text), LetterDefOf.PositiveEvent); } catch (Exception) { }
        }

        private void ExposeRanked()
        {
            if (Scribe.mode == LoadSaveMode.Saving && board != null) boardData = board.Serialize();
            Scribe_Values.Look(ref boardData, "rankedBoard", null);
            Scribe_Values.Look(ref seasonStartTick, "seasonStartTick", -1);
            Scribe_Values.Look(ref lastBoardDay, "lastBoardDay", -1);
            Scribe_Values.Look(ref seasonGames, "seasonGames", 0);
            Scribe_Values.Look(ref lastSeasonReward, "lastSeasonReward", 0);
            Scribe_Values.Look(ref lastSeasonRank, "lastSeasonRank", 0);
            Scribe_Values.Look(ref lastSeasonOldRating, "lastSeasonOldRating", -1);
            if (Scribe.mode == LoadSaveMode.LoadingVars) board = null;
        }
    }
}
