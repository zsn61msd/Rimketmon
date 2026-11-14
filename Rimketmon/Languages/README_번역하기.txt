림켓몬 번역 안내 / How to translate Rimketmon
================================================

[한국어]
림켓몬의 원문은 한국어입니다. 다른 언어는 이 Languages 폴더 안의 글자 파일로 보여 줍니다.

새 언어 추가하기
 1. English 폴더를 통째로 복사해서 림월드 언어 폴더 이름으로 바꿉니다. (예: French, Japanese (日本語))
 2. 그 안의 Rimketmon/language.txt 에서 code(짧은 코드, 예: fr)와 name(설정에 보일 이름)을 고칩니다.
 3. Rimketmon/*.txt 의 "TAB 뒤쪽"만 번역합니다. 앞쪽(한국어 원문·키)은 고치지 마세요.
 4. 게임을 다시 켜면 모드 설정 > 언어 목록에 나타납니다. 림월드 언어 폴더 이름과 같으면 처음에 자동으로 골라집니다.
 5. 아이템·연구 같은 림월드 Def 글자는 림월드 방식대로 DefInjected 폴더에 넣습니다(Korean (한국어)/DefInjected 참고).

파일
 language.txt   code=, name=
 ui.txt         화면·전투 문구. 한국어 원문<TAB>번역. 긴 문장부터 맞춰 바꿉니다. 앞뒤 공백도 그대로 씁니다. 줄바꿈은 \n.
 ui_exact.txt   짧은 낱말. 문장 전체가 정확히 같을 때만 바꿉니다.
 patterns.txt   정규식 문장 틀. $1, $2 는 괄호로 잡은 부분.
 species.txt / moves.txt / abilities.txt / items.txt   키<TAB>이름[<TAB>설명]
 natures.txt    성격 25개(순서 그대로)
 messages.txt   포켓로그 전투 메시지. {{이름}} 자리는 그대로 둡니다.
빠진 줄은 영어 → 한국어 순서로 대신 보여 줍니다. 그래서 일부만 번역해도 됩니다.
species/moves/abilities/natures/messages 는 Tools/GenLangPack.py 로 포켓로그 로케일에서 만들 수 있습니다.

[English]
Rimketmon's source text is Korean. Other languages are shown through the text files in this Languages folder.
To add a language: copy the English folder, rename it to the RimWorld language folder name, edit Rimketmon/language.txt
(code, name), and translate only the part AFTER the TAB in Rimketmon/*.txt. Missing lines fall back to English, then Korean.
Def texts (items, research...) go in DefInjected as usual for RimWorld (see Korean (한국어)/DefInjected).
