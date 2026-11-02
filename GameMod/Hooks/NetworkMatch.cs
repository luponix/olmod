using HarmonyLib;
using Overload;

namespace GameMod.Hooks
{
    [HarmonyPatch(typeof(NetworkMatch), "InitBeforeEachMatch")]
    class NetworkMatch_InitBeforeEachMatch
    {
        static void Prefix()
        {
            MPSpawnExtension.ResetForNewLevel();
        }
    }
}
