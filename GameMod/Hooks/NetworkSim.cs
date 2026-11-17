using HarmonyLib;
using Overload;
using UnityEngine;
using GameMod.Perf;

namespace GameMod.Hooks
{
    [HarmonyPatch(typeof(NetworkSim), "PauseRigidBody")]
    class NetworkSim_PauseRigidBody
    {
        static bool Prefix(Rigidbody rb)
        {
            RigidBodyStatePool.Pause(rb);
            return false;
        }
    }

    [HarmonyPatch(typeof(NetworkSim), "ResumeAllPausedRigidBodies")]
    class NetworkSim_ResumeAllPausedRigidBodies
    {
        static bool Prefix()
        {
            RigidBodyStatePool.ResumeAll();
            return false;
        }
    }
}
