using System.Collections.Generic;
using Overload;
using UnityEngine;

namespace GameMod.Perf
{
    static class ParticleLifetime
    {
        const float NoTrim = -1f;
        const float SimulationSpeedReserve = 1.25f;
        const float ExpireReserve = 0.05f;

        public static bool Enabled = true;

        static readonly Dictionary<ParticleElement, float> contentLifetimeByElement = new Dictionary<ParticleElement, float>();

        public static void TrimTimer(ParticleElement element, ParticleSystem[] systems, GameObject root, ref float timer)
        {
            if (!Enabled || timer <= 0f)
                return;
            float lifetime = ContentLifetime(element, systems, root);
            if (lifetime == NoTrim)
                return;
            float trimmed = lifetime * SimulationSpeedReserve + ExpireReserve;
            if (trimmed < timer)
                timer = trimmed;
        }

        public static void Forget()
        {
            contentLifetimeByElement.Clear();
        }

        static float ContentLifetime(ParticleElement element, ParticleSystem[] systems, GameObject root)
        {
            float lifetime;
            if (!contentLifetimeByElement.TryGetValue(element, out lifetime))
                contentLifetimeByElement[element] = lifetime = ComputeContentLifetime(systems, root);
            return lifetime;
        }

        static float ComputeContentLifetime(ParticleSystem[] systems, GameObject root)
        {
            if (systems == null || systems.Length == 0 || root == null || HasTimedComponents(root))
                return NoTrim;
            float lifetime = 0f;
            foreach (ParticleSystem system in systems)
            {
                float end = LastParticleDeath(system);
                if (end == NoTrim)
                    return NoTrim;
                lifetime = Mathf.Max(lifetime, end);
            }
            return lifetime;
        }

        static bool HasTimedComponents(GameObject root)
        {
            if (root.GetComponentInChildren<TrailRenderer>(true) != null || root.GetComponentInChildren<LineRenderer>(true) != null)
                return true;
            foreach (Behaviour behaviour in root.GetComponentsInChildren<Behaviour>(true))
                if (!(behaviour is Explosion) && !(behaviour is ExplosionLite))
                    return true;
            return false;
        }

        static float LastParticleDeath(ParticleSystem system)
        {
            ParticleSystem.MainModule main = system.main;
            if (main.loop || system.subEmitters.enabled)
                return NoTrim;
            float delay = ConstantMax(main.startDelay);
            float particleLifetime = ConstantMax(main.startLifetime);
            float lastEmission = LastEmission(system, main.duration);
            if (delay == NoTrim || particleLifetime == NoTrim || lastEmission == NoTrim)
                return NoTrim;
            return delay + lastEmission + particleLifetime;
        }

        static float LastEmission(ParticleSystem system, float duration)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            if (!emission.enabled)
                return 0f;
            float overTime = ConstantMax(emission.rateOverTime);
            float overDistance = ConstantMax(emission.rateOverDistance);
            if (overTime == NoTrim || overDistance == NoTrim)
                return NoTrim;
            if (overTime > 0f || overDistance > 0f)
                return duration;
            float last = 0f;
            ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[emission.burstCount];
            emission.GetBursts(bursts);
            foreach (ParticleSystem.Burst burst in bursts)
            {
                if (burst.cycleCount <= 0)
                    return duration;
                last = Mathf.Max(last, burst.time + (burst.cycleCount - 1) * burst.repeatInterval);
            }
            return Mathf.Min(last, duration);
        }

        static float ConstantMax(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return Mathf.Max(curve.constantMin, curve.constantMax);
                default:
                    return NoTrim;
            }
        }

        public static void CmdTrim()
        {
            if (uConsole.GetNumParameters() > 0)
                Enabled = uConsole.GetInt() != 0;
            uConsole.Log("perf_particle_trim " + (Enabled ? "1" : "0"));
        }
    }
}
