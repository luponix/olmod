using System.Collections.Generic;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod.Replay
{
    // Challenge robots as inert prefab clones: lifetimes come from the RobotSpawn/RobotDeath
    // events so a seek can rebuild the live set, poses from the frames' robot samples.
    public static class Robots
    {
        private static Recording recording;

        private class RobotLifetime
        {
            public int id;
            public EnemyType type;
            public RobotSpawnFlags flags;
            public Vector3 spawnPos;
            public Quaternion spawnRot;
            public float spawnTime;
            public float removalTime = float.MaxValue;
        }

        private static readonly List<RobotLifetime> lifetimes = new List<RobotLifetime>();
        private static readonly Dictionary<int, RobotLifetime> lifetimeById = new Dictionary<int, RobotLifetime>();
        private static readonly Dictionary<int, GameObject> liveRobots = new Dictionary<int, GameObject>();
        private static readonly List<int> expiredRobotIds = new List<int>();

        public static void Begin(Recording replay)
        {
            recording = replay;
            BuildRobotTimeline();
            DestroySceneRobots();
        }

        public static void End()
        {
            ClearLiveRobots();
            lifetimes.Clear();
            lifetimeById.Clear();
            recording = null;
        }

        public static void HideAll()
        {
            ClearLiveRobots();
        }

        private static void BuildRobotTimeline()
        {
            lifetimes.Clear();
            lifetimeById.Clear();
            liveRobots.Clear();
            foreach (var ev in recording.events)
            {
                if (ev.type == MatchEventType.RobotSpawn)
                {
                    if (lifetimeById.ContainsKey(ev.iA))
                        continue;
                    var lifetime = new RobotLifetime
                    {
                        id = ev.iA,
                        type = (EnemyType)ev.iB,
                        flags = (RobotSpawnFlags)ev.iC,
                        spawnPos = ev.pos,
                        spawnRot = ev.rot,
                        spawnTime = ev.t
                    };
                    lifetimes.Add(lifetime);
                    lifetimeById[ev.iA] = lifetime;
                }
                else if (ev.type == MatchEventType.RobotDeath && ev.iB == (int)RobotDeathStage.Exploded)
                {
                    RobotLifetime lifetime;
                    if (lifetimeById.TryGetValue(ev.iA, out lifetime))
                        lifetime.removalTime = ev.t;
                }
            }
        }

        // The level loads with its own live robots, whose AI would hunt the camera. Emptying
        // RobotManager's lists stops RobotManager.Update from walking the destroyed entries.
        private static void DestroySceneRobots()
        {
            foreach (var robot in GameObject.FindGameObjectsWithTag("Robot"))
                if (robot != null)
                    UnityEngine.Object.Destroy(robot);
            RobotManager.m_master_robot_list.Clear();
            RobotManager.m_robot_list.Clear();
        }

        public static void SpawnRobot(int robotId)
        {
            RobotLifetime lifetime;
            if (lifetimeById.TryGetValue(robotId, out lifetime))
                Spawn(lifetime);
        }

        private static void Spawn(RobotLifetime lifetime)
        {
            if (liveRobots.ContainsKey(lifetime.id))
                return;
            var prefabs = RobotManager.m_enemy_prefab;
            int prefabIndex = (int)lifetime.type;
            var prefab = prefabs != null && prefabIndex >= 0 && prefabIndex < prefabs.Length
                ? prefabs[prefabIndex] : null;
            if (prefab == null)
                return;
            var spawned = UnityEngine.Object.Instantiate(prefab, lifetime.spawnPos, lifetime.spawnRot);
            if (spawned == null)
                return;
            MakeInert(spawned);
            liveRobots[lifetime.id] = spawned;
        }

        // Robot.Start builds the AI and the damage model, and never runs for a component disabled
        // in the same call stack as the Instantiate. Everything a robot does lives in an enabled
        // MonoBehaviour; renderers, lights and particles are not one, so it still looks the part.
        // Without the "Robot" tag RobotManager cannot adopt the clone (RobotManager.cs:850).
        private static void MakeInert(GameObject robot)
        {
            World.MakeDisplayOnly(robot);
            foreach (var behaviour in robot.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null)
                    behaviour.enabled = false;
            foreach (var body in robot.GetComponentsInChildren<Rigidbody>(true))
            {
                if (body == null)
                    continue;
                body.detectCollisions = false;
                body.useGravity = false;
                body.isKinematic = true;
            }
            foreach (var node in robot.GetComponentsInChildren<Transform>(true))
                if (node != null && node.gameObject.CompareTag("Robot"))
                    node.gameObject.tag = "Untagged";
        }

        public static void DestroyRobot(int robotId)
        {
            GameObject robot;
            if (!liveRobots.TryGetValue(robotId, out robot))
                return;
            liveRobots.Remove(robotId);
            if (robot != null)
                UnityEngine.Object.Destroy(robot);
        }

        private static void ClearLiveRobots()
        {
            foreach (var entry in liveRobots)
                if (entry.Value != null)
                    UnityEngine.Object.Destroy(entry.Value);
            liveRobots.Clear();
        }

        public static void RebuildAt(float time)
        {
            expiredRobotIds.Clear();
            foreach (var entry in liveRobots)
            {
                RobotLifetime lifetime;
                bool stillAlive = entry.Value != null &&
                                  lifetimeById.TryGetValue(entry.Key, out lifetime) &&
                                  time >= lifetime.spawnTime && time < lifetime.removalTime;
                if (!stillAlive)
                    expiredRobotIds.Add(entry.Key);
            }
            for (int i = 0; i < expiredRobotIds.Count; i++)
                DestroyRobot(expiredRobotIds[i]);
            foreach (var lifetime in lifetimes)
            {
                if (lifetime.spawnTime > time)
                    break;
                if (time < lifetime.removalTime && !liveRobots.ContainsKey(lifetime.id))
                    Spawn(lifetime);
            }
        }

        // The recorder samples only robots the engine had active, so a robot with no sample in
        // either bracketing frame was out of sight and is hidden, not destroyed.
        public static void Drive()
        {
            int before = Playback.FrameBefore, after = Playback.FrameAfter;
            if (liveRobots.Count == 0 || before < 0)
                return;
            var samplesBefore = recording.frames[before].robots;
            var samplesAfter = recording.frames[after].robots;
            float intervalSeconds = recording.frames[after].t - recording.frames[before].t;
            float blend = Playback.FrameBlend;
            foreach (var entry in liveRobots)
            {
                var robot = entry.Value;
                if (robot == null)
                    continue;
                RobotSample sampleBefore, sampleAfter;
                bool hasBefore = TryGetSample(samplesBefore, entry.Key, out sampleBefore);
                bool hasAfter = TryGetSample(samplesAfter, entry.Key, out sampleAfter);
                bool shown = hasBefore || hasAfter;
                if (robot.activeSelf != shown)
                    robot.SetActive(shown);
                if (!shown)
                    continue;
                if (!hasBefore || !hasAfter)
                {
                    var onlySample = hasAfter ? sampleAfter : sampleBefore;
                    robot.transform.position = onlySample.pos;
                    robot.transform.rotation = onlySample.rot;
                    continue;
                }
                robot.transform.position = Motion.Interpolate(sampleBefore.pos, sampleBefore.vel,
                    sampleAfter.pos, sampleAfter.vel, intervalSeconds, blend);
                robot.transform.rotation = Quaternion.SlerpUnclamped(sampleBefore.rot, sampleAfter.rot, blend);
            }
        }

        private static bool TryGetSample(RobotSample[] samples, int robotId, out RobotSample sample)
        {
            if (samples != null)
                for (int i = 0; i < samples.Length; i++)
                    if (samples[i].id == robotId)
                    {
                        sample = samples[i];
                        return true;
                    }
            sample = default(RobotSample);
            return false;
        }

        // What the engine plays for a dying robot: a burst scaled by its m_explosion_size as it
        // starts to die (Robot.cs:10687), then the final flash and two cues (Robot.cs:10943).
        public static void PlayDeathFx(MatchEvent death)
        {
            GameObject robot;
            if (!liveRobots.TryGetValue(death.iA, out robot) || robot == null)
            {
                ExplosionManager.CreateRandomExplosionCore(death.pos);
                return;
            }
            var where = robot.transform;
            where.position = death.pos;
            float explosionSize = Mathf.Max(0.1f, death.fA);
            if (death.iB == (int)RobotDeathStage.Dying)
            {
                ExplosionManager.CreateRandomExplosionSimple(where, 0.2f + explosionSize, 0.9f);
                return;
            }

            RobotLifetime lifetime;
            var flags = lifetimeById.TryGetValue(death.iA, out lifetime) ? lifetime.flags : 0;
            bool boss = (flags & RobotSpawnFlags.Boss) != 0; // a boss's staged explosions need its own dying update
            if (!boss && (flags & RobotSpawnFlags.Super) != 0)
                ParticleManager.psm[0].StartParticleInstant(61, where.position, where.rotation);
            else if (!boss && (flags & RobotSpawnFlags.Alien) != 0)
                ParticleManager.psm[0].StartParticleInstant(29, where.position, where.rotation);
            else
                ExplosionManager.CreateRandomExplosionFlash(where, explosionSize);
            GameManager.m_audio.PlayCuePos(259, death.pos, 1f, Random.Range(-0.1f, 0.1f));
            GameManager.m_audio.PlayCuePos(256, death.pos, 0.8f, Random.Range(-0.1f, 0.1f));
        }

        // Matcens are scene props that spawn live robots from MatcenFrame (RobotMatcen.cs:290);
        // blocking there keeps the matcen's own visuals.
        [HarmonyPatch(typeof(RobotMatcen), "MatcenFrame")]
        class Robots_RobotMatcen_MatcenFrame
        {
            static bool Prefix()
            {
                return Session.Mode != SessionMode.Playback;
            }
        }
    }
}
