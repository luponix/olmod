using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Overload;
using UnityEngine;

namespace GameMod.Bots
{
    static class LevelCapture
    {
        const uint MAGIC = 0x3147474Fu;
        const int VERSION = 1;
        const int BAKE_MASK = (1 << 0) | (1 << 9) | (1 << 14) | (1 << 15) | (1 << 17) | (1 << 23) | (1 << 26) | (1 << 31);

        public static string CacheDir
        {
            get { return Path.Combine(Application.persistentDataPath, "BotNav"); }
        }

        public static string CurrentLevelPath
        {
            get { return Path.Combine(CacheDir, "current-level.txt"); }
        }

        public static string LevelKey(LevelInfo level)
        {
            return level.IsAddOn ? level.GetAddOnLevelIdStringHash() : level.FileName;
        }

        public static string FileName(string levelKey)
        {
            string name = levelKey.ToLowerInvariant();
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        public static void OnLevelStart()
        {
            if (MapBake.IsRunning)
            {
                MapBake.OnLevelLoaded();
                return;
            }
            if (!Hosting.HasBots || !GameplayManager.IsDedicatedServer())
                return;
            if (GameplayManager.Level == null || GameManager.m_level_data == null || GameManager.m_level_data.ChunkObjects == null)
                return;
            string levelKey = LevelKey(GameplayManager.Level);
            if (string.IsNullOrEmpty(levelKey))
                return;
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(CurrentLevelPath, levelKey);
            if (IsBaked(levelKey) || File.Exists(CapturePath(levelKey)))
                return;

            var started = DateTime.UtcNow;
            byte[] capture = CaptureLoadedLevel(levelKey);
            Debug.LogFormat("[Bots] {0}: {1} bytes in {2:0} ms", levelKey, capture.Length, (DateTime.UtcNow - started).TotalMilliseconds);
            string capturePath = CapturePath(levelKey);
            ThreadPool.QueueUserWorkItem(_ => WriteAtomically(capturePath, capture));
        }

        public static string CapturePath(string levelKey)
        {
            return Path.Combine(CacheDir, FileName(levelKey) + ".olgeo");
        }

        public static bool IsBaked(string levelKey)
        {
            string name = FileName(levelKey);
            return IsBaked(CacheDir, name) || IsBaked(Olbot.ShippedGeometryDir, name);
        }

        static bool IsBaked(string directory, string name)
        {
            return File.Exists(Path.Combine(directory, name + ".bvh")) && File.Exists(Path.Combine(directory, name + ".vbn"));
        }

        public static byte[] CaptureLoadedLevel(string levelKey)
        {
            LevelData levelData = GameManager.m_level_data;
            bool savedSegmentVisibility = GameplayManager.m_use_segment_visibility;
            GameplayManager.m_use_segment_visibility = false;
            var culledChunks = new List<GameObject>();
            try
            {
                foreach (GameObject chunk in levelData.ChunkObjects)
                    if (chunk != null && !chunk.activeSelf)
                    {
                        chunk.SetActive(true);
                        culledChunks.Add(chunk);
                    }
                return Capture(levelKey, levelData);
            }
            finally
            {
                foreach (GameObject chunk in culledChunks)
                    chunk.SetActive(false);
                GameplayManager.m_use_segment_visibility = savedSegmentVisibility;
            }
        }

        static byte[] Capture(string levelKey, LevelData levelData)
        {
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            writer.Write(MAGIC);
            writer.Write(VERSION);
            writer.Write(levelKey);

            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var layers = new List<byte>();
            foreach (Collider collider in UnityEngine.Object.FindObjectsOfType<Collider>())
            {
                if (collider == null || ((1 << collider.gameObject.layer) & BAKE_MASK) == 0 || collider.isTrigger)
                    continue;
                if (collider.GetComponentInParent<PlayerShip>() != null || collider.GetComponentInParent<MonsterBall>() != null)
                    continue;
                byte layer = (byte)collider.gameObject.layer;
                var meshCollider = collider as MeshCollider;
                if (meshCollider != null)
                {
                    Mesh mesh = meshCollider.sharedMesh;
                    if (mesh == null || !mesh.isReadable)
                        continue;
                    Vector3[] meshVertices = mesh.vertices;
                    int[] meshIndices = mesh.triangles;
                    if (meshVertices.Length == 0 || meshIndices.Length == 0)
                        continue;
                    AddTriangles(meshVertices, meshIndices, collider.transform.localToWorldMatrix, layer, vertices, indices, layers);
                    continue;
                }
                var box = collider as BoxCollider;
                if (box != null)
                    AddTriangles(BoxVertices(box), BOX_INDICES, box.transform.localToWorldMatrix, layer, vertices, indices, layers);
            }
            writer.Write(vertices.Count);
            foreach (Vector3 vertex in vertices)
                WriteVector(writer, vertex);
            writer.Write(indices.Count);
            foreach (int index in indices)
                writer.Write(index);
            writer.Write(layers.ToArray());

            Bounds world = new Bounds();
            bool any = false;
            foreach (GameObject chunk in levelData.ChunkObjects)
            {
                if (chunk == null)
                    continue;
                foreach (Renderer renderer in chunk.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { world = renderer.bounds; any = true; }
                    else world.Encapsulate(renderer.bounds);
                }
                foreach (Collider collider in chunk.GetComponentsInChildren<Collider>(true))
                {
                    if (!any) { world = collider.bounds; any = true; }
                    else world.Encapsulate(collider.bounds);
                }
            }
            WriteVector(writer, world.min);
            WriteVector(writer, world.max);

            var doorBounds = new List<Bounds>();
            var doorLocked = new List<bool>();
            foreach (DoorAnimating door in UnityEngine.Object.FindObjectsOfType<DoorAnimating>())
                foreach (Collider collider in door.GetComponentsInChildren<Collider>(true))
                {
                    if (collider.isTrigger)
                        continue;
                    doorBounds.Add(collider.bounds);
                    doorLocked.Add(door.LockType != DoorLock.NONE);
                }
            writer.Write(doorBounds.Count);
            for (int i = 0; i < doorBounds.Count; i++)
            {
                WriteVector(writer, doorBounds[i].center);
                WriteVector(writer, doorBounds[i].extents);
                writer.Write(doorLocked[i]);
            }

            var seeds = new List<Vector3>();
            foreach (LevelData.SpawnPoint[] points in new[] { levelData.m_player_spawn_points, levelData.m_item_spawn_points, levelData.m_mp_camera_points })
                if (points != null)
                    foreach (LevelData.SpawnPoint point in points)
                        if (point != null)
                            seeds.Add(point.position);
            writer.Write(seeds.Count);
            foreach (Vector3 seed in seeds)
                WriteVector(writer, seed);

            writer.Flush();
            return stream.ToArray();
        }

        static readonly int[] BOX_INDICES =
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            3, 6, 2, 3, 7, 6,
            0, 7, 3, 0, 4, 7,
            1, 2, 6, 1, 6, 5
        };

        static Vector3[] BoxVertices(BoxCollider box)
        {
            Vector3 half = box.size * 0.5f;
            Vector3 c = box.center;
            return new[]
            {
                c + new Vector3(-half.x, -half.y, -half.z), c + new Vector3(half.x, -half.y, -half.z),
                c + new Vector3(half.x, half.y, -half.z),   c + new Vector3(-half.x, half.y, -half.z),
                c + new Vector3(-half.x, -half.y, half.z),  c + new Vector3(half.x, -half.y, half.z),
                c + new Vector3(half.x, half.y, half.z),    c + new Vector3(-half.x, half.y, half.z)
            };
        }

        static void AddTriangles(Vector3[] localVertices, int[] localIndices, Matrix4x4 toWorld, byte layer,
                                 List<Vector3> vertices, List<int> indices, List<byte> layers)
        {
            int first = vertices.Count;
            foreach (Vector3 vertex in localVertices)
                vertices.Add(toWorld.MultiplyPoint3x4(vertex));
            foreach (int index in localIndices)
                indices.Add(index + first);
            for (int t = 0; t < localIndices.Length / 3; t++)
                layers.Add(layer);
        }

        static void WriteVector(BinaryWriter writer, Vector3 v)
        {
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
        }

        public static void WriteAtomically(string path, byte[] bytes)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temporary, path);
            }
            catch (IOException ex)
            {
                Debug.LogFormat("[Bots] cannot write {0}: {1}", path, ex.Message);
            }
        }
    }
}
