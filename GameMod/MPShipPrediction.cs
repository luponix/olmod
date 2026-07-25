using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace GameMod {

    // ==========================================================================
    // MPShipPrediction -- the "SMART" client-side ship prediction mode
    // (Menus.mms_lag_compensation_prediction_mode == 3).
    //
    // The stock extrapolation (MPClientShipReckoning.extrapolatePlayer) is pure
    // constant velocity: pos + vel * t. Its failure mode is overshoot-then-
    // snapback on players who juke (reverse lateral thrust), worst at
    // 100-300 ms ping. This mode replaces the position prediction with:
    //
    //   1. A per-ship tracker fed from the raw 60 Hz server snapshots that
    //      derives smoothed velocity/acceleration and the reconstructed control
    //      input (thrustEst = acc + k*vel with k = 2.5 rigidbody drag), plus
    //      lateral-thrust flip statistics in a velocity-orthogonal frame built
    //      from a direction-smoothed velocity.
    //   2. Position predictors: CV (identical math to today's behavior) and
    //      Ballistic (closed-form drag integration with a commit-decayed,
    //      magnitude-clamped thrust term). A Juke-phase predictor (sinusoidal
    //      lateral velocity) exists but is disabled by default -- see
    //      PredictionParams.JukeEnabled.
    //   3. A reversal-hazard adaptive horizon: the ballistic extrapolation time
    //      shrinks when a lateral reversal is likely (measured flip rate,
    //      strongly discounted while the target is visibly committed to its
    //      current thrust) and is frozen on server-fabricated "synthetic" ticks.
    //   4. Online model selection: each model's prediction is periodically filed
    //      into a ledger and later scored against the actual (interpolated)
    //      server positions; rendering uses the lowest-residual model. CV is
    //      always a candidate, so the mode can never *stay* worse than today's
    //      behavior.
    //   5. Projective smoothing: prediction-basis changes (new snapshot, model
    //      switch) are absorbed into a decaying render offset instead of a snap.
    //   6. A wall-slide collision clamp: the existing dive-in raycast semantics,
    //      but the clipped remainder of the motion slides along the hit plane
    //      (with a second corner-guard raycast) instead of stopping dead.
    //
    // The PredictionParams defaults are BENCHMARK-TUNED: they come from the
    // offline replay benchmark in tools/lagcomp_bench/ (13-replay corpus,
    // tune/validate split, coordinate-descent search; validated -41% mean
    // position error vs CV at 200 ms). tools/lagcomp_bench/predictors.py is
    // the semantic reference for the tracker/predictor math in this file, and
    // tools/lagcomp_bench/DESIGN.md documents the methodology, the validated
    // results and every porting decision.
    //
    // No code path here runs unless prediction mode 3 is selected; modes 0-2 use
    // the untouched legacy code in MPClientExtrapolation.cs.
    // ==========================================================================

    // ----------------------------------------------------------------------
    // All tunable constants live here. Values marked "tuned" were produced by
    // the offline benchmark (tools/lagcomp_bench/, 13-replay corpus, 2026-07);
    // see tools/lagcomp_bench/DESIGN.md.
    // ----------------------------------------------------------------------
    public static class PredictionParams {
        // ---- engine facts ----
        public const float Kdrag = 2.5f;              // 1/s; PlayerShip rigidbody linear drag (verified engine value)
        public const float ShipRadius = 0.98f;        // m; collision-clamp sphere (ship collider is ~1, kept identical to legacy clamp)

        // ---- tracker smoothing (all EWMAs use alpha = 1 - exp(-dt/tau), the
        //      exact form the benchmark tuned; do not swap in dt/(dt+tau)) ----
        public const float VelSmoothTau = 0.005f;     // s; velocity EWMA tau. Tuned on 13-replay corpus, 2026-07: offline optimum was 0.001 (effectively raw), but live network streams are jitterier than clean recordings; this safety-margin value costs ~2% of the win.
        public const float AccSmoothTau = 0.01f;      // s; acceleration EWMA tau. Tuned on 13-replay corpus, 2026-07: offline optimum 0.005; widened for live-stream jitter (see VelSmoothTau note).
        public const float DirSmoothTau = 0.8f;       // s; direction-smoothing tau for the velocity-orthogonal lateral frame. Tuned on 13-replay corpus, 2026-07.
        public const float TrackResetJump = 20f;      // m; consecutive position jump larger than this = respawn/teleport -> reset track
        public const float TrackResetGap = 1.0f;      // s; server-time gap between snapshots larger than this -> reset track
        public const float TrackPruneAge = 5f;        // s; local age after which a silent track is dropped entirely
        public const int MinSnapsForSmart = 5;        // snapshots needed before the smart pipeline runs (else CV fallback)

        // ---- flip / reversal-hazard statistics ----
        public const float ThrustDeadzone = 30f;      // m/s^2; hysteresis band: a lateral-thrust sign only commits when |component| exceeds this. Tuned on 13-replay corpus, 2026-07.
        public const float FlipRateTau = 5f;          // s; decay time of the Poisson flip-rate estimate. Tuned on 13-replay corpus, 2026-07.
        public const float FlipRateInc = 0.2f;        // 1/s; rate increment per committed flip (= 1/FlipRateTau). Tuned on 13-replay corpus, 2026-07.
        public const float HazardDamping = 0.15f;     // unitless 0..1; horizon reduction at hazard = 1 (t_eff = t*(1-damping*hazard)). Tuned on 13-replay corpus, 2026-07 (was 0.5).
        // NOTE: the opposing-thrust hazard floor that used to live here was
        // tuned to 0 on the 13-replay corpus (the mechanism never paid off)
        // and has been removed entirely.
        public const float CommittedDotFrac = 0.7f;   // unitless; dot(thrust,vel) > frac*|v||thrust| => committed. Tuned on 13-replay corpus, 2026-07.
        public const float CommittedHazardMult = 0.1f;// unitless; hazard multiplier while committed (replaces the old hazard=0 short-circuit). Tuned on 13-replay corpus, 2026-07.
        public const float CommittedMinFlipAge = 0.2f;// s; no committed flip within this window required for "committed". Tuned on 13-replay corpus, 2026-07 (was 0.3).

        // ---- synthetic-tick handling ----
        public const int SyntheticRunLength = 3;      // consecutive bit-identical vel samples (while moving) => synthetic
        public const float SyntheticMinSpeedSq = 0.25f;// (m/s)^2; a resting ship legitimately repeats v=0, don't flag it
        public const float SyntheticHorizonCap = 0.034f;// s; t_eff clamp while the server fabricates ticks (~2 physics ticks)

        // ---- ballistic predictor ----
        public const float TCommit = 3.5f;            // s; thrust commit window. Tuned on 13-replay corpus, 2026-07: response was flat beyond 2.5 s -- effectively "hold thrust"; the exp-decay code path is kept.
        public const float ThrustMax = 60f;           // m/s^2; |thrustEst| magnitude clamp. Tuned on 13-replay corpus, 2026-07.

        // ---- juke-phase predictor ----
        // Benchmark verdict (13-replay corpus, 2026-07): the juke model never
        // fired -- 0% confident frames; its select picks were residual noise.
        // Disabled by default; may be revisited with in-game LOS-frame data.
        // (static readonly, not const, so mcs doesn't emit unreachable-code
        // warnings for the gated expressions; the JIT folds it just the same)
        public static readonly bool JukeEnabled = false;
        public const float JukeCrossMinVel = 0.5f;    // m/s; |lateral vel| needed to count a zero crossing (noise gate)
        public const float JukeEwmaAlpha = 0.3f;      // unitless; EWMA gain for period/amplitude updates
        public const int JukeMinCrossings = 4;        // zero crossings observed before the juke model becomes a candidate
        public const float JukePeriodMin = 0.25f;     // s; plausible juke period lower bound
        public const float JukePeriodMax = 2.5f;      // s; plausible juke period upper bound
        public const float JukePeriodStability = 0.35f;// unitless; max EWMA period jitter as a fraction of the period
        public const float JukeMinAmp = 1.5f;         // m/s; minimum lateral velocity amplitude worth modelling
        public const float JukeMaxCrossAge = 1.5f;    // periods; oscillation considered stopped if no crossing for this many periods

        // ---- model-selection ledger ----
        public const float LedgerInterval = 0.05f;    // s; throttle between prediction filings per track
        public const float ResidualTau = 2.0f;        // s; time-based residual EWMA tau (alpha per scored sample = 1-exp(-dtSinceLastScore/tau)). Tuned on 13-replay corpus, 2026-07 (tau_sel).
        public const float ResidualPrior = 0.5f;      // m; residual prior before any samples are scored
        public const int MinModelSamples = 8;         // scored samples before a non-CV model may be selected
        public const float ModelSwitchMargin = 0.9f;  // unitless; challenger residual must be < margin * active residual to switch

        // ---- projective smoothing ----
        public const float SmoothTau = 0.08f;         // s; exponential decay time of the render-offset correction
        public const float SmoothSnapDist = 3f;       // m; offsets larger than this hard-snap (respawn/teleport scale)
        public const float SmoothMaxFrameDt = 0.1f;   // s; render-frame dt clamp for the decay (hitch protection)

        // ---- wall-slide clamp ----
        public const float SlideMinLen = 0.01f;       // m; slide motions shorter than this are dropped
    }

    // Prediction models scored by the ledger.
    public static class ShipPredModel {
        public const int CV = 0;         // constant velocity: pos + vel*t (identical math to legacy mode, always a candidate)
        public const int Ballistic = 1;  // drag-decayed velocity + commit-decayed thrust
        public const int Juke = 2;       // sinusoidal lateral velocity + drag-decayed longitudinal
        public const int Count = 3;

        public static string Name(int m) {
            switch (m) {
                case CV: return "CV";
                case Ballistic: return "BALLISTIC";
                case Juke: return "JUKE";
                default: return "?";
            }
        }
    }

    // One raw server state of one ship.
    public struct ShipSnap {
        public float tServer;   // server clock (m_server_timestamp based, or accumulated fixed ticks)
        public float tLocal;    // our Time.time when it arrived
        public Vector3 pos;
        public Vector3 vel;     // server rigidbody velocity (zero on VANILLA servers)
        public Vector3 vrot;    // angular velocity (rad/s)
        public Quaternion rot;
    }

    // ----------------------------------------------------------------------
    // ShipTrack -- one remote ship's snapshot ring + derived motion features
    // + prediction ledger + render-smoothing state, keyed by net id.
    // ----------------------------------------------------------------------
    public class ShipTrack {
        public const int RingSize = 32;               // ~0.53s at 60Hz; enough to score every live ledger entry

        public uint netId;
        public bool velFromDiff;                      // VANILLA server: no velocity in snapshots, diff positions

        // ring of raw snapshots (written on capture, consumed on the render tick)
        public readonly ShipSnap[] ring = new ShipSnap[RingSize];
        public int head = -1;                         // index of newest valid entry
        public int count;
        public int consumed;                          // how many of `count` have been feature-processed

        // --- derived features (valid once count >= 2) ---
        public Vector3 pos;                           // newest raw server position
        public Vector3 velRaw;                        // newest raw (unsmoothed) server velocity; the drag step + hazard tests use this, matching the benchmark
        public Vector3 vel;                           // server velocity, EWMA-smoothed (VelSmoothTau)
        public Vector3 acc;
        public Vector3 thrustEst;                     // acc + Kdrag*vel, clamped to ThrustMax = reconstructed control input
        public Vector3 dirSmooth;                     // direction-smoothed velocity (DirSmoothTau); basis of the lateral frame
        public float tServerNewest;
        public float tLocalNewest;

        // flip / juke statistics (lateral frame = velocity-orthogonal, built from dirSmooth)
        public float flipRate;                        // lateral-thrust sign flips per second (decaying)
        public float lastFlipT = -100f;               // server time of last flip
        public float jukePeriod;                      // EWMA of zero-cross interval * 2 (s)
        public float jukeAmp;                         // EWMA of peak |lateral vel| between crossings (m/s)
        public float jukePeriodJitter;                // EWMA of |measured period - jukePeriod| (s), stability gate
        public int jukeCrossings;                     // zero crossings observed since last reset
        public float lastCrossT = -100f;              // server time of the last zero crossing (phase anchor)
        public float latVelSign;                      // sign of the lateral velocity since the last crossing
        public Vector3 lat1 = Vector3.right;          // lateral frame used for the juke stats (updated per consume)
        public Vector3 lat2 = Vector3.up;

        public bool synthetic;                        // server is fabricating linear ticks for this player
        public int identicalVelRun;

        // per-model time-based EWMA |predicted - actual| residuals (m)
        public readonly float[] residual = new float[ShipPredModel.Count];
        public readonly int[] residualN = new int[ShipPredModel.Count];
        public readonly float[] residualLastScoreT = new float[ShipPredModel.Count]; // server time of the last scored sample (drives the time-based alpha)
        public int activeModel = ShipPredModel.Ballistic;

        // prediction ledger (filed on render, scored as snapshots pass the impact time)
        public struct LedgerEntry {
            public bool live;
            public byte model;
            public float tImpactServer;
            public Vector3 predicted;
        }
        public readonly LedgerEntry[] ledger = new LedgerEntry[32];
        public int ledgerHead;
        public float lastLedgerT = -100f;             // local time of the last filing (throttle)

        // projective-smoothing state (main thread only)
        public Vector3 smoothOffset;
        public bool smoothValid;
        public Vector3 lastRendered;
        public float lastBasisT = -1f;                // snapshot server time the last render was based on
        public int lastBasisModel = -1;               // model the last render used

        // diagnostics
        public float lastT;                           // last requested horizon (s)
        public float lastTEff;                        // last effective horizon (s)
        public float lastHazard;                      // last hazard value

        // internal flip/juke detection state
        private float m_latSign1, m_latSign2;
        private float m_peakLat;

        public ShipTrack(uint id) {
            netId = id;
            Reset();
        }

        public void Reset() {
            head = -1; count = 0; consumed = 0;
            pos = Vector3.zero;
            velRaw = vel = acc = thrustEst = dirSmooth = Vector3.zero;
            flipRate = 0f; lastFlipT = -100f;
            jukePeriod = 0f; jukeAmp = 0f; jukePeriodJitter = 0f;
            jukeCrossings = 0; lastCrossT = -100f; latVelSign = 0f;
            synthetic = false; identicalVelRun = 0;
            m_latSign1 = m_latSign2 = 0f; m_peakLat = 0f;
            for (int i = 0; i < ledger.Length; i++) {
                ledger[i].live = false;
            }
            for (int m = 0; m < ShipPredModel.Count; m++) {
                residual[m] = PredictionParams.ResidualPrior;
                residualN[m] = 0;
                residualLastScoreT[m] = -1e9f;    // first score gets alpha ~ 1 (EWMA seeds on its first sample)
            }
            // Ballistic by default until residuals mature (benchmark: select default=1)
            activeModel = ShipPredModel.Ballistic;
            smoothValid = false;
            smoothOffset = Vector3.zero;
            lastBasisT = -1f;
            lastBasisModel = -1;
        }

        // ---- ring append & access (called under the registry lock) ----
        public void Append(ref ShipSnap s) {
            if (count > 0) {
                ShipSnap newest = ring[head];
                // respawn/teleport: a jump this large is not flight
                if ((s.pos - newest.pos).sqrMagnitude >
                    PredictionParams.TrackResetJump * PredictionParams.TrackResetJump) {
                    Reset();
                }
                // long silence: the motion history is stale
                else if (s.tServer - newest.tServer > PredictionParams.TrackResetGap) {
                    Reset();
                }
            }

            head = (head + 1) % RingSize;
            ring[head] = s;
            if (count < RingSize) {
                count++;
            } else if (consumed > 0) {
                consumed--;    // oldest un-consumed entry fell off
            }
        }

        // i steps back from newest (0 = newest); caller guards i < count
        public ShipSnap Back(int i) {
            int idx = head - i;
            if (idx < 0) {
                idx += RingSize;
            }
            return ring[idx];
        }

        // Interpolated raw position at server time t (clamped to the ring span).
        public Vector3 PosAtServerTime(float t) {
            if (count == 0) {
                return pos;
            }
            if (t >= ring[head].tServer) {
                return ring[head].pos;
            }
            for (int i = 1; i < count; i++) {
                ShipSnap a = Back(i);
                if (t >= a.tServer) {
                    ShipSnap b = Back(i - 1);
                    float span = b.tServer - a.tServer;
                    float f = span > 1e-5f ? (t - a.tServer) / span : 1f;
                    return Vector3.Lerp(a.pos, b.pos, f);
                }
            }
            return Back(count - 1).pos;
        }

        // ---- per-snapshot feature derivation (main thread, under the registry lock) ----
        // backIdx counts back from newest; called oldest-first for each fresh entry.
        public void ConsumeOne(int backIdx) {
            ShipSnap s = Back(backIdx);
            pos = s.pos;
            tServerNewest = s.tServer;
            tLocalNewest = s.tLocal;

            if (backIdx >= count - 1) {
                // first sample of a (re)fresh ring: seed (EWMAs seed on x0)
                velRaw = vel = dirSmooth = s.vel;
                return;
            }

            ShipSnap prev = Back(backIdx + 1);
            float dt = s.tServer - prev.tServer;
            if (dt <= 1e-5f) {
                return;
            }

            // --- velocity ---
            Vector3 rawVel;
            if (velFromDiff || (s.vel == Vector3.zero && prev.vel == Vector3.zero)) {
                rawVel = (s.pos - prev.pos) / dt;     // VANILLA fallback: diff raw positions
            } else {
                rawVel = s.vel;
            }

            // synthetic-tick detection: the server extrapolates a starving client
            // with bit-identical velocity for up to ~200ms; don't learn juke
            // behavior from fabricated data. A resting ship repeats v=0
            // legitimately, so only a MOVING identical run counts.
            if (!velFromDiff) {
                if (s.vel == prev.vel && s.vel.sqrMagnitude > PredictionParams.SyntheticMinSpeedSq) {
                    identicalVelRun++;
                } else {
                    identicalVelRun = 0;
                }
                synthetic = identicalVelRun >= PredictionParams.SyntheticRunLength;
            }

            velRaw = rawVel;

            // EWMA form alpha = 1 - exp(-dt/tau): the exact update the benchmark
            // tuned its taus in (NOT dt/(dt+tau) -- the taus don't transfer).
            float velAlpha = 1f - Mathf.Exp(-dt / PredictionParams.VelSmoothTau);
            Vector3 newVel = Vector3.Lerp(vel, rawVel, velAlpha);
            Vector3 rawAcc = (newVel - vel) / dt;
            float accAlpha = 1f - Mathf.Exp(-dt / PredictionParams.AccSmoothTau);
            acc = Vector3.Lerp(acc, rawAcc, accAlpha);
            vel = newVel;
            thrustEst = acc + PredictionParams.Kdrag * vel;
            float thrustMag = thrustEst.magnitude;
            if (thrustMag > PredictionParams.ThrustMax) {
                thrustEst *= PredictionParams.ThrustMax / thrustMag;
            }

            // --- lateral frame: velocity-orthogonal, from the direction-smoothed
            //     velocity (benchmark semantics; the flip deadzone was calibrated
            //     in this frame, and it needs no local-player state) ---
            float dirAlpha = 1f - Mathf.Exp(-dt / PredictionParams.DirSmoothTau);
            dirSmooth = Vector3.Lerp(dirSmooth, rawVel, dirAlpha);
            if (dirSmooth.sqrMagnitude > 1e-8f) {
                Vector3 u = dirSmooth.normalized;
                Vector3 l1 = Vector3.Cross(u, Vector3.up);
                if (l1.magnitude <= 0.2f) {
                    // travel nearly vertical: world-X fallback (same 0.2 threshold
                    // on |u x up| as the benchmark's frame construction)
                    l1 = Vector3.Cross(u, Vector3.right);
                }
                if (l1.sqrMagnitude > 1e-8f) {
                    lat1 = l1.normalized;
                    lat2 = Vector3.Cross(u, lat1);    // unit: u and lat1 are orthonormal
                }
            }
            // else: keep the previous frame (better than a degenerate one)

            // --- flip / juke statistics (frozen while synthetic) ---
            flipRate *= Mathf.Exp(-dt / PredictionParams.FlipRateTau);
            if (!synthetic) {
                // committed-sign flips on either lateral axis = commanded reversals.
                // Hysteresis: a sign only commits when |component| exceeds the
                // deadzone band; a flip counts only on a committed sign change.
                // Both axes flipping in one sample counts twice (benchmark: f1+f2).
                float t1 = Vector3.Dot(thrustEst, lat1);
                float t2 = Vector3.Dot(thrustEst, lat2);
                int flips = 0;
                if (DetectFlip(ref m_latSign1, t1, PredictionParams.ThrustDeadzone)) {
                    flips++;
                }
                if (DetectFlip(ref m_latSign2, t2, PredictionParams.ThrustDeadzone)) {
                    flips++;
                }
                if (flips > 0) {
                    flipRate += flips * PredictionParams.FlipRateInc;
                    lastFlipT = s.tServer;
                }

                // zero crossings of lateral velocity -> juke period/amplitude/phase
                float vLat = Vector3.Dot(vel, lat1);
                float absLat = Mathf.Abs(vLat);
                if (absLat > m_peakLat) {
                    m_peakLat = absLat;
                }
                if (latVelSign != 0f && vLat * latVelSign < 0f && absLat > PredictionParams.JukeCrossMinVel) {
                    if (lastCrossT > -50f) {
                        float period = 2f * (s.tServer - lastCrossT);
                        if (jukePeriod <= 0f) {
                            jukePeriod = period;
                            jukePeriodJitter = 0f;
                        } else {
                            jukePeriodJitter = Mathf.Lerp(jukePeriodJitter,
                                Mathf.Abs(period - jukePeriod), PredictionParams.JukeEwmaAlpha);
                            jukePeriod = Mathf.Lerp(jukePeriod, period, PredictionParams.JukeEwmaAlpha);
                        }
                        jukeAmp = (jukeAmp <= 0f) ? m_peakLat
                                : Mathf.Lerp(jukeAmp, m_peakLat, PredictionParams.JukeEwmaAlpha);
                        jukeCrossings++;
                    }
                    lastCrossT = s.tServer;
                    m_peakLat = 0f;
                }
                if (absLat > PredictionParams.JukeCrossMinVel) {
                    latVelSign = Mathf.Sign(vLat);
                }
            }

            // --- score matured ledger entries against the ring ---
            for (int i = 0; i < ledger.Length; i++) {
                if (!ledger[i].live || ledger[i].tImpactServer > s.tServer) {
                    continue;
                }
                Vector3 actual = PosAtServerTime(ledger[i].tImpactServer);
                float err = (ledger[i].predicted - actual).magnitude;
                int m = ledger[i].model;
                // time-based EWMA (benchmark tau_sel): alpha depends on the server
                // time elapsed since this model's previous scored sample, so the
                // tuned tau transfers regardless of the filing/scoring cadence.
                float dtScore = Mathf.Max(ledger[i].tImpactServer - residualLastScoreT[m], 0f);
                float alpha = 1f - Mathf.Exp(-dtScore / PredictionParams.ResidualTau);
                residual[m] = Mathf.Lerp(residual[m], err, alpha);
                residualLastScoreT[m] = ledger[i].tImpactServer;
                residualN[m]++;
                ledger[i].live = false;
            }
        }

        private static bool DetectFlip(ref float lastSign, float value, float deadzone) {
            if (Mathf.Abs(value) < deadzone) {
                return false;
            }
            float sign = Mathf.Sign(value);
            bool flip = lastSign != 0f && sign != lastSign;
            lastSign = sign;
            return flip;
        }

        public void FilePrediction(int model, float horizon, Vector3 predicted) {
            ledgerHead = (ledgerHead + 1) % ledger.Length;
            ledger[ledgerHead].live = true;
            ledger[ledgerHead].model = (byte)model;
            ledger[ledgerHead].tImpactServer = tServerNewest + horizon;
            ledger[ledgerHead].predicted = predicted;
        }

        // Is the juke oscillation regular enough to model as a sinusoid?
        // Always false while PredictionParams.JukeEnabled is off (benchmark
        // verdict: the model never fired offline); selection then runs over
        // {CV, Ballistic} only.
        public bool JukeUsable() {
            return PredictionParams.JukeEnabled
                && !synthetic
                && jukeCrossings >= PredictionParams.JukeMinCrossings
                && jukePeriod >= PredictionParams.JukePeriodMin
                && jukePeriod <= PredictionParams.JukePeriodMax
                && jukeAmp >= PredictionParams.JukeMinAmp
                && jukePeriodJitter <= PredictionParams.JukePeriodStability * jukePeriod
                && (tServerNewest - lastCrossT) <= PredictionParams.JukeMaxCrossAge * jukePeriod;
        }
    }

    // ----------------------------------------------------------------------
    // MPShipPrediction -- track registry, the pure predictors, and the smart
    // extrapolation path called from MPClientShipReckoning.extrapolatePlayer.
    // ----------------------------------------------------------------------
    public static class MPShipPrediction {
        private static readonly object s_lock = new object();
        private static readonly Dictionary<uint, ShipTrack> s_tracks = new Dictionary<uint, ShipTrack>();
        private static readonly List<ShipTrack> s_trackList = new List<ShipTrack>(16);
        private static readonly List<uint> s_staleIds = new List<uint>();

        // accumulated server clock for messages without usable timestamps
        private static float s_serverT;
        private static bool s_haveTimestamp;
        private static int s_lastConsumeFrame = -1;

        public static bool SmartModeActive {
            get { return Menus.mms_lag_compensation_prediction_mode == 3; }
        }

        // ------------------------------------------------------------------
        // Capture: called from MPClientShipReckoning.AddNewPlayerSnapshot for
        // every incoming (real) snapshot message. May run off the main thread;
        // this only appends raw data under our own lock and stays cheap.
        // ------------------------------------------------------------------
        public static void CaptureSnapshot(NewPlayerSnapshotToClientMessage msg,
                                           MPNoPositionCompression.SnapshotVersion version) {
            if (!SmartModeActive) {
                return;
            }
            float tLocal = Time.time;
            lock (s_lock) {
                float tServer;
                bool stamped = version == MPNoPositionCompression.SnapshotVersion.VELOCITY_TIMESTAMP
                               && msg.m_server_timestamp > 0f;
                if (stamped) {
                    tServer = msg.m_server_timestamp;
                    if (s_haveTimestamp && tServer <= s_serverT) {
                        return;             // duplicate / reordered message: drop
                    }
                    s_haveTimestamp = true;
                } else {
                    // no usable timestamp: accumulate the nominal 60Hz tick
                    tServer = s_serverT + Time.fixedDeltaTime;
                }
                s_serverT = tServer;

                bool vanilla = version == MPNoPositionCompression.SnapshotVersion.VANILLA;
                for (int i = 0; i < msg.m_num_snapshots; i++) {
                    NewPlayerSnapshot ns = msg.m_snapshots[i];
                    uint id = ns.m_net_id.Value;
                    ShipTrack tr;
                    if (!s_tracks.TryGetValue(id, out tr)) {
                        tr = new ShipTrack(id);
                        tr.velFromDiff = vanilla;
                        s_tracks[id] = tr;
                        s_trackList.Add(tr);
                    }
                    ShipSnap s;
                    s.tServer = tServer;
                    s.tLocal = tLocal;
                    s.pos = ns.m_pos;
                    s.vel = ns.m_vel;
                    s.vrot = ns.m_vrot;
                    s.rot = ns.m_rot;
                    tr.Append(ref s);
                }
            }
        }

        public static void ClearAll() {
            lock (s_lock) {
                s_tracks.Clear();
                s_trackList.Clear();
                s_haveTimestamp = false;
                s_serverT = 0f;
            }
        }

        // ------------------------------------------------------------------
        // Consume: derive features from fresh snapshots + prune stale tracks.
        // Main thread, once per render frame.
        // ------------------------------------------------------------------
        private static void ConsumeAll() {
            float now = Time.time;

            lock (s_lock) {
                for (int t = 0; t < s_trackList.Count; t++) {
                    ShipTrack tr = s_trackList[t];
                    int fresh = tr.count - tr.consumed;
                    for (int i = fresh - 1; i >= 0; i--) {
                        tr.ConsumeOne(i);
                    }
                    tr.consumed = tr.count;
                }

                // prune tracks starved for a while (player left / match ended)
                s_staleIds.Clear();
                foreach (KeyValuePair<uint, ShipTrack> kv in s_tracks) {
                    if (kv.Value.count > 0 && now - kv.Value.tLocalNewest > PredictionParams.TrackPruneAge) {
                        s_staleIds.Add(kv.Key);
                    }
                }
                for (int i = 0; i < s_staleIds.Count; i++) {
                    s_trackList.Remove(s_tracks[s_staleIds[i]]);
                    s_tracks.Remove(s_staleIds[i]);
                }
            }
        }

        // ------------------------------------------------------------------
        // Pure predictors (drag kinematics helpers).
        // D1 = ∫e^(-kt)dt over [0,tau]: how much current velocity survives as
        // displacement. D2 = double integral: displacement per unit thrust.
        // ------------------------------------------------------------------
        public static float D1(float tau) {
            if (tau <= 0f) {
                return tau;    // negative horizons degrade to linear (only |tau| ~ a frame)
            }
            return (1f - Mathf.Exp(-PredictionParams.Kdrag * tau)) / PredictionParams.Kdrag;
        }

        public static float D2(float tau) {
            if (tau <= 0f) {
                return 0f;
            }
            return (tau - D1(tau)) / PredictionParams.Kdrag;
        }

        // CV: identical math to the legacy extrapolation (pos + vel*t).
        public static Vector3 PredictCV(Vector3 p0, Vector3 v0, float tau) {
            return p0 + v0 * tau;
        }

        // Ballistic: velocity decays under drag; the current thrust input is
        // trusted with weight `commit` = exp(-h/TCommit) (raw-horizon decay).
        public static Vector3 PredictBallistic(Vector3 p0, Vector3 v0, Vector3 thrust, float tau, float commit) {
            return p0 + v0 * D1(tau) + thrust * (Mathf.Clamp01(commit) * D2(tau));
        }

        // Juke-phase: the lat1 velocity component is modelled as a sinusoid
        //   vLat(t) = A * s * sin(2π (t - tCross) / P)
        // with measured amplitude A, period P, phase anchor tCross and current
        // sign s; integrated in closed form over the horizon. The remaining
        // (longitudinal + lat2) velocity decays ballistically without thrust
        // (the juke thrust is the lateral oscillation itself).
        public static Vector3 PredictJuke(ShipTrack tr, Vector3 p0, float tau) {
            Vector3 lat = tr.lat1;
            Vector3 vLong = tr.vel - lat * Vector3.Dot(tr.vel, lat);
            float period = tr.jukePeriod;
            float w = 2f * Mathf.PI / period;
            float phi0 = w * (tr.tServerNewest - tr.lastCrossT);
            float s = (tr.latVelSign != 0f) ? tr.latVelSign : 1f;
            // ∫ A s sin(phi0 + w u) du over [0,tau] = A s (cos(phi0) - cos(phi0 + w tau)) / w
            float latDisp = tr.jukeAmp * s * (Mathf.Cos(phi0) - Mathf.Cos(phi0 + w * tau)) / w;
            return p0 + vLong * D1(tau) + lat * latDisp;
        }

        // ------------------------------------------------------------------
        // The smart extrapolation path. Replaces the body of extrapolatePlayer
        // for prediction mode 3 (respawn handling already done by the caller).
        // ------------------------------------------------------------------
        public static void ExtrapolatePlayerSmart(Player player, NewPlayerSnapshot snapshot, float t) {
            // feature-update all tracks once per render frame
            if (Time.frameCount != s_lastConsumeFrame) {
                s_lastConsumeFrame = Time.frameCount;
                ConsumeAll();
            }

            Vector3 basePos = snapshot.m_pos;
            Vector3 rawVel = snapshot.m_vel;

            // ---- gather features + file predictions under the registry lock ----
            ShipTrack tr;
            int model = ShipPredModel.CV;
            float tEff = t;
            float hazard = 0f;
            Vector3 rawPos;
            bool smart;

            lock (s_lock) {
                s_tracks.TryGetValue(snapshot.m_net_id.Value, out tr);
                smart = tr != null && tr.count >= PredictionParams.MinSnapsForSmart && t > 0f;

                if (smart) {
                    // --- reversal hazard (benchmark semantics: no opposing-thrust
                    //     floor -- tuned to 0, mechanism removed; "committed"
                    //     multiplies the hazard by CommittedHazardMult instead of
                    //     zeroing it; raw velocity feeds the dot test) ---
                    float committedDot = Vector3.Dot(tr.thrustEst, tr.velRaw);
                    float tvMag = tr.velRaw.magnitude, thMag = tr.thrustEst.magnitude;
                    bool committed = committedDot > PredictionParams.CommittedDotFrac * tvMag * thMag
                                     && (tr.tServerNewest - tr.lastFlipT) > PredictionParams.CommittedMinFlipAge;

                    hazard = 1f - Mathf.Exp(-tr.flipRate * t);
                    if (committed) {
                        hazard *= PredictionParams.CommittedHazardMult;
                    }
                    hazard = Mathf.Clamp01(hazard);

                    // --- adaptive horizon (damps the ballistic kinematic step;
                    //     CV stays at the raw horizon, like the benchmark's
                    //     select over {cv, ballistic_hazard}) ---
                    tEff = t * (1f - PredictionParams.HazardDamping * hazard);
                    if (tr.synthetic && tEff > PredictionParams.SyntheticHorizonCap) {
                        // the server is fabricating linear ticks: freeze extrapolation growth
                        tEff = PredictionParams.SyntheticHorizonCap;
                    }

                    // --- model predictions ---
                    // benchmark semantics: the commit decay runs on the RAW horizon
                    // (T = thrust * exp(-h/t_commit)), the drag step on the damped
                    // one; the drag step integrates the raw snapshot velocity.
                    float commit = Mathf.Exp(-t / PredictionParams.TCommit);
                    Vector3 pCv = PredictCV(basePos, rawVel, t);
                    Vector3 pBal = PredictBallistic(basePos, tr.velRaw, tr.thrustEst, tEff, commit);
                    bool jukeOk = tr.JukeUsable();
                    Vector3 pJuke = jukeOk ? PredictJuke(tr, basePos, tEff) : pBal;

                    // --- ledger: file each model's prediction for later scoring,
                    //     at the raw horizon (that is when the render is "for") ---
                    float now = Time.time;
                    if (now - tr.lastLedgerT >= PredictionParams.LedgerInterval && t > 1e-4f) {
                        tr.lastLedgerT = now;
                        tr.FilePrediction(ShipPredModel.CV, t, pCv);
                        tr.FilePrediction(ShipPredModel.Ballistic, t, pBal);
                        if (jukeOk) {
                            tr.FilePrediction(ShipPredModel.Juke, t, pJuke);
                        }
                    }

                    // --- model selection: lowest EWMA residual, CV always eligible,
                    //     hysteresis so the active model isn't flapped every frame ---
                    int best = ShipPredModel.CV;
                    float bestRes = tr.residual[ShipPredModel.CV];
                    if (tr.residualN[ShipPredModel.Ballistic] >= PredictionParams.MinModelSamples
                        && tr.residual[ShipPredModel.Ballistic] < bestRes) {
                        best = ShipPredModel.Ballistic;
                        bestRes = tr.residual[ShipPredModel.Ballistic];
                    }
                    if (jukeOk
                        && tr.residualN[ShipPredModel.Juke] >= PredictionParams.MinModelSamples
                        && tr.residual[ShipPredModel.Juke] < bestRes) {
                        best = ShipPredModel.Juke;
                        bestRes = tr.residual[ShipPredModel.Juke];
                    }
                    // keep the active model unless the challenger is clearly better
                    // (or the active model became unusable)
                    int active = tr.activeModel;
                    bool activeUsable = active == ShipPredModel.CV
                        || (active == ShipPredModel.Ballistic)
                        || (active == ShipPredModel.Juke && jukeOk);
                    if (!activeUsable
                        || best == active
                        || bestRes < PredictionParams.ModelSwitchMargin * tr.residual[active]) {
                        tr.activeModel = best;
                    }
                    model = tr.activeModel;

                    switch (model) {
                        case ShipPredModel.Ballistic: rawPos = pBal; break;
                        case ShipPredModel.Juke: rawPos = pJuke; break;
                        default: rawPos = pCv; break;
                    }

                    tr.lastT = t;
                    tr.lastTEff = tEff;
                    tr.lastHazard = hazard;
                } else {
                    // insufficient tracker data (or extrapolation into the past):
                    // plain CV fallback, identical math to the legacy path
                    rawPos = PredictCV(basePos, rawVel, t);
                    if (tr != null) {
                        tr.lastT = t;
                        tr.lastTEff = t;
                        tr.lastHazard = 0f;
                    }
                }
            } // lock

            // ---- projective smoothing (main thread only; hides basis changes) ----
            Vector3 newPos = rawPos;
            if (tr != null) {
                float dtRender = Mathf.Clamp(Time.deltaTime, 0f, PredictionParams.SmoothMaxFrameDt);
                float basisT = smart ? tr.tServerNewest : -2f;
                if (tr.smoothValid) {
                    if (basisT != tr.lastBasisT || model != tr.lastBasisModel) {
                        // basis changed (new snapshot or model switch): absorb the
                        // jump so the rendered path stays continuous. The expected
                        // one-frame motion is added so the correction doesn't
                        // include genuine flight.
                        tr.smoothOffset = (tr.lastRendered + rawVel * dtRender) - rawPos;
                    }
                    tr.smoothOffset *= Mathf.Exp(-dtRender / PredictionParams.SmoothTau);
                    if (tr.smoothOffset.sqrMagnitude >
                        PredictionParams.SmoothSnapDist * PredictionParams.SmoothSnapDist) {
                        tr.smoothOffset = Vector3.zero;    // respawn/teleport scale: hard snap
                    }
                    newPos = rawPos + tr.smoothOffset;
                }
                tr.lastBasisT = basisT;
                tr.lastBasisModel = model;
            }

            // ---- wall clamp with slide (same maxDive semantics as the legacy clamp) ----
            if (Menus.mms_lag_compensation_collision_limit > 0) {
                newPos = ClampWithSlide(basePos, newPos);
            }

            // ---- rotation: same handling as the VEL+ROTATION mode, at the effective horizon ----
            float rotScale = RotScale();
            Quaternion newRot = Quaternion.Euler(snapshot.m_vrot * (Mathf.Rad2Deg * tEff * rotScale)) * snapshot.m_rot;

            if (tr != null) {
                tr.lastRendered = newPos;
                tr.smoothOffset = newPos - rawPos;
                tr.smoothValid = true;
            }

            player.c_player_ship.c_transform.localPosition = newPos;
            player.c_player_ship.c_transform.rotation = newRot;
            player.c_player_ship.c_mesh_collider_trans.localPosition = player.c_player_ship.c_transform.localPosition;
            player.c_player_ship.c_mesh_collider_trans.localRotation = player.c_player_ship.c_transform.localRotation;
        }

        // Rotation-strength mapping, identical to the legacy switch in
        // extrapolatePlayer (the arc scale is not used by the smart mode).
        private static float RotScale() {
            switch (Menus.mms_lag_compensation_rotation_strength) {
                default:
                case 0: return 1f;      // 100%
                case 1: return 0.75f;   // 75%
                case 2: return 0.5f;    // 50%
                case 3: return 0.25f;   // 25%
                case 4: return 0.1f;    // 10%
            }
        }

        // Dive-in clamp with wall slide: the extrapolated position is limited to
        // maxDive along the ray into the wall (legacy semantics), and the clipped
        // remainder of the motion is projected onto the hit plane. A second
        // raycast along the slide direction stops the slide before it tunnels
        // through a corner.
        private static Vector3 ClampWithSlide(Vector3 basePos, Vector3 newPos) {
            const float radius = PredictionParams.ShipRadius;
            float maxDive = (100.0f - (float)Menus.mms_lag_compensation_collision_limit) / 50.0f * radius;
            Vector3 deltaPos = newPos - basePos;
            float dist = deltaPos.magnitude;
            if (dist <= 0.05f || dist <= maxDive) {
                return newPos;    // only if the ship is moved by a significant amount
            }

            // LAVA + LEVEL only -- same reasoning as the legacy clamp (never
            // against other players)
            const int layerMask = (1 << (int)UnityObjectLayers.LEVEL) | (1 << (int)UnityObjectLayers.LAVA);
            RaycastHit hitInfo;
            Vector3 direction = (1.0f / dist) * deltaPos;
            float castDist = dist + radius;   // basic RayCast: extend by the ship's radius

            if (!Physics.Raycast(basePos, direction, out hitInfo, castDist, layerMask, QueryTriggerInteraction.Ignore)) {
                return newPos;
            }

            float diveIn = castDist - hitInfo.distance;
            if (diveIn <= maxDive) {
                return newPos;
            }

            // stop point with the legacy maxDive allowance along the motion ray
            Vector3 clamped = basePos + (hitInfo.distance - radius + maxDive) * direction;

            // slide: project the clipped remainder onto the hit plane
            Vector3 remaining = newPos - clamped;
            Vector3 n = hitInfo.normal;
            Vector3 slide = remaining - n * Vector3.Dot(remaining, n);
            float slideLen = slide.magnitude;
            if (slideLen <= PredictionParams.SlideMinLen) {
                return clamped;
            }

            // corner guard: don't slide through an adjoining wall
            Vector3 slideDir = slide / slideLen;
            RaycastHit hit2;
            if (Physics.Raycast(clamped, slideDir, out hit2, slideLen + radius, layerMask, QueryTriggerInteraction.Ignore)) {
                float allowed = hit2.distance - radius;
                if (allowed < 0f) {
                    allowed = 0f;
                }
                if (allowed < slideLen) {
                    slide = slideDir * allowed;
                }
            }
            return clamped + slide;
        }

        // ------------------------------------------------------------------
        // Diagnostics: lagcomp_stats console command.
        // ------------------------------------------------------------------
        public static void CmdLagCompStats() {
            uConsole.Log(string.Format("[LagComp] prediction mode: {0} ({1})",
                Menus.mms_lag_compensation_prediction_mode, Menus.GetMMSLagCompensationPredictionMode()));
            lock (s_lock) {
                if (s_trackList.Count == 0) {
                    uConsole.Log("[LagComp] no tracked ships (SMART mode inactive or no snapshots yet)");
                    return;
                }
                for (int i = 0; i < s_trackList.Count; i++) {
                    ShipTrack tr = s_trackList[i];
                    string name = "net:" + tr.netId;
                    Player pl = OL_Client.GetPlayerFromNetId(new NetworkInstanceId(tr.netId));
                    if (pl != null) {
                        name = pl.m_mp_name + (pl.isLocalPlayer ? " (self)" : "");
                    }
                    uConsole.Log(string.Format(
                        "  {0}: model={1} snaps={2} flips/s={3:F2} juke={4}{5} t={6:F0}ms t_eff={7:F0}ms hazard={8:F2}",
                        name, ShipPredModel.Name(tr.activeModel), tr.count, tr.flipRate,
                        PredictionParams.JukeEnabled
                            ? string.Format("P={0:F2}s A={1:F1}m/s cross={2}", tr.jukePeriod, tr.jukeAmp, tr.jukeCrossings)
                            : "disabled",
                        tr.synthetic ? " SYNTH" : "",
                        tr.lastT * 1000f, tr.lastTEff * 1000f, tr.lastHazard));
                    StringBuilder sb = new StringBuilder("    residuals:");
                    for (int m = 0; m < ShipPredModel.Count; m++) {
                        if (m == ShipPredModel.Juke && !PredictionParams.JukeEnabled) {
                            sb.Append(" JUKE=disabled");
                            continue;
                        }
                        sb.Append(string.Format(" {0}={1:F2}m/{2}", ShipPredModel.Name(m), tr.residual[m], tr.residualN[m]));
                    }
                    uConsole.Log(sb.ToString());
                }
            }
        }
    }

    // register the diagnostics command (same pattern as FramerateLimiter's maxqueue)
    [HarmonyPatch(typeof(GameManager), "Start")]
    class MPShipPrediction_GameManager_Start {
        static void Postfix() {
            uConsole.RegisterCommand("lagcomp_stats",
                "Prints per-player SMART lag-compensation prediction statistics",
                new uConsole.DebugCommand(MPShipPrediction.CmdLagCompStats));
        }
    }

    // clear the tracker when (re)connecting, alongside the reckoning reset
    [HarmonyPatch(typeof(Client), "Connect")]
    class MPShipPrediction_Client_Connect {
        static void Postfix() {
            if (Overload.NetworkManager.IsServer()) {
                return;
            }
            MPShipPrediction.ClearAll();
        }
    }
}
