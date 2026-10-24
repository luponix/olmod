using System.Collections.Generic;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod.Logging
{
    // Engine lines that repeat without ever carrying new information.
    static class EngineLogNoise
    {
        static float reportedFov;

        // AdjustFOV runs on every Viewer.Start and every respawn, so the same values are logged
        // hundreds of times a session
        public static IEnumerable<CodeInstruction> ReplaceFovLogging(IEnumerable<CodeInstruction> instructions)
        {
            return LogRewrite.Replace(instructions,
                LogRewrite.Site("Setting FOV: Ratio: ", typeof(EngineLogNoise), nameof(ReportChangedFov)));
        }

        public static IEnumerable<CodeInstruction> ReplaceChatReturnLogging(IEnumerable<CodeInstruction> instructions)
        {
            return LogRewrite.Replace(instructions,
                LogRewrite.Site("Pressed return in chat message", typeof(LogRewrite), nameof(LogRewrite.DropMessage)));
        }

        public static IEnumerable<CodeInstruction> ReplaceCollisionLogging(IEnumerable<CodeInstruction> instructions)
        {
            return LogRewrite.Replace(instructions,
                LogRewrite.Site("Collision between player ship and level is", typeof(LogRewrite), nameof(LogRewrite.DropMessage)));
        }

        public static IEnumerable<CodeInstruction> ReplaceUpgradeLogging(IEnumerable<CodeInstruction> instructions)
        {
            return LogRewrite.Replace(instructions,
                LogRewrite.Site("Setting player upgrade data", typeof(LogRewrite), nameof(LogRewrite.DropMessage)));
        }

        static void ReportChangedFov(object message)
        {
            if (Viewer.SCREEN_FOV == reportedFov)
                return;
            reportedFov = Viewer.SCREEN_FOV;
            Debug.LogFormat("[FOV] {0}", message);
        }
    }
}
