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
            if (!File.Exists(Olbot.PlayerPath))
            {
                notice = "BOTS ARE NOT INSTALLED ON THIS SERVER";
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
            if (bots.Count > 0)
            {
                LevelCapture.RenameLegacyFiles();
                StartOlbot();
            }
        }

        static void StartOlbot()
        {
            string logDir = Path.Combine(Application.persistentDataPath, "BotLogs");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "server-bots.log");
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
            if (levelKey != null)
                arguments.AddRange(new[] { "-olbot-level", levelKey });
            if (replaceWithHumans)
            {
                botCountPath = Path.Combine(logDir, "server-bot-count.txt");
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
            BotSlot slot = bots.FirstOrDefault(s => string.Equals(s.PilotName, name.Trim(), StringComparison.OrdinalIgnoreCase));
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
            int humans = NetworkMatch.m_players.Keys.Count(id => !botsByConnection.ContainsKey(id));
            if (notice != null && humans > 0)
            {
                MPChatTools.SendTo(notice);
                UnityEngine.Debug.LogFormat("[Bots] told the players: {0}", notice);
                notice = null;
            }
            if (!replaceWithHumans || olbot == null)
                return;
            WriteBotCount(Mathf.Clamp(bots.Count + 1 - humans, 0, bots.Count));
            maxPlayersField.SetValue(null, hostMaxPlayers + (botsByConnection.Count > 0 ? 1 : 0));
        }

        static void WriteBotCount(int count)
        {
            if (count == botCount)
                return;
            botCount = count;
            LevelCapture.WriteAtomically(botCountPath, System.Text.Encoding.ASCII.GetBytes(count.ToString()));
            UnityEngine.Debug.LogFormat("[Bots] bot count {0}", count);
        }

        static void Stop()
        {
            if (olbot != null)
            {
                if (!olbot.HasExited)
                    olbot.Kill();
                olbot.Dispose();
                olbot = null;
            }
            bots = new List<BotSlot>();
            botsByConnection.Clear();
            botCount = -1;
            notice = null;
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
