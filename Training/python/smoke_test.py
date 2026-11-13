"""환경 점검: 무작위 행동으로 몇 판 돌려 속도와 승률(무작위 에이전트 vs 각 상대 AI)을 본다. numpy만 필요."""
import time
import numpy as np
from rimket_env import RawEnv

env = RawEnv()
print("관측 크기", env.obs_size, "행동 수", env.n_actions)
rng = np.random.default_rng(0)
for opp, name in [(0, "랭크 AI"), (1, "기존 AI"), (2, "무작위")]:
    wins = games = steps = 0
    t = time.time()
    for g in range(60):
        o, m, r, done, win, turn = env.reset(1000 + g, opp)
        while not done:
            a = int(rng.choice(np.flatnonzero(m)))
            o, m, r, done, win, turn = env.step(a)
            steps += 1
        wins += win
        games += 1
        assert np.isfinite(o).all()
    print("무작위 에이전트 vs %s: %d/%d 승, %.0f step/s" % (name, wins, games, steps / (time.time() - t)))
env.close()
