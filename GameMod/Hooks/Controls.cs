using HarmonyLib;
using Overload;

namespace GameMod.Hooks
{
    [HarmonyPatch(typeof(Controls), "UpdateDevice")]
    class Controls_UpdateDevice
    {
        static bool Prepare()
        {
            return MenuDriver.Enabled;
        }

        static void Postfix()
        {
            MenuDriver.InjectPress();
        }
    }
}
