#!/usr/bin/env python3
"""Smogon 사용률 통계(pkmn/smogon data/stats/*.json) → Engine/Data/RankedPoolData.cs
사용: python3 -I GenRankedPools.py <stats 폴더> <GameData.cs> <ItemData.cs> <출력 .cs>
- 사용률 '수치'(비율)만 쓴다. 림켓몬 엔진에 있는 종족/기술/특성/도구만 남기고 비율을 다시 정규화한다.
- 지역폼/폼 변형(Landorus-Therian 등)은 엔진에 없어서 제외한다.
행: 포맷|종족|사용률(‰)|특성 id:가중;..|도구 id:가중;..|성격 번호:가중;..|기술 id:가중;..|함께 쓰는 종족 id:가중;..
"""
import json, re, sys, os
stats, gd, itf, out = sys.argv[1:5]
FORMATS = ['pu', 'nu', 'ru', 'uu', 'ou', 'ubers', 'doublesou']
TOP = {}; TOP_DEFAULT = 160
NATURES = ['Hardy','Lonely','Brave','Adamant','Naughty','Bold','Docile','Relaxed','Impish','Lax','Timid','Hasty','Serious','Jolly','Naive','Modest','Mild','Quiet','Bashful','Rash','Calm','Gentle','Sassy','Careful','Quirky']
def rows(fn, name):
    s = open(fn, encoding='utf-8').read()
    m = re.search(r'string\[\] ' + name + r'\s*=\s*\{(.*?)\n\s*\};', s, re.S)
    return re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1))
n = lambda s: re.sub(r'[^A-Z0-9]', '', s.upper())
sp = {n(r.split('|')[0]): r.split('|')[0] for r in rows(gd, 'SpeciesRows')}
mv = {n(r.split('|')[0]): r.split('|')[0] for r in rows(gd, 'MoveRows')}
ab = {n(r.split('|')[0]): r.split('|')[0] for r in rows(gd, 'AbilityRows')}
it = {n(x): x for x in re.findall(r'new object\[\]\{"([A-Z0-9_]+)"', open(itf, encoding='utf-8').read())}
def pick(d, table, top, minshare=0.01):
    acc = {}
    for k, v in d.items():
        i = table.get(n(k))
        if i: acc[i] = acc.get(i, 0) + v
    tot = sum(acc.values())
    if tot <= 0: return ''
    lst = [(k, v / tot) for k, v in sorted(acc.items(), key=lambda x: -x[1]) if v / tot >= minshare][:top]
    return ';'.join('%s:%d' % (k, max(1, round(v * 1000))) for k, v in lst)
res = []; skipped = 0
for f in FORMATS:
    d = json.load(open(os.path.join(stats, 'gen9' + f + '.json')))
    mons = [(k, v) for k, v in d['pokemon'].items() if n(k) in sp]
    mons.sort(key=lambda kv: -kv[1]['usage']['weighted'])
    mons = mons[:TOP.get(f, TOP_DEFAULT)]
    keep = {sp[n(k)] for k, _ in mons}
    for k, v in mons:
        nat = {}
        for key, w in v.get('spreads', {}).items():
            nm = key.split(':')[0]
            if nm in NATURES: nat[NATURES[NATURES.index(nm)]] = nat.get(nm, 0) + w
        natrows = ';'.join('%d:%d' % (NATURES.index(a), max(1, round(b * 1000))) for a, b in sorted(nat.items(), key=lambda x: -x[1])[:4])
        mates = {sp[n(a)]: b for a, b in v.get('teammates', {}).items() if n(a) in sp and sp[n(a)] in keep}
        matestr = ';'.join('%s:%d' % (a, max(1, round(b * 1000))) for a, b in sorted(mates.items(), key=lambda x: -x[1])[:12])
        sprd = []
        for key, w in sorted(v.get('spreads', {}).items(), key=lambda x: -x[1])[:5]:
            nm, evs = key.split(':') if ':' in key else (None, None)
            if nm in NATURES and evs and len(evs.split('/')) == 6: sprd.append('%d@%s:%d' % (NATURES.index(nm), evs, max(1, round(w * 1000))))
        moves = pick(v['moves'], mv, 14, 0.01); items = pick(v['items'], it, 6); abil = pick(v['abilities'], ab, 3)
        if not moves or not abil: skipped += 1; continue
        res.append('%s|%s|%d|%s|%s|%s|%s|%s|%s' % (f, sp[n(k)], round(v['usage']['weighted'] * 1000), abil, items, natrows, moves, matestr, ';'.join(sprd)))
os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, 'w', encoding='utf-8') as o:
    o.write('// GenRankedPools.py가 만든 파일 — 직접 고치지 마세요.\n// 출처: Smogon 사용률 통계 (pkmn/smogon 저장소 data/stats, 퍼블릭 도메인으로 공개된 집계 수치). CREDITS.md 참고.\n')
    o.write('namespace Rimketmon.Engine\n{\n    public static class RankedPoolData\n    {\n        /// <summary>' + '|'.join(['종족','사용률‰','특성','도구','성격','기술','팀원','노력치 배분']) + '</summary>\n        public static readonly string[] Rows =\n        {\n')
    for r in res: o.write('            "' + r + '",\n')
    o.write('        };\n    }\n}\n')
print(len(res), 'rows; skipped', skipped)
for f in FORMATS: print(f, sum(1 for r in res if r.startswith(f + '|')))
