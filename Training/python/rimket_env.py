"""림켓몬 랭크 배틀 학습 환경(gymnasium). C# TrainEnv 프로세스와 표준입출력으로 통신한다."""
import os
import struct
import subprocess
import tempfile
import time
import numpy as np

try:
    import gymnasium as gym
    from gymnasium import spaces
except ImportError:  # 학습 외 용도(스모크 테스트)에서는 없어도 된다
    gym = None

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.normpath(os.path.join(HERE, "..", "TrainEnv"))


class RawEnv:
    """gym 없이 쓰는 저수준 환경."""

    def __init__(self, dll=None):
        dll = dll or os.path.join(PROJ, "bin", "Release", "net8.0", "TrainEnv.dll")
        if not os.path.exists(dll):
            raise FileNotFoundError("TrainEnv가 빌드되지 않았습니다: dotnet build -c Release Training/TrainEnv")
        self.p = subprocess.Popen(["dotnet", dll], stdin=subprocess.PIPE, stdout=subprocess.PIPE, bufsize=0)
        self._send("info")
        self.obs_size, self.n_actions = struct.unpack("<ii", self._read(8))

    def _send(self, s):
        self.p.stdin.write((s + "\n").encode())
        self.p.stdin.flush()

    def _read(self, n):
        buf = b""
        while len(buf) < n:
            c = self.p.stdout.read(n - len(buf))
            if not c:
                raise RuntimeError("TrainEnv 프로세스가 종료되었습니다")
            buf += c
        return buf

    def _recv(self):
        o = np.frombuffer(self._read(4 * self.obs_size), dtype="<f4").copy()
        m = np.frombuffer(self._read(self.n_actions), dtype=np.uint8).astype(bool)
        reward, done, win, turn = struct.unpack("<fBBi", self._read(10))
        return o, m, reward, bool(done), bool(win), turn

    def reset(self, seed, opp=0, shape=0.0, policy=None):
        """opp 3 = 자기학습: policy(내보낸 정책 파일 경로)의 신경망이 상대를 조종한다."""
        self._send("reset %d %d %s%s" % (seed, opp, repr(float(shape)), " " + policy if policy else ""))
        return self._recv()

    def step(self, a):
        self._send("step %d" % a)
        return self._recv()

    def close(self):
        try:
            self._send("quit")
            self.p.wait(timeout=3)
        except Exception:
            self.p.kill()


if gym is not None:

    class RimketEnv(gym.Env):
        """opp: 0 = 랭크 AI, 1 = 기존 AI, 2 = 무작위, -1 = 에피소드마다 섞어서.
        pool: 자기학습용 정책 파일 폴더. 주면(opp=-1일 때) 판의 self_frac 비율은 그 폴더의 과거 정책이 상대가 된다
        (절반은 가장 최근 것, 절반은 아무거나). 나머지 판은 랭크 AI/기존 AI."""

        metadata = {"render_modes": []}

        def __init__(self, opp=-1, shape=0.1, seed=0, pool=None, self_frac=0.7):
            self.raw = RawEnv()
            self.opp, self.shape = opp, shape
            self.pool, self.self_frac = pool, self_frac
            self.rng = np.random.default_rng(seed)
            self.observation_space = spaces.Box(-3.0, 3.0, (self.raw.obs_size,), np.float32)
            self.action_space = spaces.Discrete(self.raw.n_actions)
            self._mask = np.ones(self.raw.n_actions, bool)
            self.last_win = False

        def reset(self, seed=None, options=None):
            if seed is not None:
                self.rng = np.random.default_rng(seed)
            policy = None
            if self.opp >= 0:
                opp = self.opp
            elif self.pool and self.rng.random() < self.self_frac:
                files = sorted(f for f in os.listdir(self.pool) if f.endswith(".bin"))
                if files:
                    pick = files[-1] if self.rng.random() < 0.5 else files[int(self.rng.integers(0, len(files)))]
                    opp, policy = 3, os.path.abspath(os.path.join(self.pool, pick))
                else:
                    opp = 0
            elif self.pool:
                opp = int(self.rng.choice([0, 0, 1]))
            else:
                opp = int(self.rng.choice([0, 0, 1, 2]))
            o, m, *_ = self.raw.reset(int(self.rng.integers(0, 2**31 - 1)), opp, self.shape, policy)
            self._mask = m
            return o, {}

        def step(self, a):
            o, m, r, done, win, turn = self.raw.step(int(a))
            self._mask = m
            self.last_win = win
            return o, r, done, False, {"win": win, "turn": turn}

        def action_masks(self):
            return self._mask

        def close(self):
            self.raw.close()


try:
    from stable_baselines3.common.vec_env import VecEnv
except ImportError:
    VecEnv = None

if VecEnv is not None and gym is not None:

    class BatchVecEnv(VecEnv):
        """환경 n개를 C# 프로세스 하나가 스레드로 동시에 돌린다. 파이썬과는 걸음마다 한 번만 주고받아서
        SubprocVecEnv(환경마다 프로세스 + 걸음마다 환경 수만큼 왕복)보다 훨씬 빠르다.
        상대(랭크AI/기존AI/자기학습 정책)와 시드는 C# 쪽이 판마다 스스로 고른다."""

        def __init__(self, n, shape=0.1, seed=0, pool=None, self_frac=0.7, dll=None, meta=None):
            dll = dll or os.path.join(PROJ, "bin", "Release", "net8.0", "TrainEnv.dll")
            if not os.path.exists(dll):
                raise FileNotFoundError("TrainEnv가 빌드되지 않았습니다: dotnet build -c Release Training/TrainEnv")
            self.p = subprocess.Popen(["dotnet", dll], stdin=subprocess.PIPE, stdout=subprocess.PIPE, bufsize=0)
            self.n = n
            # 큰 데이터(관측)는 공유 메모리 파일로 받는다. 파이프로 수 MB씩 옮기면 그게 가장 느린 구간이 된다.
            self.shm_path = os.path.join(tempfile.gettempdir(), "rimket_shm_%d_%d.bin" % (os.getpid(), int(time.time() * 1000) % 100000000))
            # meta: 팀 구성 통계 파일(어떤 종족·도구·기술을 데려간 쪽이 이겼나). 주면 C# 쪽이 1분마다 여기에 저장한다.
            self._send("binit %d %d %s %s|%s|%s|%s" % (n, seed, repr(float(shape)), repr(float(self_frac if pool else 0.0)),
                                                       self.shm_path, os.path.abspath(pool) if pool else "", os.path.abspath(meta) if meta else ""))
            self.obs_size, self.n_actions = struct.unpack("<ii", self._read(8))
            self._read(1)
            o_bytes, m_bytes = 4 * self.obs_size * n, self.n_actions * n
            self._mm = np.memmap(self.shm_path, dtype=np.uint8, mode="r")
            self._v_obs = self._mm[:o_bytes].view("<f4").reshape(n, self.obs_size)
            self._v_mask = self._mm[o_bytes:o_bytes + m_bytes].reshape(n, self.n_actions)
            self._v_rew = self._mm[o_bytes + m_bytes:o_bytes + m_bytes + 4 * n].view("<f4")
            self._v_done = self._mm[o_bytes + m_bytes + 4 * n:o_bytes + m_bytes + 5 * n]
            self._v_win = self._mm[o_bytes + m_bytes + 5 * n:o_bytes + m_bytes + 6 * n]
            super().__init__(n, spaces.Box(-3.0, 3.0, (self.obs_size,), np.float32), spaces.Discrete(self.n_actions))
            self._obs, self._masks = self._recv_obs()
            self._ret = np.zeros(n, np.float64)
            self._len = np.zeros(n, np.int64)
            self._actions = None
            self.env_seconds = 0.0

        def _send(self, s):
            self.p.stdin.write((s + "\n").encode())
            self.p.stdin.flush()

        def _read(self, k):
            buf = bytearray()
            while len(buf) < k:
                c = self.p.stdout.read(k - len(buf))
                if not c:
                    raise RuntimeError("TrainEnv 프로세스가 종료되었습니다")
                buf += c
            return bytes(buf)

        def _recv_obs(self):
            return np.array(self._v_obs, dtype=np.float32), self._v_mask.astype(bool)

        def reset(self):
            return self._obs      # 환경은 binit 때 이미 시작했고, 끝난 판은 스스로 새로 시작한다

        def step_async(self, actions):
            self._actions = actions

        def step_wait(self):
            t0 = time.perf_counter()
            self._send("bstep " + " ".join(str(int(a)) for a in self._actions))
            self._read(1)                      # "다 썼다" 신호
            o, m = self._recv_obs()
            rew = np.array(self._v_rew, dtype=np.float32)
            done = self._v_done.astype(bool)
            win = self._v_win.astype(bool)
            self._obs, self._masks = o, m
            self.env_seconds += time.perf_counter() - t0      # 환경(C#) 쪽에서 보낸 시간: 어디가 느린지 볼 때 쓴다
            self._ret += rew
            self._len += 1
            infos = [{} for _ in range(self.n)]
            for i in np.flatnonzero(done):
                infos[i] = {"win": bool(win[i]), "episode": {"r": float(self._ret[i]), "l": int(self._len[i]), "t": 0.0},
                            "TimeLimit.truncated": False, "terminal_observation": o[i]}
                self._ret[i] = 0.0
                self._len[i] = 0
            return o, rew, done, infos

        def action_masks(self):
            return self._masks

        def close(self):
            try:
                self._send("quit")
                self.p.wait(timeout=3)
            except Exception:
                self.p.kill()
            try:
                del self._v_obs, self._v_mask, self._v_rew, self._v_done, self._v_win, self._mm
                os.remove(self.shm_path)
            except Exception:
                pass

        # --- VecEnv가 요구하는 나머지(마스크 조회만 실제로 쓰인다)
        def _idx(self, indices):
            return range(self.n) if indices is None else ([indices] if isinstance(indices, int) else list(indices))

        def env_method(self, method_name, *args, indices=None, **kwargs):
            if method_name == "action_masks":
                return [self._masks[i] for i in self._idx(indices)]
            raise AttributeError(method_name)

        def get_attr(self, attr_name, indices=None):
            if attr_name == "action_masks":
                return [self.action_masks for _ in self._idx(indices)]
            if attr_name == "render_mode":
                return [None for _ in self._idx(indices)]
            raise AttributeError(attr_name)

        def has_attr(self, attr_name):
            return attr_name in ("action_masks", "render_mode")

        def set_attr(self, attr_name, value, indices=None):
            pass

        def env_is_wrapped(self, wrapper_class, indices=None):
            return [False for _ in self._idx(indices)]

        def seed(self, seed=None):
            return [None] * self.n
