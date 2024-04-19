using HarmonyLib;
using Ionic.Zip;
using Overload;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System.Text;
using UnityEngine.Networking;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace GameMod
{
    internal class ReduceLogging
    {
        // Replaces occurences of Debug.Log and Debug.LogFormat with empty dummy functions to avoid thousands of lines for loading of levels
        [HarmonyPatch(typeof(GameManager), "ScanForLevels")]
        class ReduceLogging_GameManager_ScanForLevels
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void ArgumentVoidForLogFormat(string format, params object[] args) { }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void ArgumentVoidForLog(object message) { }

            
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
            {
                var void_method_log_format = AccessTools.Method(typeof(ReduceLogging_GameManager_ScanForLevels), "ArgumentVoidForLogFormat");
                var void_method_log = AccessTools.Method(typeof(ReduceLogging_GameManager_ScanForLevels), "ArgumentVoidForLog");

                foreach (var code in codes)
                {
                    if (code.opcode == OpCodes.Call && ((MethodInfo)code.operand).Name == "LogFormat")
                    {
                        yield return new CodeInstruction(OpCodes.Call, void_method_log_format);
                        continue;
                    }
                    else if (code.opcode == OpCodes.Call && ((MethodInfo)code.operand).Name == "Log")
                    {
                        yield return new CodeInstruction(OpCodes.Call, void_method_log);
                        continue;
                    }
                    yield return code;
                }
            }
        }
    }
}
