#!/usr/bin/env python3
"""RimketFrame.cs 의 크기·위치 계산과 끌기 상태 기계(Begin/Tick/Step)를 그대로 옮겨 무작위로 두드려 보는 검증 스크립트.
림월드 없이 돌린다(표준 라이브러리만). C# 코드를 고치면 여기도 같이 고쳐야 한다 — 상수와 식이 RimketFrame.cs 와 같아야 의미가 있다.
실행: python3 Tools/RimketFrameSim.py
검사 1~5: 크기·위치 계산(저장값이 이상해도, 해상도가 바뀌어도, 끌어도 항상 정상)
검사 6  : 프레임 루프 시뮬레이션 — Update(Tick/Step) → OnGUI(Begin: 마우스 기록) 순서를 그대로 흉내 내서
          클릭만 한 경우(저장 없음)·끌기·창 밖까지 끌기·끌다가 창이 닫힘·창이 둘 겹침을 검사한다.
"""
import math, random, sys

# ---- RimketFrame.cs 상수
BAR, BOTTOM, SIDE, MININNER, KEEP, GRIPW, RESETW = 16.0, 12.0, 2.0, 400.0, 120.0, 16.0, 84.0

# ---- 유니티 Mathf 의미 그대로(NaN 처리까지)
def mmin(a, b): return a if a < b else b
def mmax(a, b): return a if a > b else b
def mclamp(v, lo, hi):
    if v < lo: v = lo
    elif v > hi: v = hi
    return v
def mround(v):                                    # Mathf.Round = 짝수 반올림(NaN/Infinity는 그대로)
    return v if (math.isnan(v) or math.isinf(v)) else float(round(v))

class Settings:
    def __init__(self, w=-1.0, cx=-1.0, cy=-1.0): self.winW, self.winCx, self.winCy = w, cx, cy
    def Write(self): self.writes = getattr(self, 'writes', 0) + 1

class Screen:
    def __init__(self, w, h): self.w, self.h = float(w), float(h)

def max_inner(sc): return mmin(sc.w - 2*SIDE, (sc.h - BAR - BOTTOM) * 16.0 / 9.0)
def default_inner(sc): return mmin(sc.w - 40.0 - 2*SIDE, (sc.h - 40.0 - BAR - BOTTOM) * 16.0 / 9.0)
def clamp_inner(sc, w):
    mx = max_inner(sc)
    if not (w > -1e30 and w < 1e30): w = mx          # NaN·±무한대
    return mmin(mx, mmax(mmin(MININNER, mx), w))
def outer_h(inner): return BAR + inner * 9.0 / 16.0 + BOTTOM
def custom(s):
    return s is not None and s.winW > 0 and s.winCx > -10 and s.winCx < 10 and s.winCy > -10 and s.winCy < 10

def clamp_pos(sc, r):
    x, y, w, h = r
    keep = mmin(KEEP, w)
    x = mclamp(x, -(w - keep), sc.w - keep)
    y = mclamp(y, 0.0, mmax(0.0, sc.h - BAR))
    return (mround(x), mround(y), mround(w), mround(h))

def outer(s, sc):
    cu = custom(s)
    inner = clamp_inner(sc, s.winW * sc.w - 2*SIDE if cu else default_inner(sc))
    ow, oh = inner + 2*SIDE, outer_h(inner)
    cx = s.winCx * sc.w if cu else sc.w / 2.0
    cy = s.winCy * sc.h if cu else sc.h / 2.0
    return clamp_pos(sc, (cx - ow/2.0, cy - oh/2.0, ow, oh))

def store(s, sc, r):
    w, h = mmax(1.0, sc.w), mmax(1.0, sc.h)
    s.winW = r[2] / w; s.winCx = (r[0] + r[2]/2.0) / w; s.winCy = (r[1] + r[3]/2.0) / h

def compute_drag(s, sc, mode, start_outer, dx, dy):
    """Step()의 계산부. 반환: 새로 저장한 사각형."""
    if mode == 'move':
        r = (start_outer[0] + dx, start_outer[1] + dy, start_outer[2], start_outer[3])
    else:
        start_inner = start_outer[2] - 2*SIDE
        inner = clamp_inner(sc, start_inner + (dx + dy * 9.0/16.0) / (1.0 + 81.0/256.0))
        nw, nh = inner + 2*SIDE, outer_h(inner)
        r = (mmin(start_outer[0], sc.w - nw), mmin(start_outer[1], sc.h - nh), nw, nh)
    rr = clamp_pos(sc, r)
    store(s, sc, rr)
    return rr

# ---- 끌기 상태 기계(RimketFrame.Tick / Step / EndDrag / StartDrag / Begin 의 마우스 기록 부분)
class Frame:
    def __init__(self, stale=True, deadzone=True):
        self.mode = None; self.moved = False; self.start_mouse = (0.0, 0.0); self.cur_mouse = (0.0, 0.0)
        self.start_outer = None; self.last_tick = -1; self.stale = stale; self.deadzone = deadzone
    def end_drag(self, s):
        save = self.mode is not None and self.moved
        self.mode = None; self.moved = False
        if save and s is not None: s.Write()
    def step(self, s, sc, button_down):
        if self.mode is None: return
        if s is None or not button_down: self.end_drag(s); return
        dx, dy = self.cur_mouse[0] - self.start_mouse[0], self.cur_mouse[1] - self.start_mouse[1]
        if not self.moved:
            if self.deadzone and abs(dx) < 2.0 and abs(dy) < 2.0: return
            self.moved = True
        compute_drag(s, sc, self.mode, self.start_outer, dx, dy)
    def tick(self, frame, s, sc, button_down):          # WindowUpdate → RimketFrame.Tick (프레임당 Step은 한 번)
        if self.last_tick != frame:
            if self.stale and self.mode is not None and frame - self.last_tick > 5: self.end_drag(s)
            self.last_tick = frame; self.step(s, sc, button_down)
        return outer(s, sc)                              # 창 사각형으로 적용
    @staticmethod
    def root(win_rect, local): return (local[0] + win_rect[0], local[1] + win_rect[1])
    def start_drag(self, mode, win_rect, local, s, sc):
        self.mode = mode; self.moved = False; self.start_outer = outer(s, sc)
        self.start_mouse = self.cur_mouse = self.root(win_rect, local)
    def repaint(self, win_rect, local):                  # Begin(): 끌기 중 Repaint/MouseDrag 때 마우스 기록
        if self.mode is not None: self.cur_mouse = self.root(win_rect, local)

# ------------------------------------------------------------------ 검증
fails = []
checks = 0
def check(cond, msg, ctx):
    global checks
    checks += 1
    if not cond: fails.append((msg, ctx))

SCREENS = [(1920,1080),(2560,1440),(3840,2160),(1366,768),(1280,720),(1024,768),(800,600),(640,480),
           (1920,1200),(2560,1080),(3440,1440),(1080,1920),(1280,1024),(960,540),(1707,960),(2133,1200),(500,300)]
rng = random.Random(20261118)

def invariants(r, sc, ctx):
    x, y, w, h = r
    check(all(math.isfinite(v) for v in r), "유한하지 않은 값", ctx)
    mx = max_inner(sc)
    lo = mmin(MININNER, mx) + 2*SIDE
    check(w >= lo - 1.0 and w <= sc.w + 0.5, "가로 범위 벗어남 %s" % (r,), ctx)
    check(h <= sc.h + 1.0, "세로가 화면보다 큼 %s" % (r,), ctx)
    inner_w, inner_h = w - 2*SIDE, h - BAR - BOTTOM
    check(abs(inner_h - inner_w * 9.0/16.0) <= 1.0, "16:9 비율 어긋남 %s" % (r,), ctx)
    keep = mmin(KEEP, w)
    check(x + w >= keep - 0.5 and x <= sc.w - keep + 0.5, "위 줄이 화면 밖 %s" % (r,), ctx)
    check(y >= -0.5 and y <= mmax(0.0, sc.h - BAR) + 0.5, "위 줄 y가 화면 밖 %s" % (r,), ctx)
    if x <= 0:   # 가장 왼쪽 끝으로 보내도 [창 초기화] 버튼은 화면 안에 완전히 보여야 한다
        check(x + w - RESETW - 2 >= -0.5 or w < KEEP, "초기화 버튼이 왼쪽으로 잘림 %s" % (r,), ctx)

# 1) 저장값이 이상해도(NaN/inf/거대/음수/범위밖) 항상 정상 사각형
weird = [float('nan'), float('inf'), float('-inf'), 0.0, -1.0, -5.0, 1e-9, 0.5, 1.0, 1.7, 5.0, 1e9, -1e9, 9.99, 10.0, -10.0]
for sc_w, sc_h in SCREENS:
    sc = Screen(sc_w, sc_h)
    for a in weird:
        for b in weird:
            for c in weird:
                invariants(outer(Settings(a, b, c), sc), sc, ("weird", sc_w, sc_h, a, b, c))
    for _ in range(300):
        s = Settings(rng.uniform(-0.2, 1.5), rng.uniform(-2, 3), rng.uniform(-2, 3))
        invariants(outer(s, sc), sc, ("rand", sc_w, sc_h, s.winW, s.winCx, s.winCy))
    invariants(outer(Settings(), sc), sc, ("default", sc_w, sc_h))

# 2) 끌기 계산: 이동·크기 무작위 경로. 매 단계 정상 + 저장→다시 읽기가 같은 사각형(떨림·누적 오차 없음)
for sc_w, sc_h in SCREENS:
    sc = Screen(sc_w, sc_h)
    for trial in range(400):
        s = Settings() if rng.random() < 0.5 else Settings(rng.uniform(0.3, 1), rng.uniform(0, 1), rng.uniform(0, 1))
        start = outer(s, sc)
        mode = 'move' if rng.random() < 0.5 else 'resize'
        for step in range(rng.randint(1, 40)):
            dx, dy = rng.uniform(-3*sc_w, 3*sc_w), rng.uniform(-3*sc_h, 3*sc_h)
            if rng.random() < 0.3: dx, dy = rng.uniform(-60, 60), rng.uniform(-60, 60)
            last = compute_drag(s, sc, mode, start, dx, dy)
            invariants(last, sc, ("drag", mode, sc_w, sc_h, dx, dy))
            back = outer(s, sc)
            check(all(abs(a - b) <= 1.0 for a, b in zip(back, last)), "저장→읽기 값이 다름 %s vs %s" % (back, last), ("roundtrip", mode, sc_w, sc_h))
        check(outer(s, sc) == outer(s, sc), "Outer()가 호출마다 다름", ("idem",))

# 2b) 끌기 입력에 NaN/Infinity가 섞여도 창이 정상
for sc_w, sc_h in SCREENS:
    sc = Screen(sc_w, sc_h)
    for bad in (float('nan'), float('inf'), float('-inf')):
        for mode in ('move', 'resize'):
            for dx, dy in ((bad, 5.0), (5.0, bad)):
                s = Settings(); start = outer(s, sc)
                compute_drag(s, sc, mode, start, dx, dy); invariants(outer(s, sc), sc, ("badinput", mode, bad, sc_w, sc_h))

# 3) 크기 조절: 가장자리에 걸리지 않을 때 그립(오른쪽 아래 모서리)이 커서를 따라가야 한다
sc = Screen(1920, 1080)
for _ in range(500):
    s = Settings(0.5, 0.3, 0.3); start = outer(s, sc)
    t = rng.uniform(-150, 150); dx, dy = t, t * 9.0/16.0
    r = compute_drag(s, sc, 'resize', start, dx, dy)
    cur_corner = (start[0] + start[2] + dx, start[1] + start[3] + dy); new_corner = (r[0] + r[2], r[1] + r[3])
    if r[2] - 2*SIDE > MININNER + 1 and r[2] < sc.w - 5:
        check(abs(cur_corner[0] - new_corner[0]) <= 2.0 and abs(cur_corner[1] - new_corner[1]) <= 2.0, "그립이 커서를 못 따라감 %s %s" % (cur_corner, new_corner), ("grip", t))
s = Settings(); start = outer(s, sc); prev = start[2]
for dx in range(0, 400, 10):
    r = compute_drag(s, sc, 'resize', start, float(dx), 0.0)
    check(r[2] >= prev - 1.0, "가로 끌기에서 크기가 줄어듦", ("mono", dx)); prev = r[2]

# 4) 해상도(UI 배율)가 바뀐 뒤에도 정상: 큰 화면에서 저장 → 작은 화면에서 읽기
for a in SCREENS:
    for b in SCREENS:
        sa, sb = Screen(*a), Screen(*b)
        s = Settings(); start = outer(s, sa)
        for _ in range(20): compute_drag(s, sa, 'move', start, rng.uniform(-900, 900), rng.uniform(-500, 500))
        invariants(outer(s, sb), sb, ("rescale", a, b))

# 5) 초기화(-1)하면 기본 사각형으로 돌아온다
for sc_w, sc_h in SCREENS:
    sc = Screen(sc_w, sc_h); s = Settings(0.4, 0.9, 0.9)
    s.winW = s.winCx = s.winCy = -1.0
    check(outer(s, sc) == outer(Settings(), sc), "초기화 뒤 기본값이 아님", ("reset", sc_w, sc_h))
    d = outer(Settings(), sc); ctr = (d[0] + d[2]/2.0, d[1] + d[3]/2.0)
    check(abs(ctr[0] - sc_w/2.0) <= 1.0 and abs(ctr[1] - sc_h/2.0) <= 1.0 or max_inner(sc) < MININNER, "기본 위치가 가운데가 아님 %s" % (d,), ("center", sc_w, sc_h))
    old_w = mmin(sc_w - 40.0, (sc_h - 40.0) * 16.0/9.0); new_inner = d[2] - 2*SIDE
    check(new_inner >= old_w * 0.9 or old_w < 500, "기본 크기가 예전보다 너무 작아짐 %s -> %s" % (old_w, new_inner), ("size", sc_w, sc_h))

# 6) 프레임 루프 시뮬레이션: 한 프레임 = Update(Tick→Step→창 사각형 적용) 다음 OnGUI(Begin: 마우스 기록)
def gesture(sc, s, kind, d, u, path, windows=1, close=None, reopen=None, mut=None):
    """kind: move|resize. d=마우스를 누른 프레임, u=놓은 프레임(그 프레임 Update부터 버튼 false). path(k)=k프레임의 마우스 화면 좌표.
    close/reopen: 창이 모두 닫혀 있던 프레임 구간. 반환: (Frame, 시작 사각형, 매 프레임 창 사각형 목록)"""
    fr = Frame(**(mut or {})); start_rect = outer(s, sc)
    rects = []; last = u + 8
    for k in range(0, last + 1):
        is_open = not (close is not None and close <= k < reopen)
        down = d <= k < u
        if is_open:
            rect = fr.tick(k, s, sc, down)                # Update: 창 사각형 = 공용 값
            rects.append(rect)
            m = path(k)
            local = (m[0] - rect[0], m[1] - rect[1])      # OnGUI: 창 안쪽 로컬 마우스 위치
            if k == d:                                    # MouseDown(위 줄 또는 그립)
                fr.start_drag('move' if kind == 'move' else 'resize', rect, local, s, sc)
            for _ in range(windows):                      # 창이 여러 개 겹쳐도 같은 값을 기록한다
                fr.repaint(rect, local)
        else:
            rects.append(None)
    return fr, start_rect, rects

for sc_w, sc_h in SCREENS:
    sc = Screen(sc_w, sc_h)
    for trial in range(300):
        s = Settings() if rng.random() < 0.5 else Settings(rng.uniform(0.3, 1), rng.uniform(0, 1), rng.uniform(0, 1))
        s.writes = 0
        kind = 'move' if rng.random() < 0.5 else 'resize'
        r0 = outer(s, sc)
        grab = (r0[0] + r0[2]/2.0, r0[1] + BAR/2.0) if kind == 'move' else (r0[0] + r0[2] - 6.0, r0[1] + r0[3] - 5.0)
        d = rng.randint(1, 5); u = d + rng.randint(1, 40)
        mode_c = rng.random()
        if mode_c < 0.25:        # 클릭만(2칸 안쪽)
            path = lambda k, g=grab: (g[0] + rng.uniform(-1, 1) * 0 + (0.9 if k % 2 else -0.9), g[1] + 0.4)
            small = True
        else:
            vx, vy = rng.uniform(-60, 60), rng.uniform(-40, 40)
            path = lambda k, g=grab, vx=vx, vy=vy, d=d: (g[0] + vx * (k - d), g[1] + vy * (k - d))      # 창 밖·화면 밖까지 갈 수 있다
            small = False
        close = reopen = None
        if rng.random() < 0.15 and u - d > 8:       # 끌다가 창이 닫혔다 열림(버튼은 계속 누른 채)
            close = d + 3; reopen = close + rng.randint(1, 12)
        windows = rng.choice([1, 1, 2])
        fr, start_rect, rects = gesture(sc, s, kind, d, u, path, windows, close, reopen)
        ctx = ("loop", kind, sc_w, sc_h, d, u, small, close, reopen)
        check(fr.mode is None, "끌기가 끝났는데 상태가 남음", ctx)
        for r in rects:
            if r is not None: invariants(r, sc, ctx)
        if small and close is None:
            check(s.writes == 0, "클릭만 했는데 저장함", ctx)
            check(not custom(s) or outer(s, sc) == start_rect or custom(Settings(s.winW, s.winCx, s.winCy)), "클릭만 했는데 값이 바뀜", ctx)
            if not custom(Settings(-1, -1, -1)): pass
        if not small and close is None:
            # 마지막으로 Step에 쓰인 마우스는 (놓기 전 프레임 u-1의 Update 시점) = 프레임 u-2의 Repaint 값
            last_m = path(max(d, u - 2)); dm = (last_m[0] - path(d)[0], last_m[1] - path(d)[1])
            exp = compute_drag(Settings(), sc, kind, start_rect, dm[0], dm[1]) if (abs(dm[0]) >= 2 or abs(dm[1]) >= 2) else start_rect
            # Settings()가 기본이 아닌 시작값이었을 수 있으므로 시작 사각형을 직접 넣은 기준과 비교
            tmp = Settings(); store(tmp, sc, start_rect)
            exp = compute_drag(tmp, sc, kind, start_rect, dm[0], dm[1]) if (abs(dm[0]) >= 2 or abs(dm[1]) >= 2) else start_rect
            got = outer(s, sc)
            check(all(abs(a - b) <= 1.0 for a, b in zip(got, exp)), "끌기 결과가 기대와 다름 %s vs %s" % (got, exp), ctx)
            moved = abs(dm[0]) >= 2 or abs(dm[1]) >= 2
            check(s.writes == (1 if moved else 0), "저장 횟수가 이상함 %d" % s.writes, ctx)
        if close is not None:
            check(s.writes <= 1, "닫힘 중 끌기에서 저장이 여러 번 일어남 %d" % s.writes, ctx)
            # 창이 5프레임 이상 없다가 다시 열렸는데 버튼이 아직 눌려 있어도, 오래된 끌기는 버려져 창이 튀지 않고 그 자리에 있어야 한다
            if reopen - close >= 5 and reopen < u and rects[close - 1] is not None:
                for k in range(reopen, len(rects)):
                    check(rects[k] == rects[close - 1], "닫혔다 열린 뒤 창이 튐 %s vs %s" % (rects[k], rects[close - 1]), ctx + (k,))

if fails:
    print("FAIL %d건" % len(fails))
    for m, c in fails[:25]: print(" -", m, c)
    sys.exit(1)
print("OK: %d건 검사 모두 통과" % checks)
