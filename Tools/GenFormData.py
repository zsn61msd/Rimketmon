#!/usr/bin/env python3
"""폼 종족 데이터: 포켓로그 소스에서 (1) 지역폼 종족(ALOLA_/GALAR_/HISUI_/PALDEA_ …, 붉은달 다투곰 등)과
(2) 모습마다 타입·능력치가 다른 폼(로토무 가전, 오도리드리, 루가루암, 도롱마담, 테오키스, 영물 폼체인지 …)을
별개의 종족(완성 형태)으로 만든다. 배틀 중에만 바뀌는 폼(메가·거다이·원시·달마모드 등)은 넣지 않는다.
→ Engine/Data/FormData.cs : FormSpeciesRows(SpeciesRows와 같은 형식), FormBaseRows(폼 id|원래 종족 id|도감 번호|그림 키), FormCatchRows
"""
import re, sys, os, glob
src, gd_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
gd = open(gd_path, encoding='utf-8').read()
def section(name):
    s = gd[gd.find(name + ' ='):]; return s[:s.find('};')]
species_rows = {r.split('|')[0]: r for r in re.findall(r'"([A-Z0-9_]+\|[^"]*)"', section('SpeciesRows'))}
move_ids = set(re.findall(r'"([A-Z0-9]+)\|', section('MoveRows')))
abil_ids = set(re.findall(r'"([A-Z0-9]+)\|', section('AbilityRows')))
def norm(s): return re.sub(r'[^A-Z0-9]', '', s.upper())
byn = {norm(i): i for i in species_rows}
byn = {k: v for k, v in byn.items() if v}
TYPES = ['NORMAL','FIRE','WATER','GRASS','ELECTRIC','ICE','FIGHTING','POISON','GROUND','FLYING','PSYCHIC','BUG','ROCK','GHOST','DRAGON','DARK','STEEL','FAIRY']
num = {}; cur = 0
for line in open(os.path.join(src, 'enums/species-id.ts'), encoding='utf-8').read().split('\n'):
    mm = re.match(r'^([A-Z0-9_]+)(?:\s*=\s*(\d+))?,?$', line.strip())
    if mm: cur = int(mm.group(2)) if mm.group(2) else cur + 1; num[mm.group(1)] = cur
BATTLE = re.compile(r'^(mega|gigantamax|eternamax|primal|zen|school|complete|blade|busted|hangry|noice|pirouette|stellar|terastal|hero|meteor|ultra|crowned|starter|cosplay|partner|rock-star|belle|pop-star|phd|libre|.*-mask-tera|.*tera$|10-pc|50-pc)')
TYPEKO = {'fighting':'격투','flying':'비행','poison':'독','ground':'땅','rock':'바위','bug':'벌레','ghost':'고스트','steel':'강철','fire':'불꽃','water':'물','grass':'풀','electric':'전기','psychic':'에스퍼','ice':'얼음','dragon':'드래곤','dark':'악','fairy':'페어리'}
regional_ko = ''
KOFORM = {'pompom':'파칙파칙스타일','roaming':'도보폼','pom-pom':'파칙파칙스타일','small':'작은 사이즈','large':'큰 사이즈','super':'특대 사이즈',
          'fighting':'격투','flying':'비행','poison':'독','ground':'땅','rock':'바위','bug':'벌레','ghost':'고스트','steel':'강철','fire':'불꽃','water':'물','grass':'풀','electric':'전기','psychic':'에스퍼','dragon':'드래곤','dark':'악','fairy':'페어리','heat':'히트','wash':'워시','frost':'프로스트','fan':'스핀','mow':'커트','baile':'이글이글스타일','pom-pom':'파칙파칙스타일','pau':'훌라훌라스타일','sensu':'하늘하늘스타일',
          'midday':'한낮의 모습','midnight':'한밤중의 모습','dusk':'황혼의 모습','plant':'초목도롱','sandy':'모래땅도롱','trash':'슈레도롱','attack':'어택폼','defense':'디펜스폼','speed':'스피드폼',
          'sky':'스카이폼','origin':'오리진폼','therian':'영물폼','incarnate':'화신폼','unbound':'굴레를 벗어난 모습','black':'블랙','white':'화이트','amped':'하이한 모습','low-key':'로우한 모습',
          'rapid-strike':'연격의 태세','single-strike':'일격의 태세','ice':'백마 탄 모습','shadow':'흑마 탄 모습','dawn-wings':'새벽의 날개','dusk-mane':'황혼의 갈기','10':'10%폼','combat':'컴뱃종','blaze':'블레이즈종','aqua':'워터종',
          'wellspring':'우물의 가면','hearthflame':'화덕의 가면','cornerstone':'주춧돌의 가면','female':'암컷','male':'수컷','blue-striped':'파랑줄무늬','white-striped':'하양줄무늬','east':'동쪽바다','west':'서쪽바다',
          'family-of-three':'세 식구','family-of-four':'네 식구','curly':'젖힌 모습','droopy':'늘어진 모습','stretchy':'뻗은 모습','zero':'제로폼','school':'군집','original':'500년 전의 색'}
REGION = {'ALOLA':'알로라','GALAR':'가라르','HISUI':'히스이','PALDEA':'팔데아'}
SPECIAL_KO = {'BLOODMOON_URSALUNA':'다투곰(붉은 달)','ETERNAL_FLOETTE':'플라엣테(영원의 꽃)'}
def blocks():
    for f in sorted(glob.glob(os.path.join(src, 'data/balance/species/*.ts'))):
        t = open(f, encoding='utf-8').read()
        for m in re.finditer(r'SpeciesData\[SpeciesId\.(\w+)\]\s*=\s*\{(.*?)\n  \};', t, re.S):
            yield m.group(1), m.group(2)
def field(b, k, d=None):
    m = re.search(r'\b' + k + r':\s*([^,\n]+)', b); return m.group(1).strip() if m else d
def tyidx(s):
    if not s or s == 'null' or 'UNKNOWN' in s: return -1
    return TYPES.index(s.split('.')[-1])
def abil(s):
    if not s: return None
    a = norm(s.split('.')[-1]); return a if a in abil_ids and a != 'NONE' else None
def moves(b):
    out = []
    lm = b[b.find('levelMoves'):] if 'levelMoves' in b else ''
    for lv, mv in re.findall(r'\[\s*(EVOLVE_MOVE|RELEARN_MOVE|\d+),\s*MoveId\.(\w+)\s*\]', lm):
        lvl = 0 if lv == 'EVOLVE_MOVE' else 1 if lv == 'RELEARN_MOVE' else int(lv)
        i = norm(mv)
        if i in move_ids: out.append('%d:%s' % (lvl, i))
    return ';'.join(dict.fromkeys(out))
rows, bases, rates = [], [], []
seen = set()
for key, b in blocks():
    sp = b[b.find('new PokemonSpecies'):]
    head = sp[:sp.find('forms:')] if 'forms:' in sp else sp
    # (1) 지역폼 등 별도 종족인데 우리 목록에 없는 것
    if norm(key) not in byn and key not in ('NIDORAN_F', 'NIDORAN_M') and not key.startswith('BATTLE_BOND') and key in num:
        reg = key.split('_')[0]; base = '_'.join(key.split('_')[1:])
        base_id = byn.get(norm(base))
        if base_id is None or (reg not in REGION and key not in SPECIAL_KO): continue
        ko = SPECIAL_KO.get(key) or (REGION[reg] + ' ' + species_rows[base_id].split('|')[1])
        t1, t2 = tyidx(field(head, 'type1')), tyidx(field(head, 'type2'))
        st = [field(head, k) for k in ('baseHp','baseAtk','baseDef','baseSpatk','baseSpdef','baseSpd')]
        ab = [a for a in (abil(field(head, 'ability1')), abil(field(head, 'ability2'))) if a]
        evo = []
        ev = b[b.find('evolutions:'):b.find('levelMoves')] if 'evolutions:' in b else ''
        for em in re.finditer(r'speciesId:\s*SpeciesId\.(\w+)(.*?)\}\)', ev, re.S):
            lvm = re.search(r'level:\s*(\d+)', em.group(2)); evo.append('%s:%s' % (em.group(1), lvm.group(1) if lvm else '36'))
        kg = field(head, 'weight', '10')
        rows.append('%s|%s|%d|%d|%s|0|%s|%s|%s|%s' % (key, ko, t1, t2, '|'.join(st), moves(b), ';'.join(ab), ';'.join(evo), kg))
        bases.append('%s|%s|%d|%d' % (key, base_id, num.get(base, 0), num[key]))
        rates.append('%s|%s' % (key, field(head, 'catchRate', '45')))
        seen.add(key); regional_ko = ko
        base_for_forms, num_for_forms, row_moves = key, num[key], moves(b)
    elif norm(key) in byn:
        base_for_forms, num_for_forms = byn[norm(key)], num.get(key, 0)
        row_moves = species_rows[base_for_forms].split('|')[11]
    else: continue
    # (2) 모습이 다른 폼
    if 'forms:' not in sp: continue
    forms = re.findall(r'new PokemonForm\(\{(.*?)\}\)', sp, re.S)
    if len(forms) < 2: continue
    f0 = forms[0]
    def sig(f): return (field(f, 'type1'), field(f, 'type2'), tuple(field(f, k) for k in ('baseHp','baseAtk','baseDef','baseSpatk','baseSpdef','baseSpd')))
    for f in forms:
        fk = (field(f, 'formKey', '""') or '""').strip('"\'')
        if fk == '' or fk.startswith('SpeciesFormKey.') or BATTLE.match(fk) or 'complete' in fk or 'cosplay' in fk or sig(f) == sig(f0): continue
        if base_for_forms in ('CASTFORM', 'MINIOR', 'CHERRIM', 'MELOETTA', 'DARMANITAN', 'GALAR_DARMANITAN', 'AEGISLASH', 'WISHIWASHI', 'MIMIKYU', 'EISCUE', 'MORPEKO', 'PALAFIN', 'CRAMORANT', 'TERAPAGOS', 'OGERPON') and fk not in ('wellspring', 'hearthflame', 'cornerstone'): continue      # 배틀 중에만 바뀌는 폼
        fid = base_for_forms + '_' + norm(fk)
        if fid in seen: continue
        seen.add(fid)
        fname = (field(f, 'formName', '') or '').strip('"\'')
        if 'starmobile' in fk or fk == 'super': continue      # 이벤트 전용 · 그림 없음
        baseko = species_rows[base_for_forms].split('|')[1] if base_for_forms in species_rows else regional_ko
        label = TYPEKO.get(fk, fname) if base_for_forms in ('ARCEUS', 'SILVALLY') else KOFORM.get(fk, fname)
        ko = (label + baseko) if base_for_forms == 'ROTOM' else baseko + '(' + label + ')'
        t1, t2 = tyidx(field(f, 'type1')), tyidx(field(f, 'type2'))
        st = [field(f, k) for k in ('baseHp','baseAtk','baseDef','baseSpatk','baseSpdef','baseSpd')]
        ab = [a for a in (abil(field(f, 'ability1')), abil(field(f, 'ability2'))) if a]
        rows.append('%s|%s|%d|%d|%s|0|%s|%s||%s' % (fid, ko, t1, t2, '|'.join(st), row_moves, ';'.join(ab), field(f, 'weight', '10')))
        bases.append('%s|%s|%d|%s' % (fid, byn.get(norm(base_for_forms), base_for_forms), num_for_forms, '%d-%s' % (num_for_forms, fk)))
        rates.append('%s|%s' % (fid, field(f, 'catchRate', '45')))
with open(out, 'w', encoding='utf-8') as o:
    o.write('// <auto-generated> Tools/GenFormData.py 가 만든 파일입니다. 직접 고치지 마세요. </auto-generated>\nnamespace Rimketmon.Engine\n{\n    public static partial class GameData\n    {\n')
    for nm, rr, doc in (('FormSpeciesRows', rows, 'SpeciesRows와 같은 형식(폼 = 별개의 완성 형태 종족)'), ('FormBaseRows', bases, '폼 id|원래 종족 id|원래 도감 번호|그림 키(포켓로그 images/pokemon 파일 이름)'), ('FormCatchRows', rates, '폼 id|포획률')):
        o.write('        /// <summary>%s</summary>\n        public static readonly string[] %s =\n        {\n' % (doc, nm))
        for r in rr: o.write('            "%s",\n' % r.replace('"', ''))
        o.write('        };\n')
    o.write('    }\n}\n')
print(len(rows), 'form species')
