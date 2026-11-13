#!/usr/bin/env python3
"""PkAnim 폴더의 낱개 .txt(프레임 표·색 바꾸기 표·이로치 목록)를 PkAnim/pack.gz 하나로 묶는다(모드는 묶음을 먼저 읽는다).
사용: python3 -I PackPkAnimBundle.py <Rimketmon/PkAnim> [--remove]   (--remove: 묶은 뒤 낱개 파일 삭제)"""
import gzip, os, sys
d = sys.argv[1]
names = sorted(f for f in os.listdir(d) if f.endswith('.txt'))
out = []
for f in names:
    t = open(os.path.join(d, f), encoding='utf-8').read().replace('\r\n', '\n')
    assert not any(l.startswith('@') for l in t.split('\n')), f
    out.append('@' + f[:-4] + '\n' + (t if t.endswith('\n') or not t else t + '\n'))
with gzip.open(os.path.join(d, 'pack.gz'), 'wt', encoding='utf-8', compresslevel=9) as g:
    g.write(''.join(out))
print('묶음:', len(names), '개 →', os.path.getsize(os.path.join(d, 'pack.gz')), 'bytes')
if '--remove' in sys.argv:
    for f in names: os.remove(os.path.join(d, f))
