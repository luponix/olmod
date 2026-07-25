#!/usr/bin/env python3
"""Offline lag-compensation predictor benchmark over .olr v8 replays.

Usage:
    python3 bench.py [paths or globs ...]
        default corpus: ~/.config/unity3d/Revival/Overload/Replays/*.olr

    --horizons 0.05,0.10,...   evaluation horizons in seconds
    --focus name,name          player-name substrings to break out (case-insensitive)
    --out DIR                  results dir (default: results/ next to this script)
    --stride N                 evaluate every Nth frame (default 1 = all)
    --predictors a,b,c         subset of registered predictors (default: hold,cv,cv_arc)
    --min-duration S           minimum segment length (default 2.0)

For every kept frame i of every flight segment and every horizon h, each
predictor forecasts the ship position at t[i]+h from the snapshot at i; ground
truth is the linear interpolation of the recorded positions at t[i]+h within
the same segment. Errors are Euclidean distances in meters.
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
import predictors as predictors_mod

DEFAULT_CORPUS = os.path.expanduser("~/.config/unity3d/Revival/Overload/Replays/*.olr")
DEFAULT_HORIZONS = (0.05, 0.10, 0.15, 0.20, 0.25, 0.30)
DEFAULT_FOCUS = ("Schmodderkatze", "Insaner")


def expand_paths(args_paths):
    if not args_paths:
        args_paths = [DEFAULT_CORPUS]
    files = []
    for a in args_paths:
        if os.path.isdir(a):
            files.extend(sorted(glob.glob(os.path.join(a, "*.olr"))))
        else:
            g = sorted(glob.glob(a))
            files.extend(g if g else [a])
    # dedupe, keep order
    seen = set()
    out = []
    for f in files:
        if f not in seen:
            seen.add(f)
            out.append(f)
    return out


def fmt_stats(e):
    return "n=%d mean=%.3f med=%.3f p90=%.3f p99=%.3f" % (
        e.size, e.mean(), np.median(e), np.percentile(e, 90), np.percentile(e, 99))


def stats_row(e):
    return (int(e.size), float(e.mean()), float(np.median(e)),
            float(np.percentile(e, 90)), float(np.percentile(e, 99)))


def print_table(title, preds, horizons, get_errors):
    """get_errors(pred_name, h) -> 1-D error array or None."""
    print()
    print(title)
    hdr = "%-8s %-6s %10s %9s %9s %9s %9s" % (
        "pred", "h(ms)", "samples", "mean", "median", "p90", "p99")
    print(hdr)
    print("-" * len(hdr))
    for p in preds:
        for h in horizons:
            e = get_errors(p.name, h)
            if e is None or e.size == 0:
                print("%-8s %-6d %10s" % (p.name, round(h * 1000), "-"))
                continue
            n, mean, med, p90, p99 = stats_row(e)
            print("%-8s %-6d %10d %9.3f %9.3f %9.3f %9.3f"
                  % (p.name, round(h * 1000), n, mean, med, p90, p99))
        print()


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("paths", nargs="*", help="replay files, globs, or dirs")
    ap.add_argument("--horizons", default=",".join(str(h) for h in DEFAULT_HORIZONS))
    ap.add_argument("--focus", default=",".join(DEFAULT_FOCUS))
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "results"))
    ap.add_argument("--stride", type=int, default=1)
    ap.add_argument("--predictors", default=None,
                    help="comma list; default hold,cv,cv_arc; 'all' = every registered")
    ap.add_argument("--min-duration", type=float, default=tracks.DEFAULT_MIN_DURATION)
    ap.add_argument("--max-gap", type=float, default=tracks.DEFAULT_MAX_GAP)
    ap.add_argument("--max-jump", type=float, default=tracks.DEFAULT_MAX_JUMP)
    args = ap.parse_args()

    horizons = tuple(float(x) for x in args.horizons.split(",") if x.strip())
    focus = [f.strip() for f in args.focus.split(",") if f.strip()]
    if args.predictors is None:
        preds = predictors_mod.default_predictors()
    elif args.predictors == "all":
        preds = predictors_mod.all_predictors()
    else:
        preds = [predictors_mod.get(n.strip()) for n in args.predictors.split(",")]

    files = expand_paths(args.paths)
    if not files:
        print("no input files", file=sys.stderr)
        return 2

    os.makedirs(args.out, exist_ok=True)

    version_tally = {}
    failures = []          # (path, reason)
    inventory = []         # dict rows
    seg_count = 0
    flight_seconds = 0.0
    flight_by_player = {}  # name -> seconds
    files_by_player = {}   # name -> set of files

    # errors[(pred_name, h)][player_name] = [np arrays]
    errors = {(p.name, h): {} for p in preds for h in horizons}
    speed_samples = []
    velmatch_samples = []
    t_start = time.time()

    for path in files:
        base = os.path.basename(path)
        try:
            rep = olr.load(path)
        except olr.UnsupportedVersion as e:
            version_tally[e.version] = version_tally.get(e.version, 0) + 1
            inventory.append({"file": base, "version": e.version, "ok": 0})
            continue
        except (olr.OlrError, OSError) as e:
            failures.append((base, str(e)))
            inventory.append({"file": base, "version": "", "ok": 0})
            continue
        version_tally[rep.version] = version_tally.get(rep.version, 0) + 1

        dts = np.diff(np.asarray(rep.t, dtype=np.float64))
        med_dt = float(np.median(dts[dts > 0])) if (dts > 0).any() else float("nan")
        inventory.append({
            "file": base, "version": rep.version, "ok": 1,
            "scene": rep.sceneName,
            "duration_s": round(float(rep.storedDuration), 2),
            "tickRateHz": rep.tickRateHz,
            "frames": rep.frameCount,
            "median_dt_s": round(med_dt, 5),
            "players": "|".join(p.name for p in rep.players),
        })

        segs = tracks.extract_tracks(rep, max_gap=args.max_gap, max_jump=args.max_jump,
                                     min_duration=args.min_duration)
        del rep

        for seg in segs:
            seg_count += 1
            flight_seconds += seg.duration
            flight_by_player[seg.player] = flight_by_player.get(seg.player, 0.0) + seg.duration
            files_by_player.setdefault(seg.player, set()).add(base)

            # --- sanity: speeds and recorded-vel vs position-derivative ---
            spd = np.linalg.norm(seg.vel, axis=1)
            speed_samples.append(spd[::8].astype(np.float32))
            if len(seg) >= 3:
                dtc = (seg.t[2:] - seg.t[:-2])[:, None]
                vd = (seg.pos[2:] - seg.pos[:-2]).astype(np.float64) / dtc
                vm = np.linalg.norm(seg.vel[1:-1].astype(np.float64) - vd, axis=1)
                velmatch_samples.append(vm[::8].astype(np.float32))

            # --- predictor evaluation ---
            t = seg.t
            for h in horizons:
                n_valid = int(np.searchsorted(t, t[-1] - h, side="right"))
                if n_valid <= 0:
                    continue
                idx = np.arange(0, n_valid, args.stride)
                tq = t[idx] + h
                gt = np.stack([np.interp(tq, t, seg.pos[:, k]) for k in range(3)], axis=1)
                for p in preds:
                    pr = p.predict(seg, idx, h)
                    e = np.linalg.norm(pr - gt, axis=1).astype(np.float32)
                    errors[(p.name, h)].setdefault(seg.player, []).append(e)
        del segs

    elapsed = time.time() - t_start

    # ------------------------------------------------------------------ report
    print("=" * 78)
    print("Corpus: %d files scanned in %.1f s" % (len(files), elapsed))
    for v in sorted(version_tally):
        tag = " (parsed)" if v == olr.VERSION else " (skipped: unsupported version)"
        print("  version %s: %d file(s)%s" % (v, version_tally[v], tag))
    if failures:
        print("  unreadable/corrupt: %d file(s)" % len(failures))
        for f, msg in failures:
            print("    %s: %s" % (f, msg))

    print()
    print("Per-file inventory (parsed files):")
    for row in inventory:
        if not row.get("ok"):
            continue
        print("  %-45s %-18s dur=%7.1fs tick=%dHz frames=%6d med_dt=%.5f"
              % (row["file"][:45], row["scene"][:18], row["duration_s"],
                 row["tickRateHz"], row["frames"], row["median_dt_s"]))
        print("      players: %s" % row["players"])

    print()
    print("Tracks: %d segments, %.0f flight-seconds total" % (seg_count, flight_seconds))

    # focus coverage
    focus_players = {}  # label -> [names]
    for label in focus:
        names = [n for n in flight_by_player if label.casefold() in n.casefold()]
        focus_players[label] = names
        secs = sum(flight_by_player[n] for n in names)
        fset = set()
        for n in names:
            fset |= files_by_player[n]
        print("Focus '%s': matched names %s -> %.0f flight-seconds in %d file(s)"
              % (label, names if names else "(none)", secs, len(fset)))

    # sanity
    print()
    print("Sanity checks:")
    if speed_samples:
        spd = np.concatenate(speed_samples)
        frac_fast = float((spd > 40.0).mean())
        print("  ship speed (m/s): median=%.2f p90=%.2f p99=%.2f max=%.2f; %.3f%% of samples > 40 m/s%s"
              % (np.median(spd), np.percentile(spd, 90), np.percentile(spd, 99),
                 spd.max(), 100 * frac_fast, "  <-- FLAG" if frac_fast > 0.01 else ""))
    if velmatch_samples:
        vm = np.concatenate(velmatch_samples)
        print("  |vel_recorded - vel_from_posdiff| (m/s): median=%.3f p90=%.3f p99=%.3f"
              % (np.median(vm), np.percentile(vm, 90), np.percentile(vm, 99)))

    # concat error arrays per group
    def group_concat(pred_name, h, names=None):
        d = errors[(pred_name, h)]
        arrs = []
        for n, lst in d.items():
            if names is not None and n not in names:
                continue
            arrs.extend(lst)
        if not arrs:
            return None
        return np.concatenate(arrs)

    print_table("=== Benchmark: OVERALL (%d segments, %.0f flight-s) ===" % (seg_count, flight_seconds),
                preds, horizons, lambda pn, h: group_concat(pn, h))

    for label, names in focus_players.items():
        if not names:
            print("\n=== Benchmark: %s === (no matching tracks)" % label)
            continue
        secs = sum(flight_by_player[n] for n in names)
        print_table("=== Benchmark: %s (%s, %.0f flight-s) ===" % (label, "+".join(names), secs),
                    preds, horizons, lambda pn, h, _n=tuple(names): group_concat(pn, h, _n))

    # ------------------------------------------------------------------ CSVs
    inv_path = os.path.join(args.out, "inventory.csv")
    inv_fields = ["file", "version", "ok", "scene", "duration_s", "tickRateHz",
                  "frames", "median_dt_s", "players"]
    with open(inv_path, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=inv_fields)
        w.writeheader()
        for row in inventory:
            w.writerow({k: row.get(k, "") for k in inv_fields})

    sum_path = os.path.join(args.out, "summary.csv")
    with open(sum_path, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["scope", "predictor", "horizon_ms", "samples",
                    "mean_m", "median_m", "p90_m", "p99_m"])

        def emit(scope, pn, h, e):
            if e is None or e.size == 0:
                return
            n, mean, med, p90, p99 = stats_row(e)
            w.writerow([scope, pn, round(h * 1000), n,
                        "%.4f" % mean, "%.4f" % med, "%.4f" % p90, "%.4f" % p99])

        for p in preds:
            for h in horizons:
                emit("overall", p.name, h, group_concat(p.name, h))
        for label, names in focus_players.items():
            for p in preds:
                for h in horizons:
                    emit("focus:" + label, p.name, h, group_concat(p.name, h, tuple(names)))
        for name in sorted(flight_by_player):
            for p in preds:
                for h in horizons:
                    emit("player:" + name, p.name, h, group_concat(p.name, h, (name,)))

    print()
    print("Wrote %s and %s" % (inv_path, sum_path))
    return 0


if __name__ == "__main__":
    sys.exit(main())
