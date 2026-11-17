using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace GameMod {
    /// <summary>
    /// This mod disables position compression when the server supports it.
    /// </summary>
    public class MPNoPositionCompression {
        /// <summary>
        /// Determines whether no position compression is enabled for the current game.
        /// </summary>
        static public bool enabled = true;
        static public NewPlayerSnapshotToClientMessage m_new_snapshot_buffer = new NewPlayerSnapshotToClientMessage();
        public enum SnapshotVersion : uint {
            VANILLA = 0,          // PlayerSnapshotToClientMessage from vanilla overload /  olmod <= 0.3.5
            VELOCITY,             // NewPlayerSnapshotToClientMessage omlod >= 0.3.6, but no usable timestamps
            VELOCITY_TIMESTAMP,   // NewPlayerSnapshotToClientMessage olmod >= 0.3.6 but with usable timestamps
        }
        // Which version of the NewPlayerSnapshotToClientMessages we process
        // Set it to the default of a 0.3.6/0.3.7 server where we can't trust the
        // message timestamps. This might be updated by MPTweaks with the
        // "nocompress.reliable_timestamps" field if the server actually sends
        // reliable timestamps.
        static public SnapshotVersion NewSnapshotVersion = SnapshotVersion.VELOCITY;
    }

    public class NewPlayerSnapshot {
        public NewPlayerSnapshot() {
        }

        public NewPlayerSnapshot(NetworkInstanceId net_id,
                              Vector3 pos, Quaternion rot,
                              Vector3 vel, Vector3 vrot) {
            this.m_net_id = net_id;
            this.m_pos = pos;
            this.m_rot = rot;
            this.m_vel = vel;
            this.m_vrot = vrot;
        }

        public NetworkInstanceId m_net_id;

        public Vector3 m_pos;

        public Quaternion m_rot;

        public Vector3 m_vel;

        public Vector3 m_vrot;

        internal PlayerSnapshot ToOldSnapshot() {
            return new PlayerSnapshot() {
                m_net_id = this.m_net_id,
                m_pos = this.m_pos,
                m_rot = this.m_rot
            };
        }

        internal static NewPlayerSnapshot FromOldSnapshot(PlayerSnapshot s) {
            return s == null ? new NewPlayerSnapshot() : new NewPlayerSnapshot {
                m_net_id = s.m_net_id,
                m_pos = s.m_pos,
                m_rot = s.m_rot,
                m_vel = new Vector3(),
                m_vrot = new Vector3()
            };
        }
    }

    /// <summary>
    /// A class that handles the new way to serialize and deserialize player snapshots without compression.
    /// </summary>
    public class NewPlayerSnapshotToClientMessage : MessageBase {
        /// <summary>
        /// Serialize the snapshot without compression.
        /// </summary>
        /// <param name="writer"></param>
        public override void Serialize(NetworkWriter writer) {
            writer.Write(Time.fixedTime);
            writer.Write((byte)m_num_snapshots);
            for (int i = 0; i < m_num_snapshots; i++) {
                writer.Write(m_snapshots[i].m_net_id);
                writer.Write(m_snapshots[i].m_pos.x);
                writer.Write(m_snapshots[i].m_pos.y);
                writer.Write(m_snapshots[i].m_pos.z);
                writer.Write(NetworkCompress.CompressQuaternion(m_snapshots[i].m_rot));
                writer.Write(HalfHelper.Compress(m_snapshots[i].m_vel.x));
                writer.Write(HalfHelper.Compress(m_snapshots[i].m_vel.y));
                writer.Write(HalfHelper.Compress(m_snapshots[i].m_vel.z));
                writer.Write(HalfHelper.Compress(m_snapshots[i].m_vrot.x));
                writer.Write(HalfHelper.Compress(m_snapshots[i].m_vrot.y));
                writer.Write(HalfHelper.Compress(m_snapshots[i].m_vrot.z));
            }
        }

        /// <summary>
        /// Deserialize the snapshot without compression.
        /// </summary>
        /// <param name="reader"></param>
        public override void Deserialize(NetworkReader reader) {
            m_server_timestamp = reader.ReadSingle();
            m_num_snapshots = (int)reader.ReadByte();
            for (int i = 0; i < m_num_snapshots; i++) {
                // write into the preallocated snapshots instead of replacing
                // them: this runs for every snapshot message (60 Hz per client)
                NewPlayerSnapshot snapshot = m_snapshots[i];
                snapshot.m_net_id = reader.ReadNetworkId();
                snapshot.m_pos.x = reader.ReadSingle();
                snapshot.m_pos.y = reader.ReadSingle();
                snapshot.m_pos.z = reader.ReadSingle();
                snapshot.m_rot = NetworkCompress.DecompressQuaternion(reader.ReadUInt32());
                snapshot.m_vel.x = HalfHelper.Decompress(reader.ReadUInt16());
                snapshot.m_vel.y = HalfHelper.Decompress(reader.ReadUInt16());
                snapshot.m_vel.z = HalfHelper.Decompress(reader.ReadUInt16());
                snapshot.m_vrot.x = HalfHelper.Decompress(reader.ReadUInt16());
                snapshot.m_vrot.y = HalfHelper.Decompress(reader.ReadUInt16());
                snapshot.m_vrot.z = HalfHelper.Decompress(reader.ReadUInt16());
            }
        }

        public float m_server_timestamp; // servers NetworkMatch.m_match_elapsed_seconds when packet sent, may change meaning in future
        public int m_num_snapshots;
        public NewPlayerSnapshot[] m_snapshots = NewSnapshotArray();

        private static NewPlayerSnapshot[] NewSnapshotArray() {
            var snapshots = new NewPlayerSnapshot[16];
            for (int i = 0; i < snapshots.Length; i++)
                snapshots[i] = new NewPlayerSnapshot();
            return snapshots;
        }

        internal PlayerSnapshotToClientMessage ToOldSnapshotMessage() {
            var old_snapshots = new PlayerSnapshot[m_snapshots.Length];
            for (int i = 0; i < m_snapshots.Length; i++)
                old_snapshots[i] = m_snapshots[i].ToOldSnapshot();
            return new PlayerSnapshotToClientMessage() {
                m_num_snapshots = this.m_num_snapshots,
                m_snapshots = old_snapshots
            };
        }

        /// <summary>
        /// Create a new player snapshot message from an old player snapshot message.
        /// </summary>
        /// <param name="v"></param>
        /*public static explicit operator NewPlayerSnapshotToClientMessage(PlayerSnapshotToClientMessage v) {
            return new NewPlayerSnapshotToClientMessage {
                m_num_snapshots = v.m_num_snapshots,
                //m_snapshots = v.m_snapshots
            };
        }

        /// <summary>
        /// Create an old player snapshot message from a new player snapshot message.
        /// </summary>
        /// <returns></returns>
        public PlayerSnapshotToClientMessage ToPlayerSnapshotToClientMessage() {
            return new PlayerSnapshotToClientMessage {
                m_num_snapshots = m_num_snapshots,
                //m_snapshots = m_snapshots
            };
        }*/
    }

    /// <summary>
    /// Handle the new player snapshot message.
    /// </summary>
    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    public class MPNoPositionCompression_RegisterHandlers {

        // when using the new protocol, this object replaces
        // Client.m_PendingPlayerSnapshotMessages
        //public static Queue<NewPlayerSnapshotToClientMessage> m_PendingPlayerSnapshotMessages = new Queue<NewPlayerSnapshotToClientMessage>();

        public static void OnNewPlayerSnapshotToClient(NetworkMessage msg) {
            if (NetworkMatch.GetMatchState() == MatchState.PREGAME || NetworkMatch.InGameplay()) {
                NewPlayerSnapshotToClientMessage item = msg.ReadMessage<NewPlayerSnapshotToClientMessage>();
                MPClientShipReckoning.AddNewPlayerSnapshot(item, MPNoPositionCompression.NewSnapshotVersion);
            }
        }

        private static void Postfix() {
            if (Client.GetClient() == null) {
                return;
            }

            Client.GetClient().RegisterHandler(MessageTypes.MsgNewPlayerSnapshotToClient,
                                               OnNewPlayerSnapshotToClient);
        }
    }

    /// <summary>
    /// Upgrade old snapshots to new ones.
    /// </summary>
    [HarmonyPatch(typeof(Client), "OnPlayerSnapshotToClient")]
    public class MPNoPositionCompression_OnPlayerSnapshotToClient {
        public static bool Prefix(NetworkMessage msg) {
            if (NetworkMatch.GetMatchState() == MatchState.PREGAME || NetworkMatch.InGameplay()) {
                PlayerSnapshotToClientMessage item = msg.ReadMessage<PlayerSnapshotToClientMessage>();
                NewPlayerSnapshotToClientMessage newItem = new NewPlayerSnapshotToClientMessage {
                    m_num_snapshots = item.m_num_snapshots,
                    m_server_timestamp = 0, // Unused.
                    m_snapshots = item.m_snapshots.Select(m => NewPlayerSnapshot.FromOldSnapshot(m)).ToArray()
                };
                MPClientShipReckoning.AddNewPlayerSnapshot(newItem, MPNoPositionCompression.SnapshotVersion.VANILLA);
            }
            return false;
        }
    }

    /*
    // -------- get rid of this whole thing, see below
    /// <summary>
    /// 
    /// </summary>
    [HarmonyPatch(typeof(Server), "SendSnapshotsToPlayer")]
    public class MPNoPositionCompression_SendSnapshotsToPlayer{
        private static bool displayed = false;

        public static NewPlayerSnapshotToClientMessage m_snapshot_buffer = new NewPlayerSnapshotToClientMessage();
        public static bool Prefix(Player send_to_player){
            if (!displayed) { displayed = true; Debug.Log("--CCF WARNING SendSnapshotsToPlayer GOT CALLED SOMEHOW - it's not supposed to anymore"); }
            m_snapshot_buffer.m_num_snapshots = 0;
            foreach (Player player in Overload.NetworkManager.m_Players)
            {
                if (!(player == null) && !player.m_spectator && !(player == send_to_player))
                {
                    NewPlayerSnapshot playerSnapshot = m_snapshot_buffer.m_snapshots[m_snapshot_buffer.m_num_snapshots++];
                    playerSnapshot.m_net_id = player.netId;
                    playerSnapshot.m_pos = player.transform.position;
                    playerSnapshot.m_rot = player.transform.rotation;
                    playerSnapshot.m_vel = player.c_player_ship.c_rigidbody.velocity;
                    playerSnapshot.m_vrot = player.c_player_ship.c_rigidbody.angularVelocity;
                }
            }
            if (m_snapshot_buffer.m_num_snapshots > 0)
            {
                if (!MPNoPositionCompression.enabled || !MPTweaks.ClientHasTweak(send_to_player.connectionToClient.connectionId, "nocompress_0_3_6")) {
                    send_to_player.connectionToClient.SendByChannel(64, m_snapshot_buffer.ToOldSnapshotMessage(), 1);
                    return false;
                }

                send_to_player.connectionToClient.SendByChannel(MessageTypes.MsgNewPlayerSnapshotToClient, m_snapshot_buffer, 1);
            }
            return false;
        }
    }
    // -------- ^^ get rid of this whole thing, see below
    */

    // might as well just generate the snapshot list once since there's already checks in place in olmod and in stock to not apply a snapshot to the local player
    [HarmonyPatch(typeof(Server), "SendSnapshotsToPlayers")]
    public class MPNoPositionCompression_SendSnapshotsToPlayers
    {
        public static NewPlayerSnapshotToClientMessage m_snapshot_buffer = new NewPlayerSnapshotToClientMessage();

        public static Dictionary<NetworkInstanceId, int> playerMissingTicks = new Dictionary<NetworkInstanceId, int>(16); // keeps track of how many ticks in a row have been missed (and therefore extrapolated) for a player

        public static bool Prefix()
        {
            m_snapshot_buffer.m_num_snapshots = 0;

            // build the snapshot message
            foreach (Player player in Overload.NetworkManager.m_Players)
            {
                if (!(player == null) && !player.m_spectator)
                {
                    NewPlayerSnapshot playerSnapshot = m_snapshot_buffer.m_snapshots[m_snapshot_buffer.m_num_snapshots++];
                    playerSnapshot.m_net_id = player.netId;
                    playerSnapshot.m_vel = player.c_player_ship.c_rigidbody.velocity;
                    playerSnapshot.m_vrot = player.c_player_ship.c_rigidbody.angularVelocity;

                    int spawndelay;
                    MPServerOptimization.playerRespawnDelay.TryGetValue(playerSnapshot.m_net_id, out spawndelay);

                    // Moved to MPServerOptimization in ProcessCachedControlsRemote so we have the spawndelay counter available earlier in the loop
                    /*
                    if ((spawndelay == 0 || !player.m_server_input_primed) && (player.c_player_ship.m_dead || player.c_player_ship.m_dying || player.m_server_tick < 0)) // set a delay if dead/respawning and hold it at 120 while in that state
                    {
                        spawndelay = 60;
                    }
                    */

                    if (player.m_send_updated_state || spawndelay > 0)
                    {
                        playerSnapshot.m_pos = player.transform.position;
                        playerSnapshot.m_rot = player.transform.rotation;
                        playerMissingTicks.Remove(playerSnapshot.m_net_id);
                        MPServerOptimization.playerRespawnDelay[playerSnapshot.m_net_id] = Mathf.Max(--spawndelay, 0); // this gets reset to 120 on the next frame if still dead, otherwise it continues to count down
                    }
                    else // extrapolate the movement 1 frame forward so it doesn't just freeze in the case of a starved buffer or a network hitch
                    {
                        MPServerOptimization.playerRespawnDelay.Remove(playerSnapshot.m_net_id); // remove the counter for this player
                        int numticks;
                        playerMissingTicks.TryGetValue(playerSnapshot.m_net_id, out numticks);
                        numticks = Mathf.Min(++numticks, 12); // if we keep growing extrapolation some -weird- stuff starts happening, cap at 200ms worth of movement
                        playerMissingTicks[playerSnapshot.m_net_id] = numticks;
                        //Debug.Log("==CCF EXTRAPOLATING FRAME for " + player.m_mp_name + " on player tick " + player.m_server_tick + " -- extrapolating " + numticks + " ticks==");
                        playerSnapshot.m_pos = Vector3.LerpUnclamped(player.transform.position, player.transform.position + playerSnapshot.m_vel, Time.fixedDeltaTime * numticks);
                        playerSnapshot.m_rot = Quaternion.Euler(playerSnapshot.m_vrot * Mathf.Rad2Deg * Time.fixedDeltaTime * numticks) * player.transform.rotation; // TODO - project off the previous fake snapshot for some semblance of predicted vector? Does it even matter? If we're relying on this, there's probably a better solution.
                    }
                }
            }

            // send the right version to everyone
            if (m_snapshot_buffer.m_num_snapshots > 0)
            {
                foreach (Player send_to_player in Overload.NetworkManager.m_Players)
                {
                    if (!(send_to_player == null) && !send_to_player.isLocalPlayer)
                    {
                        //SendSnapshotsToPlayer(player);
                        if (m_snapshot_buffer.m_num_snapshots > 0)
                        {
                            if (!MPNoPositionCompression.enabled || !MPTweaks.ClientHasTweak(send_to_player.connectionToClient.connectionId, "nocompress_0_3_6"))
                            {
                                send_to_player.connectionToClient.SendByChannel(64, m_snapshot_buffer.ToOldSnapshotMessage(), 1);
                            }
                            else
                            {
                                send_to_player.connectionToClient.SendByChannel(MessageTypes.MsgNewPlayerSnapshotToClient, m_snapshot_buffer, 1);
                            }
                        }
                    }
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Server), "SendUpdatedStateToPlayers")]
    public class MPNoPositionCompression_SendUpdatedStateToPlayers
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
        {
            bool skip = false;

            foreach (var code in codes)
            {
                if (!skip)
                {
                    yield return code;
                }

                if (code.opcode == OpCodes.Stfld && code.operand == AccessTools.Field(typeof(Player), "m_LastPlayerStateSent"))
                {
                    skip = true;
                }
                else if (code.opcode == OpCodes.Stfld && code.operand == AccessTools.Field(typeof(Player), "m_send_updated_state")) // check removed here as we'll be clearing the bool at the start of the next cycle instead -- see MPServerOptimization_ProcessCachedControlsRemote
                {
                    skip = false;
                }
            }
        }
    }
}
