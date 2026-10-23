using System.Collections.Generic;
using HarmonyLib;
using Overload;

namespace GameMod.Hooks
{
    [HarmonyPatch(typeof(UIElement), "DrawMainMenu")]
    class UIElement_DrawMainMenu
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return MainMenuRows.InsertRows(instructions);
        }

        static void Postfix(UIElement __instance)
        {
            MainMenuRows.DrawFallback(__instance);
        }
    }
}
