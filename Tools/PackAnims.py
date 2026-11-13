#!/usr/bin/env python3
"""PokeRogue battle-anims → 모드용 데이터.
사용: PackAnims.py <resources/game 폴더> <모드 루트(Rimketmon/)>
  출력  Anims/anims.gz               기술 연출(프레임 셀 표 + 타이밍 효과음)
        Textures/Rimket/anim/*.png    연출 스프라이트 시트(96×96 셀, 이름은 안전한 문자로)
        AnimSfx/*.ogg                 연출 효과음(ffmpeg, 모노 q2)
"""
import sys, os, re, json, gzip, subprocess, glob
game, mod = sys.argv[1], sys.argv[2]
def safe(n):
    r_ = re.sub(r'[^a-z0-9]+', '_', n.lower()).strip('_')
    return r_ if r_ else 'g' + n.encode('utf-8').hex()
def norm(n): return re.sub(r'[^a-z0-9]', '', n.lower())
os.makedirs(mod + '/Anims', exist_ok=True); os.makedirs(mod + '/Textures/Rimket/anim', exist_ok=True); os.makedirs(mod + '/AnimSfx', exist_ok=True)
sheets, sfx = set(), set()
def r(x): return ('%.2f' % x).rstrip('0').rstrip('.') if isinstance(x, float) else str(int(x))
out = []
count = 0
for f in sorted(glob.glob(game + '/battle-anims/*.json')):
    d = json.load(open(f, encoding='utf-8'))
    if isinstance(d, list):   # 기술 연출 311개는 [ {…} ] 목록 꼴이다. 첫 번째 연출을 쓴다
        d = next((x for x in d if isinstance(x, dict) and x.get('frames') and 'graphic' in x), None)
        if d is None: continue
    if not isinstance(d, dict): continue
    if 'frames' not in d or not d['frames'] or 'graphic' not in d: continue
    key = norm(os.path.basename(f)[:-5])
    g = safe(d['graphic']); sheets.add(d['graphic'])
    out.append('A\t%s\t%s\t%d' % (key, g, len(d['frames'])))
    for fr in d['frames']:
        out.append('F\t%d' % len(fr))
        for c in fr:
            tone = c.get('tone') or [0, 0, 0, 0]
            out.append(' '.join(r(v) for v in (
                c.get('target', 2), c.get('focus', 1), c.get('x', 0), c.get('y', 0),
                c.get('zoomX', 100), c.get('zoomY', 100), c.get('angle', 0), 1 if c.get('mirror') else 0,
                1 if c.get('visible') else 0, c.get('blendType', 0), c.get('graphicFrame', 0),
                c.get('opacity', 0), c.get('priority', 0), 1 if c.get('locked') else 0,
                tone[0], tone[1], tone[2], tone[3])))
    for k, evs in (d.get('frameTimedEvents') or {}).items():
        for e in evs:
            if e['eventType'] == 'AnimTimedSoundEvent' and e.get('resourceName'):
                n = e['resourceName']; base = os.path.splitext(n)[0]
                sfx.add(n)
                out.append('E\t%s\t%s\t%s\t%s' % (k, safe(base), r(e.get('volume', 100)), r(e.get('pitch', 100))))
    count += 1
with gzip.open(mod + '/Anims/anims.gz', 'wt', encoding='utf-8') as o: o.write('\n'.join(out) + '\n')
print('anims', count)

from PIL import Image
n = 0
for s in sheets:
    p = game + '/images/battle_anims/' + s + '.png'
    if os.path.exists(p):
        Image.open(p).convert('RGBA').save(mod + '/Textures/Rimket/anim/' + safe(s) + '.png', optimize=True); n += 1
    else: print('missing sheet', s)
print('sheets', n)
m = 0
for s in sorted(sfx):
    p = game + '/audio/battle_anims/' + s
    if not os.path.exists(p):
        alt = [x for x in glob.glob(game + '/audio/battle_anims/' + os.path.splitext(s)[0] + '.*')]
        if not alt: print('missing sfx', s); continue
        p = alt[0]
    dst = mod + '/AnimSfx/' + safe(os.path.splitext(s)[0]) + '.ogg'
    if os.path.exists(dst): continue
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', p, '-ac', '1', '-ar', '32000', '-c:a', 'libvorbis', '-q:a', '2', dst], check=False); m += 1
print('sfx converted', m)
