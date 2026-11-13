using System;
using System.Collections.Generic;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>랭크전에서 만나는 상대(실제 유저처럼 보이는 가상의 트레이너).</summary>
    public sealed class RankedOpponent
    {
        public string Name;          // 표시 이름(아이디풍)
        public string Country;       // 국가 이름
        public string CountryCode;   // 2글자 코드
        public int Rating;
        public string TrainerId;     // 일반 NPC 스프라이트 id (RankedTrainerCatalog)
        public string Format;        // 파티풀 포맷(pu…ubers)
        public List<RimketCreature> Party = new List<RimketCreature>();   // 6마리
        public bool BoardMember;     // 순위표에 있는 사람(결과가 그 사람 점수에도 반영된다)
    }

    /// <summary>랭크 점수(레이팅)·등급·BP 보상·상대 생성.</summary>
    public static class RankedLadder
    {
        public const int StartRating = 1000;
        public const int MinRating = 0;
        public const int BattleLevel = 50;   // 랭크는 양쪽 모두 Lv.50으로 맞춘다

        // ------------------------------------------------------------------ 점수

        public static double Expected(int mine, int theirs) { return 1.0 / (1.0 + Math.Pow(10.0, (theirs - mine) / 400.0)); }

        /// <summary>엘로 방식. 처음 20판은 변동 폭이 크다(K 48), 이후 K 32.</summary>
        public static int Delta(int mine, int theirs, bool won, int gamesPlayed)
        {
            int k = gamesPlayed < 20 ? 48 : 32;
            double d = k * ((won ? 1.0 : 0.0) - Expected(mine, theirs));
            int r = (int)Math.Round(d);
            if (won && r < 1) r = 1;           // 이기면 최소 +1
            if (!won && r > -1) r = -1;        // 지면 최소 -1
            return r;
        }

        public static int Apply(int rating, int delta) { return Math.Max(MinRating, rating + delta); }

        // ------------------------------------------------------------------ 등급 / 포맷

        public static readonly int[] RankFloors = { 0, 1100, 1300, 1500 };
        public static readonly string[] RankNames = { "몬스터볼급", "슈퍼볼급", "하이퍼볼급", "마스터볼급" };
        public static readonly string[] RankNamesEn = { "Poké Ball Class", "Great Ball Class", "Ultra Ball Class", "Master Ball Class" };

        public static int RankIndex(int rating)
        {
            int idx = 0;
            for (int i = 0; i < RankFloors.Length; i++) if (rating >= RankFloors[i]) idx = i;
            return idx;
        }
        /// <summary>등급 안의 세부 순위 4(낮음)~1(높음). 각 볼 등급을 네 구간으로 나눈다(포켓몬 챔피언스: 볼 등급마다 랭크 4~1).</summary>
        public static int SubRank(int rating)
        {
            int idx = RankIndex(rating);
            int lo = idx == 0 ? 800 : RankFloors[idx];
            int hi = idx < RankFloors.Length - 1 ? RankFloors[idx + 1] : RankFloors[idx] + 200;
            double step = (hi - lo) / 4.0;
            int band = (int)Math.Floor((rating - lo) / step);
            return 4 - Math.Max(0, Math.Min(3, band));
        }

        public static string RankName(int rating) { return RankNames[RankIndex(rating)] + " " + SubRank(rating); }

        /// <summary>점수에 따른 상대 파티 티어: 낮은 점수는 PU, 높으면 OU/Ubers.</summary>
        public static string FormatFor(int rating)
        {
            if (rating < 1050) return "pu";
            if (rating < 1150) return "nu";
            if (rating < 1250) return "ru";
            if (rating < 1350) return "uu";
            if (rating < 1500) return "ou";
            return "ubers";
        }

        // ------------------------------------------------------------------ BP
        // 포켓몬 챔피언스 환산: 1 BP = 5 VP. (챔피언스: 랭크 승 약 300VP, 패 약 100VP, 일일 미션 약 500VP, 능력 포인트 5VP, 기술 250VP, 성격 500VP)

        // 림켓몬에는 출석·퀘스트가 없으므로, 챔피언스의 미션 수입(일일 약 500VP + 주간 약 3,000VP ≈ 하루 5판 기준 판당 +35 BP)을 전투 보상에 녹였다.
        public const int WinBp = 100;         // 챔피언스 승리 300VP(60 BP) + 미션 몫
        public const int LossBp = 35;         // 챔피언스 패배 약 100VP(20 BP) + 미션 몫
        public const int StreakBp = 75;       // 연승마다 추가(최대 5연승분). 림켓몬 고유 보너스
        public const int RankUpBonus = 300;   // 등급이 처음 올랐을 때(챔피언스: 시즌 보상이 등급별로 크게 오른다)
        public const int StarterBp = 1000;    // 시작 보너스(챔피언스: 10,000 VP, 참고 자료마다 달라 절반으로)

        /// <summary>상점 가격(BP): 팬게임 가격표(은 단위)를 눌러서 쓴다. 30 × (가격/200)^0.55, 최소 5. 상처약(200) 30, 좋은상처약(700) 59, 먹다남은음식(20000) 약 380(승 6~7번), 구애머리띠(100000) 약 910.</summary>
        public static int ShopBp(double silverPrice)
        {
            return Math.Max(5, (int)Math.Round(30.0 * Math.Pow(Math.Max(1.0, silverPrice) / 200.0, 0.55)));
        }

        /// <summary>승리 100 + 연승 75씩(최대 +375) + 강한 상대 보너스(20점 차이마다 +4, 최대 +40). 패배 35.</summary>
        public static int BpReward(bool won, int winStreak, int mine, int theirs)
        {
            if (!won) return LossBp;
            int bp = WinBp + StreakBp * Math.Min(5, Math.Max(0, winStreak - 1));
            if (theirs > mine) bp += Math.Min(40, (theirs - mine) / 20 * 4);
            return bp;
        }

        // ------------------------------------------------------------------ 가상 유저 이름

        private struct Country
        {
            public string Name, Code; public string[] Names;
            public Country(string n, string c, params string[] names) { Name = n; Code = c; Names = names; }
        }

        private static readonly Country[] Countries =
        {
            new Country("대한민국", "KR", "민준", "서연", "지호", "하윤", "도윤", "서준", "예린", "태양"),
            new Country("일본", "JP", "Haruto", "Yuki", "Sora", "Mei", "Ren", "Hina", "Kaito", "Sakura"),
            new Country("미국", "US", "Jake", "Emily", "Mike", "Sarah", "Tyler", "Ashley", "Chris", "Jordan"),
            new Country("브라질", "BR", "Lucas", "Gabriel", "Beatriz", "Thiago", "Larissa", "Matheus", "Rafael"),
            new Country("프랑스", "FR", "Louis", "Camille", "Hugo", "Lea", "Theo", "Manon", "Jules"),
            new Country("독일", "DE", "Lukas", "Jonas", "Anna", "Felix", "Lena", "Moritz", "Finn"),
            new Country("스페인", "ES", "Pablo", "Carlos", "Lucia", "Diego", "Sofia", "Alvaro", "Javier"),
            new Country("이탈리아", "IT", "Marco", "Giulia", "Luca", "Alessia", "Matteo", "Chiara"),
            new Country("영국", "GB", "Oliver", "Harry", "Amelia", "George", "Poppy", "Archie"),
            new Country("중국", "CN", "Wei", "Li", "Jun", "Mei", "Chen", "Xiao", "Hao"),
            new Country("인도", "IN", "Arjun", "Priya", "Rohan", "Ananya", "Vikram", "Aditi"),
            new Country("멕시코", "MX", "Santiago", "Valeria", "Mateo", "Camila", "Emiliano", "Ximena"),
            new Country("러시아", "RU", "Dmitri", "Natasha", "Ivan", "Katya", "Sergei", "Olga"),
            new Country("캐나다", "CA", "Liam", "Emma", "Noah", "Olivia", "Ethan", "Chloe"),
            new Country("호주", "AU", "Jack", "Charlotte", "Mitchell", "Matilda", "Cooper", "Isla"),
            new Country("태국", "TH", "Somchai", "Napat", "Pim", "Anan", "Kanya"),
            new Country("인도네시아", "ID", "Budi", "Putri", "Rizky", "Dewi", "Agus", "Sari"),
            new Country("필리핀", "PH", "Juan", "Maria", "Miguel", "Angel", "Carlo", "Bea"),
            new Country("터키", "TR", "Emre", "Elif", "Mert", "Zeynep", "Burak", "Deniz"),
            new Country("폴란드", "PL", "Jakub", "Zuzanna", "Kacper", "Maja", "Szymon"),
            new Country("스웨덴", "SE", "Erik", "Alva", "Oscar", "Elsa", "Axel", "Maja"),
            new Country("아르헨티나", "AR", "Facundo", "Valentina", "Joaquin", "Martina", "Tomas"),
            new Country("베트남", "VN", "Minh", "Linh", "Huy", "Lan", "Duc", "Trang"),
            new Country("이집트", "EG", "Omar", "Nour", "Youssef", "Salma", "Karim"),
            new Country("나이지리아", "NG", "Chidi", "Amara", "Emeka", "Ngozi", "Tunde"),
            new Country("남아프리카공화국", "ZA", "Thabo", "Lerato", "Sipho", "Naledi", "Pieter"),
            new Country("네덜란드", "NL", "Daan", "Sanne", "Bram", "Lotte", "Sem"),
            new Country("칠레", "CL", "Matias", "Isidora", "Benjamin", "Antonia", "Vicente"),
            new Country("대만", "TW", "Wei", "Yu", "Ting", "Jia", "Hsin", "Kai"),
            new Country("사우디아라비아", "SA", "Faisal", "Noura", "Khalid", "Reem", "Saad"),
        };

        private static string Handle(string baseName, string code, IRng rng)
        {
            switch (rng.Next(0, 6))
            {
                case 0: return baseName;
                case 1: return baseName + rng.Next(1, 100);
                case 2: return baseName + "_" + code;
                case 3: return baseName + rng.Next(1980, 2010);
                case 4: return baseName.ToLowerInvariant() + "_" + rng.Next(10, 1000);
                default: return baseName + (rng.Next(0, 2) == 0 ? "TCG" : "Mon");
            }
        }

        public static string RandomName(IRng rng, out string country, out string code)
        {
            var c = Countries[rng.Next(0, Countries.Length)];
            country = c.Name; code = c.Code;
            return Handle(c.Names[rng.Next(0, c.Names.Length)], c.Code, rng);
        }

        // ------------------------------------------------------------------ 상대 생성

        /// <summary>순위표에 있는 실제 사람과 붙는다. 파티는 그 사람의 시즌 씨앗과 시즌 시작 점수(TeamRating)로만 정해져,
        /// 시즌 중에 점수가 오르내려도 늘 같은 조합이다(시즌이 끝나야 바뀐다).</summary>
        public static RankedOpponent FromMember(RankedBoard.Member m, EngineSettings s, IList<RankedAnimal> animals = null, TeamMeta meta = null)
        {
            var rng = new SeededRng(m.Seed);
            int teamR = m.TeamRating > 0 ? m.TeamRating : m.Rating;
            var op = new RankedOpponent { Rating = m.Rating, Format = FormatFor(teamR), Name = m.Name, Country = m.Country, CountryCode = m.Code, TrainerId = m.TrainerId, BoardMember = true };
            if (op.TrainerId == null) { var l = RankedTrainerCatalog.All.ToList(); op.TrainerId = l[rng.Next(0, l.Count)].SpriteId; }
            op.Party = RankedBuilder.Build(op.Format, RankedRules.PartySize, BattleLevel, rng, s, animals, meta, TeamMeta.StrengthFor(teamR));
            return op;
        }

        /// <summary>내 점수 주변(±약 100)의 가상 유저와 6마리 파티를 만든다. animals가 있으면 일부는 림켓몬(동물 껍데기).</summary>
        public static RankedOpponent FindOpponent(int myRating, IRng rng, EngineSettings s, IList<RankedAnimal> animals = null, TeamMeta meta = null)
        {
            int off = (int)Math.Round((rng.NextDouble() + rng.NextDouble() - 1.0) * 140);   // 최대 ±140, 가운데가 많다
            int rating = Math.Max(600, myRating + off);
            var op = new RankedOpponent { Rating = rating, Format = FormatFor(rating) };
            op.Name = RandomName(rng, out op.Country, out op.CountryCode);
            // 일반 NPC 스프라이트: 직업 트레이너 위주(80%), 가끔 고유 인물
            var list = RankedTrainerCatalog.All.ToList();
            var cls = list.Where(t => !t.Named).ToList();
            var pool = rng.NextDouble() < 0.8 && cls.Count > 0 ? cls : list;
            op.TrainerId = pool[rng.Next(0, pool.Count)].SpriteId;
            // 팀 구성 통계가 있으면 점수가 높은 상대일수록 잘 이기던 조합을 들고 나온다(1100점 이하는 지금처럼)
            op.Party = RankedBuilder.Build(op.Format, RankedRules.PartySize, BattleLevel, rng, s, animals, meta, TeamMeta.StrengthFor(rating));
            return op;
        }
    }
}
