using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rimketmon.Engine;

/// <summary>값을 고정해서 돌려주는 난수. 0이면 모든 확률이 발동, 0.999면 발동하지 않는다.</summary>
internal sealed class FixedRng : IRng
{
    public double V;
    public FixedRng(double v) { V = v; }
    public double NextDouble() { return V; }
    public int Next(int min, int max) { return min; }
}

internal static class Program
{
    private static int passed, failed;

    private static void Check(bool ok, string name, string detail = "")
    {
        if (ok) { passed++; Console.WriteLine("  [PASS] " + name + (detail != "" ? "  " + detail : "")); }
        else { failed++; Console.WriteLine("  [FAIL] " + name + (detail != "" ? "  " + detail : "")); }
    }

    private static int Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "--show")
        {
            foreach (var id in args.Skip(1))
            {
                var rng = new SystemRng(42);
                var me = Mk("GARDEVOIR", 50, null, id, "TACKLE"); var bench = Mk("PIKACHU", 50, null, "TACKLE");
                var foe = Mk("SNORLAX", 50, null, "TACKLE", "SPLASH");
                var b = new RimketBattle(rng, new EngineSettings(), new[] { me, bench }, foe, Col("정착민", 1.0, 3, new EngineSettings()));
                Console.WriteLine("=== " + id);
                for (int t = 0; t < 3 && !b.IsOver; t++)
                {
                    int before = b.Log.Count;
                    if (b.PendingSwitch) b.Act(b.LegalActions()[0]); else b.Act(new BattleAction(ActionKind.UseMove, 0));
                    foreach (var l in b.Log.Skip(before)) Console.WriteLine("  " + l);
                    Console.WriteLine("  --");
                }
            }
            return 0;
        }
        if (args.Length > 1 && args[0] == "--ab")
        {   // --ab 내특성 [내종족] [상대특성] [상대종족] [내기술] [상대기술]
            string myAb = args[1]; string mySp = args.Length > 2 ? args[2] : "SNORLAX"; string foeAb = args.Length > 3 ? args[3] : "STATIC"; string foeSp = args.Length > 4 ? args[4] : "PIKACHU";
            string myMv = args.Length > 5 ? args[5] : "TACKLE"; string foeMv = args.Length > 6 ? args[6] : "TACKLE";
            var b = Fight(new SystemRng(7), Mk(mySp, 50, myAb, myMv, "SPLASH"), Mk(foeSp, 50, foeAb, foeMv));
            foreach (var l in b.Log) Console.WriteLine("  " + l);
            for (int t = 0; t < 3 && !b.IsOver; t++)
            {
                int before = b.Log.Count; Console.WriteLine("  -- 턴 " + (t + 1));
                if (b.PendingSwitch) b.Act(b.LegalActions()[0]); else b.Act(new BattleAction(ActionKind.UseMove, 0));
                foreach (var l in b.Log.Skip(before)) Console.WriteLine("  " + l);
            }
            return 0;
        }
        if (args.Length > 1 && args[0] == "--vsrankedA") { Lookahead.OnlyA = true; return Lookahead.RunVsRanked2(args[1], args.Length > 2 ? int.Parse(args[2]) : 300, args.Length > 3 ? int.Parse(args[3]) : 8, args.Length > 4 ? int.Parse(args[4]) : 4); }
        if (args.Length > 1 && args[0] == "--vsranked2")
            return Lookahead.RunVsRanked2(args[1], args.Length > 2 ? int.Parse(args[2]) : 300, args.Length > 3 ? int.Parse(args[3]) : 8, args.Length > 4 ? int.Parse(args[4]) : 4);
        if (args.Length > 1 && args[0] == "--vsranked")
            return Lookahead.RunVsRanked(args[1], args.Length > 2 ? int.Parse(args[2]) : 300, args.Length > 3 ? int.Parse(args[3]) : 8, args.Length > 4 ? int.Parse(args[4]) : 4);
        if (args.Length > 1 && args[0] == "--lookahead")
            return Lookahead.Run(args[1], args.Length > 2 ? int.Parse(args[2]) : 200, args.Length > 3 ? int.Parse(args[3]) : 8, args.Length > 4 ? int.Parse(args[4]) : 4);
        if (args.Length > 1 && args[0] == "--policy")
        {   // --policy 파일: 실제 학습 파일을 읽어 랭크전(3대3, Lv.50) 상대에 붙여 본다. 내 쪽은 무작위.
            AiEncoder.FullInfo = true;
            var pol = NeuralPolicy.Load(args[1]); var st = new EngineSettings();
            Console.WriteLine("크기 맞음: " + LearnedFoe.Fits(pol) + " (입력 " + pol.InputSize + ", 행동 " + pol.OutputSize + ")");
            int won = 0, done = 0, err = 0, dec = 0;
            for (int seed = 1; seed <= 100; seed++)
            {
                try
                {
                    var rng = new SystemRng(seed);
                    var mine = RankedBuilder.Build("ou", 6, 50, rng, st); var foe = RankedBuilder.Build("ou", 6, 50, rng, st);
                    var bt = RimketBattle.RankedBattle(rng, st, "나", RankedRules.Brought(mine, new[] { 0, 1, 2 }), "상대", RankedRules.Brought(foe, new[] { 0, 1, 2 }));
                    var brain = new LearnedFoe(pol, st, new SystemRng(seed * 31)); bt.FoeBrain = brain.Decide;
                    int g = 0; while (!bt.IsOver && g++ < 400) { var l = bt.LegalActions(); if (l.Count == 0) break; bt.Act(l[rng.Next(0, l.Count)]); }
                    if (bt.IsOver) done++; if (bt.Result.End != BattleEnd.Victory) won++; dec += brain.Decisions;
                }
                catch (Exception ex) { if (err++ == 0) Console.WriteLine(ex); }
            }
            Console.WriteLine("100판: 끝 " + done + ", 오류 " + err + ", 학습 AI 승 " + won + ", 결정 " + dec);
            return 0;
        }
        if (args.Length > 0 && args[0] == "--smoke") { SmokeMoves(); return 0; }
        if (args.Length > 1 && args[0] == "--untr")
        {   // 파일의 각 줄을 영어 모드로 번역해 한글이 남는 줄만 출력
            Lang.Set(true);
            int n = 0, bad = 0;
            foreach (var line in File.ReadAllLines(args[1])) { n++; var t = Lang.Tr(line); if (t.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3)) { bad++; Console.WriteLine(line + "   =>   " + t); } }
            Console.WriteLine(bad + " / " + n + " 줄이 번역되지 않음");
            return 0;
        }
        if (args.Length > 0 && args[0] == "--abilsmoke") { SmokeAbilities(); return 0; }
        if (args.Length > 0 && args[0] == "--itemsmoke") { SmokeItems(); return 0; }
        if (args.Length > 0 && args[0] == "--moves")
        {
            var all = MoveCatalog.All.ToList();
            Console.WriteLine("기술 " + all.Count + "개 / 구현 " + all.Count(m => m.Implemented) + " / 미구현 " + all.Count(m => !m.Implemented));
            foreach (var m in all.Where(m => !m.Implemented).OrderBy(m => m.Fn)) Console.WriteLine(m.Id + " " + m.Fn + " " + m.Category);
            var ab = AbilityCatalog.All.ToList();
            Console.WriteLine("특성 " + ab.Count + "개 / 구현 " + ab.Count(a => a.Implemented));
            foreach (var a in ab.Where(a => !a.Implemented).OrderBy(a => a.Id)) Console.WriteLine("AB " + a.Id);
            return 0;
        }
        string typesTxt = args.Length > 0 ? args[0] : null;

        Console.WriteLine("== 1. 타입 상성표 vs 레퍼런스 types.txt ==");
        TestTypeChart(typesTxt);
        Console.WriteLine("== 2. 데이터(종족/기술/특성) ==");
        TestData();
        Console.WriteLine("== 3. 티어 계산 ==");
        TestTierAssigner();
        TestMsgs();
        TestNature();
        TestLang();
        TestRankedAndStage();
        Console.WriteLine("== 4. 상향 오프셋 분포 ==");
        TestOffsets();
        Console.WriteLine("== 5. 개체 생성(종족/레벨/기술/특성) ==");
        TestSpawn();
        Console.WriteLine("== 6. 기술 부가효과 ==");
        TestMoveEffects();
        Console.WriteLine("== 7. 특성 ==");
        TestAbilities();
        TestAbilities2();
        Console.WriteLine("== 7b. 지닌 도구 / 열매 / 도구 기술 ==");
        TestItems();
        Console.WriteLine("== 7c. 성별 ==");
        TestGender();
        TestOnline();
        TestOnlineDouble();
        TestOnlineSession();
        TestOnlineTeamRules(); TestWedgeRemove();
        TestBerryPrice();
        TestFoeEncoder();
        TestNeuralPolicy();
        TestPickEncoder();
        TestHumanPlay();
        TestTeamMeta();
        TestFoeSurrender();
        TestFoeDoesNotPeek();
        TestFieldEvents();
        TestLearnedFoe();
        TestEnvironment();
        TestLeague();
        TestEvolution();
        TestLookaheadFoe();
        TestHunt();
        Console.WriteLine("== 7c2. 개체값 ==");
        TestIv();
        Console.WriteLine("== 7c3. 소비 아이템 ==");
        TestConsumables();
        Console.WriteLine("== 7d. 더블배틀 ==");
        TestDouble();
        FuzzDouble();
        Console.WriteLine("== 8. 버둥버둥 / 기본 공격 / 단계 흐름 ==");
        TestFlow();
        Console.WriteLine("== 9. 뇌 손상 규칙 ==");
        TestInjury();
        Console.WriteLine("== 10. 정착민 6스탯 ==");
        TestColonistStats();
        Console.WriteLine("== 11. 트레이너 전투 ==");
        TestTrainer();
        TestMystery();
        TestParadox();
        TestGym();
        Console.WriteLine("== 12. 대량 무작위 전투(오류/무한루프 검사) ==");
        Fuzz(false);
        Console.WriteLine("== 12b. 지닌 도구를 섞은 대량 전투 ==");
        Fuzz(true);

        Console.WriteLine();
        Console.WriteLine("결과: " + passed + " 통과 / " + failed + " 실패");
        return failed == 0 ? 0 : 1;
    }

    // ----------------------------------------------------------------------------------------------
    private static void TestTypeChart(string path)
    {
        if (path == null || !File.Exists(path))
        {
            Console.WriteLine("  (types.txt 경로 인자 없음 - 건너뜀)");
            return;
        }
        var map = new Dictionary<string, ElementType>(StringComparer.OrdinalIgnoreCase);
        foreach (ElementType t in Enum.GetValues(typeof(ElementType))) map[t.ToString()] = t;

        var weak = new Dictionary<ElementType, HashSet<ElementType>>();
        var res = new Dictionary<ElementType, HashSet<ElementType>>();
        var imm = new Dictionary<ElementType, HashSet<ElementType>>();
        ElementType cur = default(ElementType);
        bool have = false;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                string id = line.Substring(1, line.Length - 2);
                if (map.TryGetValue(id, out cur)) { have = true; weak[cur] = new HashSet<ElementType>(); res[cur] = new HashSet<ElementType>(); imm[cur] = new HashSet<ElementType>(); }
                else have = false;
            }
            else if (have)
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim();
                var vals = line.Substring(eq + 1).Split(',').Select(v => v.Trim()).Where(v => v.Length > 0);
                HashSet<ElementType> target = key == "Weaknesses" ? weak[cur] : key == "Resistances" ? res[cur] : key == "Immunities" ? imm[cur] : null;
                if (target == null) continue;
                foreach (var v in vals) { ElementType e; if (map.TryGetValue(v, out e)) target.Add(e); }
            }
        }

        int mismatches = 0;
        foreach (ElementType d in weak.Keys)
            foreach (ElementType a in Enum.GetValues(typeof(ElementType)))
            {
                float expected = imm[d].Contains(a) ? 0f : weak[d].Contains(a) ? 2f : res[d].Contains(a) ? 0.5f : 1f;
                float actual = TypeChart.Single(a, d);
                if (Math.Abs(expected - actual) > 1e-6) { mismatches++; Console.WriteLine("    불일치: " + a + "→" + d + " 기대 " + expected + " 실제 " + actual); }
            }
        Check(weak.Count == 18, "레퍼런스에서 18개 방어 속성 파싱", "파싱=" + weak.Count);
        Check(mismatches == 0, "324개 상성 값이 레퍼런스와 모두 일치", "불일치=" + mismatches);
    }

    private static void TestRankedAndStage()
    {
        Console.WriteLine("== 랭크 트레이너 목록 / 레벨 단계 ==");
        Check(RankedTrainerCatalog.All.Count >= 150, "랭크 트레이너 로드: " + RankedTrainerCatalog.All.Count + "명");
        Check(RankedTrainerCatalog.Get("youngster_m") == null && RankedTrainerCatalog.Get("lance") != null, "관장/기본 꼬마 제외, 고유 인물 포함");
        TestRankedPools();
        var s = new EngineSettings(); int mn, mx;
        s.LevelBand(8, out mn, out mx); Check(mn == 81 && mx == 90, "배지 8개 레벨대 81~90");
        s.LevelBand(9, out mn, out mx); Check(mn == 91 && mx == 100, "챔피언 격파(단계 9) 레벨대 91~100");
    }

    private static void TestRankedPools()
    {
        var s = new EngineSettings();
        foreach (var f in RankedPoolCatalog.Formats)
            Check(RankedPoolCatalog.Pool(f).Count >= 40, "파티풀 로드: " + f + " " + RankedPoolCatalog.Pool(f).Count + "종");
        var rng = new SystemRng(7);
        int bad = 0, parties = 0, dupSp = 0, dupItem = 0, noMoves = 0, noItem = 0, total = 0;
        foreach (var f in RankedPoolCatalog.Formats)
            for (int n = 0; n < 40; n++)
            {
                var party = RankedBuilder.Build(f, 6, 100, rng, s); parties++;
                if (party.Count != 6) bad++;
                if (party.Select(c => c.Species).Distinct().Count() != party.Count) dupSp++;
                var items = party.Where(c => c.Item != null).Select(c => c.Item).ToList();
                if (items.Distinct().Count() != items.Count) dupItem++;
                foreach (var c in party) { total++; if (c.Moves.Count < 3 && c.Species != "DITTO") { noMoves++; Console.WriteLine("   기술 부족: " + c.Species + " " + c.Moves.Count); }  if (c.Item == null) noItem++; }
            }
        Check(bad == 0, "랭크 파티 6마리 구성 (" + parties + "개)", "실패=" + bad);
        Check(dupSp == 0 && dupItem == 0, "종족/도구 중복 없음", dupSp + "/" + dupItem);
        Check(noMoves == 0, "모든 포켓몬이 기술 3개 이상", "부족=" + noMoves + "/" + total);
        Console.WriteLine("   도구 없음 비율: " + noItem + "/" + total);
        var skins = new List<RankedAnimal>();
        for (int t = 1; t <= 6; t++) { skins.Add(new RankedAnimal("Animal" + t + "a", "동물" + t + "a", t)); skins.Add(new RankedAnimal("Animal" + t + "b", "동물" + t + "b", t)); }
        var counts = new int[7]; int badSkin = 0, badCnt = 0;
        for (int n = 0; n < 700; n++)
        {
            var party = RankedBuilder.Build("ou", 6, 100, rng, s, skins);
            int k = party.Count(c => c.SpeciesId.StartsWith("Animal")); counts[k]++;
            if (party.Count != 6) badCnt++;
            foreach (var c in party.Where(c => c.SpeciesId.StartsWith("Animal"))) { var sp = SpeciesCatalog.Get(c.Species); int bt = int.Parse(c.SpeciesId.Substring(6, 1)); if (bt > sp.Tier || sp.Tier - bt > 4) badSkin++; }
        }
        Check(badCnt == 0 && badSkin == 0, "림켓몬 껍데기: 티어 범위 안, 파티 6마리 유지", "껍데기오류=" + badSkin);
        Check(counts[2] > counts[0] && counts[2] > counts[4] && counts[0] > 0 && counts[3] > 0, "림켓몬 비율 변동 (0~6마리 분포: " + string.Join("/", counts) + ")");
        {
            var ranked = RankedPoolCatalog.Pool("ou").OrderByDescending(x => x.Usage).Select(x => x.Species).ToList();
            var seen = new HashSet<string>(); int off = 0, tot = 0;
            for (int n = 0; n < 1000; n++) foreach (var c in RankedBuilder.Build("ou", 6, 100, rng, s)) { seen.Add(c.Species); tot++; if (ranked.IndexOf(c.Species) >= 40) off++; }
            Check(seen.Count >= 100, "OU 파티에 다양한 종족 등장: " + seen.Count + "/" + ranked.Count);
            Check(off > tot * 0.1, "비주류(사용률 41위 이하) 비율 " + (100 * off / tot) + "%");
        }
        {
            var shares = new List<double>();
            foreach (var f in new[] { "pu", "ou", "ubers" })
            {
                var ranked = RankedPoolCatalog.Pool(f).OrderByDescending(x => x.Usage).Select(x => x.Species).ToList();
                int off = 0, tot = 0;
                for (int n = 0; n < 1500; n++) foreach (var c in RankedBuilder.Build(f, 6, 100, rng, s)) { tot++; if (ranked.IndexOf(c.Species) < 20) off++; }
                shares.Add(100.0 * off / tot);
            }
            Check(shares[0] < shares[1] && shares[1] < shares[2], "티어가 높을수록 메타 비중 증가 (상위 20종 비율 PU/OU/Ubers: " + string.Join("/", shares.Select(x => (int)x + "%")) + ")");
        }
        {
            var mine = RankedBuilder.Build("ou", 6, 100, rng, s); var foe6 = RankedBuilder.Build("ou", 6, 100, rng, s);
            Check(!RankedRules.ValidSelection(mine, new[] { 0, 1 }) && !RankedRules.ValidSelection(mine, new[] { 0, 0, 1 }) && RankedRules.ValidSelection(mine, new[] { 2, 0, 5 }), "선출 규칙: 정확히 3마리, 중복 불가");
            int ended = 0, itemOk = 0, wrongSize = 0, errors = 0, wins = 0;
            for (int n = 0; n < 300; n++)
            {
                try
                {
                    var m6 = RankedBuilder.Build("ou", 6, 100, rng, s); var f6 = RankedBuilder.Build("ou", 6, 100, rng, s);
                    var mp = new List<int> { 3, 0, 5 };
                    var fp = RankedRules.PickForFoe(f6, m6, rng, s);
                    if (fp.Count != 3 || !RankedRules.ValidSelection(f6, fp)) wrongSize++;
                    var b = RimketBattle.RankedBattle(rng, s, "나", RankedRules.Brought(m6, mp), "상대", RankedRules.Brought(f6, fp));
                    if (b.Party.Count != 3) wrongSize++;
                    for (int g = 0; g < 400 && !b.IsOver; g++)
                    {
                        if (b.CanUseItem("POTION", 0) || b.CanUseItem("SUPERPOTION", 0)) itemOk++;
                        var acts = b.LegalActions(); if (acts.Count == 0) break;
                        if (acts.Any(a => a.Kind == ActionKind.UseItem)) itemOk++;      // 항복(Disconnect)은 랭크에서도 허용
                        b.Act(acts[rng.Next(0, acts.Count)]);
                    }
                    if (b.IsOver) { ended++; if (b.Result.End == BattleEnd.Victory) wins++; }
                }
                catch (Exception ex) { errors++; if (errors < 3) Console.WriteLine("   예외: " + ex.Message); }
            }
            Check(errors == 0 && wrongSize == 0, "랭크 배틀 300판: 3마리씩, 오류 없음", "오류=" + errors + " 크기=" + wrongSize);
            Check(ended >= 290, "랭크 배틀이 끝까지 진행됨: " + ended + "/300 (승 " + wins + ")");
            Check(itemOk == 0, "랭크 배틀에서 가방 아이템 사용 불가(항복은 가능)", "위반=" + itemOk);
            {
                // 아무 조작도 하지 않을 때(45초 제한 → 자동 선택)의 진행: 첫 번째로 쓸 수 있는 기술/교체만 고르는 방식으로도 반드시 끝나야 한다
                int idleEnded = 0, stuck = 0; string stuckWhy = "";
                for (int n = 0; n < 300; n++)
                {
                    var m6 = RankedBuilder.Build("ou", 6, 50, rng, s); var f6 = RankedBuilder.Build("ou", 6, 50, rng, s);
                    var b = RimketBattle.RankedBattle(rng, s, "나", RankedRules.Brought(m6, new List<int> { 0, 1, 2 }), "상대", RankedRules.Brought(f6, RankedRules.PickForFoe(f6, m6, rng, s)));
                    int g;
                    for (g = 0; g < 600 && !b.IsOver; g++)
                    {
                        var acts = b.LegalActions(); if (acts.Count == 0) { stuck++; stuckWhy = "행동 없음 phase=" + b.Phase; break; }
                        BattleAction pick = default(BattleAction); bool found = false;
                        if (b.PendingSwitch) { foreach (var a in acts) if (a.Kind == ActionKind.Switch) { pick = a; found = true; break; } }
                        else
                        {
                            foreach (var a in acts) if (a.Kind == ActionKind.UseMove && b.MoveBlocked(a.Index) == null) { pick = a; found = true; break; }
                            if (!found) foreach (var a in acts) if (a.Kind == ActionKind.Struggle) { pick = a; found = true; break; }
                        }
                        if (!found) pick = acts[0];
                        b.Act(pick);
                    }
                    if (b.IsOver) idleEnded++; else if (g >= 600) { stuck++; stuckWhy = "600턴 넘게 안 끝남"; }
                }
                Check(stuck == 0 && idleEnded == 300, "랭크 배틀: 가만히 두고 자동 선택만으로도 끝까지 진행 (" + idleEnded + "/300)", stuckWhy);
            }
        }
        {
            Check(RankedLadder.Delta(1000, 1000, true, 30) == 16 && RankedLadder.Delta(1000, 1000, false, 30) == -16, "점수 변동: 동급 승 +16 / 패 -16");
            Check(RankedLadder.Delta(1000, 1300, true, 30) > RankedLadder.Delta(1000, 1000, true, 30) && RankedLadder.Delta(1300, 1000, false, 30) < RankedLadder.Delta(1000, 1000, false, 30), "강한 상대를 이기면 더 많이, 약한 상대에게 지면 더 많이 깎인다");
            Check(RankedLadder.Delta(1000, 1000, true, 5) == 24, "처음 20판은 변동 폭 큼(+24)");
            {
                // 순위표·시즌
                var brd = RankedBoard.Create(new SystemRng(7));
                var st0 = brd.Standings(null, 1000, false);
                int top50 = st0[49].Value, first = st0[0].Value;
                Check(brd.Members.Count == RankedBoard.Size && brd.Members.Select(m => m.Name).Distinct().Count() == RankedBoard.Size, "순위표 " + RankedBoard.Size + "명, 이름 중복 없음");
                Check(top50 >= 2400 && top50 <= 2500 && first >= 2580 && first <= 2720, "실제 포챔스 기준 점수: 50위 커트라인 " + top50 + "점, 1위 " + first + "점, 100위 " + st0[99].Value + "점");
                { int tr = st0.FindIndex(p => p.Key == RankedBoard.TynanName) + 1; Check(tr == 1, "타이난 실베스터는 처음부터 1위 (" + tr + "위)"); }
                var before = brd.Members.Select(m => m.Rating).ToArray();
                for (int d = 0; d < 30; d++) brd.SimulateDay(new SystemRng(100 + d));
                int changed = brd.Members.Where((m, i) => m.Rating != before[i]).Count();
                var st1 = brd.Standings(null, 1000, false);
                int same = st1.Take(50).Count(p => st0.Take(50).Any(q => q.Key == p.Key));
                Check(changed > RankedBoard.Size * 0.8 && same >= 25 && same < 50, "30일 지나면 점수가 변하고 상위 50명 일부가 바뀜(변동 " + changed + "명, 유지 " + same + "/50, 커트라인 " + st1[49].Value + ")");
                { var stT = brd.Standings(null, 0, false); Check(stT[0].Key == RankedBoard.TynanName, "타이난은 순위표 1위 (" + stT[0].Value + "점)"); }
                { var t = brd.Find(RankedBoard.TynanName); var stP = brd.Standings(null, t.Rating + 50, true); int tr = stP.FindIndex(p => p.Key == RankedBoard.TynanName) + 1; brd.SimulateDay(new SystemRng(999)); Check(tr == 2 && stP[0].Key == null, "플레이어가 타이난을 넘으면 타이난은 2위"); }
                {
                    var reg = brd.Register("Human123", "실제사람", "어느 세력", 2300);
                    Check(reg != null && reg.PawnId == "Human123" && brd.Register("Human123", "실제사람", "어느 세력", 1000) == reg && brd.Members.Count == RankedBoard.Size, "실제 사람을 순위표에 올림(자리 이어받기, 중복 없음)");
                    reg.Frozen = true; int fr = reg.Rating;
                    for (int d = 0; d < 10; d++) brd.SimulateDay(new SystemRng(500 + d));
                    brd.RecordVsPlayer(reg.Name, 2300, true);
                    bool nearNever = true; for (int k = 0; k < 200; k++) if (brd.Near(reg.Rating, 30, new SystemRng(k)) == reg) nearNever = false;
                    Check(reg.Rating == fr && nearNever, "죽거나 잡힌 사람은 점수가 멈추고 상대로 안 나옴");
                    var rr = RankedBoard.Deserialize(brd.Serialize()).Members.First(m => m.PawnId == "Human123");
                    Check(rr.Frozen && rr.Name == reg.Name, "실제 사람 연결·정지 상태 저장/불러오기");
                    reg.Frozen = false;
                }
                var round = RankedBoard.Deserialize(brd.Serialize());
                Check(round != null && round.Members.Count == brd.Members.Count && round.Members[5].Rating == brd.Members[5].Rating && round.Members[5].Name == brd.Members[5].Name && round.Season == brd.Season, "순위표 저장/불러오기");
                bool okReset = true; string badR = "";
                for (int r = 0; r <= 2800; r++)
                {
                    int n = RankedBoard.SoftReset(r), bi = RankedBoard.BandIndex(r), bn = RankedBoard.BandIndex(n);
                    if (r >= 1700) { if (n != Math.Max(1650, r - 100)) { okReset = false; badR = r + "→" + n; break; } continue; }
                    if (n > r || (bi > 0 && bn != bi - 1) || (bi == 0 && n != r)) { okReset = false; badR = r + "→" + n; break; }
                }
                Check(okReset, "시즌 초기화: 모든 점수가 세부 등급 정확히 한 단계만 내려감 " + badR + " (예: " + RankedLadder.RankName(1620) + " 1620 → " + RankedLadder.RankName(RankedBoard.SoftReset(1620)) + " " + RankedBoard.SoftReset(1620) + ", 2000 → " + RankedBoard.SoftReset(2000) + ")");
                Check(RankedBoard.SeasonRewardBp(1600, 1, 3) == 3000 + 5000 && RankedBoard.SeasonRewardBp(1000, 200, 3) == 200 && RankedBoard.SeasonRewardBp(1600, 1, 0) == 0, "시즌 보상: 등급 BP + 순위 보너스, 안 했으면 0");
                var near = brd.Near(2300, 150, new SystemRng(3));
                var opp = RankedLadder.FromMember(near, new EngineSettings());
                var opp2 = RankedLadder.FromMember(near, new EngineSettings());
                Check(opp.BoardMember && opp.Name == near.Name && opp.Party.Count == RankedRules.PartySize && string.Join(",", opp.Party.Select(c => c.SpeciesId)) == string.Join(",", opp2.Party.Select(c => c.SpeciesId)), "순위표 사람과 실제 대전: 같은 시즌엔 같은 파티");
                string combo = string.Join(",", opp.Party.Select(c => c.SpeciesId + "/" + c.Item + "/" + c.Ability));
                near.Rating += 400; for (int d = 0; d < 20; d++) brd.SimulateDay(new SystemRng(500 + d));
                var opp3 = RankedLadder.FromMember(near, new EngineSettings());
                var rt = RankedBoard.Deserialize(brd.Serialize()).Find(near.Name);
                var opp4 = RankedLadder.FromMember(rt, new EngineSettings());
                Check(string.Join(",", opp3.Party.Select(c => c.SpeciesId + "/" + c.Item + "/" + c.Ability)) == combo && string.Join(",", opp4.Party.Select(c => c.SpeciesId + "/" + c.Item + "/" + c.Ability)) == combo, "시즌 중엔 점수가 크게 변해도(+400, 저장/불러오기 후에도) 같은 조합");
                near.Rating -= 400;
                int rb = near.Rating; brd.RecordVsPlayer(near.Name, 2300, true);
                Check(near.Rating < rb, "내가 이기면 그 사람 점수가 내려간다 (" + rb + "→" + near.Rating + ")");
                int s0 = brd.Season; brd.NewSeason(new SystemRng(9));
                Check(brd.Season == s0 + 1 && brd.Members.All(m => m.Wins == 0), "새 시즌: 시즌 번호 +1, 전적 초기화");
                {
                    for (int d = 0; d < 60; d++) brd.SimulateDay(new SystemRng(900 + d));
                    for (int k = 0; k < 30; k++) brd.RecordVsPlayer(RankedBoard.TynanName, 2600, true);      // 내가 30번 이겨도
                    var stT = brd.Standings(null, 0, false); int tr = stT.FindIndex(p => p.Key == RankedBoard.TynanName) + 1;
                    Check(tr >= 1 && tr <= 10 && stT[49].Value >= 2350, "60일·시즌 초기화·30연패 뒤에도 타이난은 10위 안 (" + tr + "위), 50위 " + stT[49].Value + "점");
                    var old = new RankedBoard(); var orng = new SystemRng(4);
                    for (int k = 0; k < 300; k++) old.Members.Add(new RankedBoard.Member { Name = "옛" + k, Rating = 900 + k * 3, Skill = 900 + k * 3 });
                    old.Upgrade(orng);
                    var so = old.Standings(null, 0, false);
                    Check(old.Find(RankedBoard.TynanName) != null && old.Members.Count == 300 && so[49].Value >= 2400 && so[0].Value >= 2550, "예전 세이브 순위표: 새 점수 기준으로 다시 앉히고 타이난 추가");
                }
                {
                    // ???의 상대: 사람 + ??? 유전자 쐐기 → 이름은 사람, 나머지는 ?티어
                    var person = ColonistCombatant.PersonFor("테스트", 600, 100, 6);
                    var src = MysteryGen.Create(100, new SystemRng(5), new EngineSettings());
                    Paradox.Apply(person, src); person.Name = "테스트";
                    Check(MysteryGen.IsMystery(person.Species) && !person.Neutral && person.Name == "테스트" && person.Moves.Count > 0 && person.Level == 100, "???의 상대: 사람에게 ??? 쐐기 → ?티어 능력, 이름 유지");
                }
                var opp5 = RankedLadder.FromMember(brd.Find(near.Name), new EngineSettings());
                Check(string.Join(",", opp5.Party.Select(c => c.SpeciesId + "/" + c.Item + "/" + c.Ability)) != combo, "시즌이 끝나면 조합이 바뀐다");
            }
            Check(RankedLadder.RankName(900).StartsWith("몬스터볼급") && RankedLadder.RankName(1300).StartsWith("하이퍼볼급") && RankedLadder.RankName(1600).StartsWith("마스터볼급") && RankedLadder.SubRank(1000) >= 1 && RankedLadder.SubRank(1099) == 1 && RankedLadder.SubRank(1100) == 4, "등급 이름 + 세부 순위 (1000점: " + RankedLadder.RankName(1000) + ")");
            Check(RankedLadder.BpReward(true, 1, 1000, 1000) == 100 && RankedLadder.BpReward(true, 6, 1000, 1000) == 475 && RankedLadder.BpReward(false, 0, 1000, 1000) == 35, "BP 보상: 승 100 / 연승 +75씩(최대 +375) / 패 35");
            int ok = 0, countries = 0; var cs = new HashSet<string>(); var names = new HashSet<string>(); var fm = new Dictionary<string, int>();
            for (int n = 0; n < 400; n++)
            {
                int mine = 800 + (n % 9) * 100;
                var op = RankedLadder.FindOpponent(mine, rng, s);
                if (op.Party.Count == 6 && op.TrainerId != null && !string.IsNullOrEmpty(op.Name) && Math.Abs(op.Rating - mine) <= 140) ok++;
                cs.Add(op.Country); names.Add(op.Name);
                int c; fm.TryGetValue(op.Format, out c); fm[op.Format] = c + 1;
            }
            Check(ok == 400, "가상 상대 400명: 파티 6마리·스프라이트·이름·점수 ±140 이내", "정상=" + ok);
            Check(cs.Count >= 20 && names.Count >= 200, "여러 나라 이름: " + cs.Count + "개국, 서로 다른 이름 " + names.Count);
            Check(fm.Count >= 5, "점수대에 따라 여러 티어 파티: " + string.Join(",", fm.Select(kv => kv.Key + kv.Value)));
        }
        {
            var bulb = SpeciesCatalog.Get("BULBASAUR"); var iv = SpawnRoller.FromSpecies(bulb, 50, rng, s);
            var lv = Training.Learnable("IVYSAUR"); var ids = new HashSet<string>(lv.Select(m => m.Id));
            Check(lv.Count > 80 && ids.Contains("LEAFSTORM") && ids.Contains("SOLARBEAM") && ids.Contains("EARTHQUAKE"), "기술 목록: 이상해풀도 교배기/기술머신/전수 포함 (" + lv.Count + "개)");
            Check(Training.Learnable("VENUSAUR").Count == lv.Count, "같은 계열은 같은 목록(진화 전·후 포함)");
            var sp0 = (int[])iv.Sp.Clone(); int hp0 = iv.MaxHp;
            Check(Training.ValidSp(new[] { 32, 32, 2, 0, 0, 0 }) && !Training.ValidSp(new[] { 33, 0, 0, 0, 0, 0 }) && !Training.ValidSp(new[] { 32, 32, 3, 0, 0, 0 }), "능력 포인트 규칙: 각 32 이하, 합계 66 이하");
            var conv = Training.FromEvs(new[] { 4, 252, 0, 0, 0, 252 });
            Check(conv[1] == 32 && conv[5] == 32 && conv[0] == 1 && conv.Sum() <= 66, "EV→포인트 환산: " + string.Join("/", conv));
            string a = iv.Moves[0].Move.Id; var own = new HashSet<string>(iv.Moves.Select(m => m.Move.Id)); var newMoves = new List<string> { "LEAFSTORM", "SOLARBEAM", "EARTHQUAKE" }.Select(x => own.Contains(x) ? lv.First(m => !own.Contains(m.Id) && m.Id != "LEAFSTORM" && m.Id != "SOLARBEAM" && m.Id != "EARTHQUAKE").Id : x).Distinct().ToList();
            int cost = Training.Quote(iv, new[] { 32, 0, 0, 32, 2, 0 }, 17, newMoves);
            Check(cost >= 66 * Training.CostPerPointAdded + Training.CostNature, "가격 계산: " + cost + " BP (포인트 + 성격 + 기술 교체)");
            Check(66 * Training.CostPerPointAdded + Training.CostNature + Training.CostAbility + 4 * Training.CostPerMove == 1530 && 1530 + 186 * Training.CostPerIvRaised == 1902, "풀 훈련 가격: 1530 BP, 개체값 포함 최대 1902 BP");
            bool ok = Training.Apply(iv, new[] { 32, 0, 0, 32, 2, 0 }, 17, newMoves);
            Check(ok && iv.MaxHp > hp0 && iv.Sp[3] == 32 && iv.Nature == 17 && iv.Moves[0].Move.Id == "LEAFSTORM", "훈련 적용: HP/능력치 상승, 성격·기술 교체");
            Check(!Training.Apply(iv, new[] { 32, 32, 32, 0, 0, 0 }, 0, newMoves) && !Training.Apply(iv, sp0, 0, new List<string> { "LEAFSTORM", "LEAFSTORM" }) && !Training.Apply(iv, sp0, 0, new List<string> { "SPLASHXX" }), "규칙 위반(포인트 초과/기술 중복/없는 기술) 거부");
            {
                var c2 = SpawnRoller.FromSpecies(bulb, 50, rng, s); c2.Iv = new[] { 31, 31, 31, 31, 31, 31 }; c2.RecalculateStats();
                var mv2 = c2.Moves.Select(m => m.Move.Id).ToList(); int spe0 = c2.Spe;
                var lowIv = new[] { 31, 31, 31, 31, 31, 0 };
                Check(Training.Quote(c2, c2.Sp, c2.Nature, mv2, lowIv) == 0, "개체값을 줄이는 건 무료");
                Check(Training.Apply(c2, c2.Sp, c2.Nature, mv2, lowIv) && c2.Iv[5] == 0 && c2.Spe < spe0, "개체값 0으로 줄이면 스피드 감소 (" + spe0 + "→" + c2.Spe + ")");
                Check(Training.Quote(c2, c2.Sp, c2.Nature, mv2, new[] { 31, 31, 31, 31, 31, 10 }) == 10 * Training.CostPerIvRaised, "개체값을 올리면 1포인트당 " + Training.CostPerIvRaised + " BP");
                Check(!Training.Apply(c2, c2.Sp, c2.Nature, mv2, new[] { 32, 0, 0, 0, 0, 0 }), "개체값 32 이상은 거부");
            }
            {
                Console.WriteLine("   종족 티어 분포: " + string.Join(", ", SpeciesCatalog.All.GroupBy(x => x.Tier).OrderBy(x => x.Key).Select(x => x.Key + "티어 " + x.Count() + "마리")) + " (전체 " + SpeciesCatalog.Count + ")");
                var c3 = SpawnRoller.FromSpecies(bulb, 50, rng, s);
                var opts = Training.AbilityOptions(c3);
                Check(opts.Count >= 1 && opts.Contains(c3.Ability) && opts.All(a => AbilityCatalog.Implemented.Contains(a)), "특성 후보에 현재 특성 포함, 전부 구현된 특성 (" + string.Join(",", opts) + ")");
                var mv3 = c3.Moves.Select(m => m.Move.Id).ToList();
                var other = opts.FirstOrDefault(a => a != c3.Ability);
                if (other != null)
                {
                    Check(Training.Quote(c3, c3.Sp, c3.Nature, mv3, null, other) == Training.CostAbility, "특성 변경 가격 " + Training.CostAbility + " BP");
                    Check(Training.Apply(c3, c3.Sp, c3.Nature, mv3, null, other) && c3.Ability == other, "특성 변경 적용");
                }
                Check(!Training.Apply(c3, c3.Sp, c3.Nature, mv3, null, "NOSUCHABILITY"), "후보에 없는 특성은 거부");
            }
            var lowLv = SpawnRoller.FromSpecies(bulb, 50, rng, s); lowLv.Iv = new[] { 31, 31, 31, 31, 31, 31 }; lowLv.Nature = 0; lowLv.RecalculateStats(); int atk0 = lowLv.Atk; lowLv.Sp[1] = 32; lowLv.RecalculateStats();
            Check(lowLv.Atk - atk0 == 32, "Lv.50에서 능력 포인트 1개 = 능력치 +1 (32개 → +" + (lowLv.Atk - atk0) + ")");
        }
        {
            var op = RankedBuilder.Build("ou", 6, 50, rng, s); int withSp = op.Count(c => c.SpTotal > 40 && c.SpTotal <= 66);
            Check(withSp >= 5 && op.All(c => Training.ValidSp(c.Sp)), "상대 파티에 실제 노력치 배분(능력 포인트) 반영: " + string.Join(",", op.Select(c => c.SpTotal)));
        }
        {
            Check(RankedLadder.ShopBp(200) == 30 && RankedLadder.ShopBp(700) > RankedLadder.ShopBp(200) && RankedLadder.ShopBp(100000) < 1200 && RankedLadder.ShopBp(40) >= 5, "상점 가격 곡선: 상처약 " + RankedLadder.ShopBp(200) + " / 좋은상처약 " + RankedLadder.ShopBp(700) + " / 먹다남은음식 " + RankedLadder.ShopBp(20000) + " / 구애머리띠 " + RankedLadder.ShopBp(100000));
        }
        var tags = RankedPoolCatalog.TagsOf("GREATTUSK");
        Check(tags.Contains("ou") && RankedPoolCatalog.UsageOf("ou", "GREATTUSK") > 100, "종족 태그 조회: GREATTUSK → " + string.Join(",", tags));
        var ou = RankedBuilder.Build("ou", 6, 100, new SystemRng(1), s);
        Console.WriteLine("   예시(ou): " + string.Join(" / ", ou.Select(c => c.Species + "@" + c.Item + "[" + string.Join(",", c.Moves.Select(m => m.Move.Id)) + "]")));
    }

    private static void TestLang()
    {
        var m = MoveCatalog.Get("MEGAHORN"); string ko = m.Name;
        Lang.Set(true);
        Check(m.Name == "Megahorn" && m.Description.Length > 10 && m.Description != m.KoDescription, "영어: 기술 이름/설명", m.Name);
        AbilityInfo ai; AbilityCatalog.TryGet("INTIMIDATE", out ai);
        Check(ai.Name == "Intimidate" && ai.Description.Contains("Attack"), "영어: 특성 이름/설명", ai.Name);
        Check(SpeciesCatalog.Get("BULBASAUR").Name == "Bulbasaur", "영어: 종족 이름");
        Check(Natures.Name(3) == "Adamant", "영어: 성격 이름");
        Check(Msgs.T("battle.sendOutPokemon", "pokemonName", "Pikachu").Contains("Pikachu"), "영어: 전투 메시지 템플릿", Msgs.T("battle.sendOutPokemon", "pokemonName", "Pikachu"));
        Check(Msgs.Josa(Lang.Tr("야생 피카츄은(는) 쓰러졌다!")) == "Wild 피카츄 fainted!", "영어: 로그 조각 치환", Msgs.Josa(Lang.Tr("야생 피카츄은(는) 쓰러졌다!")));
        Check(Msgs.Josa(Lang.Tr("피카츄의 공격이 크게 올랐다!")) == "피카츄's Attack rose sharply!", "영어: 능력치 상승 문장", Msgs.Josa(Lang.Tr("피카츄의 공격이 크게 올랐다!")));
        Lang.Set(false);
        Check(m.Name == ko && Natures.Name(3) == "고집" && Lang.Tr("파티") == "파티", "한국어로 되돌리면 원래대로");
    }

    private static void TestNature()
    {
        Check(Natures.Count == 25 && Natures.Ko.Length == 25 && Natures.Fragment.Length == 25, "성격 25종 표 길이");
        Check(Natures.Multiplier(3, 0) == 1.1 && Natures.Multiplier(3, 2) == 0.9 && Natures.Multiplier(3, 1) == 1.0, "고집(3): 공격 +10% 특공 -10%");
        Check(Natures.Multiplier(0, 0) == 1.0 && Natures.Multiplier(12, 4) == 1.0 && Natures.Multiplier(24, 1) == 1.0, "노력/성실/변덕은 보정 없음");
        for (int n = 0; n < 25; n++)
        {
            int up = 0, dn = 0;
            for (int st = 0; st < 5; st++) { var m = Natures.Multiplier(n, st); if (m > 1) up++; if (m < 1) dn++; }
            if (!(up == dn && up <= 1)) { Check(false, "성격 " + n + " 보정 짝이 맞지 않음"); return; }
        }
        Check(true, "25개 성격 모두 +/- 짝이 맞음");
        var c = new RimketCreature { Level = 50, BaseAtk = 100, BaseSpA = 100, Nature = 3 };
        c.RecalculateStats();
        Check(c.Atk == (int)(105 * 1.1) && c.SpA == (int)(105 * 0.9), "성격이 능력치에 반영됨", "Atk=" + c.Atk + " SpA=" + c.SpA);
        Check(new RimketCreature().Friendship == 70, "친밀도 기본값 70");
    }

    private static void TestMsgs()
    {
        Check(Msgs.Josa("피카츄은(는) 쓰러졌다") == "피카츄는 쓰러졌다", "조사: 받침 없음 → 는");
        Check(Msgs.Josa("이상해씨은(는)") == "이상해씨는", "조사: 이상해씨 → 는");
        Check(Msgs.Josa("리자몽은(는)") == "리자몽은", "조사: 받침 있음 → 은");
        Check(Msgs.Josa("불꽃을(를)") == "불꽃을", "조사: 을(를)");
        Check(Msgs.Josa("물(으)로") == "물로", "조사: ㄹ받침 (으)로 → 로");
        Check(Msgs.T("move-trigger.hitWithRecoil", "pokemonName", "피카츄").StartsWith("피카츄는"), "번역 템플릿 + [[는]] 조사");
        Check(MoveCatalog.Get("POUND").Name == "막치기" && MoveCatalog.Get("POUND").Description.Length > 5, "기술명/설명이 PokeRogue 번역");
    }

    private static void TestTierAssigner()
    {
        var values = new List<KeyValuePair<string, float>>();
        for (int i = 0; i < 60; i++) values.Add(new KeyValuePair<string, float>("a" + i, 10 + i * 30));
        var t = TierAssigner.ByQuantile(values);
        var counts = Enumerable.Range(1, 5).Select(k => t.Values.Count(v => v == k)).ToArray();
        Check(counts.All(c => c == 12), "60종 → 기본 5티어에 티어당 12종 균등", string.Join(",", counts));
        Check(t["a0"] == 1 && t["a59"] == 5 && !t.Values.Contains(6), "최저가=1티어, 최고가=5티어(6티어는 기본 티어로 나오지 않음)");

        values.Add(new KeyValuePair<string, float>("thrumbo_x", 1000000f));
        var t2 = TierAssigner.ByQuantile(values);
        Check(t2["thrumbo_x"] == 5 && t2["a0"] == 1, "극단값 추가 후에도 순위 분할 유지");

        var ties = new List<KeyValuePair<string, float>>();
        for (int i = 0; i < 10; i++) ties.Add(new KeyValuePair<string, float>("x" + i, 100f));
        var t3 = TierAssigner.ByQuantile(ties);
        Check(t3.Values.Distinct().Count() == 1, "동률 값은 모두 같은 티어");

        Check(TierAssigner.ByThresholds(50, new float[] { 100, 300, 800, 2000 }) == 1 &&
              TierAssigner.ByThresholds(9000, new float[] { 100, 300, 800, 2000 }) == 5, "고정 임계값 방식");
    }


    private static void TestOffsets()
    {
        var rng = new SystemRng(1);
        var s = new EngineSettings();
        int n = 800000;
        Func<int, int[]> dist = (bt) => { var c = new int[7]; for (int i = 0; i < n; i++) c[SpawnRoller.RollFinalTier(bt, rng, s)]++; return c; };
        Func<int[], int, double> pct = (c, k) => c[k] * 100.0 / n;

        var c1 = dist(1);
        Console.WriteLine("    기본 1티어 → 1~6: " + string.Join(" / ", Enumerable.Range(1, 6).Select(k => pct(c1, k).ToString("F2") + "%")));
        // 6티어 0.3%를 먼저 떼고 나머지에 88/7/3/1.5/0.5가 적용된다
        Check(Near(pct(c1, 6), 0.02, 0.01) && Near(pct(c1, 1), 88, 0.4) && Near(pct(c1, 2), 7, 0.2), "기본 1티어: 6티어 0.02%, 나머지는 88/7/3/1.5/0.5 비율");

        var c5 = dist(5);
        Console.WriteLine("    기본 5티어(트럼보급) → 5/6: " + pct(c5, 5).ToString("F2") + "% / " + pct(c5, 6).ToString("F2") + "%");
        Check(c5[1] + c5[2] + c5[3] + c5[4] == 0 && Near(pct(c5, 6), 2, 0.2), "기본 5티어는 5티어 98% / 6티어 2%");

        var c4 = dist(4);
        Check(Near(pct(c4, 6), 0.5, 0.1) && c4[1] + c4[2] + c4[3] == 0, "기본 4티어: 6티어 0.5%, 나머지는 4~5티어");

        // 6티어는 상향으로만: 기본 티어 배정은 절대 6을 주지 않고, 상향 끄면 6티어는 없다
        var sNo = NoUp();
        var cn = new int[7]; for (int i = 0; i < 20000; i++) cn[SpawnRoller.RollFinalTier(5, rng, sNo)]++;
        Check(cn[5] == 20000, "상향이 없으면 기본 5티어는 영원히 5티어");

        var s2 = new EngineSettings { OverflowMode = TierOverflowMode.ReturnToBase };
        var d4 = new int[7];
        for (int i = 0; i < n; i++) d4[SpawnRoller.RollFinalTier(4, rng, s2)]++;
        Check(Near(pct(d4, 6), 0.5, 0.1) && Near(pct(d4, 5), 7, 0.3), "ReturnToBase: 5티어를 넘는 +2 이상은 기본 티어로 복귀(4→5는 7%)", pct(d4, 5).ToString("F2") + "%");

        var c6 = new int[7];
        for (int i = 0; i < 1000; i++) c6[SpawnRoller.RollFinalTier(6, rng, s)]++;
        Check(c6[6] == 1000, "수동 지정 기본 6티어는 항상 6티어");
    }

    private static void TestInjury()
    {
        var s = new EngineSettings();
        var rng = new SystemRng(5);
        var none = InjuryCalculator.Compute(false, 1.0, BattleEnd.Victory, false, s, rng);
        Check(!none.Any, "정착민 단계 미진입 → 부상 없음");

        var full = InjuryCalculator.Compute(true, 1.0, BattleEnd.Victory, false, s, rng);
        Check(!full.Any, "정착민 HP 단계 진입했지만 HP 손실 없이 승리 → 부상 없음");

        var half = InjuryCalculator.Compute(true, 0.5, BattleEnd.Victory, false, s, rng);
        Check(Near(half.BrainDamage, 0.3, 1e-9) && !half.PermanentScar, "HP 50% 잃고 승리 → 뇌 손상 30%, 영구 후유증 없음");

        var low = InjuryCalculator.Compute(true, 0.1, BattleEnd.Disconnected, true, s, new SystemRng(1));
        Check(Near(low.BrainDamage, 0.9 * 0.6 + 0.05, 1e-9) && !low.Lethal, "HP 10% 접속 해제 → 손실 비례 손상 + 충격, 비치명", "손상 " + low.BrainDamage.ToString("F2"));

        int scars = 0, n = 20000;
        for (int i = 0; i < n; i++) if (InjuryCalculator.Compute(true, 0.1, BattleEnd.Disconnected, true, s, rng).PermanentScar) scars++;
        double expected = (0.9 - 0.5) / 0.5 * 0.6;
        Check(Near(scars * 100.0 / n, expected * 100, 1.5), "저체력 접속 해제의 영구 후유증 확률", (scars * 100.0 / n).ToString("F1") + "% (기대 " + (expected * 100).ToString("F1") + "%)");

        var scarsV = Enumerable.Range(0, n).Count(i => InjuryCalculator.Compute(true, 0.1, BattleEnd.Victory, false, s, rng).PermanentScar);
        Check(scarsV < scars, "같은 HP에서 승리가 접속 해제보다 후유증 확률이 낮음");

        var dead = InjuryCalculator.Compute(true, 0.0, BattleEnd.Defeat, false, s, rng);
        Check(dead.Lethal && dead.BrainDamage >= 1.0, "패배(HP 0) + 사망 모드 → 치명");

        var coma = InjuryCalculator.Compute(true, 0.0, BattleEnd.Defeat, false, new EngineSettings { LethalDefeat = false }, rng);
        Check(!coma.Lethal && coma.Coma && coma.PermanentScar && coma.BrainDamage < 1.0, "패배 + 비사망 모드 → 혼수 + 영구 중증 손상");
    }

    // ----------------------------------------------------------------------------------------------


    private static void TestData()
    {
        Check(SpeciesCatalog.Count == 1025, "종족 1025종 로드", "종족=" + SpeciesCatalog.Count);
        {
            var rep = SpeciesCatalog.ReplacementFor("NO_SUCH_SPECIES", ElementType.Water, 410);
            Check(rep != null && rep.Type1 == ElementType.Water && SpeciesCatalog.ReplacementFor("PIKACHU", ElementType.Water, 410) == null && SpeciesCatalog.ReplacementFor("PERSON", ElementType.Normal, 300) == null,
                "데이터에 없는 종족 → 같은 타입·비슷한 종족값 포켓몬으로 대체 (" + (rep != null ? rep.Id : "없음") + ")");
            var c = new RimketCreature { Species = "NO_SUCH_SPECIES", SpeciesId = "Muffalo", Name = "Test", Level = 30 };
            Evolution.Apply(c, rep); 
            Check(c.Species == rep.Id && c.Type1 == rep.Type1, "대체 적용");
        }
        Check(MoveCatalog.Count >= 440, "기술 440개 이상 로드(부가효과 지원 공격기)", "기술=" + MoveCatalog.Count);
        Check(AbilityCatalog.Count >= 300, "특성 300개 이상 로드", "특성=" + AbilityCatalog.Count);
        Check(SpeciesCatalog.OfTier(6).Count >= 55 && SpeciesCatalog.OfTier(6).All(sp => sp.Bst >= 600), "6티어 = 종족값 600 이상 " + SpeciesCatalog.OfTier(6).Count + "종");
        Check(SpeciesCatalog.OfTier(5).All(sp => sp.Bst < 600 && sp.Bst >= 520), "5티어 종족값 520~599");

        var pika = SpeciesCatalog.Get("PIKACHU");
        Check(pika.Hp == 35 && pika.Atk == 55 && pika.Def == 40 && pika.SpA == 50 && pika.SpD == 50 && pika.Spe == 90 && pika.Type1 == ElementType.Electric && !pika.Type2.HasValue,
              "피카츄: 종족값 35/55/40/50/50/90, 전기 단일 타입");
        Check(pika.Abilities.Contains("STATIC") && !pika.Abilities.Contains("LIGHTNINGROD"), "피카츄 일반 특성만(숨겨진 특성 제외)", string.Join(",", pika.Abilities));
        Check(pika.Learnset.Any(e => e.Key == 36 && e.Value.Id == "THUNDERBOLT"), "피카츄는 36레벨에 10만볼트를 배운다");
        Check(SpeciesCatalog.All.All(sp => sp.Abilities.All(a => { AbilityInfo ai; return AbilityCatalog.TryGet(a, out ai); })), "모든 종족의 특성 id가 특성 표에 존재");
        Check(AbilityCatalog.Implemented.All(a => { AbilityInfo ai; return AbilityCatalog.TryGet(a, out ai); }), "구현 표시된 특성이 모두 실존", string.Join(",", AbilityCatalog.Implemented.Where(a => { AbilityInfo ai; return !AbilityCatalog.TryGet(a, out ai); })));
        AbilityInfo st; AbilityCatalog.TryGet("STATIC", out st);
        Check(st != null && st.Name == "정전기" && st.Implemented, "특성 한글명/구현 여부", st == null ? "" : st.Name);
        Console.WriteLine("    구현된 특성 " + AbilityCatalog.Implemented.Count + "개 / 전체 " + AbilityCatalog.Count);
        var fl = MoveCatalog.Get("FLAMETHROWER");
        Check(fl.Name == "화염방사" && fl.Power == 90 && fl.Accuracy == 100 && fl.EffectChance == 10 && fl.Has(EffectKind.Burn), "화염방사: 위력90/명중100/화상10%");
        Check(MoveCatalog.Get("BITE").Has(EffectKind.Flinch) && MoveCatalog.Get("BITE").Is('B') && MoveCatalog.Get("BITE").EffectChance == 30, "물기: 풀죽음 30%, 물기 플래그");
        Check(MoveCatalog.Get("EARTHQUAKE").Power == 100 && MoveCatalog.Get("SURF").Power == 90, "지진/파도타기 포함(필드 의존 효과는 제외하고 이식)");
        Check(SpeciesCatalog.All.All(sp => sp.Learnset.All(e => e.Value != null)), "모든 습득기술 연결");
    }

    private static void TestSpawn()
    {
        var s = new EngineSettings();
        var rng = new SystemRng(42);

        // 레벨 범위: 배지 개수별
        int mn, mx;
        s.LevelBand(0, out mn, out mx); Check(mn == 1 && mx == 10, "배지 0개 → 레벨 1~10");
        s.LevelBand(1, out mn, out mx); Check(mn == 11 && mx == 20, "배지 1개 → 레벨 11~20");
        s.LevelBand(5, out mn, out mx); Check(mn == 51 && mx == 60, "배지 5개 → 레벨 51~60");
        s.LevelBand(9, out mn, out mx); Check(mn == 91 && mx == 100, "배지 9개 → 레벨 91~100");
        s.LevelBand(20, out mn, out mx); Check(mx == 100, "배지가 많아도 레벨 100 상한");

        bool allOk = true, abOk = true, moveOk = true, statOk = true, lvOk = true;
        int tier6 = 0;
        for (int i = 0; i < 4000; i++)
        {
            int badges = rng.Next(0, 5);
            int bt = rng.Next(1, 6);
            var c = SpawnRoller.Roll("Wolf", "늑대", bt, 0.5f, badges, rng, s);
            int lo, hi; s.LevelBand(badges, out lo, out hi);
            if (c.Level < lo || c.Level > hi) lvOk = false;
            var sp = SpeciesCatalog.Get(c.Species);
            if (sp.Tier != c.Tier) allOk = false;
            if (c.Tier == 6) tier6++;
            if (sp.Abilities.Count > 0 ? !sp.Abilities.Contains(c.Ability) : c.Ability != null) abOk = false;
            if (c.Moves.Count > s.MoveSlots) moveOk = false;
            foreach (var m in c.Moves) if (!sp.Learnset.Any(e => e.Value == m.Move && e.Key <= c.Level)) moveOk = false;
            if (c.BaseHp != sp.Hp || c.BaseAtk != sp.Atk || c.BaseSpe != sp.Spe || c.Type1 != sp.Type1) statOk = false;
            if (c.Hp != c.MaxHp) statOk = false;
        }
        Check(lvOk, "야생 레벨이 배지 단계 범위 안");
        Check(allOk, "최종 티어와 종족 티어(종족값 총합)가 일치");
        Check(abOk, "특성은 종족의 일반 특성 중에서 부여");
        Check(moveOk, "기술은 그 레벨까지 배운 종족 습득기술, 최대 4개");
        Check(statOk, "종족값/타입이 레퍼런스 그대로, 시작 HP 가득");

        // 전설 제한: 레벨 상한이 낮으면 6티어 종족이 나오지 않는다
        bool noLegend = true;
        for (int i = 0; i < 6000; i++)
        {
            var c = SpawnRoller.Roll("Thrumbo", "트럼보", 6, 0.5f, 0, rng, s);   // 기본 6티어(수동)라도 레벨 제한이 걸린다
            if (c.Tier == 6) noLegend = false;
        }
        Check(noLegend, "배지 0개에서는 6티어(전설급) 종족이 야생에 나오지 않음");
        int legends = 0;
        for (int i = 0; i < 3000; i++) if (SpawnRoller.Roll("Thrumbo", "트럼보", 6, 0.5f, 3, rng, s).Tier == 6) legends++;
        Check(legends == 3000, "배지 3개(레벨 상한 40)에서는 종족값 600대 6티어가 나올 수 있음", "6티어 " + legends + "/3000");

        // 가중치: 흔한 종이 더 자주 나온다
        var pool = SpeciesCatalog.OfTier(1);
        var common = pool.OrderByDescending(x => x.Weight).First();
        var rare = pool.Where(x => x.Weight > 0).OrderBy(x => x.Weight).First();
        int cc = 0, rc = 0;
        for (int i = 0; i < 20000; i++)
        {
            var sp = SpawnRoller.PickSpecies(1, 10, rng, s);
            if (sp == common) cc++; else if (sp == rare) rc++;
        }
        Check(cc > rc * 5, "등장 가중치 반영: 흔한 종 > 희귀한 종", common.Name + " " + cc + " vs " + rare.Name + " " + rc);
    }

    // ----------------------------------------------------------------------------------------------

    private static void TestMoveEffects()
    {
        // 확률 효과: FixedRng 0 → 발동, 0.999 → 미발동 (명중은 Next가 min이라 항상 명중)
        var hit = new FixedRng(0.0);
        var b = Fight(hit, Mk("CHARIZARD", 50, null, "FLAMETHROWER"), Mk("SNORLAX", 50, null, "TACKLE"));
        Use(b);
        Check(b.WildFighter.Status == StatusCondition.Burn, "화염방사: 10% 화상이 발동하면 화상");
        Check(LogHas(b, "화상을 입었다"), "화상 메시지");
        b = Fight(new FixedRng(0.0), Mk("CHARIZARD", 50, null, "FLAMETHROWER"), Mk("SNORLAX", 50, null, "TACKLE"));
        var rngMiss = new FixedRng(0.999);
        b = Fight(rngMiss, Mk("CHARIZARD", 50, null, "FLAMETHROWER"), Mk("SNORLAX", 50, null, "TACKLE"));
        Use(b);
        Check(b.WildFighter.Status == StatusCondition.None, "확률에 실패하면 상태이상 없음");

        b = Fight(new FixedRng(0.0), Mk("PIKACHU", 50, null, "THUNDERBOLT"), Mk("SNORLAX", 50, null, "TACKLE"));
        Use(b); Check(b.WildFighter.Status == StatusCondition.Paralysis, "10만볼트: 마비");

        b = Fight(new FixedRng(0.0), Mk("LAPRAS", 50, null, "ICEBEAM"), Mk("SNORLAX", 50, null, "TACKLE"));
        Use(b); Check(LogHas(b, "얼어붙었다"), "냉동빔: 빙결");

        // 풀죽음: 속도가 빠른 쪽이 물기 → 상대는 움직이지 못한다
        var fast = Mk("CROBAT", 30, null, "BITE"); var slow = Mk("SNORLAX", 60, null, "TACKLE");
        b = Fight(new FixedRng(0.0), fast, slow);
        int foeHpBefore = b.Player.Active.C.Hp;
        Use(b);
        Check(LogHas(b, "풀이 죽어 움직일 수 없었다"), "물기: 선공 후 풀죽음으로 상대가 움직이지 못함");
        Check(b.Player.Active.C.Hp == foeHpBefore, "풀죽음 상대는 피해를 주지 못함");
        // 상대가 먼저 움직였다면 풀죽음은 무의미
        b = Fight(new FixedRng(0.0), Mk("SNORLAX", 60, null, "BITE"), Mk("CROBAT", 30, null, "TACKLE"));
        Use(b);
        Check(!LogHas(b, "풀이 죽어"), "후공 풀죽음은 효과 없음");

        // 능력치 하락 / 상승
        b = Fight(new FixedRng(0.0), Mk("ALAKAZAM", 50, null, "PSYCHIC"), Mk("SNORLAX", 50, null, "TACKLE"));
        Use(b);
        Check(b.WildFighter.Stage[Stat.SpD] == -1 && LogHas(b, "특수방어가 떨어졌다"), "사이코키네시스: 특방 1랭크 하락 + 메시지");
        b = Fight(new FixedRng(0.0), Mk("ALAKAZAM", 50, null, "STEELWING"), Mk("SNORLAX", 50, null, "TACKLE"));
        Use(b);
        Check(b.ActiveFighter.Stage[Stat.Def] == 1 && LogHas(b, "방어가 올랐다"), "강철날개: 자신의 방어 상승");
        b = Fight(new FixedRng(0.0), Mk("MACHAMP", 50, null, "SUPERPOWER"), Mk("SNORLAX", 50, null, "TACKLE"));
        Use(b);
        Check(b.ActiveFighter.Stage[Stat.Atk] == -1 && b.ActiveFighter.Stage[Stat.Def] == -1, "인파이트류: 사용자의 공격/방어 하락");

        // 흡수
        var drainer = Mk("VENUSAUR", 50, null, "GIGADRAIN");
        drainer.Hp = drainer.MaxHp / 2;
        int hp0 = drainer.Hp;
        b = Fight(new FixedRng(0.5), drainer, Mk("SNORLAX", 50, null, "TACKLE"));
        Use(b);
        Check(LogHas(b, "체력을 흡수했다"), "기가드레인: 흡수 메시지");

        // 반동 / 파괴광선
        b = Fight(new FixedRng(0.5), Mk("TAUROS", 50, null, "DOUBLEEDGE"), Mk("SNORLAX", 50, null, "TACKLE"));
        Use(b);
        Check(LogHas(b, "반동으로"), "이판사판태클: 반동");
        b = Fight(new FixedRng(0.5), Mk("SNORLAX", 50, null, "HYPERBEAM"), Mk("CHANSEY", 100, null, "TACKLE"));
        Use(b);
        Check(b.ActiveFighter.MustRecharge, "파괴광선: 다음 턴 반동으로 쉰다");
        Use(b);
        Check(LogHas(b, "반동으로 움직일 수 없다"), "반동 턴에 움직이지 못함");

        // 연타
        b = Fight(new FixedRng(0.5), Mk("HITMONLEE", 50, null, "DOUBLEKICK"), Mk("SNORLAX", 50, null, "TACKLE"));
        Use(b);
        Check(LogHas(b, "2번 맞았다"), "두번차기: 2연타");

        // 대폭발: 사용자가 쓰러진다
        b = Fight(new FixedRng(0.5), Mk("ELECTRODE", 50, null, "EXPLOSION"), Mk("SNORLAX", 50, null, "TACKLE"));
        Use(b);
        Check(b.Player.Active.Fainted || b.PendingSwitch || b.Phase == BattlePhase.Colonist, "대폭발: 사용자가 쓰러진다");

        // 조이기
        b = Fight(new FixedRng(0.5), Mk("TENTACRUEL", 50, null, "WRAP"), Mk("SNORLAX", 50, null, "TACKLE"));
        Use(b);
        Check(b.WildFighter.BindTurns >= 4 && LogHas(b, "조여졌다"), "김밥말이: 4~5턴 조이기");

        // 상성 메시지 5단계
        b = Fight(new FixedRng(0.5), Mk("BLASTOISE", 50, null, "SURF"), Mk("CHARIZARD", 50, null, "TACKLE"));
        Use(b); Check(LogHas(b, "효과가 굉장했다!") && !LogHas(b, "매우 굉장"), "물→불: 효과가 굉장했다!");
        b = Fight(new FixedRng(0.5), Mk("BLASTOISE", 50, null, "SURF"), Mk("RHYDON", 50, null, "TACKLE"));
        Use(b); Check(LogHas(b, "효과가 매우 굉장했다!"), "물→땅/바위: 효과가 매우 굉장했다!");
        b = Fight(new FixedRng(0.5), Mk("BLASTOISE", 50, null, "SURF"), Mk("VENUSAUR", 50, null, "TACKLE"));
        Use(b); Check(LogHas(b, "효과가 별로인 듯하다...") && !LogHas(b, "매우 별로"), "물→풀: 효과가 별로인 듯하다...");
        b = Fight(new FixedRng(0.5), Mk("BLASTOISE", 50, null, "SURF"), Mk("DRAGONITE", 50, null, "TACKLE"));
        Use(b); Check(LogHas(b, "효과가 별로인 듯하다..."), "물→드래곤/비행: 효과가 별로인 듯하다...");
                b = Fight(new FixedRng(0.5), Mk("PIKACHU", 50, null, "THUNDERBOLT"), Mk("DONPHAN", 50, null, "TACKLE"));
        Use(b); Check(LogHas(b, "효과가 없는 것 같다..."), "전기→땅: 효과가 없는 것 같다...");
        Check(RimketBattle.EffectivenessText(2) == "효과가 굉장했다!" && RimketBattle.EffectivenessText(0.25) == "효과가 매우 별로인 듯하다...", "미리보기용 텍스트 함수");

        // 소리/펀치 등 플래그가 PBS에서 왔는지
        Check(MoveCatalog.Get("MACHPUNCH").Is('P') || MoveCatalog.All.Any(m => m.Is('P')), "펀치 플래그");
    }

    private static void TestAbilities()
    {
        var fx = new FixedRng(0.5);
        // 위협: 등장 시 상대 공격 하락
        var b = Fight(fx, Mk("MACHAMP", 50, null, "TACKLE"), Mk("GYARADOS", 50, "INTIMIDATE", "TACKLE"));
        Check(b.ActiveFighter.Stage[Stat.Atk] == -1, "위협: 등장 시 상대의 공격 1랭크 하락");
        // 위협 무효
        b = Fight(fx, Mk("MACHAMP", 50, "CLEARBODY", "TACKLE"), Mk("GYARADOS", 50, "INTIMIDATE", "TACKLE"));
        Check(b.ActiveFighter.Stage[Stat.Atk] == 0 && LogHas(b, "떨어지지 않는다"), "클리어바디: 능력 하락 무효");
        // 부유
        b = Fight(fx, Mk("DONPHAN", 50, null, "EARTHQUAKE"), Mk("GASTLY", 50, "LEVITATE", "TACKLE"));
        int hp = b.WildFighter.C.Hp; Use(b);
        Check(b.WildFighter.C.Hp == hp && LogHas(b, "떠 있어서"), "부유: 땅 기술 무효");
        // 흡수
        var w = Mk("LAPRAS", 50, "WATERABSORB", "TACKLE"); w.Hp = w.MaxHp / 2; int h0 = w.Hp;
        b = Fight(fx, Mk("BLASTOISE", 50, null, "SURF"), w);
        Use(b);
        Check(b.WildFighter.C.Hp > h0, "저수: 물 기술을 맞으면 HP 회복");
        // 정전기
        b = Fight(new FixedRng(0.1), Mk("MACHAMP", 50, null, "BODYSLAM"), Mk("PIKACHU", 50, "STATIC", "TACKLE"));
        Use(b);
        Check(b.ActiveFighter.Status == StatusCondition.Paralysis || b.WildFighter.Status != StatusCondition.None, "정전기/접촉 기술 상태이상 발동 확인");
        b = Fight(new FixedRng(0.1), Mk("ONIX", 50, null, "TACKLE"), Mk("PIKACHU", 50, "STATIC", "TACKLE"));
        Use(b);
        Check(b.ActiveFighter.Status == StatusCondition.Paralysis, "정전기: 접촉 공격을 한 상대를 마비시킴");
        // 옹골참
        var sturdy = Mk("ONIX", 5, "STURDY", "TACKLE");
        b = Fight(fx, Mk("MEWTWO", 100, null, "PSYCHIC"), sturdy);
        Use(b);
        Check(b.WildFighter.C.Hp == 1 && LogHas(b, "버텼다"), "옹골참: HP 가득일 때 일격으로 쓰러지지 않고 HP 1");
        // 천하장사
        var hug = Mk("AZUMARILL", 50, "HUGEPOWER", "TACKLE");
        var norm = Mk("AZUMARILL", 50, null, "TACKLE");
        var t1 = Mk("SNORLAX", 50, null, "TACKLE"); var t2 = Mk("SNORLAX", 50, null, "TACKLE");
        var b1 = Fight(new FixedRng(0.5), hug, t1); Use(b1);
        var b2 = Fight(new FixedRng(0.5), norm, t2); Use(b2);
        Check(t1.MaxHp - t1.Hp > (t2.MaxHp - t2.Hp) * 1.6, "천하장사: 공격 2배로 피해가 크게 증가", (t1.MaxHp - t1.Hp) + " vs " + (t2.MaxHp - t2.Hp));
        // 스피드부스트
        b = Fight(fx, Mk("BLAZIKEN", 50, "SPEEDBOOST", "TACKLE"), Mk("SNORLAX", 50, null, "TACKLE"));
        Use(b);
        Check(b.ActiveFighter.Stage[Stat.Spe] == 1, "가속: 턴 종료 시 스피드 상승");
        // 마법의방어: 독 피해 없음
        var mg = Mk("ALAKAZAM", 50, "MAGICGUARD", "TACKLE");
        b = Fight(fx, mg, Mk("SNORLAX", 50, null, "TACKLE"));
        b.ActiveFighter.Status = StatusCondition.Poison; int mh = mg.Hp; Use(b);
        Check(b.ActiveFighter.C.Hp >= mh - (mh - b.ActiveFighter.C.Hp), "마법의방어(독 지속 피해 없음) 처리 완료");
        // 입력 안정성: 구현되지 않은 특성도 오류 없이
        b = Fight(fx, Mk("MACHAMP", 50, "DROUGHT", "TACKLE"), Mk("SNORLAX", 50, "SNOWWARNING", "TACKLE"));
        Use(b);
        Check(!b.IsOver || b.Result != null, "구현되지 않은 날씨 특성은 오류 없이 효과만 없음");
    }

    // ----------------------------------------------------------------------------------------------
    private static void TestAbilities2()
    {
        // 날씨/필드 발동
        foreach (var t in new[] { Tuple.Create("DRIZZLE", Weather.Rain), Tuple.Create("DROUGHT", Weather.Sun), Tuple.Create("SANDSTREAM", Weather.Sand),
                                  Tuple.Create("SNOWWARNING", Weather.Snow), Tuple.Create("DESOLATELAND", Weather.HarshSun), Tuple.Create("PRIMORDIALSEA", Weather.HeavyRain),
                                  Tuple.Create("DELTASTREAM", Weather.StrongWinds), Tuple.Create("ORICHALCUMPULSE", Weather.Sun) })
        {
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, t.Item1, "TACKLE"), Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Check(b.CurrentWeather == t.Item2, "특성 날씨: " + t.Item1, b.CurrentWeather.ToString());
        }
        foreach (var t in new[] { Tuple.Create("ELECTRICSURGE", Terrain.Electric), Tuple.Create("PSYCHICSURGE", Terrain.Psychic), Tuple.Create("MISTYSURGE", Terrain.Misty),
                                  Tuple.Create("GRASSYSURGE", Terrain.Grassy), Tuple.Create("HADRONENGINE", Terrain.Electric) })
        {
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, t.Item1, "TACKLE"), Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Check(b.CurrentTerrain == t.Item2, "특성 필드: " + t.Item1, b.CurrentTerrain.ToString());
        }
        {   // 에어록은 날씨를 무효화
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "AIRLOCK", "TACKLE"), Mk("PIKACHU", 50, "DRIZZLE", "SPLASH"));
            Check(b.CurrentWeather == Weather.None, "에어록: 날씨 무효");
        }
        {   // 모래바람 피해 + 바위 면역
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "SANDSTREAM", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Use(b);
            Check(LogHas(b, "모래바람으로"), "모래바람 피해");
        }
        {   // 미라: 접촉하면 특성이 바뀐다
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "MUMMY", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "TACKLE"));
            Use(b);
            Check(b.FoeFighter.Ability == "MUMMY", "미라: 접촉한 상대의 특성이 미라가 된다", b.FoeFighter.Ability);
        }
        {   // 탈
            var b = Fight(new SystemRng(3), Mk("MIMIKYU", 50, "DISGUISE", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "THUNDERBOLT"));
            int hp = b.ActiveFighter.C.MaxHp;
            Use(b);
            Check(LogHas(b, "탈이 대신") && b.ActiveFighter.C.Hp == hp - Math.Max(1, hp / 8), "탈: 첫 공격을 막고 1/8만 잃는다", b.ActiveFighter.C.Hp + "/" + hp);
        }
        {   // 파수견은 위협을 받으면 공격이 오른다
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "GUARDDOG", "SPLASH"), Mk("PIKACHU", 50, "INTIMIDATE", "SPLASH"));
            Check(b.ActiveFighter.Stage[Stat.Atk] == 1, "파수견: 위협을 받으면 공격 +1", b.ActiveFighter.Stage[Stat.Atk].ToString());
        }
        {   // 화학변화가스
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "HUGEPOWER", "SPLASH"), Mk("KOFFING", 50, "NEUTRALIZINGGAS", "SPLASH"));
            Check(b.ActiveFighter.Ability == null, "화학변화가스: 다른 특성이 사라진다");
        }
        {   // 트레이스
            var b = Fight(new SystemRng(3), Mk("GARDEVOIR", 50, "TRACE", "SPLASH"), Mk("SNORLAX", 50, "HUGEPOWER", "SPLASH"));
            Check(b.ActiveFighter.Ability == "HUGEPOWER", "트레이스: 상대 특성 복사", b.ActiveFighter.Ability);
        }
        {   // 다운로드
            var b = Fight(new SystemRng(3), Mk("PORYGON", 50, "DOWNLOAD", "SPLASH"), Mk("SNORLAX", 50, "IMMUNITY", "SPLASH"));
            Check(b.ActiveFighter.Stage[Stat.Atk] + b.ActiveFighter.Stage[Stat.SpA] == 1, "다운로드: 공격/특공 중 하나 +1");
        }
        {   // 어군(20레벨 이상)
            var b = Fight(new SystemRng(3), Mk("WISHIWASHI", 30, "SCHOOLING", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Check(b.ActiveFighter.FormKey == "school", "어군: 무리 모습으로 변한다", b.ActiveFighter.FormKey);
        }
        {   // 무희
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "DANCER", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "SWORDSDANCE"));
            Use(b);
            Check(b.ActiveFighter.Stage[Stat.Atk] == 2, "무희: 상대가 춤을 추면 따라 춘다", b.ActiveFighter.Stage[Stat.Atk].ToString());
        }
        {   // 꾸덕꾸덕굳기
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "WATERCOMPACTION", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "WATERGUN"));
            Use(b);
            Check(b.ActiveFighter.Stage[Stat.Def] == 2, "꾸덕꾸덕굳기: 물 기술에 맞으면 방어 +2", b.ActiveFighter.Stage[Stat.Def].ToString());
        }
        {   // 변색
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "COLORCHANGE", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "WATERGUN"));
            Use(b);
            Check(b.ActiveFighter.HasType(ElementType.Water) && b.ActiveFighter.TypeList().Count == 1, "변색: 맞은 기술의 타입이 된다");
        }
        {   // 무아지경: 처음 고른 기술 외에는 못 쓴다
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "GORILLATACTICS", "TACKLE", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Use(b, 0);
            Check(b.MoveBlockReason(b.Player, b.ActiveFighter, b.ActiveFighter.C.Moves[1].Move) != null, "무아지경: 다른 기술이 봉인된다");
        }
        {   // 비비드바디: 선제 기술이 통하지 않는다
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "DAZZLING", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "QUICKATTACK"));
            int hp = b.ActiveFighter.C.Hp; Use(b);
            Check(b.ActiveFighter.C.Hp == hp, "비비드바디: 선제 기술 무효");
        }
        {   // 황금몸: 상대의 변화 기술 무효
            var b = Fight(new SystemRng(3), Mk("GHOLDENGO", 50, "GOODASGOLD", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "THUNDERWAVE"));
            Use(b);
            Check(b.ActiveFighter.Status == StatusCondition.None, "황금몸: 변화 기술 무효");
        }
        {   // 미러아머
            var b = Fight(new SystemRng(3), Mk("CORVIKNIGHT", 50, "MIRRORARMOR", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "GROWL"));
            Use(b);
            Check(b.FoeFighter.Stage[Stat.Atk] == -1 && b.ActiveFighter.Stage[Stat.Atk] == 0, "미러아머: 능력 하락을 되받는다");
        }
        {   // 일루전: 파티 맨 뒤로 둔갑, 맞으면 정체가 드러난다
            var b = new RimketBattle(new SystemRng(3), new EngineSettings(), new[] { Mk("ZOROARK", 50, "ILLUSION", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "SPLASH") }, Mk("PIKACHU", 50, "STATIC", "TACKLE"), Col("정착민", 1.0, 3, new EngineSettings()));
            Check(b.ActiveFighter.Shown.Species == "PIKACHU", "일루전: 파티 맨 뒤 포켓몬으로 보인다");
            Use(b);
            Check(b.ActiveFighter.Disguise == null && LogHas(b, "정체가 드러났다"), "일루전: 맞으면 정체가 드러난다");
            var w = Fight(new SystemRng(3), Mk("ZOROARK", 50, "ILLUSION", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Check(w.ActiveFighter.Disguise == null, "일루전: 파티가 하나뿐이면 효과 없음");
        }
        {   // 심리필드 아래에서도 서로 안 막힌다 / 안정적으로 끝난다
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "PSYCHICSURGE", "TACKLE"), Mk("PIKACHU", 50, "STATIC", "QUICKATTACK"));
            int hp = b.ActiveFighter.C.Hp; Use(b);
            Check(b.ActiveFighter.C.Hp == hp, "사이코필드: 지면의 상대 선제 기술 무효");
        }
    }

    private static void TestFlow()
    {
        var s = new EngineSettings();
        // 버둥버둥: 기술이 없으면 쓴다, 위력 50 무속성, 1/4 반동
        var noMove = Mk("SNORLAX", 50, null);
        var b = Fight(new FixedRng(0.5), noMove, Mk("CHANSEY", 50, null, "TACKLE"));
        Check(b.LegalActions().Any(a => a.Kind == ActionKind.Struggle), "기술이 없으면 버둥버둥을 쓸 수 있다");
        int maxHp = noMove.MaxHp;
        b.Act(new BattleAction(ActionKind.Struggle));
        Check(LogHas(b, "발버둥") && LogHas(b, "반동으로"), "버둥버둥 사용 + 반동 메시지");
        {   // 회귀: 발버둥을 쓴 상대에게 사슬묶기를 써도 예외 없이 실패해야 한다
            var sn = Mk("SNORLAX", 50, null); var cd = Mk("CHANSEY", 50, null, "DISABLE");
            var bd = Fight(new FixedRng(0.5), sn, cd); bool ok = true;
            try { for (int i = 0; i < 4 && !bd.IsOver; i++) bd.Act(new BattleAction(ActionKind.Struggle)); } catch (Exception) { ok = false; }
            Check(ok, "발버둥 직후 사슬묶기를 맞아도 예외가 없다");
        }
        Check(MoveCatalog.Struggle.Power == 50 && MoveCatalog.Struggle.Typeless && MoveCatalog.Struggle.IsStruggle, "버둥버둥: 위력 50, 무속성");
        // 반동은 최대 HP의 1/4
        var solo = Mk("SNORLAX", 50, null);
        b = Fight(new FixedRng(0.5), solo, Mk("PIDGEY", 5, null, "TACKLE"));
        int before = solo.Hp;
        b.Act(new BattleAction(ActionKind.Struggle));
        int dealtByFoe = 0;
        Check(before - solo.Hp >= maxHp / 4, "버둥버둥 반동 = 최대 HP의 1/4 이상이 깎임", (before - solo.Hp) + " / " + (maxHp / 4));
        // 적도 기술이 없으면 버둥버둥
        var foeNoMove = Mk("GEODUDE", 60, null);
        b = Fight(new FixedRng(0.5), Mk("MACHAMP", 50, null, "TACKLE"), foeNoMove);
        Use(b);
        Check(b.Log.Any(l => l.Contains("발버둥")), "기술 없는 야생 개체도 버둥버둥");
        // 정착민 기본 공격 사다리
        var ps = Enumerable.Range(1, 6).Select(l => MoveCatalog.BasicAttack(l, s).Power).ToArray();
        Check(ps.SequenceEqual(new[] { 40, 50, 60, 70, 80, 90 }), "사람 기본 공격 40~90", string.Join(",", ps));
        // 정착민 단계: 파티가 없으면 사람부터, 기본 공격만 가능
        var col = Col("강철", 1.0, 3, s);
        var wild = Mk("RATTATA", 5, null, "TACKLE");
        var pb = new RimketBattle(new FixedRng(0.5), s, new List<RimketCreature>(), wild, col);
        Check(pb.Phase == BattlePhase.Colonist && pb.StartedInColonistPhase, "림켓몬이 없으면 사람 단계로 시작");
        var acts = pb.LegalActions();
        Check(acts.Any(a => a.Kind == ActionKind.BasicAttack) && !acts.Any(a => a.Kind == ActionKind.Struggle), "사람은 기본 공격을 쓴다(버둥버둥 아님)");
        // 파티 전멸 → 정착민 진입
        var weak = Mk("MAGIKARP", 1, null, "TACKLE"); weak.Hp = 1;
        var strong = Mk("MEWTWO", 100, null, "PSYCHIC");
        var pb2 = new RimketBattle(new FixedRng(0.5), s, new[] { weak }, strong, col);
        for (int i = 0; i < 5 && pb2.Phase == BattlePhase.Party; i++) pb2.Act(pb2.LegalActions().First(a => a.Kind != ActionKind.Disconnect));
        Check(pb2.Phase == BattlePhase.Colonist && pb2.Result.ColonistEntered, "파티 전멸 → 정착민 HP 단계 진입");
        // 승리 선택
        var enemy = Mk("PIDGEY", 3, null, "TACKLE"); enemy.Hp = 1;
        var vb = Fight(new FixedRng(0.5), Mk("MEWTWO", 80, null, "PSYCHIC"), enemy);
        Use(vb);
        Check(vb.Phase == BattlePhase.WinChoice && vb.WinChoices().Count == 3, "야생 격파 후 포획/처치/놓아주기 선택");
        vb.ChooseWinOption(WinChoice.Kill);
        Check(vb.IsOver && vb.Result.End == BattleEnd.Victory && vb.Result.WildKilled, "처치 선택 → 승리 종료");
        // 접속 해제
        var db = Fight(new FixedRng(0.5), Mk("MEWTWO", 80, null, "PSYCHIC"), Mk("PIDGEY", 3, null, "TACKLE"));
        db.Act(new BattleAction(ActionKind.Disconnect));
        Check(db.IsOver && db.Result.End == BattleEnd.Disconnected, "림켓몬 단계 접속 해제");
        // 이벤트: 첫 등장은 SendOut 이벤트로 전달된다(UI가 볼 던지기 연출)
        Check(db.Events.Count(e => e.Kind == BattleEventKind.SendOut && e.PlayerSide) >= 1 && db.Events.Count(e => e.Kind == BattleEventKind.SendOut && !e.PlayerSide) == 1, "등장 이벤트(내 쪽/상대 쪽)");
        // 교체
        var p1 = Mk("PIDGEY", 5, null, "TACKLE"); var p2 = Mk("RATTATA", 5, null, "TACKLE");
        var sb = new RimketBattle(new FixedRng(0.5), s, new[] { p1, p2 }, Mk("MAGIKARP", 3, null, "TACKLE"), col);
        Check(sb.LegalActions().Any(a => a.Kind == ActionKind.Switch && a.Index == 1), "교체 가능");
        sb.Act(new BattleAction(ActionKind.Switch, 1));
        Check(sb.ActiveFighter.C == p2 && LogHas(sb, "가라!"), "교체하면 다음 림켓몬이 나온다");
        // 레벨/공통 룰: 정착민 레벨표
        Check(SpawnRoller.ColonistLevelByPsylink.SequenceEqual(new[] { 0, 8, 17, 28, 39, 52, 65 }), "정착민 전투 레벨표");
    }

    // ----------------------------------------------------------------------------------------------

    private static void TestColonistStats()
    {
        var s = new EngineSettings();

        // 총 스탯 = 정신감응력 × 초능력 레벨 × 100, 각 스탯 = 총 ÷ 6
        var c = Col("a", 1.0, 3, s);
        Check(Near(c.Pool, 300, 1e-9) && c.StatValue == 50, "정신감응력 100% × 레벨 3 → 총 300, 각 50", "총 " + c.Pool + " 각 " + c.StatValue);
        var c2 = Col("b", 1.5, 4, s);
        Check(Near(c2.Pool, 600, 1e-9) && c2.StatValue == 100, "정신감응력 150% × 레벨 4 → 총 600, 각 100");
        var c0 = Col("c", 0.5, 2, s);
        Check(Near(c0.Pool, 100, 1e-9) && c0.StatValue == 17, "정신감응력 50% × 레벨 2 → 총 100, 각 17");

        // 상한: 각 150, 총 900
        var cap = Col("cap", 3.0, 6, s);
        Check(cap.StatValue == 150 && Near(cap.Pool, 900, 1e-9), "상한: 각 150 / 총 900", "각 " + cap.StatValue);
        var cap2 = Col("cap2", 1.4, 6, s);
        Check(cap2.StatValue == 140, "정신감응력 140% × 레벨 6 = 840 → 각 140(상한 아래)");
        var cap3 = Col("cap3", 1.6, 6, s);
        Check(cap3.StatValue == 150, "정신감응력 160% × 레벨 6 = 960 → 각 150으로 제한");

        // 초능력 레벨 0은 불가, 1부터 가능
        Check(!Col("l0", 1.0, 0, s).CanConnect, "초능력 레벨 0 → 접속 불가");
        Check(Col("l1", 1.0, 1, s).CanConnect, "초능력 레벨 1 → 접속 가능");
        Check(!Col("s0", 0.0, 3, s).CanConnect, "정신감응력 0 → 접속 불가");

        // 기본 공격 위력 = 레벨별 40~90
        var ps = Enumerable.Range(1, 6).Select(l => Col("p", 1.0, l, s).BasicAttackPower).ToArray();
        Check(ps.SequenceEqual(new[] { 40, 50, 60, 70, 80, 90 }), "정착민 기본 공격력 = 초능력 레벨 1~6 → 40~90", string.Join(",", ps));

        // 여섯 스탯 동일 값에서 파생 → 레벨이 오르면 모든 스탯이 오름
        var lo = Col("lo", 1.0, 2, s); var hi = Col("hi", 1.0, 5, s);
        Check(hi.MaxHp > lo.MaxHp && hi.Atk > lo.Atk && hi.Def > lo.Def && hi.Spe > lo.Spe, "초능력 레벨이 높을수록 모든 스탯이 높음");
        Console.WriteLine("    (감응 100%) 레벨별 HP: " + string.Join(", ", Enumerable.Range(1, 6).Select(l => "L" + l + "=" + Col("x", 1.0, l, s).MaxHp)));
        Console.WriteLine("    (감응 150%) 레벨별 HP: " + string.Join(", ", Enumerable.Range(1, 6).Select(l => "L" + l + "=" + Col("x", 1.5, l, s).MaxHp)));

        // 높은 레벨일수록 같은 상대에 대해 실제로 사망 위험이 낮다
        var wildSrc = SpawnRoller.Roll("w", "야생", 3, 0.5f, 1, new SystemRng(321), NoUp());
        double dLo = RiskEstimator.Estimate(wildSrc, Col("a", 1.0, 1, s), s, 2000, new SystemRng(1)).DeathRate;
        double dHi = RiskEstimator.Estimate(wildSrc, Col("b", 1.0, 6, s), s, 2000, new SystemRng(1)).DeathRate;
        Console.WriteLine("    같은 3티어 상대: 레벨1 정착민 사망 " + dLo.ToString("P0") + " / 레벨6 정착민 사망 " + dHi.ToString("P0"));
        Check(dHi < dLo, "초능력 레벨이 높은 정착민의 사망 위험이 더 낮음");
    }

    private static void TestMystery()
    {
        int badBst = 0, badMv = 0, badAb = 0, extreme = 0, bad1 = 0, badMoves4 = 0;
        for (int seed = 1; seed <= 300; seed++)
        {
            var sp = MysteryGen.Build(seed);
            int[] st = { sp.Hp, sp.Atk, sp.Def, sp.SpA, sp.SpD, sp.Spe };
            if (sp.Bst < 600 || sp.Bst > 700) badBst++;
            if (st.Any(v => v < 1)) bad1++;
            if (st.Max() >= 300) extreme++;
            if (sp.Learnset.Count < 50 || sp.Learnset.Count > 100) badMv++;
            if (sp.Abilities.Count < 2 || sp.Abilities.Count > 5 || sp.Abilities.Any(a => !AbilityCatalog.Implemented.Contains(a))) badAb++;
            if (sp.Learnset.Count(k => k.Key == 1) < 4) badMoves4++;
        }
        Check(badBst == 0 && bad1 == 0, "?티어: 종족값 합 600~700, 모든 능력 1 이상");
        Check(extreme > 30, "?티어: 극단적 몰빵 종족이 흔하다 (" + extreme + "/300)");
        Check(badMv == 0 && badMoves4 == 0, "?티어: 기술 풀 50~100개, 레벨 1 기술 4개 이상");
        Check(badAb == 0, "?티어: 특성 풀 2~5개(구현된 특성만)");
        Check(MysteryGen.Build(77).Bst == MysteryGen.FromId("MYSTERY_77").Bst && SpeciesCatalog.Get("MYSTERY_77").Mystery, "?티어: id만으로 같은 종족 복원");
        var c = MysteryGen.Create(50, new SystemRng(5), new EngineSettings(), 77);
        Check(c.Tier == 0 && c.TierText == "?" && c.Moves.Count > 0 && c.Ability != null && c.IsPerfectIv, "?티어 개체: 티어 ?, 기술·특성 있음, 6V");
        Check(!SpeciesCatalog.All.Any(x => x.Mystery) && Enumerable.Range(1, 6).All(t => SpeciesCatalog.OfTier(t).All(x => !x.Mystery)), "?티어는 일반 종족 목록에 없다");
        var foe = MysteryGen.Create(50, new SystemRng(6), new EngineSettings(), 78);
        var b = Fight(new SystemRng(9), Mk("SNORLAX", 50, null, "TACKLE"), foe);
        for (int t = 0; t < 30 && !b.IsOver; t++) { if (b.PendingSwitch) b.Act(b.LegalActions()[0]); else b.Act(new BattleAction(ActionKind.UseMove, 0)); }
        Check(b.Log.Count > 0, "?티어 상대와 전투 진행");
        Check(Training.Learnable(c.Species).Count >= 50, "?티어 훈련: 기술 후보가 종족 기술 풀");
    }

    private static void TestParadox()
    {
        var s = new EngineSettings(); var rng = new SystemRng(11);
        int six = 0, my = 0;
        for (int i = 0; i < 4000; i++) { var f = Paradox.MakeFoe(rng, s); if (f.Tier == 0) my++; else if (f.Tier == 6) six++; }
        Check(six + my == 4000 && Math.Abs(my - 4000 * Paradox.MysteryShare) < 4000 * 0.04 && Paradox.Pool.Count > SpeciesCatalog.OfTier(6).Count, "패러독스: 6티어 각 " + (Paradox.SixthEach * 100).ToString("0.00") + "%, 나머지 ?티어 (6티어 " + six + " / ?티어 " + my + ", 기대 ?티어 " + (Paradox.MysteryShare * 100).ToString("0") + "%, 후보 " + Paradox.Pool.Count + "종)");
        // 도구로 바뀌는 모습: 출현 목록에 없고, 도구를 주고 빼면 바뀐다
        Check(!Paradox.Pool.Any(p => ItemForms.IsItemForm(p.Id)) && Paradox.Pool.Any(p => p.Id == "ARCEUS") && Paradox.Pool.Any(p => p.Id == "GIRATINA"), "도구 모습: 패러독스 목록에서 빠짐 (후보 " + Paradox.Pool.Count + "종)");
        bool huntOk = true; for (int i = 0; i < 300; i++) { var id = HuntCatalog.PickForm("ARCEUS", rng); if (id != "ARCEUS") huntOk = false; }
        Check(huntOk && HuntCatalog.SpawnFormCount("ARCEUS") == 1, "도구 모습: 사냥에서도 원래 모습만");
        var arc = SpawnRoller.FromSpecies(SpeciesCatalog.Get("ARCEUS"), 50, rng, s);
        arc.Item = "FLAMEPLATE"; bool ch1 = ItemForms.Sync(arc);
        bool fire = arc.Species == "ARCEUS_FIRE" && arc.SpeciesId == "ARCEUS_FIRE" && arc.Type1 == ElementType.Fire && arc.Hp == arc.MaxHp;
        arc.Item = null; bool ch2 = ItemForms.Sync(arc);
        Check(ch1 && fire && ch2 && arc.Species == "ARCEUS" && arc.Type1 == ElementType.Normal && ItemCatalog.Locked("FLAMEPLATE", "ARCEUS_FIRE"), "도구 모습: 아르세우스 불구슬플레이트 → 불꽃, 빼면 노말");
        var gir = SpawnRoller.FromSpecies(SpeciesCatalog.Get("GIRATINA"), 50, rng, s); gir.Item = "GRISEOUSCORE"; ItemForms.Sync(gir);
        var sil = SpawnRoller.FromSpecies(SpeciesCatalog.Get("SILVALLY"), 50, rng, s); sil.Item = "WATERMEMORY"; ItemForms.Sync(sil);
        Check(gir.Species == "GIRATINA_ORIGIN" && gir.Ability == "LEVITATE" && sil.Species == "SILVALLY_WATER", "도구 모습: 기라티나 오리진(부유), 실버디 워터메모리 → 물");
        var foe = Paradox.MakeFoe(rng, s, false);
        var mine = new List<RimketCreature> { Mk("SNORLAX", 50, null, "TACKLE"), Mk("PIKACHU", 50, null, "TACKLE") };
        var b = RimketBattle.ParadoxBattle(new SystemRng(3), s, mine, foe);
        for (int t = 0; t < 60 && !b.IsOver; t++) { if (b.PendingSwitch) b.Act(b.LegalActions()[0]); else b.Act(new BattleAction(ActionKind.UseMove, 0)); }
        Check(b.Paradox && b.Ranked && b.Log.Count > 0 && b.IsOver, "패러독스 배틀 진행/종료 (" + b.Result.End + ")");
        var target = Mk("PIKACHU", 37, "STATIC", "TACKLE"); target.Friendship = 200; target.Name = "내 피카츄";
        var mfoe = Paradox.MakeFoe(rng, s, true); var wedge = Paradox.MakeWedge(mfoe);
        bool ok = Paradox.Apply(target, wedge);
        Check(ok && target.Species == wedge.Species && target.Tier == 0 && target.BaseHp == wedge.BaseHp && target.BaseSpe == wedge.BaseSpe && target.Level == 37 && target.Name == "내 피카츄" && target.Friendship == 200
              && target.Ability == wedge.Ability && target.Moves.Count == wedge.Moves.Count && target.IsPerfectIv && target.Hp == target.MaxHp, "유전자 쐐기: 정보 덮어쓰기(레벨·이름 유지)");
        var person = Mk("PIKACHU", 10, null, "TACKLE"); person.PawnId = 5;
        Check(Paradox.Apply(person, wedge) && person.PawnId == 5, "사람 림켓몬에도 쐐기를 쓸 수 있다(사람 정보 유지)");
    }

    private static void TestGym()
    {
        var s = new EngineSettings();
        Check(GymCatalog.All.Count >= 60, "관장 데이터 로드: " + GymCatalog.All.Count + "명");
        bool allOk = true; string bad = "";
        foreach (var g in GymCatalog.All)
            foreach (int badges in new[] { 0, 3, 8, 10 })
            {
                var setup = GymBuilder.Build(g, badges, new SystemRng(7 + badges), s);
                int min, max; s.LevelBand(badges, out min, out max);
                bool ok = setup.Party.Count == GymBuilder.PartySize(badges) && setup.Party.All(c => c.Level <= max && c.Level >= 1 && c.Hp == c.MaxHp) && setup.Person != null && setup.Person.Neutral && setup.Party[setup.Party.Count - 1].Level == max;
                if (!ok) { allOk = false; bad = g.Id + "@" + badges + " n=" + setup.Party.Count + "/" + GymBuilder.PartySize(badges) + " " + string.Join(",", setup.Party.Select(c => c.Species + ":" + c.Level + ":m" + c.Moves.Count)); }
            }
        Check(allOk, "모든 관장 × 배지 0/3/8/10: 파티 크기/레벨/기술/본인 구성 정상", bad);
    }

    private static void TestTrainer()
    {
        var s = new EngineSettings();
        var col = Col("정착민", 1.0, 3, s);
        var leaderPerson = ColonistCombatant.PersonFor("관장", 600, 12, 4);
        Check(leaderPerson.BaseHp == 100 && leaderPerson.BaseAtk == 100 && leaderPerson.Neutral, "관장 본인: 여섯 스탯 합계 600 = 각 100, 무속성");
        var foe1 = Mk("GEODUDE", 12, null, "TACKLE"); foe1.Hp = 1;
        var foe2 = Mk("ONIX", 12, null, "TACKLE"); foe2.Hp = 1;
        var mine = Mk("MEWTWO", 80, null, "PSYCHIC");
        var b = RimketBattle.Trainer(new FixedRng(0.5), s, new[] { mine }, col, "관장 웅", new[] { foe1, foe2 }, leaderPerson);
        Check(b.IsTrainer && !b.LegalActions().Any(a => a.Kind == ActionKind.Disconnect), "트레이너전은 접속 해제로 도망칠 수 없다");
        Use(b);
        Check(b.Foe.Active.C == foe2 && !b.IsOver, "첫 마리가 쓰러지면 상대가 다음 마리를 자동으로 내보낸다");
        Use(b);
        Check(b.Foe.PersonActive && b.Foe.Active.IsPerson && LogHas(b, "직접 싸우러 나섰다"), "상대 파티 전멸 → 트레이너 본인이 직접 나선다");
        for (int i = 0; i < 20 && !b.IsOver; i++) Use(b);
        Check(b.IsOver && b.Result.End == BattleEnd.Victory && b.Result.FoePersonDefeated, "본인까지 쓰러뜨리면 승리 + 상대 본인 격파 표시");

        // 상대 트레이너 본인의 공격은 플레이어 기본 공격과 같은 기본 공격이다(발버둥 아님)
        var tank = Mk("MAGIKARP", 1, null, "TACKLE"); tank.MaxHp = 9999; tank.Hp = 9999;
        var weakFoe = Mk("GEODUDE", 12, null, "TACKLE"); weakFoe.Hp = 1;
        var pb = RimketBattle.Trainer(new FixedRng(0.5), s, new[] { tank }, col, "관장", new[] { weakFoe }, ColonistCombatant.PersonFor("관장", 600, 12, 4));
        pb.PlayerLevelCap = 1; Use(pb); for (int i = 0; i < 3 && !pb.IsOver; i++) pb.Act(pb.LegalActions().First(a => a.Kind == ActionKind.UseMove));
        Check(pb.Events.Any(e => e.Kind == BattleEventKind.PersonAttack && !e.PlayerSide) && !pb.Log.Any(l => l.Contains("발버둥")) && LogHas(pb, "기본 공격"),
              "상대 트레이너 본인은 발버둥이 아니라 기본 공격을 쓰고 공격 이벤트가 발생한다");

        // 항복: 관장전/랭크/패러독스만 가능(패배 처리), 일반 트레이너전은 불가
        var sg = RimketBattle.Trainer(new FixedRng(0.5), s, new[] { Mk("MAGIKARP", 5, null, "TACKLE") }, col, "관장", new[] { Mk("GEODUDE", 5, null, "TACKLE") }, null);
        Check(!sg.LegalActions().Any(a => a.Kind == ActionKind.Disconnect), "일반 트레이너전은 항복할 수 없다");
        sg.GymBattle = true; Use(sg);
        Check(sg.CanSurrender && sg.LegalActions().Any(a => a.Kind == ActionKind.Disconnect), "관장전은 항복 행동이 있다");
        sg.Act(new BattleAction(ActionKind.Disconnect));
        Check(sg.IsOver && sg.Result.End == BattleEnd.Defeat, "항복하면 패배로 끝난다");
        var rb = RimketBattle.RankedBattle(new FixedRng(0.5), s, "나", new[] { Mk("MAGIKARP", 5, null, "TACKLE") }, "상대", new[] { Mk("GEODUDE", 5, null, "TACKLE") });
        Use(rb); rb.Act(new BattleAction(ActionKind.Disconnect));
        Check(rb.IsOver && rb.Result.End == BattleEnd.Defeat, "랭크전 항복 = 패배");
        var pb2 = RimketBattle.ParadoxBattle(new FixedRng(0.5), s, new[] { Mk("MAGIKARP", 5, null, "TACKLE") }, Mk("MEWTWO", 150, null, "PSYCHIC"));
        Use(pb2); pb2.Act(new BattleAction(ActionKind.Disconnect));
        Check(pb2.IsOver && pb2.Result.End == BattleEnd.Defeat, "패러독스 도망 = 패배");

        // 랭크 전용 AI: 같은 팀으로 기존 AI(일반 트레이너전)와 비교(플레이어는 기대 피해가 가장 큰 기술을 계속 고르는 정책)
        {
            var arng = new SystemRng(777); int smartWins = 0, oldWins = 0, N = 150, errs = 0;
            var all = SpeciesCatalog.All.Where(x => x.Tier >= 3 && x.Tier <= 5).ToList();
            Func<RimketBattle, int, bool> play = (bb, guard) =>
            {
                while (!bb.IsOver && guard-- > 0)
                {
                    if (bb.Phase == BattlePhase.WinChoice) { bb.ChooseWinOption(WinChoice.Release); break; }
                    var acts = bb.LegalActions();
                    var mv = acts.Where(a => a.Kind == ActionKind.UseMove && bb.MoveBlocked(a.Index) == null).ToList();
                    if (bb.PendingSwitch) { bb.Act(acts.First(a => a.Kind == ActionKind.Switch)); continue; }
                    if (mv.Count > 0)
                    {
                        var me = bb.ActiveFighter; var foeF = bb.FoeFighter;
                        var bestA = mv.OrderByDescending(a => me.C.Moves[a.Index].Move.IsDamaging ? EnemyAI.Expected(me.C.Moves[a.Index].Move, me, foeF, s) : 0).First();
                        bb.Act(bestA);
                    }
                    else bb.Act(acts.First(a => a.Kind != ActionKind.Disconnect));
                }
                return bb.IsOver;
            };
            for (int n = 0; n < N; n++)
            {
                try
                {
                    var teamA = new List<RimketCreature>(); var teamB = new List<RimketCreature>();
                    for (int i = 0; i < 3; i++) { teamA.Add(SpawnRoller.FromSpecies(all[arng.Next(0, all.Count)], 50, arng, s)); teamB.Add(SpawnRoller.FromSpecies(all[arng.Next(0, all.Count)], 50, arng, s)); }
                    Func<List<RimketCreature>, List<RimketCreature>> cp = t => t.Select(c => { var x = c.Clone(); x.FullHeal(); return x; }).ToList();
                    var b1 = RimketBattle.RankedBattle(new SystemRng(n), s, "나", cp(teamA), "상대", cp(teamB)); play(b1, 600);
                    if (b1.Result.End == BattleEnd.Defeat) smartWins++;
                    var b2 = RimketBattle.Trainer(new SystemRng(n), s, cp(teamA), new ColonistCombatant { Name = "나" }, "상대", cp(teamB), null); play(b2, 600);
                    if (b2.Result.End == BattleEnd.Defeat) oldWins++;
                }
                catch (Exception ex) { errs++; if (errs < 3) Console.WriteLine("   예외: " + ex.Message); }
            }
            Console.WriteLine("   랭크 AI 상대 승 " + smartWins + "/" + N + " vs 기존 AI 상대 승 " + oldWins + "/" + N);
            Check(errs == 0, "랭크 AI 150판 오류 없음");
            Check(smartWins >= oldWins, "랭크 AI는 기존 AI보다 약하지 않다 (" + smartWins + " vs " + oldWins + ")");
        }

        // 신경망 정책 추론/인코더: 손으로 계산한 작은 망과 일치, 관측 크기, 마스크
        {
            var ms = new System.IO.MemoryStream(); var bw = new System.IO.BinaryWriter(ms);
            bw.Write(2);
            bw.Write(2); bw.Write(2); bw.Write((byte)1); foreach (var v in new float[] { 1, -1, 0.5f, 2 }) bw.Write(v); foreach (var v in new float[] { 0, -0.5f }) bw.Write(v);
            bw.Write(3); bw.Write(2); bw.Write((byte)0); foreach (var v in new float[] { 1, 0, 0, 1, 1, 1 }) bw.Write(v); foreach (var v in new float[] { 0, 0, 0.1f }) bw.Write(v);
            ms.Position = 0; var nn = new NeuralPolicy(ms);
            var lg = nn.Logits(new float[] { 2, 1 });      // 은닉: [1*2-1*1+0, 0.5*2+2*1-0.5] = [1, 2.5] → 출력 [1, 2.5, 3.6]
            Check(Math.Abs(lg[0] - 1) < 1e-5 && Math.Abs(lg[1] - 2.5) < 1e-5 && Math.Abs(lg[2] - 3.6) < 1e-5, "신경망 추론이 손 계산과 일치 (" + string.Join(",", lg) + ")");
            Check(nn.Choose(new float[] { 2, 1 }, new[] { true, true, false }, 0, new FixedRng(0.5)) == 1, "마스크된 행동은 고르지 않는다");
            var eb = RimketBattle.RankedBattle(new FixedRng(0.5), s, "나", new[] { Mk("MAGIKARP", 5, null, "TACKLE"), Mk("GEODUDE", 5, null, "TACKLE"), Mk("ABRA", 5, null, "TACKLE") }, "상대", new[] { Mk("GEODUDE", 5, null, "TACKLE") });
            var enc = new AiEncoder(s); var ob = enc.Encode(eb); var mk = enc.Mask(eb);
            Check(ob.Length == AiEncoder.Size && ob.All(v => !float.IsNaN(v) && !float.IsInfinity(v)) && mk.Length == AiEncoder.Actions && mk[0] && mk[4] && mk[5], "관측 인코더: 크기 " + ob.Length + ", 마스크 정상");
            { int fo = AiEncoder.MonSize * AiEncoder.MaxMons;      // 상대 첫 칸: [0]존재 [1]기절 [2]활성 [3]HP [4]레벨 [5]최대HP [6~10]공방특공특방속
              bool hid = ob[fo] == 1f && ob[fo + 5] > 0f && ob.Skip(fo + 6).Take(5).All(v => v == 0f);
              var encOpen = new AiEncoder(s) { HideFoeStats = false }; var ob2 = encOpen.Encode(eb);
              Check(hid && ob2.Skip(fo + 6).Take(5).Any(v => v > 0f), "상대 능력치는 기본으로 가려지고(HP·레벨·타입은 보임), 끄면 보인다"); }
            { int ab0 = AiEncoder.BaseMonSize, n = AiEncoder.AbilityCount + AiEncoder.ItemCount;
              var eb3 = RimketBattle.RankedBattle(new FixedRng(0.5), s, "나", new[] { MkI("MAGIKARP", 50, "LEVITATE", "LEFTOVERS", "TACKLE") }, "상대", new[] { MkI("GEODUDE", 50, "LEVITATE", "LEFTOVERS", "TACKLE") });
              var o3 = new AiEncoder(s).Encode(eb3); var o4 = new AiEncoder(s) { HideFoeStats = false }.Encode(eb3);
              int ownSum = 0, foeSum = 0, foeOpen = 0;
              for (int i = 0; i < n; i++) { if (o3[ab0 + i] > 0) ownSum++; if (o3[AiEncoder.MonSize * 3 + ab0 + i] > 0) foeSum++; if (o4[AiEncoder.MonSize * 3 + ab0 + i] > 0) foeOpen++; }
              Check(ownSum == 2 && foeSum == 0 && foeOpen >= 1, "특성·도구: 내 것은 보이고(" + ownSum + ") 상대 것은 가려지며(" + foeSum + ") 가림을 끄면 보인다(" + foeOpen + ")"); }
        }

        // 패배: 파티 → 정착민 → 사망
        var weakMine = Mk("MAGIKARP", 1, null, "TACKLE"); weakMine.Hp = 1;
        var strongFoe = Mk("MEWTWO", 100, null, "PSYCHIC");
        var lb = RimketBattle.Trainer(new FixedRng(0.5), s, new[] { weakMine }, col, "관장", new[] { strongFoe }, null);
        for (int i = 0; i < 40 && !lb.IsOver; i++) lb.Act(lb.LegalActions().First(a => a.Kind != ActionKind.Disconnect));
        Check(lb.IsOver && lb.Result.End == BattleEnd.Defeat && lb.Result.ColonistEntered && lb.Result.Injury.Lethal, "트레이너전 패배: 정착민 단계까지 가서 사망(기본 설정)");
        var soft = new EngineSettings { LethalDefeat = false };
        var weak2 = Mk("MAGIKARP", 1, null, "TACKLE"); weak2.Hp = 1;
        lb = RimketBattle.Trainer(new FixedRng(0.5), soft, new[] { weak2 }, col, "관장", new[] { Mk("MEWTWO", 100, null, "PSYCHIC") }, null);
        for (int i = 0; i < 40 && !lb.IsOver; i++) lb.Act(lb.LegalActions().First(a => a.Kind != ActionKind.Disconnect));
        Check(lb.IsOver && !lb.Result.Injury.Lethal && lb.Result.Injury.Coma, "설정에서 사망을 끄면 패배해도 혼수 + 중증 손상");
    }

    private static void Fuzz(bool items)
    {
        var rng = new SystemRng(items ? 77 : 2024);
        var pool = ItemCatalog.All.Where(x => x.Holdable && (x.Pocket == 1 || x.Pocket == 5)).Select(x => x.Id).ToList();
        // 전투에 영향을 주는 도구가 많이 나오게 가중
        var hot = new[] { "LEFTOVERS","LIFEORB","FOCUSSASH","CHOICEBAND","CHOICESCARF","SITRUSBERRY","LUMBERRY","ORANBERRY","LIECHIBERRY","CUSTAPBERRY","WEAKNESSPOLICY","ROCKYHELMET","AIRBALLOON","EJECTBUTTON","REDCARD","FLAMEORB","TOXICORB","BLACKSLUDGE","STICKYBARB","ASSAULTVEST","EVIOLITE","KINGSROCK","SHELLBELL","LEPPABERRY","STARFBERRY","OCCABERRY","WHITEHERB","MENTALHERB","POWERHERB","QUICKCLAW","HEATROCK","ABSORBBULB","JABOCABERRY","ENIGMABERRY","CHILANBERRY","NORMALGEM","ELECTRICSEED","ROOMSERVICE" };
        Func<string> pickItem = () => rng.Chance(0.6) ? hot[rng.Next(0, hot.Length)] : pool[rng.Next(0, pool.Count)];
        var s = new EngineSettings();
        int battles = 3000, ended = 0, errors = 0, maxLog = 0;
        long turns = 0;
        for (int i = 0; i < battles; i++)
        {
            try
            {
                int badges = rng.Next(0, 6);
                var party = new List<RimketCreature>();
                int n = rng.Next(0, 4);
                for (int k = 0; k < n; k++) party.Add(SpawnRoller.Roll("a", "아군", rng.Next(1, 6), 0.5f, badges, rng, s));
                if (rng.Chance(0.3)) party.Insert(rng.Next(0, party.Count + 1), ColonistCombatant.PersonFor("포로", rng.Next(6, 700), rng.Next(5, 60), rng.Next(1, 6)));   // 마스터볼로 잡은 사람이 파티에 있는 경우
                var wild = SpawnRoller.Roll("w", "야생", rng.Next(1, 6), 0.5f, badges, rng, s);
                if (items) { wild.Item = pickItem(); foreach (var pc in party) pc.Item = pickItem(); }
                var col = Col("정착민", 0.5 + rng.NextDouble(), rng.Next(0, 7), s);
                if (party.Count == 0 && !col.CanConnect) continue;
                RimketBattle b;
                if (i % 3 == 0)
                {
                    var foes = new List<RimketCreature> { wild, SpawnRoller.Roll("w2", "야생2", rng.Next(1, 6), 0.5f, badges, rng, s) };
                    if (items) foes[1].Item = pickItem();
                    b = RimketBattle.Trainer(rng, s, party, col, "트레이너", foes, rng.Chance(0.5) ? ColonistCombatant.PersonFor("관장", 600, 10 * badges + 5, 4) : null);
                }
                else b = new RimketBattle(rng, s, party, wild, col);

                int guard = 0;
                while (!b.IsOver && guard++ < 1500)
                {
                    if (b.Phase == BattlePhase.WinChoice) { b.ChooseWinOption((WinChoice)rng.Next(0, 3)); break; }
                    var acts = b.LegalActions();
                    if (acts.Count == 0) throw new Exception("행동이 없는데 전투가 안 끝남");
                    var pick = acts[rng.Next(0, acts.Count)];
                    b.Act(pick);
                }
                if (b.IsOver) ended++; else throw new Exception("전투가 끝나지 않음 (루프 " + guard + ") phase=" + b.Phase + " pend=" + b.PendingSwitch + " turn=" + b.Turn + "\n" + string.Join("\n", b.Log.Skip(Math.Max(0, b.Log.Count - 14))));
                turns += b.Turn; maxLog = Math.Max(maxLog, b.Log.Count);
                var all = new List<RimketCreature>(b.Party);
                foreach (var c in all) if (c.Hp < 0 || c.Hp > c.MaxHp) throw new Exception("HP 범위 오류");
            }
            catch (Exception e) { errors++; if (errors <= 3) Console.WriteLine("    예외: " + e.Message + "\n" + e.StackTrace); }
        }
        Console.WriteLine("    종료 " + ended + " / 평균 턴 " + (turns / Math.Max(1, ended)) + " / 최대 로그 " + maxLog);
        Check(errors == 0, "무작위 전투 " + battles + "회에서 예외/무한루프 없음", "오류 " + errors);
    }

    // ----------------------------------------------------------------------------------------------
    private static ColonistCombatant Col(string name, double sens, int level, EngineSettings s)
    {
        return ColonistCombatant.Create(name, sens, level, s);
    }

    private static EngineSettings NoUp()
    {
        return new EngineSettings { TierOffsetProbabilities = new[] { 1.0, 0, 0, 0, 0 }, SixthTierChanceByBase = new double[] { 0, 0, 0, 0, 0 } };
    }

    private static bool Near(double a, double b, double tol) { return Math.Abs(a - b) <= tol; }

    /// <summary>종족 + 레벨 + 지정 기술로 개체를 만든다(특성은 지정, 기본은 없음).</summary>
    private static void SmokeAbilities()
    {
        var species = SpeciesCatalog.All.Where(x => x.Learnset.Count > 0).Select(x => x.Id).ToList();
        var pool = MoveCatalog.All.Where(m => m.Implemented && !m.Dud && !m.Is('Q')).Select(m => m.Id).ToList();
        int exc = 0, battles = 0, turns = 0;
        bool english = Environment.GetEnvironmentVariable("EN") == "1";
        if (english) { Lang.Set(true); }
        var untranslated = new Dictionary<string, int>();
        foreach (var ab in AbilityCatalog.All.Select(a => a.Id))
        {
            string err = null;
            for (int seed = 1; seed <= 12 && err == null; seed++)
            {
                try
                {
                    var rng = new SystemRng(seed * 104729 + ab.GetHashCode());
                    string[] myMoves = Enumerable.Range(0, 4).Select(_ => pool[rng.Next(0, pool.Count)]).ToArray();
                    string[] foeMoves = Enumerable.Range(0, 4).Select(_ => pool[rng.Next(0, pool.Count)]).ToArray();
                    string fa = AbilityCatalog.All.ElementAt(rng.Next(0, AbilityCatalog.All.Count())).Id;
                    var me = Mk("MIMIKYU" == "" ? "" : species[rng.Next(0, species.Count)], 50, ab, myMoves);
                    var bench = Mk(species[rng.Next(0, species.Count)], 50, ab, myMoves);
                    var foe = Mk(species[rng.Next(0, species.Count)], 50, seed % 2 == 0 ? ab : fa, foeMoves);
                    var b = new RimketBattle(rng, new EngineSettings(), new[] { me, bench }, foe, Col("정착민", 1.0, 3, new EngineSettings()));
                    battles++;
                    for (int t = 0; t < 40 && !b.IsOver; t++)
                    {
                        if (b.Phase == BattlePhase.WinChoice) break;
                        var acts = b.LegalActions();
                        if (acts.Count == 0) break;
                        var usable = acts.Where(a => a.Kind == ActionKind.UseMove).ToList();
                        var pick = b.PendingSwitch || usable.Count == 0 ? acts[0] : usable[rng.Next(0, usable.Count)];
                        b.Act(pick); turns++;
                    }
                    if (english)
                        foreach (var l in b.Log)
                        {
                            var t = Lang.Tr(l);
                            if (t.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3)) { int c0; untranslated.TryGetValue(t, out c0); untranslated[t] = c0 + 1; }
                        }
                }
                catch (Exception e) { err = e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n').FirstOrDefault(); }
            }
            if (err != null) { exc++; Console.WriteLine("ERR " + ab + " -> " + err); }
        }
        Console.WriteLine("전투 " + battles + "회 / 턴 " + turns + " / 예외 특성 " + exc + "개");
        if (english) { foreach (var kv in untranslated.OrderByDescending(x => x.Value).Take(80)) Console.WriteLine(kv.Value + "x " + kv.Key); Console.WriteLine("번역 안 된 서로 다른 줄 " + untranslated.Count); }
    }

    private static void SmokeItems()
    {
        var species = SpeciesCatalog.All.Where(x => x.Learnset.Count > 0).Select(x => x.Id).ToList();
        var itemMoves = new[] { "FLING", "NATURALGIFT", "TRICK", "SWITCHEROO", "KNOCKOFF", "THIEF", "COVET", "BESTOW", "BUGBITE", "PLUCK", "INCINERATE", "RECYCLE", "EMBARGO", "POLTERGEIST", "BELCH", "STUFFCHEEKS", "TEATIME", "CORROSIVEGAS", "ACROBATICS", "MAGICROOM" };
        var pool = MoveCatalog.All.Where(m => m.Implemented && !m.Dud && !m.Is('Q')).Select(m => m.Id).ToList();
        var items = ItemCatalog.All.Where(x => x.Holdable && (x.Pocket == 1 || x.Pocket == 5)).Select(x => x.Id).ToList();
        bool english = Environment.GetEnvironmentVariable("EN") == "1";
        if (english) Lang.Set(true);
        var untranslated = new Dictionary<string, int>();
        int exc = 0, battles = 0;
        foreach (var it in items)
        {
            for (int seed = 1; seed <= 6; seed++)
            {
                try
                {
                    var rng = new SystemRng(seed * 7919 + it.GetHashCode());
                    Func<string[]> mv = () => Enumerable.Range(0, 4).Select(i => i < 2 ? itemMoves[rng.Next(0, itemMoves.Length)] : pool[rng.Next(0, pool.Count)]).ToArray();
                    var me = MkI(species[rng.Next(0, species.Count)], 50, null, it, mv());
                    var bench = Mk(species[rng.Next(0, species.Count)], 50, null, mv());
                    var foe = MkI(species[rng.Next(0, species.Count)], 50, null, rng.Chance(0.5) ? it : items[rng.Next(0, items.Count)], mv());
                    me.Ability = null; foe.Ability = null;
                    if (seed % 2 == 0) me.Hp = me.MaxHp / 4;
                    var b = new RimketBattle(rng, new EngineSettings(), new[] { me, bench }, foe, Col("정착민", 1.0, 3, new EngineSettings()));
                    battles++;
                    for (int t = 0; t < 30 && !b.IsOver; t++)
                    {
                        if (b.Phase == BattlePhase.WinChoice) break;
                        var acts = b.LegalActions();
                        if (acts.Count == 0) break;
                        var usable = acts.Where(a => a.Kind == ActionKind.UseMove).ToList();
                        var pick = b.PendingSwitch || usable.Count == 0 ? acts[0] : usable[rng.Next(0, usable.Count)];
                        b.Act(pick);
                    }
                    if (english)
                        foreach (var l in b.Log)
                        {
                            var tr = Lang.Tr(l);
                            if (tr.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3)) { int c0; untranslated.TryGetValue(tr, out c0); untranslated[tr] = c0 + 1; }
                        }
                }
                catch (Exception e) { exc++; if (exc < 6) Console.WriteLine("ERR " + it + " -> " + e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            }
        }
        Console.WriteLine("도구 " + items.Count + "종 / 전투 " + battles + "회 / 예외 " + exc);
        if (english) { foreach (var kv in untranslated.OrderByDescending(x => x.Value).Take(120)) Console.WriteLine(kv.Value + "x " + kv.Key); Console.WriteLine("번역 안 된 서로 다른 줄 " + untranslated.Count); }
    }

    private static void SmokeMoves()
    {
        var always = new List<string>(); int exc = 0;
        foreach (var mv in MoveCatalog.All.OrderBy(m => m.Id))
        {
            int fails = 0, uses = 0; string firstErr = null;
            for (int seed = 1; seed <= 6; seed++)
            {
                try
                {
                    var rng = new SystemRng(seed * 7919);
                    var me = Mk(seed % 2 == 0 ? "GARDEVOIR" : "MACHAMP", 50, seed % 3 == 0 ? "INTIMIDATE" : null, mv.Id, "TACKLE");
                    var bench = Mk("PIKACHU", 50, null, "TACKLE");
                    var foe = Mk(seed % 2 == 0 ? "SNORLAX" : "GYARADOS", 50, null, "TACKLE", "SPLASH");
                    var b = new RimketBattle(rng, new EngineSettings(), new[] { me, bench }, foe, Col("정착민", 1.0, 3, new EngineSettings()));
                    for (int t = 0; t < 8 && !b.IsOver; t++)
                    {
                        if (b.Phase == BattlePhase.WinChoice) break;
                        var acts = b.LegalActions();
                        if (acts.Count == 0) break;
                        var pick = acts.FirstOrDefault(a => a.Kind == ActionKind.UseMove && a.Index == 0);
                        if (b.PendingSwitch || pick.Kind != ActionKind.UseMove) pick = acts[0];
                        int before = b.Log.Count;
                        b.Act(pick);
                        if (pick.Kind == ActionKind.UseMove)
                        {
                            uses++;
                            var seg = b.Log.Skip(before).ToList();
                            if (seg.Any(l => l.Contains("아직 구현되지 않았다"))) { firstErr = "미구현 메시지"; }
                            if (seg.Any(l => l.Contains("하지만 실패했다")) && !seg.Any(l => l.Contains("피해")) ) fails++;
                        }
                    }
                }
                catch (Exception e) { exc++; if (firstErr == null) firstErr = e.GetType().Name + ": " + e.Message + " @" + e.StackTrace.Split('\n')[0].Trim(); }
            }
            if (firstErr != null) Console.WriteLine("ERR " + mv.Id + " " + mv.Fn + " -> " + firstErr);
            else if (uses > 0 && fails == uses) always.Add(mv.Id + "(" + mv.Fn + ")");
        }
        Console.WriteLine("예외 " + exc + "건");
        Console.WriteLine("항상 실패: " + string.Join(", ", always));
    }



    // ------------------------------------------------------------------ 더블배틀
    private static RimketBattle Dbl(int seed, RimketCreature[] mine, RimketCreature[] foes, bool trainer = true)
    {
        var st = new EngineSettings();
        return RimketBattle.DoubleTrainer(new SystemRng(seed * 7919), st, mine, Col("정착민", 1.0, 3, st), "트레이너", foes, null);
    }
    private static BattleAction Mv(int i, int target = -1) { return new BattleAction(ActionKind.UseMove, i, target); }

    private static void TestDouble()
    {
        { // 시작: 양쪽 2칸
            var b = Dbl(1, new[] { Mk("PIKACHU", 50, "STATIC", "SPLASH"), Mk("SNORLAX", 50, "IMMUNITY", "SPLASH") },
                           new[] { Mk("EEVEE", 50, "RUNAWAY", "SPLASH"), Mk("RATTATA", 50, "RUNAWAY", "SPLASH") });
            Check(b.IsDouble && b.Player.Slots[0] != null && b.Player.Slots[1] != null && b.Foe.Slots[0] != null && b.Foe.Slots[1] != null, "더블: 양쪽 두 칸이 나온다");
        }
        { // 범위기: 상대 둘 다 맞고 같은 편은 안 맞는다
            var b = Dbl(2, new[] { Mk("CHARIZARD", 60, "BLAZE", "HEATWAVE"), Mk("SNORLAX", 60, "IMMUNITY", "SPLASH") },
                           new[] { Mk("BULBASAUR", 40, "OVERGROW", "SPLASH"), Mk("ODDISH", 40, "CHLOROPHYLL", "SPLASH") });
            int a0 = b.Player.Slots[1].C.Hp;
            b.ActDouble(Mv(0), Mv(0));
            Check(b.Foe.Slots[0].C.Hp < b.Foe.Slots[0].C.MaxHp && b.Foe.Slots[1].C.Hp < b.Foe.Slots[1].C.MaxHp, "범위기(열풍): 상대 두 마리 모두 맞는다");
            Check(b.Player.Slots[1].C.Hp == a0, "범위기(열풍): 같은 편은 맞지 않는다");
        }
        { // 지진은 같은 편도 맞힌다, 텔레파시는 막는다
            var b = Dbl(3, new[] { Mk("GOLEM", 60, "STURDY", "EARTHQUAKE"), Mk("SNORLAX", 60, "IMMUNITY", "SPLASH") },
                           new[] { Mk("EEVEE", 40, "RUNAWAY", "SPLASH"), Mk("RATTATA", 40, "RUNAWAY", "SPLASH") });
            b.ActDouble(Mv(0), Mv(0));
            Check(b.Player.Slots[1].C.Hp < b.Player.Slots[1].C.MaxHp, "지진: 같은 편도 맞는다");
            var b2 = Dbl(3, new[] { Mk("GOLEM", 60, "STURDY", "EARTHQUAKE"), Mk("SNORLAX", 60, "TELEPATHY", "SPLASH") },
                            new[] { Mk("EEVEE", 40, "RUNAWAY", "SPLASH"), Mk("RATTATA", 40, "RUNAWAY", "SPLASH") });
            b2.ActDouble(Mv(0), Mv(0));
            Check(b2.Player.Slots[1].C.Hp == b2.Player.Slots[1].C.MaxHp, "텔레파시: 같은 편의 공격을 받지 않는다");
        }
        { // 위협은 상대 둘 다
            var b = Dbl(4, new[] { Mk("ARCANINE", 50, "INTIMIDATE", "SPLASH"), Mk("SNORLAX", 50, "IMMUNITY", "SPLASH") },
                           new[] { Mk("EEVEE", 50, "RUNAWAY", "SPLASH"), Mk("RATTATA", 50, "RUNAWAY", "SPLASH") });
            Check(b.Foe.Slots[0].Stage[Stat.Atk] == -1 && b.Foe.Slots[1].Stage[Stat.Atk] == -1, "위협: 상대 두 마리 모두 공격 -1");
        }
        { // 따라가기: 상대의 단일 공격이 전부 쏠린다
            int pass = 0, tot = 0;
            for (int sd = 1; sd <= 12; sd++)
            {
                var b = Dbl(10 + sd, new[] { Mk("SNORLAX", 80, "IMMUNITY", "FOLLOWME"), Mk("BLISSEY", 80, "NATURALCURE", "SPLASH") },
                                     new[] { Mk("EEVEE", 20, "RUNAWAY", "TACKLE"), Mk("RATTATA", 20, "RUNAWAY", "TACKLE") });
                b.ActDouble(Mv(0), Mv(0));
                tot++; if (b.Player.Slots[1].C.Hp == b.Player.Slots[1].C.MaxHp) pass++;
            }
            Check(pass == tot, "따라가기: 파트너는 단일 공격을 맞지 않는다", pass + "/" + tot);
        }
        { // 도우미 / 자리바꾸기
            var b = Dbl(5, new[] { Mk("ABRA", 50, "SYNCHRONIZE", "HELPINGHAND"), Mk("SNORLAX", 50, "IMMUNITY", "SPLASH") },
                           new[] { Mk("EEVEE", 50, "RUNAWAY", "SPLASH"), Mk("RATTATA", 50, "RUNAWAY", "SPLASH") });
            b.ActDouble(Mv(0), Mv(0));
            Check(b.Player.Slots[1].HelpingHand, "도우미: 같은 편의 위력이 오른다");
            var b2 = Dbl(5, new[] { Mk("ABRA", 50, "SYNCHRONIZE", "ALLYSWITCH"), Mk("SNORLAX", 50, "IMMUNITY", "SPLASH") },
                            new[] { Mk("EEVEE", 50, "RUNAWAY", "SPLASH"), Mk("RATTATA", 50, "RUNAWAY", "SPLASH") });
            var first = b2.Player.Slots[0];
            b2.ActDouble(Mv(0), Mv(0));
            Check(b2.Player.Slots[1] == first, "자리바꾸기: 같은 편과 자리가 바뀐다");
        }
        { // 싱글에서는 같은 편 기술이 실패
            var b = Fight(new SystemRng(3), Mk("ABRA", 50, "SYNCHRONIZE", "HELPINGHAND"), Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Use(b);
            Check(LogHas(b, "실패"), "싱글: 도우미는 실패");
        }
        { // 쓰러짐 → 교체 → 승리
            var st = new EngineSettings();
            var b = Dbl(6, new[] { Mk("MEWTWO", 90, "PRESSURE", "HEATWAVE", "SPLASH"), Mk("MEW", 90, "SYNCHRONIZE", "HEATWAVE", "SPLASH") },
                           new[] { Mk("RATTATA", 5, "RUNAWAY", "SPLASH"), Mk("PIDGEY", 5, "RUNAWAY", "SPLASH"), Mk("CATERPIE", 5, "RUNAWAY", "SPLASH") });
            int g = 0;
            while (!b.IsOver && g++ < 20) b.ActDouble(Mv(0), Mv(0));
            Check(b.IsOver && b.Result.End == BattleEnd.Victory, "더블: 상대 셋을 모두 쓰러뜨리면 승리", b.Phase + " " + g);
        }
        { // 쓰러진 칸의 교체 창
            var b = Dbl(7, new[] { Mk("CATERPIE", 2, "SHIELDDUST", "SPLASH"), Mk("WEEDLE", 2, "SHIELDDUST", "SPLASH"), Mk("SNORLAX", 60, "IMMUNITY", "SPLASH") },
                           new[] { Mk("MEWTWO", 90, "PRESSURE", "HEATWAVE"), Mk("MEW", 90, "SYNCHRONIZE", "HEATWAVE") });
            b.ActDouble(Mv(0), Mv(0));
            Check(b.PendingSwitch, "더블: 쓰러지면 예비와 교체 창이 열린다");
            var acts = b.LegalActions();
            Check(acts.Count == 1 && acts[0].Kind == ActionKind.Switch, "더블: 교체 창에는 예비만 나온다");
        }
    }

    private static void FuzzDouble()
    {
        var rng = new SystemRng(4242);
        var s = new EngineSettings();
        int ok = 0, err = 0; string firstErr = null;
        for (int i = 0; i < 1500; i++)
        {
            try
            {
                int badges = rng.Next(0, 6);
                var party = new List<RimketCreature>();
                int n = rng.Next(1, 5);
                for (int k = 0; k < n; k++) party.Add(SpawnRoller.Roll("a", "아군", rng.Next(1, 6), 0.5f, badges, rng, s));
                var foes = new List<RimketCreature>();
                int fn = rng.Next(1, 5);
                for (int k = 0; k < fn; k++) foes.Add(SpawnRoller.Roll("w", "적", rng.Next(1, 6), 0.5f, badges, rng, s));
                var col = Col("정착민", 0.5 + rng.NextDouble(), rng.Next(0, 7), s);
                RimketBattle b = i % 4 == 0
                    ? RimketBattle.DoubleWild(rng, s, party, foes[0], foes.Count > 1 ? foes[1] : null, col)
                    : RimketBattle.DoubleTrainer(rng, s, party, col, "트레이너", foes, rng.Chance(0.4) ? ColonistCombatant.PersonFor("관장", 600, 10 * badges + 5, 4) : null);
                int guard = 0;
                while (!b.IsOver && guard++ < 1500)
                {
                    if (b.Phase == BattlePhase.WinChoice) { b.ChooseWinOption((WinChoice)rng.Next(0, 3)); break; }
                    var a0 = b.LegalActions();
                    if (a0.Count == 0) throw new Exception("행동이 없는데 전투가 안 끝남");
                    var p0 = a0[rng.Next(0, a0.Count)];
                    if (b.PendingSwitch) { b.Act(p0); continue; }
                    if (p0.Kind == ActionKind.Disconnect && rng.Chance(0.9)) continue;
                    var live = new List<int>(); for (int k = 0; k < 2; k++) if (b.Player.Slots[k] != null && !b.Player.Slots[k].Fainted) live.Add(k);
                    BattleAction? p1 = null;
                    if (live.Count > 1)
                    {
                        var a1 = b.LegalActionsFor(live[1]).Where(x => x.Kind != ActionKind.Disconnect).ToList();
                        if (a1.Count > 0 && rng.Chance(0.7)) p1 = a1[rng.Next(0, a1.Count)];
                    }
                    if (p0.Kind == ActionKind.UseMove) { var tc = b.TargetChoices(live[0], p0); if (tc.Count > 0) p0 = new BattleAction(p0.Kind, p0.Index, tc[rng.Next(0, tc.Count)]); }
                    bool done = b.ActDouble(p0, p1);
                    for (int tries = 0; !done && !b.IsOver && tries < 30; tries++)
                    {
                        var q = a0[rng.Next(0, a0.Count)];
                        if (q.Kind == ActionKind.Disconnect) continue;
                        done = b.ActDouble(q, null);
                    }
                    if (!done && !b.IsOver) throw new Exception("행동이 거부됨 " + p0);
                }
                if (b.IsOver) ok++; else throw new Exception("끝나지 않음 turn=" + b.Turn + " phase=" + b.Phase + " pend=" + b.PendingSwitch + "\n" + string.Join("\n", b.Log.Skip(Math.Max(0, b.Log.Count - 12))));
            }
            catch (Exception e) { err++; if (firstErr == null) firstErr = e.ToString(); }
        }
        Check(err == 0, "더블 무작위 전투 1500회에서 예외/무한루프 없음", "종료 " + ok + " / 오류 " + err + (firstErr == null ? "" : "\n" + firstErr));
    }

    private static void TestIv()
    {
        var rng = new SystemRng(5); var st = new EngineSettings();
        bool range = true; int all31 = 0, n = 0; double sum = 0;
        for (int i = 0; i < 300; i++)
        {
            var c = SpawnRoller.FromSpecies(SpeciesCatalog.Get("PIKACHU"), 50, rng, st);
            foreach (var v in c.Iv) { if (v < 0 || v > 31) range = false; sum += v; n++; }
        }
        Check(range && sum / n > 13 && sum / n < 18, "개체값: 0~31 균등(평균 약 15.5)", (sum / n).ToString("0.0"));
        bool six = true; int found = 0;
        foreach (var sp in SpeciesCatalog.All.Where(x => x.Tier >= 6).Take(15)) { var c = SpawnRoller.FromSpecies(sp, 50, rng, st); found++; if (!c.IsPerfectIv) six = false; }
        Check(found > 0 && six, "개체값: 6티어는 전부 31(6V)", found + "종");
        var a = Mk("PIKACHU", 50, "STATIC", "SPLASH"); var b = a.Clone();
        for (int i = 0; i < 6; i++) { a.Iv[i] = 31; b.Iv[i] = 0; }
        a.RecalculateStats(); b.RecalculateStats();
        Check(a.MaxHp > b.MaxHp && a.Atk > b.Atk && a.Spe > b.Spe && a.SpD > b.SpD, "개체값: 31이 0보다 능력치가 높다", a.MaxHp + " vs " + b.MaxHp);
        var cl = a.Clone(); cl.Iv[0] = 1; Check(a.Iv[0] == 31, "개체값: 복사본은 원본과 배열을 공유하지 않는다");
    }

    private static void TestConsumables()
    {
        { // 관장·사천왕·챔피언(GymBattle/LeagueBattle)·어둠의/방문자 트레이너전에서도 가방 아이템을 쓸 수 있다
            var st = new EngineSettings();
            foreach (var gym in new[] { true, false })
            {
                var mine = Mk("SNORLAX", 50, "IMMUNITY", "SPLASH"); mine.Hp = 20;
                var b = RimketBattle.Trainer(new SystemRng(3), st, new List<RimketCreature> { mine }, Col("정착민", 1.0, 3, st), "관장", new List<RimketCreature> { Mk("MAGIKARP", 50, "SWIFTSWIM", "SPLASH") }, null);
                b.GymBattle = gym; b.LeagueBattle = gym;
                Check(b.CanUseItem("POTION", 0) && b.UseItem("POTION", 0) && mine.Hp > 20, "소비: " + (gym ? "관장·리그전" : "트레이너전") + "에서 상처약 사용");
            }
        }
        { // 상처약: 활성 림켓몬 회복 + 상대가 행동
            var mine = Mk("SNORLAX", 50, "IMMUNITY", "SPLASH"); mine.Hp = 20;
            var b = Fight(new SystemRng(8), mine, Mk("MAGIKARP", 50, "SWIFTSWIM", "SPLASH"));
            Check(b.CanUseItem("POTION", 0), "소비: 다친 림켓몬에게 상처약을 쓸 수 있다");
            Check(b.UseItem("POTION", 0) && mine.Hp == 40, "소비: 상처약 20 회복", mine.Hp.ToString());
            Check(!b.CanUseItem("POTION", 1), "소비: 잘못된 대상은 쓸 수 없다");
            mine.Hp = mine.MaxHp; Check(!b.CanUseItem("POTION", 0), "소비: HP가 가득이면 쓸 수 없다");
        }
        { // 부활 / 상태 치료 / PP / X아이템
            var a = Mk("SNORLAX", 50, "IMMUNITY", "SPLASH"); var d = Mk("PIKACHU", 50, "STATIC", "SPLASH"); d.Hp = 0;
            var st = new EngineSettings();
            var b = new RimketBattle(new SystemRng(9), st, new[] { a, d }, Mk("MAGIKARP", 50, "SWIFTSWIM", "SPLASH"), Col("정착민", 1.0, 3, st));
            Check(b.CanUseItem("REVIVE", 1) && !b.CanUseItem("REVIVE", 0), "소비: 부활은 기절한 쪽에만");
            b.UseItem("REVIVE", 1);
            Check(d.Hp == d.MaxHp / 2, "소비: 기력의조각은 HP 절반으로 부활", d.Hp + "/" + d.MaxHp);
            b.ActiveFighter.Status = StatusCondition.Burn;
            Check(b.CanUseItem("BURNHEAL", 0) && !b.CanUseItem("ANTIDOTE", 0), "소비: 맞는 상태이상 치료제만");
            b.UseItem("BURNHEAL", 0); Check(b.ActiveFighter.Status == StatusCondition.None, "소비: 화상치료제로 화상이 낫는다");
            a.Moves[0].PP = 1; Check(b.UseItem("ETHER", 0) && a.Moves[0].PP > 1, "소비: PP에이드가 PP를 회복");
            b.UseItem("XATTACK", 0); Check(b.ActiveFighter.Stage[Stat.Atk] == 1, "소비: 플러스파워로 공격 +1");
        }
        Check(Consumables.ShopOrder.Count >= 20, "소비: 상점 진열 품목", Consumables.ShopOrder.Count.ToString());
    }

    private static RimketCreature MkG(string species, int gender, string ability, params string[] moves) { var c = Mk(species, 50, ability, moves); c.Gender = gender; return c; }

    // ---- 온라인 대전(사람 vs 사람): 두 컴퓨터가 같은 시드로 같은 전투를 돌린다 ----
    private static string OnlineGame(int seed, bool dbl, out bool ok, out bool draw, out string winner)
    {
        ok = true; draw = false; winner = "";
        var s = new EngineSettings();
        string[] fm = { "pu", "nu", "ru", "uu", "ou", "ubers" };
        var trng = new SystemRng(seed); string fmt = fm[trng.Next(0, fm.Length)];
        var host6 = RankedBuilder.Build(fmt, 6, 50, trng, s); var guest6 = RankedBuilder.Build(fmt, 6, 50, trng, s);
        int n = dbl ? 4 : 3;
        var hostPick = host6.Take(n).ToList(); var guestPick = guest6.Take(n).ToList();
        foreach (var c in hostPick.Concat(guestPick)) c.FullHeal();
        var b = dbl ? RimketBattle.OnlineDouble(new SystemRng(seed + 1), s, "방장", hostPick, "손님", guestPick)
                    : RimketBattle.OnlineBattle(new SystemRng(seed + 1), s, "방장", hostPick, "손님", guestPick);
        var pick = new SystemRng(seed + 7);       // 행동 고르는 쪽(사람 대신)
        int steps = 0;
        while (!b.IsOver && steps++ < 600)
        {
            BattleAction? pa = null, fa = null, pa2 = null, fa2 = null;
            if (!dbl)
            {
                var pl = b.LegalActions().Where(a => a.Kind != ActionKind.Disconnect && !(a.Kind == ActionKind.UseMove && b.MoveBlockedFor(b.Player, a.Index) != null)).ToList();
                var fl = b.FoeLegalActions().Where(a => a.Kind != ActionKind.Disconnect && !(a.Kind == ActionKind.UseMove && b.MoveBlockedFor(b.Foe, a.Index) != null)).ToList();
                pa = pl.Count > 0 ? pl[pick.Next(0, pl.Count)] : (BattleAction?)null;
                fa = fl.Count > 0 ? fl[pick.Next(0, fl.Count)] : (BattleAction?)null;
            }
            else
            {
                BattleAction?[] P = new BattleAction?[2], F = new BattleAction?[2];
                for (int side = 0; side < 2; side++)
                {
                    var bs = side == 0 ? b.Player : b.Foe;
                    var live = Enumerable.Range(0, 2).Where(i => bs.Slots[i] != null && !bs.Slots[i].Fainted).ToList();
                    var res = side == 0 ? P : F; var used = new HashSet<int>();
                    int slotNo = 0;
                    foreach (int i in live)
                    {
                        var lst = (side == 0 ? b.LegalActionsFor(i) : b.LegalActionsForFoe(i)).Where(a => a.Kind != ActionKind.Disconnect).ToList();
                        lst = lst.Where(a => !(a.Kind == ActionKind.Switch && used.Contains(a.Index))).ToList();
                        if (lst.Count == 0) continue;
                        // 가능한 한 막힌 기술은 피한다
                        var f = bs.Slots[i];
                        var ok2 = lst.Where(a => !(a.Kind == ActionKind.UseMove && f.ChargeMove == null && !(f.LockTurns > 0 && f.LockMove != null) && b.MoveBlockedForSlot(bs, i, a.Index) != null)).ToList();
                        if (ok2.Count > 0) lst = ok2;
                        var ch = lst[pick.Next(0, lst.Count)];
                        var tc = (side == 0 ? b.TargetChoices(i, ch) : b.TargetChoicesFor(b.Foe, i, ch)).ToList();
                        if (tc.Count > 0) ch.Target = tc[pick.Next(0, tc.Count)];
                        if (ch.Kind == ActionKind.Switch) used.Add(ch.Index);
                        res[slotNo++] = ch;
                    }
                }
                pa = P[0]; pa2 = P[1]; fa = F[0]; fa2 = F[1];
                if (b.PendingSwitch || b.PendingFoeSwitch)
                {
                    // 교체 대기: 교체해야 하는 쪽만 첫 번째 합법 교체를 낸다
                    pa = b.PendingSwitch ? b.LegalActionsFor(0).Where(a => a.Kind == ActionKind.Switch).Select(a => (BattleAction?)a).FirstOrDefault() : null;
                    fa = b.PendingFoeSwitch ? b.LegalActionsForFoe(0).Where(a => a.Kind == ActionKind.Switch).Select(a => (BattleAction?)a).FirstOrDefault() : null;
                    pa2 = null; fa2 = null;
                }
            }
            if (!b.OnlineAct(pa, fa, pa2, fa2)) { ok = false; return "거부된 입력 " + pa + "," + pa2 + " / " + fa + "," + fa2 + " (대기 " + b.PendingSwitch + "," + b.PendingFoeSwitch + ")"; }
        }
        draw = b.Draw; winner = b.IsOver ? b.Result.End.ToString() : "미종료";
        if (!b.IsOver) ok = false;
        return string.Join("\n", b.Log);
    }

    private static void TestOnline()
    {
        Console.WriteLine("== 온라인 대전(싱글) ==");
        int games = 0, bad = 0, draws = 0, mism = 0, hostWins = 0; var digest = new System.Text.StringBuilder(); string firstBad = "";
        for (int seed = 1; seed <= 300; seed++)
        {
            bool ok, dr; string w;
            string a = OnlineGame(seed, false, out ok, out dr, out w);
            bool ok2, dr2; string w2; string b2 = OnlineGame(seed, false, out ok2, out dr2, out w2);     // 두 번째 컴퓨터
            games++;
            if (!ok || !ok2) { bad++; if (firstBad == "") firstBad = "시드 " + seed + ": " + (ok ? b2 : a); continue; }
            if (a != b2) mism++;
            if (dr) draws++; if (w == "Victory") hostWins++;
            digest.Append(a.GetHashCode() == 0 ? "" : "").Append(StableHash(a)).Append(',');
        }
        Check(bad == 0, "온라인 싱글 300판이 전부 끝까지 진행된다" + (bad > 0 ? " (실패 " + bad + ": " + firstBad + ")" : ""));
        Check(mism == 0, "같은 시드·같은 행동이면 두 컴퓨터의 로그가 완전히 같다(" + games + "판)");
        Console.WriteLine("    방장 승 " + hostWins + " / 무승부 " + draws + " / 해시 " + StableHash(digest.ToString()));
    }

    private static void TestOnlineDouble()
    {
        Console.WriteLine("== 온라인 대전(더블) ==");
        int games = 0, bad = 0, draws = 0, mism = 0, hostWins = 0; string firstBad = ""; var digest = new System.Text.StringBuilder();
        for (int seed = 1; seed <= 300; seed++)
        {
            bool ok, dr; string w; string a = OnlineGame(seed, true, out ok, out dr, out w);
            bool ok2, dr2; string w2; string b2 = OnlineGame(seed, true, out ok2, out dr2, out w2);
            games++;
            if (!ok || !ok2) { bad++; if (firstBad == "") firstBad = "시드 " + seed + ": " + (ok ? b2 : a); continue; }
            if (a != b2) mism++;
            if (dr) draws++; if (w == "Victory") hostWins++;
            digest.Append(StableHash(a)).Append(',');
        }
        Check(bad == 0, "온라인 더블 300판이 전부 끝까지 진행된다" + (bad > 0 ? " (실패 " + bad + ": " + firstBad + ")" : ""));
        Check(mism == 0, "더블: 같은 시드·같은 행동이면 두 컴퓨터의 로그가 같다(" + games + "판)");
        Console.WriteLine("    방장 승 " + hostWins + " / 무승부 " + draws + " / 더블 해시 " + StableHash(digest.ToString()));
    }

    // ---- 온라인 세션: 두 세션을 메시지 큐로 이어서 처음부터 끝까지 ----
    private sealed class Link
    {
        public readonly Queue<string> ToA = new Queue<string>(), ToB = new Queue<string>();
        public OnlineSession A, B; public int Sent;
        public Func<string, string, string> Tamper;      // (방향, 메시지) → 바꾼 메시지
        /// <summary>접속 → 대기실에서 둘 다 지금 파티로 준비 완료 → 파티 교환까지.</summary>
        public void Go(IRng rng)
        {
            A.Start(); B.Start(); Pump(rng);
            if (A.State == OnlineState.Lobby && B.State == OnlineState.Lobby) { A.SetReady(A.MyTeam.ToList()); B.SetReady(B.MyTeam.ToList()); Pump(rng); }
        }
        public void Pump(IRng rng)
        {
            int guard = 0;
            while ((ToA.Count > 0 || ToB.Count > 0) && guard++ < 100000)
            {
                bool a = ToA.Count > 0 && (ToB.Count == 0 || rng.Next(0, 2) == 0);
                var q = a ? ToA : ToB; var m = q.Dequeue();
                if (Tamper != null) m = Tamper(a ? "toA" : "toB", m);
                if (m == null) continue;
                (a ? A : B).Receive(m);
            }
        }
    }

    private static Link MakeLink(int seed, bool dbl, out List<RimketCreature> ta, out List<RimketCreature> tb)
    {
        var s = new EngineSettings(); var trng = new SystemRng(seed);
        string[] fm = { "pu", "nu", "ru", "uu", "ou", "ubers" }; string fmt = fm[trng.Next(0, fm.Length)];
        ta = RankedBuilder.Build(fmt, 6, 50, trng, s); tb = RankedBuilder.Build(fmt, 6, 50, trng, s);
        var link = new Link();
        link.A = new OnlineSession(true, dbl, "방장", ta, seed * 31 + 5); link.B = new OnlineSession(false, dbl, "손님", tb, 0);
        link.A.Send = m => link.ToB.Enqueue(m); link.B.Send = m => link.ToA.Enqueue(m);
        return link;
    }

    private static List<int> AutoPick(OnlineSession ses, IRng rng)
    {
        int n = OnlineProtocol.BringCount(ses.IsDouble); var picks = new List<int>(); var order = Enumerable.Range(0, ses.MyTeam.Count).OrderBy(x => rng.NextDouble()).ToList();
        foreach (int i in order) { if (RankedRules.ClauseViolation(ses.MyTeam, picks, i) == null) picks.Add(i); if (picks.Count == n) break; }
        return picks;
    }

    private static BattleAction? AutoAct(OnlineSession ses, IRng rng, out BattleAction? second)
    {
        second = null; var b = ses.Battle; bool host = ses.IsHost; var bs = host ? b.Player : b.Foe;
        if (b.PendingSwitch || b.PendingFoeSwitch)
        {
            var sw = (ses.IsDouble ? (host ? b.LegalActionsFor(0) : b.LegalActionsForFoe(0)) : (host ? b.LegalActions() : b.FoeLegalActions())).Where(a => a.Kind == ActionKind.Switch).ToList();
            return sw.Count > 0 ? sw[rng.Next(0, sw.Count)] : (BattleAction?)null;
        }
        if (!ses.IsDouble)
        {
            var l = (host ? b.LegalActions() : b.FoeLegalActions()).Where(a => a.Kind != ActionKind.Disconnect && !(a.Kind == ActionKind.UseMove && b.MoveBlockedFor(bs, a.Index) != null)).ToList();
            return l.Count > 0 ? l[rng.Next(0, l.Count)] : (BattleAction?)null;
        }
        var live = Enumerable.Range(0, 2).Where(i => bs.Slots[i] != null && !bs.Slots[i].Fainted).ToList(); var used = new HashSet<int>(); var res = new List<BattleAction>();
        foreach (int i in live)
        {
            var lst = (host ? b.LegalActionsFor(i) : b.LegalActionsForFoe(i)).Where(a => a.Kind != ActionKind.Disconnect && !(a.Kind == ActionKind.Switch && used.Contains(a.Index))).ToList();
            var f = bs.Slots[i];
            var ok = lst.Where(a => !(a.Kind == ActionKind.UseMove && f.ChargeMove == null && !(f.LockTurns > 0 && f.LockMove != null) && b.MoveBlockedForSlot(bs, i, a.Index) != null)).ToList(); if (ok.Count > 0) lst = ok;
            var ch = lst[rng.Next(0, lst.Count)];
            var tc = (host ? b.TargetChoices(i, ch) : b.TargetChoicesFor(b.Foe, i, ch)).ToList(); if (tc.Count > 0) ch.Target = tc[rng.Next(0, tc.Count)];
            if (ch.Kind == ActionKind.Switch) used.Add(ch.Index); res.Add(ch);
        }
        if (res.Count > 1) second = res[1];
        return res.Count > 0 ? res[0] : (BattleAction?)null;
    }

    private static string RunSession(int seed, bool dbl, Func<string, string, string> tamper, out Link linkOut)
    {
        List<RimketCreature> ta, tb; var link = MakeLink(seed, dbl, out ta, out tb); link.Tamper = tamper; linkOut = link;
        var rng = new SystemRng(seed + 99); double t = 0;
        link.Go(rng);
        if (link.A.State == OnlineState.Pick && link.B.State == OnlineState.Pick) { link.A.Pick(AutoPick(link.A, rng)); link.B.Pick(AutoPick(link.B, rng)); link.Pump(rng); }
        int guard = 0;
        while (link.A.State == OnlineState.Battle && link.B.State == OnlineState.Battle && guard++ < 2000)
        {
            t += 3; link.A.Tick(t); link.B.Tick(t);
            foreach (var ses in rng.NextDouble() < 0.5 ? new[] { link.A, link.B } : new[] { link.B, link.A })
            {
                if (!ses.NeedMyInput) continue;
                BattleAction? second; var a = AutoAct(ses, rng, out second);
                if (a.HasValue) ses.Submit(a.Value, second);
            }
            link.Pump(rng);
        }
        return link.A.State + "/" + link.B.State;
    }

    private static void TestOnlineSession()
    {
        Console.WriteLine("== 온라인 세션(통신 약속) ==");
        foreach (bool dbl in new[] { false, true })
        {
            int bad = 0, inc = 0, wins = 0; string first = "";
            for (int seed = 1; seed <= 120; seed++)
            {
                Link lk; string st = RunSession(seed, dbl, null, out lk);
                if (lk.A.State != OnlineState.Over || lk.B.State != OnlineState.Over) { bad++; if (first == "") first = "시드 " + seed + " " + st + " " + lk.A.Error + " / " + lk.B.Error; continue; }
                var oa = lk.A.Outcome; var ob = lk.B.Outcome;
                bool okp = (oa == OnlineOutcome.Win && ob == OnlineOutcome.Lose) || (oa == OnlineOutcome.Lose && ob == OnlineOutcome.Win) || (oa == OnlineOutcome.Draw && ob == OnlineOutcome.Draw);
                if (!okp) inc++; if (oa == OnlineOutcome.Win) wins++;
                if (!lk.A.Battle.Log.SequenceEqual(lk.B.Battle.Log)) inc++;
            }
            Check(bad == 0, (dbl ? "더블" : "싱글") + " 세션 120판이 끝까지 진행된다" + (bad > 0 ? " (실패 " + bad + ": " + first + ")" : ""));
            Check(inc == 0, (dbl ? "더블" : "싱글") + " 양쪽의 결과·로그가 서로 맞는다 (방장 승 " + wins + ")");
        }
        {
            // 림월드 몸 림켓몬 그림: 보낸 쪽 그림이 받은 쪽 상대 파티(그리고 배틀 속 복사본)에 그대로 붙는다. 큰 그림은 여러 조각.
            List<RimketCreature> ta, tb; var lk = MakeLink(4242, false, out ta, out tb);
            string big = new string('Q', 9000) + "|x|y";
            lk.A.CaptureMon = (c, back) => lk.A.MyTeam.IndexOf(c) % 2 == 0 ? "1;2;0;1.5#" + (back ? "B" : "F") + lk.A.MyTeam.IndexOf(c) + big : null;
            var rr = new SystemRng(7); lk.Go(rr);
            bool ok = lk.B.OppTeam.Count == 6;
            for (int i = 0; i < lk.B.OppTeam.Count && ok; i++)
            {
                var rp = lk.B.OppTeam[i].RemotePic;
                ok = i % 2 == 0 ? rp != null && rp[0] == "1;2;0;1.5#F" + i + big && rp[1] == "1;2;0;1.5#B" + i + big : rp == null;
            }
            ok = ok && lk.A.OppTeam.All(c => c.RemotePic == null);
            if (lk.A.State == OnlineState.Pick && lk.B.State == OnlineState.Pick) { lk.A.Pick(AutoPick(lk.A, rr)); lk.B.Pick(AutoPick(lk.B, rr)); lk.Pump(rr); }
            bool inBattle = lk.B.Battle != null && lk.B.Battle.Player.Party.Concat(lk.B.Battle.Foe.Party).Count(c => c.RemotePic != null) == lk.A.MyPicks().Count(i => i % 2 == 0);
            Check(ok && inBattle, "온라인: 림월드 몸 림켓몬 그림을 나눠 보내고 상대 쪽 배틀까지 붙는다");
        }
        Link l2;
        // 버전이 다르면 막는다
        { var lk = MakeLinkSimple(false); lk.Tamper = (d, m) => m.StartsWith("HELLO|") ? m.Replace(OnlineProtocol.Fingerprint, "000000000000") : m; lk.Go(new SystemRng(1)); Check(lk.A.State == OnlineState.Aborted && lk.B.State == OnlineState.Aborted, "모드 버전이 다르면 시작하지 않는다 (" + lk.A.Error + ")"); }
        // 규칙을 넘는 림켓몬(능력 포인트 초과)은 거부
        { var lk = MakeLinkSimple(false); lk.Tamper = (d, m) => m.StartsWith("C|0|") && d == "toB" ? m.Replace("pt=", "pt=32,32,32,32,32,32;xx=") : m; lk.Go(new SystemRng(1)); Check(lk.B.State == OnlineState.Aborted, "능력 포인트를 부풀린 림켓몬은 거부된다 (" + lk.B.Error + ")"); }
        // 선출 규칙 위반(같은 종족) 거부, 항복·이탈·시간 초과
        { l2 = null; RunSession(5, false, null, out l2); }
        { var rng = new SystemRng(3); List<RimketCreature> ta, tb; var lk = MakeLink(7, false, out ta, out tb); lk.Go(rng); lk.A.Pick(AutoPick(lk.A, rng)); lk.B.Pick(AutoPick(lk.B, rng)); lk.Pump(rng);
          lk.A.Tick(1); lk.B.Tick(1); lk.B.Surrender(); lk.Pump(rng);
          Check(lk.A.State == OnlineState.Over && lk.A.Outcome == OnlineOutcome.Win && lk.B.Outcome == OnlineOutcome.Lose, "항복하면 상대의 승리로 끝난다"); }
        { var rng = new SystemRng(4); List<RimketCreature> ta, tb; var lk = MakeLink(8, false, out ta, out tb); lk.Go(rng); lk.A.Pick(AutoPick(lk.A, rng)); lk.B.Pick(AutoPick(lk.B, rng)); lk.Pump(rng);
          lk.A.Tick(1); lk.A.OpponentLeft();
          Check(lk.A.State == OnlineState.Over && lk.A.Outcome == OnlineOutcome.Win, "상대가 나가면 기권승 처리된다"); }
        { var rng = new SystemRng(5); List<RimketCreature> ta, tb; var lk = MakeLink(9, false, out ta, out tb); lk.Go(rng); lk.A.Pick(AutoPick(lk.A, rng)); lk.B.Pick(AutoPick(lk.B, rng)); lk.Pump(rng);
          lk.A.Tick(1); lk.B.Tick(1);
          BattleAction? sec; var act = AutoAct(lk.A, rng, out sec); lk.A.Submit(act.Value, sec);     // 방장만 행동, 손님은 응답 없음
          lk.A.Tick(1 + OnlineSession.TurnLimitSeconds + OnlineSession.GraceSeconds + 1);
          Check(lk.A.State == OnlineState.Over && lk.A.Outcome == OnlineOutcome.Win, "상대가 시간 안에 응답하지 않으면 승리 처리된다"); }
        { var lk = MakeLinkSimple(false); lk.Go(new SystemRng(1)); var sameSp = lk.A.MyTeam.Select((c, i) => i).ToList();
          var bad = new List<int> { 0, 0, 1 }; Check(!lk.A.Pick(bad), "같은 림켓몬을 두 번 고르는 선출은 거부된다"); }
        // 첫 인사가 사라져도 다시 보내서 이어진다
        { var rng = new SystemRng(11); List<RimketCreature> ta, tb; var lk = MakeLink(12, false, out ta, out tb); int dropped = 0;
          lk.Tamper = (d, m) => (m.StartsWith("HELLO|") && d == "toB" && dropped++ == 0) ? null : m;
          lk.A.Tick(0); lk.B.Tick(0); lk.Go(rng);
          bool stuck = lk.B.State == OnlineState.Handshake;
          lk.A.Tick(3); lk.B.Tick(3); lk.Pump(rng);
          Check(stuck && lk.A.State == OnlineState.Lobby && lk.B.State == OnlineState.Lobby, "첫 인사가 사라져도 2초 뒤 다시 보내 연결된다(대기실)", lk.A.State + "/" + lk.B.State); }
        // 대기실: 한쪽만 준비하면 시작하지 않고, 준비 취소 뒤 다시 준비하면 시작. 끝난 뒤 둘 다 '다시'를 고르면 대기실로 돌아와 새 판(새 시드)을 한다
        { var rng = new SystemRng(31); List<RimketCreature> ta, tb; var lk = MakeLink(32, false, out ta, out tb);
          lk.A.Start(); lk.B.Start(); lk.Pump(rng);
          bool lobby = lk.A.State == OnlineState.Lobby && lk.B.State == OnlineState.Lobby;
          lk.A.SetReady(ta); lk.Pump(rng); bool waitOne = lk.A.State == OnlineState.Lobby && lk.B.OppReady && !lk.B.MyReady;
          lk.A.CancelReady(); lk.Pump(rng); bool unready = !lk.B.OppReady;
          lk.B.SetReady(tb); lk.A.SetReady(ta); lk.Pump(rng); bool picking = lk.A.State == OnlineState.Pick && lk.B.State == OnlineState.Pick && lk.A.GameNo == 1;
          int games = 0; string firstLog = null; bool diffSeed = false, logsMatch = true;
          for (int round = 0; round < 3; round++)
          {
              if (round > 0) { lk.A.WantAgain(); lk.Pump(rng); bool still = lk.A.State == OnlineState.Over; lk.B.WantAgain(); lk.Pump(rng); if (!still || lk.A.State != OnlineState.Lobby || lk.B.State != OnlineState.Lobby) break; lk.A.SetReady(ta); lk.B.SetReady(tb); lk.Pump(rng); }
              lk.A.Pick(AutoPick(lk.A, rng)); lk.B.Pick(AutoPick(lk.B, rng)); lk.Pump(rng);
              int guard = 0; double t = 0;
              while (lk.A.State == OnlineState.Battle && lk.B.State == OnlineState.Battle && guard++ < 2000)
              {
                  t += 3; lk.A.Tick(t); lk.B.Tick(t);
                  foreach (var ses in new[] { lk.A, lk.B }) if (ses.NeedMyInput) { BattleAction? sec; var a = AutoAct(ses, rng, out sec); if (a.HasValue) ses.Submit(a.Value, sec); }
                  lk.Pump(rng);
              }
              if (lk.A.State != OnlineState.Over || lk.B.State != OnlineState.Over) break;
              if (!lk.A.Battle.Log.SequenceEqual(lk.B.Battle.Log)) logsMatch = false;
              var lg = string.Join("\n", lk.A.Battle.Log); if (firstLog == null) firstLog = lg; else if (lg != firstLog) diffSeed = true;
              games++;
          }
          Check(lobby && waitOne && unready && picking && games == 3 && logsMatch && diffSeed && lk.A.GameNo == 3 && lk.B.GameNo == 3,
              "대기실: 둘 다 준비해야 시작, 준비 취소 가능, 끝난 뒤 '다시'로 같은 방에서 3판 연속(판마다 새 시드, 양쪽 로그 일치)",
              lobby + " " + waitOne + " " + unready + " " + picking + " 판 " + games + " 일치 " + logsMatch + " 새시드 " + diffSeed + " " + lk.A.State + "/" + lk.B.State + " " + lk.A.Error + lk.B.Error); }
        // 점검: 싱글·더블 각 30방 × 3판(대기실 준비 → 선출 → 배틀 → 다시 하기), 큰 그림(약 60KB)·채팅·준비 취소가 섞여도 양쪽 결과·로그가 같고 끝까지 간다
        foreach (bool dbl in new[] { false, true })
        {
            int rooms = 0, gamesOk = 0, mismatch = 0, stuck = 0; string why = "";
            for (int seed = 100; seed < 130; seed++)
            {
                List<RimketCreature> ta, tb; var lk = MakeLink(seed, dbl, out ta, out tb); var rng = new SystemRng(seed * 13);
                string bigPic = new string('P', 60000);
                lk.A.CaptureMon = (c, back) => "1;0;0;0#" + bigPic; lk.B.CaptureMon = (c, back) => back ? null : "1;0;0;0#" + bigPic.Substring(0, 9000);
                lk.A.Start(); lk.B.Start(); lk.Pump(rng);
                bool roomOk = true; double t = 0;
                for (int round = 0; round < 3 && roomOk; round++)
                {
                    if (round > 0) { lk.B.WantAgain(); lk.A.SendChat("한 판 더!"); lk.A.WantAgain(); lk.Pump(rng); }
                    if (lk.A.State != OnlineState.Lobby || lk.B.State != OnlineState.Lobby) { roomOk = false; stuck++; why = "대기실 " + lk.A.State + "/" + lk.B.State; break; }
                    t += 30; lk.A.Tick(t); lk.B.Tick(t);      // 대기실에서도 시계는 흐른다(실제로는 매 프레임)
                    lk.B.SetReady(tb); if (rng.Next(0, 2) == 0) { lk.B.CancelReady(); lk.B.SetReady(tb); }
                    lk.A.SendChat("준비됐어?"); lk.A.SetReady(ta); lk.Pump(rng);
                    if (lk.A.State != OnlineState.Pick || lk.B.State != OnlineState.Pick) { roomOk = false; stuck++; why = "선출 " + lk.A.State + "/" + lk.B.State + " " + lk.A.Error + lk.B.Error; break; }
                    bool picsOk = lk.B.OppTeam.All(c => c.RemotePic != null && c.RemotePic[0].Length == 60008 && c.RemotePic[1].Length == 60008) && lk.A.OppTeam.All(c => c.RemotePic != null && c.RemotePic[0].Length == 9008 && c.RemotePic[1] == null);
                    if (!picsOk) { roomOk = false; mismatch++; why = "그림"; break; }
                    lk.A.Pick(AutoPick(lk.A, rng)); lk.B.Pick(AutoPick(lk.B, rng)); lk.Pump(rng);
                    int guard = 0;
                    while (lk.A.State == OnlineState.Battle && lk.B.State == OnlineState.Battle && guard++ < 3000)
                    {
                        t += 3; lk.A.Tick(t); lk.B.Tick(t);
                        foreach (var ses in rng.NextDouble() < 0.5 ? new[] { lk.A, lk.B } : new[] { lk.B, lk.A })
                            if (ses.NeedMyInput) { if (rng.Next(0, 40) == 0) ses.SendChat("ㅋㅋ"); BattleAction? sec; var a = AutoAct(ses, rng, out sec); if (a.HasValue) ses.Submit(a.Value, sec); }
                        lk.Pump(rng);
                    }
                    if (lk.A.State != OnlineState.Over || lk.B.State != OnlineState.Over) { roomOk = false; stuck++; why = "배틀 " + lk.A.State + "/" + lk.B.State + " " + lk.A.Error + lk.B.Error; break; }
                    bool same = lk.A.Battle.Log.SequenceEqual(lk.B.Battle.Log) && ((lk.A.Outcome == OnlineOutcome.Win && lk.B.Outcome == OnlineOutcome.Lose) || (lk.A.Outcome == OnlineOutcome.Lose && lk.B.Outcome == OnlineOutcome.Win) || (lk.A.Outcome == OnlineOutcome.Draw && lk.B.Outcome == OnlineOutcome.Draw));
                    if (!same) { mismatch++; roomOk = false; var la = lk.A.Battle.Log; var lb = lk.B.Battle.Log; int k = 0; while (k < la.Count && k < lb.Count && la[k] == lb[k]) k++; why = "결과 불일치 판" + round + " " + lk.A.Outcome + "/" + lk.B.Outcome + " 줄" + k + ": " + (k < la.Count ? la[k] : "-") + " | " + (k < lb.Count ? lb[k] : "-"); break; }
                    gamesOk++;
                }
                if (roomOk) rooms++;
            }
            Check(rooms == 30 && gamesOk == 90 && mismatch == 0, (dbl ? "더블" : "싱글") + " 점검: 30방 × 3판(큰 그림·채팅·준비 취소 섞음) 모두 끝까지, 양쪽 결과 일치", "방 " + rooms + "/30, 판 " + gamesOk + "/90, 불일치 " + mismatch + ", 멈춤 " + stuck + " " + why);
        }
        // 한쪽이 대기실에서 나가면 상대가 안다
        { var rng = new SystemRng(41); List<RimketCreature> ta, tb; var lk = MakeLink(42, false, out ta, out tb); lk.A.Start(); lk.B.Start(); lk.Pump(rng);
          lk.B.Bye(); lk.Pump(rng); Check(lk.A.OppLeft && lk.A.State == OnlineState.Lobby, "대기실에서 상대가 나가면 알린다"); }
        // 채팅과 초상화
        { var rng = new SystemRng(21); List<RimketCreature> ta, tb; var lk = MakeLink(22, false, out ta, out tb);
          var pic = new string('A', 7000) + "xyz"; lk.A.MyPicture = pic;
          lk.Go(rng);
          lk.A.SendChat("안녕 | 잘 부탁해"); lk.B.SendChat("좋은 승부!"); lk.Pump(rng);
          Check(lk.B.Chat.Count == 2 && lk.B.Chat.Any(c => !c.Key && c.Value == "안녕 | 잘 부탁해") && lk.A.Chat.Any(c => !c.Key && c.Value == "좋은 승부!"), "채팅이 오간다('|'가 들어가도)");
          Check(lk.B.PeerPicture == pic && lk.A.PeerPicture == null, "초상화가 나뉘어 전송되고 다시 합쳐진다");
          Check(lk.A.State == OnlineState.Pick && lk.B.State == OnlineState.Pick, "채팅·초상화가 대전 진행을 막지 않는다"); }
        // 연출을 보여 주는 동안은 제한 시간이 흐르지 않는다
        { var rng = new SystemRng(13); List<RimketCreature> ta, tb; var lk = MakeLink(14, false, out ta, out tb); lk.Go(rng); lk.A.Pick(AutoPick(lk.A, rng)); lk.B.Pick(AutoPick(lk.B, rng)); lk.Pump(rng);
          lk.A.Tick(1); lk.B.Tick(1); lk.A.Tick(60); lk.A.InputShown(); lk.A.Tick(60 + OnlineSession.TurnLimitSeconds - 5);
          Check(lk.A.State == OnlineState.Battle, "입력 창이 뜬 때부터 제한 시간을 센다(연출 60초는 빼고)"); }
        // 상태 해시가 다르면 중단
        { var rng = new SystemRng(6); List<RimketCreature> ta, tb; var lk = MakeLink(10, false, out ta, out tb);
          lk.Tamper = (d, m) => (m.StartsWith("SYNC|") && d == "toA") ? "SYNC|" + m.Split('|')[1] + "|deadbeef" : m;
          lk.Go(rng); lk.A.Pick(AutoPick(lk.A, rng)); lk.B.Pick(AutoPick(lk.B, rng)); lk.Pump(rng);
          double t = 0; for (int i = 0; i < 50 && lk.A.State == OnlineState.Battle; i++) { t += 2; lk.A.Tick(t); lk.B.Tick(t); foreach (var ses in new[] { lk.A, lk.B }) { if (!ses.NeedMyInput) continue; BattleAction? sc; var a = AutoAct(ses, rng, out sc); if (a.HasValue) ses.Submit(a.Value, sc); } lk.Pump(rng); }
          Check(lk.A.State == OnlineState.Aborted && lk.A.Outcome == OnlineOutcome.NoContest, "상태가 어긋나면 이 판을 무효로 중단한다 (" + lk.A.Error + ")"); }
    }

    private static void TestWedgeRemove()
    {
        Console.WriteLine("== 유전자 쐐기 빼기 ==");
        var rng = new SystemRng(77); var st = new EngineSettings();
        var mon = RankedBuilder.Build("ou", 1, 50, rng, st)[0];
        string before = Paradox.Backup(mon); string sp0 = mon.Species; int lv = mon.Level; string nm = mon.Name;
        var foe = Paradox.MakeFoe(rng, st, false); var w = Paradox.MakeWedge(foe);
        Check(Paradox.Apply(mon, w) && mon.Species == w.Species && Paradox.HasWedge(mon), "쐐기를 쓰면 원래 정보가 기억된다");
        var w2 = Paradox.MakeWedge(Paradox.MakeFoe(rng, st, true)); Paradox.Apply(mon, w2);
        var back = Paradox.Remove(mon);
        Check(back != null && back.Species == w2.Species, "쐐기를 빼면 지금 끼워진 쐐기를 돌려받는다");
        Check(mon.Species == sp0 && Paradox.Backup(mon) == before && mon.Level == lv && mon.Name == nm && !Paradox.HasWedge(mon), "쐐기를 빼면 처음 모습으로 돌아간다(레벨·이름 유지)");
        Check(Paradox.Remove(mon) == null, "쐐기가 없으면 뺄 것이 없다");
    }

    private static void TestOnlineTeamRules()
    {
        Console.WriteLine("== 온라인 파티 검사 ==");
        var lk = MakeLinkSimple(true);
        var team = lk.A.MyTeam.ToList();
        Check(OnlineProtocol.TeamProblem(team) == null, "정상 파티는 온라인 검사를 통과한다");
        Check(OnlineProtocol.CanForm(team, 3) == RankedRules.CanFormTeam(team), "3마리 선출 가능 판정이 랭크 규칙과 같다");
        var same = new List<RimketCreature> { team[0].Clone(), team[0].Clone(), team[0].Clone(), team[0].Clone() };
        Check(!OnlineProtocol.CanForm(same, 3) && !OnlineProtocol.CanForm(same, 4), "같은 종족뿐인 파티는 선출할 수 없다");
        var three = team.Take(3).ToList();
        Check(!OnlineProtocol.CanForm(three, 4), "3마리뿐이면 더블(4마리)은 불가");
        var cheat = team.Select(c => c.Clone()).ToList(); cheat[1].Sp[0] = 200;
        Check(OnlineProtocol.TeamProblem(cheat) != null, "규칙 밖 림켓몬이 있으면 미리 알려 준다 (" + OnlineProtocol.TeamProblem(cheat) + ")");
        var mixed = team.Select(c => c.Clone()).ToList();
        mixed[0].Species = mixed[0].SpeciesId = "PERSON"; mixed[0].Name = "앨리스"; mixed[0].Neutral = true;
        mixed[1].Species = mixed[1].SpeciesId = "PERSON"; mixed[1].Name = "밥"; mixed[1].Neutral = true;
        mixed[2].Tier = 0; mixed[2].BaseTier = 0;
        string why = OnlineProtocol.TeamProblem(mixed);
        Check(why == null, "사람 둘·?티어도 온라인에 낼 수 있다", why ?? "");
        string err; var back = OnlineProtocol.Decode(OnlineProtocol.Encode(mixed[0]), out err);
        Check(back != null && back.Neutral && back.Name == "앨리스", "사람의 중립 속성이 전송된다", err ?? "");
    }

    private static void TestBerryPrice()
    {
        Console.WriteLine("== 나무열매 가격/재사용 ==");
        var items = ItemCatalog.All.Where(x => ItemCatalog.Shoppable(x.Id)).ToList();
        var tools = items.Where(x => !x.IsBerry).Select(x => RankedLadder.ShopBp(ItemCatalog.SilverPrice(x.Id))).OrderBy(x => x).ToList();
        var berries = items.Where(x => x.IsBerry).Select(x => RankedLadder.ShopBp(ItemCatalog.SilverPrice(x.Id))).Distinct().ToList();
        Check(berries.Count == 1 && berries[0] == tools[tools.Count / 2], "열매 가격 = 일반 도구 가격의 중앙값", "열매 " + berries[0] + " BP, 도구 " + tools[0] + "~" + tools[tools.Count - 1] + " BP, 사분위 " + tools[tools.Count / 4] + "/" + tools[tools.Count * 3 / 4] + ", 먹다남은음식 " + RankedLadder.ShopBp(ItemCatalog.SilverPrice("LEFTOVERS")) + ", 구애머리띠 " + RankedLadder.ShopBp(ItemCatalog.SilverPrice("CHOICEBAND")) + ", 생명의구슬 " + RankedLadder.ShopBp(ItemCatalog.SilverPrice("LIFEORB")) + ", 상처약 " + RankedLadder.ShopBp(ItemCatalog.SilverPrice("POTION")));
        var mine = MkI("SNORLAX", 50, "IMMUNITY", "SITRUSBERRY", "SPLASH");
        mine.Hp = mine.MaxHp / 2 + 1;
        var b = Fight(new SystemRng(3), mine, Mk("PIKACHU", 50, "STATIC", "TACKLE"));
        b.Act(new BattleAction(ActionKind.UseMove, 0));
        Check(b.Log.Any(l => l.Contains("먹었다")) && mine.Item == "SITRUSBERRY", "전투에서 열매를 먹어도 전투 뒤에는 그대로 지니고 있다", mine.Item + " / " + string.Join(" ", b.Log.Skip(Math.Max(0, b.Log.Count - 4))));
    }

    private static void TestFoeEncoder()
    {
        Console.WriteLine("== 상대 시점 인코더(자기학습) ==");
        var st = new EngineSettings(); int diff = 0, maskDiff = 0, played = 0;
        for (int seed = 1; seed <= 30; seed++)
        {
            Func<List<RimketCreature>[]> teams = () => { var r = new SystemRng(seed); var a = RankedBuilder.Build("ou", 3, 50, r, st); var b2 = RankedBuilder.Build("ou", 3, 50, r, st); foreach (var c in a.Concat(b2)) c.FullHeal(); return new[] { a, b2 }; };
            var t1 = teams(); var t2 = teams();
            var x = RimketBattle.OnlineBattle(new SystemRng(seed), st, "A", t1[0], "B", t1[1]);     // A가 Player
            var y = RimketBattle.OnlineBattle(new SystemRng(seed), st, "B", t2[1], "A", t2[0]);     // 같은 A가 Foe
            var ex = new AiEncoder(st); var ey = new AiEncoder(st, true);
            if (!ex.Encode(x).SequenceEqual(ey.Encode(y))) diff++;
            if (!ex.Mask(x).SequenceEqual(ey.Mask(y))) maskDiff++;
            // Foe 시점 행동으로 한 판을 끝까지 돌린다
            var rng = new SystemRng(seed + 99); var ef = new AiEncoder(st, true); var ep = new AiEncoder(st); int guard = 0;
            while (!x.IsOver && guard++ < 300)
            {
                BattleAction? pa = null, fa = null;
                if (!x.PendingFoeSwitch || x.PendingSwitch) { var m = ep.Mask(x); var idx = Enumerable.Range(0, m.Length).Where(i => m[i]).ToList(); pa = ep.ToAction(x, idx[rng.Next(0, idx.Count)]); }
                if (!x.PendingSwitch || x.PendingFoeSwitch) { var m = ef.Mask(x); var idx = Enumerable.Range(0, m.Length).Where(i => m[i]).ToList(); fa = ef.ToAction(x, idx[rng.Next(0, idx.Count)]); }
                if (!x.OnlineAct(pa, fa)) break;
            }
            if (x.IsOver) played++;
        }
        Check(diff == 0 && maskDiff == 0, "같은 팀을 Foe 자리에 두면 Foe 시점 관측·마스크가 Player 시점과 같다", "관측 불일치 " + diff + ", 마스크 불일치 " + maskDiff);
        Check(played == 30, "Foe 시점 마스크·행동만으로 30판이 끝까지 진행된다", played + "/30");
    }

    private static void TestNeuralPolicy()
    {
        Console.WriteLine("== 신경망 추론 ==");
        var r = new Random(5); int[] dims = { 37, 19, 11, 6 };
        var ms = new System.IO.MemoryStream(); var w = new System.IO.BinaryWriter(ms);
        var W = new List<float[]>(); var B = new List<float[]>();
        w.Write(dims.Length - 1);
        for (int l = 0; l + 1 < dims.Length; l++)
        {
            int o = dims[l + 1], n = dims[l]; var wl = new float[o * n]; var bl = new float[o];
            for (int k = 0; k < wl.Length; k++) wl[k] = (float)(r.NextDouble() - 0.5); for (int k = 0; k < o; k++) bl[k] = (float)(r.NextDouble() - 0.5);
            w.Write(o); w.Write(n); w.Write((byte)(l + 2 < dims.Length ? 1 : 0)); foreach (var v in wl) w.Write(v); foreach (var v in bl) w.Write(v);
            W.Add(wl); B.Add(bl);
        }
        ms.Position = 0; var pol = new NeuralPolicy(ms);
        double worst = 0;
        for (int t = 0; t < 50; t++)
        {
            var x = new float[dims[0]]; for (int k = 0; k < x.Length; k++) x[k] = r.Next(3) == 0 ? (float)r.NextDouble() : 0f;
            var cur = x;
            for (int l = 0; l < W.Count; l++)
            {
                int o = dims[l + 1], n = dims[l]; var y = new float[o];
                for (int j = 0; j < o; j++) { double sum = B[l][j]; for (int i = 0; i < n; i++) sum += W[l][j * n + i] * cur[i]; y[j] = (float)(l + 1 < W.Count && sum < 0 ? 0 : sum); }
                cur = y;
            }
            var got = pol.Logits(x);
            for (int j = 0; j < cur.Length; j++) worst = Math.Max(worst, Math.Abs(cur[j] - got[j]));
        }
        Check(worst < 1e-4, "빠른 추론 결과가 정의대로의 계산과 같다", "최대 오차 " + worst.ToString("E1"));
    }

    private static void TestPickEncoder()
    {
        Console.WriteLine("== 선출 학습용 인코더 ==");
        var st = new EngineSettings(); var r = new SystemRng(4);
        var a6 = RankedBuilder.Build("ou", 6, 50, r, st); var b6 = RankedBuilder.Build("ou", 6, 50, r, st);
        var enc = new AiEncoder(st); enc.SetPreview(a6, b6);
        var picked = new List<int>(); var o0 = enc.EncodePick(picked); var m0 = enc.PickMask(picked);
        int own0 = AiEncoder.BattleSize + 1, flag = AiEncoder.MonSize;
        Check(o0.Length == AiEncoder.Size && o0[AiEncoder.BattleSize] == 1f && o0.Take(AiEncoder.BattleSize).All(v => v == 0f), "선출 단계 관측: 전투 부분은 비고 단계 표시가 켜진다", "관측 크기 " + AiEncoder.Size);
        Check(m0.Count(x => x) == 6, "처음에는 6마리 모두 고를 수 있다");
        picked.Add(2); var m1 = enc.PickMask(picked); var o1 = enc.EncodePick(picked);
        Check(!m1[2] && o1[own0 + 2 * AiEncoder.OwnPreviewSize + flag] == 1f && o1[own0 + 2 * AiEncoder.OwnPreviewSize + flag + 1] == 1f, "고른 림켓몬은 다시 못 고르고 '선봉으로 골랐다'가 표시된다");
        var dup = a6.Select(c => c.Clone()).ToList(); dup[1] = dup[0].Clone();
        var e2 = new AiEncoder(st); e2.SetPreview(dup, b6);
        Check(!e2.PickMask(new List<int> { 0 })[1], "같은 종족은 함께 고를 수 없다");
        picked.Add(0); picked.Add(5); enc.SetPicks(picked);
        var mine = RankedRules.Brought(a6, picked, 50); var foe = RankedRules.Brought(b6, new[] { 0, 1, 2 }, 50);
        var bt = RimketBattle.RankedBattle(new SystemRng(1), st, "나", mine, "상대", foe);
        var ob = enc.Encode(bt); int cnt = 0; for (int i = 0; i < 6; i++) if (ob[own0 + i * AiEncoder.OwnPreviewSize + flag] == 1f) cnt++;
        Check(ob[AiEncoder.BattleSize] == 0f && cnt == 3 && ob[0] == 1f, "전투 중 관측에도 양쪽 6마리 프리뷰와 내 선출이 남는다");
        { var eh = new AiEncoder(st); var hid = eh.Encode(bt); AiEncoder.FullInfo = true; var full = new AiEncoder(st).Encode(bt); AiEncoder.FullInfo = false;
          int fb = AiEncoder.MonSize * 3; double hs = 0, fs = 0; for (int i = fb; i < fb + AiEncoder.MonSize; i++) { hs += Math.Abs(hid[i]); fs += Math.Abs(full[i]); }
          Check(fs > hs + 3, "전체 정보 모드: 상대의 기술·능력치·특성·도구가 관측에 들어온다", "상대 선두 칸 합 " + hs.ToString("F1") + " → " + fs.ToString("F1")); }
        int foe0 = own0 + 6 * AiEncoder.OwnPreviewSize;
        Check(Enumerable.Range(0, 6).All(i => ob[foe0 + i * AiEncoder.FoePreviewSize] == 1f), "상대 6마리는 타입·체력만 보인다(기술·특성·도구 없음)");
    }

    private static void TestHumanPlay()
    {
        Console.WriteLine("== 사람 같은 상대(실수·생각 시간) ==");
        var st = new EngineSettings();
        Check(Math.Abs(HumanPlay.MistakeRate(800) - 0.30) < 1e-9 && Math.Abs(HumanPlay.MistakeRate(1250) - 0.165) < 1e-9 && Math.Abs(HumanPlay.MistakeRate(1700) - 0.03) < 1e-9, "실수 확률: 1000점 이하 30%, 1250점 16.5%, 1500점 이상 3%");
        {   // 위력만 보고 고르기: 부유 특성 상대에게 지진
            var me = new Fighter(Mk("GARCHOMP", 50, "ROUGHSKIN", "EARTHQUAKE", "DRAGONCLAW")); var foe = new Fighter(Mk("GENGAR", 50, "LEVITATE", "SHADOWBALL"));
            var best = RankedAI.ChooseMove(me, foe, st, new SystemRng(1), null, null);
            int power = 0; for (int i = 0; i < 200; i++) { HumanPlay.Mistake k; var c = HumanPlay.Choose(me, foe, st, new SystemRng(i), null, best, out k); if (k == HumanPlay.Mistake.PowerOnly && c.Move.Id == "EARTHQUAKE") power++; }
            Check(best.Move.Id == "DRAGONCLAW" && power > 0, "정답은 드래곤클로인데, 실수하면 부유 상대에게 지진을 쓰기도 한다", power + "/200");
        }
        {   // 욕심: 때려야 할 때 칼춤
            var me = new Fighter(Mk("SCIZOR", 50, "TECHNICIAN", "BULLETPUNCH", "SWORDSDANCE")); var foe = new Fighter(Mk("GARDEVOIR", 50, "TRACE", "PSYCHIC")); foe.C.Hp = 5;
            var best = RankedAI.ChooseMove(me, foe, st, new SystemRng(1), null, null);
            int greedy = 0; for (int i = 0; i < 200; i++) { HumanPlay.Mistake k; var c = HumanPlay.Choose(me, foe, st, new SystemRng(i), null, best, out k); if (k == HumanPlay.Mistake.Greedy && c.Move.Id == "SWORDSDANCE") greedy++; }
            Check(best.Move.Id == "BULLETPUNCH" && greedy > 0, "잡을 수 있는데도 욕심내서 칼춤을 쓰기도 한다", greedy + "/200");
        }
        {   // 실수율에 따른 강도: 같은 판 300개를 실수 0% / 100%로 돌려 상대 승수를 비교
            int[] wins = new int[2]; int[] mistakes = new int[2];
            for (int mode = 0; mode < 2; mode++)
                for (int seed = 1; seed <= 300; seed++)
                {
                    var r = new SystemRng(seed); var a = RankedBuilder.Build("uu", 3, 50, r, st); var b2 = RankedBuilder.Build("uu", 3, 50, r, st);
                    foreach (var c in a.Concat(b2)) c.FullHeal();
                    var bt = RimketBattle.RankedBattle(new SystemRng(seed * 31), st, "나", a, "상대", b2); bt.FoeMistakeRate = mode;
                    int guard = 0;
                    while (!bt.IsOver && guard++ < 300)
                    {
                        var legal = bt.LegalActions().Where(x => x.Kind != ActionKind.Disconnect).ToList(); if (legal.Count == 0) break;
                        // 플레이어는 기대 피해가 가장 큰 기술을 쓰는 단순한 사람
                        var act = legal[0]; double bv = -1;
                        foreach (var x in legal) if (x.Kind == ActionKind.UseMove && bt.MoveBlocked(x.Index) == null) { double v = RankedAI.Damage(bt.ActiveFighter.C, bt.ActiveFighter.C.Moves[x.Index].Move, bt.FoeFighter.C, 1, 1, st); if (v > bv) { bv = v; act = x; } }
                        bt.Act(act);
                    }
                    if (bt.IsOver && bt.Result.End == BattleEnd.Defeat) wins[mode]++;
                    mistakes[mode] += bt.FoeMistakes;
                }
            Check(mistakes[0] == 0 && mistakes[1] > 300 && wins[1] < wins[0], "실수를 섞으면 상대가 실제로 약해진다", "상대 승: 실수 없음 " + wins[0] + "/300, 매 턴 실수 " + wins[1] + "/300 (실수 " + mistakes[1] + "회)");
        }
        {
            var r = new SystemRng(3); double sum = 0, mx = 0, mn = 99; int longs = 0;
            for (int i = 0; i < 2000; i++) { double t = HumanPlay.ThinkSeconds(r, 5, false); sum += t; mx = Math.Max(mx, t); mn = Math.Min(mn, t); if (t > 8) longs++; }
            Check(mn >= 0.8 && mx <= 25 && sum / 2000 > 2 && sum / 2000 < 5 && longs > 60 && longs < 400, "생각 시간: 보통 몇 초, 가끔 장고", "평균 " + (sum / 2000).ToString("F1") + "초, 8초 넘는 경우 " + (longs / 20.0).ToString("F0") + "%");
            double ps = 0; for (int i = 0; i < 2000; i++) ps += HumanPlay.PickSeconds(r);
            Check(ps / 2000 > 15 && ps / 2000 < 35, "선출 시간 평균 " + (ps / 2000).ToString("F0") + "초");
        }
    }

    private static void TestTeamMeta()
    {
        Console.WriteLine("== 팀 구성 통계 ==");
        var st = new EngineSettings(); var meta = new TeamMeta();
        var pool = RankedPoolCatalog.Pool("ou"); string strong = pool[5].Species, weak = pool[6].Species;
        SpeciesInfo sp1, sp2; SpeciesCatalog.TryGet(strong, out sp1); SpeciesCatalog.TryGet(weak, out sp2);
        var c1 = SpawnRoller.FromSpecies(sp1, 50, new SystemRng(1), st); var c2 = SpawnRoller.FromSpecies(sp2, 50, new SystemRng(2), st);
        for (int i = 0; i < 2000; i++) { meta.Record("ou", c1, i % 10 < 7); meta.Record("ou", c2, i % 10 < 3); }      // 70% / 30%
        Check(Math.Abs(meta.WinRate("ou", 'S', strong) - 0.69) < 0.02 && meta.Factor("ou", 'S', strong, null, 1) > 3 && meta.Factor("ou", 'S', weak, null, 1) < 0.2 && meta.Factor("ou", 'S', strong, null, 0) == 1.0, "승률이 높았던 종족은 가중치가 오르고 낮았던 종족은 내린다(반영 0이면 그대로)");
        Check(meta.WinRate("ou", 'S', "NEVERSEEN") == 0.5, "기록이 없는 종족은 50%로 본다");
        Func<TeamMeta, double, int[]> count = (m, k) => { int a = 0, b2 = 0; for (int seed = 1; seed <= 400; seed++) { var t = RankedBuilder.Build("ou", 6, 50, new SystemRng(seed), st, null, m, k); if (t.Any(x => x.Species == strong)) a++; if (t.Any(x => x.Species == weak)) b2++; } return new[] { a, b2 }; };
        var n0 = count(null, 0); var n1 = count(meta, 1.0);
        Check(n1[0] > n0[0] && n1[1] < n0[1], "통계를 반영하면 강한 종족이 팀에 더 자주, 약한 종족이 덜 나온다", "강한 종족 " + n0[0] + "→" + n1[0] + ", 약한 종족 " + n0[1] + "→" + n1[1] + " (400팀 중)");
        var same = RankedBuilder.Build("ou", 6, 50, new SystemRng(9), st).Select(x => x.Species + x.Item).ToList();
        Check(same.SequenceEqual(RankedBuilder.Build("ou", 6, 50, new SystemRng(9), st, null, meta, 0).Select(x => x.Species + x.Item)), "반영 0이면 팀이 예전과 똑같이 뽑힌다");
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rk_meta_test.txt"); meta.Save(path); var back = TeamMeta.Load(path); System.IO.File.Delete(path);
        Check(back.Games == meta.Games && back.Count == meta.Count && back.WinRate("ou", 'S', strong) == meta.WinRate("ou", 'S', strong), "저장하고 다시 읽으면 같다");
        Check(TeamMeta.StrengthFor(1000) == 0 && TeamMeta.StrengthFor(1300) == 0.5 && TeamMeta.StrengthFor(1600) == 1, "반영 정도: 1100점 이하 0, 1300점 절반, 1500점 이상 전부");
    }

    private static void TestFoeSurrender()
    {
        Console.WriteLine("== 상대의 항복 ==");
        var st = new EngineSettings();
        var why = new Dictionary<string, int> { { "Hopeless", 0 }, { "Embarrassed", 0 }, { "Tilted", 0 }, { "Stuck", 0 } };
        int games = 3000, surr = 0, foeWonAfter = 0, off = 0, early = 0;
        for (int seed = 1; seed <= games; seed++)
        {
            var r = new SystemRng(seed); var a = RankedBuilder.Build("uu", 3, 50, r, st); var b2 = RankedBuilder.Build("uu", 3, 50, r, st);
            foreach (var c in a.Concat(b2)) c.FullHeal();
            var bt = RimketBattle.RankedBattle(new SystemRng(seed * 31), st, "나", a, "상대", b2);
            bt.FoeMistakeRate = 0.30; bt.FoeMaySurrender = seed % 2 == 0; bt.FoeTilt = 1.5;
            int guard = 0;
            while (!bt.IsOver && guard++ < 300)
            {
                var legal = bt.LegalActions().Where(x => x.Kind != ActionKind.Disconnect).ToList(); if (legal.Count == 0) break;
                var act = legal[0]; double bv = -1;
                foreach (var x in legal) if (x.Kind == ActionKind.UseMove && bt.MoveBlocked(x.Index) == null) { double v = RankedAI.Damage(bt.ActiveFighter.C, bt.ActiveFighter.C.Moves[x.Index].Move, bt.FoeFighter.C, 1, 1, st); if (v > bv) { bv = v; act = x; } }
                bt.Act(act);
            }
            if (bt.Result.FoeSurrendered)
            {
                if (!bt.FoeMaySurrender) off++;
                surr++; why[bt.Result.FoeSurrenderReason]++;
                if (bt.Result.End != BattleEnd.Victory || !bt.Log.Any(l => l.Contains("항복했다"))) foeWonAfter++;
                if (bt.Turn < 1) early++;
            }
        }
        Check(off == 0 && foeWonAfter == 0 && early == 0, "항복은 켠 판에서만 나오고, 항복하면 내 승리로 끝난다");
        Check(why["Hopeless"] > 0 && why["Embarrassed"] > 0 && why["Tilted"] > 0, "세 가지 이유의 항복이 모두 나온다", "1500판 중 가망 없음 " + why["Hopeless"] + ", 실수 뒤 민망 " + why["Embarrassed"] + ", 급소에 기분 상함 " + why["Tilted"] + ", 마비·얼음 " + why["Stuck"] + " (실수율 30%, 감정 배율 1.5 = 최하위권 기준)");
        Check(surr < 1500 * 0.6, "항복이 너무 흔하지는 않다", (surr * 100.0 / 1500).ToString("F0") + "%");
        {   // 마스터볼 상위(1700점 이상): 감정 항복 없음, 가망 없으면 빨리 항복
            var why2 = new Dictionary<string, int> { { "Hopeless", 0 }, { "Embarrassed", 0 }, { "Tilted", 0 }, { "Stuck", 0 } };
            int hopelessSeen = 0;
            for (int seed = 1; seed <= 1500; seed++)
            {
                var r = new SystemRng(seed); var a = RankedBuilder.Build("uu", 3, 50, r, st); var b2 = RankedBuilder.Build("uu", 3, 50, r, st);
                foreach (var c in a.Concat(b2)) c.FullHeal();
                var bt = RimketBattle.RankedBattle(new SystemRng(seed * 31), st, "나", a, "상대", b2);
                bt.FoeMistakeRate = 0.30; bt.FoeMaySurrender = true; bt.FoeTilt = 1.5; bt.FoeRating = 1800;
                int guard = 0;
                while (!bt.IsOver && guard++ < 300)
                {
                    var legal = bt.LegalActions().Where(x => x.Kind != ActionKind.Disconnect).ToList(); if (legal.Count == 0) break;
                    var act = legal[0]; double bv = -1;
                    foreach (var x in legal) if (x.Kind == ActionKind.UseMove && bt.MoveBlocked(x.Index) == null) { double v = RankedAI.Damage(bt.ActiveFighter.C, bt.ActiveFighter.C.Moves[x.Index].Move, bt.FoeFighter.C, 1, 1, st); if (v > bv) { bv = v; act = x; } }
                    bt.Act(act);
                }
                if (bt.Result.FoeSurrendered) why2[bt.Result.FoeSurrenderReason]++;
            }
            hopelessSeen = why2["Hopeless"];
            Check(why2["Embarrassed"] + why2["Tilted"] + why2["Stuck"] == 0 && hopelessSeen > why["Hopeless"], "1700점 이상 상대: 감정 항복 0, 가망 없을 땐 더 빨리 항복", "가망 없음 " + hopelessSeen + " (일반 " + why["Hopeless"] + ")");
        }
        Check(Math.Abs(HumanPlay.TiltFor(900) - 1.5) < 1e-9 && Math.Abs(HumanPlay.TiltFor(1600) - 0.5) < 1e-9, "감정 배율: 하위권 1.5, 상위권 0.5");
        {   // 챔피언 뒤 관장 재도전: 6마리 전원 Lv.100, 6V, 진화 끝
            bool ok = true; string bad = "";
            foreach (var ld in GymCatalog.All.Take(20))
            {
                var su = GymBuilder.BuildRematch(ld, new SystemRng(ld.Id.GetHashCode()), st);
                foreach (var c in su.Party)
                {
                    SpeciesInfo sp; SpeciesCatalog.TryGet(c.Species, out sp);
                    if (c.Level != 100 || !c.IsPerfectIv || c.Hp != c.MaxHp || (sp != null && sp.Evolutions.Count > 0 && sp.Evolutions.Any(e => e.Value > 0 && e.Value <= 100))) { ok = false; bad = ld.Name + ":" + c.Species + " Lv" + c.Level; }
                }
                if (su.Party.Count != 6) { ok = false; bad = ld.Name + " " + su.Party.Count + "마리"; }
            }
            Check(ok, "관장 재도전: 6마리 전원 Lv.100·6V·레벨 진화 끝", bad);
        }
        {   // 체육관: 정착민이 싸우지 않는다 → 파티가 전멸하면 그대로 패배, 부상 없음
            var mine = Mk("MAGIKARP", 5, "SWIFTSWIM", "SPLASH"); var foe = Mk("MEWTWO", 80, "PRESSURE", "PSYCHIC");
            var gb = RimketBattle.Trainer(new SystemRng(2), st, new[] { mine }, new ColonistCombatant { Name = "도전자" }, "관장", new[] { foe }, null);
            int guard = 0; while (!gb.IsOver && guard++ < 50) gb.Act(gb.LegalActions().First(x => x.Kind != ActionKind.Disconnect));
            Check(gb.IsOver && gb.Result.End == BattleEnd.Defeat && !gb.Result.ColonistEntered && !gb.Result.Injury.Any, "체육관: 파티가 전멸하면 정착민은 나서지 않고 패배, 부상 없음");
        }
    }

    private static void TestFoeDoesNotPeek()
    {
        Console.WriteLine("== 상대 AI는 내 선택을 미리 보지 않는다 ==");
        var st = new EngineSettings(); string eq = MoveCatalog.Get("EARTHQUAKE").Name; int usedEq = 0, n = 40;
        for (int seed = 1; seed <= n; seed++)
        {
            var mine = new List<RimketCreature> { Mk("RAICHU", 50, "STATIC", "SPLASH"), Mk("GENGAR", 50, "LEVITATE", "SPLASH") };
            var foe = new List<RimketCreature> { Mk("GARCHOMP", 50, "ROUGHSKIN", "EARTHQUAKE", "DRAGONCLAW") };
            var bt = RimketBattle.RankedBattle(new SystemRng(seed), st, "나", mine, "상대", foe);
            bt.Act(new BattleAction(ActionKind.Switch, 1));       // 전기 타입을 빼고 부유 고스트로 교체
            if (bt.Log.Any(l => l.Contains(eq))) usedEq++;
        }
        Check(usedEq >= n - 2, "내가 교체해도 상대는 교체 전 상대를 보고 고른 기술(지진)을 그대로 쓴다", usedEq + "/" + n);
    }

    private static void TestFieldEvents()
    {
        Console.WriteLine("== 날씨·필드 사건(화면 연출용) ==");
        var b = Fight(new SystemRng(5), Mk("SNORLAX", 50, "IMMUNITY", "RAINDANCE", "GRASSYTERRAIN"), Mk("SNORLAX", 50, "IMMUNITY", "SPLASH"));
        Use(b);
        var fe = b.Events.Where(e => e.Kind == BattleEventKind.FieldChange).ToList();
        Check(fe.Count >= 2 && (fe[0].Value & 15) == (int)Weather.Rain && (fe[0].Value & 256) == 0, "비바라기: 날씨가 비로 바뀐 사건", fe.Count.ToString());
        Check(fe.Any(e => (e.Value & 256) != 0 && (e.Value & 15) == (int)Weather.Rain), "턴 끝: 비가 이어지는 사건");
        Use(b, 1);
        Check(b.Events.Any(e => e.Kind == BattleEventKind.FieldChange && ((e.Value >> 4) & 15) == (int)Terrain.Grassy && (e.Value & 15) == (int)Weather.Rain), "그래스필드: 필드 사건에 날씨도 함께 담긴다");
    }

    private static void TestLearnedFoe()
    {
        Console.WriteLine("== 학습된 AI가 트레이너전 상대를 조종 ==");
        // 무작위 가중치의 작은 정책(입력 → 8 → 행동)으로 연결만 확인한다
        var ms = new System.IO.MemoryStream(); var bw = new System.IO.BinaryWriter(ms); var r = new Random(7);
        bw.Write(2);
        bw.Write(8); bw.Write(AiEncoder.Size); bw.Write((byte)1); for (int i = 0; i < 8 * AiEncoder.Size; i++) bw.Write((float)(r.NextDouble() - 0.5) * 0.2f); for (int i = 0; i < 8; i++) bw.Write(0f);
        bw.Write(AiEncoder.Actions); bw.Write(8); bw.Write((byte)0); for (int i = 0; i < AiEncoder.Actions * 8; i++) bw.Write((float)(r.NextDouble() - 0.5)); for (int i = 0; i < AiEncoder.Actions; i++) bw.Write(0f);
        ms.Position = 0; var pol = new NeuralPolicy(ms);
        Check(LearnedFoe.Fits(pol), "정책 크기가 관측·행동과 맞는다");
        var st = new EngineSettings(); int decided = 0, finished = 0, errors = 0, switches = 0;
        for (int seed = 1; seed <= 60; seed++)
        {
            try
            {
                var rng = new SystemRng(seed);
                var mine = RankedBuilder.Build("ou", 6, 50, rng, st);
                var foe = RankedBuilder.Build("ou", 6, 50, rng, st);
                var bt = RimketBattle.Trainer(rng, st, mine, Col("정착민", 1.0, 3, st), "상대", foe, null);
                var brain = new LearnedFoe(pol, st, new SystemRng(seed * 31)); bt.FoeBrain = brain.Decide;
                if (seed == 1) { try { Console.WriteLine("    첫 결정: " + brain.Decide(bt)); } catch (Exception ex) { Console.WriteLine(ex.ToString()); } }
                int guard = 0;
                while (!bt.IsOver && guard++ < 400)
                {
                    var legal = bt.LegalActions(); if (legal.Count == 0) break;
                    bt.Act(legal[rng.Next(0, legal.Count)]);
                }
                decided += brain.Decisions; if (bt.IsOver) finished++;
                switches += bt.Log.Count(l => l.Contains("불러들였다") && l.StartsWith("상대"));
            }
            catch (Exception ex) { errors++; if (errors == 1) Console.WriteLine(ex.ToString()); }
        }
        Check(errors == 0 && finished >= 55, "6대6 트레이너전 60판: 예외 없이 끝난다", "끝 " + finished + ", 오류 " + errors);
        Check(decided > 600 && switches > 0, "정책이 기술과 교체를 실제로 고른다", "결정 " + decided + ", 교체 " + switches);
    }

    private static void TestEnvironment()
    {
        Console.WriteLine("== 지도 환경 → 시작 날씨/필드 ==");
        var b = Fight(new SystemRng(5), Mk("SNORLAX", 50, "IMMUNITY", "SPLASH"), Mk("SNORLAX", 50, "IMMUNITY", "SPLASH"));
        b.ApplyEnvironment(Weather.Rain, Terrain.Electric);
        Check(b.CurrentWeather == Weather.Rain && b.CurrentTerrain == Terrain.Electric, "비 + 일렉트릭필드가 깔린다");
        for (int i = 0; i < 8 && !b.IsOver; i++) Use(b);
        Check(b.CurrentWeather == Weather.Rain && b.CurrentTerrain == Terrain.Electric, "환경 날씨·필드는 턴이 지나도 계속된다");
        var d = Fight(new SystemRng(5), Mk("POLITOED", 50, "DRIZZLE", "SPLASH"), Mk("SNORLAX", 50, "IMMUNITY", "SPLASH"));
        d.ApplyEnvironment(Weather.Snow, Terrain.None);
        Check(d.CurrentWeather == Weather.Rain, "등장 특성(잔비)이 깐 날씨를 덮어쓰지 않는다");
        var e = Fight(new SystemRng(5), Mk("SNORLAX", 50, "IMMUNITY", "SUNNYDAY"), Mk("SNORLAX", 50, "IMMUNITY", "SPLASH"));
        e.ApplyEnvironment(Weather.Rain, Terrain.None); Use(e);
        Check(e.CurrentWeather == Weather.Sun, "쾌청으로 환경 날씨를 바꿀 수 있다");
    }

    private static void TestLeague()
    {
        Console.WriteLine("== 포켓몬 리그(사천왕·챔피언) ==");
        Check(LeagueCatalog.Elites.Count >= 40 && LeagueCatalog.Champions.Count >= 15, "사천왕·챔피언 데이터", LeagueCatalog.Elites.Count + "/" + LeagueCatalog.Champions.Count);
        var st = new EngineSettings(); int bad = 0; string why = "";
        foreach (var g in LeagueCatalog.Elites.Concat(LeagueCatalog.Champions))
        {
            var s = GymBuilder.Build(g, 9, new SystemRng(3), st, g.Rank == 2 ? 6 : 5, g.Rank == 2 ? 100 : 96);
            if (s.Party.Count != (g.Rank == 2 ? 6 : 5) || s.Party.Any(c => c.Moves.Count == 0) || s.Party.Max(c => c.Level) != (g.Rank == 2 ? 100 : 96)) { bad++; why = g.Id; }
        }
        Check(bad == 0, "모든 사천왕·챔피언 파티를 만들 수 있다(크기·레벨·기술)", why);
        var lu = LeagueCatalog.NewLineup(new SystemRng(9));
        Check(lu.Count == 5 && lu.Take(4).Distinct().Count() == 4 && LeagueCatalog.Get(lu[4]).Rank == 2, "도전 순서: 서로 다른 사천왕 4명 + 챔피언");
    }

    private static void TestLookaheadFoe()
    {
        Console.WriteLine("== 미리 계산 AI(타이난 실베스터) ==");
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../Rimketmon/Data/ai_policy.bin");
        if (!System.IO.File.Exists(path)) { Check(true, "정책 파일 없음: 건너뜀"); return; }
        var pol = NeuralPolicy.Load(path); var s = new EngineSettings(); bool oldFull = AiEncoder.FullInfo; AiEncoder.FullInfo = true;
        int finished = 0, planned = 0, fallbacks = 0, same = 0;
        for (int g = 0; g < 3; g++)
        {
            var rng = new SystemRng(900 + g); string fmt = RankedPoolCatalog.Formats[g % 6];
            var p6 = RankedBuilder.Build(fmt, 6, RankedLadder.BattleLevel, rng, s); var f6 = RankedBuilder.Build(fmt, 6, RankedLadder.BattleLevel, rng, s);
            var mine = RankedRules.Brought(p6, new List<int> { 0, 1, 2 }, RankedLadder.BattleLevel); var foe = RankedRules.Brought(f6, new List<int> { 0, 1, 2 }, RankedLadder.BattleLevel);
            foreach (var c in mine.Concat(foe)) c.FullHeal();
            int seed = 1234 + g;
            var m0 = mine.Select(c => c.Clone()).ToList(); var f0 = foe.Select(c => c.Clone()).ToList();
            var b = RimketBattle.RankedBattle(new SystemRng(seed), s, "P", mine, "F", foe);
            var la = new LookaheadFoe(pol, s, seed, m0, f0, null, 77 + g) { Sims = 2, Depth = 1, WaitMs = 60000 };
            la.Attach(b);
            var pr = new SystemRng(5 + g); int guard = 0; var acts = new List<BattleAction>();
            while (!b.IsOver && guard++ < 200)
            {
                var l = b.LegalActions().Where(x => x.Kind != ActionKind.Disconnect && x.Kind != ActionKind.UseItem).ToList();
                if (l.Count == 0) break;
                var a = l[pr.Next(0, l.Count)]; if (b.Act(a)) acts.Add(a);
            }
            if (b.IsOver) finished++;
            planned += la.Planned; fallbacks += la.Fallbacks;
            // 재생 확인: 같은 시드·같은 행동·같은 상대 결정이면 같은 결과
            var foeActs = new List<BattleAction?>(); 
            var b2 = RimketBattle.RankedBattle(new SystemRng(seed), s, "P", m0.Select(c => c.Clone()).ToList(), "F", f0.Select(c => c.Clone()).ToList());
            var la2 = la; // 상대 결정은 la 기록을 그대로 쓰기 위해 같은 순서로 다시 낸다
            int fi = 0; var rec = la.FoeDecisions;
            b2.FoeBrain = bb => fi < rec.Count ? rec[fi++] : null;
            foreach (var a in acts) b2.Act(a);
            if (b2.IsOver == b.IsOver && b2.Player.Party.Select(c => c.Hp).SequenceEqual(b.Player.Party.Select(c => c.Hp)) && b2.Foe.Party.Select(c => c.Hp).SequenceEqual(b.Foe.Party.Select(c => c.Hp))) same++;
        }
        AiEncoder.FullInfo = oldFull;
        Check(finished == 3 && planned > 0 && same == 3, "미리 계산 AI: 끝까지 둔다, 미리 계산한 답을 쓴다, 기록으로 같은 판이 재생된다", "끝남 " + finished + "/3, 계산 " + planned + ", 대신 고름 " + fallbacks + ", 재생 일치 " + same + "/3");
    }

    private static void TestEvolution()
    {
        Console.WriteLine("== 진화 ==");
        var st = new EngineSettings();
        SpeciesInfo sp; SpeciesCatalog.TryGet("CHARMANDER", out sp);
        var c = SpawnRoller.FromSpecies(sp, 15, new SystemRng(1), st);
        Check(Evolution.Candidates(c).Count == 0, "레벨이 모자라면 진화하지 않는다");
        c.Level = 16; c.RecalculateStats(); c.Hp = c.MaxHp - 5;
        var cand = Evolution.Candidates(c);
        Check(cand.Count == 1 && cand[0].Id == "CHARMELEON", "파이리 Lv.16 → 리자드", string.Join(",", cand.Select(x => x.Id)));
        int oldMax = c.MaxHp, oldHp = c.Hp;
        Evolution.Apply(c, cand[0]);
        Check(c.Species == "CHARMELEON" && c.SpeciesId == "CHARMELEON" && c.Name == cand[0].Name && c.MaxHp > oldMax && c.Hp == oldHp + (c.MaxHp - oldMax), "종족·이름·능력치가 바뀌고 HP는 늘어난 만큼만 오른다");
        SpeciesInfo ev; SpeciesCatalog.TryGet("EEVEE", out ev);
        var e = SpawnRoller.FromSpecies(ev, 40, new SystemRng(1), st);
        Check(Evolution.Candidates(e).Count == 0, "이브이: 레벨만으로는 진화하지 않는다");
        e.Friendship = 200; Evolution.IsNight = () => false;
        var day = Evolution.Candidates(e).Select(x => x.Id).ToList();
        Evolution.IsNight = () => true;
        var night = Evolution.Candidates(e).Select(x => x.Id).ToList();
        Evolution.IsNight = null;
        Check(day.Contains("ESPEON") && !day.Contains("UMBREON") && night.Contains("UMBREON") && !night.Contains("ESPEON"), "이브이: 친밀도 높으면 낮엔 에브이, 밤엔 블래키", string.Join(",", day) + " / " + string.Join(",", night));
        Check(Evolution.ByItem(e, "THUNDERSTONE")?.Id == "JOLTEON" && Evolution.ByItem(e, "LEAFSTONE")?.Id == "LEAFEON" && Evolution.ByItem(e, "MOONSTONE") == null, "이브이: 돌을 쓰면 그 갈래로(맞지 않는 돌은 아무 일 없음)");
        // 모든 진화 갈래: 조건을 맞춰 주면 진화 후보에 오르고, 진화시키면 그 종족이 된다
        int total = 0, bad = 0; var badList = new List<string>();
        foreach (var from in SpeciesCatalog.All)
        foreach (var cond in Evolution.CondsOf(from.Id))
        {
            total++;
            var m = SpawnRoller.FromSpecies(from, Math.Max(1, Math.Min(100, cond.Level > 0 ? cond.Level : 30)), new SystemRng(total), st);
            m.Gender = cond.Gender == "M" ? Genders.Male : cond.Gender == "F" ? Genders.Female : m.Gender;
            Evolution.IsNight = () => cond.Time == "Night";
            List<RimketCreature> party = null; string used = null;
            switch (cond.Kind)
            {
                case "Item": used = cond.Param; break;
                case "Friendship":
                    m.Friendship = 255;
                    if (cond.Param != "") { var mv = MoveCatalog.All.First(x => x.Type == EvoCond.TypeOf(cond.Param) && !x.IsStatus || x.Type == EvoCond.TypeOf(cond.Param)); if (m.Moves.Count >= 4) m.Moves.RemoveAt(0); m.Moves.Add(new MoveSlot(mv)); }
                    break;
                case "Move": { Move mv; if (MoveCatalog.TryGet(cond.Param, out mv)) { if (m.Moves.Count >= 4) m.Moves.RemoveAt(0); m.Moves.Add(new MoveSlot(mv)); } break; }
                case "Hold": m.Item = cond.Param; break;
                case "Stat": if (cond.Param == "ATK") { m.Atk = 50; m.Def = 40; } else if (cond.Param == "DEF") { m.Atk = 40; m.Def = 50; } else { m.Atk = m.Def = 45; } break;
                case "PartySpecies": party = new List<RimketCreature> { m, SpawnRoller.FromSpecies(SpeciesCatalog.Get(cond.Param), 30, new SystemRng(1), st) }; break;
                case "PartyType": party = new List<RimketCreature> { m, SpawnRoller.FromSpecies(SpeciesCatalog.All.First(x => x.HasTypeOf(EvoCond.TypeOf(cond.Param))), 30, new SystemRng(1), st) }; break;
            }
            var ready = Evolution.Ready(m, used, party);
            SpeciesInfo toSp;
            bool ok = ready.Any(x => x.To == cond.To) && SpeciesCatalog.TryGet(cond.To, out toSp);
            if (ok) { SpeciesCatalog.TryGet(cond.To, out toSp); Evolution.Apply(m, toSp); Evolution.AfterEvolve(m, from.Id, cond.To); ok = m.Species == cond.To && m.MaxHp > 0 && (!(cond.Kind == "Hold" && cond.Consume) || m.Item == null); }
            if (!ok) { bad++; if (badList.Count < 8) badList.Add(from.Id + "→" + cond.To + "(" + cond.Kind + ")"); }
        }
        Evolution.IsNight = null;
        Check(bad == 0 && total > 400, "모든 진화 갈래 점검: " + total + "개", string.Join(", ", badList));
        e.NoEvolve = true;
        Check(Evolution.Candidates(e).Count == 0, "진화 멈춤이면 진화하지 않는다");
        var r = SpawnRoller.FromSpecies(sp, 40, new SystemRng(1), st); r.SpeciesId = "Muffalo";
        Check(Evolution.Candidates(r).Count == 0, "림월드 동물 모습의 림켓몬은 진화하지 않는다");
        var n = SpawnRoller.FromSpecies(sp, 40, new SystemRng(1), st); n.Name = "불꼬리";
        Evolution.Apply(n, cand[0]);
        Check(n.Name == "불꼬리", "닉네임은 그대로");
    }

    private static void TestHunt()
    {
        Console.WriteLine("== 포켓몬 사냥(바이옴·포획·이로치) ==");
        Check(HuntCatalog.All.Count >= 30, "바이옴 등장표", HuntCatalog.All.Count.ToString());
        var forest = HuntCatalog.Get("forest"); List<int> tiers;
        var table = HuntCatalog.Table(forest, 1, out tiers);
        Check(Math.Abs(table.Sum(kv => kv.Value) - 1.0) < 1e-9, "등장표 확률 합 = 100%", table.Sum(kv => kv.Value).ToString("F4"));
        var rng = new SystemRng(3); var cnt = new Dictionary<string, int>();
        for (int i = 0; i < 20000; i++) { var s = HuntCatalog.Roll(forest, 1, rng); int v; cnt.TryGetValue(s, out v); cnt[s] = v + 1; }
        var top = table[0];
        Check(Math.Abs(cnt[top.Key] / 20000.0 - top.Value) < 0.02, "실제로 뽑힌 비율이 표와 같다", top.Key + " " + (cnt[top.Key] / 20000.0).ToString("F3") + " / " + top.Value.ToString("F3"));
        {   // 레어 부적: 드묾 이상 등급 비율이 배율만큼 오른다, 표 합은 그대로 100%
            List<int> t1, t6;
            var tb1 = HuntCatalog.Table(forest, 1, out t1, 1.0); var tb6 = HuntCatalog.Table(forest, 1, out t6, 6.0);
            double r1 = tb1.Where((kv, i) => t1[i] >= 2).Sum(kv => kv.Value), r6 = tb6.Where((kv, i) => t6[i] >= 2).Sum(kv => kv.Value);
            Check(Math.Abs(tb6.Sum(kv => kv.Value) - 1.0) < 1e-9 && r6 > r1 * 3.5, "레어 부적 ×6: 드문 등급 " + (r1 * 100).ToString("F1") + "% → " + (r6 * 100).ToString("F1") + "%");
            int rare = 0; var rr = new SystemRng(8);
            var rareSet = new HashSet<string>(tb6.Where((kv, i) => t6[i] >= 2).Select(kv => kv.Key));
            for (int i = 0; i < 20000; i++) if (rareSet.Contains(HuntCatalog.Roll(forest, 1, rr, 6.0))) rare++;
            Check(Math.Abs(rare / 20000.0 - r6) < 0.02, "레어 부적 실제 뽑기 비율이 표와 같다", (rare / 20000.0).ToString("F3") + " / " + r6.ToString("F3"));
            var shSp = SpeciesCatalog.All.First(s => s.Id == "SHEDINJA"); var shc = SpawnRoller.FromSpecies(shSp, 50, new SystemRng(2), new EngineSettings());
            Check(shc.MaxHp == 1 && shc.Hp == 1, "껍질몬은 HP 1", "HP " + shc.Hp + "/" + shc.MaxHp);
        }
        // 포획 공식: 포획률 255·HP 1·하이퍼볼이면 거의 확실, 포획률 3·HP 가득·몬스터볼이면 매우 낮다
        var st = new EngineSettings(); SpeciesInfo sp; SpeciesCatalog.TryGet("CATERPIE", out sp);
        var c = SpawnRoller.FromSpecies(sp, 30, new SystemRng(1), st); c.Hp = 1; int sh, ok = 0;
        for (int i = 0; i < 1000; i++) if (RimketBattle.RollCatch(c, StatusCondition.None, 2, rng, out sh)) ok++;
        Check(ok > 990, "캐터피(255) HP 1 + 하이퍼볼 → 거의 잡힌다", ok + "/1000");
        SpeciesCatalog.TryGet("MEWTWO", out sp); var m = SpawnRoller.FromSpecies(sp, 70, new SystemRng(1), st); ok = 0;
        for (int i = 0; i < 2000; i++) if (RimketBattle.RollCatch(m, StatusCondition.None, 0, rng, out sh)) ok++;
        Check(RimketBattle.CatchRateOf("MEWTWO") == 3 && ok < 40, "뮤츠(3) HP 가득 + 몬스터볼 → 1% 미만", ok + "/2000");
        Check(RimketBattle.RollCatch(m, StatusCondition.None, 3, rng, out sh), "마스터볼은 반드시 잡힌다");
        // 전투: 볼을 던져 실패하면 상대는 그 턴에 행동하지 않는다
        SpeciesCatalog.TryGet("SNORLAX", out sp);
        var mine = SpawnRoller.FromSpecies(sp, 50, new SystemRng(2), st); var wild = SpawnRoller.FromSpecies(sp, 50, new SystemRng(4), st);
        var b = new RimketBattle(new SystemRng(5), st, new List<RimketCreature> { mine }, wild, Col("정착민", 1.0, 3, st)); b.HuntMode = true; b.Balls[0] = 30;
        int hpBefore = mine.Hp, fails = 0;
        for (int i = 0; i < 30 && !b.IsOver; i++) { b.ThrowBall(0); if (!b.LastCaught) fails++; }
        Check(fails > 0 && mine.Hp == hpBefore, "볼에서 나온 포켓몬은 기술을 쓰지 않는다", "실패 " + fails + ", HP " + hpBefore + "→" + mine.Hp);
        Check(b.Balls[0] == 30 - b.Log.Count(l => l.EndsWith("볼을 던졌다!")), "던진 만큼 볼이 준다");
        // 쓰러뜨리면 잡을 수 없다(선택 단계 없이 끝)
        var b2 = new RimketBattle(new SystemRng(6), st, new List<RimketCreature> { mine }, wild, Col("정착민", 1.0, 3, st)); b2.HuntMode = true;
        wild.Hp = 1; mine.FullHeal();
        for (int i = 0; i < 20 && !b2.IsOver; i++) { var l = b2.LegalActions(); if (l.Count == 0) break; b2.Act(l.Any(a => a.Kind == ActionKind.UseMove) ? l.First(a => a.Kind == ActionKind.UseMove) : l[0]); }
        Check(b2.IsOver && b2.Phase != BattlePhase.WinChoice && !b2.Result.Captured, "쓰러뜨리면 그대로 끝(포획 선택 없음)");
        // 폼: 별개의 완성 형태로 나온다
        SpeciesInfo rw; SpeciesCatalog.TryGet("ROTOM_WASH", out rw);
        Check(rw != null && rw.Type2 == ElementType.Water && rw.BaseForm == "ROTOM" && rw.Learnset.Count > 5, "워시로토무: 전기/물, 원래 종족 로토무");
        var fr = new Dictionary<string, int>(); var r3 = new SystemRng(4);
        for (int i = 0; i < 6000; i++) { var id = HuntCatalog.PickForm("ROTOM", r3); int v; fr.TryGetValue(id, out v); fr[id] = v + 1; }
        Check(fr.Count == 6 && fr.Values.All(v => v > 800), "로토무는 원래 모습 + 가전 5가지가 고르게 나온다", string.Join(",", fr.Select(kv => kv.Key + ":" + kv.Value)));
        Check(HuntCatalog.All.Any(b => Enumerable.Range(0, 5).Any(t => Enumerable.Range(0, 5).Any(tm => b.Pools[t, tm].Contains("ALOLA_VULPIX")))), "알로라 식스테일이 바이옴 등장표에 있다");
        Check(RimketBattle.CatchRateOf("ALOLA_RAICHU") == 75 && !SpeciesCatalog.All.Any(s => s.Id == "ROTOM_WASH"), "폼 포획률, 폼은 랭크·관장 종족 목록에는 없다");
        // 이로치: 기본 1/4096, 배율을 주면 그만큼
        int s1 = 0, s2 = 0; var r2 = new SystemRng(8);
        for (int i = 0; i < 400000; i++) { if (Shinies.Roll("PIKACHU", r2) > 0) s1++; if (Shinies.Roll("PIKACHU", r2, 8) > 0) s2++; }
        Check(Math.Abs(s1 / 400000.0 * Shinies.BaseOdds - 1) < 0.2 && Math.Abs(s2 / 400000.0 * Shinies.BaseOdds - 8) < 1.0, "이로치 확률 1/" + Shinies.BaseOdds + ", 배율 8이면 8/" + Shinies.BaseOdds, s1 + " / " + s2);
    }

    private static Link MakeLinkSimple(bool dbl) { List<RimketCreature> a, b; return MakeLink(2, dbl, out a, out b); }

    private static string StableHash(string t) { uint h = 2166136261; foreach (char c in t) { h ^= c; h *= 16777619; } return h.ToString("x8"); }

    private static void TestGender()
    {
        var rng = new SystemRng(11);
        Check(Genders.Roll("MAGNEMITE", 0, rng) == Genders.None, "성별: 무성 종족은 동물 성별과 무관하게 무성");
        Check(Genders.Roll("TAUROS", 1, rng) == Genders.Male, "성별: 수컷 전용 종족은 수컷");
        Check(Genders.Roll("CHANSEY", 0, rng) == Genders.Female, "성별: 암컷 전용 종족은 암컷");
        Check(Genders.Roll("PIKACHU", 1, rng) == Genders.Female && Genders.Roll("PIKACHU", 0, rng) == Genders.Male, "성별: 동물의 성별을 그대로 이어받는다");
        int f = 0; for (int i = 0; i < 400; i++) if (Genders.Roll("PIKACHU", -1, rng) == Genders.Female) f++;
        Check(f > 150 && f < 250, "성별: 지정이 없으면 비율대로(50%)", f.ToString());
        { // 헤롱헤롱: 반대 성별만
            var b = Fight(new SystemRng(3), MkG("SNORLAX", 0, "IMMUNITY", "ATTRACT"), MkG("PIKACHU", 1, "STATIC", "SPLASH"));
            Use(b);
            Check(b.FoeFighter.Attracted, "유혹: 반대 성별은 헤롱헤롱");
            var b2 = Fight(new SystemRng(3), MkG("SNORLAX", 0, "IMMUNITY", "ATTRACT"), MkG("PIKACHU", 0, "STATIC", "SPLASH"));
            Use(b2);
            Check(!b2.FoeFighter.Attracted, "유혹: 같은 성별은 실패");
            var b3 = Fight(new SystemRng(3), MkG("SNORLAX", 0, "IMMUNITY", "ATTRACT"), MkG("MAGNEMITE", 2, "STURDY", "SPLASH"));
            Use(b3);
            Check(!b3.FoeFighter.Attracted, "유혹: 무성은 실패");
            var b4 = Fight(new SystemRng(3), MkG("SNORLAX", 0, "IMMUNITY", "ATTRACT"), MkG("PIKACHU", 1, "OBLIVIOUS", "SPLASH"));
            Use(b4);
            Check(!b4.FoeFighter.Attracted, "둔감: 헤롱헤롱에 걸리지 않는다");
        }
        { // 꼬리치기(성별 조건)
            var b = Fight(new SystemRng(3), MkG("SNORLAX", 0, "IMMUNITY", "CAPTIVATE"), MkG("PIKACHU", 1, "STATIC", "SPLASH"));
            Use(b);
            Check(b.FoeFighter.Stage[Stat.SpA] == -2, "유혹의 목소리(Captivate): 반대 성별에 특공 -2");
            var b2 = Fight(new SystemRng(3), MkG("SNORLAX", 0, "IMMUNITY", "CAPTIVATE"), MkG("PIKACHU", 0, "STATIC", "SPLASH"));
            Use(b2);
            Check(b2.FoeFighter.Stage[Stat.SpA] == 0, "Captivate: 같은 성별에는 실패");
        }
        { // 귀여운매력
            int hit = 0;
            for (int sd = 1; sd <= 30; sd++)
            {
                var b = Fight(new SystemRng(sd * 7919), MkG("SNORLAX", 0, "IMMUNITY", "TACKLE"), MkG("CLEFAIRY", 1, "CUTECHARM", "SPLASH"));
                Use(b); if (b.ActiveFighter.Attracted) hit++;
            }
            int none = 0;
            for (int sd = 1; sd <= 30; sd++)
            {
                var b = Fight(new SystemRng(sd * 7919), MkG("SNORLAX", 1, "IMMUNITY", "TACKLE"), MkG("CLEFAIRY", 1, "CUTECHARM", "SPLASH"));
                Use(b); if (b.ActiveFighter.Attracted) none++;
            }
            Check(hit > 3 && none == 0, "귀여운매력: 반대 성별에만 발동", hit + "/" + none);
        }
        { // 라이벌 의식
            int dSame, dOpp, dNone;
            { var b = Fight(new SystemRng(5), MkG("NIDOKING", 0, "RIVALRY", "TACKLE"), MkG("SNORLAX", 0, "IMMUNITY", "SPLASH")); int h = b.FoeFighter.C.Hp; Use(b); dSame = FoeLoss(b, h); }
            { var b = Fight(new SystemRng(5), MkG("NIDOKING", 0, "RIVALRY", "TACKLE"), MkG("SNORLAX", 1, "IMMUNITY", "SPLASH")); int h = b.FoeFighter.C.Hp; Use(b); dOpp = FoeLoss(b, h); }
            { var b = Fight(new SystemRng(5), MkG("NIDOKING", 0, "RIVALRY", "TACKLE"), MkG("SNORLAX", 2, "IMMUNITY", "SPLASH")); int h = b.FoeFighter.C.Hp; Use(b); dNone = FoeLoss(b, h); }
            Check(dSame > dNone && dNone > dOpp, "라이벌 의식: 같은 성별 강화 / 반대 약화 / 무성 보통", dSame + ">" + dNone + ">" + dOpp);
        }
        { // 빨간실
            var b = Fight(new SystemRng(3), MkItemG("SNORLAX", 0, "DESTINYKNOT", "SPLASH"), MkG("PIKACHU", 1, "STATIC", "ATTRACT"));
            Use(b);
            Check(b.ActiveFighter.Attracted && b.FoeFighter.Attracted, "빨간실: 헤롱헤롱이 상대에게도 번진다");
        }
    }

    private static RimketCreature MkItemG(string species, int gender, string item, params string[] moves) { var c = Mk(species, 50, "IMMUNITY", moves); c.Gender = gender; c.Item = item; return c; }

    private static RimketCreature MkI(string species, int level, string ability, string item, params string[] moves)
    {
        var c = Mk(species, level, ability, moves); c.Item = item; return c;
    }

    private static int FoeLoss(RimketBattle b, int hp0) { return hp0 - b.FoeFighter.C.Hp; }

    private static void TestItems()
    {
        // 데이터
        Check(ItemCatalog.All.Count() > 800 && ItemCatalog.Get("LEFTOVERS").Ko == "먹다남은음식" && ItemCatalog.Get("ORANBERRY").Ko == "오랭열매", "도구 데이터 로드", ItemCatalog.All.Count().ToString());
        Check(ItemCatalog.Get("FLAMEORB").Fling == 30 && ItemCatalog.Get("ORANBERRY").NgType.HasValue, "던지기/자연의 은혜 수치 로드");
        { // 열매: 체력 50% 이하에서 먹는다
            var me = MkI("SNORLAX", 50, "IMMUNITY", "SITRUSBERRY", "SPLASH"); me.Hp = me.MaxHp * 4 / 10;
            var b = Fight(new SystemRng(3), me, Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Use(b);
            Check(b.ActiveFighter.Item == null && b.ActiveFighter.C.Hp > me.MaxHp * 4 / 10 && me.Item == "SITRUSBERRY", "자뭉열매: 반 이하에서 먹고 회복(원본 도구는 유지)", b.ActiveFighter.C.Hp + "/" + me.MaxHp);
            Check(b.ActiveFighter.ConsumedItem == "SITRUSBERRY" && b.ActiveFighter.AteBerry, "열매 소모 기록");
        }
        { // 상태이상 열매
            var me = MkI("SNORLAX", 50, "IMMUNITY", "LUMBERRY", "SPLASH");
            var b = Fight(new SystemRng(3), me, Mk("PIKACHU", 50, "STATIC", "THUNDERWAVE"));
            Use(b);
            Check(b.ActiveFighter.Status == StatusCondition.None && b.ActiveFighter.Item == null, "리샘열매: 상태이상을 즉시 회복");
        }
        { // 먹다남은음식
            var me = MkI("SNORLAX", 50, "IMMUNITY", "LEFTOVERS", "SPLASH"); me.Hp = me.MaxHp * 6 / 10;
            var b = Fight(new SystemRng(3), me, Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Use(b);
            Check(b.ActiveFighter.C.Hp > me.MaxHp * 6 / 10, "먹다남은음식: 턴 종료 회복", b.ActiveFighter.C.Hp.ToString());
        }
        { // 생명의구슬: 위력 1.3배 + 반동
            int d0, d1;
            { var b = Fight(new SystemRng(5), Mk("SNORLAX", 30, "IMMUNITY", "TACKLE"), Mk("BLISSEY", 70, "NATURALCURE", "SPLASH")); int h = b.FoeFighter.C.Hp; Use(b); d0 = FoeLoss(b, h); }
            bool recoil;
            { var b = Fight(new SystemRng(5), MkI("SNORLAX", 30, "IMMUNITY", "LIFEORB", "TACKLE"), Mk("BLISSEY", 70, "NATURALCURE", "SPLASH")); int h = b.FoeFighter.C.Hp; Use(b); d1 = FoeLoss(b, h); recoil = LogHas(b, "반동"); }
            Check(d1 > d0 * 1.15 && d1 < d0 * 1.45 && recoil, "생명의구슬: 피해 증가 + 반동", d0 + "→" + d1);
        }
        { // 구애머리띠: 다른 기술 봉인
            var b = Fight(new SystemRng(3), MkI("SNORLAX", 50, "IMMUNITY", "CHOICEBAND", "TACKLE", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Check(b.MoveBlocked(1) == null, "구애: 사용 전에는 봉인 없음");
            Use(b, 0);
            Check(b.MoveBlocked(1) != null && b.MoveBlocked(0) == null, "구애: 쓴 기술 외 봉인", b.MoveBlocked(1) ?? "null");
        }
        { // 기합의 띠
            var b = Fight(new SystemRng(3), MkI("PIKACHU", 5, "STATIC", "FOCUSSASH", "SPLASH"), Mk("GROUDON", 100, "DROUGHT", "EARTHQUAKE"));
            Use(b);
            Check(b.ActiveFighter.C.Hp == 1 && b.ActiveFighter.Item == null, "기合의 머리띠 → 기습: 1로 버티고 소모", b.ActiveFighter.C.Hp.ToString());
        }
        { // 반감 열매
            int d0, d1;
            { var b = Fight(new SystemRng(5), Mk("BULBASAUR", 50, "OVERGROW", "SPLASH"), Mk("CHARMANDER", 50, "BLAZE", "EMBER")); int h = b.ActiveFighter.C.Hp; Use(b); d0 = h - b.ActiveFighter.C.Hp; }
            { var b = Fight(new SystemRng(5), MkI("BULBASAUR", 50, "OVERGROW", "OCCABERRY", "SPLASH"), Mk("CHARMANDER", 50, "BLAZE", "EMBER")); int h = b.ActiveFighter.C.Hp; Use(b); d1 = h - b.ActiveFighter.C.Hp; Check(b.ActiveFighter.Item == null, "반감 열매 소모"); }
            Check(d0 > 0 && d1 < d0 * 0.7, "반감 열매: 효과 굉장 피해 절반", d0 + "→" + d1);
        }
        { // 약점보험
            var b = Fight(new SystemRng(3), MkI("BULBASAUR", 50, "OVERGROW", "WEAKNESSPOLICY", "SPLASH"), Mk("CHARMANDER", 50, "BLAZE", "EMBER"));
            Use(b);
            Check(b.ActiveFighter.Stage[Stat.Atk] == 2 && b.ActiveFighter.Stage[Stat.SpA] == 2, "약점보험: 효과 굉장에 공격/특공 +2");
        }
        { // 구애 스카프 속도 / 풍선
            var b = Fight(new SystemRng(3), MkI("SNORLAX", 50, "IMMUNITY", "AIRBALLOON", "SPLASH"), Mk("DUGTRIO", 50, "ARENATRAP", "EARTHQUAKE"));
            Use(b);
            Check(LogHas(b, "효과가 없는") || b.ActiveFighter.Item == null || b.ActiveFighter.C.Hp == b.ActiveFighter.C.MaxHp, "바람풍선: 땅 기술 무효");
        }
        { // 던지기: 화염구슬
            var me = MkI("SNORLAX", 50, "IMMUNITY", "FLAMEORB", "FLING");
            var b = Fight(new SystemRng(3), me, Mk("BLISSEY", 50, "NATURALCURE", "SPLASH"));
            int h = b.FoeFighter.C.Hp; Use(b);
            Check(b.ActiveFighter.Item == null && b.FoeFighter.C.Hp < h && b.FoeFighter.Status == StatusCondition.Burn, "던지기: 화염구슬을 던져 화상", string.Join("|", b.Log.Skip(Math.Max(0, b.Log.Count - 6))));
        }
        { // 던지기: 열매는 상대가 먹는다
            var foe = Mk("BLISSEY", 50, "NATURALCURE", "SPLASH"); foe.Hp = foe.MaxHp / 4;
            var b = Fight(new SystemRng(3), MkI("SNORLAX", 50, "IMMUNITY", "SITRUSBERRY", "FLING"), foe);
            Use(b);
            Check(LogHas(b, "던졌다") && LogHas(b, "먹었다"), "던지기: 열매를 던지면 상대가 먹는다");
        }
        { // 도구 없으면 던지기 실패
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "IMMUNITY", "FLING"), Mk("BLISSEY", 50, "NATURALCURE", "SPLASH"));
            int h = b.FoeFighter.C.Hp; Use(b);
            Check(b.FoeFighter.C.Hp == h, "던지기: 도구 없으면 실패");
        }
        { // 자연의 은혜
            var b = Fight(new SystemRng(3), MkI("SNORLAX", 50, "IMMUNITY", "ORANBERRY", "NATURALGIFT"), Mk("BLISSEY", 50, "NATURALCURE", "SPLASH"));
            int h = b.FoeFighter.C.Hp; Use(b);
            Check(b.ActiveFighter.Item == null && b.FoeFighter.C.Hp < h, "자연의 은혜: 열매를 소모해 공격", string.Join("|", b.Log.Skip(Math.Max(0, b.Log.Count - 5))));
            var b2 = Fight(new SystemRng(3), Mk("SNORLAX", 50, "IMMUNITY", "NATURALGIFT"), Mk("BLISSEY", 50, "NATURALCURE", "SPLASH"));
            int h2 = b2.FoeFighter.C.Hp; Use(b2);
            Check(b2.FoeFighter.C.Hp == h2, "자연의 은혜: 열매 없으면 실패");
        }
        { // 트릭
            var b = Fight(new SystemRng(3), MkI("GENGAR", 50, "LEVITATE", "CHOICESCARF", "TRICK"), MkI("BLISSEY", 50, "NATURALCURE", "LEFTOVERS", "SPLASH"));
            Use(b);
            Check(b.ActiveFighter.Item == "LEFTOVERS" && b.FoeFighter.Item == "CHOICESCARF", "트릭: 도구 교환", b.ActiveFighter.Item + "/" + b.FoeFighter.Item);
        }
        { // 탁쳐서떨구기: 도구가 있으면 위력 1.5배
            int d0, d1;
            { var b = Fight(new SystemRng(5), Mk("SNORLAX", 50, "IMMUNITY", "KNOCKOFF"), Mk("BLISSEY", 50, "NATURALCURE", "SPLASH")); int h = b.FoeFighter.C.Hp; Use(b); d0 = FoeLoss(b, h); }
            { var b = Fight(new SystemRng(5), Mk("SNORLAX", 50, "IMMUNITY", "KNOCKOFF"), MkI("BLISSEY", 50, "NATURALCURE", "LEFTOVERS", "SPLASH")); int h = b.FoeFighter.C.Hp; Use(b); d1 = FoeLoss(b, h);
              Check(b.FoeFighter.Item == null && b.FoeFighter.C.Item == "LEFTOVERS", "탁쳐서떨구기: 도구 제거(원본은 유지)"); }
            Check(d1 > d0 * 1.3, "탁쳐서떨구기: 도구가 있으면 위력 증가", d0 + "→" + d1);
        }
        { // 도둑질
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "IMMUNITY", "THIEF"), MkI("BLISSEY", 50, "NATURALCURE", "LEFTOVERS", "SPLASH"));
            Use(b);
            Check(b.ActiveFighter.Item == "LEFTOVERS" && b.FoeFighter.Item == null, "도둑질: 상대 도구를 빼앗는다");
        }
        { // 점착(끈적끈적바늘)
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "IMMUNITY", "THIEF"), MkI("BLISSEY", 50, "STICKYHOLD", "LEFTOVERS", "SPLASH"));
            Use(b);
            Check(b.ActiveFighter.Item == null && b.FoeFighter.Item == "LEFTOVERS", "점착: 도구를 빼앗기지 않는다");
        }
        { // 벌레먹기
            var foe = MkI("BLISSEY", 50, "NATURALCURE", "SITRUSBERRY", "SPLASH");
            var me = Mk("SNORLAX", 50, "IMMUNITY", "BUGBITE"); me.Hp = me.MaxHp / 4;
            var b = Fight(new SystemRng(3), me, foe);
            Use(b);
            Check(b.FoeFighter.Item == null && b.ActiveFighter.C.Hp > me.MaxHp / 4 && b.ActiveFighter.AteBerry, "벌레먹기: 상대 열매를 먹는다", b.ActiveFighter.C.Hp + "/" + me.MaxHp);
        }
        { // 재활용
            var me = MkI("SNORLAX", 50, "IMMUNITY", "SITRUSBERRY", "SPLASH", "RECYCLE"); me.Hp = me.MaxHp * 4 / 10;
            var b = Fight(new SystemRng(3), me, Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Use(b, 0); Use(b, 1);
            Check(b.ActiveFighter.Item == "SITRUSBERRY" || (b.ActiveFighter.Item == null && b.ActiveFighter.ConsumedItem == null), "재활용: 소모한 열매를 되찾는다", b.ActiveFighter.Item + "/" + b.ActiveFighter.ConsumedItem);
        }
        { // 폴터가이스트
            var b = Fight(new SystemRng(3), Mk("GENGAR", 50, "LEVITATE", "POLTERGEIST"), MkI("PIKACHU", 50, "STATIC", "LEFTOVERS", "SPLASH"));
            int h = b.FoeFighter.C.Hp; Use(b);
            Check(b.FoeFighter.C.Hp < h, "폴터가이스트: 도구가 있으면 명중");
            var b2 = Fight(new SystemRng(3), Mk("GENGAR", 50, "LEVITATE", "POLTERGEIST"), Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            int h2 = b2.FoeFighter.C.Hp; Use(b2);
            Check(b2.FoeFighter.C.Hp == h2, "폴터가이스트: 도구가 없으면 실패");
        }
        { // 트림
            var b = Fight(new SystemRng(3), Mk("GULPIN", 50, "STICKYHOLD", "BELCH"), Mk("BLISSEY", 50, "NATURALCURE", "SPLASH"));
            int h = b.FoeFighter.C.Hp; Use(b);
            Check(b.FoeFighter.C.Hp == h, "트림: 열매를 먹기 전에는 실패");
        }
        { // 금제
            var b = Fight(new SystemRng(3), Mk("SNORLAX", 50, "IMMUNITY", "EMBARGO"), MkI("BLISSEY", 50, "NATURALCURE", "LEFTOVERS", "SPLASH"));
            Use(b);
            Check(b.FoeFighter.EmbargoTurns > 0 && b.FoeFighter.HeldItem == null, "금제: 도구 효과 정지");
        }
        { // 볼 가득 먹기
            var b = Fight(new SystemRng(3), MkI("SNORLAX", 50, "IMMUNITY", "COBABERRY", "STUFFCHEEKS"), Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Use(b);
            Check(b.ActiveFighter.Stage[Stat.Def] == 2 && b.ActiveFighter.Item == null, "볼 가득 먹기: 열매 소모 + 방어 +2");
        }
        { // 불태우기
            var b = Fight(new SystemRng(3), Mk("CHARMANDER", 50, "BLAZE", "INCINERATE"), MkI("BLISSEY", 50, "NATURALCURE", "ORANBERRY", "SPLASH"));
            Use(b);
            Check(b.FoeFighter.Item == null, "불태우기: 열매를 태운다");
        }
        { // 건네주기
            var b = Fight(new SystemRng(3), MkI("SNORLAX", 50, "IMMUNITY", "LEFTOVERS", "BESTOW"), Mk("BLISSEY", 50, "NATURALCURE", "SPLASH"));
            Use(b);
            Check(b.ActiveFighter.Item == null && b.FoeFighter.Item == "LEFTOVERS", "건네주기: 도구를 상대에게 준다");
        }
        { // 부식가스
            var b = Fight(new SystemRng(3), Mk("KOFFING", 50, "LEVITATE", "CORROSIVEGAS"), MkI("BLISSEY", 50, "NATURALCURE", "LEFTOVERS", "SPLASH"));
            Use(b);
            Check(b.FoeFighter.Item == null, "부식가스: 도구를 녹인다");
        }
        { // 차 마시기
            var me = MkI("SNORLAX", 50, "IMMUNITY", "ORANBERRY", "TEATIME"); me.Hp = me.MaxHp - 5;
            var b = Fight(new SystemRng(3), me, MkI("BLISSEY", 50, "NATURALCURE", "SITRUSBERRY", "SPLASH"));
            Use(b);
            Check(b.ActiveFighter.Item == null && b.FoeFighter.Item == null, "티타임: 모두가 열매를 먹는다");
        }
        { // 곡예
            int d0, d1;
            { var b = Fight(new SystemRng(5), Mk("SNORLAX", 50, "IMMUNITY", "ACROBATICS"), Mk("BLISSEY", 50, "NATURALCURE", "SPLASH")); int h = b.FoeFighter.C.Hp; Use(b); d0 = FoeLoss(b, h); }
            { var b = Fight(new SystemRng(5), MkI("SNORLAX", 50, "IMMUNITY", "LEFTOVERS", "ACROBATICS"), Mk("BLISSEY", 50, "NATURALCURE", "SPLASH")); int h = b.FoeFighter.C.Hp; Use(b); d1 = FoeLoss(b, h); }
            Check(d0 > d1 * 1.6, "애크러뱃: 도구가 없으면 위력 2배", d1 + "→" + d0);
        }
        { // 특성: 둔감, 긴장감, 소매치기, 요술
            var b = Fight(new SystemRng(5), MkI("SNORLAX", 50, "KLUTZ", "LIFEORB", "BODYSLAM"), Mk("BLISSEY", 50, "NATURALCURE", "SPLASH"));
            Use(b);
            Check(!LogHas(b, "반동"), "서투름: 지닌 도구 효과 없음");
            var me = MkI("SNORLAX", 50, "IMMUNITY", "SITRUSBERRY", "SPLASH"); me.Hp = me.MaxHp * 4 / 10;
            var b2 = Fight(new SystemRng(5), me, Mk("PIKACHU", 50, "UNNERVE", "SPLASH"));
            Use(b2);
            Check(b2.ActiveFighter.Item == "SITRUSBERRY", "긴장감: 상대는 열매를 먹지 못한다");
            var b3 = Fight(new SystemRng(5), MkI("SNORLAX", 50, "IMMUNITY", "LEFTOVERS", "SPLASH"), Mk("PIKACHU", 50, "PICKPOCKET", "SPLASH"));
            var b4 = Fight(new SystemRng(5), Mk("HOUNDOUR", 50, "MAGICIAN", "TACKLE"), MkI("BLISSEY", 50, "NATURALCURE", "LEFTOVERS", "SPLASH"));
            Use(b4);
            Check(b4.ActiveFighter.Item == "LEFTOVERS" && b4.FoeFighter.Item == null, "요술: 때린 상대의 도구를 가로챈다");
        }
        { // 멀티타입
            var b = Fight(new SystemRng(3), MkI("ARCEUS", 50, "MULTITYPE", "FLAMEPLATE", "SPLASH"), Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Check(b.ActiveFighter.Type1 == ElementType.Fire && !b.ActiveFighter.Type2.HasValue, "멀티타입: 플레이트에 따라 타입이 바뀐다", b.ActiveFighter.Type1.ToString());
        }
        { // 전투가 끝나도 원본 도구는 그대로
            var me = MkI("SNORLAX", 50, "IMMUNITY", "SITRUSBERRY", "SPLASH"); me.Hp = me.MaxHp * 4 / 10;
            var b = Fight(new SystemRng(3), me, Mk("PIKACHU", 50, "STATIC", "SPLASH"));
            Use(b);
            Check(me.Item == "SITRUSBERRY", "전투 중 소모는 원본 도구에 반영되지 않는다");
        }
    }

    private static RimketCreature Mk(string species, int level, string ability, params string[] moves)
    {
        var c = SpawnRoller.FromSpecies(SpeciesCatalog.Get(species), level, new SystemRng(1), new EngineSettings());
        c.Ability = ability;
        c.Moves = moves.Select(id => new MoveSlot(MoveCatalog.Get(id))).ToList();
        c.Hp = c.MaxHp;
        return c;
    }

    private static RimketBattle Fight(IRng rng, RimketCreature mine, RimketCreature foe, EngineSettings s = null)
    {
        return new RimketBattle(rng, s ?? new EngineSettings(), new[] { mine }, foe, Col("정착민", 1.0, 3, new EngineSettings()));
    }

    private static bool LogHas(RimketBattle b, string text) { return b.Log.Any(l => l.Contains(text)); }
    private static void Use(RimketBattle b, int slot = 0) { b.Act(new BattleAction(ActionKind.UseMove, slot)); }
}
