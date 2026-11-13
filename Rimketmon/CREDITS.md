# 출처 / Credits (림켓몬)

제작: **RimGall**

비공식 팬 모드입니다. Nintendo / Creatures Inc. / GAME FREAK inc. / The Pokémon Company 와 관계가 없습니다. 원작 자료의 권리는 각 권리자에게 있습니다. 개별 자료의 라이선스는 확인하지 않았으며, 삭제 요청이 있으면 즉시 제거합니다.

## 그림·소리·데이터 (모드에 들어간 자료)

| 출처 | 어디에 썼나 | 변환 도구 |
|---|---|---|
| **PokeRogue** (pagefaultgames와 기여자들, 복원 소스·설치본) / **pokerogue-assets** | 포켓몬 애니메이션 스프라이트(앞/뒤)·이로치 1~3등급, 폼 종족 그림, 기술 연출 `battle-anims` 924개 + 공용 연출 50개와 그 효과음, 아레나 배경, UI·볼·일반 NPC 트레이너 스프라이트, 한글/영어 로케일(기술·특성·전투 메시지), 바이옴 등장표·포획률, 관장·사천왕·챔피언 파티(trainer-config), 효과음(audio/se), 울음소리(audio/cry), 사천왕·챔피언 BGM 21곡과 반복 지점, 메뉴 들판 BGM(meadow·forest·snowy_forest·swamp·lake), 센터(town)·랭크 선발(factory)·추가 관장(칼로스·가라르·팔데아)·트레이너(라이벌 1~3)·강한 야생 BGM, 진화 연출 | PackAnims.py, PackPokemonAnim.py, PackFormSprites.py, PackShiny.py, PackAssets.py, PackSummary.py, GenEnData.py, GenMsgData.py, GenHuntData.py, GenFormData.py, GenTrainerData.py, GenLeagueData.py, GenRankedTrainers.py |
| **Pokémon Feral F Sevii Update Gen9 Korean v0.4** (사용자 제공 파일) | 효과음(`Se/`, 느낌표 `exclaim`·슬롯 정지음 `slots_stop` 포함), 방문자 '!' 말풍선 그림만. (데이터·한글 이름·도구 아이콘·포켓몬 그림·BGM·상점 화면은 2026-11-13에 모두 다른 출처로 교체) | PackAssets.py |
| **Generation 9 Pack v3.3.8** (Eevee Expo, Caruban 외, https://eeveeexpo.com/resources/1101, 사용자 제공 파일) | 종족·기술·특성·도구 PBS 수치(Vanilla PBS (with updates) + Gen 9 Pack), 성별 비율, 배울 수 있는 기술, 진화 방식, 도구 아이콘(`Graphics/Items`), 지도 위 걷는 그림(Followers, 일반·이로치·폼), 포켓몬 키 데이터 | GenGameData.py, GenItemData.py, GenGenderData.py, GenLearnData.py, GenEvoData.py, PackFormFollowers.py |
| **pokefirered** (pret 디컴파일 프로젝트) | 포켓몬 센터·프렌들리숍·유니온룸(온라인 대기실) 맵·타일, 유니온룸 접수원, 간호사·점원·격투가(훈련 NPC) 스프라이트, 회복 연출 타이밍 | PackCenter.py → ModSource/CenterData.cs |
| **pokefirered** 신사(gentleman) 스프라이트 | 센터 이름 짓기 아저씨 | 직접 변환(팔레트 0 투명) |
| **PokeAPI** (github.com/PokeAPI/pokeapi `data/v2/csv`) | 한국어 종족·기술·특성·도구 이름과 특성·도구 설명, 도감 분류·키·몸무게·설명 | GenGameData.py, GenItemData.py, GenDexData.py |
| **Bulbagarden Archives** (archives.bulbagarden.net) | HGSS·Pt 트레이너 애니메이션(일목·카렌·충호·대엽) | APNG → 프레임 PNG + `Anims/trainer_frames.txt` |
| **github.com/coopscollectionbiz-debug/Poke-discord-bot** (`public/sprites/trainers_idle`) | 트레이너 애니메이션 GIF(시바·독수·들국화·대엽·태홍·갤럭시단 조무래기 남·여). 같은 저장소의 팬 제작 그림은 쓰지 않음 | GIF → 프레임 PNG + `Anims/trainer_frames.txt` |
| **Smogon 사용률 통계** (github.com/pkmn/smogon `data/stats/gen9*.json`, 2026-11-11) | 랭크 상대 파티 구성: 종족 사용률, 특성·도구·성격·기술·팀원 비율, 노력치 배분 | GenRankedPools.py → RankedPoolData.cs |
| **RimWorld** (Ludeon Studios) | 모드 대상 게임, 동물 그래픽·소리, 타이난 실베스터 배경(TynanCustom) | — |
| 사용자 제공 이미지 | 세레나 그림(`ui/serena.png`) | — |
| 모드 자체 제작 | 미싱노 그림(코드로 생성), 지도 글리치 연출, 프렌들리숍 배경·커서(코드로 생성) | — |

## 규칙·수치 참고 (코드·자산은 쓰지 않음)

- **포켓몬 챔피언스** 능력 포인트·훈련·랭크 규칙: ChampDex (champdex.com/guides/stat-points, /format-rules, /tools/vp-guide), Serebii (serebii.net/pokemonchampions/rankedbattle), Vandal (vandal.elespanol.com), BlueStacks 공략(수치 부정확할 수 있음). 2026-11-11 확인.
- **랭크 순위표 점수 분포**: battlenest.jp 포켓몬 챔피언스 싱글 최종 순위(M-5·M-6).
- **배틀 화면 구성**: 포켓로그 UI와 배틀 UI 참고 영상 (`docs/배틀UI_영상분석.md`).

## 참고만 하고 쓰지 않은 자료

- Complete-Fire-Red-Upgrade (CFRU), Dynamic-Pokemon-Expansion (DPE): 센터 제작 때 참고. 모드에 들어간 데이터 없음.
- Pokémon Battle Online 핸드오프 문서/소스(Showdown 서버 방식): 검토만 함. 코드 미사용.

## 기록 규칙

새 자료를 모드에 넣을 때마다 이 표에 추가한다: 자료 이름 / 출처 URL 또는 제공 파일 / 쓰임새 / 변환 도구.

- 어노말리 전용 배틀 배경(지하동굴·부자연적 어둠, `Anomaly/`): 포켓로그 cave·abyss 아레나 배경을 색만 바꿔 만든 것.
