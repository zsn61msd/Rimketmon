#!/usr/bin/env python3
"""폼 종족(FormData.cs의 FormBaseRows)의 포켓로그 애니메이션 그림을 옮긴다: 일반(앞/뒤), 이로치 1등급, 2·3등급(색 바꾸기 표 또는 별도 그림).
사용: PackFormSprites.py --pr <설치본 images/pokemon> --assets <pokerogue-assets images/pokemon> --forms Engine/Data/FormData.cs --out-tex Rimketmon/Textures/Rimket/pk --out-txt Rimketmon/PkAnim"""
import argparse, json, os, re, shutil
ap = argparse.ArgumentParser()
for k in ('pr', 'assets', 'forms', 'out_tex', 'out_txt'): ap.add_argument('--' + k.replace('_', '-'), required=True)
a = ap.parse_args()
t = open(a.forms, encoding='utf-8').read()
rows = re.findall(r'"([A-Z0-9_]+)\|([A-Z0-9_]+)\|(\d+)\|([0-9a-z\-]+)"', t[t.find('FormBaseRows'):t.find('FormCatchRows')])
master = json.load(open(os.path.join(a.assets, 'variant', '_masterlist.json')))
def frames_of(js):
    j = json.load(open(js, encoding='utf-8'))
    if 'textures' in j:
        if len(j['textures']) != 1: return None
        fr = list(j['textures'][0]['frames'])
    elif isinstance(j.get('frames'), dict): fr = [dict(v, filename=k) for k, v in j['frames'].items()]
    else: fr = list(j.get('frames', []))
    if not fr or any(f.get('rotated') for f in fr): return None
    fr.sort(key=lambda f: f['filename']); return fr
def copy(png, js, key):
    if not (os.path.exists(png) and os.path.exists(js)): return False
    fr = frames_of(js)
    if fr is None: return False
    shutil.copyfile(png, os.path.join(a.out_tex, key + '.png'))
    with open(os.path.join(a.out_txt, key + '.txt'), 'w') as o:
        for f in fr:
            r = f['frame']; ss = f['spriteSourceSize']; sz = f['sourceSize']
            o.write('%d %d %d %d %d %d %d %d\n' % (r['x'], r['y'], r['w'], r['h'], ss['x'], ss['y'], sz['w'], sz['h']))
    return True
st = {'normal': 0, 'shiny': 0, 'pal': 0, 'sep': 0, 'missing': []}
for fid, base, num, sk in rows:
    ok = False
    for side, sub in (('f', ''), ('b', 'back')):
        for root in (a.pr, a.assets):
            d = os.path.join(root, sub) if sub else root
            if copy(os.path.join(d, sk + '.png'), os.path.join(d, sk + '.json'), '%s_%s' % (side, fid)): ok = True; st['normal'] += 1; break
        d = os.path.join(a.assets, sub, 'shiny') if sub else os.path.join(a.assets, 'shiny')
        if copy(os.path.join(d, sk + '.png'), os.path.join(d, sk + '.json'), 's1%s_%s' % (side, fid)): st['shiny'] += 1
        ms = master.get('back', {}) if sub else master
        vs = ms.get(sk)
        if not vs: continue
        vdir = os.path.join(a.assets, 'variant', sub) if sub else os.path.join(a.assets, 'variant')
        pal = []
        for tier in (1, 2):
            if vs[tier] == 1 and os.path.exists(os.path.join(vdir, sk + '.json')):
                p = json.load(open(os.path.join(vdir, sk + '.json')))
                for s0, d0 in p.get(str(tier), {}).items(): pal.append('%d %s %s' % (tier + 1, s0, d0))
            elif vs[tier] == 2 and copy(os.path.join(vdir, '%s_%d.png' % (sk, tier + 1)), os.path.join(vdir, '%s_%d.json' % (sk, tier + 1)), 's%d%s_%s' % (tier + 1, side, fid)): st['sep'] += 1
        if pal: open(os.path.join(a.out_txt, 'pal_%s_%s.txt' % (side, fid)), 'w').write('\n'.join(pal) + '\n'); st['pal'] += 1
    if not ok: st['missing'].append(fid)
print(st)
