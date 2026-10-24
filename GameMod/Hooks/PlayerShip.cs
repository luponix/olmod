using System.Collections.Generic;
using HarmonyLib;
using Overload;
using GameMod.Logging;

namespace GameMod.Hooks
{
    [HarmonyPatch(typeof(PlayerShip), "UpdateChatMessage")]
    class PlayerShip_UpdateChatMessage
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return EngineLogNoise.ReplaceChatReturnLogging(instructions);
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "EnablePlayerLevelCollision")]
    class PlayerShip_EnablePlayerLevelCollision
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return EngineLogNoise.ReplaceCollisionLogging(instructions);
        }
    }
}
