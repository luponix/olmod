using System.Collections.Generic;
using HarmonyLib;
using Overload;
using GameMod.Bots;
using GameMod.Logging;
using GameMod.Perf;

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

    [HarmonyPatch(typeof(GameManager), "Update")]
    class GameManager_Update
    {
        static void Prefix()
        {
            FrameIntervals.MarkUpdate();
        }

        static void Postfix()
        {
            RenderExperiments.Tick();
            FramePacer.StartOnce();
        }
    }

    [HarmonyPatch(typeof(GameManager), "UpdateTargetFramerate")]
    class GameManager_UpdateTargetFramerate
    {
        static bool Prefix()
        {
            FramePacer.ApplyTargetFramerate();
            return false;
        }
    }

    [HarmonyPatch(typeof(GameManager), "LateUpdate")]
    class GameManager_LateUpdate
    {
        static void Postfix()
        {
            RearView.FlushImageLayer();
            ImpactMarks.Draw();
            MapBake.Tick();
            Hosting.Tick();
            MapView.Draw();
            ShipParticleCull.Tick();
            MenuDriver.Tick();
        }
    }
}
