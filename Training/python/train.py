"""MaskablePPO로 랭크 배틀 AI를 학습한다.

사용:  python train.py --steps 5000000 --envs 10
자기학습:  python train.py --selfplay --resume runs/ppo3/final --out runs/sp1 --steps 20000000 --envs 20 --device cuda
  상대 자리의 70%는 과거 시점의 내 정책(runs/sp1/pool 폴더에 50만 step마다 저장), 30%는 랭크 AI/기존 AI.
권장 환경(i5-12400 / 48GB / RTX 3060): MLP는 CPU 학습도 충분히 빠르다. GPU를 쓰려면 --device cuda.
"""
import argparse
import os
import time

import numpy as np
import torch
from sb3_contrib import MaskablePPO as _MaskablePPO
from sb3_contrib.common.maskable.buffers import MaskableRolloutBuffer, MaskableRolloutBufferSamples
from sb3_contrib.common.maskable.evaluation import evaluate_policy  # noqa: F401  (참고용)
from stable_baselines3.common.callbacks import BaseCallback
from stable_baselines3.common.vec_env import SubprocVecEnv

from export_weights import export
from rimket_env import BatchVecEnv, RimketEnv


class MaskablePPO(_MaskablePPO):
    """학습 단계에 쓴 시간을 잰다(시간 배분 표시용). 저장 파일은 원래 MaskablePPO와 같다."""
    train_seconds = 0.0

    def train(self):
        t0 = time.perf_counter()
        super().train()
        MaskablePPO.train_seconds += time.perf_counter() - t0


def make_env(rank, seed, shape, pool=None, self_frac=0.7):
    def _f():
        return RimketEnv(opp=-1, shape=shape, seed=seed + rank, pool=pool, self_frac=self_frac)
    return _f


def play(model, opp, games, seed):
    """학습된 정책을 결정적으로(argmax) 돌려 opp 상대 승률을 잰다."""
    env = RimketEnv(opp=opp, shape=0.0, seed=seed)
    wins = 0
    for g in range(games):
        o, _ = env.reset(seed=seed + g)
        done = False
        while not done:
            a, _ = model.predict(o, action_masks=env.action_masks(), deterministic=True)
            o, r, done, _, info = env.step(int(a))
        wins += info["win"]
    env.close()
    return wins / games


class EvalCallback(BaseCallback):
    """주기적으로 승률을 재고 저장한다. 자기학습이면 pool 폴더에 현재 정책을 상대용으로 내보낸다(최근 keep개만 남김)."""

    def __init__(self, every, games, outdir, pool=None, snap_every=500_000, keep=10):
        super().__init__()
        self.every, self.games, self.outdir, self.next, self.best = every, games, outdir, every, -1.0
        self.pool, self.snap_every, self.next_snap, self.keep, self.round = pool, snap_every, snap_every, keep, 0

    def snapshot(self, tag):
        export(self.model, os.path.join(self.pool, "opp_%010d.bin" % tag))
        files = sorted(f for f in os.listdir(self.pool) if f.endswith(".bin"))
        for f in files[:-self.keep]:
            try:
                os.remove(os.path.join(self.pool, f))
            except OSError:
                pass

    def _on_step(self):
        if self.pool and self.num_timesteps >= self.next_snap:
            self.next_snap += self.snap_every
            self.snapshot(self.num_timesteps)
        if self.num_timesteps < self.next:
            return True
        self.next += self.every
        self.round += 1
        env = self.training_env
        if hasattr(env, "env_seconds"):
            now = time.time()
            tr = MaskablePPO.train_seconds
            if getattr(self, "_t_last", None) is not None and now > self._t_last:
                share = 100.0 * (env.env_seconds - self._env_last) / (now - self._t_last)
                trs = 100.0 * (tr - self._tr_last) / (now - self._t_last)
                print("[시간 배분] 환경(C#) %.0f%% · 행동 고르기(신경망) %.0f%% · 학습 %.0f%%" % (share, max(0.0, 100 - share - trs), trs), flush=True)
            self._env_last = env.env_seconds
            self._tr_last = tr
        print("[평가 중] %dk step — 1~2분 걸립니다(이 동안은 CPU·GPU 사용률이 낮게 보입니다)" % (self.num_timesteps // 1000), flush=True)
        seed = 50_000 + self.round * 10_007      # 매번 다른 판으로 잰다(같은 판을 반복하면 운 좋은 시점이 best로 뽑힌다)
        res = {"랭크AI": play(self.model, 0, self.games, seed)}
        for n, o in [("기존AI", 1), ("무작위", 2)]:
            res[n] = play(self.model, o, max(50, self.games // 3), seed)
        print("[평가] %dk step: " % (self.num_timesteps // 1000) + ", ".join("%s %.0f%%" % kv for kv in ((k, v * 100) for k, v in res.items())), flush=True)
        score = res["랭크AI"]
        self.model.save(os.path.join(self.outdir, "latest"))
        if score > self.best:
            self.best = score
            self.model.save(os.path.join(self.outdir, "best"))
        self._t_last = time.time()      # 평가 시간은 시간 배분 계산에서 뺀다
        return True


class FastBuffer(MaskableRolloutBuffer):
    """학습 단계에서, 모은 데이터를 한 번에 학습 장치(GPU)로 올려 두고 거기서 바로 뽑는다.
    원래는 묶음(1024개)마다 CPU에서 골라 담아 GPU로 보냈다(한 번 모을 때 128번). 결과는 같고 옮기는 일만 줄어든다."""
    NAMES = ["observations", "actions", "values", "log_probs", "advantages", "returns", "action_masks"]

    def reset(self):
        super().reset()
        self._dev = None

    def get(self, batch_size=None):
        assert self.full, ""
        if not self.generator_ready:
            for k in self.NAMES:
                self.__dict__[k] = self.swap_and_flatten(self.__dict__[k])
            self.generator_ready = True
            self._dev = None
        n = self.buffer_size * self.n_envs
        if self._dev is None:
            try:
                d = {k: torch.as_tensor(self.__dict__[k], device=self.device) for k in self.NAMES}
                for k in ("values", "log_probs", "advantages", "returns"):
                    d[k] = d[k].flatten()
                d["action_masks"] = d["action_masks"].reshape(n, self.mask_dims)
                self._dev = d
            except RuntimeError as e:      # GPU 메모리가 모자라면 예전 방식으로
                print("[알림] 빠른 학습 버퍼를 쓸 수 없어 예전 방식으로 돌립니다: %s" % str(e)[:80], flush=True)
                self._dev = False
        if self._dev is False:
            yield from super().get(batch_size)
            return
        d = self._dev
        idx = torch.randperm(n, device=self.device)
        bs = batch_size or n
        for s in range(0, n, bs):
            i = idx[s:s + bs]
            yield MaskableRolloutBufferSamples(d["observations"][i], d["actions"][i], d["values"][i], d["log_probs"][i],
                                               d["advantages"][i], d["returns"][i], d["action_masks"][i])


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--steps", type=int, default=3_000_000)
    ap.add_argument("--envs", type=int, default=10)
    ap.add_argument("--device", default="cpu")
    ap.add_argument("--shape", type=float, default=0.1, help="중간 보상(쓰러뜨림/쓰러짐) 가중치")
    ap.add_argument("--out", default="runs/ppo1")
    ap.add_argument("--resume", default=None)
    ap.add_argument("--eval-every", type=int, default=1_000_000)
    ap.add_argument("--eval-games", type=int, default=300)
    ap.add_argument("--selfplay", action="store_true", help="자기학습: 상대 자리에 과거 시점의 내 정책을 앉힌다(--resume 권장)")
    ap.add_argument("--self-frac", type=float, default=0.7, help="자기학습 판의 비율(나머지는 랭크 AI/기존 AI)")
    ap.add_argument("--snap-every", type=int, default=500_000, help="상대용 정책을 내보내는 간격(step)")
    ap.add_argument("--n-steps", type=int, default=None, help="환경 하나가 한 번에 모으는 걸음 수(기본 512). 환경을 많이 띄울 때는 줄인다: envs × n-steps 가 3만 안팎이면 적당")
    ap.add_argument("--widen", default=None, help="관측이 더 작던 예전 모델을 이어받는다(새로 생긴 입력은 0에서 시작 → 기존 전투 실력은 그대로). 예: --widen runs/sp1/final")
    ap.add_argument("--profile", type=int, default=0, help="속도 점검: 이만큼의 step만 돌리면서 파이썬 쪽에서 시간이 어디에 쓰이는지 표로 보여 주고 끝낸다(저장 안 함)")
    ap.add_argument("--subproc", action="store_true", help="예전 방식(환경마다 프로세스). 느리다. 문제 생겼을 때만")
    ap.add_argument("--lr", type=float, default=None, help="시작 학습률(기본: 새 학습 3e-4, 이어서 학습 1e-4)")
    ap.add_argument("--slow-buffer", action="store_true", help="학습 데이터를 GPU에 한 번에 올리지 않는 예전 방식(문제 생겼을 때만)")
    ap.add_argument("--full-info", action="store_true", help="상대의 능력치·기술·특성·도구를 전부 보는 모델(랭크AI와 같은 조건). 학습과 평가에 똑같이 붙여야 한다")
    a = ap.parse_args()
    if a.full_info:
        os.environ["RIMKET_FULLINFO"] = "1"
    os.makedirs(a.out, exist_ok=True)
    pool = None
    if a.selfplay:
        pool = os.path.abspath(os.path.join(a.out, "pool"))
        os.makedirs(pool, exist_ok=True)

    lr0 = a.lr or (1e-4 if (a.resume or a.widen) else 3e-4)
    lr = lambda p: lr0 * max(0.1, p)  # noqa: E731
    if a.selfplay:
        # 첫 상대: 이어받은 모델(없으면 방금 만든 백지 정책). 환경을 띄우기 전에 파일이 있어야 한다.
        seedm = MaskablePPO.load(a.resume, device="cpu") if a.resume and not a.widen else None
        if seedm is not None and not any(f.endswith(".bin") for f in os.listdir(pool)):
            export(seedm, os.path.join(pool, "opp_%010d.bin" % 0))
        del seedm
    if a.subproc:
        vec = SubprocVecEnv([make_env(i, 1000, a.shape, pool, a.self_frac) for i in range(a.envs)])
    else:
        vec = BatchVecEnv(a.envs, a.shape, int(time.time()) % 1_000_000, pool, a.self_frac, meta=os.path.join(a.out, "team_meta.txt"))      # 기본: C# 한 프로세스가 환경을 묶어 돌린다(빠름)
    if a.resume and not a.widen:
        model = MaskablePPO.load(a.resume, env=vec, device=a.device, custom_objects={"learning_rate": lr, "lr_schedule": lr},
                                 **({"n_steps": a.n_steps} if a.n_steps else {}))
    else:
        model = MaskablePPO(
            "MlpPolicy", vec, device=a.device, verbose=1,
            n_steps=a.n_steps or 512, batch_size=1024, n_epochs=4, learning_rate=lr, gamma=0.995, gae_lambda=0.95,
            ent_coef=0.01, clip_range=0.2,
            policy_kwargs=dict(net_arch=dict(pi=[512, 256], vf=[512, 256]), activation_fn=torch.nn.ReLU),
        )
    if a.widen:
        # 예전 모델의 가중치를 새 모델에 옮긴다. 입력이 늘어난 첫 층은 예전 입력 자리에만 복사하고 새 입력 자리는 0으로 둔다.
        old = MaskablePPO.load(a.widen, device="cpu").policy.state_dict()
        new = model.policy.state_dict()
        moved = 0
        for k, v in old.items():
            if k not in new:
                continue
            if new[k].shape == v.shape:
                new[k] = v.clone()
                moved += 1
            elif v.dim() == 2 and new[k].shape[0] == v.shape[0] and new[k].shape[1] > v.shape[1]:
                w = torch.zeros_like(new[k])
                w[:, :v.shape[1]] = v
                new[k] = w
                moved += 1
        model.policy.load_state_dict(new)
        print("예전 모델 이어받기: %s → 텐서 %d개 복사(관측 %d칸)" % (a.widen, moved, vec.observation_space.shape[0]), flush=True)
    if not a.slow_buffer:
        rb = model.rollout_buffer
        model.rollout_buffer = FastBuffer(rb.buffer_size, rb.observation_space, rb.action_space, device=model.device,
                                          gamma=rb.gamma, gae_lambda=rb.gae_lambda, n_envs=rb.n_envs)
    if a.device != "cpu":
        torch.set_float32_matmul_precision("high")
    t = time.time()
    if a.profile:
        import cProfile
        import pstats
        pr = cProfile.Profile()
        pr.enable()
        model.learn(total_timesteps=a.profile)
        pr.disable()
        print("\n===== 속도 점검 결과(이 아래 표를 통째로 복사해서 알려 주세요) =====")
        pstats.Stats(pr).sort_stats("tottime").print_stats(28)
        vec.close()
        raise SystemExit(0)
    cb = EvalCallback(a.eval_every, a.eval_games, a.out, pool, a.snap_every)
    if pool and not any(f.endswith(".bin") for f in os.listdir(pool)):
        cb.model = model
        cb.snapshot(0)
    try:
        model.learn(total_timesteps=a.steps, callback=cb)
    except KeyboardInterrupt:      # Ctrl+C로 멈춰도 그때까지 배운 것을 final로 저장한다
        print("\n[중단] 지금까지 학습한 모델을 저장합니다…", flush=True)
    model.save(os.path.join(a.out, "final"))
    print("끝: %.1f분" % ((time.time() - t) / 60))
