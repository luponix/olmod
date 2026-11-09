using HarmonyLib;

namespace GameMod.Hooks
{
    // Loc.Initialize builds a fresh table on startup and on every language change, and
    // can run before this patch is applied, so Prepare adds the strings once as well.
    [HarmonyPatch(typeof(Loc), "ReadLanguageFile")]
    class Loc_ReadLanguageFile
    {
        static void Prepare()
        {
            Localization.AddOlmodStrings();
        }

        static void Postfix()
        {
            Localization.AddOlmodStrings();
        }
    }
}
