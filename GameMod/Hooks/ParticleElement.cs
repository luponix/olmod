using HarmonyLib;
using Overload;
using UnityEngine;
using GameMod.Perf;

namespace GameMod.Hooks
{
    [HarmonyPatch(typeof(ParticleElement), "SetLifetime")]
    class ParticleElement_SetLifetime
    {
        static void Postfix(ParticleElement __instance, ParticleSystem[] ___c_ps, GameObject ___c_go, ref float ___m_timer)
        {
            ParticleLifetime.TrimTimer(__instance, ___c_ps, ___c_go, ref ___m_timer);
        }
    }
}
