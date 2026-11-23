using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    [HarmonyPatch(typeof(UIElement), "DrawMpMatchSetup")]
    class UIElement_DrawMpMatchSetup
    {
        static bool Prepare()
        {
            return Core.GameMod.HasInternetMatch();
        }

        static bool Prefix(UIElement __instance)
        {
            MatchSetupMenu.Draw(__instance);
            return false;
        }
    }

    [HarmonyPatch]
    class UIElement_SelectAndDraw
    {
        static bool Prepare()
        {
            return MenuDriver.Enabled;
        }

        static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(UIElement).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name.StartsWith("SelectAndDraw"))
                .Where(method => method.GetParameters().Any(p => p.Name == "s" && p.ParameterType == typeof(string))
                    && method.GetParameters().Any(p => p.Name == "selection" && p.ParameterType == typeof(int)))
                .Cast<MethodBase>();
        }

        static void Prefix(string s, int selection)
        {
            MenuDriver.RecordItem(selection, s);
        }
    }
}
