using System.Collections.Generic;
using HarmonyLib;
using Overload;
using GameMod.Bots;
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

    [HarmonyPatch(typeof(PlayerShip), "CanFire")]
    class PlayerShip_CanFire
    {
        static bool Prefix(PlayerShip __instance, ref bool __result)
        {
            if (!MPBoostFire.Enabled)
                return true;
            __result = MPBoostFire.AllowsFire(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "AllowedToUseFullChat")]
    class PlayerShip_AllowedToUseFullChat
    {
        static void Postfix(ref bool __result)
        {
            if (AddBotMenu.IsTypingName)
                __result = false;
        }
    }
}
