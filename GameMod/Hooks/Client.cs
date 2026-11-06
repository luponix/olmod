using HarmonyLib;
using Overload;

namespace GameMod.Hooks
{
    [HarmonyPatch(typeof(Client), "Disconnect")]
    class Client_Disconnect
    {
        static void Postfix()
        {
            MPBoostFire.ResetAfterMatch();
        }
    }
}
