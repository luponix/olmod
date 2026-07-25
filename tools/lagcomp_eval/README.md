# lagcomp_eval — the REAL C# prediction core vs recorded replays

Standalone (no game, no Unity, no LLM — just mono) evaluator that compiles
**the actual shipped prediction code** — `GameMod/MPShipPredictionCore.cs`,
the engine-independent core of the SMART lag-compensation mode
(`mms_lag_compensation_prediction_mode == 3`) — against a minimal
`UnityEngine` shim and runs it, plus the legacy extrapolation modes, over
recorded `.olr` v8 replays.

This is the companion to `tools/lagcomp_bench/` (Python). The Python bench
evaluates *re-implementations* of the predictors, which can drift from the
shipped C#; this tool evaluates the code the game actually runs. Same
methodology, directly comparable numbers.

## Usage

```sh
# build + run over the default corpus (~/.config/unity3d/Revival/Overload/Replays)
tools/lagcomp_eval/run.sh

# specific files or a directory
tools/lagcomp_eval/run.sh --replays /path/to/Replays
tools/lagcomp_eval/run.sh some_match.olr other_match.olr

# options
tools/lagcomp_eval/run.sh --horizons 0.1,0.2,0.3 --modes hold,cv,arc,smart \
                          --focus Schmodderkatze --csv results.csv

# parameter sweep: override any PredictionParams field (repeatable)
tools/lagcomp_eval/run.sh --modes smart --set HazardDamping=0.3
tools/lagcomp_eval/run.sh --modes smart --set FlipRateTau=3 --set ThrustMax=50

# shim sanity checks (quaternion conventions, half decode, Vector3 ==)
tools/lagcomp_eval/run.sh --selftest
```

`run.sh` rebuilds automatically when a source (including the core file in
`GameMod/`) is newer than the exe; `build.sh` forces a build. Both work from
any cwd. `mono lagcomp_eval.exe ...` works too once built.

- `--focus NAME` — extra breakout table for players whose recorded name
  contains NAME (substring, case-insensitive; comma list allowed).
- `--set Param=Value` — writes the `PredictionParams` static field via
  reflection before evaluation. Pass a wrong name to get the full field list
  with current values.
- `--csv OUT` — per (scope, mode, horizon) stats; scopes `overall`,
  `focus:<label>`, `player:<exact name>`.
- Non-v8 `.olr` files are skipped and tallied.

## Modes

| mode | what runs |
|---|---|
| `hold` | last received position (what pure interpolation shows) |
| `cv` | `pos + vel*h` — legacy prediction modes 0/1 position math (`MPClientExtrapolation.extrapolatePlayer`) |
| `arc` | legacy Motion Arc (mode 2) position math at rotation strength 100% (rotScale 1.0, arcRotScale 0.34), exact copy of the extrapolatePlayer formulas |
| `smart` | **the real core**: `ShipTrack` tracker + reversal hazard + adaptive horizon + synthetic-tick freeze + prediction ledger + online model selection (`ShipPredictionCore.PredictSmart`) |

For `smart`, each flight segment's snapshots are streamed into a fresh
`ShipTrack` in frame order — exactly the game's capture path — and the
prediction at frame *i* uses only samples ≤ *i* (the ledger scores itself as
later snapshots arrive, as in-game). One track per (segment, horizon), since
in-game the horizon is the roughly constant ping-derived delta and the ledger
must score the horizon it renders.

## Methodology (identical to the Python bench)

Segments: per roster slot, contiguous present-AND-alive frame runs, split on
`dt > 0.5 s`, `dt <= 0`, or position jump `> 20 m`; segments with fewer than
3 frames or shorter than 2 s are discarded. Ground truth: linear
interpolation of the recorded positions at `t+h` within the same segment.
Error: Euclidean distance in meters. Stats: mean / median / p90 / p99
(numpy-style linear-interpolation percentiles).

## What differs from the in-game pipeline

The evaluator measures the **raw position prediction** only:

- **No projective smoothing** (`SmoothTau` render-offset decay) — render-side
  cosmetics, needs `Time.deltaTime`/frame pacing.
- **No wall-slide collision clamp** — needs `Physics.Raycast` + level
  geometry.
- No rotation evaluation (position error only).
- Snapshot velocities come from the replay's half-precision (binary16)
  encoding; a live client gets full floats. Same data the Python bench uses.
- The ledger-filing throttle runs on the replay clock instead of `Time.time`
  (that clock is a parameter of the core — the game passes `Time.time`).

## What differs from the Python bench

- `smart` here is the shipped C# — including the safety nets the Python
  `select` predictor does not model: cold-start CV fallback
  (`MinSnapsForSmart`), residual priors + `MinModelSamples` gate,
  model-switch hysteresis (`ModelSwitchMargin`), the synthetic-tick horizon
  freeze, and the shipped (slightly widened) smoothing taus
  `VelSmoothTau = 0.005` / `AccSmoothTau = 0.01` (Python tuned optimum was
  0.001/0.005; see `tools/lagcomp_bench/DESIGN.md` §4).
- All arithmetic is float (like the engine); the Python bench runs float64.

## Cross-check (13-replay corpus, defaults, 2026-07)

Full-corpus numbers from this tool vs the Python bench's expectations:

| mode | h | this tool (mean m) | Python reference | note |
|---|---|---|---|---|
| hold | 200 ms | 1.806 | ≈ 1.8 | |
| cv | 200 ms | 0.446 | 0.445 (full corpus) / 0.443 (validate) | matches |
| arc | 200 ms | 0.456 | ≈ cv (wash) | matches |
| smart | 200 ms | **0.272** | ballistic_hazard 0.262 / select 0.264–0.265 (validate) | +3–4%: expected — the real core pays for the safety nets above (CV cold-start frames, maturing residuals after each track reset, widened smoothing taus) |
| smart | 100 ms | 0.072 | 0.069 | |
| smart | 300 ms | 0.621 | 0.603–0.608 | |

Focus (Schmodderkatze): cv@200 0.577 vs 0.572; smart@200 0.353 vs
0.347–0.348 — same ≲2% deltas.

Sweep sanity: `--set HazardDamping=0.0 / 0.3 / 0.9` moves smart@200 from
0.273 / 0.273 / **0.294** mean (median 0.165 / 0.176 / 0.205) — flat near the
tuned optimum 0.15, clearly worse far away, as the tuning surface predicts.

## Files

| file | role |
|---|---|
| `UnityShim.cs` | minimal `UnityEngine.Vector3/Quaternion/Mathf` (Unity semantics where they matter: approximate `Vector3 ==`, `Mathf.Sign(0)=1`, ZXY `Quaternion.Euler`); compiled only into the evaluator, never into GameMod.dll |
| `OlrReader.cs` | `.olr` v8 parser (header/roster/frames; events/chat skipped); manual IEEE binary16 decode for vel/vrot |
| `Evaluator.cs` | CLI, segmentation, ground truth, mode evaluation, stats, CSV, `--selftest` |
| `build.sh` / `run.sh` | mcs build (pulls `../../GameMod/MPShipPredictionCore.cs`) / build-if-stale + run |
