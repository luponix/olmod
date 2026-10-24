using System.Collections.Generic;
using HarmonyLib;
using Overload;
using GameMod.Logging;

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
}
