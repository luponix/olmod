using System.Collections.Generic;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod
{
    static class FastStartup
    {
        [HarmonyPatch(typeof(GameManager), "MaybeEnableDeveloperMode")]
        class BatchInitSteps
        {
            static void Prefix(IEnumerator<float> ___m_initialization_enumerator, ref float ___m_initialization_progress)
            {
                float until = Time.realtimeSinceStartup + 0.1f;
                while (!GameManager.m_initialized && Time.realtimeSinceStartup < until)
                {
                    ___m_initialization_progress = ___m_initialization_enumerator.Current;
                    GameManager.m_initialized = !___m_initialization_enumerator.MoveNext();
                }
            }
        }
    }
}
