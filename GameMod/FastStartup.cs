using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod
{
    static class FastStartup
    {
        const string TextureLimitKey = "olmod_master_texture_limit";

        static bool deferring;
        static readonly List<MethodBase> pending = new List<MethodBase>();
        static readonly HashSet<MethodBase> pendingSet = new HashSet<MethodBase>();
        static readonly MethodInfo deferred = AccessTools.Method(typeof(FastStartup), "Deferred");
        static readonly Type sharedState = AccessTools.TypeByName("HarmonyLib.HarmonySharedState");
        static readonly MethodInfo updateWrapper = AccessTools.Method(AccessTools.TypeByName("HarmonyLib.PatchFunctions"), "UpdateWrapper");
        static readonly MethodInfo updatePatchInfo = AccessTools.Method(sharedState, "UpdatePatchInfo");
        static readonly MethodInfo getPatchInfo = AccessTools.Method(sharedState, "GetPatchInfo");
        static readonly MethodInfo serialize = AccessTools.Method(AccessTools.TypeByName("HarmonyLib.PatchInfoSerialization"), "Serialize");

        static void Deferred() { }

        public static void PatchAll(Harmony harmony, Assembly assembly)
        {
            try
            {
                if (updateWrapper == null || updatePatchInfo == null || getPatchInfo == null || serialize == null || AccessTools.Field(sharedState, "state") == null)
                    throw new MissingMemberException("Harmony internals not found");
                var self = new Harmony("olmod.faststartup");
                self.Patch(updateWrapper, new HarmonyMethod(AccessTools.Method(typeof(FastStartup), "DeferWrapper")));
                self.Patch(updatePatchInfo, new HarmonyMethod(AccessTools.Method(typeof(FastStartup), "DeferPatchInfo")));
            }
            catch (Exception ex)
            {
                Debug.LogWarningFormat("[Init] one-pass patching not available, patching the normal way: {0}", ex.Message);
                harmony.PatchAll(assembly);
                return;
            }
            deferring = true;
            try
            {
                harmony.PatchAll(assembly);
            }
            finally
            {
                deferring = false;
                foreach (var original in pending)
                {
                    try
                    {
                        object info = getPatchInfo.Invoke(null, new object[] { original });
                        object replacement = updateWrapper.Invoke(null, new object[] { original, info });
                        updatePatchInfo.Invoke(null, new object[] { original, replacement, info });
                    }
                    catch (Exception ex)
                    {
                        Debug.LogErrorFormat("[Init] patching {0} failed: {1}", original.FullDescription(), ex.InnerException ?? ex);
                    }
                }
                pending.Clear();
                pendingSet.Clear();
            }
        }

        static bool DeferWrapper(MethodBase original, ref MethodInfo __result)
        {
            if (!deferring)
                return true;
            if (pendingSet.Add(original))
                pending.Add(original);
            __result = deferred;
            return false;
        }

        static bool DeferPatchInfo(MethodBase original, MethodInfo replacement, PatchInfo patchInfo)
        {
            if (replacement != deferred)
                return true;
            var state = (Dictionary<MethodBase, byte[]>)AccessTools.Field(sharedState, "state").GetValue(null);
            byte[] bytes = (byte[])serialize.Invoke(null, new object[] { patchInfo });
            lock (state)
                state[original] = bytes;
            return false;
        }

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
