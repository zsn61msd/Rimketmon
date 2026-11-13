using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 언어(한국어/영어). 종족·기술·특성·성격·PokeRogue 번역 메시지는 EnData(영어 번역 원본)에서, 직접 쓴 UI/전투 문구는 아래 사전에서 가져온다.
    /// 사전은 문장 조각 치환이다(긴 조각 먼저). 사전에 없는 문구는 한국어로 남는다.
    /// </summary>
    public static class Lang
    {
        public static bool English { get; private set; }
        public static event Action Changed;

        public static void Set(bool english)
        {
            if (English == english && applied) return;
            English = english; applied = true;
            SpeciesCatalog.ApplyLanguage(); MoveCatalog.ApplyLanguage(); AbilityCatalog.ApplyLanguage(); Msgs.ResetTable();
            if (Changed != null) Changed();
        }
        private static bool applied;

        // ---- 영어 이름/설명 조회 (id는 영문 소문자만 남긴 키)
        public static string Key(string id)
        {
            var sb = new StringBuilder();
            foreach (char c in id.ToLowerInvariant()) if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
            string k = sb.ToString();
            string alias;
            return KeyAlias.TryGetValue(k, out alias) ? alias : k;
        }
        /// <summary>게임 데이터 id와 영어 표의 키가 다른 항목.</summary>
        private static readonly Dictionary<string, string> KeyAlias = new Dictionary<string, string>
        {
            { "nidoranfe", "nidoranf" }, { "nidoranma", "nidoranm" },
            { "asonechillingneigh", "asoneglastrier" }, { "asonegrimneigh", "asonespectrier" }, { "battlebond2", "battlebond" },
            { "embodyaspect", "embodyaspectteal" }, { "embodyaspect1", "embodyaspectwellspring" }, { "embodyaspect2", "embodyaspecthearthflame" }, { "embodyaspect3", "embodyaspectcornerstone" },
        };
        private static Dictionary<string, string[]> Build(string[] rows)
        {
            var d = new Dictionary<string, string[]>();
            foreach (var r in rows) { var f = r.Split('|'); d[f[0]] = f; }
            return d;
        }
        private static Dictionary<string, string[]> sp, mv, ab;
        public static string[] EnSpecies(string id) { if (sp == null) sp = Build(EnData.Species); string[] r; return sp.TryGetValue(Key(id), out r) ? r : null; }
        public static string[] EnMove(string id) { if (mv == null) mv = Build(EnData.Moves); string[] r; return mv.TryGetValue(Key(id), out r) ? r : null; }
        public static string[] EnAbility(string id) { if (ab == null) ab = Build(EnData.Abilities); string[] r; return ab.TryGetValue(Key(id), out r) ? r : null; }

        // ---- 문장 조각 사전 ------------------------------------------------
        private static readonly string[] Pairs =
        {
            // 속성/상태
            "노말","Normal", "불꽃","Fire", "물","Water", "풀","Grass", "전기","Electric", "얼음","Ice", "격투","Fighting", "독","Poison", "땅","Ground", "비행","Flying",
            "에스퍼","Psychic", "벌레","Bug", "바위","Rock", "고스트","Ghost", "드래곤","Dragon", "악","Dark", "강철","Steel", "페어리","Fairy",
            "화상","Burn", "마비","Paralysis", "잠듦","Sleep", "맹독","Toxic", "속성","Type",
            // 능력치
            "스테이터스","Stats", "특수공격","Sp. Atk", "특수방어","Sp. Def", "스피드","Speed", "공격","Attack", "방어","Defense", "명중","Accuracy", "위력","Power", "분류","Category",
            "최대 HP","Max HP", "정신감응력","Psychic sensitivity", "초능력 레벨","Psychic level", "전투 레벨","Battle level", "총 스탯 / 각 스탯","Total / each stat",
            // 메뉴
            "파티","Party", "센터","Center", "상점","Shop", "체육관","Gym", "랭크전","Ranked", "설정","Settings", "닫기","Close", "개발자","Developer", "뒤로","Back",
            "림켓몬","Rimketmon", "정착민","Colonist", "정착지","Colony", "관장","Leader", "배지","Badges", "티어","Tier", "레벨","Level", "기술","Moves", "도전자","Challenger",
            "교체한다","Send out", "능력치를 본다","Summary", "그만둔다","Cancel", "위로 이동","Move up", "아래로 이동","Move down", "볼에서 꺼낸다","Release from ball", "꺼내기","Release",
            "무엇을 할까?","What will you do?", "도망칠 수 없습니다.","You can't flee.", "포획한다","Capture", "처치한다","Defeat", "놓아준다","Release", "성공률 ","Success rate ",
            "접속 해제","Disconnect", "승리","Victory", "패배","Defeat", "상대","Foe", "야생","Wild", "기본 공격","Basic attack", "발버둥","Struggle",
            "어버이/","OT/", "타입/","Type/", "현재 경험치","Exp. Points", "다음 레벨까지","To Next Lv.", "지닌 도구가 없습니다 (클릭 / Enter: 도구 주기)","No held item (Click / Enter: give item)",
            "아직 포획한 림켓몬이 없습니다. 림볼을 야생 동물에게 던져 보세요.","You haven't caught any Rimketmon yet. Throw a Rimball at a wild animal.",
            "림켓몬 센터","Rimketmon Center", "림켓몬 상점","Rimketmon Shop", "관장 도전","Gym Challenge", "모두 치료하기","Heal all",
            "맡기신 림켓몬의 HP와 PP를 전부 회복해 드립니다. 비용은 없습니다.","We'll fully restore HP and PP of your Rimketmon. Free of charge.",
            "림켓몬의 HP와 PP를 전부 회복합니다. 비용은 없습니다.","Fully restores HP and PP. Free of charge.",
            "파티를 확인하고 순서를 바꾸거나 림켓몬을 볼에서 꺼냅니다.","Check your party, reorder it, or release Rimketmon from their balls.",
            "은으로 림볼을 구매합니다. 보급 포드로 투하됩니다.","Buy Rimballs with silver. They arrive by supply pod.",
            "은으로 구매합니다. 물건은 보급 포드로 투하됩니다.","Buy with silver. Goods arrive by supply pod.",
            "이세계 림켓몬 트레이너에게 도전합니다. 상대는 추첨으로 정해집니다.","Challenge a parallel-world Rimketmon trainer. The opponent is drawn at random.",
            "랭크전은 준비 중입니다.","Ranked mode is coming soon.", "준비 중입니다.","Coming soon.", "전투 화면과 소리 설정을 바꿉니다.","Change battle display and sound settings.", "메뉴를 닫습니다.","Close the menu.",
            "디버그용: 원하는 관장/종족과 바로 배틀합니다.","Debug: battle any leader/species right away.",
            "치료가 필요한 림켓몬 ","Rimketmon needing care: ", "림켓몬을 모두 회복시켰다!","All Rimketmon were healed!",
            "클릭 / Enter: 구매   ·   뒤로: 우클릭 / Esc","Click / Enter: buy   ·   Back: right-click / Esc", "뒤로: 우클릭 / Esc","Back: right-click / Esc", "닫기: 우클릭 / Esc","Close: right-click / Esc",
            "↑↓ 항목  ←→ 값 변경   ·   뒤로: 우클릭 / Esc","↑↓ item  ←→ change   ·   Back: right-click / Esc",
            // 설정
            "■ 규칙","■ Rules", "■ 배틀 연출","■ Battle display", "■ 소리","■ Sound",
            "패배 시 사망","Death on defeat", "패배 시 혼수·중증 손상","Coma + severe injury on defeat",
            "승리 후 포획 항상 성공 (끄면 티어/야생성에 따른 확률, 실패 시 도주)","Capture always succeeds after victory (off: chance by tier/wildness, fails → flees)",
            "티어 상향 초과분을 최대 티어로 고정 (끄면 기본 티어로 복귀)","Clamp tier-upgrade overflow to max tier (off: back to base tier)",
            "패배(림켓몬 전멸 후 정착민 HP 0) 시 정착민 사망 — 끄면 혼수 + 중증 영구 손상","Colonist dies on defeat (all Rimketmon down, colonist HP 0) — off: coma + severe permanent injury",
            "전투에 데려가는 림켓몬 수: ","Rimketmon brought to battle: ",
            "게임 속도 (메시지/연출 속도)","Game speed (message/effect speed)", "게임 속도","Game speed", "HP 바 속도","HP bar speed", "메시지 넘김","Message advance", "트레이너 그림","Trainer art", "어둠의 배틀·관장·사천왕·챔피언·랭크 상위권(1800점 이상)은 학습된 AI 사용","Learned AI for dark battles, gyms, Elite Four, champions and top ranked (1800+)", "도트","Pixel", "일러스트","Artwork", "트레이너 공식 일러스트 사용","Use official trainer artwork",
            "메시지를 클릭해야 다음으로 넘어가기","Click to advance messages", "피해 숫자 표시","Show damage numbers", "상대 HP 수치 표시(HP 바 옆, 실시간)","Show foe HP numbers (next to HP bar, live)", "싸운다","Fight", "아이템","Items", "도망친다","Run", "사용할 수 있는 아이템이 없다.","No usable items.", "피해 숫자","Damage numbers",
            "기술 애니메이션(피격 흔들림/번쩍임)","Move animations (shake/flash)", "기술 애니메이션","Move animations",
            "기술 버튼에 상성 힌트(▲굉장 ▼별로) 표시","Show effectiveness hints on move buttons (▲ ▼)", "기술 버튼에 위력/명중/PP 표시","Show power/accuracy/PP on move buttons",
            "배틀 진입 시 화면 울렁임 연출","Screen-ripple transition on battle start", "배틀 BGM 재생","Play battle BGM", "배틀 효과음 재생","Play battle sound effects",
            "전체 볼륨","Master volume", "BGM 볼륨","BGM volume", "효과음 볼륨","SFX volume", "전체 음량","Master volume", "BGM 음량","BGM volume", "효과음 음량","SFX volume",
            "느림","Slow", "보통","Normal", "매우 빠름","Very fast", "빠름","Fast", "터보","Turbo", "즉시","Instant", "자동","Auto", "켬","On", "끔","Off", "클릭","Click", "기본","Default",
            "언어","Language",
            "피격 연출","Hit effects", "상성 표시","Type hints", "기술 정보","Move info", "상대 기술 표시","Show foe moves", "진입 연출","Intro effect", "속도 ","Speed ",
            "메시지를 클릭","Click message", "결과 확인","Confirm result", "선택","Select", "취소","Cancel",
            // 상점/기타
            "림볼이 없다.","You have no Rimball.", "야생 동물을 클릭하세요.","Click a wild animal.",
            "야생 동물이나 우리 정착지의 동물에게만 던질 수 있다.","Can only be thrown at wild animals or animals of your colony.",
            "초능력 레벨 1 이상이어야 림볼을 쓸 수 있다.","Psychic level 1+ is required to use a Rimball.",
            "개  ·  관장 ","  ·  Leaders ", "개 — 야생 레벨 상한 ","  — wild level cap ", "  ·  보유 림켓몬 ","  ·  Rimketmon owned: ", "개  ·  야생 레벨 ","   ·  Wild Lv. ", "마리","",
            // 전투 로그 문장
            "은(는) 쓰러졌다!"," fainted!", "은(는) 도망쳤다..."," fled...", "을(를) 내보냈다!"," was sent out!", "가라! ","Go! ", ", 돌아와!",", come back!",
            "이(가) 승부를 걸어왔다!"," wants to battle!", "이(가) 나타났다! ("," appeared! (", "이(가) 직접 싸우러 나섰다! (HP "," steps up to fight in person! (HP ",
            "의 의식이 직접 전장에 투입된다! (HP","'s mind is thrown into the field! (HP", "의 의식이 무너졌다...","'s mind collapsed...", "의 의식에 ","'s mind was hit by ",
            "은(는) 경험치 "," gained ", "을(를) 얻었다!"," Exp!", "을(를) 포획했다!"," was captured!", "을(를) 처치했다."," was defeated.", "을(를) 놓아주었다."," was released.",
            "포획에 실패했다! ","Capture failed! ", "은(는) 독에 걸렸다!"," was poisoned!", "은(는) 맹독에 걸렸다!"," was badly poisoned!", "은(는) 화상을 입었다!"," was burned!",
            "은(는) 마비되어 기술이 나오기 어려워졌다!"," is paralyzed! It may be unable to move!", "은(는) 잠들어 버렸다!"," fell asleep!", "은(는) 얼어붙었다!"," was frozen solid!",
            "은(는) 혼란에 빠졌다!"," became confused!", "은(는) 혼란에 빠져 있다!"," is confused!", "의 혼란이 풀렸다!"," snapped out of confusion!", "은(는) 눈을 떴다!"," woke up!",
            "의 얼음이 녹았다!","'s ice melted!", "의 마비가 풀렸다!","'s paralysis wore off!", "의 상태이상이 회복되었다!","'s status was cured!",
            "은(는) 몸이 저려서 움직일 수 없다!"," is paralyzed! It can't move!", "은(는) 얼어붙어서 움직일 수 없다!"," is frozen solid!", "은(는) 쿨쿨 잠들어 있다."," is fast asleep.",
            "은(는) 반동으로 움직일 수 없다!"," must recharge!", "은(는) 풀이 죽어 움직일 수 없었다!"," flinched and couldn't move!", "은(는) 힘을 모으고 있다!"," is gathering power!",
            "은(는) 모습을 감췄다!"," vanished!", "은(는) 조여졌다!"," was squeezed!", "은(는) 조이기에서 벗어났다!"," broke free of the bind!", "은(는) 조이기에서 풀려났다!"," was released from the bind!",
            "은(는) 더 이상 도망칠 수 없다!"," can no longer escape!", "은(는) 튼튼한 몸으로 버텼다!"," endured the hit!", "은(는) 폭발했다!"," exploded!",
            "은(는) 기세가 지나쳐 땅에 부딪혔다!"," kept going and crashed!", "은(는) 게으름을 피우고 있다!"," is loafing around!",
            "은(는) 떠 있어서 땅 기술이 맞지 않았다!"," is floating, so Ground moves miss!", "영문도 모른 채 자신을 공격했다!"," hurt itself in its confusion!",
            "의 공격은 빗나갔다!","'s attack missed!", "급소에 맞았다!","A critical hit!", "번 맞았다!"," times!",
            "효과가 굉장했다!","It's super effective!", "효과가 매우 굉장했다!","It's extremely effective!", "효과가 별로인 듯하다...","It's not very effective...",
            "효과가 매우 별로인 듯하다...","It's barely effective...", "효과가 없는 것 같다...","It doesn't seem to affect the target...", "하지만 실패했다!","But it failed!",
            "에게는 효과가 없는 것 같다...","... It doesn't seem to affect it...",
            "의 체력을 흡수했다! ","'s HP was drained! ", "의 체력이 회복되었다! (HP","'s HP was restored! (HP", "의 피해를 입었다! (HP"," took damage! (HP", "의 피해! (HP"," damage! (HP",
            "반동으로 "," recoil: ", "독으로 ","Poison: ", "맹독으로 ","Toxic: ", "화상으로 ","Burn: ", "악몽으로 ","Nightmare: ", "조이기로 ","Bind: ",
            "의 헤롱헤롱 물질을 들이마셨다!"," inhaled the oozing liquid!",
            "의 능력 변화가 원래대로 돌아왔다!","'s stat changes were reset!",
            "접속 해제에 실패했다!","Failed to disconnect!", "신경 접속을 해제했다.","Neural link disconnected.", "강제 접속 해제! 신경에 충격이 남았다.","Forced disconnect! The shock lingers in the nerves.",
            "더 이상 싸울 수 있는 존재가 없다...","There is nothing left that can fight...", "림켓몬이 모두 쓰러졌다! ","All Rimketmon fainted! ",
            "와의 승부에서 이겼다!"," was defeated in the battle!", "에게 이겼다! 배지를 얻어 레벨 상한이 "," was beaten! Badge earned — level cap raised to ", "로 올랐다. 이제 야생 동물이 더 강하게 나온다.",". Wild animals get stronger now.",
            "에게 도전한다!"," is challenged!", "에게 패배해 목숨을 잃었다."," defeated you — you lost your life.",
            "불꽃 기술의 위력이 올랐다!","Fire moves got stronger!", "의 프레셔","'s Pressure", "은(는) 프레셔를 내뿜고 있다!"," is exerting its Pressure!",
            "은(는) 틀을 깨는 보디를 지니고 있다!"," has a mold-breaking body!", "은(는) 불타오르는 오라를 뿜고 있다!"," is radiating a blazing aura!", "은(는) 작렬하는 오라를 뿜고 있다!"," is radiating a burning aura!",
            " 덕분에 위협이 통하지 않았다!","'s ability blocked the intimidation! ", " 덕분에 효과가 없었다!"," made it ineffective!", " 덕분에 "," thanks to ",
            "의 승기가 발동했다!","'s Competitive activated!", "의 오기가 발동했다!","'s Defiant activated!",
            // 능력치 변화
            "의 특수공격이","'s Sp. Atk", "의 특수방어가","'s Sp. Def", "의 공격이","'s Attack", "의 방어가","'s Defense", "의 스피드가","'s Speed", "의 명중률이","'s accuracy", "의 회피율이","'s evasiveness",
            "의 ","'s ", "하지만 ","But ",
            " 더 이상 오르지 않는다!"," won't go any higher!", " 더 이상 떨어지지 않는다!"," won't go any lower!", " 떨어지지 않는다!"," won't be lowered!",
            "매우 크게 올랐다!"," rose drastically!", "크게 올랐다!"," rose sharply!", "올랐다!"," rose!", "매우 크게 떨어졌다!"," severely fell!", "크게 떨어졌다!"," harshly fell!", "떨어졌다!"," fell!",
            "명중률","accuracy", "회피율","evasiveness", "★특이 개체"," ★ Special", " / 티어 "," / Tier ",
            // 기타
            "은(는) ","", "이(가) ","", "을(를) ","", "와(과) ","", "(으)로 ","", "에게 ","",
            "어떻게 할까?","What will you do?",
        };
        // 엔진 확장(전체 기술·특성 구현)에서 새로 생긴 전투 문장
        private static readonly string[] Pairs2 =
        {
            " 덕분에 명중률이 떨어지지 않는다!"," keeps its accuracy from dropping!",
            " 쪽 발밑에 독압정이 흩뿌려졌다!","'s side was scattered with Toxic Spikes!",
            " 쪽은 선제 기술을 받지 않는다!","'s side is protected from priority moves!",
            " 효과가 사라졌다."," effect wore off.",
            "정체가 드러났다!","true identity was revealed!", "수면","Sleep", "빙결","Freeze",
            "도구 주기 — ","Give Item — ", "가방에 도구가 없습니다. 상점에서 구매하세요.","No items in the bag. Buy some at the shop.",
            "도구 빼기","Take item", "림켓몬 상점 — 도구","Rimketmon Shop — Items", "림볼","Rimket Ball", "도구","Items",
            "은으로 구매하면 가방에 들어갑니다. 요약 화면에서 림켓몬에게 줄 수 있습니다.","Buy with silver; items go into the bag. Give them to a Rimketmon from the summary screen.",
            "구매 완료","purchased", "은이 부족합니다","Not enough silver",
            "HP가 부족해서 대타를 만들 수 없다!","Not enough HP to make a substitute!",
            "날씨와 필드의 영향이 모두 사라졌다!","The effects of weather and terrain disappeared!",
            "날씨의 영향이 사라졌다!","The effects of weather disappeared!",
            "누구도 도망칠 수 없게 되었다!","No one can escape now!",
            "눈이 그쳤다.","The snow stopped.",
            "눈이 내리기 시작했다!","It started to snow!",
            "도발당해서 변화 기술을 쓸 수 없다!"," can't use status moves because of the taunt!",
            "리플렉터","Reflect",
            "빛의장막","Light Screen",
            "오로라베일","Aurora Veil",
            "신비의베일","Safeguard",
            "흰안개","Mist",
            "순풍","Tailwind",
            "행운의 주문","Lucky Chant",
            "퀵가드","Quick Guard",
            "와이드가드","Wide Guard",
            "크래프티실드","Crafty Shield",
            "판뒤집기","Mat Block",
            "매그니튜드 ","Magnitude ",
            "매직룸이 원래대로 돌아왔다!","Magic Room wore off!",
            "멸망의 노래! 3턴 뒤에 쓰러진다!","Perish Song! It will faint in 3 turns!",
            "모든 능력 변화가 원래대로 돌아왔다!","All stat changes were eliminated!",
            "모래바람으로 ","Sandstorm: ",
            "모래바람이 가라앉았다.","The sandstorm subsided.",
            "모래바람이 몰아치고 있다.","The sandstorm rages.",
            "모래바람이 불기 시작했다!","A sandstorm kicked up!",
            "무아지경 때문에 처음 고른 기술밖에 쓸 수 없다!"," can only use the first move it chose because of Gorilla Tactics!",
            "물놀이 효과가 사라졌다.","Water Sport's effect wore off.",
            "물리 공격에 강해졌다!","Physical attacks will be weaker!",
            "물리 방어 벽이 사라졌다.","The physical barrier disappeared.",
            "물의 베일로 ","Aqua Ring: ",
            "발밑에 안개가 자욱해졌다!","Mist swirled around the battlefield!",
            "발밑에 전기가 흐르기 시작했다!","An electric current ran across the battlefield!",
            "발밑에 풀이 무성해졌다!","Grass grew to cover the battlefield!",
            "발밑이 이상한 느낌으로 가득 찼다!","The battlefield got weird!",
            "방어와 특수방어가 뒤바뀌는 공간이 생겼다!","A bizarre area was created where Defense and Sp. Def are swapped!",
            "배고픈 모양","Hangry Mode",
            "배부른 모양","Full Belly Mode",
            "벽이 부서졌다!","The barrier was shattered!",
            "봉인당해서 쓸 수 없다!"," can't use the move because it was imprisoned!",
            "불꽃 기술의 위력이 약해졌다!","Fire's power was weakened!",
            "비가 그쳤다.","The rain stopped.",
            "비가 내리기 시작했다!","It started to rain!",
            "뾰족한 바위가 ","Stealth Rock: ",
            "압정이 ","Spikes: ",
            "뿌리로 ","Ingrain: ",
            "사슬묶기로 봉인되었다!","was disabled!",
            "상대 발밑에 끈적끈적네트가 깔렸다!","A sticky web was laid beneath the foe's feet!",
            "상대 발밑에 독압정이 뿌려졌다!","Toxic Spikes were scattered beneath the foe's feet!",
            "상대 발밑에 압정이 뿌려졌다!","Spikes were scattered beneath the foe's feet!",
            "상대 주위에 뾰족한 바위가 떠올랐다!","Pointed stones float in the air around the foe!",
            "상대 파티가 비어 있다.","The foe's party is empty.",
            "서로 도망칠 수 없게 되었다!","Neither can escape now!",
            "서로의 HP를 나눴다!","The battlers shared their HP!",
            "서로의 공격 능력을 나눴다!","The battlers shared their offensive stats!",
            "서로의 공격 변화를 바꿨다!","The battlers swapped their offensive stat changes!",
            "서로의 능력 변화를 바꿨다!","The battlers swapped their stat changes!",
            "서로의 방어 능력을 나눴다!","The battlers shared their defensive stats!",
            "서로의 방어 변화를 바꿨다!","The battlers swapped their defensive stat changes!",
            "서로의 스피드를 바꿨다!","The battlers swapped their Speed!",
            "서로의 특성을 바꿨다!","The battlers swapped their abilities!",
            "서로의 특성이 바뀌었다!","The battlers swapped their abilities!",
            "소금절임으로 ","Salt Cure: ",
            "수상한 바람이 그쳤다.","The mysterious air current has dissipated.",
            "수상한 바람이 불어닥쳤다!","A mysterious air current is protecting Flying-type Pokémon!",
            "순풍이 멈췄다.","The tailwind petered out.",
            "순풍이 불기 시작했다!","A tailwind blew from behind!",
            "습기 때문에 폭발할 수 없다!","It can't explode because of the dampness!",
            "신비의베일이 사라졌다.","Safeguard wore off.",
            "신비의베일이 아군을 감쌌다!","Safeguard cloaked the team!",
            "싸라기눈으로 ","Hail: ",
            "싸라기눈이 그쳤다.","The hail stopped.",
            "싸라기눈이 내리고 있다.","Hail continues to fall.",
            "싸라기눈이 내리기 시작했다!","It started to hail!",
            "씨앗에게 체력을 빼앗겨 ","Leech Seed: ",
            "양쪽 모두 3턴 후에 쓰러진다!","Both will faint in 3 turns!",
            "양쪽의 벽이 모두 사라졌다!","The barriers on both sides disappeared!",
            "양쪽의 상태가 뒤바뀌었다!","The battlers' conditions were swapped!",
            "엄청난 비가 내리기 시작했다!","A heavy rain began to fall!",
            "에게 고스트 타입이 더해졌다!"," became part Ghost type!",
            "에게 셀들이 모여들었다! 퍼펙트폼으로 변했다!"," was joined by its cells! It changed to Complete Forme!",
            "에게 씨앗이 심어졌다!"," was seeded!",
            "에게 저주를 걸었다!"," was cursed!",
            "에게 풀 타입이 더해졌다!"," became part Grass type!",
            "에게는 이미 대타가 있다!"," already has a substitute!",
            "오로라베일이 사라졌다.","Aurora Veil wore off.",
            "오로라베일이 아군을 감쌌다!","Aurora Veil cloaked the team!",
            "와이드가드가 발동했다!","Wide Guard protected the team!",
            "원더룸이 원래대로 돌아왔다!","Wonder Room wore off!",
            "은(는) 가루가 폭발해서 불꽃 기술을 쓸 수 없다!"," can't use Fire moves because of the powder!",
            "은(는) 가루투성이가 되었다!"," was covered in powder!",
            "은(는) 공격과 방어를 뒤바꿨다!"," swapped its Attack and Defense!",
            "은(는) 공격으로부터 몸을 지켰다!"," protected itself!",
            "은(는) 공중에 떠올랐다!"," was lifted into the air!",
            "은(는) 기술을 되받아쳤다!"," bounced the move back!",
            "은(는) 기합을 넣었다!"," is getting pumped!",
            "은(는) 긴장해서 나무열매를 먹을 수 없다!"," is too nervous to eat Berries!",
            "은(는) 길동무를 노리고 있다!"," is trying to take its foe down with it!",
            "은(는) 끈적끈적네트에 걸렸다!"," was caught in a sticky web!",
            "은(는) 끌려 나왔다!"," was dragged out!",
            "은(는) 높이 붙잡혔다!"," was taken high into the sky!",
            "은(는) 다시 껍질을 썼다!"," put its shell back on!",
            "은(는) 달마모드로 변했다!"," triggered Zen Mode!",
            "은(는) 도발에 넘어갔다!"," fell for the taunt!",
            "은(는) 되살아났다!"," came back to life!",
            "은(는) 땅에 뿌리를 내렸다!"," planted its roots!",
            "은(는) 땅으로 떨어졌다!"," fell straight down!",
            "은(는) 마법의 막으로 몸을 감쌌다!"," shrouded itself with a magic coat!",
            "은(는) 마이티폼으로 변했다!"," transformed into its Hero Form!",
            "은(는) 마지막에 움직이게 되었다!"," will move last!",
            "은(는) 먹이를 물었다!"," caught its prey!",
            "은(는) 먼저 움직이게 되었다!"," will move first!",
            "은(는) 몸이 떨리기 시작했다!"," shuddered!",
            "은(는) 무리를 이루었다!"," formed a school!",
            "은(는) 물 타입이 되었다!"," became Water type!",
            "은(는) 물의 베일을 둘렀다!"," surrounded itself with a veil of water!",
            "은(는) 미래를 향해 공격을 날렸다!"," foresaw an attack!",
            "은(는) 미래의 공격을 받았다!"," took the future attack!",
            "은(는) 미스트필드에 지켜지고 있다!"," is protected by the mist!",
            "은(는) 방어 태세에 들어갔다!"," braced itself!",
            "은(는) 버텼다!"," endured the hit!",
            "은(는) 버틸 각오를 했다!"," braced itself!",
            "은(는) 봉인되었다!"," was sealed!",
            "은(는) 봉인을 썼다!"," used Imprison!",
            "은(는) 불에 약해졌다!"," became weaker to fire!",
            "은(는) 블레이드폼으로 변했다!"," changed to Blade Forme!",
            "은(는) 사이코필드에 지켜지고 있다!"," is protected by the weird terrain!",
            "은(는) 상대와 같은 타입이 되었다!"," became the same type as the foe!",
            "은(는) 상대의 기술을 가로챌 태세다!"," is ready to snatch the foe's move!",
            "은(는) 상대의 능력 변화를 복사했다!"," copied the foe's stat changes!",
            "은(는) 상대의 능력 변화를 훔쳤다!"," stole the foe's stat changes!",
            "은(는) 소금에 절여졌다!"," is being salt cured!",
            "은(는) 소란에 잠에서 깼다!"," woke up in the uproar!",
            "은(는) 소란을 피우기 시작했다!"," caused an uproar!",
            "은(는) 소원을 빌었다!"," made a wish!",
            "은(는) 소원의 힘으로 완전히 회복되었다!"," was fully restored by the wish!",
            "은(는) 시공을 뒤틀었다!"," twisted the dimensions!",
            "은(는) 신비의베일에 지켜지고 있다!"," is protected by Safeguard!",
            "은(는) 신비의부적에 지켜지고 있다!"," is protected by Mist!",
            "은(는) 실드폼으로 변했다!"," changed to Shield Forme!",
            "은(는) 썰렁한 농담을 던졌다!"," made a chilling joke!",
            "은(는) 쓰러지며 소원을 빌었다!"," fainted and made a wish!",
            "은(는) 쓰러진 동료들의 힘을 받았다!"," gained strength from the fallen!",
            "은(는) 아로마베일에 지켜지고 있다!"," is protected by an aromatic veil!",
            "은(는) 악몽을 꾸기 시작했다!"," began having a nightmare!",
            "은(는) 앙코르를 받았다!"," received an encore!",
            "은(는) 에스퍼 타입이 되었다!"," became Psychic type!",
            "은(는) 옥죄어졌다!"," was wrapped up tight!",
            "은(는) 움직일 수 없다!"," can't move!",
            "은(는) 원래 모습으로 돌아왔다!"," returned to its original form!",
            "은(는) 원한을 품었다!"," bears a grudge!",
            "은(는) 일렉트릭필드 때문에 잠들지 않는다!"," can't fall asleep because of the electric terrain!",
            "은(는) 자신의 HP를 깎아 대타를 만들었다!"," put in a substitute!",
            "은(는) 잠들어서 건강해졌다!"," slept and became healthy!",
            "은(는) 전자기력으로 떠올랐다!"," levitated with electromagnetism!",
            "은(는) 정신을 집중했다!"," is focusing its mind!",
            "은(는) 졸음이 쏟아지기 시작했다!"," made the foe drowsy!",
            "은(는) 지시를 받고 다시 움직인다!"," moves again on instruction!",
            "은(는) 집중이 흐트러져 기술을 쓸 수 없다!"," lost its focus and couldn't move!",
            "은(는) 충전되었다!"," became charged!",
            "은(는) 충전을 시작했다!"," began charging power!",
            "은(는) 컨디션이 좋지 않다!"," can't get it going!",
            "은(는) 테라스탈 폼으로 변했다!"," transformed into its Terastal Form!",
            "은(는) 텔레키네시스에서 풀려났다!"," was freed from the telekinesis!",
            "은(는) 트집을 잡혔다!"," was subjected to torment!",
            "은(는) 플라워베일에 지켜지고 있다!"," is protected by the flower veil!",
            "은(는) 헤롱헤롱 상태다!"," is in love!",
            "은(는) 헤롱헤롱해서 움직일 수 없다!"," is immobilized by love!",
            "은(는) 헤롱헤롱해졌다!"," fell in love!",
            "은(는) 황금몸 덕분에 변화 기술이 통하지 않는다!","'s Good as Gold blocks status moves!",
            "은(는) 회복을 봉쇄당했다!"," was prevented from healing!",
            "은(는) 힘을 모으기 시작했다!"," is getting pumped!",
            "의 PP가 0이 되었다!","'s PP dropped to 0!",
            "의 PP가 줄었다!","'s PP was reduced!",
            "의 기술은 빗나갔다!","'s move missed!",
            "의 기술이 전기 타입이 되었다!","'s moves became Electric type!",
            "의 껍질이 깨졌다!","'s shell broke!",
            "의 꿈을 먹었다! ","'s dream was eaten! ",
            "의 능력 변화가 뒤집혔다!","'s stat changes were inverted!",
            "의 대타가 나타났다!","'s substitute appeared!",
            "의 대타가 대신 맞았다!","'s substitute took the hit!",
            "의 대타는 쓰러졌다!","'s substitute faded!",
            "의 대타에게 막혔다!","'s substitute blocked the move!",
            "의 도발이 풀렸다!","'s taunt wore off!",
            "의 무리가 흩어졌다!","'s school scattered!",
            "의 방어를 뚫었다!","'s protection was broken through!",
            "의 불꽃이 꺼졌다!","'s fire went out!",
            "의 비축이 사라졌다!","'s stockpile faded!",
            "의 사슬묶기가 풀렸다!","'s disable wore off!",
            "의 소원이 이루어졌다!","'s wish came true!",
            "의 앙코르가 풀렸다!","'s encore ended!",
            "의 얼음 머리가 대신 맞았다!","'s ice face took the hit!",
            "의 얼음 머리가 원래대로 돌아왔다!","'s ice face was restored!",
            "의 전기가 사라졌다!","'s electricity dissipated!",
            "의 전자부유가 끝났다!","'s magnetic levitation wore off!",
            "의 정체를 꿰뚫어 봤다!"," was identified!",
            "의 퀵드로!","'s Quick Draw!",
            "의 탈이 대신 맞았다!","'s disguise took the hit!",
            "의 특성이 사라졌다!","'s ability disappeared!",
            "의 특성이 효과를 잃었다!","'s ability was suppressed!",
            "의 회복봉인이 풀렸다!","'s heal block wore off!",
            "이 기술은 아직 구현되지 않았다!","This move isn't implemented yet!",
            "이(가) 독압정을 흡수했다!"," absorbed the Toxic Spikes!",
            "이상한 공간에서는 도구가 효과를 잃는다!","Items lose their effects in this strange space!",
            "이상한 공간이 원래대로 돌아왔다!","The strange space returned to normal!",
            "전기 기술의 위력이 약해졌다!","Electric's power was weakened!",
            "전기의 기운이 흩뿌려졌다!","Electricity was scattered!",
            "전투를 시작할 수 없다: 쓸 수 있는 림켓몬도, 접속 가능한 정착민도 없다.","Can't start the battle: no usable Rimketmon and no colonist who can connect.",
            "주변에 화학변화가스가 넘쳐 흐르고 있다!","Neutralizing gas filled the area!",
            "주변의 장애물이 사라졌다!","The hazards around disappeared!",
            "주변이 깨끗해졌다!","The surroundings were cleared!",
            "중력 때문에 쓸 수 없다!"," can't use it because of gravity!",
            "중력이 강해졌다!","Gravity intensified!",
            "중력이 원래대로 돌아왔다!","Gravity returned to normal!",
            "지르기로 소리 기술이 봉인되었다!"," can't use sound-based moves because of Throat Chop!",
            "진흙놀이 효과가 사라졌다.","Mud Sport's effect wore off.",
            "체력을 깎아 ","Cost: ",
            "저주로 ","Curse: ",
            "저주를 위해 ","Curse cost: ",
            "축하합니다!","Congratulations!",
            "트집을 잡혀서 같은 기술을 연속으로 쓸 수 없다!"," can't use the same move twice in a row because of the torment!",
            "특수 공격에 강해졌다!","Special attacks will be weaker!",
            "특수 방어 벽이 사라졌다.","The special barrier disappeared.",
            "파티의 상태이상이 모두 회복되었다!","The party's status conditions were all cured!",
            "퀵가드가 발동했다!","Quick Guard protected the team!",
            "크래프티실드가 발동했다!","Crafty Shield protected the team!",
            "판뒤집기가 발동했다!","Mat Block protected the team!",
            "풀필드로 ","Grassy Terrain: ",
            "필드가 사라졌다.","The terrain disappeared.",
            "필드의 효과가 사라졌다.","The terrain effect wore off.",
            "하지만 날씨는 바뀌지 않았다!","But the weather didn't change!",
            "하지만 아무 일도 일어나지 않았다!","But nothing happened!",
            "강한 햇살로 ","Harsh sunlight: ",
            "햇살이 강해졌다!","The sunlight turned harsh!",
            "햇살이 매우 강해졌다!","The sunlight turned extremely harsh!",
            "햇살이 약해졌다.","The sunlight faded.",
            "행운의 주문 효과가 끝났다.","Lucky Chant wore off.",
            "행운의 주문이 아군을 감쌌다!","Lucky Chant shielded the team!",
            "화학변화가스의 효과가 사라졌다!","The neutralizing gas effect disappeared!",
            "회복봉인 때문에 쓸 수 없다!"," can't use it because of Heal Block!",
            "흰안개가 사라졌다.","The mist disappeared.",
            "흰안개가 아군을 감쌌다!","Mist shrouded the team!",
            "의 명중률이","'s accuracy",
            "의 회피율이","'s evasiveness",
            "의 특성이 ","'s ability became ",
            "의 특성은 ","'s ability is ",
            "의 멸망 카운트가 ","'s perish count is ",
        };

        /// <summary>이름이 끼어 있어 조각 치환으로 안 되는 문장은 정규식으로 먼저 바꾼다.</summary>
        private static readonly string[] Rx =
        {
            @"^(.*)은\(는\) (.+)에게 (.+)을\(를\) 사용했다!$", "$1 used $3 on $2!",
            @"^(.+)은\(는\) 기운을 되찾았다! \(HP (.*)\)$", "$1 regained its strength! (HP $2)",
            @"^(.+)의 기술 PP가 회복되었다!$", "$1's move PP was restored!",
            @"^(.*)은\(는\) 상대와 같은 타입이 되었다!$", "$1 became the same type as the foe!",
            @"^(.*)은\(는\) (.+) 타입이 되었다!$", "$1 became $2 type!",
            @"^(.*)은\(는\) (.+)을\(를\) 사용했다!$", "$1 used $2!",
            @"^(.*)은\(는\) (.+)을\(를\) 배웠다!$", "$1 learned $2!",
            @"^(.*)은\(는\) (.+)을\(를\) 가지고 있다!$", "$1 has $2!",
            @"^(.*)은\(는\) (.+)의 (.+)을\(를\) 트레이스했다!$", "$1 traced $2's $3!",
            @"^(.*)의 특성이 (.+)\(으\)로 바뀌었다!$", "$1's ability became $2!",
            @"^(.*)의 특성은 (.+)이다!$", "$1's ability is $2!",
            @"^(.*)은\(는\) (.+)\(으\)로 변신했다!$", "$1 transformed into $2!",
            @"^(.*)은\(는\) 길동무로 (.+)을\(를\) 데려갔다!$", "$1 took $2 down with it!",
            @"^(.*)은\(는\) (.+)\(으\)로 몸을 지켰다!$", "$1 protected itself with $2!",
            @"^(.*)의 멸망 카운트가 (\d+)이\(가\) 되었다!$", "$1's perish count fell to $2!",
            @"^(.*)은\(는\) (\d+)개 비축했다!$", "$1 stockpiled $2!",
            @"^(.*)은\(는\) (.+)을\(를\) 겨냥했다!$", "$1 took aim at $2!",
            @"^(.*)은\(는\) (배고픈 모양|배부른 모양)이 되었다!$", "$1 switched to $2!",
            @"^(.*)의 (.+) 덕분에 (.+?)[이가] 떨어지지 않는다!$", "$1's $2 keeps $3 from dropping!",
            @"^(.*)은\(는\) (.+)의 (.+)\(을\)를 트레이스했다!$", "$1 traced $2's $3!",
            // ---- 지닌 도구 / 열매
            @"^(.+?)\(으\)로 (.+)의 체력이 회복되었다! \(HP (.*)\)$", "$2 restored HP with $1! (HP $3)",
            @"^(.+?)\(으\)로 (.+)은\(는\) (\d+)의 피해를 입었다! \(HP (.*)\)$", "$2 was hurt by $1 for $3! (HP $4)",
            @"^(.+?)의 반동으로 (.+)은\(는\) (\d+)의 피해를 입었다! \(HP (.*)\)$", "$2 was hurt by $1's recoil for $3! (HP $4)",
            @"^(.+)은\(는\) (.+)을\(를\) 먹고 선수를 쳤다!$", "$1 ate $2 and moved first!",
            @"^(.+)은\(는\) (.+)을\(를\) 먹었다!$", "$1 ate $2!",
            @"^(.+)은\(는\) (.+)을\(를\) 마셨다!$", "$1 drank $2!",
            @"^(.+)은\(는\) (.+)을\(를\) 되새김질했다!$", "$1 chewed $2 again!",
            @"^(.+)은\(는\) (.+)을\(를\) 수확했다!$", "$1 harvested $2!",
            @"^(.+)은\(는\) (.+)을\(를\) 주웠다!$", "$1 picked up $2!",
            @"^(.+)은\(는\) (.+)을\(를\) 썼다!$", "$1 used $2!",
            @"^(.+)은\(는\) (.+)을\(를\) 발동했다!$", "$1 activated $2!",
            @"^(.+)은\(는\) (.+)을\(를\) 내밀었다!$", "$1 held out $2!",
            @"^(.+)은\(는\) (.+)을\(를\) 눌렀다!$", "$1 pressed $2!",
            @"^(.+)은\(는\) (.+)을\(를\) 던졌다!$", "$1 threw $2!",
            @"^(.+)은\(는\) (.+)의 힘을 끌어냈다!$", "$1 drew out the power of $2!",
            @"^(.+)은\(는\) (.+)에게서 (.+)을\(를\) 받았다!$", "$1 received $3 from $2!",
            @"^(.+)은\(는\) (.+)을\(를\) 받았다!$", "$1 received $2!",
            @"^(.+)은\(는\) (.+)와\(과\) 도구를 바꿨다!$", "$1 switched items with $2!",
            @"^(.+)은\(는\) (.+)을\(를\) 되찾았다!$", "$1 got $2 back!",
            @"^(.+)은\(는\) (.+)의 (.+)을\(를\) 떨어뜨렸다!$", "$1 knocked off $2's $3!",
            @"^(.+)은\(는\) (.+)에게서 (.+)을\(를\) 훔쳤다!$", "$1 stole $3 from $2!",
            @"^(.+)은\(는\) (.+)의 (.+)을\(를\) 훔쳤다!$", "$1 stole $2's $3!",
            @"^(.+)은\(는\) (.+)의 (.+)을\(를\) 가로챘다!$", "$1 snatched $2's $3!",
            @"^(.+)은\(는\) (.+)의 (.+)을\(를\) 빼앗아 먹었다!$", "$1 stole and ate $2's $3!",
            @"^(.+)의 (.+)은\(는\) 불타 없어졌다!$", "$1's $2 was burned up!",
            @"^(.+)의 (.+)이\(가\) 녹아버렸다!$", "$1's $2 corroded away!",
            @"^(.+)의 (.+)이\(가\) 터졌다!$", "$1's $2 popped!",
            @"^(.+)에게 (.+)이\(가\) 덮쳐든다!$", "$2 is about to attack $1!",
            @"^(.+)은\(는\) (.+)\(으\)로 버텼다!$", "$1 endured the hit using $2!",
            @"^(.+)은\(는\) (.+)\(으\)로 (.+)의 위력을 줄였다!$", "$1 weakened $3 using $2!",
            @"^(.+)의 힘으로 (.+)의 위력이 올랐다!$", "$1 powered up $2!",
            @"^(.+)은\(는\) (.+)\(으\)로 능력을 되돌렸다!$", "$1 restored its stats with $2!",
            @"^(.+)은\(는\) (.+)\(으\)로 방해 효과가 사라졌다!$", "$1 shook off the disruption with $2!",
            @"^(.+)은\(는\) (.+)\(으\)로 힘이 넘친다!$", "$1 became fully charged with $2!",
            @"^(.+)은\(는\) (.+)\(으\)로 둥둥 떠올랐다!$", "$1 floats in the air with $2!",
            @"^(.+)은\(는\) (.+)의 열기로 화상을 입었다!$", "$1 was burned by the heat of $2!",
            @"^(.+)은\(는\) (.+)의 독으로 맹독에 걸렸다!$", "$1 was badly poisoned by $2!",
            @"^(.+)의 (마비|수면|독|화상|빙결) 상태가 나았다!$", "$1's $2 was cured!",
            @"^(.+)의 혼란이 풀렸다!$", "$1 snapped out of its confusion!",
            @"^(.+)의 (.+)의 PP가 회복되었다!$", "$1's $2 PP was restored!",
            @"^(.+)은\(는\) 급소를 노리기 쉬워졌다!$", "$1 is getting pumped!",
            @"^(.+)의 다음 기술은 맞기 쉬워졌다!$", "$1's next move will hit more easily!",
            @"^(.+)은\(는\) 도구를 쓸 수 없게 되었다!$", "$1 can't use items anymore!",
            @"^(.+)의 금제가 풀렸다!$", "$1 can use items again!",
            @"^(.+)의 (.+) 덕분에 도구를 빼앗기지 않았다!$", "$1's $2 kept its item from being taken!",
            @"^(.+)의 (.+)\(으\)로 (.+)도 헤롱헤롱해졌다!$", "$2 of $1 made $3 infatuated too!",
            @"^(.+)의 (.+)이\(가\) 반격했다!$", "$1's $2 struck back!",
            @"^(.+)의 (.+)이\(가\) 발동했다!$", "$1's $2 activated!",
        };

        private static string[] srcs; private static Dictionary<string, string> map;
        private static List<System.Text.RegularExpressions.Regex> rx; private static List<string> rxTo;

        private static void Init()
        {
            if (map != null) return;
            map = new Dictionary<string, string>();
            for (int i = 0; i + 1 < Pairs.Length; i += 2) map[Pairs[i]] = Pairs[i + 1];
            for (int i = 0; i + 1 < Pairs2.Length; i += 2) map[Pairs2[i]] = Pairs2[i + 1];
            rx = new List<System.Text.RegularExpressions.Regex>(); rxTo = new List<string>();
            for (int i = 0; i + 1 < Rx.Length; i += 2) { rx.Add(new System.Text.RegularExpressions.Regex(Rx[i])); rxTo.Add(Rx[i + 1]); }
            srcs = map.Keys.OrderByDescending(k => k.Length).ToArray();
        }

        /// <summary>영어 모드면 문장 조각을 치환한다. 한국어 모드나 한글이 없는 글자는 그대로.</summary>
        public static string Tr(string s)
        {
            if (!English || string.IsNullOrEmpty(s)) return s;
            bool hasKo = false;
            foreach (char c in s) if (c >= 0xAC00 && c <= 0xD7A3) { hasKo = true; break; }
            if (!hasKo) return s;
            Init();
            string r;
            if (map.TryGetValue(s, out r)) return r;
            for (int i = 0; i < rx.Count; i++) if (rx[i].IsMatch(s)) { s = rx[i].Replace(s, rxTo[i]); break; }
            // 긴 조각부터 한 번씩 치환 (이미 영어가 된 부분은 한글이 없어 영향 없음)
            var sb = new StringBuilder(s);
            foreach (var k in srcs) if (s.Contains(k)) sb.Replace(k, "\u0001" + map[k] + "\u0002");
            string o = sb.ToString().Replace("\u0001", "").Replace("\u0002", "");
            while (o.Contains("  ")) o = o.Replace("  ", " ");
            return o.Replace(" !", "!").Replace(" ,", ",").Replace(" .", ".").Replace(" '", "'").Replace(" ·", " ·");
        }
    }
}
