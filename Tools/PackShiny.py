#!/usr/bin/env python3
"""이로치(포켓로그 변종 3등급) 스프라이트를 모드용으로 옮긴다.
- 1등급(일반 이로치, 노랑 별): images/pokemon/shiny/<n>.png(+back/shiny) → pk/s1f_<ID>.png, s1b_<ID>.png (+ PkAnim/*.txt)
- 2·3등급(레어 파랑 별, 에픽 빨강 별): 포켓로그 에셋의 variant/_masterlist.json 값이
    1 → 색 바꾸기 표(일반 그림에 적용): PkAnim/pal_f_<ID>.txt / pal_b_<ID>.txt  각 줄 "등급 원래색 바뀐색"(16진수). 게임이 실행 중에 색을 바꿔 그린다
    2 → 따로 그린 그림: variant/<n>_<등급>.png(+json) → pk/s2f_<ID>.png …
사용: PackShiny.py --pr <PokeRogue images/pokemon> --variant <pokerogue-assets images/pokemon/variant> --species-id-ts <species-id.ts> --gamedata Engine/Data/GameData.cs --out-tex Rimketmon/Textures/Rimket/pk --out-txt Rimketmon/PkAnim
"""
import argparse, json, os, re, shutil
ap = argparse.ArgumentParser()
for k in ('pr', 'variant', 'species_id_ts', 'gamedata', 'out_tex', 'out_txt'): ap.add_argument('--' + k.replace('_', '-'), required=True)
a = ap.parse_args()
m = {}; cur = 0
for line in open(a.species_id_ts, encoding='utf-8').read().split('\n'):
    mm = re.match(r'^([A-Z0-9_]+)(?:\s*=\s*(\d+))?,?$', line.strip())
    if mm:
        cur = int(mm.group(2)) if mm.group(2) else cur + 1
        m[mm.group(1).replace('_', '')] = cur
m['NIDORANFE'] = 29; m['NIDORANMA'] = 32
gd = open(a.gamedata, encoding='utf-8').read().split('SpeciesRows')[1].split('};')[0]
ids = [r.split('|')[0] for r in re.findall(r'"([^"]+\|[^"]*)"', gd)]
master = json.load(open(os.path.join(a.variant, '_masterlist.json')))
mback = master.get('back', {})

def frames_of(js):
    j = json.load(open(js, encoding='utf-8'))
    fr = []
    if 'textures' in j:
        if len(j['textures']) != 1: return None
        fr = list(j['textures'][0]['frames'])
    elif isinstance(j.get('frames'), dict):
        for k, v in j['frames'].items(): v = dict(v); v['filename'] = k; fr.append(v)
    else: fr = list(j.get('frames', []))
    if not fr or any(f.get('rotated') for f in fr): return None
    fr.sort(key=lambda f: f['filename'])
    return fr

def copy_sheet(png, js, key):
    if not (os.path.exists(png) and os.path.exists(js)): return False
    fr = frames_of(js)
    if fr is None: return False
    shutil.copyfile(png, os.path.join(a.out_tex, key + '.png'))
    with open(os.path.join(a.out_txt, key + '.txt'), 'w') as o:
        for f in fr:
            r = f['frame']; ss = f['spriteSourceSize']; sz = f['sourceSize']
            o.write('%d %d %d %d %d %d %d %d\n' % (r['x'], r['y'], r['w'], r['h'], ss['x'], ss['y'], sz['w'], sz['h']))
    return True

stat = {'s1': 0, 'pal': 0, 'sep': 0}
for sid in ids:
    n = m.get(sid.upper())
    if not n: continue
    if not os.path.exists(os.path.join(a.out_tex, 'f_%s.png' % sid)): continue      # 애니메이션 시트가 없는 종은 건너뜀
    for side, sub, ms in (('f', '', master), ('b', 'back', mback)):
        base = os.path.join(a.pr, sub) if sub else a.pr
        if copy_sheet(os.path.join(base, 'shiny', '%d.png' % n), os.path.join(base, 'shiny', '%d.json' % n), 's1%s_%s' % (side, sid)): stat['s1'] += 1
        vs = ms.get(str(n))
        if not vs: continue
        vdir = os.path.join(a.variant, sub) if sub else a.variant
        pal_lines = []
        for t in (1, 2):      # 등급 2, 3
            if vs[t] == 1:
                pj = os.path.join(vdir, '%d.json' % n)
                if os.path.exists(pj):
                    p = json.load(open(pj))
                    for src, dst in p.get(str(t), {}).items(): pal_lines.append('%d %s %s' % (t + 1, src, dst))
            elif vs[t] == 2:
                if copy_sheet(os.path.join(vdir, '%d_%d.png' % (n, t + 1)), os.path.join(vdir, '%d_%d.json' % (n, t + 1)), 's%d%s_%s' % (t + 1, side, sid)): stat['sep'] += 1
        if pal_lines:
            open(os.path.join(a.out_txt, 'pal_%s_%s.txt' % (side, sid)), 'w').write('\n'.join(pal_lines) + '\n'); stat['pal'] += 1
print(stat)
