# 인계: 림월드 시간 조작 + 창/월드 입력 분리 + 배틀별 시간 정지 + 랭크 타이머 제거 (2026-11-19)

## 요구사항 (사용자 확정)
1. 림켓몬을 하는 도중에도 림월드 시간을 조작할 수 있다. 림켓몬 창과 림월드 월드를 **둘 다 조작**할 수 있고,
   창을 누르면 창이, 월드를 누르면 월드가 조작된다(창은 입력을 받지 않는다).
2. 아래 **5종 배틀은 림월드 시간이 멈추고**, 그때 시간을 움직이려 하면 "배틀 중에는 시간을 움직일 수 없다." 문구를 띄운다.
   | 정지 대상 | 시작 위치 | 판정 |
   |---|---|---|
   | 야생동물(림볼 던지기) | `BattleFlow.OpenWild`, `DevStartWild` | `RimketTime.MarkFrozen` |
   | 방문자 배틀 | `BattleFlow.StartVisitorBattle` | `MarkFrozen` |
   | 어둠의 배틀(사람·아포크립톤) | `BattleFlow.StartChallenge` | `MarkFrozen` |
   | ??? | `BattleFlow.StartMystery` | `MarkFrozen` |
   | 온라인 매칭 배틀(싱글·더블, 개발용 컴퓨터 매칭 포함) | `BattleFlow.StartOnline` | `battle.Online` |
3. 그 밖의 배틀(체육관·사천왕·챔피언, 랭크전, 패러독스, 포켓몬 사냥)은 **림월드 시간과 동시 진행**: 시간이 흐르고 조작된다.
4. **랭크전 타이머 제거**: 처음 요청 "랭크배틀중에 시간이 안 흐르게"는 림월드 시간을 멈추라는 뜻이 아니라
   **랭크전에 붙은 제한 시간 기능(선출 90초·한 턴 45초)을 꺼 달라는 것**이었다(2026-11-18 정정). 관련 모드 설정도 삭제.

## 랭크전 타이머 제거 — 무엇을 지웠나
- 설정 `RimketmonSettings.rankedTimer`: 필드, `Scribe_Values`, 모드 설정 체크박스 "랭크배틀 시간 제한 (선출 90초 · 한 턴 45초)" 삭제
  (예전 설정 파일에 키가 남아 있어도 무시된다).
- 선출 화면(`Window_RimketMenu.Ranked.cs`): `RankedPreviewSeconds`(90초), 남은 시간 표시, 시간 초과 시 자동 선출(`LaunchRanked`) 삭제. 이제 "선택 N/3"만 표시.
- 배틀 창(`Window_RimketBattle.cs`): `RankedTurnSeconds`(45초), 남은 시간 라벨, `AutoRankedAct`(시간 초과 자동 행동), `CloseOpenSwitchMenus`, `lastAutoAct` 삭제.
  `rankedTimerOn/rankedTurnStart`는 `rankedTurnBegun`으로 바꿔 **상대의 생각 시간(`oppReadyAt`)을 턴마다 한 번 정하는 용도로만** 남겼다.
- 튜토리얼 문구 "한 턴에 45초 안에 정해야 해" 삭제(`Window_RimketMenu.Tutorial.cs`), 영어 번역(`ui.txt`)도 같이 수정·삭제.
- 일부러 **그대로 둔 것**: ① 온라인 배틀의 제한 시간(`Engine/Online/OnlineSession.cs`, 상대와 맞춰야 해서 필요) ② 랭크 상대 AI의 "생각 시간" 연출
  (`humanTiming`, `thinkTimeScale`, `HumanWait`)은 플레이어를 재촉하는 타이머가 아니라 상대 행동을 늦추는 연출이라 유지.
  ③ `Tests/Program.cs`의 "항상 첫 행동만 고르는 랭크 배틀 300판" 검증은 엔진 견고성 테스트라 유지(주석만 수정).
- `docs/개발기록.md`의 "선출 90초 제한"(217행 부근)은 이제 옛 설명이다 — 메인 AI가 개발 기록을 고칠 때 반영할 것(이 인계에서는 건드리지 않음).

## 시간 조작 구현 (핵심 = `ModSource/RimketTime.cs`)
- `IRimketLive` 마커를 구현한 창만 새 규칙: **Window_RimketMenu, Window_RimketBattle, Window_RimketDouble**.
  체육관 룰렛·진화·연출·경고·전환·인카운터·세레나 등은 예전 그대로(항상 정지, 전체 화면).
- **시간**: `RimketTime.Apply(창, freeze)`가 `forcePause = freeze`(= `FreezeFor(battle)`)로 매 프레임(PreOpen/WindowUpdate/ExtraOnGUI) 갱신.
  Menu는 배틀 중 교체창일 때만 그 배틀의 `FreezeFor`를 따르고 그 밖의 화면은 멈추지 않는다. Battle/Double은 자기 battle의 `FreezeFor`.
- **시간 조작 시도 차단**: 정지 중 `GuardSpeed`가 `TickManager.CurTimeSpeed` 변경(속도 버튼·Space·1/2/3)을 감지해 되돌리고
  `Messages.Message(RejectInput)`(1초 간격, 문구 `RimketTime.FrozenMsg`, 영어는 `Languages/English/Rimketmon/ui.txt`).
- **입력 분리**: live 창은 `absorbInputAroundWindow=false`, 창 크기를 `RimketTime.WindowRect(true)`로 줄여
  아래 `BottomReserve`(200px)와 좌우를 비워 월드·시간 버튼을 누를 수 있게 함.
  `RimketTime.Track`(ExtraOnGUI)이 `Event.rawType==MouseDown` 위치로 `WorldFocus` 갱신(창 밖=true, 안=false).
  맨 위 live 창만 판정하고, 그 위에 `absorbInputAroundWindow` 창(체육관 룰렛·진화·연출·경고·이름 입력 등)이 떠 있으면 판정하지 않고 창 포커스로 둔다.
  `WorldFocus`일 때: 림켓몬 창 키 입력 무시(`PrWindow.Key`, 배틀창 `AcceptKey/CancelKey/MoveCursorKeys/Space`,
  온라인 코드 입력·채팅 T·센터 걷기), 열려 있던 채팅 입력은 닫히고(`OnlineChat.Draw`), 창이 35% 어두워짐(`DrawInactive`),
  `preventCameraMotion=false`(카메라 이동 가능).
- **동시 진행 안전장치**(시간이 흐르고 창 밖 클릭이 가능해져서 필요): `BattleFlow.RimketUiOpen()`이 true(림켓몬 창이 하나라도 떠 있음)이면
  림볼 던지기 배틀 시작(`TryStart`), 방문자 승부(`CanVisitorBattle`), 어둠의 배틀/???의 수락(`RimketChallengeComponent.Accept`)과
  24시간 강제창(`ShowForced` 호출)을 막거나 미루고, 화면의 림켓몬 아이콘(`PartyComponent`)으로 메뉴를 또 여는 것도 막는다
  (같은 종류 창이 겹쳐 앞의 배틀이 닫히는 것 방지).
- **설정**: `RimketmonSettings.liveWorldTime`(기본 true, 모드 설정 체크박스 + 인게임 설정 "림월드 시간 조작").
  끄면 예전 동작(전체 화면·항상 정지·월드 입력 차단)으로 완전 복귀. 창이 열린 채 바꿔도(인게임 설정은 메뉴 안에 있어 항상 이 경우)
  `Apply`가 forcePause·absorb·preventCamera·windowRect를 곧바로 다시 맞춘다.

## 전달물 · 적용 · 변경 파일
전달은 **zip 하나**(`Rimketmon-time-patch.zip`, 풀면 `Rimketmon-time-patch/` 폴더):
```
Rimketmon-time-patch/
├─ HANDOFF_time_input.md   이 문서 (같은 내용이 files/docs/ 에도 있다)
├─ time_input_patch.diff   원본(Rimketmon-main) 대비 전체 변경. 저장소 루트에서 patch -p1 < time_input_patch.diff (또는 git apply)
└─ files/                  바뀐 파일만, 저장소 기준 상대 경로 그대로
```
- 메인 AI 쪽 소스가 이 패치의 기준(원본 `Rimketmon-main`)과 **같으면 `files/` 안을 저장소에 그대로 덮어쓰면 끝**이다.
  그 사이 소스가 바뀌었으면 `time_input_patch.diff`를 `patch -p1`(또는 `git apply --3way`)로 병합하고, 충돌은 위 "시간 조작 구현"을 기준으로 손으로 맞출 것.
- 적용한 뒤 **반드시 다시 빌드**: `ModSource/build.sh <Managed경로>` (1.7 빌드는 `RW17`).

변경 파일(17개)
- 신규: `ModSource/RimketTime.cs`
- 수정(ModSource): `PokeUi.cs`(PrWindow), `Window_RimketMenu.cs`, `Window_RimketBattle.cs`, `Window_RimketDouble.cs`, `BattleFlow.cs`, `RimketChallenge.cs`,
  `PartyComponent.cs`, `RimketmonMod.cs`(설정), `Window_RimketMenu.Ranked.cs` / `.Tutorial.cs` / `.Online.cs` / `.Center.cs`, `OnlineChat.cs`
- 번역: `Rimketmon/Languages/English/Rimketmon/ui.txt`
- 테스트: `Tests/Program.cs`(주석만)
- 문서: `docs/HANDOFF_time_input.md`

## 튜닝 포인트
- 정지 대상 추가/제거: 해당 시작 함수에서 `RimketTime.MarkFrozen(battle)` 추가/삭제. 새 배틀 종류는 **기본이 동시 진행**(표시 안 하면 안 멈춤).
- 문구: `RimketTime.FrozenMsg`. 창 하단 여유: `RimketTime.BottomReserve`.
- 새 림켓몬 창을 live로 만들려면 `PrWindow` 상속 + `IRimketLive` 구현 + 필요 시 `FreezeTime` 오버라이드.
  새 키 입력은 `PrWindow.Key()`를 쓰거나 `RimketTime.KeysBlocked`를 확인할 것.
- 체육관 룰렛(`Window_RimketGym`, [배틀 개시] 뒤 몇 초)·진화·연출·경고는 예전처럼 항상 정지다(체육관 **배틀 자체**는 `Window_RimketBattle`이라 동시 진행).
  룰렛까지 동시 진행으로 하려면 `IRimketLive`를 구현하면 되지만, 룰렛이 끝나면 `Close()` 후 배틀을 여는 흐름이라 확인이 필요하다.

## ⚠ 미검증
이 환경엔 dotnet·림월드 DLL이 없어 **컴파일과 게임 내 테스트를 못 했다.** 저장소의 `Rimketmon/1.6|1.7/Assemblies/Rimketmon.dll`은 옛 코드 그대로이니
위 방법으로 다시 빌드해야 반영된다.

정적으로 확인한 것
- diff를 원본에 적용하면 수정본과 바이트 단위로 같다(17개 파일).
- 바뀐 `.cs` 15개의 괄호·중괄호 균형(원본 68개 파일로 보정한 간이 검사기), 지운 식별자(`rankedTimer`, `RankedTurnSeconds`, `AutoRankedAct` 등)가 남아 있지 않음.
- 새 멤버의 시그니처·필드·네임스페이스·override·접근 수준을 소스에서 대조(중복 override 없음).
- 기존 코드가 같은 형태로 쓰는 API(`Window.forcePause·absorbInputAroundWindow·preventCameraMotion·windowRect`, `Messages.Message(string, MessageTypeDef, bool)`,
  `MessageTypeDefOf.RejectInput`, `UI.screenWidth`, `Time.realtimeSinceStartup`·`Time.frameCount`, `Find.WindowStack.Windows`(+LINQ `ToList`), `Find.TickManager`,
  `Event.rawType`·`mousePosition`·`EventType.MouseDown`, `BaseContent.WhiteTex`)는 원본 소스에서 확인.
- **원본에 쓰인 적이 없어 기억에 의존한 API** — 컴파일이 안 되면 여기부터: `Window.ExtraOnGUI()`, `TickManager.CurTimeSpeed`(set)·`TimeSpeed`,
  `ConditionalWeakTable<,>`, `List<>.FindLastIndex`.

게임에서 확인할 것
1. 랭크 선출·배틀에서 초 단위 시계가 안 보이고, 오래 놔둬도 자동 선출·자동 행동이 일어나지 않는지. 모드 설정에 "랭크배틀 시간 제한"이 없는지. 랭크 상대의 "고르는 중" 연출은 그대로인지.
2. `Window.ExtraOnGUI`가 창 밖 클릭에도 호출되는지(포커스 전환이 안 되면 `WindowStack` 후킹으로 대체).
3. 야생·방문자·어둠·???·온라인에서 시간 버튼/Space를 눌렀을 때 속도가 되돌아가고 문구가 뜨는지. 정지 직후 한두 프레임 깜빡임 여부.
4. 체육관·랭크·패러독스·사냥에서 시간 버튼이 동작하는지, 그 사이 습격 등 이벤트가 와도 배틀 창이 안 깨지는지(특히 정착민이 `thrower`/`challenger`인 배틀).
5. 월드 클릭 후 Z/Enter/방향키가 림켓몬 창에 안 먹고, 창 클릭 후 다시 먹는지. 카메라 이동 여부.
6. 동시 진행 중 림볼 던지기·아이콘 클릭이 막히는지(안내 메시지/버저).
7. 작은 해상도에서 창이 너무 작지 않은지.
8. 온라인 채팅 입력 중 월드를 누르면 채팅 입력이 닫히고 월드 키(WASD 등)가 먹는지.
9. 메뉴 안 설정 "림월드 시간 조작"을 켜고 끌 때 창이 바로 작은 창↔전체 화면으로 바뀌고, 정지/동시 진행이 바뀌는지.
10. 메뉴·배틀 위에 진화·체육관 룰렛·경고 창이 떠 있는 동안 창 밖(아래쪽)을 눌러도 그 창이 어두워지거나 키가 막히지 않는지.
