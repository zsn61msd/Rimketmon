#!/usr/bin/env python3
"""
림켓몬 배틀 UI 에셋 패키저.
PokeRogue 에셋(아레나/UI/트레이너/볼)과 팬게임 스프라이트를 림월드 Textures 폴더용 개별 PNG로 변환한다.
- 아틀라스(json)는 프레임별 PNG로 풀고, trim된 프레임은 sourceSize로 복원한다(발 기준 정렬 유지).
- 포켓몬 스프라이트는 포켓로그 애니메이션 아틀라스(PackPokemonAnim.py)만 쓴다.
"""
import argparse, json, os, re, shutil
from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument('--pr', required=True)        # .../resources/game/images
ap.add_argument('--fan', default=None)       # (더 이상 쓰지 않음: 포켓몬 그림은 포켓로그 pk/ 아틀라스만 쓴다)
ap.add_argument('--out', required=True)       # Rimketmon/Textures/Rimket
ap.add_argument('--gamedata', required=True)  # Engine/Data/GameData.cs
ap.add_argument('--trainerdata', required=True)
a = ap.parse_args()

def save(im, rel):
    rel = re.sub(r'[^A-Za-z0-9_/\-]', '_', rel)
    p = os.path.join(a.out, rel + '.png')
    os.makedirs(os.path.dirname(p), exist_ok=True)
    try: im.save(p)
    except Exception as e: raise SystemExit('FAIL %r %r' % (rel, p))

def slice_atlas(png, js, prefix, only_first=False):
    sheet = Image.open(png).convert('RGBA')
    j = json.load(open(js, encoding='utf-8'))
    frames = []
    if 'textures' in j:
        for t in j['textures']: frames.extend(t['frames'])
    elif isinstance(j.get('frames'), dict):
        for k, v in j['frames'].items(): v = dict(v); v['filename'] = k; frames.append(v)
    else:
        frames = j.get('frames', [])
    n = 0
    for f in frames:
        fr = f['frame']; ss = f['spriteSourceSize']; sz = f['sourceSize']
        crop = sheet.crop((fr['x'], fr['y'], fr['x'] + fr['w'], fr['y'] + fr['h']))
        if f.get('rotated'): crop = crop.transpose(Image.ROTATE_90)
        full = Image.new('RGBA', (sz['w'], sz['h']), (0, 0, 0, 0))
        full.paste(crop, (ss['x'], ss['y']))
        name = os.path.splitext(str(f.get('filename', '')))[0] or ('%02d' % n)
        name = name.replace('/', 'slash')
        save(full, '%s__%s' % (prefix, name))
        n += 1
        if only_first: break
    return n

# ---------------------------------------------------------------- 아레나(바이옴 배경)
arenas = os.path.join(a.pr, 'arenas')
cnt = 0
for f in sorted(os.listdir(arenas)):
    if not f.endswith('.png'): continue
    base = f[:-4]
    if not re.fullmatch(r'[a-z_]+?_(bg|a|b)', base): continue   # 소품(_b_1 등)은 제외
    js = os.path.join(arenas, base + '.json')
    if os.path.exists(js): slice_atlas(os.path.join(arenas, f), js, 'arena/' + base, only_first=True); 
    else: save(Image.open(os.path.join(arenas, f)).convert('RGBA'), 'arena/' + base)
    cnt += 1
print('arena', cnt)

# ---------------------------------------------------------------- UI
ui = os.path.join(a.pr, 'ui')
cnt = 0
for f in sorted(os.listdir(ui)):
    p = os.path.join(ui, f)
    if f.endswith('.png'):
        base = f[:-4]
        if base.endswith('_oauth') or 'overworld' in base or base.endswith('_icon_bg') : continue
        js = os.path.join(ui, base + '.json')
        if os.path.exists(js): slice_atlas(p, js, 'ui/' + base)
        else: save(Image.open(p).convert('RGBA'), 'ui/' + base)
        cnt += 1
for f in os.listdir(os.path.join(ui, 'windows')):
    if f.endswith('.png'): save(Image.open(os.path.join(ui, 'windows', f)).convert('RGBA'), 'ui/windows/' + f[:-4])
print('ui', cnt)

# 이미지 루트의 공용 아틀라스: 몬스터볼, 타입/분류 아이콘, 상태 아이콘
root = os.path.dirname(ui)
for name in ['pb', 'categories', 'pbinfo_stat_numbers']:
    p = os.path.join(root, name + '.png'); js = os.path.join(root, name + '.json')
    if os.path.exists(p) and os.path.exists(js): slice_atlas(p, js, 'atlas/' + name)

# ---------------------------------------------------------------- 트레이너(관장 + 플레이어 뒷모습)
tr = os.path.join(a.pr, 'trainer')
td = open(a.trainerdata, encoding='utf-8').read()
ids = re.findall(r'"([A-Z_0-9]+)\|[^|]*\|\d+\|(?:true|false)\|', td)
want = set(i.lower() for i in ids) | {'trainer_m_back', 'trainer_f_back', 'trainer_m_back_pb', 'trainer_f_back_pb', 'youngster_m', 'youngster_f'}
cnt = 0
for name in sorted(want):
    p = os.path.join(tr, name + '.png'); js = os.path.join(tr, name + '.json')
    if not (os.path.exists(p) and os.path.exists(js)): print('missing trainer', name); continue
    slice_atlas(p, js, 'trainer/' + name)
    cnt += 1
print('trainer', cnt)

# ---------------------------------------------------------------- 포켓몬(팬게임 스프라이트)
gd = open(a.gamedata, encoding='utf-8').read()
sp = re.findall(r'^\s*"([A-Z0-9_]+)\|', gd, re.M)
sp_ids = []
sec = gd[gd.index('SpeciesRows'):]
sec = sec[:sec.index('};')] if '};' in sec else sec
sp_ids = re.findall(r'"([A-Z0-9_]+)\|[^|"]*\|\d+\|', sec)
front = back = '/nonexistent'      # 팬게임 그림은 쓰지 않는다
nf = nb = 0; missing = []
for sid in sp_ids:
    ok = False
    for d, suffix, k in ((front, 'f', 'nf'), (back, 'b', 'nb')):
        p = os.path.join(d, sid + '.png')
        if os.path.exists(p):
            save(Image.open(p).convert('RGBA'), 'mon/%s_%s' % (sid, suffix)); ok = True
            if suffix == 'f': nf += 1
            else: nb += 1
    if not ok: missing.append(sid)
print('mon front', nf, 'back', nb, 'missing', len(missing), missing[:20])
