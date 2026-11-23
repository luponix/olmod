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
        const float ONLY_BOTS_GRACE = 30f;

        static List<BotSlot> bots = new List<BotSlot>();
        static readonly Dictionary<int, BotSlot> botsByConnection = new Dictionary<int, BotSlot>();
        static bool replaceWithHumans;
        static string connectAddress;
        static int hostMaxPlayers;
        static readonly Dictionary<Process, List<BotSlot>> olbots = new Dictionary<Process, List<BotSlot>>();
        static List<BotSlot> countedBots = new List<BotSlot>();
        static int processesStarted;
        static string botCountPath;
        static int botCount = -1;
        static string notice;
        static float nextCheck;
        static float onlyBotsSince = -1f;
        static bool endedForOnlyBots;

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
            hostMaxPlayers = NetworkMatch.GetMaxPlayersForMatch();
            List<BotSlot> requested = MPModPrivateData.BotSettings.Slots;
            if (!GameplayManager.IsDedicatedServer() || !MPModPrivateData.BotSettings.Enabled || requested.Count == 0)
                return;
            string levelKey = MPJoinInProgress.NetworkMatchLevelName();
            notice = WhyBotsCannotStart(levelKey);
            if (notice != null)
                return;
            int allowed = Math.Max(0, Math.Min(ServerMaxBots, hostMaxPlayers - 1));
            if (requested.Count > allowed)
                notice = string.Format("THIS MATCH ALLOWS {0} OF THE {1} BOTS", allowed, requested.Count);
            replaceWithHumans = MPModPrivateData.BotSettings.ReplaceWithHumans;
            List<BotSlot> slots = requested.Take(allowed).Select(slot => slot.Resolve()).ToList();
            if (slots.Count > 0 && !StartOlbot(slots, levelKey, replaceWithHumans))
                notice = "THE BOTS COULD NOT START ON THIS SERVER";
        }

        static string WhyBotsCannotStart(string levelKey)
        {
            if (Core.GameMod.FindArg("-no-bots"))
                return "THIS SERVER DOES NOT RUN BOTS";
            if (!OlbotInstaller.Ensure())
                return OlbotInstaller.Failure;
            if (connectAddress == null)
                return "THE BOTS CANNOT FIND THE PASSWORD OF THIS MATCH";
            LevelCapture.RenameLegacyFiles();
            string failure = string.IsNullOrEmpty(levelKey) ? null : LevelCapture.FailureOf(levelKey);
            if (failure == null)
                return null;
            UnityEngine.Debug.LogFormat("[Bots] no bots on {0}, its map data failed before: {1}", levelKey, failure);
            return "THE BOTS CANNOT PLAY THIS MAP YET";
        }

        static bool StartOlbot(List<BotSlot> slots, string levelKey, bool countBots)
        {
            string logDir = Path.Combine(Application.persistentDataPath, "BotLogs");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "server-bots" + LevelCapture.InstanceTag + (processesStarted == 0 ? string.Empty : "-" + processesStarted) + ".log");
            var arguments = new List<string> {
                "-batchmode", "-nographics",
                "-logFile", logPath,
                "-olbot-connect", connectAddress,
                "-olbot-brain", "-olbot-policy", "pilot",
                "-olbot-bots", string.Join(",", slots.Select(s => s.PilotName + ":" + s.TierFile + ":" + s.LoadoutSpec).ToArray()),
                "-olbot-parent", Process.GetCurrentProcess().Id.ToString(),
                "-olbot-geometry-cache", LevelCapture.CacheDir,
            };
            if (countBots)
            {
                botCountPath = Path.Combine(logDir, "server-bot-count" + LevelCapture.InstanceTag + ".txt");
                WriteBotCount(slots.Count);
                arguments.AddRange(new[] { "-olbot-bot-count-file", botCountPath });
                countedBots = slots;
            }
            Process process;
            try
            {
                process = Olbot.Start(arguments);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogFormat("[Bots] cannot start {0}: {1}", Olbot.PlayerPath, ex.Message);
                return false;
            }
            processesStarted++;
            olbots[process] = slots;
            bots.AddRange(slots);
            UnityEngine.Debug.LogFormat("[Bots] started {0} bots, level {1}, humans replace bots {2}, log {3}", slots.Count, levelKey, countBots, logPath);
            return true;
        }

        public static string Add(BotSlot request, out BotSlot added)
        {
            added = null;
            if (!GameplayManager.IsDedicatedServer())
                return "ONLY A DEDICATED SERVER RUNS BOTS";
            MatchState state = NetworkMatch.GetMatchState();
            bool inLobby = state == MatchState.LOBBY || state == MatchState.LOBBY_LOAD_COUNTDOWN;
            if (!inLobby && state != MatchState.PLAYING)
                return "ADD BOTS IN THE LOBBY OR DURING THE MATCH";
            if (!inLobby && !MPJoinInProgress.NetworkMatchEnabled)
                return "THIS MATCH DOES NOT LET PLAYERS JOIN DURING THE MATCH";
            string levelKey = CurrentLevelKey();
            string reason = WhyBotsCannotStart(levelKey);
            if (reason != null)
                return reason;
            StopReplacingWithHumans();
            int joining = bots.Count(s => !botsByConnection.ContainsValue(s));
            if (bots.Count >= ServerMaxBots)
                return string.Format("THIS SERVER ALLOWS {0} BOTS", ServerMaxBots);
            if (NetworkMatch.m_players.Count + joining >= NetworkMatch.GetMaxPlayersForMatch())
                return "THE MATCH IS FULL";
            BotSlot slot = request.Resolve();
            if (string.IsNullOrEmpty(slot.Name))
                slot.Name = FreeName();
            else if (IsNameTaken(slot.PilotName))
                return "A PLAYER WITH THE NAME " + slot.PilotName + " IS IN THE MATCH";
            if (!StartOlbot(new List<BotSlot> { slot }, levelKey, false))
                return "THE BOT COULD NOT START ON THIS SERVER";
            added = slot;
            return null;
        }

        public static bool IsBot(int connectionId)
        {
            return botsByConnection.ContainsKey(connectionId);
        }

        public static void StopReplacingWithHumans()
        {
            if (!replaceWithHumans)
                return;
            replaceWithHumans = false;
            maxPlayersField.SetValue(null, hostMaxPlayers);
            bots.RemoveAll(s => !botsByConnection.ContainsValue(s));
            WriteBotCount(countedBots.Count(s => botsByConnection.ContainsValue(s)));
            UnityEngine.Debug.Log("[Bots] bots were added or kicked by hand, humans no longer replace bots in this match");
        }

        static string FreeName()
        {
            for (int number = 1; ; number++)
                if (!bots.Exists(s => s.Name == number.ToString()))
                    return number.ToString();
        }

        static bool IsNameTaken(string pilotName)
        {
            return bots.Exists(s => string.Equals(s.PilotName, pilotName, StringComparison.OrdinalIgnoreCase))
                || NetworkMatch.m_players.Values.Any(p => string.Equals(p.m_name, pilotName, StringComparison.OrdinalIgnoreCase));
        }

        static string CurrentLevelKey()
        {
            return GameplayManager.Level != null ? LevelCapture.LevelKey(GameplayManager.Level) : MPJoinInProgress.NetworkMatchLevelName();
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
            if (HasFixedTeam(slot))
                lobbyData.m_team = MPTeams.AllTeams[slot.Team];
            MPLoadouts.NetworkLoadouts[connectionId] = new MPLoadouts.LoadoutDataMessage {
                lobby_id = connectionId,
                loadouts = new List<MPLoadouts.CustomLoadout> {
                    new MPLoadouts.CustomLoadout { weapons = slot.Primaries, missiles = slot.Secondaries }
                }
            };
            UnityEngine.Debug.LogFormat("[Bots] {0} joined on connection {1}, team {2}", slot.PilotName, connectionId, lobbyData.m_team);
        }

        public static bool KeepsLobbyTeam(int connectionId)
        {
            BotSlot slot;
            return botsByConnection.TryGetValue(connectionId, out slot) && HasFixedTeam(slot);
        }

        static bool HasFixedTeam(BotSlot slot)
        {
            return slot.Team >= 0 && NetworkMatch.IsTeamMode(NetworkMatch.GetMode()) && slot.Team < MPTeams.NetworkMatchTeamCount;
        }

        public static void OnDisconnect(int connectionId)
        {
            BotSlot slot;
            if (!botsByConnection.TryGetValue(connectionId, out slot))
                return;
            botsByConnection.Remove(connectionId);
            if (replaceWithHumans)
                return;
            bots.Remove(slot);
            if (countedBots.Remove(slot))
                WriteBotCount(botCount - 1);
        }

        public static void Tick()
        {
            if ((bots.Count == 0 && notice == null) || Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + 1f;
            foreach (Process process in olbots.Keys.Where(p => p.HasExited).ToList())
                OnOlbotExited(process);
            int humans = NetworkMatch.m_players.Keys.Count(id => !botsByConnection.ContainsKey(id));
            EndIfOnlyBots(humans);
            if (notice != null && humans > 0)
            {
                MPChatTools.SendTo(notice);
                UnityEngine.Debug.LogFormat("[Bots] told the players: {0}", notice);
                notice = null;
            }
            InputStats.Report();
            if (!replaceWithHumans || olbots.Count == 0)
                return;
            WriteBotCount(Mathf.Clamp(bots.Count + 1 - humans, 0, bots.Count));
            maxPlayersField.SetValue(null, hostMaxPlayers + (botsByConnection.Count > 0 ? 1 : 0));
        }

        static void EndIfOnlyBots(int humans)
        {
            if (humans > 0 || botsByConnection.Count == 0)
            {
                onlyBotsSince = -1f;
                return;
            }
            if (onlyBotsSince < 0f)
                onlyBotsSince = Time.unscaledTime;
            MatchState state = NetworkMatch.GetMatchState();
            if (endedForOnlyBots || Time.unscaledTime - onlyBotsSince < ONLY_BOTS_GRACE || state == MatchState.NONE || state >= MatchState.POSTGAME)
                return;
            endedForOnlyBots = true;
            UnityEngine.Debug.LogFormat("[Bots] only bots are left after {0} s, ending the match in state {1}", ONLY_BOTS_GRACE, state);
            NetworkMatch.End();
        }

        static void OnOlbotExited(Process process)
        {
            List<BotSlot> slots = olbots[process];
            olbots.Remove(process);
            process.Dispose();
            bots.RemoveAll(s => slots.Contains(s) && !botsByConnection.ContainsValue(s));
            string levelKey = CurrentLevelKey();
            string failure = string.IsNullOrEmpty(levelKey) ? null : LevelCapture.FailureOf(levelKey);
            UnityEngine.Debug.LogFormat("[Bots] the bot process ended, level {0}{1}", levelKey, failure == null ? string.Empty : ", map data failed: " + failure);
            if (failure != null)
                notice = "THE BOTS CANNOT PLAY THIS MAP YET";
        }

        static void WriteBotCount(int count)
        {
            if (count == botCount || botCountPath == null)
                return;
            if (!LevelCapture.WriteAtomically(botCountPath, System.Text.Encoding.ASCII.GetBytes(count.ToString())))
                return;
            botCount = count;
            UnityEngine.Debug.LogFormat("[Bots] bot count {0}", count);
        }

        static void Stop()
        {
            foreach (Process process in olbots.Keys)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogFormat("[Bots] cannot stop the bot process: {0}", ex.Message);
                }
                process.Dispose();
            }
            olbots.Clear();
            processesStarted = 0;
            bots = new List<BotSlot>();
            countedBots = new List<BotSlot>();
            botsByConnection.Clear();
            replaceWithHumans = false;
            botCountPath = null;
            botCount = -1;
            notice = null;
            onlyBotsSince = -1f;
            endedForOnlyBots = false;
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
            if (bots.Count == 0 || olbots.Count == 0 || pregameWaitField == null)
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
