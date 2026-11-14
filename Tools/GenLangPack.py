#!/usr/bin/env python3
"""PokeRogue locales/<언어> → 언어 묶음 글자 파일(종족·기술·특성·성격·전투 메시지).
사용: GenEnData.py <pokerogue locales/en 폴더> <Rimketmon/Languages/English/Rimketmon 폴더>
다른 언어도 같은 방식: locales/fr → Languages/French/Rimketmon (ui.txt·patterns.txt·items.txt·language.txt 는 따로 번역)."""
import json, os, re, sys
src, out = sys.argv[1], sys.argv[2]
def norm(x): return re.sub(r'[^a-z0-9]', '', x.lower())
def c(x): return (x or '').replace('|', '/').replace('\n', ' ')
def q(s): return '"' + s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', ' ').replace('\t', ' ') + '"'
L = lambda f: json.load(open(os.path.join(src, f + '.json'), encoding='utf-8'))
rows = {'Species': [], 'Moves': [], 'Abilities': [], 'Natures': []}
for k, v in L('pokemon').items():
    if isinstance(v, str): rows['Species'].append(norm(k) + '|' + c(v))
for k, v in L('move').items(): rows['Moves'].append(norm(k) + '|' + c(v.get('name', '')) + '|' + c(v.get('effect')))
for k, v in L('ability').items(): rows['Abilities'].append(norm(k) + '|' + c(v.get('name', '')) + '|' + c(v.get('description')))
nat = L('nature'); order = ['hardy','lonely','brave','adamant','naughty','bold','docile','relaxed','impish','lax','timid','hasty','serious','jolly','naive','modest','mild','quiet','bashful','rash','calm','gentle','sassy','careful','quirky']
rows['Natures'] = [nat[k] for k in order]
FILES = ['battle', 'move-trigger', 'ability-trigger', 'battler-tags', 'status-effect', 'weather', 'terrain', 'arena-tag', 'battle-info', 'fight-ui-handler', 'command-ui-handler', 'party-ui-handler', 'pokemon-summary', 'nature']
msgs = []
def walk(x, p):
    if isinstance(x, dict):
        for k, v in x.items(): yield from walk(v, p + '.' + k)
    elif isinstance(x, str): yield p, x
for f in FILES:
    for k, v in walk(L(f), f): msgs.append(k + '\t' + v.replace('\n', ' ').replace('\t', ' '))
os.makedirs(out, exist_ok=True)
def esc(x): return x.replace('\\', '\\\\').replace('\t', '\\t').replace('\n', '\\n')
def write(fn, head, lines):
    with open(os.path.join(out, fn), 'w', encoding='utf-8', newline='\n') as o:
        o.write(head)
        for r in lines: o.write(r + '\n')
write('species.txt', '# 종족 이름: 키<TAB>이름  (키 = 영문 id를 소문자·숫자만 남긴 것)\n', [r.replace('|', '\t') for r in rows['Species']])
write('moves.txt', '# 기술: 키<TAB>이름<TAB>설명\n', [r.replace('|', '\t') for r in rows['Moves']])
write('abilities.txt', '# 특성: 키<TAB>이름<TAB>설명\n', [r.replace('|', '\t') for r in rows['Abilities']])
write('natures.txt', '# 성격 25개(이 순서 그대로): ' + ' '.join(order) + '\n', rows['Natures'])
write('messages.txt', '# 포켓로그 메시지: 키<TAB>문장  ({{이름}} 자리는 그대로 둔다)\n', msgs)
