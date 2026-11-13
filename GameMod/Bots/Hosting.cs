using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace GameMod.Bots
{
    static class Hosting
    {
        static readonly FieldInfo maxPlayersField = AccessTools.Field(typeof(NetworkMatch), "m_max_players_for_match");
        static readonly FieldInfo pregameWaitField = AccessTools.Field(typeof(NetworkMatch), "m_pregame_elapsed_seconds_waiting_before_countdown");
        const float ENGINE_PREGAME_WAIT = 60f;
        const float BOT_PREGAME_WAIT = 150f;

        static List<BotSlot> bots = new List<BotSlot>();
        static readonly Dictionary<int, BotSlot> botsByConnection = new Dictionary<int, BotSlot>();
        static bool replaceWithHumans;
        static string connectAddress;
        static int hostMaxPlayers;
        static Process olbot;
        static string botCountPath;
        static int botCount = -1;
        static string notice;
        static float nextCheck;

        public static bool HasBots
        {
            get { return bots.Count > 0; }
        }

        static int ServerMaxBots
        {
            get
            {
                string text;
                int count;
                return Core.GameMod.FindArgVal("-max-bots", out text) && int.TryParse(text, out count) ? Math.Max(0, count) : 8;
            }
        }

        public static void ReadPassword(Dictionary<string, object> attributes)
        {
            connectAddress = null;
            object value;
            if (!attributes.TryGetValue("password", out value))
                return;
            var list = value as string[];
            string password = list != null && list.Length > 0 ? list[0] : value as string;
            if (password == null)
                return;
            int separator = password.IndexOf('_');
            connectAddress = "127.0.0.1" + (separator < 0 ? string.Empty : password.Substring(separator));
        }

        public static void OnGameSessionStart()
        {
            Stop();
            List<BotSlot> requested = MPModPrivateData.BotSettings.Slots;
            if (!GameplayManager.IsDedicatedServer() || !MPModPrivateData.BotSettings.Enabled || requested.Count == 0)
                return;
            if (Core.GameMod.FindArg("-no-bots"))
            {
                notice = "THIS SERVER DOES NOT RUN BOTS";
                return;
            }
            if (!OlbotInstaller.Ensure())
            {
                notice = OlbotInstaller.Failure;
                return;
            }
            if (connectAddress == null)
                return;
            hostMaxPlayers = NetworkMatch.GetMaxPlayersForMatch();
            int allowed = Math.Max(0, Math.Min(ServerMaxBots, hostMaxPlayers - 1));
            if (requested.Count > allowed)
                notice = string.Format("THIS MATCH ALLOWS {0} OF THE {1} BOTS", allowed, requested.Count);
            bots = requested.Take(allowed).Select(slot => slot.Resolve()).ToList();
            replaceWithHumans = MPModPrivateData.BotSettings.ReplaceWithHumans;
            if (bots.Count == 0)
                return;
            LevelCapture.RenameLegacyFiles();
            try
            {
                StartOlbot();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogFormat("[Bots] cannot start {0}: {1}", Olbot.PlayerPath, ex.Message);
                notice = "THE BOTS COULD NOT START ON THIS SERVER";
                olbot = null;
            }
        }

        static void StartOlbot()
        {
            string logDir = Path.Combine(Application.persistentDataPath, "BotLogs");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "server-bots" + LevelCapture.InstanceTag + ".log");
            var arguments = new List<string> {
                "-batchmode", "-nographics",
                "-logFile", logPath,
                "-olbot-connect", connectAddress,
                "-olbot-brain", "-olbot-policy", "pilot",
                "-olbot-bots", string.Join(",", bots.Select(s => s.PilotName + ":" + s.TierFile + ":" + s.LoadoutSpec).ToArray()),
                "-olbot-parent", Process.GetCurrentProcess().Id.ToString(),
                "-olbot-geometry-cache", LevelCapture.CacheDir,
            };
            string levelKey = MPJoinInProgress.NetworkMatchLevelName();
            if (replaceWithHumans)
            {
                botCountPath = Path.Combine(logDir, "server-bot-count" + LevelCapture.InstanceTag + ".txt");
                WriteBotCount(bots.Count);
                arguments.AddRange(new[] { "-olbot-bot-count-file", botCountPath });
            }
            olbot = Olbot.Start(arguments);
            UnityEngine.Debug.LogFormat("[Bots] started {0} bots, level {1}, humans replace bots {2}, log {3}", bots.Count, levelKey, replaceWithHumans, logPath);
        }

        public static void OnPlayerAddedToLobby(int connectionId, string name, PlayerLobbyData lobbyData)
        {
            if (lobbyData == null || !IsLoopback(connectionId))
                return;
            Func<BotSlot, bool> named = s => string.Equals(s.PilotName, name.Trim(), StringComparison.OrdinalIgnoreCase);
            BotSlot slot = bots.FirstOrDefault(s => named(s) && !botsByConnection.ContainsValue(s)) ?? bots.FirstOrDefault(named);
            if (slot == null)
                return;
            botsByConnection[connectionId] = slot;
            if (slot.Team >= 0 && NetworkMatch.IsTeamMode(NetworkMatch.GetMode()) && slot.Team < MPTeams.NetworkMatchTeamCount)
                lobbyData.m_team = MPTeams.AllTeams[slot.Team];
            MPLoadouts.StoreNetworkLoadout(new MPLoadouts.LoadoutDataMessage {
                lobby_id = connectionId,
                loadouts = new List<MPLoadouts.CustomLoadout> {
                    new MPLoadouts.CustomLoadout { weapons = slot.Primaries, missiles = slot.Secondaries }
                }
            });
            UnityEngine.Debug.LogFormat("[Bots] {0} joined on connection {1}, team {2}", slot.PilotName, connectionId, lobbyData.m_team);
        }

        public static void OnDisconnect(int connectionId)
        {
            botsByConnection.Remove(connectionId);
        }

        public static void Tick()
        {
            if ((bots.Count == 0 && notice == null) || Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + 1f;
            if (olbot != null && olbot.HasExited)
                OnOlbotExited();
            int humans = NetworkMatch.m_players.Keys.Count(id => !botsByConnection.ContainsKey(id));
            if (notice != null && humans > 0)
            {
                MPChatTools.SendTo(notice);
                UnityEngine.Debug.LogFormat("[Bots] told the players: {0}", notice);
                notice = null;
            }
            InputStats.Report();
            if (!replaceWithHumans || olbot == null)
                return;
            WriteBotCount(Mathf.Clamp(bots.Count + 1 - humans, 0, bots.Count));
            maxPlayersField.SetValue(null, hostMaxPlayers + (botsByConnection.Count > 0 ? 1 : 0));
        }

        static void OnOlbotExited()
        {
            olbot.Dispose();
            olbot = null;
            string levelKey = GameplayManager.Level != null ? LevelCapture.LevelKey(GameplayManager.Level) : MPJoinInProgress.NetworkMatchLevelName();
            string failure = string.IsNullOrEmpty(levelKey) ? null : LevelCapture.FailureOf(levelKey);
            UnityEngine.Debug.LogFormat("[Bots] the bot process ended, level {0}{1}", levelKey, failure == null ? string.Empty : ", map data failed: " + failure);
            if (failure != null)
                notice = "THE BOTS CANNOT PLAY THIS MAP YET";
        }

        static void WriteBotCount(int count)
        {
            if (count == botCount)
                return;
            if (!LevelCapture.WriteAtomically(botCountPath, System.Text.Encoding.ASCII.GetBytes(count.ToString())))
                return;
            botCount = count;
            UnityEngine.Debug.LogFormat("[Bots] bot count {0}", count);
        }

        static void Stop()
        {
            if (olbot != null)
            {
                try
                {
                    if (!olbot.HasExited)
                    {
                        olbot.Kill();
                        olbot.WaitForExit(3000);
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogFormat("[Bots] cannot stop the bot process: {0}", ex.Message);
                }
                olbot.Dispose();
                olbot = null;
            }
            bots = new List<BotSlot>();
            botsByConnection.Clear();
            botCount = -1;
            notice = null;
        }

        static bool holdingCountdown;

        public static bool HoldCountdown()
        {
            bool hold = ShouldHoldCountdown();
            if (hold != holdingCountdown)
                UnityEngine.Debug.LogFormat(hold ? "[Bots] holding the countdown for the bots' map data" : "[Bots] countdown released");
            holdingCountdown = hold;
            return hold;
        }

        static bool ShouldHoldCountdown()
        {
            if (bots.Count == 0 || olbot == null || olbot.HasExited || pregameWaitField == null)
                return false;
            float waited = (float)pregameWaitField.GetValue(null);
            if (waited < ENGINE_PREGAME_WAIT || waited > BOT_PREGAME_WAIT || GameplayManager.Level == null)
                return false;
            string levelKey = LevelCapture.LevelKey(GameplayManager.Level);
            if (!LevelCapture.IsBaked(levelKey))
                return LevelCapture.FailureOf(levelKey) == null;
            return botsByConnection.Keys.Any(id => {
                Player player = Server.FindPlayerByConnectionId(id);
                return player == null || !player.m_ready_for_countdown;
            });
        }

        static bool IsLoopback(int connectionId)
        {
            NetworkConnection connection = connectionId >= 0 && connectionId < NetworkServer.connections.Count ? NetworkServer.connections[connectionId] : null;
            return connection != null && connection.address != null && connection.address.EndsWith("127.0.0.1");
        }
    }

    [HarmonyPatch(typeof(NetworkMatch), "NetSystemOnGameSessionStart")]
    class Hosting_NetworkMatch_NetSystemOnGameSessionStart
    {
        static void Postfix()
        {
            Hosting.OnGameSessionStart();
        }
    }

    [HarmonyPatch(typeof(NetworkMatch), "CanLaunchCountdown")]
    class Hosting_NetworkMatch_CanLaunchCountdown
    {
        static bool Prefix(ref bool __result)
        {
            if (!Hosting.HoldCountdown())
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(NetworkMatch), "AddPlayerToLobby")]
    class Hosting_NetworkMatch_AddPlayerToLobby
    {
        static void Postfix(int connection_id, string name, PlayerLobbyData __result)
        {
            Hosting.OnPlayerAddedToLobby(connection_id, name, __result);
        }
    }

    [HarmonyPatch(typeof(NetworkMatch), "RemoveConnectionId")]
    class Hosting_NetworkMatch_RemoveConnectionId
    {
        static void Prefix(int connection_id)
        {
            Hosting.OnDisconnect(connection_id);
        }
    }
}
