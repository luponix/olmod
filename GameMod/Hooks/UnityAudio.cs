using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using mod_moreaudio;
using GameMod.Perf;
using Overload;
using UnityEngine;

namespace GameMod.Hooks
{
    [HarmonyPatch]
    class UnityAudio_LoadSoundEffects
    {
        static MethodBase TargetMethod() { return MoreAudio.LoadSoundEffectsIterator(); }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> cs) { return MoreAudio.ChangeMaxSources(cs); }
    }

    [HarmonyPatch(typeof(UnityAudio), "InitializeForNewLevel")]
    class UnityAudio_InitializeForNewLevel { static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> cs) { return MoreAudio.ChangeMaxSources(cs); } }

    [HarmonyPatch(typeof(UnityAudio), "AdjustSFXPitch")]
    class UnityAudio_AdjustSFXPitch { static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> cs) { return MoreAudio.ChangeMaxSources(cs); } }

    [HarmonyPatch(typeof(UnityAudio), "UpdateAudio")]
    class UnityAudio_UpdateAudio { static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> cs) { return MoreAudio.ChangeMaxSources(cs); } }

    [HarmonyPatch(typeof(UnityAudio), "PauseAllSounds")]
    class UnityAudio_PauseAllSounds { static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> cs) { return MoreAudio.ChangeMaxSources(cs); } }

    [HarmonyPatch(typeof(UnityAudio), "RestartSoundsFromLoadGame")]
    class UnityAudio_RestartSoundsFromLoadGame { static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> cs) { return MoreAudio.ChangeMaxSources(cs); } }

    [HarmonyPatch(typeof(UnityAudio), "Serialize")]
    class UnityAudio_Serialize { static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> cs) { return MoreAudio.ChangeMaxSources(cs); } }

    [HarmonyPatch(typeof(UnityAudio), "Deserialize")]
    class UnityAudio_Deserialize { static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> cs) { return MoreAudio.ChangeMaxSources(cs); } }

    [HarmonyPatch(typeof(UnityAudio), MethodType.Constructor)]
    class UnityAudio_Constructor { static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> cs) { return MoreAudio.ChangeMaxSources(cs); } }

    [HarmonyPatch(typeof(UnityAudio), "FindNextOpenAudioSlot")]
    class UnityAudio_FindNextOpenAudioSlot { static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> cs) { return MoreAudio.ChangeMaxSources(cs); } }

    [HarmonyPatch(typeof(UnityAudio), "PlaySound")]
    class UnityAudio_PlaySound
    {
        static bool Prefix(UnityAudio __instance, int effect, Vector3 pos3d, float vol, UnityAudio.SoundType st, bool loop, ref int __result)
        {
            if (AudioBudget.AllowPlay(__instance, effect, pos3d, vol, st, loop))
                return true;
            __result = -1;
            return false;
        }

        static void Postfix(int effect, int __result)
        {
            AudioBudget.OnPlayed(effect, __result);
        }
    }
}
