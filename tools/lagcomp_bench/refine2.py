#!/usr/bin/env python3
"""Stage-3: (a) probe below the tau_v/tau_a bounds, (b) re-tune select/blend
against the stage-2 components, including a 4-model select variant that gets
both the mean-tuned and the tail-aware hazard.

Run:  python3 -u refine2.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import predictors as P
from tune import (Objective, coord_search, load_corpus, split_segments, JUKE)

BALL2 = {'tau_v': 0.0025, 'tau_a': 0.01, 't_commit': 2.5, 't_max': 60.0}
HAZ2A = {'dead': 30.0, 'tau_flip': 8.0, 'tau_dir': 0.8, 'floor': 0.0,
         'commit_mult': 0.1, 't_recent': 0.2, 'c_damp': 0.2, 'c_tc': 0.0}
HAZ2B = {'dead': 15.0, 'tau_flip': 3.0, 'tau_dir': 0.5, 'floor': 0.0,
         'commit_mult': 0.4, 't_recent': 0.2, 'c_damp': 0.2, 'c_tc': 0.6}


def main():
    by_file, _ = load_corpus()
    tune_segs, _ = split_segments(by_file)
    obj = Objective(tune_segs)

    # (a) micro-probe below the smoothing bounds + t_commit neighborhood,
    #     jointly with the hazard damping that rides on top (haz2a fixed).
    grids = dict(
        tau_v=[0.001, 0.0025, 0.005],
        tau_a=[0.0025, 0.005, 0.01, 0.02],
        t_commit=[1.5, 2.0, 2.5, 3.5],
        c_damp=[0.1, 0.15, 0.2, 0.3],
        dead=[20.0, 30.0, 45.0],
        tau_flip=[5.0, 8.0, 12.0],
    )
    start = dict(BALL2, **HAZ2A)
    best, jb = coord_search(lambda p: P.BallisticHazard(**p), start, grids,
                            obj, sweeps=3, label="bh3")
    print("BH3 =", best, jb)
    ball3 = {k: best[k] for k in BALL2}
    haz3a = dict(HAZ2A)
    for k in ("c_damp", "dead", "tau_flip"):
        haz3a[k] = best[k]

    # (b) select / blend with the refreshed components
    cv = P.get("cv")

    def components():
        bh_a = P.BallisticHazard(name="bh_a", **dict(ball3, **haz3a))
        bh_b = P.BallisticHazard(name="bh_b", **dict(ball3, **HAZ2B))
        jp = P.JukePhase(**dict(JUKE, **dict(ball3, **haz3a)))
        return cv, bh_a, bh_b, jp

    def mk_sel3(p):
        cv_, bh_a, bh_b, jp = components()
        return P.Select(name="select", models=[cv_, bh_a, jp], mode="hard", **p)

    def mk_sel4(p):
        cv_, bh_a, bh_b, jp = components()
        return P.Select(name="select4", models=[cv_, bh_a, bh_b, jp],
                        mode="hard", **p)

    sel_grids = dict(tau_sel=[0.5, 1.0, 2.0, 4.0, 8.0], default=[1])
    sel3, j3 = coord_search(mk_sel3, dict(tau_sel=2.0, default=1), sel_grids,
                            obj, sweeps=2, label="select3")
    print("SELECT3 (cv,bh_a,jp) =", sel3, j3)
    sel4, j4 = coord_search(mk_sel4, dict(tau_sel=2.0, default=1), sel_grids,
                            obj, sweeps=2, label="select4")
    print("SELECT4 (cv,bh_a,bh_b,jp) =", sel4, j4)

    def mk_blend(p):
        cv_, bh_a, bh_b, jp = components()
        return P.Select(name="select_blend", models=[cv_, bh_a, bh_b, jp],
                        mode="blend", **dict(sel4, **p))

    blend_grids = dict(blend_p=[1.0, 2.0, 4.0, 8.0],
                       blend_eps=[0.01, 0.02, 0.05, 0.10])
    blend, jbl = coord_search(mk_blend, dict(blend_p=2.0, blend_eps=0.05),
                              blend_grids, obj, sweeps=2, label="blend3")
    print("BLEND3 =", blend, jbl)

    print()
    print("BALL3 =", ball3)
    print("HAZ3A =", haz3a)
    print("SELECT3 =", sel3, "J=", j3)
    print("SELECT4 =", sel4, "J=", j4)
    print("BLEND3 =", blend, "J=", jbl)


if __name__ == "__main__":
    main()
