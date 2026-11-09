using System;
using System.Collections.Generic;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace GameMod.Replay
{
    public static class Recorder
    {
        public static Recording Recording;
        private static bool started;

        private static float recordTime;
        private static float lastRawTimestamp;
        private static float lastSampledTime;

        private const float MAX_CLOCK_EXTRAPOLATION_SECONDS = 0.25f;
        private const float SOURCE_STALLED_SECONDS = 1f;
        private static float clockTime;
        private static float clockAnchorRealTime;

        internal static float CurrentTime
        {
            get
            {
                float sinceAnchor = Mathf.Max(0f, Time.time - clockAnchorRealTime);
                if (sinceAnchor <= SOURCE_STALLED_SECONDS)
                    sinceAnchor = Mathf.Min(sinceAnchor, MAX_CLOCK_EXTRAPOLATION_SECONDS);
                return clockTime + sinceAnchor;
            }
        }
        private static bool alreadySaved;
        private static Dictionary<uint, int> netIdToIndex = new Dictionary<uint, int>();
        private static Dictionary<Item, int> itemIds = new Dictionary<Item, int>();
        private static int nextItemId;

        private static readonly Dictionary<Item, MatchEvent> itemSpawnEvents = new Dictionary<Item, MatchEvent>();

        private static readonly List<Item> movingItems = new List<Item>();
        private static readonly List<ItemSample> itemSampleBuffer = new List<ItemSample>();

        private static Dictionary<Projectile, int> fireIdByProjectile = new Dictionary<Projectile, int>();
        private static int nextFireId;

        private static List<int> lastTeam = new List<int>();
        private static List<bool> lastSpectator = new List<bool>();

        private static MonsterBall trackedBall;
        private static int[] lastTeamScore;
        private static readonly FlagState[] lastFlagState = new FlagState[CTF.TeamCount];
        private static readonly int[] lastFlagCarrier = new int[CTF.TeamCount];

        private static bool objectiveBaselineRecorded;
        private static bool recordingCTF;
        private static bool recordingMonsterball;
        private static bool recordingTeamMode;

        private static readonly List<MatchEvent> pendingGoalEvents = new List<MatchEvent>();
        private struct PendingScoreIncrease { public int team; public float t; }
        private static readonly List<PendingScoreIncrease> recentScoreIncreases = new List<PendingScoreIncrease>();
        private const float GOAL_ATTRIBUTION_WINDOW_SECONDS = 1f;

        private struct GoalCredit { public int goals; public int blunders; }
        private static readonly Dictionary<uint, GoalCredit> lastGoalCredit = new Dictionary<uint, GoalCredit>();

        private struct PendingPickupFX { public int playerIndex; public int itemType; public float t; }
        private static readonly List<PendingPickupFX> recentPickupFX = new List<PendingPickupFX>();

        private struct PendingPickupEvent { public MatchEvent ev; public int itemType; }
        private static readonly List<PendingPickupEvent> recentPickupEvents = new List<PendingPickupEvent>();
        private const float PICKUP_MATCH_WINDOW_SECONDS = 0.5f;

        public static void Reset()
        {
            started = false;
            Recording = null;
            recordTime = 0f;
            lastRawTimestamp = 0f;
            lastSampledTime = 0f;
            clockTime = 0f;
            clockAnchorRealTime = Time.time;
            netIdToIndex.Clear();
            itemIds.Clear();
            nextItemId = 0;
            itemSpawnEvents.Clear();
            movingItems.Clear();
            fireIdByProjectile.Clear();
            nextFireId = 0;
            lastTeam.Clear();
            lastSpectator.Clear();
            recentPickupFX.Clear();
            recentPickupEvents.Clear();
            recentMiniFires.Clear();
            robotIds.Clear();
            nextRobotId = 0;
            recordingRobots = false;

            lastTeamScore = null;
            for (int flag = 0; flag < CTF.TeamCount; flag++)
            {
                lastFlagState[flag] = FlagState.HOME;
                lastFlagCarrier[flag] = -1;
            }
            objectiveBaselineRecorded = false;
            recordingCTF = false;
            recordingMonsterball = false;
            recordingTeamMode = false;
            pendingGoalEvents.Clear();
            recentScoreIncreases.Clear();
            lastGoalCredit.Clear();
        }

        public static void OnNewGame()
        {
            if (started)
                StopAndSave();
            alreadySaved = false;
        }

        public static bool IsRecording => started && Session.Mode == SessionMode.Recording;

        internal static bool CanRecord => IsRecording && Recording != null;

        private static int AddRosterPlayer(Player player, NetworkInstanceId netId, string name)
        {
            int rosterIndex = Recording.players.Count;
            Recording.players.Add(new RosterEntry
            {
                name = name,
                team = (int)player.m_mp_team,

                color = UIManager.ChooseMpColor(player.m_mp_team),
                netId = netId
            });
            netIdToIndex[netId.Value] = rosterIndex;
            lastTeam.Add((int)player.m_mp_team);
            lastSpectator.Add(player.m_spectator);

            if (Recording.recorderSlot < 0 && player == GameManager.m_local_player)
                Recording.recorderSlot = rosterIndex;
            return rosterIndex;
        }

        private static Vector3 ShipPosition(Player player)
        {
            return player.c_player_ship != null && player.c_player_ship.c_transform != null
                ? player.c_player_ship.c_transform.position : Vector3.zero;
        }

        public static void Start()
        {
            if (!Session.RecordReplays)
                return;
            if (Session.Mode == SessionMode.Playback)
                return;
            if (IsRecording)
                return;
            if (GameplayManager.IsDedicatedServer())
                return;

            Reset();
            Recording = new Recording();
            Recording.recordedUtcBinary = DateTime.UtcNow.ToBinary();
            var level = GameplayManager.Level;
            if (level != null)
            {
                Recording.sceneName = level.FileName;
                if (level.IsAddOn)
                    Recording.levelIdHash = level.GetAddOnLevelIdStringHash();
            }
            bool challenge = GameplayManager.IsChallengeMode;
            Recording.matchMode = challenge ? Recording.CHALLENGE_MATCH_MODE : (int)NetworkMatch.GetMode();
            recordingCTF = Recording.matchMode == (int)CTF.MatchModeCTF;
            recordingMonsterball = Recording.matchMode == (int)MatchMode.MONSTERBALL;

            recordingTeamMode = !challenge && NetworkMatch.IsTeamMode(NetworkMatch.GetMode());
            recordingRobots = challenge;

            Recording.maxHitpoints = Player.MAX_HITPOINTS > 1f ? Player.MAX_HITPOINTS : 120f;

            int timeLimitSeconds = challenge ? 0 : NetworkMatch.m_match_time_limit_seconds;
            bool hasTimeLimit = timeLimitSeconds > 0 &&
                                timeLimitSeconds < 2000000; // the menu stores "no limit" as a huge seconds value
            Recording.matchTimeLimit = hasTimeLimit ? timeLimitSeconds : 0f;

            Recording.matchStartOffset = challenge ? 0f : Mathf.Max(0f, NetworkMatch.m_match_elapsed_seconds);

            if (challenge)
            {
                var pilot = GameManager.m_local_player;
                AddRosterPlayer(pilot, pilot.netId, PilotManager.PilotName);
            }
            else
            {
                foreach (var player in Overload.NetworkManager.m_Players)
                {
                    if (player == null || player.m_spectator)
                        continue;
                    AddRosterPlayer(player, player.netId, player.m_mp_name);
                }
            }

            if (Item.m_ItemList != null)
                foreach (var item in Item.m_ItemList)
                    RecordItemSpawn(item, moving: false);

            Session.Mode = SessionMode.Recording;
            started = true;
            alreadySaved = false;
            Debug.LogFormat("[Replay] recording started on {0} with {1} players", Recording.sceneName, Recording.players.Count);
        }

        public static void StopAndSave()
        {
            if (!started || Recording == null)
            {
                Reset();
                return;
            }
            started = false;
            alreadySaved = true;
            Session.Mode = SessionMode.Off;
            try
            {
                if (Recording.frames.Count > 0)
                    OlrFile.Save(Recording);
                else
                    Debug.Log("[Replay] nothing to save (0 frames recorded)");
            }
            catch (System.Exception e)
            {
                Debug.LogErrorFormat("[Replay] save failed: {0}", e);
            }
            Reset();
        }

        private static bool ShouldSample(float time)
        {
            if (Recording.frames.Count == 0)
                return true;
            // Absorb small float rounding differences
            return (time - lastSampledTime) >= 1f / 60f - 1e-4f;
        }

        private static void AdvanceClock(float t)
        {
            clockTime = t;
            clockAnchorRealTime = Time.time;
        }

        private const int MISSILE_COUNT_BITS = 8;

        private static ShipState CaptureShipState(Player player, Vector3 pos, Quaternion rot, Vector3 vel, Vector3 vrot)
        {
            var state = new ShipState
            {
                present = true, pos = pos, rot = rot, vel = vel, vrot = vrot,
                alive = true, health = ShipState.FULL_HEALTH_BYTE
            };
            if (player == null)
                return state;

            state.weapon = (byte)player.m_weapon_type;
            state.missile = (byte)player.m_missile_type;
            if (player.c_player_ship != null)
                state.alive = !(player.c_player_ship.m_dead || player.c_player_ship.m_dying);

            float healthFraction = Recording.maxHitpoints > 1f
                ? (float)player.m_hitpoints / Recording.maxHitpoints : 1f;

            state.health = (byte)Mathf.Clamp(
                Mathf.FloorToInt(healthFraction * ShipState.FULL_HEALTH_BYTE + 0.5f),
                0, ShipState.FULL_HEALTH_BYTE);

            CaptureLoadout(player, ref state);
            return state;
        }

        private static void CaptureLoadout(Player player, ref ShipState state)
        {
            state.energy = (byte)Mathf.Clamp(Mathf.RoundToInt((float)player.m_energy), 0, byte.MaxValue);
            state.ammo = (ushort)Mathf.Clamp((int)player.m_ammo, 0, ushort.MaxValue);
            uint weaponLevels = 0, missileLevels = 0;
            ulong missileAmmo = 0;
            for (int slot = 0; slot < 8; slot++)
            {
                weaponLevels = ShipState.PackWeaponLevel(weaponLevels, slot, player.m_weapon_level[slot]);
                missileLevels = ShipState.PackWeaponLevel(missileLevels, slot, player.m_missile_level[slot]);
                int count = Mathf.Clamp((int)player.m_missile_ammo[slot], 0, 255);
                missileAmmo |= (ulong)count << (slot * MISSILE_COUNT_BITS);
            }
            state.weaponLevels = weaponLevels;
            state.missileLevels = missileLevels;
            state.missileAmmo = missileAmmo;
            state.statusFlags = ShipState.PackStatusFlags(player.m_cloaked, player.m_overdrive,
                player.m_invulnerable, player.m_upgrade_level[2]); // upgrade 2 = ammo capacity
        }

        [HarmonyPatch(typeof(MPClientShipReckoning), "AddNewPlayerSnapshot")]
        class Recorder_AddNewPlayerSnapshot
        {
            static void Postfix(NewPlayerSnapshotToClientMessage msg, MPNoPositionCompression.SnapshotVersion version)
            {
                if (GameplayManager.IsDedicatedServer() || Session.Mode == SessionMode.Playback)
                    return;

                bool haveTimestamp = version == MPNoPositionCompression.SnapshotVersion.VELOCITY_TIMESTAMP &&
                                     msg.m_server_timestamp > 0f;

                if (IsRecording && Recording != null && Recording.frames.Count > 0 &&
                    haveTimestamp && msg.m_server_timestamp < lastRawTimestamp - 2f)
                {
                    StopAndSave();
                    alreadySaved = false;
                }

                if (!IsRecording)
                {
                    bool matchIsPlaying = NetworkMatch.GetMatchState() == MatchState.PLAYING &&
                                          Overload.NetworkManager.m_Players.Count > 0;
                    if (!alreadySaved && Session.Mode == SessionMode.Off && matchIsPlaying)
                        Start();
                    if (!IsRecording)
                        return;
                }
                if (Recording == null || !NetworkMatch.InGameplay())
                    return;

                if (!haveTimestamp)
                    recordTime += Time.fixedDeltaTime;
                float t = haveTimestamp ? msg.m_server_timestamp : recordTime;
                lastRawTimestamp = t;
                AdvanceClock(t);

                if (!ShouldSample(t))
                    return;
                lastSampledTime = t;

                for (int i = 0; i < msg.m_num_snapshots; i++)
                {
                    var netId = msg.m_snapshots[i].m_net_id;
                    if (netIdToIndex.ContainsKey(netId.Value))
                        continue;
                    Player joiner = OL_Client.GetPlayerFromNetId(netId);
                    if (joiner == null || joiner.m_spectator)
                        continue;
                    AddRosterPlayer(joiner, netId, joiner.m_mp_name);
                }

                Frame previousFrame = Recording.frames.Count > 0 ? Recording.frames[Recording.frames.Count - 1] : null;
                var frame = new Frame { t = t, ships = new ShipState[Recording.players.Count] };
                for (int i = 0; i < msg.m_num_snapshots; i++)
                {
                    var snapshot = msg.m_snapshots[i];
                    int rosterIndex;
                    if (!netIdToIndex.TryGetValue(snapshot.m_net_id.Value, out rosterIndex))
                        continue;
                    Player player = OL_Client.GetPlayerFromNetId(snapshot.m_net_id);
                    frame.ships[rosterIndex] = CaptureShipState(player, snapshot.m_pos, snapshot.m_rot, snapshot.m_vel, snapshot.m_vrot);
                    RecordTeamChangeIfAny(rosterIndex, player, t);
                    FillMissingRosterName(rosterIndex, player);
                }

                FillMissingShips(frame, previousFrame);
                CollectItemSamples(frame);
                CollectObjectiveState(frame, t);
                Recording.frames.Add(frame);
            }
        }

        private static void FillMissingShips(Frame frame, Frame previousFrame)
        {
            if (previousFrame == null)
                return;
            for (int rosterIndex = 0; rosterIndex < frame.ships.Length; rosterIndex++)
            {
                if (frame.ships[rosterIndex].present)
                    continue;
                bool wasFlyingLastFrame = rosterIndex < previousFrame.ships.Length &&
                                          previousFrame.ships[rosterIndex].present &&
                                          previousFrame.ships[rosterIndex].alive;
                if (!wasFlyingLastFrame)
                    continue;
                Player player = Recording == null || rosterIndex < 0 || rosterIndex >= Recording.players.Count
                    ? null : OL_Client.GetPlayerFromNetId(Recording.players[rosterIndex].netId);
                if (player == null || player.m_spectator || player.c_player_ship == null)
                    continue;
                if (player.c_player_ship.m_dead || player.c_player_ship.m_dying)
                    continue;
                var shipTransform = player.c_player_ship.c_transform;
                if (shipTransform == null)
                    continue;
                var shipBody = player.c_player_ship.c_rigidbody;
                Vector3 velocity = shipBody != null ? shipBody.velocity : Vector3.zero;
                Vector3 angularVelocity = shipBody != null ? shipBody.angularVelocity : Vector3.zero;
                frame.ships[rosterIndex] = CaptureShipState(player, shipTransform.localPosition,
                                                            shipTransform.rotation, velocity, angularVelocity);
            }
        }

        // A challenge has no snapshot stream, so the local ship is sampled on the physics tick.
        [HarmonyPatch(typeof(PlayerShip), "FixedUpdateAll")]
        class Recorder_PlayerShip_FixedUpdateAll
        {
            static void Postfix()
            {
                if (!GameplayManager.IsChallengeMode || Session.Mode == SessionMode.Playback)
                    return;
                var player = GameManager.m_local_player;
                if (GameplayManager.m_gameplay_state != GameplayState.PLAYING ||
                    player == null || player.c_player_ship == null)
                    return;
                if (!IsRecording && !alreadySaved && Session.Mode == SessionMode.Off)
                    Start();
                if (!IsRecording)
                    return;

                recordTime += Time.fixedDeltaTime;
                AdvanceClock(recordTime);
                if (!ShouldSample(recordTime))
                    return;
                lastSampledTime = recordTime;

                var ship = player.c_player_ship;
                var body = ship.c_rigidbody;
                var frame = new Frame { t = recordTime, ships = new ShipState[1] };
                frame.ships[0] = CaptureShipState(player, ship.c_transform.localPosition, ship.c_transform.rotation,
                    body != null ? body.velocity : Vector3.zero, body != null ? body.angularVelocity : Vector3.zero);
                CollectItemSamples(frame);
                CollectRobotSamples(frame);
                Recording.frames.Add(frame);
            }
        }

        // Death and quit both end a challenge through DoneLevel, which then opens the results
        // menu (GameplayManager.cs:2130) and never reaches GameplayState.EXIT.
        [HarmonyPatch(typeof(GameplayManager), "DoneLevel")]
        class Recorder_GameplayManager_DoneLevel
        {
            static void Prefix()
            {
                if (GameplayManager.IsChallengeMode && IsRecording)
                    StopAndSave();
            }
        }

        [HarmonyPatch(typeof(Projectile), "Fire")]
        class Recorder_Projectile_Fire
        {
            static void Postfix(Projectile __instance, GameObject owner, ProjTeam team,
                                WeaponUnlock upgrade_lvl, Vector3 pos, Quaternion rot)
            {
                if (!CanRecord || __instance == null)
                    return;
                if (team == ProjTeam.ENEMY)
                {
                    if (recordingRobots)
                        RecordRobotFire(__instance, owner, upgrade_lvl, pos, rot);
                    return;
                }
                if (owner == null)
                    return;
                var player = owner.GetComponent<Player>();
                if (player != null)
                    RecordPlayerFire(__instance, player, upgrade_lvl, pos, rot);
            }
        }

        private static void RecordPlayerFire(Projectile projectile, Player player,
                                             WeaponUnlock upgradeLevel, Vector3 pos, Quaternion rot)
        {
            if (IsUnrecordedFireType(projectile.m_type))
                return;
            int rosterIndex;
            if (!netIdToIndex.TryGetValue(player.netId.Value, out rosterIndex))
                return;
            bool isMiniSubmunition = projectile.m_type == ProjPrefab.missile_smart_mini ||
                                     projectile.m_type == ProjPrefab.missile_devastator_mini;
            if (isMiniSubmunition && IsDuplicateMiniFire(rosterIndex, projectile.m_type, pos, rot))
                return;

            int fireId = nextFireId++;
            fireIdByProjectile[projectile] = fireId;
            Recording.events.Add(new MatchEvent
            {
                t = CurrentTime, type = MatchEventType.Fire,
                iA = rosterIndex, iB = (int)projectile.m_type, iC = fireId,
                fA = (int)upgradeLevel, pos = pos, rot = rot
            });
        }

        internal static bool IsUnrecordedFireType(ProjPrefab type)
        {
            return type == ProjPrefab.proj_flare ||
                   type == ProjPrefab.proj_flare_sticky;
        }

        private struct RecentMiniFire { public float t; public int rosterIndex; public int prefab; public Vector3 pos; public Quaternion rot; }
        private static readonly List<RecentMiniFire> recentMiniFires = new List<RecentMiniFire>();

        private static bool IsDuplicateMiniFire(int rosterIndex, ProjPrefab type, Vector3 pos, Quaternion rot)
        {
            float now = CurrentTime;
            for (int i = recentMiniFires.Count - 1; i >= 0; i--)
            {
                if (now - recentMiniFires[i].t > 0.25f) // forget minis older than the dedup window
                {
                    recentMiniFires.RemoveAt(i);
                    continue;
                }
                var recent = recentMiniFires[i];

                if (recent.rosterIndex == rosterIndex && recent.prefab == (int)type &&
                    recent.pos.x == pos.x && recent.pos.y == pos.y && recent.pos.z == pos.z &&
                    recent.rot.x == rot.x && recent.rot.y == rot.y && recent.rot.z == rot.z && recent.rot.w == rot.w)
                    return true;
            }
            recentMiniFires.Add(new RecentMiniFire { t = now, rosterIndex = rosterIndex, prefab = (int)type, pos = pos, rot = rot });
            return false;
        }

        // The instareap massdriver is not a Projectile, so the fire hook above never sees it.
        // Both the local shot and a relayed one reach this method, and only it knows where the
        // beam ended -- a local shot arrives with the end still unresolved. The shooter is not
        // recorded: a relayed packet does not name one, so no client could supply it.
        [HarmonyPatch(typeof(MPInstareap.MassdriverProjectile), "Fire")]
        class Recorder_MassdriverProjectile_Fire
        {
            static void Postfix(Vector3 pos, Quaternion rot, Vector3 ___end_position)
            {
                if (!CanRecord)
                    return;
                Recording.events.Add(new MatchEvent
                {
                    t = CurrentTime, type = MatchEventType.MassdriverShot,
                    iA = -1, fA = (___end_position - pos).magnitude, pos = pos, rot = rot
                });
            }
        }

        private static void RecordDespawn(Projectile proj, bool exploded)
        {
            if (!CanRecord || proj == null)
                return;
            int fireId;
            if (!fireIdByProjectile.TryGetValue(proj, out fireId))
                return;
            fireIdByProjectile.Remove(proj);
            Recording.events.Add(new MatchEvent
            {
                t = CurrentTime, type = MatchEventType.ProjDespawn,
                iA = fireId, iB = exploded ? 1 : 0,
                pos = proj.c_transform != null ? proj.c_transform.position : Vector3.zero
            });
        }

        [HarmonyPatch(typeof(Projectile), "Explode")]
        class Recorder_Projectile_Explode
        {
            static void Prefix(Projectile __instance) { RecordDespawn(__instance, exploded: true); }
        }

        [HarmonyPatch(typeof(Projectile), "DieNoExplode")]
        class Recorder_Projectile_DieNoExplode
        {
            static void Prefix(Projectile __instance) { RecordDespawn(__instance, exploded: false); }
        }

        [HarmonyPatch(typeof(Projectile), "KillInstant")]
        class Recorder_Projectile_KillInstant
        {
            static void Prefix(Projectile __instance) { RecordDespawn(__instance, exploded: false); }
        }

        [HarmonyPatch(typeof(Player), "RpcAddKill")]
        class Recorder_RpcAddKill
        {
            static void Postfix(string killer_name, string killed_name, int kill_type)
            {
                if (!CanRecord)
                    return;
                if (!string.IsNullOrEmpty(killer_name) && killer_name[0] == '*')
                    killer_name = killer_name.Substring(1);
                int killerIndex = Recording.IndexForName(killer_name);

                int killerTeam = killerIndex >= 0 && killerIndex < lastTeam.Count
                    ? lastTeam[killerIndex] : (int)MpTeam.ANARCHY;
                Recording.events.Add(new MatchEvent
                {
                    t = CurrentTime, type = MatchEventType.Kill,
                    iA = killerIndex, iB = Recording.IndexForName(killed_name),
                    iC = killerTeam, fA = kill_type,
                    nameA = killer_name ?? "", nameB = killed_name ?? ""
                });
            }
        }

        [HarmonyPatch(typeof(Player), "RpcAddDeath")]
        class Recorder_RpcAddDeath
        {
            static void Postfix(Player __instance)
            {
                int rosterIndex = RosterIndexOf(__instance);
                if (rosterIndex < 0)
                    return;
                Recording.events.Add(new MatchEvent
                {
                    t = CurrentTime, type = MatchEventType.Death,
                    iA = rosterIndex, pos = ShipPosition(__instance)
                });
            }
        }

        [HarmonyPatch(typeof(Player), "RpcAddAssist")]
        class Recorder_RpcAddAssist
        {
            static void Postfix(Player __instance)
            {
                int rosterIndex = RosterIndexOf(__instance);
                if (rosterIndex < 0)
                    return;
                Recording.events.Add(new MatchEvent
                {
                    t = CurrentTime, type = MatchEventType.Assist, iA = rosterIndex
                });
            }
        }

        private static int RosterIndexOf(Player player)
        {
            if (!CanRecord || player == null)
                return -1;
            int rosterIndex;
            return netIdToIndex.TryGetValue(player.netId.Value, out rosterIndex) ? rosterIndex : -1;
        }

        private static void RecordTeamChangeIfAny(int rosterIndex, Player player, float t)
        {
            if (player == null || rosterIndex < 0 || rosterIndex >= lastTeam.Count)
                return;
            int team = (int)player.m_mp_team;
            bool isSpectator = player.m_spectator;
            if (team == lastTeam[rosterIndex] && isSpectator == lastSpectator[rosterIndex])
                return;
            lastTeam[rosterIndex] = team;
            lastSpectator[rosterIndex] = isSpectator;

            Color color = UIManager.ChooseMpColor(player.m_mp_team);
            Recording.events.Add(new MatchEvent
            {
                t = t, type = MatchEventType.TeamChange,
                iA = rosterIndex, iB = team, iC = isSpectator ? 1 : 0,

                pos = new Vector3(color.r, color.g, color.b)
            });
        }

        private static void FillMissingRosterName(int rosterIndex, Player player)
        {
            if (player == null || rosterIndex < 0 || rosterIndex >= Recording.players.Count)
                return;
            if (string.IsNullOrEmpty(Recording.players[rosterIndex].name) && !string.IsNullOrEmpty(player.m_mp_name))
                Recording.players[rosterIndex].name = player.m_mp_name;
        }

        [HarmonyPatch(typeof(Player), "RestorePlayerShipDataAfterRespawn")]
        class Recorder_RestorePlayerShipDataAfterRespawn
        {
            static void Postfix(Player __instance)
            {
                int rosterIndex = RosterIndexOf(__instance);
                if (rosterIndex < 0)
                    return;
                Recording.events.Add(new MatchEvent
                {
                    t = CurrentTime, type = MatchEventType.Respawn,
                    iA = rosterIndex, iB = __instance.m_use_loadout1 ? 1 : 0,
                    pos = ShipPosition(__instance)
                });
            }
        }

        [HarmonyPatch(typeof(PlayerShip), "RpcApplyDamage")]
        class Recorder_RpcApplyDamage
        {
            static void Postfix(PlayerShip __instance, float damage_scaled)
            {
                if (__instance == null)
                    return;
                int rosterIndex = RosterIndexOf(__instance.c_player);
                if (rosterIndex < 0)
                    return;
                Recording.events.Add(new MatchEvent
                {
                    t = CurrentTime, type = MatchEventType.Damage,
                    iA = rosterIndex, fA = damage_scaled, pos = __instance.c_transform.position
                });
            }
        }

        [HarmonyPatch(typeof(PlayerShip), "RpcApplyDamageEffects")]
        class Recorder_RpcApplyDamageEffects
        {
            static void Postfix(PlayerShip __instance, DamageType damage_type, float damage,
                Vector3 damage_pos)
            {
                if (__instance == null)
                    return;
                int rosterIndex = RosterIndexOf(__instance.c_player);
                if (rosterIndex < 0)
                    return;
                Recording.events.Add(new MatchEvent
                {
                    t = CurrentTime, type = MatchEventType.DamageImpact,
                    iA = rosterIndex, iB = (int)damage_type, fA = damage, pos = damage_pos
                });
            }
        }

        private static void RecordItemSpawn(Item item, bool moving)
        {
            if (item == null || Recording == null || itemIds.ContainsKey(item))
                return;
            if (IsItemPrefabTemplate(item))
                return;
            int id = nextItemId++;
            itemIds[item] = id;
            var ev = new MatchEvent
            {
                t = CurrentTime, type = MatchEventType.ItemSpawn,
                iA = id, iB = (int)item.m_type, iC = item.m_super ? 1 : 0,

                fA = item.m_index,
                pos = item.c_transform != null ? item.c_transform.position : Vector3.zero
            };
            Recording.events.Add(ev);
            itemSpawnEvents[item] = ev;
            if (moving && id <= ushort.MaxValue)
                movingItems.Add(item);
        }

        internal static bool IsItemPrefabTemplate(Item item)
        {
            return item != null && item.gameObject != null && CTF.FlagObjs.Contains(item.gameObject);
        }

        private static void CollectItemSamples(Frame frame)
        {
            if (movingItems.Count == 0)
                return;
            itemSampleBuffer.Clear();
            for (int i = movingItems.Count - 1; i >= 0; i--)
            {
                var item = movingItems[i];
                int id;
                if (item == null || item.c_transform == null || !itemIds.TryGetValue(item, out id))
                {
                    movingItems.RemoveAt(i);
                    continue;
                }
                itemSampleBuffer.Add(new ItemSample { id = (ushort)id, pos = item.c_transform.position });
            }
            if (itemSampleBuffer.Count > 0)
                frame.items = itemSampleBuffer.ToArray();
        }

        [HarmonyPatch(typeof(MonsterBall), "Awake")]
        class Recorder_MonsterBall_Awake
        {
            static void Postfix(MonsterBall __instance)
            {
                if (Session.Mode == SessionMode.Playback)
                    return;
                trackedBall = __instance;
            }
        }

        private static void CollectObjectiveState(Frame frame, float t)
        {
            if (recordingMonsterball)
            {
                var ball = trackedBall != null ? trackedBall : NetworkMatch.m_monsterball;
                if (ball != null && ball.c_transform != null)
                {
                    frame.objective.ballPresent = true;
                    frame.objective.ballPos = ball.c_transform.position;
                    frame.objective.ballRot = ball.c_transform.rotation;
                    frame.objective.ballVel = ball.c_rigidbody != null ? ball.c_rigidbody.velocity : Vector3.zero;
                }
            }
            bool recordBaseline = !objectiveBaselineRecorded;
            if (recordBaseline && recordingMonsterball)
            {
                int scorerBeforeRecording;
                bool ownGoalBeforeRecording;
                RefreshGoalCredit(out scorerBeforeRecording, out ownGoalBeforeRecording);
            }
            if (recordingTeamMode)
                RecordTeamScoreChanges(t, recordBaseline);
            if (recordingCTF)
                RecordFlagStateChanges(t, recordBaseline);
            PruneStaleGoalAttribution(t);
            objectiveBaselineRecorded = true;
        }

        private static void RecordTeamScoreChanges(float t, bool recordBaseline)
        {
            var scores = NetworkMatch.m_team_scores;
            if (scores == null)
                return;
            if (lastTeamScore == null || lastTeamScore.Length != scores.Length)
            {
                lastTeamScore = new int[scores.Length];
                recordBaseline = true;
            }
            for (int team = 0; team < scores.Length; team++)
            {
                int score = scores[team];
                if (recordBaseline ? score == 0 : score == lastTeamScore[team])
                    continue;
                bool scoreIncreased = !recordBaseline && score > lastTeamScore[team];
                lastTeamScore[team] = score;
                Recording.events.Add(new MatchEvent
                {
                    t = t, type = MatchEventType.TeamScore, iA = team, iB = score
                });
                if (scoreIncreased && recordingMonsterball)
                    OnScoreIncrease(team, t);
            }
        }

        private static void RecordFlagStateChanges(float t, bool recordBaseline)
        {
            var states = CTF.FlagStates;
            if (states == null)
                return;
            for (int flag = 0; flag < states.Length && flag < lastFlagState.Length; flag++)
            {
                int carrier = FlagCarrierIndex(flag);
                if (!recordBaseline && states[flag] == lastFlagState[flag] &&
                    carrier == lastFlagCarrier[flag])
                    continue;
                lastFlagState[flag] = states[flag];
                lastFlagCarrier[flag] = carrier;

                Recording.events.Add(new MatchEvent
                {
                    t = t, type = MatchEventType.FlagState,
                    iA = flag, iB = (int)states[flag], iC = carrier,
                    fA = ReturnTimerSecondsLeft(flag, states[flag])
                });
            }
        }

        private static float ReturnTimerSecondsLeft(int flag, FlagState state)
        {
            if (state != FlagState.LOST || CTF.FlagReturnTime == null || flag >= CTF.FlagReturnTime.Length)
                return 0f;
            return Mathf.Max(0f, CTF.FlagReturnTime[flag] - Time.time);
        }

        private static int FlagCarrierIndex(int flag)
        {
            foreach (var carried in CTF.PlayerHasFlag)
            {
                if (carried.Value != flag)
                    continue;
                int rosterIndex;
                return netIdToIndex.TryGetValue(carried.Key.Value, out rosterIndex) ? rosterIndex : -1;
            }
            return -1;
        }

        [HarmonyPatch(typeof(MonsterBall), "RpcMonsterBallExplodeFX")]
        class Recorder_RpcMonsterBallExplodeFX
        {
            static void Postfix(Vector3 pos, Quaternion rot)
            {
                if (!CanRecord)
                    return;
                float now = CurrentTime;
                var goal = new MatchEvent
                {
                    t = now, type = MatchEventType.MonsterballGoal,

                    iA = TakeRecentScoreIncrease(now), iB = -1, iC = 0,
                    pos = pos, rot = rot
                };
                Recording.events.Add(goal);
                int scorerIndex;
                bool ownGoal;
                if (RefreshGoalCredit(out scorerIndex, out ownGoal))
                {
                    goal.iB = scorerIndex;
                    goal.iC = ownGoal ? 1 : 0;
                }
                if (goal.GoalScoringTeam < 0)
                    pendingGoalEvents.Add(goal);
                PruneStaleGoalAttribution(now);
            }
        }

        private static bool RefreshGoalCredit(out int scorerIndex, out bool ownGoal)
        {
            scorerIndex = -1;
            ownGoal = false;
            var stats = MonsterballAddon.PlayerStats;
            if (stats == null)
                return false;
            bool credited = false;
            foreach (var entry in stats)
            {
                if (entry.Value == null)
                    continue;
                uint netId = entry.Key.Value;
                GoalCredit last;
                if (!lastGoalCredit.TryGetValue(netId, out last))
                    last = new GoalCredit();
                bool scored = entry.Value.Goals > last.goals;
                bool blundered = entry.Value.Blunders > last.blunders;
                lastGoalCredit[netId] = new GoalCredit
                {
                    goals = entry.Value.Goals, blunders = entry.Value.Blunders
                };
                if (!scored && !blundered)
                    continue;
                int rosterIndex;
                if (!netIdToIndex.TryGetValue(netId, out rosterIndex))
                    continue;
                scorerIndex = rosterIndex;
                ownGoal = blundered;
                credited = true;
            }
            return credited;
        }

        private static void OnScoreIncrease(int team, float now)
        {
            PruneStaleGoalAttribution(now);
            for (int i = pendingGoalEvents.Count - 1; i >= 0; i--)
            {
                var goal = pendingGoalEvents[i];
                if (goal.GoalScoringTeam >= 0)
                    continue;
                goal.GoalScoringTeam = team;
                pendingGoalEvents.RemoveAt(i);
                return;
            }
            recentScoreIncreases.Add(new PendingScoreIncrease { team = team, t = now });
        }

        private static int TakeRecentScoreIncrease(float now)
        {
            for (int i = recentScoreIncreases.Count - 1; i >= 0; i--)
            {
                if (now - recentScoreIncreases[i].t > GOAL_ATTRIBUTION_WINDOW_SECONDS)
                    continue;
                int team = recentScoreIncreases[i].team;
                recentScoreIncreases.RemoveAt(i);
                return team;
            }
            return -1;
        }

        private static void PruneStaleGoalAttribution(float now)
        {
            for (int i = pendingGoalEvents.Count - 1; i >= 0; i--)
                if (now - pendingGoalEvents[i].t > GOAL_ATTRIBUTION_WINDOW_SECONDS)
                    pendingGoalEvents.RemoveAt(i);
            for (int i = recentScoreIncreases.Count - 1; i >= 0; i--)
                if (now - recentScoreIncreases[i].t > GOAL_ATTRIBUTION_WINDOW_SECONDS)
                    recentScoreIncreases.RemoveAt(i);
        }

        [HarmonyPatch(typeof(Item), "Awake")]
        class Recorder_Item_Awake
        {
            static void Postfix(Item __instance)
            {
                if (!CanRecord || __instance == null)
                    return;
                RecordItemSpawn(__instance, moving: true);
            }
        }

        [HarmonyPatch(typeof(Item), "MaybeMakeSuper")]
        class Recorder_MaybeMakeSuper
        {
            static void Postfix(Item __instance)
            {
                if (!CanRecord || __instance == null || !__instance.m_super)
                    return;

                MatchEvent ev;
                if (itemSpawnEvents.TryGetValue(__instance, out ev))
                    ev.iC = 1;
            }
        }

        [HarmonyPatch(typeof(Player), "RpcShowWarningMessage")]
        class Recorder_RpcShowWarningMessage
        {
            static void Postfix(int type)
            {
                if (!CanRecord)
                    return;

                Recording.events.Add(new MatchEvent
                {
                    t = CurrentTime, type = MatchEventType.SuperAlert, iA = type
                });
            }
        }

        [HarmonyPatch(typeof(Item), "OnDestroy")]
        class Recorder_Item_OnDestroy
        {
            static void Prefix(Item __instance)
            {
                if (!CanRecord || __instance == null)
                    return;
                int id;
                if (!itemIds.TryGetValue(__instance, out id))
                    return;
                itemIds.Remove(__instance);
                itemSpawnEvents.Remove(__instance);
                movingItems.Remove(__instance);
                float now = CurrentTime;
                PruneStalePickups(now);
                int picker = TakeRecentPickupFX((int)__instance.m_type, now);

                var ev = new MatchEvent
                {
                    t = now, type = MatchEventType.ItemPickup,
                    iA = id, iB = picker,
                    pos = __instance.c_transform != null
                        ? __instance.c_transform.position : Vector3.zero
                };
                Recording.events.Add(ev);
                if (picker < 0)
                    recentPickupEvents.Add(new PendingPickupEvent { ev = ev, itemType = (int)__instance.m_type });
            }
        }

        private static void PruneStalePickups(float now)
        {
            for (int i = recentPickupFX.Count - 1; i >= 0; i--)
                if (now - recentPickupFX[i].t > PICKUP_MATCH_WINDOW_SECONDS)
                    recentPickupFX.RemoveAt(i);
            for (int i = recentPickupEvents.Count - 1; i >= 0; i--)
                if (now - recentPickupEvents[i].ev.t > PICKUP_MATCH_WINDOW_SECONDS)
                    recentPickupEvents.RemoveAt(i);
        }

        private static int TakeRecentPickupFX(int itemType, float now)
        {
            for (int i = recentPickupFX.Count - 1; i >= 0; i--)
            {
                var fx = recentPickupFX[i];
                if (fx.itemType == itemType && now - fx.t <= PICKUP_MATCH_WINDOW_SECONDS)
                {
                    recentPickupFX.RemoveAt(i);
                    return fx.playerIndex;
                }
            }
            return -1;
        }

        private static void OnPickupFX(Player player, int itemType)
        {
            if (!CanRecord || player == null)
                return;
            int rosterIndex;
            if (!netIdToIndex.TryGetValue(player.netId.Value, out rosterIndex))
                return;
            float now = CurrentTime;
            PruneStalePickups(now);

            for (int i = recentPickupEvents.Count - 1; i >= 0; i--)
            {
                var pending = recentPickupEvents[i];
                if (pending.itemType == itemType && pending.ev.PickedUpByIndex < 0)
                {
                    pending.ev.PickedUpByIndex = rosterIndex;
                    recentPickupEvents.RemoveAt(i);
                    return;
                }
            }
            recentPickupFX.Add(new PendingPickupFX { playerIndex = rosterIndex, itemType = itemType, t = now });
        }

        [HarmonyPatch(typeof(Player), "RpcPlayItemPickupFX")]
        class Recorder_RpcPlayItemPickupFX
        {
            static void Postfix(Player __instance, ItemType type)
            {
                OnPickupFX(__instance, (int)type);
            }
        }

        // Outside multiplayer the item plays its pickup FX itself instead of sending the Rpc
        // (Item.cs:570).
        [HarmonyPatch(typeof(Item), "PlayItemPickupFX")]
        class Recorder_Item_PlayItemPickupFX
        {
            static void Postfix(Item __instance, Player player)
            {
                if (!GameplayManager.IsMultiplayerActive && __instance != null)
                    OnPickupFX(player, (int)__instance.m_type);
            }
        }

        // Robots are not networked, so the master list is walked once per stored frame. A robot
        // is registered the first time anything sees it, which covers level robots, matcen
        // spawns and boss splits with no spawn hook.
        private static bool recordingRobots;
        private static readonly Dictionary<Robot, int> robotIds = new Dictionary<Robot, int>();
        private static int nextRobotId;
        private static readonly List<RobotSample> robotSampleBuffer = new List<RobotSample>();

        // Returns the robot's id, emitting its RobotSpawn (the only record of its type) on first
        // sight. Returns -1 once the ushort id space is used up.
        private static int EnsureRobotId(Robot robot)
        {
            int id;
            if (robotIds.TryGetValue(robot, out id))
                return id;
            if (nextRobotId > ushort.MaxValue)
                return -1;
            id = nextRobotId++;
            robotIds[robot] = id;
            var flags = (robot.m_super ? RobotSpawnFlags.Super : 0) |
                        (robot.m_variant ? RobotSpawnFlags.Variant : 0) |
                        (robot.m_alien ? RobotSpawnFlags.Alien : 0) |
                        (robot.m_is_boss ? RobotSpawnFlags.Boss : 0);
            Recording.events.Add(new MatchEvent
            {
                t = CurrentTime, type = MatchEventType.RobotSpawn,
                iA = id, iB = (int)robot.robot_type, iC = (int)flags,
                pos = robot.c_transform != null ? robot.c_transform.position : Vector3.zero,
                rot = robot.c_transform != null ? robot.c_transform.rotation : Quaternion.identity
            });
            return id;
        }

        // RobotManager.ActivateRobot deactivates sleeping robots out of the player's sight, so
        // only active robots move or render, and a missing sample means "not visible".
        private static void CollectRobotSamples(Frame frame)
        {
            var robots = RobotManager.m_master_robot_list;
            if (!recordingRobots || robots == null || robots.Count == 0)
                return;
            robotSampleBuffer.Clear();
            foreach (var robot in robots)
            {
                if (robot == null || robot.c_go == null || robot.c_transform == null || !robot.c_go.activeSelf)
                    continue;
                int id = EnsureRobotId(robot);
                if (id < 0)
                    continue;
                robotSampleBuffer.Add(new RobotSample
                {
                    id = (ushort)id,
                    pos = robot.c_transform.position,
                    rot = robot.c_transform.rotation,
                    vel = robot.c_rigidbody != null ? robot.c_rigidbody.velocity : Vector3.zero
                });
            }
            if (robotSampleBuffer.Count > 0)
                frame.robots = robotSampleBuffer.ToArray();
        }

        // No flare filter and no mini dedup: robots fire no flares, and their minis come once,
        // straight from the robot (a detonator fires twelve as it dies, Robot.cs:10613).
        // Reactor turrets fire without a robot and record iA = -1.
        private static void RecordRobotFire(Projectile projectile, GameObject owner,
                                            WeaponUnlock upgradeLevel, Vector3 pos, Quaternion rot)
        {
            var robot = owner != null ? owner.GetComponent<Robot>() : null;
            int fireId = nextFireId++;
            fireIdByProjectile[projectile] = fireId;
            Recording.events.Add(new MatchEvent
            {
                t = CurrentTime, type = MatchEventType.RobotFire,
                iA = robot != null ? EnsureRobotId(robot) : -1, iB = (int)projectile.m_type, iC = fireId,
                fA = (int)upgradeLevel, pos = pos, rot = rot
            });
        }

        // The dying tumble's poses are in the frames, this only cues the FX. m_dying is the
        // engine's own once-only test (Robot.cs:10547).
        [HarmonyPatch(typeof(Robot), "StartExploding")]
        class Recorder_Robot_StartExploding
        {
            static void Prefix(Robot __instance)
            {
                if (!CanRecord || !recordingRobots || __instance == null || __instance.m_dying)
                    return;
                RecordRobotDeath(__instance, RobotDeathStage.Dying);
            }
        }

        // ExplodeNow ends by removing the robot from the master list (Robot.cs:11166); a
        // prefix still sees its pose.
        [HarmonyPatch(typeof(Robot), "ExplodeNow")]
        class Recorder_Robot_ExplodeNow
        {
            static void Prefix(Robot __instance)
            {
                if (!CanRecord || !recordingRobots || __instance == null || !robotIds.ContainsKey(__instance))
                    return;
                RecordRobotDeath(__instance, RobotDeathStage.Exploded);
                robotIds.Remove(__instance);
            }
        }

        private static void RecordRobotDeath(Robot robot, RobotDeathStage stage)
        {
            int id = EnsureRobotId(robot);
            if (id < 0)
                return;
            Recording.events.Add(new MatchEvent
            {
                t = CurrentTime, type = MatchEventType.RobotDeath,
                iA = id, iB = (int)stage, fA = robot.m_explosion_size,
                pos = robot.c_transform != null ? robot.c_transform.position : Vector3.zero,
                rot = robot.c_transform != null ? robot.c_transform.rotation : Quaternion.identity
            });
        }

        [HarmonyPatch(typeof(Client), "OnMatchStart")]
        class Recorder_OnMatchStart
        {
            static void Postfix() { alreadySaved = false; }
        }

        [HarmonyPatch(typeof(NetworkMatch), "ExitMatch")]
        class Recorder_ExitMatch
        {
            static void Prefix() { StopAndSave(); }
        }

        [HarmonyPatch(typeof(Client), "Disconnect")]
        class Recorder_Disconnect
        {
            static void Prefix() { StopAndSave(); }
        }
    }

    internal static class RecorderChat
    {
        internal static void Record(string senderName, MpTeam team, string message)
        {
            if (!Recorder.CanRecord)
                return;
            Recorder.Recording.chat.Add(new ChatLine
            {
                t = Recorder.CurrentTime,
                sender = Recorder.Recording.IndexForName(senderName),
                team = (int)team,
                senderName = senderName ?? "",
                message = message ?? ""
            });
        }
    }

    [HarmonyPatch(typeof(NetworkMessageManager), "AddFullChatMessage")]
    class RecorderChat_AddFullChatMessage
    {
        static void Postfix(string sender_name, MpTeam sender_team, string msg)
        {
            RecorderChat.Record(sender_name, sender_team, msg);
        }
    }

    [HarmonyPatch(typeof(NetworkMessageManager), "AddQuickChatMessage")]
    class RecorderChat_AddQuickChatMessage
    {
        static void Postfix(string sender_name, MpTeam sender_team, int qc_idx)
        {
            bool phraseKnown = qc_idx >= 0 && qc_idx < MenuManager.m_qc_phrases.Length &&
                               !string.IsNullOrEmpty(MenuManager.m_qc_phrases[qc_idx]);
            string text = phraseKnown ? MenuManager.m_qc_phrases[qc_idx] : "[QC " + qc_idx + "]";
            RecorderChat.Record(sender_name, sender_team, text);
        }
    }
}
