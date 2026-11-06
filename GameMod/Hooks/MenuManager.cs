using HarmonyLib;
using Overload;

namespace GameMod.Hooks
{
    [HarmonyPatch(typeof(MenuManager), "MpMatchSetup")]
    class MenuManager_MpMatchSetup
    {
        static bool Prepare()
        {
            return Core.GameMod.HasInternetMatch();
        }

        static bool Prefix(bool online, ref float ___m_menu_state_timer)
        {
            MatchSetupMenu.Update(online, ref ___m_menu_state_timer);
            return false;
        }
    }
}
