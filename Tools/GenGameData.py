#!/usr/bin/env python3
"""
레퍼런스 데이터 → Engine/Data/GameData.cs 생성기.

입력:
  --pbs      Pokémon Essentials + Generation 9 Pack PBS 폴더 (moves.txt, pokemon.txt, *_Gen_9_Pack.txt)
  --data     PokeAPI CSV 폴더(pokemon_species_names·move_names·ability_names·ability_flavor_text: 한글 이름·설명)
  --pr       PokeRogue 복원 소스의 src 폴더 (바이옴 등장 등급: data/balance/biomes/*.ts)
  --out      출력 C# 파일

출력은 순수 데이터(문자열 표)이며, 엔진이 시작할 때 파싱한다.
이 스크립트는 '지원하는 부가효과'가 있는 공격기만 기술 풀로 뽑는다.
"""
import argparse, glob, os, re, collections

ap = argparse.ArgumentParser()
ap.add_argument('--pbs', required=True); ap.add_argument('--data', required=True)
ap.add_argument('--pr', required=True); ap.add_argument('--out', required=True); ap.add_argument('--prko', default=None)
a = ap.parse_args()

# ---------------------------------------------------------------- PBS 파서
def parse(fn):
    out = collections.OrderedDict(); cur = None
    for l in open(fn, encoding='utf-8-sig'):
        l = l.rstrip('\n')
        if l.startswith('#') or not l.strip(): continue
        m = re.match(r'\[(.+)\]', l)
        if m: cur = m.group(1); out[cur] = {}; continue
        if '=' in l and cur: k, v = l.split('=', 1); out[cur][k.strip()] = v.strip()
    return out

def load(names):
    r = collections.OrderedDict()
    for n in names:
        p = os.path.join(a.pbs, n)
        if os.path.exists(p):
            for k, v in parse(p).items(): r.setdefault(k, v)
    return r

moves_raw = load(['moves.txt', 'moves_Gen_9_Pack.txt'])
species_raw = load(['pokemon.txt', 'pokemon_base_Gen_9_Pack.txt'])

# ---------------------------------------------------------------- 한글 이름
# --data 폴더의 PokeAPI CSV(github.com/PokeAPI/pokeapi data/v2/csv): 영어 이름 → 한국어 이름·설명. (예전 팬게임 메시지 파일은 쓰지 않는다)
import csv as _csv
def _api(name):
    return list(_csv.DictReader(open(os.path.join(a.data, name + '.csv'), encoding='utf-8')))
def _en2ko(table, idcol):
    en, ko = {}, {}
    for r in _api(table):
        if r['local_language_id'] == '9': en[r[idcol]] = r['name']
        elif r['local_language_id'] == '3': ko[r[idcol]] = r['name']
    return {en[i]: ko[i] for i in en if i in ko}
class _KoMap(dict):
    """영어 이름으로 찾는다(대소문자·기호 무시)."""
    def __init__(self, d): super().__init__({re.sub(r'[^a-z0-9]', '', k.lower()): v for k, v in d.items()})
    def get(self, k, default=None): return super().get(re.sub(r'[^a-z0-9]', '', (k or '').lower()), default)
ko_species = _KoMap(_en2ko('pokemon_species_names', 'pokemon_species_id'))
ko_moves = _KoMap(_en2ko('move_names', 'move_id'))
ko_ab = _KoMap(_en2ko('ability_names', 'ability_id'))
_abd_en = {r['ability_id']: r['name'] for r in _api('ability_names') if r['local_language_id'] == '9'}
_abd = {}
for r in _api('ability_flavor_text'):
    if r['language_id'] == '3': _abd[r['ability_id']] = r['flavor_text'].replace('\n', ' ')      # 가장 마지막 판의 설명
_abd_by_name = _KoMap({_abd_en[i]: t for i, t in _abd.items() if i in _abd_en})
_ab_desc_en = {}
class _AbDesc:
    def get(self, desc, default=''): return default
ko_abd = _AbDesc()

# PokeRogue 한국어 번역(있으면 우선). --prko 로 locales/ko 폴더 지정
import json as _json
def _norm(x): return re.sub(r'[^a-z0-9]', '', x.lower())
pr_move, pr_ab = {}, {}
if getattr(a, 'prko', None) and os.path.isdir(a.prko):
    for k, v in _json.load(open(os.path.join(a.prko, 'move.json'), encoding='utf-8')).items(): pr_move[_norm(k)] = v
    for k, v in _json.load(open(os.path.join(a.prko, 'ability.json'), encoding='utf-8')).items(): pr_ab[_norm(k)] = v

# ---------------------------------------------------------------- 지원하는 부가효과
TYPES = ['NORMAL','FIRE','WATER','GRASS','ELECTRIC','ICE','FIGHTING','POISON','GROUND','FLYING',
         'PSYCHIC','BUG','ROCK','GHOST','DRAGON','DARK','STEEL','FAIRY']
STAT = {'Attack':'A','Defense':'D','SpAtk':'S','SpDef':'P','Speed':'E','Accuracy':'C'}

def effect_of(code):
    """FunctionCode → 효과 코드 문자열. 지원하지 않으면 None."""
    if code == 'None': return ''
    m = re.fullmatch(r'(Flinch|Burn|Paralyze|Poison|BadPoison|Freeze|Confuse|Sleep)Target', code)
    if m: return {'Flinch':'FL','Burn':'BN','Paralyze':'PR','Poison':'PS','BadPoison':'TX','Freeze':'FZ','Confuse':'CF','Sleep':'SL'}[m.group(1)]
    m = re.fullmatch(r'LowerTarget(Attack|Defense|SpAtk|SpDef|Speed|Accuracy)([123])', code)
    if m: return 'T' + STAT[m.group(1)] + '-' + m.group(2)
    m = re.fullmatch(r'(Raise|Lower)User(Attack|Defense|SpAtk|SpDef|Speed)([123])', code)
    if m: return 'U' + STAT[m.group(2)] + ('+' if m.group(1) == 'Raise' else '-') + m.group(3)
    if code == 'LowerUserDefSpDef1': return 'UD-1,UP-1'
    if code == 'RaiseUserMainStats1': return 'UA+1,UD+1,US+1,UP+1,UE+1'
    if code == 'HealUserByHalfOfDamageDone': return 'DR2'
    if code == 'HealUserByThreeQuartersOfDamageDone': return 'DR3'
    if code == 'RecoilQuarterOfDamageDealt': return 'RC4'
    if code == 'RecoilThirdOfDamageDealt': return 'RC3'
    if code == 'RecoilHalfOfDamageDealt': return 'RC2'
    if code == 'HitTwoTimes': return 'H2'
    if code == 'HitTwoToFiveTimes': return 'H5'
    if code == 'AlwaysCriticalHit': return 'CR'
    if code == 'AttackAndSkipNextTurn': return 'RE'
    if code == 'UserFaintsExplosive': return 'EX'
    if code == 'BindTarget': return 'BD'
    # ---- 추가 지원(팬게임 PBS 함수 코드 기준) ----
    extra = {
        'HitTwoTimesPoisonTarget': 'H2,PS',
        'RecoilThirdOfDamageDealtParalyzeTarget': 'RC3,PR',
        'PowerHigherWithUserHP': 'PH',
        'DoublePowerIfTargetNotActed': 'DN',
        'DoublePowerIfTargetActed': 'DA',
        'FailsIfTargetActed': 'FA',
        'HitThreeTimesPowersUpWithEachHit': 'H3U',
        'IgnoreTargetDefSpDefEvaStatStages': 'IG',
        'UseTargetDefenseInsteadOfTargetSpDef': 'UT',
        'MultiTurnAttackConfuseUserAtEnd': 'LK',
        'IncreasePowerSuperEffective': 'SE',
        'DoublePowerIfUserLostHPThisTurn': 'DL',
        'CantSelectConsecutiveTurns': 'NC',
        'FlinchTargetDoublePowerIfTargetInSky': 'FL',
        'HitTwoToFiveTimesRaiseUserSpd1LowerUserDef1': 'H5,UE+1,UD-1',
        'CrashDamageIfFailsUnusableInGravity': 'CK',
        'CannotMakeTargetFaint': 'FS',
        'IgnoreTargetAbility': 'MB',
        # 이 모드에는 리플렉터/도구/하늘 상태가 없으므로 해당 부가효과는 생략하고 피해만 준다
        'RemoveScreens': '', 'UserConsumeTargetBerry': '', 'UserTakesTargetItem': '', 'RemoveTargetItem': '',
        'HitsTargetInSkyGroundsTarget': '', 'PursueSwitchingFoe': '',
        'DoublePowerIfTargetLostHPThisTurn': 'DL',
        # ---- 단식 배틀에서 날씨/필드/하늘/더블 상태가 없는 규칙으로 옮길 수 있는 것 ----
        'SwitchOutUserDamagingMove': '', 'SwitchOutTargetDamagingMove': '',
        'DoublePowerIfTargetUnderground': '', 'DoublePowerIfTargetUnderwater': '', 'DoublePowerIfTargetInSky': '',
        'HitsTargetInSky': '', 'CannotBeRedirected': '', 'DamageTargetAlly': '', 'RemoveProtections': '',
        'RemoveProtectionsBypassSubstitute': '', 'LowerPPOfTargetLastMoveBy3': '', 'CureTargetBurn': '',
        'DestroyTargetBerryOrGem': '', 'AddMoneyGainedFromBattle': '', 'DoublePowerIfAllyFaintedLastTurn': '',
        'IncreasePowerInSunWeather': '', 'IncreasePowerInElectricTerrain': '', 'DoublePowerInElectricTerrain': '',
        'UsedAfterAllyRoundWithDoublePower': '', 'RemoveTerrain': '', 'RemoveTerrainIceSpinner': '',
        'HitTwoTimesTargetThenTargetAlly': 'H2',
        'ParalyzeTargetAlwaysHitsInRainHitsTargetInSky': 'PR',
        'ConfuseTargetAlwaysHitsInRainHitsTargetInSky': 'CF',
        'FreezeTargetAlwaysHitsInHail': 'FZ',
        'ParalyzeTargetAlwaysHitsInRain': 'PR', 'BurnTargetAlwaysHitsInRain': 'BN',
        'LowerTargetSpeed1AlwaysHitsInRain': 'TE-1', 'LowerTargetSpeed1WeakerInGrassyTerrain': 'TE-1',
        'LowerTargetDefense1PowersUpInGravity': 'TD-1', 'HigherPriorityInGrassyTerrain': '',
        'BindTargetDoublePowerIfTargetUnderwater': 'BD',
        'ParalyzeFlinchTarget': 'PR,FL', 'BurnFlinchTarget': 'BN,FL', 'FreezeFlinchTarget': 'FZ,FL',
        'RecoilThirdOfDamageDealtBurnTarget': 'RC3,BN', 'HitTwoTimesFlinchTarget': 'H2,FL',
        'LowerTargetDefense1FlinchTarget': 'TD-1,FL',
        'LowerUserAtkDef1': 'UA-1,UD-1', 'LowerUserDefSpDefSpd1': 'UD-1,UP-1,UE-1',
        'HoopaRemoveProtectionsBypassSubstituteLowerUserDef1': 'UD-1',
        'AddMoneyGainedFromBattleLowerUserSpAtk1': 'US-2',
        'HitThreeTimesAlwaysCriticalHit': 'H3,CR', 'HitThreeTimes': 'H3',
        'DoublePowerIfTargetHPLessThanHalf': 'DB', 'DoublePowerIfTargetStatusProblem': 'DS',
        'DoublePowerIfTargetPoisoned': 'DP', 'DoublePowerIfUserPoisonedBurnedParalyzed': 'DF',
        'DoublePowerIfTargetParalyzedCureTarget': 'DQ', 'DoublePowerIfTargetAsleepCureTarget': 'DW',
        'DoublePowerIfTargetStatusProblemBurnTarget': 'DS,BN', 'DoublePowerIfTargetPoisonedPoisonTarget': 'DP,PS',
        'PowerHigherWithUserPositiveStatStages': 'PW',
        'UseTargetAttackInsteadOfUserAttack': 'FP', 'UseUserDefenseInsteadOfUserAttack': 'BP',
        'FlinchTargetFailsIfNotUserFirstTurn': 'FF,FL', 'FailsIfNotUserFirstTurn': 'FF',
        'RemoveUserBindingAndEntryHazards': 'RB', 'RemoveUserBindingAndEntryHazardsPoisonTarget': 'RB,PS',
        'ResetTargetStatStages': 'RS',
        'RecoilHalfOfTotalHP': 'RM', 'UserLosesHalfOfTotalHP': 'RM', 'UserLosesHalfOfTotalHPExplosive': 'RM',
        'UserFaintsPowersUpInMistyTerrainExplosive': 'EX',
        'HealUserByHalfOfDamageDoneBurnTarget': 'DR2,BN',
        'CrashDamageIfFails': 'CK', 'CrashDamageIfFailsConfuseTarget': 'CK',
        'TwoTurnAttack': 'TT', 'TwoTurnAttackOneTurnInSun': 'TT', 'TwoTurnAttackFlinchTarget': 'TT,FL',
        'TwoTurnAttackChargeRaiseUserDefense1': 'TT,UD+1', 'TwoTurnAttackChargeRaiseUserSpAtk1': 'TT,US+1',
        'TwoTurnAttackParalyzeTarget': 'TT,PR', 'TwoTurnAttackBurnTarget': 'TT,BN',
        'TwoTurnAttackInvulnerableInSky': 'TI', 'TwoTurnAttackInvulnerableUnderground': 'TI',
        'TwoTurnAttackInvulnerableUnderwater': 'TI', 'TwoTurnAttackInvulnerableInSkyParalyzeTarget': 'TI,PR',
        'TwoTurnAttackInvulnerableRemoveProtections': 'TI',
        'ParalyzeBurnOrFreezeTarget': 'TA',
        'TrapTargetInBattle': 'TR', 'TrapTargetInBattleMainEffect': 'TR',
        'TypeIsUserFirstType': 'Y1', 'TypeIsUserSecondType': 'Y2', 'TypeIsUserSecondTypeRemoveScreens': 'Y2',
        'FreezeTargetSuperEffectiveAgainstWater': 'FZ,FD',
        'TwoTurnAttackInvulnerableInSkyTargetCannotAct': 'TI', 'TwoTurnAttackOneTurnInRainRaiseUserSpAtk1': 'TT,US+1',
        'TwoTurnAttackRaiseUserSpAtkSpDefSpd2': 'TT,US+2,UP+2,UE+2',
    }
    if code in extra: return extra[code]
    return None

moves = []
# 효과 코드가 피해만 주고 부가효과를 버리는(손실) 함수 코드: 엔진의 Fn 처리기가 있어야 '구현됨'으로 본다
LOSSY_EMPTY = {'RemoveScreens','UserConsumeTargetBerry','UserTakesTargetItem','RemoveTargetItem','HitsTargetInSkyGroundsTarget','PursueSwitchingFoe',
 'SwitchOutUserDamagingMove','SwitchOutTargetDamagingMove','DoublePowerIfTargetUnderground','DoublePowerIfTargetUnderwater','DoublePowerIfTargetInSky',
 'HitsTargetInSky','CannotBeRedirected','DamageTargetAlly','RemoveProtections','RemoveProtectionsBypassSubstitute','LowerPPOfTargetLastMoveBy3','CureTargetBurn',
 'DestroyTargetBerryOrGem','AddMoneyGainedFromBattle','DoublePowerIfAllyFaintedLastTurn','IncreasePowerInSunWeather','IncreasePowerInElectricTerrain',
 'DoublePowerInElectricTerrain','UsedAfterAllyRoundWithDoublePower','RemoveTerrain','RemoveTerrainIceSpinner','HigherPriorityInGrassyTerrain'}
FLAGMAP = (('Contact','C'),('Punching','P'),('Biting','B'),('Sound','S'),('Pulse','U'),('Slicing','L'),('Wind','W'),('Bomb','M'),('Powder','D'),
           ('CanProtect','J'),('CanMirrorMove','K'),('Dance','N'),('ThawsUser','T'),('CannotMetronome','Q'),('TramplesMinimize','G'))
for key, v in moves_raw.items():
    cat = v.get('Category')
    if cat not in ('Physical', 'Special', 'Status'): continue
    power = int(v.get('Power', '0'))
    fn = v.get('FunctionCode', 'None')
    eff = effect_of(fn)
    lossy = 1 if (eff is None or fn in LOSSY_EMPTY) else 0
    if eff is None: eff = ''
    t = v.get('Type', 'NORMAL')
    if t not in TYPES: continue
    flags = v.get('Flags', '')
    moves.append(dict(
        id=key, name=pr_move.get(_norm(key), {}).get('name') or ko_moves.get(v.get('Name', ''), v.get('Name', key)),
        desc=(pr_move.get(_norm(key), {}).get('effect') or '').replace('|', '/').replace('\n', ' '),
        type=TYPES.index(t), cat={'Physical':0,'Special':1,'Status':2}[cat],
        power=power, acc=int(v.get('Accuracy', '100')), pp=int(v.get('TotalPP', '5')),
        prio=int(v.get('Priority', '0')), eff=eff, chance=int(v.get('EffectChance', '0')),
        highcrit=1 if 'HighCriticalHitRate' in flags else 0, fn=fn, target=v.get('Target', 'NearOther'), lossy=lossy,
        flags=''.join(c for n, c in FLAGMAP if n in [x.strip() for x in flags.split(',')])))
move_ids = set(m['id'] for m in moves)
print('moves', len(moves))

# ---------------------------------------------------------------- 종족
def norm(s): return re.sub(r'[^A-Z0-9]', '', s.upper())
evolves_from = {}
for k, v in species_raw.items():
    evo = v.get('Evolutions', '')
    if evo:
        parts = evo.split(',')
        for i in range(0, len(parts) - 2, 3):
            evolves_from.setdefault(parts[i], k)
def root_of(k):
    seen = set()
    while k in evolves_from and k not in seen:
        seen.add(k); k = evolves_from[k]
    return k

# ---------------------------------------------------------------- 등장 등급(PokeRogue 바이옴 풀)
NONBOSS = {'COMMON': 356/512, 'UNCOMMON': 124/512, 'RARE': 26/512, 'SUPER_RARE': 5/512, 'ULTRA_RARE': 1/512}
BOSS = {'BOSS': 44/64 * 0.1, 'BOSS_RARE': 14/64 * 0.1, 'BOSS_SUPER_RARE': 5/64 * 0.1, 'BOSS_ULTRA_RARE': 1/64 * 0.1}  # 보스는 10층에 한 번
PROB = dict(NONBOSS); PROB.update(BOSS)

def block(text, start):
    i = text.index('{', start); depth = 0
    for j in range(i, len(text)):
        if text[j] == '{': depth += 1
        elif text[j] == '}':
            depth -= 1
            if depth == 0: return text[i:j + 1]
    return text[i:]

rarity = collections.defaultdict(float)
for f in sorted(glob.glob(os.path.join(a.pr, 'data/balance/biomes/*.ts'))):
    t = open(f, encoding='utf-8').read()
    pi = t.find('pokemonPool')
    if pi < 0: continue
    pool = block(t, pi)
    for tier, p in PROB.items():
        m = re.search(r'\[BiomePoolTier\.' + tier + r'\]\s*:', pool)
        if not m: continue
        b = block(pool, m.end())
        ids = set(re.findall(r'SpeciesId\.(\w+)', b))
        if not ids: continue
        for sid in ids: rarity[norm(sid)] += p / len(ids)
print('rarity-keyed species', len(rarity))

lookup = {norm(k): k for k in species_raw}
base_weight = {}
for nid, w in rarity.items():
    if nid in lookup: base_weight[lookup[nid]] = w
missing = [n for n in rarity if n not in lookup]
print('PokeRogue ids not matched to PBS:', len(missing), missing[:12])
positive = sorted(base_weight.values())
floor = (positive[len(positive)//10] if positive else 0.001)

def weight_of(k):
    r = root_of(k)
    w = base_weight.get(k) or base_weight.get(r) or 0.0
    return w if w > 0 else floor

abil_raw = load(['abilities.txt', 'abilities_Gen_9_Pack.txt'])
abilities = []
for key, v in abil_raw.items():
    _pa = pr_ab.get(_norm(key), {})
    abilities.append((key, _pa.get('name') or ko_ab.get(v.get('Name', ''), v.get('Name', key)), _pa.get('description') or _abd_by_name.get(v.get('Name', key), v.get('Description', ''))))

def evo_str(v):
    # Evolutions = TARGET,Method,Param,...  → 'TARGET:레벨' (레벨 진화는 그 레벨, 그 외 방식은 36레벨로 근사). 트레이너 파티의 진화 단계 결정에만 쓴다.
    parts = [x for x in v.get('Evolutions', '').split(',') if x != '']
    out = []
    for i in range(0, len(parts) - 2, 3):
        tgt, meth, par = parts[i], parts[i + 1], parts[i + 2]
        lv = int(par) if meth.startswith('Level') and par.isdigit() else 36
        out.append('%s:%d' % (tgt, lv))
    return ';'.join(out)

# 실제 포켓몬만 남긴다: 포켓로그 SpeciesId 목록에 있는 종(참고한 팬게임에만 있는 몬스터는 넣지 않는다)
_enum = open(os.path.join(a.pr, 'enums', 'species-id.ts'), encoding='utf-8').read()
OFFICIAL = set(re.sub(r'[^A-Z0-9]', '', x) for x in re.findall(r'^\s*([A-Z0-9_]+)\s*=?', _enum, re.M))
def is_official(key):
    n = re.sub(r'[^A-Z0-9]', '', key.upper()).replace('NIDORANFE', 'NIDORANF').replace('NIDORANMA', 'NIDORANM')
    return n in OFFICIAL

species = []
for key, v in species_raw.items():
    if not is_official(key): continue
    stats = [int(x) for x in v['BaseStats'].split(',')]
    hp, atk, df, spe, spa, spd = stats
    types = v['Types'].split(',')
    t1 = TYPES.index(types[0]) if types[0] in TYPES else 0
    t2 = TYPES.index(types[1]) if len(types) > 1 and types[1] in TYPES else -1
    lm = []
    parts = v.get('Moves', '').split(',')
    seen = set()
    for i in range(0, len(parts) - 1, 2):
        lv, mid = int(parts[i]), parts[i + 1]
        if mid in move_ids and (lv, mid) not in seen:
            seen.add((lv, mid)); lm.append('%d:%s' % (lv, mid))
    species.append(dict(id=key, name=ko_species.get(v.get('Name', ''), v.get('Name', key)),
                        t1=t1, t2=t2, hp=hp, atk=atk, df=df, spa=spa, spd=spd, spe=spe,
                        w=weight_of(key), moves=';'.join(lm),
                        ab=';'.join(x for x in v.get('Abilities', '').split(',') if x),
                        evo=evo_str(v), wt=v.get('Weight', '10')))
print('species', len(species), 'bst>=600:', sum(1 for s in species if s['hp']+s['atk']+s['df']+s['spa']+s['spd']+s['spe'] >= 600))

# ---------------------------------------------------------------- 출력
def q(s): return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'
with open(a.out, 'w', encoding='utf-8') as o:
    o.write('// <auto-generated> Tools/GenGameData.py 가 만든 파일입니다. 직접 고치지 마세요. </auto-generated>\n')
    o.write('namespace Rimketmon.Engine\n{\n    public static partial class GameData\n    {\n')
    o.write('        /// <summary>id|한글명|타입|분류(0물리1특수)|위력|명중|PP|우선도|효과|효과확률|급소높음|플래그(C접촉 P펀치 B물기 S소리 U파동 L베기 W바람 M폭탄 D가루)</summary>\n')
    o.write('        public static readonly string[] MoveRows =\n        {\n')
    for m in moves:
        o.write('            %s,\n' % q('|'.join(str(x) for x in (m['id'], m['name'], m['type'], m['cat'], m['power'], m['acc'], m['pp'], m['prio'], m['eff'], m['chance'], m['highcrit'], m['flags'], m.get('desc', ''), m['fn'], m['target'], m['lossy']))))
    o.write('        };\n\n')
    o.write('        /// <summary>id|한글명|타입1|타입2|HP|공격|방어|특공|특방|속도|등장가중치|습득기술(레벨:기술id;...)|일반특성(;구분)|진화(대상:레벨;...)</summary>\n')
    o.write('        public static readonly string[] SpeciesRows =\n        {\n')
    for s in species:
        o.write('            %s,\n' % q('|'.join(str(x) for x in (s['id'], s['name'], s['t1'], s['t2'], s['hp'], s['atk'], s['df'], s['spa'], s['spd'], s['spe'], '%.6g' % s['w'], s['moves'], s['ab'], s['evo'], s['wt']))))
    o.write('        };\n\n')
    o.write('        /// <summary>id|한글명|설명</summary>\n')
    o.write('        public static readonly string[] AbilityRows =\n        {\n')
    for ab in abilities:
        o.write('            %s,\n' % q('|'.join(x.replace('|', '/').replace('\n', ' ') for x in ab)))
    o.write('        };\n    }\n}\n')
print('written', a.out, os.path.getsize(a.out))
