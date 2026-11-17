using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod.Perf
{
    static class AudioBudget
    {
        public static int MaxPerClip = 4;
        public static float ExemptDistance = 15f;

        const int MaxSlots = 512;
        const int Untracked = -1;
        const float RolloffEnd = 70f;
        const int ListenerSearchInterval = 30;
        static readonly FieldInfo sourcesField = AccessTools.Field(typeof(UnityAudio), "m_a_source");
        static readonly FieldInfo nextCheckSlotField = AccessTools.Field(typeof(UnityAudio), "m_next_check_slot");
        static readonly Dictionary<int, List<int>> slotsByEffect = new Dictionary<int, List<int>>();
        static readonly int[] effectBySlot = Enumerable.Repeat(Untracked, MaxSlots).ToArray();
        static readonly float[] loudnessBySlot = new float[MaxSlots];
        static float pendingLoudness = -1f;
        static AudioListener listener;
        static int nextListenerSearchFrame;

        static readonly SFXCue[] neverDroppedCues =
        {
            SFXCue.exp_devastator, SFXCue.exp_nova, SFXCue.exp_nova_wash, SFXCue.exp_timebomb,
            SFXCue.mssile_devastator, SFXCue.mssile_nova, SFXCue.mssile_timebomb,
        };
        static readonly string[] neverDroppedEffectPrefixes = { "wep_missile_dev", "wep_missle_devastator", "wep_missile_nova", "wep_missile_timebomb" };
        static HashSet<int> neverDropped;

        public static bool AllowPlay(UnityAudio audio, int effect, Vector3 position, float volume, UnityAudio.SoundType type, bool loop)
        {
            pendingLoudness = -1f;
            if (MaxPerClip <= 0 || loop || type != UnityAudio.SoundType.ST_3D || IsNeverDropped(effect))
                return true;
            Transform ear = ListenerTransform();
            float distance = ear != null ? Vector3.Distance(position, ear.position) : RolloffEnd;
            if (distance < ExemptDistance)
                return true;
            AudioSource[] playing = (AudioSource[])sourcesField.GetValue(audio);
            List<int> slots;
            if (!slotsByEffect.TryGetValue(effect, out slots))
                slotsByEffect[effect] = slots = new List<int>();
            for (int i = slots.Count - 1; i >= 0; i--)
                if (effectBySlot[slots[i]] != effect || playing[slots[i]] == null || !playing[slots[i]].isPlaying)
                    slots.RemoveAt(i);
            pendingLoudness = volume / Mathf.Clamp(distance, 1f, RolloffEnd);
            if (slots.Count < MaxPerClip)
                return true;
            int weakest = -1;
            float weakestLoudness = float.MaxValue;
            foreach (int slot in slots)
            {
                float remaining = RemainingLoudness(playing[slot], loudnessBySlot[slot]);
                if (remaining < weakestLoudness)
                {
                    weakest = slot;
                    weakestLoudness = remaining;
                }
            }
            if (weakestLoudness > pendingLoudness)
            {
                pendingLoudness = -1f;
                return false;
            }
            playing[weakest].Stop();
            slots.Remove(weakest);
            effectBySlot[weakest] = Untracked;
            nextCheckSlotField.SetValue(audio, weakest);
            return true;
        }

        public static void OnPlayed(int effect, int slot)
        {
            if (slot < 0 || slot >= MaxSlots)
                return;
            if (pendingLoudness < 0f)
            {
                effectBySlot[slot] = Untracked;
                return;
            }
            effectBySlot[slot] = effect;
            loudnessBySlot[slot] = pendingLoudness;
            slotsByEffect[effect].Add(slot);
            pendingLoudness = -1f;
        }

        static bool IsNeverDropped(int effect)
        {
            if (neverDropped == null)
            {
                if (SFXCueManager.cue_data == null || SFXCueManager.cue_data.Count == 0)
                    return true;
                neverDropped = CollectNeverDropped();
            }
            return neverDropped.Contains(effect);
        }

        static HashSet<int> CollectNeverDropped()
        {
            var effects = new HashSet<int>();
            foreach (SoundEffect effect in Enum.GetValues(typeof(SoundEffect)))
            {
                string name = effect.ToString();
                if (neverDroppedEffectPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                    effects.Add((int)effect);
            }
            foreach (SFXCue cue in neverDroppedCues)
            {
                if ((int)cue >= SFXCueManager.cue_data.Count)
                    continue;
                SFXCueData data = SFXCueManager.cue_data[(int)cue];
                for (int layer = 0; layer < data.num_layers; layer++)
                    for (int element = 0; element < data.layers[layer].num_elements; element++)
                        effects.Add((int)data.layers[layer].elements[element].effect);
            }
            return effects;
        }

        static float RemainingLoudness(AudioSource source, float loudness)
        {
            AudioClip clip = source.clip;
            if (clip == null || clip.length <= 0f)
                return loudness;
            return loudness * Mathf.Clamp01(1f - source.time / clip.length);
        }

        static Transform ListenerTransform()
        {
            if (listener != null && listener.isActiveAndEnabled)
                return listener.transform;
            if (Time.frameCount >= nextListenerSearchFrame)
            {
                nextListenerSearchFrame = Time.frameCount + ListenerSearchInterval;
                listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
            }
            return listener != null && listener.isActiveAndEnabled ? listener.transform : null;
        }

        public static void CmdAudioBudget()
        {
            int parameters = uConsole.GetNumParameters();
            int cap;
            float exempt;
            if (parameters > 0 && int.TryParse(uConsole.GetString(), out cap))
                MaxPerClip = Mathf.Max(0, cap);
            if (parameters > 1 && float.TryParse(uConsole.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out exempt))
                ExemptDistance = Mathf.Max(0f, exempt);
            uConsole.Log("audio_budget = " + MaxPerClip + " copies of one 3D clip beyond " + ExemptDistance + " m (0 = no cap)");
        }
    }
}
