using System.Collections.Generic;
using HarmonyLib;
using Overload;
using GameMod.Logging;

namespace GameMod.Hooks
{
    [HarmonyPatch(typeof(GameManager), "ScanForLevels")]
    class GameManager_ScanForLevels
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return LevelScanLog.ReplaceLogging(instructions);
        }

        static void Prefix()
        {
            LevelScanLog.BeginFolder();
        }

        static void Postfix(string folder_path)
        {
            LevelScanLog.ReportFolder(folder_path);
        }
    }
}
