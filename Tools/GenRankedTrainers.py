#!/usr/bin/env python3
"""랭크배틀용 일반 트레이너 목록 + 스프라이트 생성. 파티 구성은 나중에 조사해 채운다(지금은 스프라이트/이름만).
사용: GenRankedTrainers.py --pr <images 폴더> --locale <ko 로케일 폴더> --out Rimketmon/Textures/Rimket --cs Engine/Data/RankedTrainerData.cs
"""
import argparse, json, os, re, sys
from PIL import Image
ap = argparse.ArgumentParser()
ap.add_argument('--pr', required=True); ap.add_argument('--locale', required=True)
ap.add_argument('--out', required=True); ap.add_argument('--cs', required=True)
a = ap.parse_args()

def save(im, rel):
    p = os.path.join(a.out, re.sub(r'[^A-Za-z0-9_/\-]', '_', rel) + '.png')
    os.makedirs(os.path.dirname(p), exist_ok=True); im.save(p)

def slice_atlas(png, js, prefix):
    sheet = Image.open(png).convert('RGBA'); j = json.load(open(js, encoding='utf-8'))
    frames = []
    if 'textures' in j:
        for t in j['textures']: frames.extend(t['frames'])
    elif isinstance(j.get('frames'), dict):
        for k, v in j['frames'].items(): v = dict(v); v['filename'] = k; frames.append(v)
    else: frames = j.get('frames', [])
    for n, f in enumerate(frames):
        fr = f['frame']; ss = f['spriteSourceSize']; sz = f['sourceSize']
        crop = sheet.crop((fr['x'], fr['y'], fr['x'] + fr['w'], fr['y'] + fr['h']))
        if f.get('rotated'): crop = crop.transpose(Image.ROTATE_90)
        full = Image.new('RGBA', (sz['w'], sz['h']), (0, 0, 0, 0)); full.paste(crop, (ss['x'], ss['y']))
        name = os.path.splitext(str(f.get('filename', '')))[0] or ('%02d' % n)
        save(full, '%s__%s' % (prefix, name.replace('/', 'slash')))

cls = json.load(open(os.path.join(a.locale, 'trainer-classes.json'), encoding='utf-8'))
names = json.load(open(os.path.join(a.locale, 'trainer-names.json'), encoding='utf-8'))
gd = open(os.path.join(os.path.dirname(a.cs), 'TrainerData.cs'), encoding='utf-8').read()
leaders = {m.lower() for m in re.findall(r'"([A-Z_0-9]+)\|[^|]*\|\d+\|(?:true|false)\|', gd)}
SKIP = re.compile(r'^(trainer_|player_|rival_|input$|unknown_|interviewers$|rocket_boss|halloween|jacinthe|youngster_)')
def camel(s):
    p = s.split('_'); return p[0] + ''.join(x.capitalize() for x in p[1:])
rows = []
for f in sorted(os.listdir(a.pr)):
    if not f.endswith('.png'): continue
    sid = f[:-4]
    if sid in leaders or SKIP.match(sid) or not os.path.exists(os.path.join(a.pr, sid + '.json')): continue
    female = sid.endswith('_f'); base = sid[:-2] if sid.endswith(('_f', '_m')) else sid
    base = re.sub(r'_\d+$', '', base)
    ck = camel(base) + ('Female' if female else '')
    ko = names.get(camel(sid)) or cls.get(ck) or cls.get(camel(base)) or base.replace('_', ' ')
    named = camel(sid) in names
    male = not female
    slice_atlas(os.path.join(a.pr, sid + '.png'), os.path.join(a.pr, sid + '.json'), 'trainer/' + sid)
    rows.append('%s|%s|%s|%s' % (sid.upper(), ko, 'true' if male else 'false', 'named' if named else 'class'))
with open(a.cs, 'w', encoding='utf-8') as o:
    o.write('namespace Rimketmon.Engine\n{\n    /// <summary>랭크배틀 일반 트레이너(스프라이트/이름만). 파티 구성은 추후 조사해 채운다. 형식: ID|표시 이름|남성여부|named(고유 인물)/class(직업)</summary>\n    public static class RankedTrainerData\n    {\n        public static readonly string[] Rows =\n        {\n')
    for r in rows: o.write('            "%s",\n' % r.replace('"', ''))
    o.write('        };\n    }\n}\n')
print(len(rows), 'trainers')
