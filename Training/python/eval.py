import os
"""저장한 모델의 승률을 잰다.  python eval.py runs/ppo1/best --games 300"""
import argparse

from sb3_contrib import MaskablePPO

from train import play

ap = argparse.ArgumentParser()
ap.add_argument("model")
ap.add_argument("--games", type=int, default=300)
ap.add_argument("--full-info", action="store_true", help="전체 정보로 학습한 모델을 잴 때 붙인다(학습 때와 같아야 한다)")
ap.add_argument("--rule-pick", action="store_true", help="선출을 AI 대신 기존 규칙에 맡겨서 잰다(선출 학습 효과 비교용)")
a = ap.parse_args()
if a.full_info:
    os.environ["RIMKET_FULLINFO"] = "1"
if a.rule_pick:
    os.environ["RIMKET_AUTOPICK"] = "1"
m = MaskablePPO.load(a.model, device="cpu")
for name, opp in [("랭크AI", 0), ("기존AI", 1), ("무작위", 2)]:
    print("%s 상대 승률: %.1f%%" % (name, 100 * play(m, opp, a.games, 777)))
