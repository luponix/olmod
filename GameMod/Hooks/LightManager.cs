using HarmonyLib;
using Overload;
using UnityEngine;
using GameMod.Perf;

namespace GameMod.Hooks
{
    [HarmonyPatch(typeof(LightManager), "CreateLightFlash")]
    class LightManager_CreateLightFlash
    {
        static bool Prefix(Vector3 pos, float range)
        {
            return RenderExperiments.AllowLightFlash(pos, range);
        }
    }
}
