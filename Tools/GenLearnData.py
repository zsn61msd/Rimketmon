#!/usr/bin/env python3
"""PBS pokemon.txt(+Gen9 base) → Engine/Data/LearnData.cs  (훈련 NPC가 가르칠 수 있는 기술 목록)
한 진화 계열(진화 전·후 포함)의 레벨업 기술 + 기술머신/전수(TutorMoves) + 교배기(EggMoves)를 모두 합친다.
사용: python3 -I GenLearnData.py <PBS 폴더> <GameData.cs> <출력 .cs>
행: 종족id,종족id,...|기술id;기술id;...  (계열 하나가 한 행)"""
import sys, re, os
pbs, gd, out = sys.argv[1:4]
def rows(fn, name):
    s = open(fn, encoding='utf-8').read()
    m = re.search(r'string\[\] ' + name + r'\s*=\s*\{(.*?)\n\s*\};', s, re.S)
    return re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1))
species = [r.split('|')[0] for r in rows(gd, 'SpeciesRows')]
moves = {r.split('|')[0] for r in rows(gd, 'MoveRows')}
sp = set(species)
data = {}
for fn in ('pokemon.txt', 'pokemon_base_Gen_9_Pack.txt'):
    p = os.path.join(pbs, fn)
    if not os.path.exists(p): continue
    cur = None
    for line in open(p, encoding='utf-8'):
        line = line.strip()
        m = re.match(r'^\[(\w+)\]$', line)
        if m: cur = m.group(1); data.setdefault(cur, {'moves': set(), 'evo': set()}); continue
        if cur is None or '=' not in line: continue
        k, v = [x.strip() for x in line.split('=', 1)]
        if k == 'Moves':
            parts = v.split(',')
            for i in range(1, len(parts), 2): data[cur]['moves'].add(parts[i].strip())
        elif k in ('TutorMoves', 'EggMoves'):
            for x in v.split(','): data[cur]['moves'].add(x.strip())
        elif k == 'Evolutions':
            parts = v.split(',')
            for i in range(0, len(parts), 3): data[cur]['evo'].add(parts[i].strip())
# 진화 계열(연결 요소)
parent = {s: s for s in species}
def find(x):
    while parent[x] != x: parent[x] = parent[parent[x]]; x = parent[x]
    return x
for s in species:
    for e in data.get(s, {}).get('evo', ()):
        if e in sp: parent[find(s)] = find(e)
fam = {}
for s in species: fam.setdefault(find(s), []).append(s)
lines = []; total = 0
for root, members in sorted(fam.items()):
    ms = set()
    for s in members: ms |= data.get(s, {}).get('moves', set())
    ms = sorted(m for m in ms if m in moves)
    total += len(ms) * len(members)
    lines.append('%s|%s' % (','.join(sorted(members)), ';'.join(ms)))
with open(out, 'w', encoding='utf-8') as f:
    f.write('// GenLearnData.py가 만든 파일 — 직접 고치지 마세요.\n// 진화 계열(진화 전·후) 전체의 레벨업·기술머신·전수·교배 기술 합집합. 출처: Generation 9 Pack v3.3.8 PBS (CREDITS.md)\nnamespace Rimketmon.Engine\n{\n    public static class LearnData\n    {\n        public static readonly string[] Rows =\n        {\n')
    for l in lines: f.write('            "' + l + '",\n')
    f.write('        };\n    }\n}\n')
print(len(lines), 'families; avg moves per species', total // len(species))
