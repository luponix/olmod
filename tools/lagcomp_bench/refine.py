#!/usr/bin/env python3
"""Stage-2 refinement search: extended grids around the stage-1 optimum
(several constants tuned to grid bounds there), plus a tail-aware objective
variant for the hazard family (its value is the p99, not the mean).

Run:  python3 -u refine.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import predictors as P
import tune
from tune import (Objective, coord_search, eval_errors, stats,
                  HORIZONS_SEARCH, load_corpus, split_segments)


class TailObjective(Objective):
    """0.5*mean@200 + 0.5*p99@300 (each cv-normalized, overall/focus blended)."""

    def __call__(self, pred):
        r = eval_errors(pred, self.segs, HORIZONS_SEARCH, self.stride)
        P.clear_seg_caches(self.segs)
        o200 = stats(r[0.20]["overall"])["mean"]
        f200 = stats(r[0.20]["focus"])["mean"]
        o399 = stats(r[0.30]["overall"])["p99"]
        f399 = stats(r[0.30]["focus"])["p99"]
        jm = 0.5 * o200 / self.cv200[0] + 0.5 * f200 / self.cv200[1]
        jt = 0.5 * o399 / self.cv300p99[0] + 0.5 * f399 / self.cv300p99[1]
        return 0.5 * jm + 0.5 * jt, jm


# stage-1 optima (tune.py --search, 2026-07-25)
BALL1 = {'tau_v': 0.005, 'tau_a': 0.03, 't_commit': 1.0, 't_max': 60.0}
HAZ1 = {'dead': 15.0, 'tau_flip': 3.0, 'tau_dir': 0.5, 'floor': 0.0,
        'commit_mult': 0.2, 't_recent': 0.3, 'c_damp': 0.2, 'c_tc': 0.0}


def main():
    by_file, _ = load_corpus()
    tune_segs, _ = split_segments(by_file)
    obj = Objective(tune_segs)
    print("cv reference (tune): mean200=%.4f/%.4f p99_300=%.4f/%.4f"
          % (obj.cv200[0], obj.cv200[1], obj.cv300p99[0], obj.cv300p99[1]))

    ball_grids = dict(
        tau_v=[0.0025, 0.005, 0.01],
        tau_a=[0.01, 0.02, 0.03, 0.05],
        t_commit=[0.7, 1.0, 1.5, 2.5, 4.0, 8.0],
        t_max=[40.0, 60.0, 90.0, 120.0],
    )
    ball, jb = coord_search(lambda p: P.Ballistic(**p), BALL1, ball_grids,
                            obj, sweeps=3, label="ballistic2")
    print("BALLISTIC2 =", ball, jb)

    haz_grids = dict(
        dead=[6.0, 10.0, 15.0, 20.0, 30.0],
        tau_flip=[2.0, 3.0, 5.0, 8.0],
        tau_dir=[0.3, 0.5, 0.8, 1.2],
        floor=[0.0, 0.2, 0.4],
        commit_mult=[0.1, 0.2, 0.4],
        t_recent=[0.2, 0.3, 0.5],
        c_damp=[0.0, 0.1, 0.2, 0.3, 0.5],
        c_tc=[0.0, 0.3, 0.6],
    )
    # (a) mean-primary, per spec
    haz_a, ja = coord_search(lambda p: P.BallisticHazard(**dict(ball, **p)),
                             HAZ1, haz_grids, obj, sweeps=3, label="hazard2a")
    print("HAZARD2a (mean-primary) =", haz_a, ja)
    # (b) tail-aware: hazard is the tail-guard model for select
    tobj = TailObjective(tune_segs)
    haz_b, jbt = coord_search(lambda p: P.BallisticHazard(**dict(ball, **p)),
                              HAZ1, haz_grids, tobj, sweeps=3, label="hazard2b")
    print("HAZARD2b (tail-aware) =", haz_b, jbt)

    # compare both hazards on the plain objective for the record
    for tag, hz in (("a", haz_a), ("b", haz_b)):
        j = obj(P.BallisticHazard(**dict(ball, **hz)))
        print("hazard2%s on mean-objective: J=%.4f tie=%.4f" % (tag, j[0], j[1]))

    print()
    print("BALLISTIC2 =", ball)
    print("HAZARD2a =", haz_a)
    print("HAZARD2b =", haz_b)


if __name__ == "__main__":
    main()
