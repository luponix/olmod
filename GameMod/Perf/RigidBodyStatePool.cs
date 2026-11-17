using System.Collections.Generic;
using Overload;
using UnityEngine;

namespace GameMod.Perf
{
    static class RigidBodyStatePool
    {
        static readonly Stack<RigidBodyState> freeStates = new Stack<RigidBodyState>();

        public static void Pause(Rigidbody rb)
        {
            if (rb.isKinematic)
                return;
            RigidBodyState state;
            if (freeStates.Count > 0)
            {
                state = freeStates.Pop();
                state.m_velocity = rb.velocity;
                state.m_angular_velocity = rb.angularVelocity;
            }
            else
            {
                state = new RigidBodyState(rb);
            }
            rb.isKinematic = true;
            NetworkSim.m_paused_rigid_bodies.Add(rb, state);
        }

        public static void ResumeAll()
        {
            foreach (KeyValuePair<Rigidbody, RigidBodyState> pair in NetworkSim.m_paused_rigid_bodies)
            {
                Rigidbody rb = pair.Key;
                RigidBodyState state = pair.Value;
                rb.isKinematic = false;
                rb.velocity = state.m_velocity;
                rb.angularVelocity = state.m_angular_velocity;
                freeStates.Push(state);
            }
            NetworkSim.m_paused_rigid_bodies.Clear();
        }
    }
}
