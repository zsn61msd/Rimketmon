#!/usr/bin/env python3
"""포켓로그의 애니메이션 포켓몬 스프라이트(앞/뒤 아틀라스)를 모드용으로 옮긴다.
- 아틀라스 PNG는 그대로 복사: Textures/Rimket/pk/f_<ID>.png (앞), b_<ID>.png (뒤)
- 프레임 목록(애니메이션 순서 0001..): PkAnim/f_<ID>.txt  각 줄 "x y w h ox oy sw sh" (잘라낸 영역, 원본 크기 안에서의 위치, 원본 크기)
사용: PackPokemonAnim.py --pr <images/pokemon> --species-id-ts <species-id.ts> --gamedata Engine/Data/GameData.cs --out-tex Rimketmon/Textures/Rimket/pk --out-txt Rimketmon/PkAnim
"""
import argparse, json, os, re, shutil
ap = argparse.ArgumentParser()
for k in ('pr', 'species_id_ts', 'gamedata', 'out_tex', 'out_txt'): ap.add_argument('--' + k.replace('_', '-'), required=True)
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
os.makedirs(a.out_tex, exist_ok=True); os.makedirs(a.out_txt, exist_ok=True)
ok = 0
for sid in ids:
    n = m.get(sid.upper())
    if not n: continue
    for pre, d in (('f', a.pr), ('b', os.path.join(a.pr, 'back'))):
        png = os.path.join(d, '%d.png' % n); js = os.path.join(d, '%d.json' % n)
        if not (os.path.exists(png) and os.path.exists(js)): continue
        j = json.load(open(js, encoding='utf-8'))
        frames = []
        if 'textures' in j:
            if len(j['textures']) != 1: continue
            frames = list(j['textures'][0]['frames'])
        elif isinstance(j.get('frames'), dict):
            for k, v in j['frames'].items(): v = dict(v); v['filename'] = k; frames.append(v)
        else: frames = list(j.get('frames', []))
        if not frames or any(f.get('rotated') for f in frames): continue   # 회전된 조각은 쓰지 않는다
        frames.sort(key=lambda f: f['filename'])
        shutil.copyfile(png, os.path.join(a.out_tex, '%s_%s.png' % (pre, sid)))
        with open(os.path.join(a.out_txt, '%s_%s.txt' % (pre, sid)), 'w') as o:
            for f in frames:
                fr = f['frame']; ss = f['spriteSourceSize']; sz = f['sourceSize']
                o.write('%d %d %d %d %d %d %d %d\n' % (fr['x'], fr['y'], fr['w'], fr['h'], ss['x'], ss['y'], sz['w'], sz['h']))
        ok += 1
print(ok, 'sprite sheets')
