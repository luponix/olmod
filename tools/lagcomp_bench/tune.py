#!/usr/bin/env python3
"""Tuning driver for the candidate lag-compensation predictors.

Loads the .olr v8 corpus once, splits it into a tune and a validate set
(~50/50 by flight-seconds, Schmodderkatze data in both, one 30 Hz-effective
file in each), then:

    python3 tune.py --search            coordinate-descent search on the TUNE
                                        set only (prints best constants)
    python3 tune.py --report            full error tables (tune + validate,
                                        overall + Schmodderkatze), snap metric,
                                        synthetic-tick scan, CSV output
    python3 tune.py --search --report   both

Objective for the search: J = 0.5*(mean@200ms / cv) overall
                            + 0.5*(mean@200ms / cv) Schmodderkatze
on the tune set; ties broken by the same blend of p99@300ms.
"""

import argparse
import csv
import glob
import os
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import olr
import tracks
import predictors as P

CORPUS = os.path.expanduser("~/.config/unity3d/Revival/Overload/Replays/*.olr")
FOCUS = "schmodderkatze"
HORIZONS_FULL = (0.05, 0.10, 0.15, 0.20, 0.25, 0.30)
HORIZONS_SEARCH = (0.20, 0.30)

TUNE_FILES = {
    "Replay_20260715_205141_swat.olr",
    "Replay_20260717_191310_black forest ham.olr",
    "Replay_20260718_212527_Whoopass_Cannery.olr",
    "Replay_20260718_214459_Skybox_V1.2.olr",
    "Replay_20260718_223946_Sub Rosa v4.olr",
    "Replay_20260722_210639_swat.olr",          # 30 Hz effective
    "Replay_20260725_143344_JazzyBox.olr",
}
VALIDATE_FILES = {
    "Replay_20260717_193642_black forest ham.olr",
    "Replay_20260718_210643_mp_vault.olr",
    "Replay_20260718_220325_Ancient.olr",
    "Replay_20260718_222150_black forest ham.olr",
    "Replay_20260722_212603_Skybox_V1.2.olr",
    "Replay_20260722_214255_Sub Rosa v4.olr",   # 30 Hz effective
}

# ---------------------------------------------------------------------------
# Tuned constants (updated from --search results; used by --report and by the
# registered predictor defaults check).
# ---------------------------------------------------------------------------

# Final constants after 3 search stages on the TUNE set only (logs:
# search1.log / refine.log / refine2.log in the session scratchpad).
BALLISTIC = dict(tau_v=0.001, tau_a=0.005, t_commit=3.5, t_max=60.0)
HAZARD = dict(dead=30.0, tau_flip=5.0, tau_dir=0.8, floor=0.0,
              commit_mult=0.1, t_recent=0.2, c_damp=0.15, c_tc=0.0)
# tail-aware alternate hazard (tuned on 0.5*mean200 + 0.5*p99_300): the
# "tail-guard" component for the 4-model select
HAZARD_TAIL = dict(dead=15.0, tau_flip=3.0, tau_dir=0.5, floor=0.0,
                   commit_mult=0.4, t_recent=0.2, c_damp=0.2, c_tc=0.6)
JUKE = dict(dead_l=0.5, tau_cross=1.5, min_count=3.0, cv_max=0.25,
            amp_min=1.5, half_min=0.15, half_max=0.9, overdue=1.2,
            a_fit_max=2.0)
SELECT = dict(tau_sel=2.0, default=1)
SELECT4 = dict(tau_sel=8.0, default=1)
BLEND = dict(blend_p=8.0, blend_eps=0.01)


def build_predictors():
    ball = P.Ballistic(**BALLISTIC)
    bh = P.BallisticHazard(**dict(BALLISTIC, **HAZARD))
    bh_tail = P.BallisticHazard(name="bh_tail", **dict(BALLISTIC, **HAZARD_TAIL))
    jp = P.JukePhase(**dict(JUKE, **dict(BALLISTIC, **HAZARD)))
    cv = P.get("cv")
    sel = P.Select(name="select", models=[cv, bh, jp], mode="hard", **SELECT)
    sel4 = P.Select(name="select4", models=[cv, bh, bh_tail, jp], mode="hard",
                    **SELECT4)
    selb = P.Select(name="select_blend", models=[cv, bh, bh_tail, jp],
                    mode="blend", **dict(SELECT4, **BLEND))
    return [cv, ball, bh, jp, sel, sel4, selb]


# ---------------------------------------------------------------------------
# Corpus loading + synthetic-tick scan
# ---------------------------------------------------------------------------

def synthfreeze_scan(rep):
    """Runs of >=3 consecutive bit-identical vel samples while |v|^2 > 0.25."""
    runs = 0
    frames = 0
    longest = 0
    for sd in rep.slots:
        v = sd.vel
        pres = sd.present
        eq = (np.all(v[1:] == v[:-1], axis=1) & pres[1:] & pres[:-1]
              & (np.nansum(v[1:] * v[1:], axis=1) > 0.25))
        if not eq.any():
            continue
        # run-lengths of consecutive True; a run of L 'equal' flags means
        # L+1 identical samples -> need L >= 2
        d = np.diff(np.concatenate(([0], eq.view(np.int8), [0])))
        starts = np.flatnonzero(d == 1)
        ends = np.flatnonzero(d == -1)
        ln = ends - starts
        keep = ln >= 2
        runs += int(keep.sum())
        frames += int((ln[keep] + 1).sum())
        if keep.any():
            longest = max(longest, int(ln[keep].max()) + 1)
    return runs, frames, longest


def load_corpus():
    files = sorted(glob.glob(CORPUS))
    by_file = {}
    synth = {"runs": 0, "frames": 0, "longest": 0, "present_frames": 0}
    for f in files:
        try:
            rep = olr.load(f)
        except olr.OlrError:
            continue
        r, fr, lg = synthfreeze_scan(rep)
        synth["runs"] += r
        synth["frames"] += fr
        synth["longest"] = max(synth["longest"], lg)
        synth["present_frames"] += int(sum(sd.present.sum() for sd in rep.slots))
        segs = tracks.extract_tracks(rep)
        del rep
        by_file[os.path.basename(f)] = segs
    return by_file, synth


def split_segments(by_file):
    tune, val = [], []
    for base, segs in by_file.items():
        if base in TUNE_FILES:
            tune.extend(segs)
        elif base in VALIDATE_FILES:
            val.extend(segs)
        else:
            print("WARNING: %s in neither split, ignored" % base)
    return tune, val


# ---------------------------------------------------------------------------
# Evaluation
# ---------------------------------------------------------------------------

def eval_errors(pred, segs, horizons, stride=1):
    """-> {h: {"overall": [arrays], "focus": [arrays]}}"""
    out = {h: {"overall": [], "focus": []} for h in horizons}
    for seg in segs:
        t = seg.t
        is_focus = FOCUS in seg.player.casefold()
        for h in horizons:
            n_valid = int(np.searchsorted(t, t[-1] - h, side="right"))
            if n_valid <= 0:
                continue
            idx = np.arange(0, n_valid, stride)
            tq = t[idx] + h
            gt = np.stack([np.interp(tq, t, seg.pos[:, k]) for k in range(3)],
                          axis=1)
            pr = pred.predict(seg, idx, h)
            e = np.linalg.norm(pr - gt, axis=1).astype(np.float32)
            out[h]["overall"].append(e)
            if is_focus:
                out[h]["focus"].append(e)
    return out


def stats(arrs):
    if not arrs:
        return None
    e = np.concatenate(arrs)
    return dict(n=e.size, mean=float(e.mean()), med=float(np.median(e)),
                p90=float(np.percentile(e, 90)), p99=float(np.percentile(e, 99)))


def snap_metric(pred, segs, h):
    """Mean frame-to-frame discontinuity of the rendered (predicted) position
    beyond actual motion: mean || d(pred) - d(gt) || over consecutive frames."""
    tot = 0.0
    n = 0
    for seg in segs:
        t = seg.t
        n_valid = int(np.searchsorted(t, t[-1] - h, side="right"))
        if n_valid < 2:
            continue
        idx = np.arange(n_valid)
        tq = t[idx] + h
        gt = np.stack([np.interp(tq, t, seg.pos[:, k]) for k in range(3)],
                      axis=1)
        pr = pred.predict(seg, idx, h)
        d = np.linalg.norm(np.diff(pr, axis=0) - np.diff(gt, axis=0), axis=1)
        tot += float(d.sum())
        n += d.size
    return tot / max(n, 1)


# ---------------------------------------------------------------------------
# Coordinate-descent search
# ---------------------------------------------------------------------------

class Objective(object):
    def __init__(self, segs, stride=2):
        self.segs = segs
        self.stride = stride
        cv = eval_errors(P.get("cv"), segs, HORIZONS_SEARCH, stride)
        self.cv200 = (stats(cv[0.20]["overall"])["mean"],
                      stats(cv[0.20]["focus"])["mean"])
        self.cv300p99 = (stats(cv[0.30]["overall"])["p99"],
                         stats(cv[0.30]["focus"])["p99"])

    def __call__(self, pred):
        r = eval_errors(pred, self.segs, HORIZONS_SEARCH, self.stride)
        P.clear_seg_caches(self.segs)
        o200 = stats(r[0.20]["overall"])["mean"]
        f200 = stats(r[0.20]["focus"])["mean"]
        o399 = stats(r[0.30]["overall"])["p99"]
        f399 = stats(r[0.30]["focus"])["p99"]
        j = 0.5 * o200 / self.cv200[0] + 0.5 * f200 / self.cv200[1]
        tie = 0.5 * o399 / self.cv300p99[0] + 0.5 * f399 / self.cv300p99[1]
        return j, tie


def coord_search(make_pred, params, grids, objective, sweeps=2, label=""):
    """Coordinate descent: for each param, try grid values keeping the rest.

    make_pred(params_dict) -> Predictor. Returns (best_params, best_J)."""
    cur = dict(params)
    best = objective(make_pred(cur))
    print("[%s] start J=%.4f tie=%.4f  %s" % (label, best[0], best[1], cur))
    for sweep in range(sweeps):
        improved = False
        for key, values in grids.items():
            for v in values:
                if v == cur[key]:
                    continue
                cand = dict(cur, **{key: v})
                j = objective(make_pred(cand))
                if (j[0] < best[0] - 1e-5 or
                        (abs(j[0] - best[0]) <= 1e-5 and j[1] < best[1] - 1e-4)):
                    best = j
                    cur = cand
                    improved = True
                    print("[%s]   %s=%s -> J=%.4f tie=%.4f"
                          % (label, key, v, j[0], j[1]))
        print("[%s] sweep %d done: J=%.4f tie=%.4f  %s"
              % (label, sweep + 1, best[0], best[1], cur))
        if not improved:
            break
    return cur, best


def run_search(tune_segs):
    obj = Objective(tune_segs)
    print("cv reference (tune set): mean@200 overall=%.4f focus=%.4f; "
          "p99@300 overall=%.4f focus=%.4f"
          % (obj.cv200[0], obj.cv200[1], obj.cv300p99[0], obj.cv300p99[1]))

    # --- ballistic ---
    ball_grids = dict(
        tau_v=[0.005, 0.01, 0.02, 0.04, 0.08],
        tau_a=[0.03, 0.06, 0.10, 0.15, 0.25],
        t_commit=[0.10, 0.15, 0.20, 0.30, 0.45, 0.70, 1.00],
        t_max=[40.0, 60.0, 90.0],
    )
    ball, _ = coord_search(lambda p: P.Ballistic(**p), BALLISTIC, ball_grids,
                           obj, label="ballistic")
    print("BALLISTIC =", ball)

    # --- ballistic_hazard (on top of tuned ballistic) ---
    haz_grids = dict(
        dead=[3.0, 6.0, 10.0, 15.0],
        tau_flip=[1.0, 2.0, 3.0],
        tau_dir=[0.15, 0.30, 0.50],
        floor=[0.0, 0.40, 0.60, 0.80],
        commit_mult=[0.20, 0.50, 0.80, 1.00],
        t_recent=[0.30, 0.50, 0.80],
        c_damp=[0.20, 0.40, 0.60, 0.80, 1.00],
        c_tc=[0.00, 0.30, 0.60, 1.00],
    )
    haz0 = dict(HAZARD)
    haz, _ = coord_search(lambda p: P.BallisticHazard(**dict(ball, **p)),
                          haz0, haz_grids, obj, label="hazard")
    print("HAZARD =", haz)

    # --- jukephase (on top of tuned hazard) ---
    juke_grids = dict(
        dead_l=[0.3, 0.5, 1.0],
        tau_cross=[1.0, 1.5, 2.5],
        min_count=[2.0, 3.0, 4.0],
        cv_max=[0.15, 0.25, 0.35],
        amp_min=[1.0, 1.5, 2.5],
        overdue=[1.2, 1.6, 2.2],
        a_fit_max=[1.5, 2.0, 3.0],
        half_max=[0.6, 0.9, 1.2],
    )
    juke0 = dict(JUKE)
    juke, _ = coord_search(
        lambda p: P.JukePhase(**dict(p, **dict(ball, **haz))),
        juke0, juke_grids, obj, label="jukephase")
    print("JUKE =", juke)

    # --- select ---
    cv = P.get("cv")

    def mk_sel(p):
        bh = P.BallisticHazard(**dict(ball, **haz))
        jp = P.JukePhase(**dict(juke, **dict(ball, **haz)))
        return P.Select(name="select", models=[cv, bh, jp], mode="hard", **p)

    sel_grids = dict(tau_sel=[0.5, 1.0, 1.5, 2.5, 4.0], default=[0, 1])
    sel, _ = coord_search(mk_sel, dict(SELECT), sel_grids, obj, label="select")
    print("SELECT =", sel)

    def mk_blend(p):
        bh = P.BallisticHazard(**dict(ball, **haz))
        jp = P.JukePhase(**dict(juke, **dict(ball, **haz)))
        return P.Select(name="select_blend", models=[cv, bh, jp], mode="blend",
                        **dict(sel, **p))

    blend_grids = dict(blend_p=[1.0, 2.0, 4.0], blend_eps=[0.02, 0.05, 0.10])
    blend, _ = coord_search(mk_blend, dict(BLEND), blend_grids, obj,
                            label="blend")
    print("BLEND =", blend)

    print()
    print("=== paste into tune.py / predictors.py defaults ===")
    print("BALLISTIC =", ball)
    print("HAZARD =", haz)
    print("JUKE =", juke)
    print("SELECT =", sel)
    print("BLEND =", blend)


# ---------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------

def report(tune_segs, val_segs, synth, out_dir):
    preds = build_predictors()
    rows = []
    for set_name, segs in (("tune", tune_segs), ("validate", val_segs)):
        results = {}
        for pred in preds:
            t0 = time.time()
            results[pred.name] = eval_errors(pred, segs, HORIZONS_FULL)
            P.clear_seg_caches(segs)
            print("  evaluated %-13s on %-8s (%.1f s)"
                  % (pred.name, set_name, time.time() - t0))
        for scope in ("overall", "focus"):
            print()
            print("=== %s / %s ===" % (set_name, scope))
            hdr = ("%-13s %-6s %10s %9s %9s %9s %9s %8s %8s"
                   % ("pred", "h(ms)", "samples", "mean", "median", "p90",
                      "p99", "d_mean%", "d_p99%"))
            print(hdr)
            print("-" * len(hdr))
            for pred in preds:
                for h in HORIZONS_FULL:
                    s = stats(results[pred.name][h][scope])
                    if s is None:
                        continue
                    c = stats(results["cv"][h][scope])
                    dm = 100.0 * (s["mean"] - c["mean"]) / c["mean"]
                    dp = 100.0 * (s["p99"] - c["p99"]) / c["p99"]
                    print("%-13s %-6d %10d %9.3f %9.3f %9.3f %9.3f %8.1f %8.1f"
                          % (pred.name, round(h * 1000), s["n"], s["mean"],
                             s["med"], s["p90"], s["p99"], dm, dp))
                    rows.append([set_name, scope, pred.name, round(h * 1000),
                                 s["n"], "%.4f" % s["mean"], "%.4f" % s["med"],
                                 "%.4f" % s["p90"], "%.4f" % s["p99"],
                                 "%.2f" % dm, "%.2f" % dp])
                print()

    # snap metric on the validate set
    print("=== snap metric (validate set): mean ||d(pred)-d(gt)|| per frame, m ===")
    snap_rows = []
    for h in (0.20, 0.30):
        for pred in preds:
            v = snap_metric(pred, val_segs, h)
            P.clear_seg_caches(val_segs)
            print("  h=%dms %-13s %.4f" % (round(h * 1000), pred.name, v))
            snap_rows.append([round(h * 1000), pred.name, "%.5f" % v])

    print()
    print("=== synthetic-tick (bit-identical vel) scan, whole corpus ===")
    print("  runs(>=3 identical samples, |v|^2>0.25): %d  frames: %d  "
          "longest run: %d  (of %d present ship-frames)"
          % (synth["runs"], synth["frames"], synth["longest"],
             synth["present_frames"]))

    os.makedirs(out_dir, exist_ok=True)
    p1 = os.path.join(out_dir, "tuned_summary.csv")
    with open(p1, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["set", "scope", "predictor", "horizon_ms", "samples",
                    "mean_m", "median_m", "p90_m", "p99_m",
                    "mean_vs_cv_pct", "p99_vs_cv_pct"])
        w.writerows(rows)
    p2 = os.path.join(out_dir, "snap_metric.csv")
    with open(p2, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["horizon_ms", "predictor", "snap_m"])
        w.writerows(snap_rows)
    print("wrote %s and %s" % (p1, p2))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--search", action="store_true")
    ap.add_argument("--report", action="store_true")
    ap.add_argument("--out", default=os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "results"))
    args = ap.parse_args()
    if not (args.search or args.report):
        args.report = True

    t0 = time.time()
    by_file, synth = load_corpus()
    tune_segs, val_segs = split_segments(by_file)
    ts = sum(s.duration for s in tune_segs)
    vs = sum(s.duration for s in val_segs)
    tsf = sum(s.duration for s in tune_segs if FOCUS in s.player.casefold())
    vsf = sum(s.duration for s in val_segs if FOCUS in s.player.casefold())
    print("corpus loaded in %.1f s: tune %d segs %.0f fs (%.0f focus), "
          "validate %d segs %.0f fs (%.0f focus)"
          % (time.time() - t0, len(tune_segs), ts, tsf,
             len(val_segs), vs, vsf))

    if args.search:
        run_search(tune_segs)
    if args.report:
        report(tune_segs, val_segs, synth, args.out)


if __name__ == "__main__":
    main()
