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
    // the semantic reference for the tracker/predictor math, and
    // tools/lagcomp_bench/DESIGN.md documents the methodology, the validated
    // results and every porting decision.
    //
    // The engine-independent core (PredictionParams, ShipSnap, ShipTrack, the
    // predictors and the smart pipeline) lives in MPShipPredictionCore.cs so
    // the standalone evaluator (tools/lagcomp_eval/) can compile and run the
    // REAL shipped math over recorded .olr replays. This file is the engine
    // glue: snapshot capture, track registry, projective smoothing, wall-slide
    // clamp, rotation, diagnostics, Harmony patches.
    //
    // No code path here runs unless prediction mode 3 is selected; modes 0-2 use
    // the untouched legacy code in MPClientExtrapolation.cs.
    // ==========================================================================

    // ----------------------------------------------------------------------
    // MPShipPrediction -- track registry and the smart extrapolation path
    // called from MPClientShipReckoning.extrapolatePlayer.
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
                    ShipPredictionCore.ConsumeFresh(s_trackList[t]);
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
        // The smart extrapolation path. Replaces the body of extrapolatePlayer
        // for prediction mode 3 (respawn handling already done by the caller).
        // The position pipeline itself is ShipPredictionCore.PredictSmart;
        // this adds the render-side layers: projective smoothing, wall clamp,
        // rotation, transform writes.
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
            int model;
            float tEff;
            float hazard;
            Vector3 rawPos;
            bool smart;

            lock (s_lock) {
                s_tracks.TryGetValue(snapshot.m_net_id.Value, out tr);
                smart = ShipPredictionCore.SmartUsable(tr, t);
                rawPos = ShipPredictionCore.PredictSmart(tr, basePos, rawVel, t, Time.time,
                                                         out model, out tEff, out hazard);
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
            float radius = PredictionParams.ShipRadius;
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
