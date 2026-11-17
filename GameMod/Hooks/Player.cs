using System.Collections.Generic;
using HarmonyLib;
using Overload;
using GameMod.Logging;
using GameMod.Perf;

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

    [HarmonyPatch(typeof(Player), "RestorePlayerShipDataAfterRespawn")]
    class Player_RestorePlayerShipDataAfterRespawn
    {
        static void Postfix(Player __instance)
        {
            ShipMeshCombine.Respawned(__instance.c_player_ship);
        }
    }
}
