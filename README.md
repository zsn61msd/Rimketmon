# RimKéMoN (림켓몬)

림월드(RimWorld) 1.6 / 1.7용 포켓몬풍 배틀 모드입니다. 림월드 동물을 잡아 키우고, 체육관·리그·랭크전·온라인 대전을 즐길 수 있습니다.

- 제작: **RimGall**
- **이 모드의 소스 코드는 Anthropic의 AI인 Claude(클로드)와 함께 만들었습니다.** 기획·방향·테스트는 RimGall이, 코드 작성 대부분은 Claude가 맡았습니다.

> 비공식 팬 모드입니다. Nintendo / Creatures Inc. / GAME FREAK inc. / The Pokémon Company와 관계가 없으며, 원작 자료의 권리는 각 권리자에게 있습니다. 그림·소리·데이터의 출처는 [CREDITS.md](CREDITS.md)에 정리되어 있습니다.

## 폴더 구조

| 폴더 | 내용 |
|---|---|
| `Rimketmon/` | 림월드 모드 본체. 이 폴더를 통째로 림월드 `Mods` 폴더에 넣으면 됩니다(1.6·1.7 DLL 포함) |
| `ModSource/` | 모드 C# 소스(림월드 연동, 화면, 온라인) |
| `Engine/` | 순수 C# 배틀 엔진(림월드 없이 동작) |
| `Tests/` | 엔진 테스트 |
| `Tools/` | 원본 자료를 모드용 그림·데이터로 바꾸는 스크립트 |
| `Training/` | 상대 AI 학습 환경 |
| `docs/` | 설계 메모·개발 기록 |

## 설치

1. `Rimketmon/` 폴더를 림월드 `Mods` 폴더에 복사합니다.
2. 게임의 모드 메뉴에서 **RimKéMoN**을 켜고 재시작합니다. DLC는 필요 없습니다(로열티가 있으면 정착민의 사이링크 레벨이 전투 능력치에 반영되고, 아노말리가 있으면 전용 콘텐츠가 추가됩니다).

## 빌드

- 엔진 테스트: `dotnet run --project Tests/Rimketmon.Tests.csproj`
- 모드 DLL: `ModSource/Rimketmon.Mod.csproj` (빌드할 때 `RimWorldManaged`에 림월드 `Managed` 폴더 경로를 지정)
  - 배포판은 `RIMKET_RELEASE`, 1.7용은 `RW17` 컴파일 기호를 씁니다.
- 배포 zip: `Tools/build_release.sh`

## 라이선스

소스 코드는 [GNU AGPL-3.0](LICENSE)으로 공개합니다(배틀 엔진 일부가 AGPL-3.0인 PokeRogue를 참고·이식). 그림·소리·데이터는 각 출처의 권리를 따르며 이 라이선스에 포함되지 않습니다.

## 출처

[CREDITS.md](CREDITS.md)를 참고하세요. PokeRogue, pokerogue-assets, Generation 9 Pack, pokefirered, PokeAPI, Smogon 통계 등 여러 공개 자료를 사용했습니다.
