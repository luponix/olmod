using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod.Bots
{
    static class MapView
    {
        const uint VBN_MAGIC = 0x314E4256;
        const int MAX_CUBES = 8000;
        const byte FLAG_DOOR = 1;

        static float radius = 20f;
        static bool isShown;
        static string loadedLevel;
        static Dictionary<int, byte> cells;
        static Vector3 origin;
        static float cellSize;
        static Mesh mesh;
        static Material material;
        static Vector3 builtAround;
        static float nextRebuild;

        static void CmdShow()
        {
            if (uConsole.GetNumParameters() > 0)
            {
                string argument = uConsole.GetString();
                float parsed;
                if (argument == "off")
                {
                    isShown = false;
                    uConsole.Log("botmaps_show: off");
                    return;
                }
                if (float.TryParse(argument, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed) && parsed > 0f)
                    radius = parsed;
            }
            isShown = true;
            loadedLevel = null;
            uConsole.Log(string.Format("botmaps_show: on, radius {0} m", radius));
        }

        public static void Draw()
        {
            if (!isShown || GameManager.m_viewer == null || GameplayManager.Level == null)
                return;
            string level = LevelCapture.LevelKey(GameplayManager.Level);
            if (level != loadedLevel)
            {
                loadedLevel = level;
                cells = Load(level);
                builtAround = new Vector3(float.MaxValue, 0f, 0f);
            }
            if (cells == null)
                return;
            if (material == null)
                material = new Material(Shader.Find("Sprites/Default"));

            Camera camera = GameManager.m_viewer.c_camera;
            Vector3 eye = camera.transform.position;
            if (Time.realtimeSinceStartup >= nextRebuild && (eye - builtAround).sqrMagnitude > 4f)
            {
                Rebuild(eye);
                builtAround = eye;
                nextRebuild = Time.realtimeSinceStartup + 0.5f;
            }
            Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0, camera, 0, null, false, false);
        }

        static Dictionary<int, byte> Load(string level)
        {
            string name = LevelCapture.FileName(level) + ".vbn";
            string path = Path.Combine(LevelCapture.CacheDir, name);
            if (!File.Exists(path))
                path = Path.Combine(Olbot.ShippedGeometryDir, name);
            if (!File.Exists(path))
            {
                uConsole.Log("botmaps_show: no map data for " + level);
                return null;
            }
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.ReadUInt32() != VBN_MAGIC)
                {
                    uConsole.Log("botmaps_show: not a .vbn file: " + path);
                    return null;
                }
                reader.ReadByte();
                cellSize = reader.ReadSingle();
                reader.ReadSingle();
                reader.ReadInt32();
                origin = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                int count = reader.ReadInt32();
                var open = new Dictionary<int, byte>(count);
                for (int i = 0; i < count; i++)
                {
                    int key = reader.ReadInt32();
                    open[key] = reader.ReadByte();
                }
                uConsole.Log(string.Format("botmaps_show: {0} cells of {1} m for {2}", count, cellSize, level));
                return open;
            }
        }

        static void Rebuild(Vector3 eye)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();
            float radiusSquared = radius * radius;
            float inset = cellSize * 0.1f;
            foreach (KeyValuePair<int, byte> cell in cells)
            {
                int x = cell.Key & 0x3FF, y = (cell.Key >> 10) & 0x3FF, z = (cell.Key >> 20) & 0x3FF;
                Vector3 min = origin + new Vector3(x, y, z) * cellSize;
                if ((min - eye).sqrMagnitude > radiusSquared)
                    continue;
                bool isEdge = !cells.ContainsKey(cell.Key + 1) || !cells.ContainsKey(cell.Key - 1)
                    || !cells.ContainsKey(cell.Key + (1 << 10)) || !cells.ContainsKey(cell.Key - (1 << 10))
                    || !cells.ContainsKey(cell.Key + (1 << 20)) || !cells.ContainsKey(cell.Key - (1 << 20));
                if (!isEdge)
                    continue;
                AddWireCube(min + Vector3.one * inset, cellSize - 2f * inset,
                    (cell.Value & FLAG_DOOR) != 0 ? new Color(1f, 0.6f, 0.1f, 0.8f) : new Color(0.2f, 0.9f, 0.9f, 0.5f),
                    vertices, colors, indices);
                if (vertices.Count >= MAX_CUBES * 8)
                    break;
            }
            if (mesh == null)
                mesh = new Mesh();
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
        }

        static readonly int[] CUBE_EDGES = { 0, 1, 1, 3, 3, 2, 2, 0, 4, 5, 5, 7, 7, 6, 6, 4, 0, 4, 1, 5, 2, 6, 3, 7 };

        static void AddWireCube(Vector3 min, float size, Color color, List<Vector3> vertices, List<Color> colors, List<int> indices)
        {
            int first = vertices.Count;
            for (int corner = 0; corner < 8; corner++)
            {
                vertices.Add(min + new Vector3((corner & 1) * size, ((corner >> 1) & 1) * size, ((corner >> 2) & 1) * size));
                colors.Add(color);
            }
            foreach (int edge in CUBE_EDGES)
                indices.Add(first + edge);
        }

        [HarmonyPatch(typeof(Console), "RegisterCommands")]
        class MapView_RegisterCommands
        {
            static void Postfix()
            {
                uConsole.RegisterCommand("botmaps_show", "Draw the bot navigation cells of this level around the camera: botmaps_show [radius|off]",
                    new uConsole.DebugCommand(CmdShow));
            }
        }
    }
}
