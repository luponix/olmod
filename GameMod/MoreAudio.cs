using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace mod_moreaudio
{
    static class MoreAudio
    {
        public static IEnumerable<CodeInstruction> ChangeMaxSources(IEnumerable<CodeInstruction> cs) {
            foreach (var c in cs)
            {
                if (c.opcode == OpCodes.Ldc_I4 && (int)c.operand == 128)
                    c.operand = 512;
                yield return c;
            }
        }

        public static MethodBase LoadSoundEffectsIterator()
        {
            foreach (var x in typeof(UnityAudio).GetNestedTypes(BindingFlags.NonPublic))
                if (x.Name.Contains("LoadSoundEffects"))
                    return x.GetMethod("MoveNext");
            return null;
        }
    }
}
