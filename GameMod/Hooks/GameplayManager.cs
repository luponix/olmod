using HarmonyLib;
using GameMod.Bots;
using GameMod.Perf;
using Overload;

namespace GameMod.Hooks
{
    // Start every level without the previous level's impact marks
    [HarmonyPatch(typeof(GameplayManager), "StartLevel")]
    class GameplayManager_StartLevel
    {
        static void Postfix()
        {
            ImpactMarks.Clear();
            LevelCapture.OnLevelStart();
            ParticleLifetime.Forget();
            RemoteThunderboltLight.Forget();
            System.GC.Collect();
        }
    }

    // Drop impact marks when a level ends, so none wait in the queue for the next one
    [HarmonyPatch(typeof(GameplayManager), "DoneLevel")]
    class GameplayManager_DoneLevel
    {
        static void Postfix()
        {
            ImpactMarks.Clear();
        }
    }
}
