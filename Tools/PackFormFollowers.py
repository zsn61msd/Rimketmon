#!/usr/bin/env python3
"""지도에서 따라다니는 그림(팬게임 Graphics/Characters/Followers, 4×4 걷기 시트): 폼 종족은 팬게임의 폼 번호 그림(ROTOM_2 …)을,
이로치는 'Followers shiny'를 옮긴다. → Textures/Rimket/follow/<ID>.png, follow_s/<ID>.png
사용: PackFormFollowers.py <팬게임 폴더> Engine/Data/FormData.cs Engine/Data/GameData.cs Rimketmon/Textures/Rimket"""
import os, re, shutil, sys
game, forms_cs, gd_cs, tex = sys.argv[1:5]
def norm(s): return re.sub(r'[^A-Z0-9]', '', s.upper())
txt = ''
for fn in ('pokemon_forms.txt', 'pokemon_forms_Gen_9_Pack.txt'):
    p = os.path.join(game, 'PBS', fn)
    if os.path.exists(p): txt += open(p, encoding='utf-8-sig').read() + '\n'
names = {}; cur = None
for l in txt.split('\n'):
    m = re.match(r'\[(\w+),(\d+)\]', l.strip())
    if m: cur = (m.group(1), int(m.group(2))); continue
    if cur and l.startswith('FormName'): names.setdefault(cur[0], []).append((cur[1], l.split('=', 1)[1].strip()))
fol, fols = os.path.join(game, 'Graphics/Characters/Followers'), os.path.join(game, 'Graphics/Characters/Followers shiny')
os.makedirs(os.path.join(tex, 'follow_s'), exist_ok=True)
t = open(forms_cs, encoding='utf-8').read()
rows = re.findall(r'"([A-Z0-9_]+)\|([A-Za-z0-9_]+)\|(\d+)\|([0-9a-z\-]+)"', t[t.find('FormBaseRows'):t.find('FormCatchRows')])
REG = {'ALOLA': 'ALOLAN', 'GALAR': 'GALARIAN', 'HISUI': 'HISUIAN', 'PALDEA': 'PALDEAN', 'BLOODMOON': 'BLOODMOON', 'ETERNAL': 'ETERNAL'}
ok, miss = 0, []
for fid, base, num, sk in rows:
    cands = names.get(base, [])
    if '-' in sk:   # 같은 종의 다른 모습
        key = norm(sk.split('-', 1)[1])
        if fid.startswith('PALDEA_TAUROS'): key = norm(sk.split('-', 1)[1]); cands = names.get('TAUROS', [])
        hit = [i for i, n in cands if key and key in norm(n)]
    else:           # 지역폼 등
        pre = fid.split('_')[0]
        hit = [i for i, n in cands if REG.get(pre, '~') in norm(n)]
        if fid == 'PALDEA_TAUROS': hit = [i for i, n in names.get('TAUROS', []) if 'COMBAT' in norm(n)]; base = 'TAUROS'
    if fid.startswith('PALDEA_TAUROS'): base = 'TAUROS'
    if not hit: miss.append(fid); continue
    src = os.path.join(fol, '%s_%d.png' % (base, hit[0]))
    if not os.path.exists(src): miss.append(fid); continue
    shutil.copyfile(src, os.path.join(tex, 'follow', fid + '.png')); ok += 1
    ss = os.path.join(fols, '%s_%d.png' % (base, hit[0]))
    if os.path.exists(ss): shutil.copyfile(ss, os.path.join(tex, 'follow_s', fid + '.png'))
# 일반 종족의 이로치 걷기 그림
n = 0
for f in os.listdir(os.path.join(tex, 'follow')):
    s = os.path.join(fols, f)
    if os.path.exists(s) and not os.path.exists(os.path.join(tex, 'follow_s', f)): shutil.copyfile(s, os.path.join(tex, 'follow_s', f)); n += 1
print('forms', ok, 'missing', miss, 'shiny', n)
