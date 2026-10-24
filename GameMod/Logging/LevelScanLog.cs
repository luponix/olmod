using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace GameMod.Logging
{
    // GameManager.ScanForLevels logs every file it walks and every entry of every zip it opens,
    // which is ~8500 of the ~9500 lines in a player log. Count those instead and report one line
    // per folder.
    static class LevelScanLog
    {
        static int files;
        static int archiveEntries;
        static int missions;
        static int multiplayerLevels;
        static int challenges;

        public static IEnumerable<CodeInstruction> ReplaceLogging(IEnumerable<CodeInstruction> instructions)
        {
            return LogRewrite.Replace(instructions,
                LogRewrite.Site("Scanning user level folder", typeof(LogRewrite), nameof(LogRewrite.DropFormat)),
                LogRewrite.Site("... {0}", typeof(LevelScanLog), nameof(TallyFile)),
                LogRewrite.Site("...... {0}", typeof(LevelScanLog), nameof(TallyArchiveEntry)),
                LogRewrite.Site("    Adding as a mission", typeof(LevelScanLog), nameof(TallyMission)),
                LogRewrite.Site("       Adding as a mission", typeof(LevelScanLog), nameof(TallyMission)),
                LogRewrite.Site("    Adding as a multiplayer level", typeof(LevelScanLog), nameof(TallyMultiplayerLevel)),
                LogRewrite.Site("       Adding as a multiplayer level", typeof(LevelScanLog), nameof(TallyMultiplayerLevel)),
                LogRewrite.Site("    Adding as a challenge mode", typeof(LevelScanLog), nameof(TallyChallenge)),
                LogRewrite.Site("       Adding as a challenge mode", typeof(LevelScanLog), nameof(TallyChallenge)));
        }

        public static void BeginFolder()
        {
            files = archiveEntries = missions = multiplayerLevels = challenges = 0;
        }

        public static void ReportFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                return; // the engine already logged that it is missing
            Debug.LogFormat("[Levels] {0}: {1} missions, {2} multiplayer, {3} challenge from {4} files and {5} archive entries",
                folderPath, missions, multiplayerLevels, challenges, files, archiveEntries);
        }

        static void TallyFile(string format, params object[] args)
        {
            files++;
        }

        static void TallyArchiveEntry(string format, params object[] args)
        {
            archiveEntries++;
        }

        static void TallyMission(object message)
        {
            missions++;
        }

        static void TallyMultiplayerLevel(object message)
        {
            multiplayerLevels++;
        }

        static void TallyChallenge(object message)
        {
            challenges++;
        }
    }
}
