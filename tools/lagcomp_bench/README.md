# lagcomp_bench — offline lag-compensation predictor benchmark

Parses recorded Overload replay files (`.olr` v8, written by olmod's replay
recorder — they contain the raw 60 Hz server snapshot stream per ship) and
measures how well candidate extrapolation predictors forecast ship positions
at horizons of 50–300 ms against recorded ground truth. Used to evaluate
replacements for olmod's current constant-velocity client-side lag
compensation (`GameMod/MPClientExtrapolation.cs`, `extrapolatePlayer`).

## Usage

```sh
# whole default corpus (~/.config/unity3d/Revival/Overload/Replays/*.olr)
python3 bench.py

# specific files / globs / dirs
python3 bench.py '/path/to/Replays/Replay_202607*.olr'

# options
python3 bench.py --horizons 0.05,0.1,0.2 --focus Schmodderkatze,Insaner \
                 --predictors hold,cv,cv_arc --stride 1 --out results
```

Requires Python 3 + numpy (the parser alone falls back to pure stdlib, the
benchmark itself is numpy-vectorized). Non-v8 files are skipped and tallied.

Outputs a human-readable report to stdout and two CSVs to `results/`:

- `inventory.csv` — per file: version, scene, duration, tick rate, frames,
  median frame dt, player roster
- `summary.csv` — per (scope, predictor, horizon): sample count, mean /
  median / p90 / p99 error in meters. Scopes: `overall`, `focus:<name>`
  (substring-matched player groups), `player:<exact recorded name>`

## Methodology

- **Tracks** (`tracks.py`): per roster slot, contiguous frame runs where the
  ship is present AND alive; split when the consecutive-frame time gap
  exceeds 0.5 s, time goes backwards, or the position jumps > 20 m
  (respawn/teleport); segments shorter than 2 s are discarded.
- **Evaluation** (`bench.py`): for every frame `i` of a segment and horizon
  `h`, a predictor forecasts the position at `t[i]+h` from the snapshot state
  at `i`; ground truth is the linear interpolation of the recorded positions
  at `t[i]+h` within the same segment. Error = Euclidean distance (m).
- **Predictors** (`predictors.py`):
  - `hold` — last received position (what pure interpolation shows)
  - `cv` — `pos + vel*h`; olmod's current extrapolation
  - `cv_arc` — faithful emulation of olmod's "Motion Arc" mode
    (`mms_lag_compensation_prediction_mode == 2`) at 100 % rotation strength:
    `newRot = Euler(vrot*Rad2Deg*h) * rot`, then blend 34 % of the way from
    the linear position to `pos + (Inverse(rot)*newRot)*vel*h`
  - `ballistic` — drag closed form (k = 2.5 s^-1) with thrust
    T = acc + k*vel estimated from EWMA-differentiated velocity, decayed
    over the horizon by exp(-h/t_commit)
  - `ballistic_hazard` — ballistic plus reversal-hazard horizon damping
    (decaying Poisson rate of lateral-thrust sign flips ->
    h_eff = h*(1 - c_damp*(1-exp(-rate*h))))
  - `jukephase` — sinusoidal lateral-velocity model when regular jukes are
    detected (zero-crossing regularity gate); falls back to ballistic_hazard.
    On this corpus the gate essentially never fires — kept for reference.
  - `select` / `select_blend` — online model selection via a causal
    prediction ledger (per-model EWMA of residuals scored when the target
    sample arrives, strictly causal); hard argmin or residual-weighted blend
  - All non-baseline predictors carry **tuned constants as their defaults**
    (tuned by `tune.py --search` on a file-level tune/validate split; see
    `tune.py` for the split + constants and `results/tuned_summary.csv` for
    the final numbers). All state is strictly causal and per-frame
    incremental — portable 1:1 to a real-time C# client.
  - Add more by subclassing `predictors.Predictor` and calling
    `predictors.register(...)` (then select via `--predictors`).

## Tuning (`tune.py`, `refine.py`, `refine2.py`)

`tune.py --search` runs a coordinate-descent search on the tune half of the
corpus (objective: cv-normalized mean error @200 ms, overall/Schmodderkatze
blended 50/50; p99 @300 ms as tiebreaker). `tune.py --report` prints the full
error tables (tune + validate, overall + focus, 50–300 ms), a frame-to-frame
snap metric, and a synthetic-tick scan, and writes
`results/tuned_summary.csv` + `results/snap_metric.csv`. `refine.py` /
`refine2.py` are the stage-2/3 extended-grid searches that produced the
final constants.

## .olr v8 binary format

Little-endian, written by .NET `BinaryWriter`. Strings are 7-bit-varint
length-prefixed UTF-8 (LEB128-style length). Half floats are IEEE 754
binary16.

**Header**

| field | type |
|---|---|
| magic | uint32 = `0x314C4F52` (`"ROL1"` LE, bytes `OLR1`) |
| version | byte, must be **8** |
| sceneName | string |
| levelIdHash | string |
| matchMode | int32 |
| tickRateHz | int32 |
| storedDuration | float32 (seconds) |
| maxHitpoints | float32 |
| matchTimeLimit | float32 (0 = none) |
| matchStartOffset | float32 |
| playerCount | int32, then per player: string name, int32 team, float32 r, g, b |

**Frames** — int32 frameCount, then per frame:

- float32 `t` (seconds, server match-elapsed, normalized to start at 0)
- playerCount ship states, each:
  - byte flags (bit0 = present, bit1 = alive). If present NOT set: nothing
    more for this slot.
  - if present (56 more bytes): float32 pos.x,y,z; float32 rot.x,y,z,w
    (quaternion); byte weapon; byte missile; byte health; 3× uint16 half
    vel.x,y,z (m/s); 3× uint16 half vrot.x,y,z (rad/s); byte energy; uint16
    ammo; byte weaponsOwned; byte missilesOwned; uint64 missileAmmo
- uint16 itemCount, then per item: uint16 id, float32 pos.x,y,z

**Events** — int32 eventCount, then per event: float32 t; byte type; int32
iA, iB, iC; float32 fA; float32 pos.x,y,z; float32 rot.x,y,z,w; string nameA;
string nameB

**Chat** — int32 chatCount, then per chat: float32 t; int32 sender; int32
team; byte kind; string senderName; string message

## Files

| file | role |
|---|---|
| `olr.py` | `.olr` v8 parser: `load(path)` -> header + roster + per-slot numpy arrays (t, pos, vel, rot, vrot, present, alive); `read_info(path)` header-only |
| `tracks.py` | flight-segment extraction |
| `predictors.py` | predictor interface + `hold` / `cv` / `cv_arc` baselines + `ballistic` / `ballistic_hazard` / `jukephase` / `select` candidates (tuned defaults) + causal EWMA/decay/hysteresis helpers + Unity-convention quaternion helpers |
| `bench.py` | CLI: corpus inventory, sanity checks, benchmark, CSV output |
| `tune.py` | tune/validate split, coordinate-descent search, final report (error tables, snap metric, synthetic-tick scan), tuned constants |
| `refine.py`, `refine2.py` | stage-2/3 extended-grid searches (kept for reproducibility) |
