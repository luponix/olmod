using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Overload;
using GameMod.Logging;
using GameMod.Perf;

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

    [HarmonyPatch(typeof(PlayerShip), "StartDyingCamera")]
    class PlayerShip_StartDyingCamera
    {
        static void Postfix(PlayerShip __instance)
        {
            if (__instance.c_player.isLocalPlayer)
                System.GC.Collect();
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

    [HarmonyPatch(typeof(PlayerShip), "DrawEffectMesh")]
    class PlayerShip_DrawEffectMesh
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ShipEffectMesh.UseCachedMountFilters(instructions, "DrawEffectMesh");
        }

        static bool Prefix(PlayerShip __instance, ref MaterialPropertyBlock ___m_spawn_mpb, float str, bool cloak)
        {
            if (!ShipEffectMesh.EffectMeshOutsideView(__instance))
                return true;
            ShipEffectMesh.KeepEffectMeshProperties(ref ___m_spawn_mpb, str, cloak);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "DrawEffectMeshWeapon")]
    class PlayerShip_DrawEffectMeshWeapon
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ShipEffectMesh.UseCachedMountFilters(instructions, "DrawEffectMeshWeapon");
        }

        static bool Prefix(PlayerShip __instance, ref MaterialPropertyBlock ___m_spawn_mpb, float str)
        {
            if (!ShipEffectMesh.EffectMeshOutsideView(__instance))
                return true;
            ShipEffectMesh.KeepEffectMeshProperties(ref ___m_spawn_mpb, str, false);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "Awake")]
    class PlayerShip_Awake
    {
        static void Postfix(PlayerShip __instance)
        {
            ShipGuiLayout.Apply(__instance);
            ShipLightCull.InstallOnShip(__instance);
            ShipMeshCombine.Attach(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "Update")]
    class PlayerShip_Update
    {
        static void Postfix(PlayerShip __instance)
        {
            RemoteThunderboltLight.SwitchOffWhenDark(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "UpdateMaterial")]
    class PlayerShip_UpdateMaterial
    {
        static void Postfix(PlayerShip __instance, ref float ___m_prev_headlight, float ___m_headlight_brightness)
        {
            ShipMaterialLatch.Close(__instance, ref ___m_prev_headlight, ___m_headlight_brightness);
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "RestoreLights")]
    class PlayerShip_RestoreLights
    {
        static void Postfix(PlayerShip __instance)
        {
            ShipLightCull.Rearm(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "SetCockpitVisibility")]
    class PlayerShip_SetCockpitVisibility
    {
        static void Postfix(PlayerShip __instance)
        {
            RenderExperiments.KeepShipShadowsOff(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "SetCustomBody")]
    class PlayerShip_SetCustomBody
    {
        static void Postfix(PlayerShip __instance)
        {
            ShipMaterialLatch.Reopen(__instance);
            RenderExperiments.KeepShipShadowsOff(__instance);
            ShipMeshCombine.Attach(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "SetCustomWings")]
    class PlayerShip_SetCustomWings
    {
        static void Postfix(PlayerShip __instance)
        {
            ShipMaterialLatch.Reopen(__instance);
            RenderExperiments.KeepShipShadowsOff(__instance);
            ShipMeshCombine.Attach(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "DeactivateCloak")]
    class PlayerShip_DeactivateCloak
    {
        static void Postfix(PlayerShip __instance)
        {
            ShipMeshCombine.Reassert(__instance);
        }
    }
}
