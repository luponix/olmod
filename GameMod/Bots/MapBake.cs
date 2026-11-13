using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod.Bots
{
    static class MapBake
    {
        enum Step { Idle, Launch, Loading, Leave, Baking }

        class PendingLevel
        {
            public int Index;
            public string Key;
        }

        static Step step = Step.Idle;
        static readonly Queue<PendingLevel> queue = new Queue<PendingLevel>();
        static PendingLevel current;
        static float levelStarted;
        static float leaveAt;
        static float runStarted;
        static int levelsDone;
        static int levelsTotal;
        static Process baker;
        static string bakeLogPath;

        static string LoadingPath
        {
            get { return Path.Combine(LevelCapture.CacheDir, "botmaps-loading.txt"); }
        }

        static string SkipListPath
        {
            get { return Path.Combine(LevelCapture.CacheDir, "botmaps-skip.txt"); }
        }

        public static bool IsRunning
        {
            get { return step != Step.Idle; }
        }

        static void CmdBotMaps()
        {
            if (IsRunning)
            {
                Report("botmaps: already running");
                return;
            }
            if (MenuManager.m_menu_state != MenuState.MAIN_MENU)
            {
                Report("botmaps: start it from the main menu");
                return;
            }
            LevelCapture.RenameLegacyFiles();
            var skipped = new HashSet<string>();
            if (File.Exists(SkipListPath))
                skipped.UnionWith(File.ReadAllLines(SkipListPath));
            if (File.Exists(LoadingPath))
            {
                string hung = File.ReadAllText(LoadingPath).Trim();
                File.AppendAllText(SkipListPath, hung + "\n");
                File.Delete(LoadingPath);
                skipped.Add(hung);
                Report("botmaps: the last run stopped at " + hung + ", it is skipped from now on (" + SkipListPath + ")");
            }
            var filters = new List<string>();
            for (int i = uConsole.GetNumParameters(); i > 0; i--)
                filters.Add(uConsole.GetString().ToLowerInvariant());

            Mission mission = GameManager.MultiplayerMission;
            int matching = 0;
            queue.Clear();
            for (int index = 0; index < mission.NumLevels; index++)
            {
                string key = mission.IsLevelAnAddon(index) ? mission.GetAddOnLevelIdStringHash(index) : mission.GetLevelFileName(index);
                if (string.IsNullOrEmpty(key) || (filters.Count > 0 && !filters.Exists(f => key.ToLowerInvariant().Contains(f))))
                    continue;
                matching++;
                if (!skipped.Contains(key) && !LevelCapture.IsBaked(key) && !File.Exists(LevelCapture.CapturePath(key)))
                    queue.Enqueue(new PendingLevel { Index = index, Key = key });
            }
            levelsTotal = queue.Count;
            levelsDone = 0;
            Report(string.Format("botmaps: {0} of {1} levels have no map data", levelsTotal, matching));
            Directory.CreateDirectory(LevelCapture.CacheDir);
            runStarted = Time.realtimeSinceStartup;
            step = Step.Launch;
        }

        public static void Tick()
        {
            switch (step)
            {
                case Step.Launch:
                    if (MenuManager.m_menu_state != MenuState.MAIN_MENU || MenuManager.m_menu_sub_state != MenuSubState.ACTIVE)
                        return;
                    if (queue.Count == 0)
                    {
                        File.Delete(LoadingPath);
                        StartBake();
                        return;
                    }
                    current = queue.Dequeue();
                    File.WriteAllText(LoadingPath, current.Key);
                    levelStarted = Time.realtimeSinceStartup;
                    step = Step.Loading;
                    GameplayManager.CreateNewGame(GameManager.MultiplayerMission, current.Index);
                    GameplayManager.m_game_type = GameType.MISSION;
                    GameplayManager.SwitchToMenu(MenuState.PLAY_GAME);
                    return;
                case Step.Loading:
                    if (Time.realtimeSinceStartup - levelStarted > 180f)
                    {
                        Report(string.Format("botmaps: {0} did not load in 180 s, skipped", current.Key));
                        step = Step.Leave;
                    }
                    return;
                case Step.Leave:
                    if (GameplayManager.m_gameplay_state != GameplayState.PLAYING && Time.realtimeSinceStartup - levelStarted < 180f)
                        return;
                    if (leaveAt == 0f)
                        leaveAt = Time.realtimeSinceStartup + 2f;
                    if (Time.realtimeSinceStartup < leaveAt)
                        return;
                    leaveAt = 0f;
                    GameplayManager.DoneLevel(GameplayManager.DoneReason.Quit);
                    GameplayManager.SwitchToMenu(MenuState.MAIN_MENU);
                    step = Step.Launch;
                    return;
                case Step.Baking:
                    if (baker.HasExited)
                        FinishBake();
                    return;
            }
        }

        public static void OnLevelLoaded()
        {
            if (step != Step.Loading)
                return;
            float loadSeconds = Time.realtimeSinceStartup - levelStarted;
            var watch = Stopwatch.StartNew();
            string key = LevelCapture.LevelKey(GameplayManager.Level);
            byte[] capture = LevelCapture.CaptureLoadedLevel(key);
            LevelCapture.WriteAtomically(LevelCapture.CapturePath(key), capture);
            levelsDone++;
            Report(string.Format("botmaps: {0}/{1} {2}: load {3:0.0} s, capture {4} ms, {5:0.0} MB",
                levelsDone, levelsTotal, key, loadSeconds, watch.ElapsedMilliseconds, capture.Length / 1048576f));
            step = Step.Leave;
        }

        static void StartBake()
        {
            Report(string.Format("botmaps: captured {0} levels in {1:0} s, baking", levelsDone, Time.realtimeSinceStartup - runStarted));
            if (!File.Exists(Olbot.PlayerPath))
            {
                Report("botmaps: no olbot at " + Olbot.PlayerPath + ", captures stay unbaked");
                step = Step.Idle;
                return;
            }
            string logDir = Path.Combine(Application.persistentDataPath, "BotLogs");
            Directory.CreateDirectory(logDir);
            bakeLogPath = Path.Combine(logDir, "bake.log");
            try
            {
                baker = Olbot.Start(new List<string> {
                    "-batchmode", "-nographics", "-logFile", bakeLogPath, "-olbot-bake-all", LevelCapture.CacheDir });
            }
            catch (Exception ex)
            {
                Report("botmaps: cannot start " + Olbot.PlayerPath + ": " + ex.Message);
                step = Step.Idle;
                return;
            }
            step = Step.Baking;
        }

        static void FinishBake()
        {
            baker.Dispose();
            baker = null;
            step = Step.Idle;
            foreach (string line in File.ReadAllLines(bakeLogPath))
                if (line.StartsWith("[Olbot] baked") || line.StartsWith("[Olbot] cannot bake"))
                    Report("botmaps: " + line.Substring("[Olbot] ".Length));
            Report(string.Format("botmaps: done in {0:0} s, olbot log {1}", Time.realtimeSinceStartup - runStarted, bakeLogPath));
        }

        static void Report(string line)
        {
            uConsole.Log(line);
            UnityEngine.Debug.Log(line);
        }

        [HarmonyPatch(typeof(Console), "RegisterCommands")]
        class MapBake_RegisterCommands
        {
            static void Postfix()
            {
                uConsole.RegisterCommand("botmaps", "Capture and bake bot map data for every MP level that has none (optional name filters)",
                    new uConsole.DebugCommand(CmdBotMaps));
            }
        }
    }
}
