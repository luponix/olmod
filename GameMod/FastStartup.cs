using System.Collections.Generic;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod
{
    static class FastStartup
    {
        const string TextureLimitKey = "olmod_master_texture_limit";

        public static void ApplyTextureLimit()
        {
            if (!GameplayManager.IsDedicatedServer())
                QualitySettings.masterTextureLimit = PlayerPrefs.GetInt(TextureLimitKey, 0);
        }

        [HarmonyPatch(typeof(MenuManager), "SetTextureLevel")]
        class RememberTextureLimit
        {
            static void Postfix()
            {
                if (GameplayManager.IsDedicatedServer() || PlayerPrefs.GetInt(TextureLimitKey, 0) == QualitySettings.masterTextureLimit)
                    return;
                PlayerPrefs.SetInt(TextureLimitKey, QualitySettings.masterTextureLimit);
                PlayerPrefs.Save();
            }
        }

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
