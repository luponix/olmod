# lagcomp_bench — DESIGN & tuning record

Offline benchmark that produced the tuned constants in
`GameMod/MPShipPrediction.cs` (the SMART lag-compensation prediction mode,
`mms_lag_compensation_prediction_mode == 3`). `predictors.py` is the
**semantic source of truth** for the tracker/predictor math — the C# port was
made to match it, not the other way around.

## 1. Methodology

### Corpus
13 `.olr` v8 replays recorded by olmod's replay recorder
(`~/.config/unity3d/Revival/Overload/Replays/*.olr`). Each file contains the
raw per-ship 60 Hz server snapshot stream (pos, vel, rot, vrot per frame), so
predictors can be evaluated against exactly the data a live client receives.
Tracks (`tracks.py`): per roster slot, contiguous present-and-alive runs;
split on time gaps > 0.5 s, backward time, or position jumps > 20 m
(respawn/teleport); segments < 2 s discarded.

### Split
Tune / validate ≈ 50/50 by flight-seconds (7 / 6 files, listed in `tune.py`
`TUNE_FILES` / `VALIDATE_FILES`). The focus player (Schmodderkatze, the
jukiest mover in the corpus) appears in both splits; one 30 Hz-effective file
is in each split so the tuning isn't 60 Hz-only.

### Search
Coordinate descent (`tune.py --search`), on the TUNE set only, in stages:
ballistic → hazard (on top of tuned ballistic) → jukephase → select → blend.
Objective `J = 0.5*(mean@200ms / cv)_overall + 0.5*(mean@200ms / cv)_focus`;
ties broken by the same blend of p99@300ms. Three stages of grids
(search1/refine/refine2); final constants are the `BALLISTIC`/`HAZARD`/…
dicts at the top of `tune.py`.

### Causality
Every helper in `predictors.py` below the "causal state-estimation helpers"
banner is semantically a per-frame incremental update (EWMA, exponential
decay, hysteresis, event counters) exactly as a real-time C# client runs it
on each arriving snapshot — merely *evaluated* vectorized for speed. No
helper looks at samples after the current index. The `Select` ledger only
scores a prediction once its target time has arrived (target ≤ now), i.e.
the same information a live client would have.

## 2. Validated results (VALIDATE set, never touched by the search)

Error = Euclidean distance (m) between the predicted and the actual
(interpolated) position at `t + h`. `cv` is olmod's current constant-velocity
extrapolation (`pos + vel*h`).

### Overall

| predictor | h (ms) | mean (m) | p99 (m) | mean vs cv | p99 vs cv |
|---|---|---|---|---|---|
| cv | 100 | 0.133 | 0.586 | — | — |
| cv | 200 | 0.443 | 1.442 | — | — |
| cv | 300 | 0.902 | 2.608 | — | — |
| ballistic_hazard | 100 | 0.069 | 0.558 | **−48.2%** | −4.8% |
| ballistic_hazard | 200 | 0.262 | 1.360 | **−41.0%** | −5.7% |
| ballistic_hazard | 300 | 0.603 | 2.422 | **−33.1%** | −7.1% |
| select | 100 | 0.069 | 0.558 | −47.8% | −4.8% |
| select | 200 | 0.264 | 1.360 | −40.4% | −5.7% |
| select | 300 | 0.608 | 2.417 | −32.6% | −7.3% |

### Schmodderkatze (focus)

| predictor | h (ms) | mean (m) | p99 (m) | mean vs cv | p99 vs cv |
|---|---|---|---|---|---|
| cv | 100 | 0.168 | 0.655 | — | — |
| cv | 200 | 0.572 | 1.613 | — | — |
| cv | 300 | 1.168 | 2.887 | — | — |
| ballistic_hazard | 100 | 0.089 | 0.609 | **−47.2%** | −7.1% |
| ballistic_hazard | 200 | 0.347 | 1.464 | **−39.4%** | −9.3% |
| ballistic_hazard | 300 | 0.794 | 2.568 | **−32.0%** | −11.0% |
| select | 100 | 0.089 | 0.609 | −47.0% | −7.1% |
| select | 200 | 0.348 | 1.464 | −39.2% | −9.3% |
| select | 300 | 0.797 | 2.568 | −31.8% | −11.0% |

Full tables (all horizons 50–300 ms, median/p90, tune set, jukephase,
select4, select_blend, snap metric) are in `results/tuned_summary.csv` and
`results/snap_metric.csv` (`tune.py --report`).

Headline: **−41% mean error vs CV at 200 ms overall** (−48% at 100 ms), with
p99 also improved everywhere. `select` (hard model selection over
{cv, ballistic_hazard}) costs a hair of mean vs raw ballistic_hazard but is
the safety mechanism that guarantees the mode can never *stay* worse than CV;
hard selection validated better than residual-weighted blending on mean.

## 3. Tuned constants (as ported to `PredictionParams`)

| Python (`tune.py`) | value | C# (`PredictionParams`) | ported value | note |
|---|---|---|---|---|
| `tau_v` | 0.001 | `VelSmoothTau` | **0.005** | safety margin, see §4 |
| `tau_a` | 0.005 | `AccSmoothTau` | **0.01** | safety margin, see §4 |
| `t_commit` | 3.5 | `TCommit` | 3.5 | response flat beyond 2.5 s — effectively "hold thrust"; exp-decay path kept |
| `t_max` | 60.0 | `ThrustMax` | 60 | thrust magnitude clamp (m/s²) |
| `dead` | 30.0 | `ThrustDeadzone` | 30 | hysteresis band, velocity-orthogonal frame |
| `tau_flip` | 5.0 | `FlipRateTau` | 5 | |
| (1/`tau_flip`) | 0.2 | `FlipRateInc` | 0.2 | increment per committed flip |
| `tau_dir` | 0.8 | `DirSmoothTau` | 0.8 | direction smoothing for the lateral frame |
| `c_damp` | 0.15 | `HazardDamping` | 0.15 | was 0.5 in the placeholder |
| `floor` | **0.0** | — | *removed* | opposing-thrust hazard floor never paid off |
| `commit_mult` | 0.1 | `CommittedHazardMult` | 0.1 | replaces the old hazard=0 short-circuit |
| `commit_dot` | 0.7 | `CommittedDotFrac` | 0.7 | |
| `t_recent` | 0.2 | `CommittedMinFlipAge` | 0.2 | was 0.3 |
| `c_tc` | **0.0** | — | *removed* | commit-window shrink by hazard tuned to 0 |
| `tau_sel` | 2.0 | `ResidualTau` | 2.0 | time-based residual EWMA |
| `default` | 1 (ballistic) | `activeModel` initial | Ballistic | until residuals mature |
| JUKE dict | — | `JukeEnabled` | **false** | see §5 |

## 4. Porting decisions (Python → C#)

- **EWMA form.** All EWMAs use `alpha = 1 − exp(−dt/tau)` (Python
  `ewma_time`), NOT `dt/(dt+tau)`. The tuned taus only transfer in this form.
- **Smoothing safety margin.** The offline optimum was `tau_v=0.001` /
  `tau_a=0.005` — effectively raw samples, because the recorded corpus is a
  clean server-side stream. Live network streams are jitterier (reordering,
  coalesced messages, half-float quantization arriving at uneven local
  times), so the port ships `0.005` / `0.01`. Measured cost offline: ~2% of
  the win.
- **Lateral frame: LOS → velocity-orthogonal.** The flip/juke statistics use
  a velocity-orthogonal frame built from a direction-smoothed velocity
  (`tau_dir = 0.8 s`, world-up cross with a world-X fallback when travel is
  near-vertical, threshold `|u×up| ≤ 0.2`). The original C# draft used the
  local-player LOS frame; the tuned `dead = 30` was calibrated in the
  velocity-orthogonal frame, so the C# was switched to match. Side benefit:
  no dependence on the local player's position.
- **Hysteresis flip semantics.** A lateral-thrust sign is only *committed*
  when |component| exceeds the deadzone band; inside the band the last
  committed sign is held; a flip counts only on a committed sign change
  (Python `_hyst_sign` + `_sign_flips`). Both axes flipping in one sample
  count twice (`f1 + f2`).
- **Committed condition.** `dot(thrust, velRaw) > 0.7·|thrust|·|velRaw|` and
  no committed flip within 0.2 s. It *multiplies* the hazard by 0.1 instead
  of zeroing it. The old magnitude preconditions (`OpposingVelMin`,
  `OpposingThrustMin`) were not part of the tuned Python condition and were
  dropped with the opposing-floor mechanism.
- **Horizon usage.** Hazard `1 − exp(−rate·h)` and the thrust-commit decay
  `exp(−h/TCommit)` both use the RAW horizon `h`; only the drag kinematic
  step runs at the damped `h_eff = h·(1 − c_damp·hazard)`. CV predicts at the
  raw horizon (as in the benchmark's `select`), and predictions are filed in
  the ledger against `t_now + h` (raw).
- **Raw vs smoothed velocity.** The drag step integrates the *raw* snapshot
  velocity; only the thrust estimate (`acc + k·vel`, clamped to 60 m/s²) is
  built from the smoothed velocity — exactly `Ballistic._base_state` /
  `predict`.
- **Residual scoring.** Time-based EWMA: per scored sample
  `alpha = 1 − exp(−dtSinceLastScore/tau_sel)`, seeded on its first sample
  (matches `ewma_time` with `y0 = x0`). This makes the tuned `tau_sel = 2 s`
  independent of the C# ledger's 50 ms filing throttle.
- **Kept C#-only safety nets** (not in the Python model, deliberately
  retained): the synthetic-tick horizon freeze, `MinModelSamples` before a
  non-CV model is eligible, the model-switch hysteresis margin, the residual
  prior, projective smoothing, and the wall-slide clamp.
- `JukeEnabled` is `static readonly` rather than `const` only because mcs
  emits unreachable-code warnings (CS0429) for constant-folded gates; the
  Mono JIT folds a `static readonly bool` identically.

## 5. Juke model verdict

Disabled by default (`PredictionParams.JukeEnabled = false`). Offline the
sinusoidal juke model **never fired**: 0% of frames passed its confidence
gates, and the few `select` picks it got were residual noise (jukephase rows
in `results/tuned_summary.csv` are identical to ballistic_hazard to within
0.01%). Real jukes in the corpus are irregular enough that the
regular-oscillation detector never commits. May be revisited with in-game
LOS-frame data (a juke that matters is lateral *relative to the shooter*,
which a replay has no shooter to define).

## 6. Synthetic-tick finding

The server extrapolates starving clients with bit-identical velocity
("synthetic" ticks). Corpus scan (runs of ≥ 3 bit-identical vel samples while
`|v|² > 0.25`): **~0.5% of present ship-frames**, longest run **~3 s**. Rare
enough that no predictor needed special offline handling, but the C# keeps
its live freeze (`SyntheticHorizonCap`) since a 3 s fabricated run would
otherwise teach the tracker garbage and let the ballistic model extrapolate a
fiction.

## 7. Known open items

- **Tail guard (p99).** A 4-model `select4` with a second, tail-tuned hazard
  variant (`HAZARD_TAIL` in `tune.py`, tuned on 0.5·mean@200 + 0.5·p99@300)
  improves p99 by another ~1–3 points on validate (e.g. focus p99@300
  −13.0% vs −11.0%) at negligible mean cost. Not ported yet — the C# ships
  the simpler 2-model select.
- **Juke revisit** with in-game LOS-frame data (§5).
- **In-game validation** (the remaining gate): select SMART in the MP
  options, then join via the JOIN LAN/INTERNET MATCH menu, and use
  `lagcomp_stats` in the console — it prints per-player active model,
  flip rate, hazard, raw/effective horizon and per-model residuals; the
  BALLISTIC residual should sit below CV on moving targets, and the render
  should show no visible snap-back on jukers at 100–300 ms ping.
