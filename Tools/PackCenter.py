#!/usr/bin/env python3
"""pokefirered(decomp) 의 포켓몬 센터 1F / 마트 맵을 림켓몬용 그림 + 데이터로 바꾼다.
사용: python3 PackCenter.py <pokefirered 폴더> <림켓몬 저장소 폴더>
출력: Rimketmon/Textures/Rimket/center/*.png, ModSource/CenterData.cs
"""
import json, os, struct, sys
from PIL import Image

src, out = sys.argv[1], sys.argv[2]
tex = os.path.join(out, 'Rimketmon/Textures/Rimket/center')
os.makedirs(tex, exist_ok=True)

def pal(path):
    L = open(path).read().split('\n')
    n = int(L[2]); cols = []
    for i in range(n):
        r, g, b = [int(x) for x in L[3 + i].split()]
        cols.append((r, g, b, 255))
    return cols

def load_tileset(prim, sec):
    ts = {}
    for key, folder in (('p', prim), ('s', sec)):
        base = os.path.join(src, 'data/tilesets', folder)
        im = Image.open(os.path.join(base, 'tiles.png'))
        idx = list(im.getdata()); w, h = im.size
        meta = struct.unpack('<%dH' % (os.path.getsize(base + '/metatiles.bin') // 2), open(base + '/metatiles.bin', 'rb').read())
        attr = struct.unpack('<%dI' % (os.path.getsize(base + '/metatile_attributes.bin') // 4), open(base + '/metatile_attributes.bin', 'rb').read())
        pals = [pal(os.path.join(base, 'palettes/%02d.pal' % i)) for i in range(16)]
        ts[key] = dict(idx=idx, w=w, h=h, meta=meta, attr=attr, pals=pals)
    return ts

def tile_img(ts, tile, pal_i, hflip, vflip):
    s = ts['p'] if tile < 640 else ts['s']
    t = tile if tile < 640 else tile - 640
    pals = ts['p']['pals'] if pal_i < 7 else ts['s']['pals']
    pl = pals[pal_i]
    tx, ty = (t % 16) * 8, (t // 16) * 8
    im = Image.new('RGBA', (8, 8), (0, 0, 0, 0))
    px = im.load()
    for y in range(8):
        for x in range(8):
            v = s['idx'][(ty + y) * s['w'] + tx + x]
            if v == 0: continue
            px[x, y] = pl[v]
    if hflip: im = im.transpose(Image.FLIP_LEFT_RIGHT)
    if vflip: im = im.transpose(Image.FLIP_TOP_BOTTOM)
    return im

def build(name, layout_dir, prim, sec, W, H):
    ts = load_tileset(prim, sec)
    data = struct.unpack('<%dH' % (W * H), open(os.path.join(src, 'data/layouts', layout_dir, 'map.bin'), 'rb').read())
    below = Image.new('RGBA', (W * 16, H * 16), (0, 0, 0, 0))
    above = Image.new('RGBA', (W * 16, H * 16), (0, 0, 0, 0))
    cache = {}
    passrows, counter, pc = [], [], []
    for y in range(H):
        pr = cr = pr2 = ''
        for x in range(W):
            v = data[y * W + x]
            m = v & 0x3ff; col = (v >> 10) & 3
            s = ts['p'] if m < 640 else ts['s']
            mi = m if m < 640 else m - 640
            ent = s['meta'][mi * 8:(mi + 1) * 8]
            at = s['attr'][mi] if mi < len(s['attr']) else 0
            beh = at & 0x1ff; layer = (at >> 29) & 3
            for i in range(8):
                e = ent[i]
                key = (e & 0x3ff, (e >> 12) & 15, (e >> 10) & 1, (e >> 11) & 1)
                if key not in cache: cache[key] = tile_img(ts, *key)
                ti = cache[key]
                dx, dy = x * 16 + (i % 2) * 8, y * 16 + ((i // 2) % 2) * 8
                top = i >= 4
                (above if (top and layer != 1) else below).alpha_composite(ti, (dx, dy))
            pr += '1' if col else '0'
            cr += '1' if beh == 0x80 else '0'
            pr2 += '1' if beh == 0x83 else '0'
        passrows.append(pr); counter.append(cr); pc.append(pr2)
    bg = Image.new('RGBA', below.size, (0, 0, 0, 255)); bg.alpha_composite(below); below = bg
    below.save(os.path.join(tex, name + '_below.png'))
    above.save(os.path.join(tex, name + '_above.png'))
    return W, H, passrows, counter, pc

cs = {}
cs['center'] = build('center', 'PokemonCenter_1F', 'primary/building', 'secondary/pokemon_center', 15, 10)
cs['mart'] = build('mart', 'Mart', 'primary/building', 'secondary/mart', 11, 9)
cs['union'] = build('union', 'UnionRoom', 'primary/building', 'secondary/hall_of_fame', 15, 12)   # 온라인 대기실(유니온룸)

# 캐릭터 시트(투명 처리) + 공 불빛
def conv(path, dst):
    im = Image.open(os.path.join(src, path)).convert('RGBA'); im.save(os.path.join(tex, dst))
conv('graphics/object_events/pics/people/nurse.png', 'nurse.png')
conv('graphics/object_events/pics/people/clerk.png', 'clerk.png')
conv('graphics/object_events/pics/people/black_belt.png', 'coach.png')   # 훈련 담당
conv('graphics/object_events/pics/people/union_room_receptionist.png', 'union_desk.png')   # 온라인 대기실 접수원
# 공 불빛은 팔레트 0번이 투명(원작)이라 검은 네모가 되지 않게 따로 변환한다
_g = Image.open(os.path.join(src, 'graphics/field_effects/pics/pokeball_glow.png')); _pal = _g.getpalette()
_o = Image.new('RGBA', _g.size, (0, 0, 0, 0))
for _y in range(_g.size[1]):
    for _x in range(_g.size[0]):
        _i = _g.getpixel((_x, _y))
        if _i: _o.putpixel((_x, _y), (_pal[_i * 3], _pal[_i * 3 + 1], _pal[_i * 3 + 2], 255))
_o.save(os.path.join(tex, 'ball_glow.png'))

with open(os.path.join(out, 'ModSource/CenterData.cs'), 'w', encoding='utf-8') as f:
    f.write('// PackCenter.py가 만든 파일 — 직접 고치지 마세요. (pokefirered 센터/마트: 막힘 1, 카운터, PC)\nnamespace Rimketmon\n{\n    internal static class CenterData\n    {\n')
    for name, (W, H, p, c, pc) in cs.items():
        f.write('        public const int %sW = %d, %sH = %d;\n' % (name, W, name, H))
        for label, rows in (('Pass', p), ('Counter', c), ('Pc', pc)):
            f.write('        public static readonly string[] %s%s =\n        {\n' % (name, label))
            for r in rows: f.write('            "%s",\n' % r)
            f.write('        };\n')
    f.write('    }\n}\n')
print('ok')
