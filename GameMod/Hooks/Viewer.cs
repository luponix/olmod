using System.Collections.Generic;
using HarmonyLib;
using Overload;
using GameMod.Logging;
using GameMod.Perf;

namespace GameMod.Hooks
{
    [HarmonyPatch(typeof(Viewer), "AdjustFOV")]
    class Viewer_AdjustFOV
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return EngineLogNoise.ReplaceFovLogging(instructions);
        }
    }

    [HarmonyPatch(typeof(Viewer), "SetCameraGraphicsSettings")]
    class Viewer_SetCameraGraphicsSettings
    {
        static void Postfix(Viewer __instance)
        {
            RenderExperiments.KeepBloomOff(__instance);
        }
    }
}
