using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GameMod.Logging
{
    static class LogRewrite
    {
        // Redirects the engine's own Debug.Log calls inside a patched method. Each entry maps
        // the start of a message literal to a replacement that must carry the same signature as
        // the call it takes over, so only the call operand changes and the IL stack is untouched.
        public static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions,
            params KeyValuePair<string, MethodInfo>[] replacements)
        {
            var codes = new List<CodeInstruction>(instructions);
            var sites = new Dictionary<int, MethodInfo>();
            var found = new bool[replacements.Length];
            int armed = -1;

            for (int i = 0; i < codes.Count; i++) {
                // a message literal arms its replacement for the next Debug.Log call, rather than
                // the call searching backwards: "... CRC = " + crc.ToString("X") puts an unrelated
                // ldstr between the two
                if (codes[i].opcode == OpCodes.Ldstr) {
                    armed = MatchPrefix(replacements, codes[i].operand as string, armed);
                    continue;
                }
                var original = codes[i].operand as MethodInfo;
                if (armed < 0 || codes[i].opcode != OpCodes.Call || original == null ||
                    original.DeclaringType != typeof(Debug) || !original.Name.StartsWith("Log", StringComparison.Ordinal))
                    continue;
                if (!SameSignature(original, replacements[armed].Value)) {
                    Debug.LogErrorFormat("[LogRewrite] {0} does not match {1}, engine logging left as is",
                        replacements[armed].Value.Name, original);
                    return codes;
                }
                sites[i] = replacements[armed].Value;
                found[armed] = true;
                armed = -1;
            }

            for (int i = 0; i < replacements.Length; i++)
                if (!found[i]) {
                    Debug.LogErrorFormat("[LogRewrite] no Debug.Log call found for \"{0}\", engine logging left as is",
                        replacements[i].Key);
                    return codes;
                }

            foreach (var site in sites)
                codes[site.Key].operand = site.Value;
            return codes;
        }

        public static KeyValuePair<string, MethodInfo> Site(string messagePrefix, Type owner, string replacement)
        {
            return new KeyValuePair<string, MethodInfo>(messagePrefix, AccessTools.Method(owner, replacement));
        }

        public static void DropMessage(object message)
        {
        }

        public static void DropFormat(string format, params object[] args)
        {
        }

        static int MatchPrefix(KeyValuePair<string, MethodInfo>[] replacements, string message, int armed)
        {
            if (message == null)
                return armed;
            for (int i = 0; i < replacements.Length; i++)
                if (message.StartsWith(replacements[i].Key, StringComparison.Ordinal))
                    return i;
            return armed;
        }

        static bool SameSignature(MethodInfo original, MethodInfo replacement)
        {
            var left = original.GetParameters();
            var right = replacement.GetParameters();
            if (left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i].ParameterType != right[i].ParameterType)
                    return false;
            return true;
        }
    }
}
