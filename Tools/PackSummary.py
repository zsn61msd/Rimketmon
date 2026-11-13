#!/usr/bin/env python3
"""요약/파티 화면용 한글 텍스트 이미지와 한글 타입·상태 아이콘 아틀라스를 Textures/Rimket/ui 로 푼다.
사용: PackSummary.py <images 폴더(types_ko.png 위치)> <ui/text_images/ko 폴더> <Rimketmon/Textures/Rimket>"""
import json, os, sys, shutil
from PIL import Image
img, tko, out = sys.argv[1:4]
ui = os.path.join(out, 'ui'); os.makedirs(ui, exist_ok=True)
def atlas(name, prefix):
    sheet = Image.open(os.path.join(img, name + '.png')).convert('RGBA')
    j = json.load(open(os.path.join(img, name + '.json'), encoding='utf-8'))
    frames = []
    for t in j.get('textures', []): frames += t['frames']
    for f in frames:
        fr, ss, sz = f['frame'], f['spriteSourceSize'], f['sourceSize']
        crop = sheet.crop((fr['x'], fr['y'], fr['x']+fr['w'], fr['y']+fr['h']))
        full = Image.new('RGBA', (sz['w'], sz['h']), (0,0,0,0)); full.paste(crop, (ss['x'], ss['y']))
        full.save(os.path.join(ui, '%s__%s.png' % (prefix, os.path.splitext(f['filename'])[0])))
atlas('types_ko', 'types_ko')
atlas('statuses_ko', 'statuses_ko')
n = 0
for sub in ('summary', 'party_ui'):
    d = os.path.join(tko, sub)
    for f in os.listdir(d):
        if f.endswith('.png'):
            shutil.copy(os.path.join(d, f), os.path.join(ui, f.replace('_ko.png', '_ko.png'))); n += 1
print('copied', n)
