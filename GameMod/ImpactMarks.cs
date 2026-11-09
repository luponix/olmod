using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod
{
    // Scorch marks where shots hit the level. The engine has no decal system (Projectile's
    // m_decal_mat is never read), so the marks are small grids in a ring of batch meshes,
    // drawn with the build's unlit Sprites/Default: a black mark then darkens any lighting.
    // Not Particles/Alpha Blended: its soft-particle variant fades by the depth gap to the
    // wall behind, and at WALL_OFFSET that leaves a mark about 1% opaque.
    //
    // A batch takes the marks of a short time window and fades as one through the material
    // color, so a frame without new hits uploads nothing and costs one draw call per live batch.
    static class ImpactMarks
    {
        public const string KEY_ENABLED = "GFX_IMPACT_MARKS";

        private const int BATCHES = 16;
        private const int MARKS_PER_BATCH = 64;     // 1024 marks in all
        private const float BATCH_SECONDS = 2f;     // so the marks of one batch fade at most 2 s apart
        private const int GRID = 5;                 // vertices per side; odd, so one sits on the hit point
        private const int VERTS = GRID * GRID;
        private const float HOLD_SECONDS = 60f;
        private const float FADE_SECONDS = 5f;
        private const float WALL_OFFSET = 0.02f;    // metres off the wall: against z-fighting, and the grid's chords across a bump
        private const int LEVEL_MASK = 1 << (int)UnityObjectLayers.LEVEL;
        private const int PLACED_PER_FRAME = 8;     // at most 200 raycasts in a frame, however much fire lands at once

        public static int enabledVal = 1;

        private class Batch
        {
            public readonly Vector3[] vertices = new Vector3[MARKS_PER_BATCH * VERTS];
            public readonly Color[] colors = new Color[MARKS_PER_BATCH * VERTS];
            public Mesh mesh;
            public int count;
            public float startTime;
            public bool changed;
        }

        private static readonly Batch[] batches = new Batch[BATCHES];
        private static int currentBatch;
        private static Material material;
        private static MaterialPropertyBlock fadeBlock;
        private static bool shaderMissing;
        private static Vector2[] batchUvs;
        private static int[] batchTriangles;

        // Hits wait here for Draw, which places PLACED_PER_FRAME of them a frame: a vortex or flak
        // burst then costs a few frames of raycasts, not one long one, and none run in the physics
        // callback that reports the hit. A hit arriving at a full queue leaves no mark.
        private struct PendingHit
        {
            public Vector3 point;
            public Vector3 normal;
            public float radius;
        }
        private static readonly PendingHit[] pendingHits = new PendingHit[64];
        private static int pendingFirst;
        private static int pendingCount;

        private static readonly int colorId = Shader.PropertyToID("_Color");

        // scratch for Place, so a hit allocates nothing
        private static readonly Vector3[] onPlane = new Vector3[VERTS];
        private static readonly Vector3[] placed = new Vector3[VERTS];
        private static readonly float[] alphas = new float[VERTS];

        public static void LoadPrefs()
        {
            enabledVal = ModPrefs.GetInt(KEY_ENABLED, enabledVal) == 1 ? 1 : 0;
            if (enabledVal == 0)
                Clear();
        }

        public static void StorePrefs()
        {
            ModPrefs.SetInt(KEY_ENABLED, enabledVal);
        }

        // A row on the PERFORMANCE page of ADVANCED GRAPHICS OPTIONS, below GpuPerf's three.
        public static void DrawOption(UIElement uie, Vector2 position)
        {
            uie.SelectAndDrawStringOptionItem(Loc.LS("IMPACT MARKS"), position, 3, MenuManager.GetToggleSetting(enabledVal), Loc.LS("SHOTS LEAVE SCORCH MARKS ON WALLS THAT FADE AFTER A MINUTE"));
        }

        public static void HandleSelection(int selection)
        {
            if (selection != 3)
                return;
            enabledVal = 1 - enabledVal;
            if (enabledVal == 0)
                Clear();
            MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
        }

        public static void OnProjectileHit(Projectile projectile, GameObject collider, Vector3 point, Vector3 normal)
        {
            if (!projectile.m_alive || collider.layer != (int)UnityObjectLayers.LEVEL)
                return;
            // flares, and creepers and timebombs, which roll and bounce along the walls before they blow up
            switch (projectile.m_type)
            {
                case ProjPrefab.proj_flare:
                case ProjPrefab.proj_flare_sticky:
                case ProjPrefab.missile_creeper:
                case ProjPrefab.missile_timebomb:
                    return;
            }

            // m_lava_sz is the prefab's own impact size, the one the engine picks its lava splash by
            Add(point, normal, Mathf.Clamp(0.3f * projectile.m_lava_sz, 0.12f, 0.8f));
        }

        // The massdriver beam is not a Projectile. A relayed beam carries no normal, so it is traced again.
        public static void OnBeamHit(Vector3 start, Vector3 end)
        {
            if (enabledVal == 0)
                return;
            Vector3 beam = end - start;
            RaycastHit hit;
            if (Physics.Raycast(start, beam.normalized, out hit, beam.magnitude + 0.1f, LEVEL_MASK, QueryTriggerInteraction.Ignore))
                Add(hit.point, hit.normal, 0.25f);
        }

        private static void Add(Vector3 point, Vector3 normal, float radius)
        {
            if (enabledVal == 0 || GameplayManager.IsDedicatedServer() || pendingCount == pendingHits.Length)
                return;
            pendingHits[(pendingFirst + pendingCount) % pendingHits.Length] = new PendingHit { point = point, normal = normal, radius = radius };
            pendingCount++;
        }

        private static void Place(Vector3 point, Vector3 normal, float radius)
        {
            Vector3 anyAxis = Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right;
            Vector3 right = Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * Vector3.Cross(normal, anyAxis).normalized * radius;
            Vector3 up = Vector3.Cross(normal, right);
            for (int y = 0; y < GRID; y++)
                for (int x = 0; x < GRID; x++)
                    onPlane[y * GRID + x] = point + right * (2f * x / (GRID - 1) - 1f) + up * (2f * y / (GRID - 1) - 1f);

            // ---- flat test: most hits land on a flat face, and nine rays on every other grid point prove it ----
            RaycastHit centerHit;
            bool flat = DropOntoSurface(onPlane[VERTS / 2], normal, out centerHit);
            for (int y = 0; flat && y < GRID; y += 2)
            {
                for (int x = 0; flat && x < GRID; x += 2)
                {
                    RaycastHit hit;
                    flat = DropOntoSurface(onPlane[y * GRID + x], normal, out hit)
                        && Vector3.Dot(hit.normal, centerHit.normal) > 0.999f
                        && Mathf.Abs(Vector3.Dot(hit.point - centerHit.point, centerHit.normal)) < 0.005f;
                }
            }

            // ---- place the grid: on the proven plane, else each vertex dropped onto the collider ----
            // Stock levels are deformed and carry decal geometry off the wall (the editor's 816
            // decal meshes reach 0.125 m at the median, 0.25 m at the upper quartile), which a
            // flat quad cuts into. A vertex that finds no surface facing the shot (an edge, a
            // hole) turns transparent instead of hanging in the air.
            int verticesOnSurface = 0;
            for (int v = 0; v < VERTS; v++)
            {
                RaycastHit hit;
                if (flat)
                {
                    placed[v] = onPlane[v] - centerHit.normal * (Vector3.Dot(onPlane[v] - centerHit.point, centerHit.normal) - WALL_OFFSET);
                    alphas[v] = 1f;
                }
                else if (DropOntoSurface(onPlane[v], normal, out hit))
                {
                    placed[v] = hit.point + hit.normal * WALL_OFFSET;
                    alphas[v] = 1f;
                }
                else
                {
                    placed[v] = onPlane[v] + normal * WALL_OFFSET;
                    alphas[v] = 0f;
                }
                if (alphas[v] > 0f)
                    verticesOnSurface++;
            }
            if (verticesOnSurface == 0)
                return;

            // ---- store: a full or too old batch hands over to the next, oldest one in the ring ----
            Batch batch = batches[currentBatch];
            if (batch.count > 0 && (batch.count == MARKS_PER_BATCH || Time.time - batch.startTime > BATCH_SECONDS))
            {
                currentBatch = (currentBatch + 1) % BATCHES;
                batch = batches[currentBatch];
                batch.count = 0;
            }
            if (batch.count == 0)
            {
                // the grids of the marks this batch held before collapse to the origin
                System.Array.Clear(batch.vertices, 0, batch.vertices.Length);
                batch.startTime = Time.time;
            }
            int first = batch.count * VERTS;
            for (int v = 0; v < VERTS; v++)
            {
                batch.vertices[first + v] = placed[v];
                batch.colors[first + v] = new Color(1f, 1f, 1f, alphas[v]);
            }
            batch.count++;
            batch.changed = true;
        }

        // A ray along -normal from 0.3 m in front, reaching 0.3 m behind; true on a level face that faces the shot.
        private static bool DropOntoSurface(Vector3 onPlane, Vector3 normal, out RaycastHit hit)
        {
            return Physics.Raycast(onPlane + normal * 0.3f, -normal, out hit, 0.6f, LEVEL_MASK, QueryTriggerInteraction.Ignore)
                && Vector3.Dot(hit.normal, normal) > 0.5f;
        }

        public static void Clear()
        {
            foreach (Batch batch in batches)
                if (batch != null)
                    batch.count = 0;
            currentBatch = 0;
            pendingCount = 0;
        }

        public static void Draw()
        {
            if (enabledVal == 0)
                return;
            if (material == null && (shaderMissing || !CreateMaterial()))
                return;
            // set when a level loads (LevelData.cs:162, Player.cs:4006), so null in the menus before the first one
            if (GameManager.m_viewer == null)
                return;

            for (int placedThisFrame = 0; placedThisFrame < PLACED_PER_FRAME && pendingCount > 0; placedThisFrame++)
            {
                PendingHit hit = pendingHits[pendingFirst];
                pendingFirst = (pendingFirst + 1) % pendingHits.Length;
                pendingCount--;
                Place(hit.point, hit.normal, hit.radius);
            }

            float now = Time.time;
            Camera camera = GameManager.m_viewer.c_camera;
            foreach (Batch batch in batches)
            {
                if (batch.count == 0)
                    continue;
                float alpha = 1f - Mathf.Clamp01((now - batch.startTime - HOLD_SECONDS) / FADE_SECONDS);
                if (alpha <= 0f)
                {
                    batch.count = 0;
                    continue;
                }
                if (batch.changed)
                {
                    UploadMesh(batch);
                    batch.changed = false;
                }
                fadeBlock.SetColor(colorId, new Color(1f, 1f, 1f, alpha));
                // the viewer camera only, so the rear view does not draw the marks
                Graphics.DrawMesh(batch.mesh, Matrix4x4.identity, material, 0, camera, 0, fadeBlock, false, false);
            }
        }

        private static void UploadMesh(Batch batch)
        {
            batch.mesh.vertices = batch.vertices;
            batch.mesh.colors = batch.colors;
            batch.mesh.RecalculateBounds();
        }

        private static bool CreateMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.Log("[ImpactMarks] shader Sprites/Default not found, marks disabled");
                shaderMissing = true;
                return false;
            }
            material = new Material(shader);
            material.mainTexture = CreateTexture();
            fadeBlock = new MaterialPropertyBlock();

            // every batch shares one layout: MARKS_PER_BATCH grids, unused ones collapsed to the origin
            batchUvs = new Vector2[MARKS_PER_BATCH * VERTS];
            batchTriangles = new int[MARKS_PER_BATCH * (GRID - 1) * (GRID - 1) * 6];
            int t = 0;
            for (int mark = 0; mark < MARKS_PER_BATCH; mark++)
            {
                for (int y = 0; y < GRID; y++)
                    for (int x = 0; x < GRID; x++)
                        batchUvs[mark * VERTS + y * GRID + x] = new Vector2((float)x / (GRID - 1), (float)y / (GRID - 1));
                for (int y = 0; y < GRID - 1; y++)
                {
                    for (int x = 0; x < GRID - 1; x++)
                    {
                        int v = mark * VERTS + y * GRID + x;
                        batchTriangles[t++] = v;
                        batchTriangles[t++] = v + GRID;
                        batchTriangles[t++] = v + GRID + 1;
                        batchTriangles[t++] = v;
                        batchTriangles[t++] = v + GRID + 1;
                        batchTriangles[t++] = v + 1;
                    }
                }
            }

            // all batches up front, at the first frame after startup, so a match never allocates for marks
            for (int i = 0; i < BATCHES; i++)
            {
                var batch = new Batch();
                batch.mesh = new Mesh();
                batch.mesh.MarkDynamic();
                batch.mesh.vertices = batch.vertices;
                batch.mesh.uv = batchUvs;
                batch.mesh.triangles = batchTriangles;
                batches[i] = batch;
            }
            return true;
        }

        // A black blob with a ragged edge and a darker core; the edge varies with the angle.
        private static Texture2D CreateTexture()
        {
            const int SIZE = 64;
            var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float dx = (x + 0.5f) / SIZE * 2f - 1f;
                    float dy = (y + 0.5f) / SIZE * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);
                    float edge = 0.7f + 0.25f * Mathf.PerlinNoise(3f + Mathf.Cos(angle) * 1.5f, 7f + Mathf.Sin(angle) * 1.5f);
                    float alpha = Mathf.Clamp01((edge - r) / 0.3f) * (0.55f + 0.35f * Mathf.Clamp01(1f - r * 2f));
                    alpha *= 0.8f + 0.2f * Mathf.PerlinNoise(x * 0.25f, y * 0.25f);
                    texture.SetPixel(x, y, new Color(0.02f, 0.015f, 0.01f, alpha));
                }
            }
            texture.Apply(true, true);
            return texture;
        }
    }

    // Read the pilot's IMPACT MARKS setting
    [HarmonyPatch(typeof(ModPrefs), "Load")]
    class ImpactMarks_ModPrefs_Load
    {
        static void Postfix()
        {
            ImpactMarks.LoadPrefs();
        }
    }

    // Write the setting before ModPrefs serialises its table
    [HarmonyPatch(typeof(ModPrefs), "Flush")]
    class ImpactMarks_ModPrefs_Flush
    {
        static void Prefix()
        {
            ImpactMarks.StorePrefs();
        }
    }
}
