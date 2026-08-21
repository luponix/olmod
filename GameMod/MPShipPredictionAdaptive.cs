using System.Collections.Generic;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod {

    // ==========================================================================
    // MPShipPredictionAdaptive -- the "ADAPTIVE" client-side ship prediction
    // mode (Menus.mms_lag_compensation_prediction_mode == 3).
    //
    // The stock extrapolation (modes 0-2) is constant velocity, optionally
    // with rotation; its failure mode is overshoot-then-snapback on players
    // who juke, worst at 100-300 ms ping. This mode replaces the position
    // prediction with three mechanisms (distilled from the retired
    // lup.test2/lup.test3 experiments by ablating every other part of them
    // on a 45-replay evaluation corpus -- each of these measurably carries
    // the win, everything else measured as dead weight):
    //
    //   1. DRAG PHYSICS. The ship is a drag-damped rigidbody, so the honest
    //      extrapolation is not pos + vel*t but the analytic drag solution
    //      with the reconstructed control input (thrust = acc + k*vel) held
    //      over the horizon. The single biggest term: zeroing the drag
    //      constant costs 3.7x the mean error.
    //   2. ONE ONLINE CHOICE. Drag physics is right while the pilot holds a
    //      course and wrong the moment they don't, so both it and plain
    //      constant velocity are scored online against what actually
    //      happened, and the cheaper-so-far one is used. A fixed 50/50 mix
    //      instead costs +43% mean error.
    //   3. CONTINUITY. The chosen point is carried along the model's own
    //      velocity between calls and corrected toward the new target at a
    //      fixed rate, with a ceiling on the correction speed. This turns a
    //      snapshot innovation into a short glide instead of a warp, and it
    //      is what makes mechanism 2 safe as a hard pick: a model switch
    //      arrives as a glide.
    //
    // Corpus results (45 replays, mean error m, vs the legacy VELOCITY mode):
    // 30 ms 0.018 -> 0.012, 100 ms 0.126 -> 0.073, 150 ms 0.256 -> 0.160.
    //
    // There is deliberately NO innovation snap guard (lup.test3 hard-snapped
    // innovations > 3 m): at 400 ms it fired on almost every innovation and
    // posted a displayed-step jitter p99 of 193 m/s against 27 without it,
    // buying only 7% mean error. Respawns/teleports are still handled: they
    // reset the track, which drops continuity instead of gliding across the
    // map.
    //
    // The math here is the twin of PredLean.cs in the standalone evaluator
    // repo (4_Personal_Git_Repositories/Ovl-Prediction-Runner/, which also
    // carries the full write-up in LEAN.md: algorithm, ablation tables,
    // results, parameter sweeps). Keep the two in sync when tuning -- the
    // tunables keep their `Lean` names for exactly that reason. This file
    // adds only the engine glue: message capture, the registry lock, the
    // wall-slide clamp and rotation, and the reconnect reset. No code path
    // here runs unless mode 3 is selected.
    // ==========================================================================
    public static class MPShipPredictionAdaptive {

        // The whole tuning surface (plain mutable statics so the offline
        // evaluator's twin can sweep them; the game never writes them). The
        // field names match the evaluator twin's --set names -- keep them.
        public static class LeanParams {
            public static float LeanDrag = 2.5f;         // 1/s; PlayerShip rigidbody linear drag (verified engine value)
            public static float LeanThrustMax = 60f;     // m/s^2; clamp on the reconstructed control input. Uncapped costs +18% jitter p99.
            public static float LeanAccTau = 0.01f;      // s; EWMA tau of the acceleration estimate. Inert on the clean corpus; kept as the one filter against live-stream jitter.
            public static float LeanResidTau = 2f;       // s; EWMA tau of the two model error averages (response is flat from 0.5 to 8)
            public static float LeanSmoothTau = 0.02f;   // s; continuity correction rate. THE error/jitter dial: 0.01 -> 0.071 mean / 16.8 jitter p99, 0.04 -> 0.083 / 15.2 (corpus @100 ms).
            public static float LeanMaxCorrVel = 12f;    // m/s; ceiling on the continuity correction speed -- bounds visible warp
        }

        // Not tunables: "is this still the same flight" guards and ring sizing.
        private const float ResetJump = 20f;      // m; a step this large is a respawn/teleport, not flight
        private const float ResetGap = 1f;        // s; longer silence than this makes the motion history stale
        private const float MaxSmoothDt = 0.1f;   // s; continuity dt clamp (hitch protection)
        private const float ResidualPrior = 0.5f; // m; equal prior for both models, so neither is favoured before evidence
        private const int PendingSize = 32;       // filings awaiting truth; one per snapshot, so ~0.5 s of horizon at 60 Hz

        // A prediction waiting for reality to catch up with it.
        private struct Pending {
            public bool live;
            public float impactT;      // server time this prediction was aimed at
            public Vector3 cv;
            public Vector3 drag;
        }

        private class LeanShip {
            public bool velFromDiff;              // VANILLA protocol: snapshots carry no velocity, diff positions

            // newest snapshot and the one before it (the bracket truth is read from)
            public bool seeded;
            public Vector3 pos, vel;
            public float t;
            public Vector3 prevPos;
            public float prevT;
            public Vector3 prevSnapVel;           // previous snapshot's RAW velocity (the vanilla fallback compares raw pairs, not the derived vel)

            // derived motion: smoothed acceleration and the control input it implies
            public Vector3 acc, thrust;

            // the online choice: one error average per model, both scored on the
            // same schedule, so one shared timestamp drives both EWMA rates
            public float resCv = ResidualPrior;
            public float resDrag = ResidualPrior;
            public float lastScoreT = -1e9f;      // first score gets alpha ~ 1 (the EWMAs seed on their first sample)
            public readonly Pending[] pending = new Pending[PendingSize];
            public int pendingHead;
            public float filedT = -1e9f;          // snapshot already filed, so repeat render frames do not refile

            // continuity state of the displayed point
            public bool contValid;
            public Vector3 contPos;
            public float contT;

            public void Reset() {
                seeded = false;
                acc = thrust = Vector3.zero;
                resCv = resDrag = ResidualPrior;
                lastScoreT = -1e9f;
                for (int i = 0; i < pending.Length; i++) {
                    pending[i].live = false;
                }
                filedT = -1e9f;
                contValid = false;    // never glide across a respawn
            }
        }

        private static readonly object s_lock = new object();
        private static readonly Dictionary<uint, LeanShip> s_ships = new Dictionary<uint, LeanShip>();

        // accumulated server clock for messages without usable timestamps
        private static float s_serverT;
        private static bool s_haveTimestamp;

        public static void ClearAll() {
            lock (s_lock) {
                s_ships.Clear();
                s_haveTimestamp = false;
                s_serverT = 0f;
            }
        }

        // ------------------------------------------------------------------
        // Drag kinematics. D1 = how much of the current velocity survives as
        // displacement over the horizon, D2 = displacement per unit thrust.
        // ------------------------------------------------------------------
        private static float D1(float tau) {
            float k = LeanParams.LeanDrag;
            if (tau <= 0f || k <= 0f) {
                return tau;    // no drag (or a horizon in the past) degrades to linear
            }
            return (1f - Mathf.Exp(-k * tau)) / k;
        }

        private static float D2(float tau) {
            float k = LeanParams.LeanDrag;
            if (tau <= 0f) {
                return 0f;
            }
            if (k <= 0f) {
                return 0.5f * tau * tau;
            }
            return (tau - D1(tau)) / k;
        }

        // ------------------------------------------------------------------
        // Capture: called from MPClientShipReckoning.AddNewPlayerSnapshot for
        // every incoming (real) snapshot message. May run off the main
        // thread; only appends under our own lock and stays cheap. No-op
        // unless mode 3 is selected.
        // ------------------------------------------------------------------
        public static void CaptureSnapshot(NewPlayerSnapshotToClientMessage msg,
                                           MPNoPositionCompression.SnapshotVersion version) {
            if (Menus.mms_lag_compensation_prediction_mode != 3) {
                return;
            }
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
                    LeanShip ship;
                    if (!s_ships.TryGetValue(id, out ship)) {
                        ship = new LeanShip();
                        ship.velFromDiff = vanilla;
                        s_ships[id] = ship;
                    }
                    Capture(ship, ns.m_pos, ns.m_vel, tServer);
                }
            }
        }

        // Fold one raw server snapshot into the tracker, then settle any
        // prediction whose impact time has now passed. Caller holds s_lock.
        private static void Capture(LeanShip ship, Vector3 pos, Vector3 vel, float tServer) {
            if (ship.seeded) {
                if ((pos - ship.pos).sqrMagnitude > ResetJump * ResetJump
                    || tServer - ship.t > ResetGap) {
                    ship.Reset();
                }
            }

            if (!ship.seeded) {
                ship.seeded = true;
                ship.pos = ship.prevPos = pos;
                ship.t = ship.prevT = tServer;
                ship.vel = ship.prevSnapVel = vel;
                return;
            }

            float dt = tServer - ship.t;
            if (dt <= 1e-5f) {
                return;    // duplicate or out-of-order snapshot
            }

            ship.prevPos = ship.pos;
            ship.prevT = ship.t;
            ship.pos = pos;
            ship.t = tServer;

            // VANILLA servers send no velocity: fall back to differencing
            // positions. The heuristic must compare the raw snapshot pair --
            // testing the derived velocity instead would alternate diff/zero,
            // because the diff itself makes it nonzero.
            Vector3 newVel = (ship.velFromDiff || (vel == Vector3.zero && ship.prevSnapVel == Vector3.zero))
                ? (ship.pos - ship.prevPos) / dt
                : vel;
            ship.prevSnapVel = vel;

            // EWMA form alpha = 1 - exp(-dt/tau), so the tau is a real time
            // constant regardless of snapshot cadence
            float accAlpha = 1f - Mathf.Exp(-dt / LeanParams.LeanAccTau);
            ship.acc = Vector3.Lerp(ship.acc, (newVel - ship.vel) / dt, accAlpha);
            ship.vel = newVel;

            // the control input the pilot must be holding to produce this
            // acceleration against drag
            ship.thrust = ship.acc + LeanParams.LeanDrag * ship.vel;
            float thrustMag = ship.thrust.magnitude;
            if (thrustMag > LeanParams.LeanThrustMax) {
                ship.thrust *= LeanParams.LeanThrustMax / thrustMag;
            }

            Score(ship);
        }

        // Every filing whose impact time the stream has now passed is scored
        // against the truth interpolated in the freshest snapshot bracket.
        private static void Score(LeanShip ship) {
            float span = ship.t - ship.prevT;
            for (int i = 0; i < ship.pending.Length; i++) {
                if (!ship.pending[i].live || ship.pending[i].impactT > ship.t) {
                    continue;
                }
                float impactT = ship.pending[i].impactT;
                float fraction = span > 1e-5f ? (impactT - ship.prevT) / span : 1f;
                Vector3 truth = Vector3.Lerp(ship.prevPos, ship.pos, fraction);

                // time-based EWMA: the alpha follows the server time since the
                // last scored sample, so LeanResidTau transfers across cadences
                float alpha = 1f - Mathf.Exp(-Mathf.Max(impactT - ship.lastScoreT, 0f) / LeanParams.LeanResidTau);
                ship.resCv = Mathf.Lerp(ship.resCv, (ship.pending[i].cv - truth).magnitude, alpha);
                ship.resDrag = Mathf.Lerp(ship.resDrag, (ship.pending[i].drag - truth).magnitude, alpha);
                ship.lastScoreT = impactT;
                ship.pending[i].live = false;
            }
        }

        // ------------------------------------------------------------------
        // Predict the displayed position `horizon` seconds ahead of the newest
        // snapshot. `now` is the render clock (Time.time); it drives the
        // continuity dt, so the smoothing is frame-rate independent. Caller
        // holds s_lock.
        // ------------------------------------------------------------------
        private static Vector3 Predict(LeanShip ship, Vector3 basePos, Vector3 rawVel, float horizon, float now) {
            if (!ship.seeded || horizon <= 0f) {
                ship.contValid = false;
                return basePos + rawVel * horizon;
            }

            float d1 = D1(horizon), d2 = D2(horizon);
            Vector3 pCv = basePos + rawVel * horizon;
            Vector3 pDrag = basePos + ship.vel * d1 + ship.thrust * d2;

            // the whole model selection: whichever has been cheaper. A hard
            // pick is safe because the continuity filter below turns the
            // switch into a glide.
            bool useDrag = ship.resDrag <= ship.resCv;
            Vector3 target = useDrag ? pDrag : pCv;

            // file this snapshot's pair once, however many render frames ask
            if (ship.filedT != ship.t) {
                ship.filedT = ship.t;
                ship.pendingHead = (ship.pendingHead + 1) % ship.pending.Length;
                ship.pending[ship.pendingHead].live = true;
                ship.pending[ship.pendingHead].impactT = ship.t + horizon;
                ship.pending[ship.pendingHead].cv = pCv;
                ship.pending[ship.pendingHead].drag = pDrag;
            }

            if (!ship.contValid) {
                ship.contValid = true;
                ship.contPos = target;
                ship.contT = now;
                return target;
            }

            // Carry the last displayed point along the chosen model's velocity
            // AT THE HORIZON -- the displayed point tracks a future position,
            // whose velocity is not the current one -- then correct toward the
            // new target. Without the carrier the filter lags under
            // acceleration (measured on lup.test3, whose carrier this mirrors:
            // disabling it costs +17% mean error).
            float dtc = now - ship.contT;
            if (dtc < 0f) {
                dtc = 0f;
            } else if (dtc > MaxSmoothDt) {
                dtc = MaxSmoothDt;
            }
            Vector3 vModel = useDrag
                ? ship.vel * Mathf.Exp(-LeanParams.LeanDrag * horizon) + ship.thrust * d1
                : rawVel;
            Vector3 carried = ship.contPos + vModel * dtc;

            Vector3 correction = (target - carried) * (1f - Mathf.Exp(-dtc / LeanParams.LeanSmoothTau));
            float maxCorrection = LeanParams.LeanMaxCorrVel * dtc;
            float correctionMag = correction.magnitude;
            if (correctionMag > maxCorrection && correctionMag > 1e-6f) {
                correction *= maxCorrection / correctionMag;
            }

            ship.contPos = carried + correction;
            ship.contT = now;
            return ship.contPos;
        }

        // ------------------------------------------------------------------
        // The mode-3 extrapolation path, replacing the body of
        // extrapolatePlayer (respawn handling already done by the caller).
        // ------------------------------------------------------------------
        public static void ExtrapolatePlayerAdaptive(Player player, NewPlayerSnapshot snapshot, float t) {
            Vector3 basePos = snapshot.m_pos;
            Vector3 newPos;
            lock (s_lock) {
                LeanShip ship;
                s_ships.TryGetValue(snapshot.m_net_id.Value, out ship);
                newPos = (ship != null)
                    ? Predict(ship, basePos, snapshot.m_vel, t, Time.time)
                    : basePos + snapshot.m_vel * t;    // no snapshot captured yet: plain CV
            }

            // wall clamp with slide (same maxDive semantics as the legacy clamp)
            if (Menus.mms_lag_compensation_collision_limit > 0) {
                newPos = ClampWithSlide(basePos, newPos);
            }

            // rotation: same handling as the VEL+ROTATION mode
            float rotScale = RotScale();
            Quaternion newRot = Quaternion.Euler(snapshot.m_vrot * (Mathf.Rad2Deg * t * rotScale)) * snapshot.m_rot;

            player.c_player_ship.c_transform.localPosition = newPos;
            player.c_player_ship.c_transform.rotation = newRot;
            player.c_player_ship.c_mesh_collider_trans.localPosition = player.c_player_ship.c_transform.localPosition;
            player.c_player_ship.c_mesh_collider_trans.localRotation = player.c_player_ship.c_transform.localRotation;
        }

        // ------------------------------------------------------------------
        // Render-side helpers, moved from the retired lup.test2/test3 glue.
        // ------------------------------------------------------------------
        private const float ShipRadius = 0.98f;    // m; collision-clamp sphere (ship collider is ~1, identical to the legacy clamp)
        private const float SlideMinLen = 0.01f;   // m; slide motions shorter than this are dropped

        // Rotation-strength mapping, identical to the legacy switch in
        // extrapolatePlayer (the arc scale is not used by this mode).
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
            float radius = ShipRadius;
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
            if (slideLen <= SlideMinLen) {
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
    }

    // clear the tracker when (re)connecting, alongside the reckoning reset
    [HarmonyPatch(typeof(Client), "Connect")]
    class MPShipPredictionAdaptive_Client_Connect {
        static void Postfix() {
            if (Overload.NetworkManager.IsServer()) {
                return;
            }
            MPShipPredictionAdaptive.ClearAll();
        }
    }
}
