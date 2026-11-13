#!/usr/bin/env python3
"""진화 조건 표 만들기: Generation 9 Pack v3.3.8(Pokémon Essentials 기반) PBS pokemon.txt·pokemon_base_Gen_9_Pack.txt의 진화 줄을 읽어
Engine/Data/EvoData.cs 를 만든다. 형식(한 줄): FROM|TO|KIND|PARAM|LEVEL|TIME|GENDER|CONSUME
KIND: Level / Item(진화의 돌 쓰기) / Friendship / Move / Hold(지니고 레벨업) / Stat(공격·방어 비교) / PartySpecies / PartyType
어려운 방식(통신교환·아름다움·이벤트·비·급소 등)은 Level(근사)로 바꾸고, 레벨은 지금 데이터의 근사 레벨을 쓴다.
Essentials에 없는 종족(9세대 등)은 지금 데이터의 레벨 진화를 그대로 둔다.
사용: GenEvoData.py <pokemon.txt[,pokemon_base_Gen_9_Pack.txt]> <저장소 루트>
"""
import sys, os, re
pbs, root = sys.argv[1], sys.argv[2]
game = open(os.path.join(root, 'Engine/Data/GameData.cs'), encoding='utf-8').read()
forms = open(os.path.join(root, 'Engine/Data/FormData.cs'), encoding='utf-8').read()
items = open(os.path.join(root, 'Engine/Data/ItemData.cs'), encoding='utf-8').read()
item_ids = set(re.findall(r'new object\[\]\{"([A-Z0-9_]+)"', items))
species = {}; cur_evo = {}
for m in re.finditer(r'"([A-Z0-9_]+)\|([^"]*)"', game):
    f = (m.group(1) + '|' + m.group(2)).split('|')
    if len(f) < 12 or not f[4].isdigit(): continue
    species[f[0]] = True
    if len(f) > 13 and re.fullmatch(r'[A-Z0-9_]+:\d+(;[A-Z0-9_]+:\d+)*', f[13]):
        cur_evo[f[0]] = [(e.split(':')[0], int(e.split(':')[1])) for e in f[13].split(';')]
for m in re.finditer(r'"([A-Z0-9_]+)\|', forms): species.setdefault(m.group(1), True)

ess = {}
for path in pbs.split(','):
    cur = None
    for line in open(path, encoding='utf-8-sig'):
        line = line.strip()
        if line.startswith('['): cur = line[1:-1]; continue
        if not cur or '=' not in line: continue
        k, v = [x.strip() for x in line.split('=', 1)]
        if k == 'Evolution':                         # v21: 한 줄에 하나
            p = [x.strip() for x in v.split(',')]
            ess.setdefault(cur, []).append((p[0], p[1], p[2] if len(p) > 2 else ''))
        elif k == 'Evolutions' and v:                # Gen 9 Pack: 한 줄에 셋씩 이어 붙임
            p = [x.strip() for x in v.split(',')]
            for i in range(0, len(p) - 2, 3): ess.setdefault(cur, []).append((p[i], p[i + 1], p[i + 2]))

LOC = {'Magnetic': 'THUNDERSTONE', 'MossRock': 'LEAFSTONE', 'IceRock': 'ICESTONE'}
# Essentials(8세대까지)에 없는 9세대·레전드 아르세우스 진화. (방식, 값) — 종족·도구가 데이터에 있을 때만 쓴다.
EXTRA = {
    ('CHARCADET', 'ARMAROUGE'): ('Item', 'AUSPICIOUSARMOR'), ('CHARCADET', 'CERULEDGE'): ('Item', 'MALICIOUSARMOR'),
    ('CAPSAKID', 'SCOVILLAIN'): ('Item', 'FIRESTONE'), ('TADBULB', 'BELLIBOLT'): ('Item', 'THUNDERSTONE'),
    ('CETODDLE', 'CETITAN'): ('Item', 'ICESTONE'), ('POLTCHAGEIST', 'SINISTCHA'): ('Item', 'UNREMARKABLETEACUP'),
    ('APPLIN', 'DIPPLIN'): ('Item', 'SYRUPYAPPLE'), ('DIPPLIN', 'HYDRAPPLE'): ('Move', 'DRAGONCHEER'),
    ('DUNSPARCE', 'DUDUNSPARCE'): ('Move', 'HYPERDRILL'), ('GIRAFARIG', 'FARIGIRAF'): ('Move', 'TWINBEAM'),
    ('PRIMEAPE', 'ANNIHILAPE'): ('Move', 'RAGEFIST'), ('STANTLER', 'WYRDEER'): ('Move', 'PSYSHIELDBASH'),
    ('URSARING', 'URSALUNA'): ('Item', 'PEATBLOCK'), ('DURALUDON', 'ARCHALUDON'): ('Item', 'METALALLOY'),
    ('SCYTHER', 'KLEAVOR'): ('Item', 'BLACKAUGURITE'), ('BISHARP', 'KINGAMBIT'): ('Hold', 'LEADERSCREST'),
}
# 지금 데이터의 근사 레벨이 엉뚱한 것(걸음 수 1000, 이벤트 100)은 보기 좋은 레벨로
FIXLV = {('PAWMO', 'PAWMOT'): 40, ('BRAMBLIN', 'BRAMBLEGHAST'): 40, ('RELLOR', 'RABSCA'): 40, ('KUBFU', 'URSHIFU'): 60}
rows = []; approx = 0; seen_rows = set()
def row(fr, to, kind, param='', lv=0, time='', gender='', consume=False):
    lv = FIXLV.get((fr, to), lv) if kind == 'Level' else lv
    key = (fr, to, kind, param, time, gender)
    if key in seen_rows: return
    seen_rows.add(key)
    rows.append('|'.join([fr, to, kind, param, str(lv), time, gender, '1' if consume else '0']))
for fr in sorted(species):
    cur_list = cur_evo.get(fr, [])
    cur_lv = dict(cur_list)
    done = set()
    for (to, meth, par) in ess.get(fr, []):
        if to not in species or meth == 'None': continue      # None: 지역 모습만의 진화(원래 모습은 진화하지 않음)
        fb = cur_lv.get(to, 36)
        if meth == 'LevelDefeatItsKindWithItem' and par in item_ids:      # 대장의징표: 지니고 레벨업으로
            row(fr, to, 'Hold', par); done.add(to); continue
        tm = 'Day' if meth.endswith('Day') or meth.startswith('Day') else 'Night' if 'Night' in meth else ''
        g = 'M' if meth.endswith('Male') else 'F' if meth.endswith('Female') else ''
        if meth in ('Level', 'LevelDay', 'LevelNight', 'LevelMale', 'LevelFemale', 'Silcoon', 'Cascoon', 'Ninjask', 'Shedinja'):
            row(fr, to, 'Level', '', int(par), tm, g)
        elif meth in ('Item', 'ItemMale', 'ItemFemale') and par in item_ids:
            row(fr, to, 'Item', par, 0, '', g)
        elif meth == 'LocationFlag' and LOC.get(par) in item_ids:
            row(fr, to, 'Item', LOC[par])
        elif meth in ('Happiness', 'HappinessDay', 'HappinessNight'):
            row(fr, to, 'Friendship', '', 0, tm)
        elif meth == 'HappinessMoveType':
            row(fr, to, 'Friendship', par, 0, tm)
        elif meth == 'HasMove':
            row(fr, to, 'Move', par)
        elif meth in ('HoldItem', 'DayHoldItem', 'NightHoldItem', 'TradeItem') and par in item_ids:
            row(fr, to, 'Hold', par, 0, tm, '', meth != 'HoldItem')
        elif meth in ('AttackGreater', 'DefenseGreater', 'AtkDefEqual'):
            row(fr, to, 'Stat', {'AttackGreater': 'ATK', 'DefenseGreater': 'DEF', 'AtkDefEqual': 'EQ'}[meth], int(par))
        elif meth == 'HasInParty' and par in species:
            row(fr, to, 'PartySpecies', par)
        elif meth == 'LevelDarkInParty':
            row(fr, to, 'PartyType', 'DARK', int(par))
        else:
            row(fr, to, 'Level', '', fb if not meth.startswith('Level') or not par.isdigit() else int(par)); approx += 1
        done.add(to)
    for (to, lv) in cur_list:
        if to in done: continue
        ex = EXTRA.get((fr, to))
        if ex and (ex[0] != 'Item' or ex[1] in item_ids): row(fr, to, ex[0], ex[1]); done.add(to)
        else: row(fr, to, 'Level', '', lv); done.add(to)
    for (k, v) in EXTRA.items():
        if k[0] == fr and k[1] not in done and k[1] in species and (v[0] != 'Item' or v[1] in item_ids): row(fr, k[1], v[0], v[1]); done.add(k[1])
out = os.path.join(root, 'Engine/Data/EvoData.cs')
with open(out, 'w', encoding='utf-8') as f:
    f.write('// 자동 생성: Tools/GenEvoData.py (Generation 9 Pack v3.3.8 PBS, Eevee Expo). 손으로 고치지 말 것.\n')
    f.write('namespace Rimketmon.Engine\n{\n    internal static class EvoData\n    {\n        // FROM|TO|KIND|PARAM|LEVEL|TIME|GENDER|CONSUME\n        public static readonly string[] Rows =\n        {\n')
    for r in rows: f.write('            "' + r + '",\n')
    f.write('        };\n    }\n}\n')
print(len(rows), 'rows; approximated', approx)
