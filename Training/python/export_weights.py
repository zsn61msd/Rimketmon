"""학습한 정책(행동 선택 부분)을 모드가 읽는 이진 파일로 내보낸다.  python export_weights.py runs/ppo1/best policy.bin
형식(리틀엔디언): int32 층 수, 층마다 [int32 출력, int32 입력, byte relu, float32 w[출력*입력](행 우선), float32 b[출력]]
--half: 모드 배포용. 층 수를 음수로 쓰고 w를 float16으로 저장한다(용량 절반). 학습 중 상대 풀 파일은 float32 그대로.
  python export_weights.py runs/full2/latest ai_policy.bin --half
"""
import os
import struct
import sys

import numpy as np
import torch
from sb3_contrib import MaskablePPO

def export(model, path, half=False):
    """정책망(행동 선택 부분)을 이진 파일로 쓴다. 임시 파일에 쓴 뒤 이름을 바꿔서, 읽는 쪽이 반쯤 쓴 파일을 보지 않게 한다."""
    pol = model.policy
    layers = []
    for m in pol.mlp_extractor.policy_net:
        if isinstance(m, torch.nn.Linear):
            layers.append((m.weight.detach().cpu().numpy(), m.bias.detach().cpu().numpy(), True))
    layers.append((pol.action_net.weight.detach().cpu().numpy(), pol.action_net.bias.detach().cpu().numpy(), False))
    tmp = path + ".tmp"
    with open(tmp, "wb") as f:
        f.write(struct.pack("<i", -len(layers) if half else len(layers)))
        for w, b, relu in layers:
            f.write(struct.pack("<iiB", w.shape[0], w.shape[1], 1 if relu else 0))
            f.write(np.ascontiguousarray(w, dtype="<f2" if half else "<f4").tobytes())
            f.write(np.ascontiguousarray(b, dtype="<f4").tobytes())
    os.replace(tmp, path)
    return len(layers)


if __name__ == "__main__":
    n = export(MaskablePPO.load(sys.argv[1], device="cpu"), sys.argv[2], half="--half" in sys.argv[3:])
    print("저장:", sys.argv[2], "층", n)
