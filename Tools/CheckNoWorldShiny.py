"""월드(지도) 걷는 그림에서 이로치판이 일반판과 픽셀이 똑같은 종을 찾아,
ModSource/Window_RimketMenu.Card.cs 의 NoWorldShiny 목록과 비교한다. 다르면 종료 코드 1.
사용: python3 Tools/CheckNoWorldShiny.py"""
import os, re, sys
from PIL import Image
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
tex = os.path.join(root, "Rimketmon", "Textures", "Rimket")
same = set()
for f in os.listdir(os.path.join(tex, "follow")):
    b = os.path.join(tex, "follow_s", f)
    if not os.path.exists(b):
        same.add(f[:-4]); continue
    A = Image.open(os.path.join(tex, "follow", f)).convert("RGBA"); B = Image.open(b).convert("RGBA")
    if A.size == B.size and A.tobytes() == B.tobytes(): same.add(f[:-4])
src = open(os.path.join(root, "ModSource", "Window_RimketMenu.Card.cs"), encoding="utf-8").read()
m = re.search(r"NoWorldShiny = new HashSet<string> \{([^}]*)\}", src)
listed = set(re.findall(r'"([A-Z0-9_]+)"', m.group(1))) if m else set()
print("그림 비교:", sorted(same)); print("코드 목록:", sorted(listed))
sys.exit(0 if same == listed else 1)
