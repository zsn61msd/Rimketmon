"""폼 팀 대응 AI: 미리 계산 AI 따라 하기(전문가 반복 학습).

한 바퀴:
  1) 수집: 지금 정책으로 미리 계산 AI가 폼 팀(플레이어 쪽) 상대로 판을 두고, 매 결정(관측·합법 행동·고른 행동)을 기록한다(C#).
  2) 학습: 정책망이 그 결정을 따라 하도록 배운다(지금까지 모은 모든 바퀴의 기록을 함께 쓴다).
  3) 평가: 같은 판 묶음에서 학습 AI·미리 계산 AI의 승률을 잰다(C#).
다음 바퀴의 미리 계산 AI는 새 정책으로 앞 수를 굴리므로 둘이 함께 좋아지는 것을 노린다.

  py -3.10 distill.py --start ../../Rimketmon/Data/ai_policy.bin --out runs/distill1 --rounds 4 --games 4000 --device cuda

결과: runs/<이름>/round<N>/policy.bin(32비트, 다음 수집용), ai_policy_form.bin(16비트, 모드용), 그리고 log.txt.
모드에 넣을 때는 평가에서 학습 AI 승률이 가장 높았던 바퀴의 ai_policy_form.bin을 Rimketmon/Data/에 복사한다.
"""
import argparse
import os
import struct
import subprocess
import sys
import time

import numpy as np
import torch

HERE = os.path.dirname(os.path.abspath(__file__))
DLL = os.path.normpath(os.path.join(HERE, "..", "TrainEnv", "bin", "Release", "net8.0", "TrainEnv.dll"))
FORM_IN = 7252      # 폼 입력이 붙은 관측 크기(AiEncoder.SizeFor(true))


# ---------------------------------------------------------------- 정책 파일(모드와 같은 형식)
def load_bin(path):
    with open(path, "rb") as f:
        n = struct.unpack("<i", f.read(4))[0]
        half = n < 0
        n = abs(n)
        layers = []
        for _ in range(n):
            o, i, relu = struct.unpack("<iiB", f.read(9))
            w = np.frombuffer(f.read(o * i * (2 if half else 4)), dtype="<f2" if half else "<f4").astype(np.float32).reshape(o, i)
            b = np.frombuffer(f.read(o * 4), dtype="<f4").astype(np.float32)
            layers.append((w, b, relu == 1))
    return layers


def save_bin(net, path, half=False):
    lins = [m for m in net if isinstance(m, torch.nn.Linear)]
    tmp = path + ".tmp"
    with open(tmp, "wb") as f:
        f.write(struct.pack("<i", -len(lins) if half else len(lins)))
        for k, m in enumerate(lins):
            w = m.weight.detach().cpu().numpy()
            b = m.bias.detach().cpu().numpy()
            f.write(struct.pack("<iiB", w.shape[0], w.shape[1], 0 if k == len(lins) - 1 else 1))
            f.write(np.ascontiguousarray(w, dtype="<f2" if half else "<f4").tobytes())
            f.write(np.ascontiguousarray(b, dtype="<f4").tobytes())
    os.replace(tmp, path)


def build_net(layers):
    """정책 파일 → torch 신경망. 입력이 7222(폼 입력 없음)면 7252로 늘리고 새 칸의 가중치는 0(처음엔 예전과 똑같이 둔다)."""
    mods = []
    for k, (w, b, relu) in enumerate(layers):
        if k == 0 and w.shape[1] < FORM_IN:
            w = np.concatenate([w, np.zeros((w.shape[0], FORM_IN - w.shape[1]), np.float32)], axis=1)
        lin = torch.nn.Linear(w.shape[1], w.shape[0])
        with torch.no_grad():
            lin.weight.copy_(torch.from_numpy(w))
            lin.bias.copy_(torch.from_numpy(b))
        mods.append(lin)
        if relu:
            mods.append(torch.nn.ReLU())
    return torch.nn.Sequential(*mods)


# ---------------------------------------------------------------- 기록 읽기
def load_records(path):
    """[uint16 행동, uint16 마스크, int32 k, k×(int32, float32)] → (희소 행렬용 배열들)."""
    data = open(path, "rb").read()
    acts, masks, rows, cols, vals = [], [], [], [], []
    p, r = 0, 0
    pair = np.dtype([("i", "<i4"), ("v", "<f4")])
    while p < len(data):
        a, m, k = struct.unpack_from("<HHi", data, p)
        p += 8
        arr = np.frombuffer(data, dtype=pair, count=k, offset=p)
        p += 8 * k
        acts.append(a)
        masks.append([(m >> i) & 1 for i in range(16)])
        rows.append(np.full(k, r, np.int64))
        cols.append(arr["i"].astype(np.int64))
        vals.append(arr["v"].astype(np.float32))
        r += 1
    if r == 0:
        return None
    return dict(n=r, act=np.array(acts, np.int64), mask=np.array(masks, bool),
                rows=np.concatenate(rows), cols=np.concatenate(cols), vals=np.concatenate(vals))


def merge(sets):
    sets = [s for s in sets if s]
    off, rows = 0, []
    for s in sets:
        rows.append(s["rows"] + off)
        off += s["n"]
    return dict(n=off, act=np.concatenate([s["act"] for s in sets]), mask=np.concatenate([s["mask"] for s in sets]),
                rows=np.concatenate(rows), cols=np.concatenate([s["cols"] for s in sets]), vals=np.concatenate([s["vals"] for s in sets]))


def dense_batch(d, idx, dev, row_start):
    """희소 기록 중 idx 행들을 밀집 행렬로(GPU에서)."""
    pos = np.searchsorted
    parts_r, parts_c, parts_v = [], [], []
    for j, r in enumerate(idx):
        a, b = row_start[r], row_start[r + 1]
        parts_r.append(np.full(b - a, j, np.int64))
        parts_c.append(d["cols"][a:b])
        parts_v.append(d["vals"][a:b])
    x = torch.zeros((len(idx), FORM_IN), device=dev)
    x[torch.from_numpy(np.concatenate(parts_r)).to(dev), torch.from_numpy(np.concatenate(parts_c)).to(dev)] = torch.from_numpy(np.concatenate(parts_v)).to(dev)
    return x


def train(net, d, dev, epochs, lr, bs, log):
    n_act = net[-1].out_features
    row_start = np.zeros(d["n"] + 1, np.int64)
    np.add.at(row_start, d["rows"] + 1, 1)
    row_start = np.cumsum(row_start)
    order = np.argsort(d["rows"], kind="stable")
    d = dict(d, cols=d["cols"][order], vals=d["vals"][order])
    rng = np.random.default_rng(0)
    perm = rng.permutation(d["n"])
    hold = perm[: max(1, d["n"] // 20)]
    tr = perm[len(hold):]
    act = torch.from_numpy(d["act"]).to(dev)
    mask = torch.from_numpy(d["mask"][:, :n_act]).to(dev)
    opt = torch.optim.Adam(net.parameters(), lr=lr)

    def acc(ix):
        net.eval()
        hit = 0
        with torch.no_grad():
            for s in range(0, len(ix), 4096):
                b = ix[s:s + 4096]
                lg = net(dense_batch(d, b, dev, row_start)).masked_fill(~mask[b], -1e9)
                hit += (lg.argmax(1) == act[b]).sum().item()
        net.train()
        return hit / len(ix)

    log("  따라 하기 정확도(시작): %.1f%%" % (acc(hold) * 100))
    for ep in range(epochs):
        rng.shuffle(tr)
        tot = 0.0
        for s in range(0, len(tr), bs):
            b = tr[s:s + bs]
            lg = net(dense_batch(d, b, dev, row_start)).masked_fill(~mask[b], -1e9)
            loss = torch.nn.functional.cross_entropy(lg, act[b])
            opt.zero_grad()
            loss.backward()
            opt.step()
            tot += loss.item() * len(b)
        log("  학습 %d/%d: 손실 %.4f, 정확도(따로 둔 5%%) %.1f%%" % (ep + 1, epochs, tot / len(tr), acc(hold) * 100))


# ---------------------------------------------------------------- C# 도구
def run_tool(args, log):
    env = dict(os.environ, RIMKET_FULLINFO="1")
    p = subprocess.Popen(["dotnet", DLL] + [str(a) for a in args], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, env=env, encoding="utf-8", errors="replace")
    out = []
    import threading

    def err():
        for line in p.stderr:
            print("    " + line.rstrip(), flush=True)
    t = threading.Thread(target=err, daemon=True)
    t.start()
    for line in p.stdout:
        out.append(line.strip())
    p.wait()
    t.join(1)
    res = [l for l in out if l.startswith("RESULT")]
    if p.returncode != 0 or not res:
        raise RuntimeError("C# 도구 실패: " + " ".join(map(str, args)) + "\n" + "\n".join(out[-20:]))
    return res[-1].split()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--start", required=True, help="출발 정책(.bin). 보통 모드의 ai_policy.bin")
    ap.add_argument("--out", default="runs/distill1")
    ap.add_argument("--rounds", type=int, default=4)
    ap.add_argument("--games", type=int, default=4000, help="바퀴마다 수집할 판 수")
    ap.add_argument("--sims", type=int, default=24, help="미리 계산 횟수(모드의 타이난과 같은 24)")
    ap.add_argument("--depth", type=int, default=4, help="미리 계산 깊이(모드와 같은 4)")
    ap.add_argument("--eps", type=float, default=0.1, help="폼 팀 쪽이 무작위로 두는 비율(자료 다양성)")
    ap.add_argument("--epochs", type=int, default=4)
    ap.add_argument("--lr", type=float, default=1e-4)
    ap.add_argument("--bs", type=int, default=512)
    ap.add_argument("--eval-games", type=int, default=600, help="평가 판 수(학습에 쓰지 않는 판)")
    ap.add_argument("--eval-look", action="store_true", help="평가에서 미리 계산 AI 승률도 잰다(느림)")
    ap.add_argument("--threads", type=int, default=0, help="C# 동시 판 수(0=CPU 논리 프로세서 수)")
    ap.add_argument("--device", default="cuda" if torch.cuda.is_available() else "cpu")
    a = ap.parse_args()

    if not os.path.exists(DLL):
        sys.exit("TrainEnv가 빌드되지 않았습니다: dotnet build -c Release Training/TrainEnv")
    os.makedirs(a.out, exist_ok=True)
    logf = open(os.path.join(a.out, "log.txt"), "a", encoding="utf-8")

    def log(m):
        print(m, flush=True)
        logf.write(m + "\n")
        logf.flush()

    th = [] if a.threads <= 0 else [a.threads]
    EVAL0 = 1_000_000      # 평가 판 번호(수집 판과 겹치지 않게)
    net = build_net(load_bin(a.start)).to(a.device)
    cur = os.path.abspath(a.start)
    log("== 시작 %s: 출발 %s, 바퀴 %d × %d판, sims %d depth %d" % (time.strftime("%Y-%m-%d %H:%M"), a.start, a.rounds, a.games, a.sims, a.depth))
    r0 = run_tool(["--bench", cur, EVAL0, a.eval_games, "learned"] + ([a.sims, a.depth] + th if th else []), log)
    log("[평가] 출발 정책: 학습 AI %.1f%%" % (float(r0[3]) * 100))
    if a.eval_look:
        r = run_tool(["--bench", cur, EVAL0, a.eval_games, "look", a.sims, a.depth] + th, log)
        log("[평가] 출발 정책: 미리 계산 AI %.1f%% (%s초)" % (float(r[3]) * 100, r[4]))
    best = float(r0[3])
    datasets = []
    for rd in range(1, a.rounds + 1):
        rdir = os.path.join(a.out, "round%d" % rd)
        os.makedirs(rdir, exist_ok=True)
        rec = os.path.join(rdir, "records.bin")
        t = time.time()
        if not os.path.exists(rec):
            r = run_tool(["--collect", cur, (rd - 1) * a.games, a.games, rec + ".part", a.sims, a.depth, a.eps] + th, log)
            os.replace(rec + ".part", rec)
            log("[%d바퀴] 수집: %d판, 결정 %s개, %.1f분" % (rd, a.games, r[2], (time.time() - t) / 60))
        else:
            log("[%d바퀴] 수집 기록이 이미 있어 그대로 씀" % rd)
        datasets.append(load_records(rec))
        d = merge(datasets)
        t = time.time()
        train(net, d, a.device, a.epochs, a.lr, a.bs, log)
        log("[%d바퀴] 학습: 결정 %d개, %.1f분" % (rd, d["n"], (time.time() - t) / 60))
        pb = os.path.join(rdir, "policy.bin")
        save_bin(net, pb)
        save_bin(net, os.path.join(rdir, "ai_policy_form.bin"), half=True)
        cur = os.path.abspath(pb)
        r = run_tool(["--bench", cur, EVAL0, a.eval_games, "learned"] + ([a.sims, a.depth] + th if th else []), log)
        wr = float(r[3])
        mark = " ← 최고" if wr > best else ""
        best = max(best, wr)
        log("[%d바퀴 평가] 학습 AI %.1f%%%s" % (rd, wr * 100, mark))
        if a.eval_look:
            r = run_tool(["--bench", cur, EVAL0, a.eval_games, "look", a.sims, a.depth] + th, log)
            log("[%d바퀴 평가] 미리 계산 AI %.1f%%" % (rd, float(r[3]) * 100))
    log("== 끝: 학습 AI 최고 %.1f%% (출발 %.1f%%)" % (best * 100, float(r0[3]) * 100))


if __name__ == "__main__":
    main()
