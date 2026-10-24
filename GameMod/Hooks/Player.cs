using System.Collections.Generic;
using HarmonyLib;
using Overload;
using GameMod.Logging;

namespace GameMod.Hooks
{
    [HarmonyPatch(typeof(Player), "UpdateUpgrades")]
    class Player_UpdateUpgrades
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return EngineLogNoise.ReplaceUpgradeLogging(instructions);
        }
    }
}
