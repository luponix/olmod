using UnityEngine;

namespace GameMod {

    // ==========================================================================
    // MPShipPredictionCore -- the engine-independent core of the SMART
    // client-side ship prediction mode (see MPShipPrediction.cs for the full
    // mode description and the engine glue: Harmony patches, snapshot capture,
    // projective smoothing, wall-slide clamp, rotation, diagnostics).
    //
    // Everything in this file reproduces a position prediction from a raw
    // snapshot stream and nothing else. It deliberately references only
    // UnityEngine value types (Vector3/Quaternion/Mathf) so the standalone
    // replay evaluator (tools/lagcomp_eval/) can compile it against a minimal
    // shim and run the REAL shipped math over recorded .olr replays.
    //
    // Do NOT add references to Harmony, Overload.*, Menus, uConsole, Player,
    // NewPlayerSnapshot*, UnityEngine.Time or Physics here -- that is what
    // keeps the file dual-compilable. Anything time-dependent is parameterized
    // on a caller-passed clock (the game passes Time.time, the evaluator
    // passes the replay clock).
    // ==========================================================================

    // ----------------------------------------------------------------------
    // All tunable constants live here. Values marked "tuned" were produced by
    // the offline benchmark (tools/lagcomp_bench/, 13-replay corpus, 2026-07);
    // see tools/lagcomp_bench/DESIGN.md.
    //
    // These are plain mutable statics (not const/readonly) so the standalone
    // evaluator can override individual values for parameter sweeps
    // (`--set Name=Value`). The game itself never writes them, so in-game
    // behavior is identical to the previous const values.
    // ----------------------------------------------------------------------
    public static class PredictionParams {
        // ---- engine facts ----
        public static float Kdrag = 2.5f;              // 1/s; PlayerShip rigidbody linear drag (verified engine value)
        public static float ShipRadius = 0.98f;        // m; collision-clamp sphere (ship collider is ~1, kept identical to legacy clamp)

        // ---- tracker smoothing (all EWMAs use alpha = 1 - exp(-dt/tau), the
        //      exact form the benchmark tuned; do not swap in dt/(dt+tau)) ----
        public static float VelSmoothTau = 0.005f;     // s; velocity EWMA tau. Tuned on 13-replay corpus, 2026-07: offline optimum was 0.001 (effectively raw), but live network streams are jitterier than clean recordings; this safety-margin value costs ~2% of the win.
        public static float AccSmoothTau = 0.01f;      // s; acceleration EWMA tau. Tuned on 13-replay corpus, 2026-07: offline optimum 0.005; widened for live-stream jitter (see VelSmoothTau note).
        public static float DirSmoothTau = 0.8f;       // s; direction-smoothing tau for the velocity-orthogonal lateral frame. Tuned on 13-replay corpus, 2026-07.
        public static float TrackResetJump = 20f;      // m; consecutive position jump larger than this = respawn/teleport -> reset track
        public static float TrackResetGap = 1.0f;      // s; server-time gap between snapshots larger than this -> reset track
        public static float TrackPruneAge = 5f;        // s; local age after which a silent track is dropped entirely
        public static int MinSnapsForSmart = 5;        // snapshots needed before the smart pipeline runs (else CV fallback)

        // ---- flip / reversal-hazard statistics ----
        public static float ThrustDeadzone = 30f;      // m/s^2; hysteresis band: a lateral-thrust sign only commits when |component| exceeds this. Tuned on 13-replay corpus, 2026-07.
        public static float FlipRateTau = 5f;          // s; decay time of the Poisson flip-rate estimate. Tuned on 13-replay corpus, 2026-07.
        public static float FlipRateInc = 0.2f;        // 1/s; rate increment per committed flip (= 1/FlipRateTau). Tuned on 13-replay corpus, 2026-07.
        public static float HazardDamping = 0.15f;     // unitless 0..1; horizon reduction at hazard = 1 (t_eff = t*(1-damping*hazard)). Tuned on 13-replay corpus, 2026-07 (was 0.5).
        // NOTE: the opposing-thrust hazard floor that used to live here was
        // tuned to 0 on the 13-replay corpus (the mechanism never paid off)
        // and has been removed entirely.
        public static float CommittedDotFrac = 0.7f;   // unitless; dot(thrust,vel) > frac*|v||thrust| => committed. Tuned on 13-replay corpus, 2026-07.
        public static float CommittedHazardMult = 0.1f;// unitless; hazard multiplier while committed (replaces the old hazard=0 short-circuit). Tuned on 13-replay corpus, 2026-07.
        public static float CommittedMinFlipAge = 0.2f;// s; no committed flip within this window required for "committed". Tuned on 13-replay corpus, 2026-07 (was 0.3).

        // ---- synthetic-tick handling ----
        public static int SyntheticRunLength = 3;      // consecutive bit-identical vel samples (while moving) => synthetic
        public static float SyntheticMinSpeedSq = 0.25f;// (m/s)^2; a resting ship legitimately repeats v=0, don't flag it
        public static float SyntheticHorizonCap = 0.034f;// s; t_eff clamp while the server fabricates ticks (~2 physics ticks)

        // ---- ballistic predictor ----
        public static float TCommit = 3.5f;            // s; thrust commit window. Tuned on 13-replay corpus, 2026-07: response was flat beyond 2.5 s -- effectively "hold thrust"; the exp-decay code path is kept.
        public static float ThrustMax = 60f;           // m/s^2; |thrustEst| magnitude clamp. Tuned on 13-replay corpus, 2026-07.

        // ---- juke-phase predictor ----
        // Benchmark verdict (13-replay corpus, 2026-07): the juke model never
        // fired -- 0% confident frames; its select picks were residual noise.
        // Disabled by default; may be revisited with in-game LOS-frame data.
        public static bool JukeEnabled = false;
        public static float JukeCrossMinVel = 0.5f;    // m/s; |lateral vel| needed to count a zero crossing (noise gate)
        public static float JukeEwmaAlpha = 0.3f;      // unitless; EWMA gain for period/amplitude updates
        public static int JukeMinCrossings = 4;        // zero crossings observed before the juke model becomes a candidate
        public static float JukePeriodMin = 0.25f;     // s; plausible juke period lower bound
        public static float JukePeriodMax = 2.5f;      // s; plausible juke period upper bound
        public static float JukePeriodStability = 0.35f;// unitless; max EWMA period jitter as a fraction of the period
        public static float JukeMinAmp = 1.5f;         // m/s; minimum lateral velocity amplitude worth modelling
        public static float JukeMaxCrossAge = 1.5f;    // periods; oscillation considered stopped if no crossing for this many periods

        // ---- model-selection ledger ----
        public static float LedgerInterval = 0.05f;    // s; throttle between prediction filings per track
        public static float ResidualTau = 2.0f;        // s; time-based residual EWMA tau (alpha per scored sample = 1-exp(-dtSinceLastScore/tau)). Tuned on 13-replay corpus, 2026-07 (tau_sel).
        public static float ResidualPrior = 0.5f;      // m; residual prior before any samples are scored
        public static int MinModelSamples = 8;         // scored samples before a non-CV model may be selected
        public static float ModelSwitchMargin = 0.9f;  // unitless; challenger residual must be < margin * active residual to switch

        // ---- projective smoothing (render-side; used by the glue only) ----
        public static float SmoothTau = 0.08f;         // s; exponential decay time of the render-offset correction
        public static float SmoothSnapDist = 3f;       // m; offsets larger than this hard-snap (respawn/teleport scale)
        public static float SmoothMaxFrameDt = 0.1f;   // s; render-frame dt clamp for the decay (hitch protection)

        // ---- wall-slide clamp (render-side; used by the glue only) ----
        public static float SlideMinLen = 0.01f;       // m; slide motions shorter than this are dropped
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
    // ShipPredictionCore -- the pure predictors and the full smart position
    // pipeline (hazard -> adaptive horizon -> model predictions -> ledger ->
    // model selection). Stateless except for what lives in the ShipTrack.
    // ----------------------------------------------------------------------
    public static class ShipPredictionCore {

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
        // Feature-update a track: derive features for every not-yet-consumed
        // ring entry, oldest first (the game runs this once per render frame
        // per track, the evaluator after every appended snapshot).
        // ------------------------------------------------------------------
        public static void ConsumeFresh(ShipTrack tr) {
            int fresh = tr.count - tr.consumed;
            for (int i = fresh - 1; i >= 0; i--) {
                tr.ConsumeOne(i);
            }
            tr.consumed = tr.count;
        }

        // Does the track have enough data for the smart pipeline at horizon t?
        // (Otherwise PredictSmart degrades to plain CV.)
        public static bool SmartUsable(ShipTrack tr, float t) {
            return tr != null && tr.count >= PredictionParams.MinSnapsForSmart && t > 0f;
        }

        // ------------------------------------------------------------------
        // The full smart position prediction for one ship at raw horizon t:
        // reversal hazard -> adaptive horizon -> model predictions -> ledger
        // filing -> online model selection. Mutates the track (ledger,
        // residuals, active model, diagnostics).
        //
        // nowLedger drives the ledger-filing throttle and is the ONLY wall
        // clock in the core: the game passes Time.time, the offline evaluator
        // passes the replay clock.
        //
        // tr may be null / immature: falls back to plain CV, identical math
        // to the legacy path. Callers needing thread safety hold their own
        // lock (the game calls this under the registry lock).
        // ------------------------------------------------------------------
        public static Vector3 PredictSmart(ShipTrack tr, Vector3 basePos, Vector3 rawVel,
                                           float t, float nowLedger,
                                           out int model, out float tEff, out float hazard) {
            model = ShipPredModel.CV;
            tEff = t;
            hazard = 0f;

            if (!SmartUsable(tr, t)) {
                // insufficient tracker data (or extrapolation into the past):
                // plain CV fallback, identical math to the legacy path
                if (tr != null) {
                    tr.lastT = t;
                    tr.lastTEff = t;
                    tr.lastHazard = 0f;
                }
                return PredictCV(basePos, rawVel, t);
            }

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
            if (nowLedger - tr.lastLedgerT >= PredictionParams.LedgerInterval && t > 1e-4f) {
                tr.lastLedgerT = nowLedger;
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

            Vector3 rawPos;
            switch (model) {
                case ShipPredModel.Ballistic: rawPos = pBal; break;
                case ShipPredModel.Juke: rawPos = pJuke; break;
                default: rawPos = pCv; break;
            }

            tr.lastT = t;
            tr.lastTEff = tEff;
            tr.lastHazard = hazard;
            return rawPos;
        }
    }
}
