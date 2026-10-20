using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HighlightingSystem;
using Overload;
using UnityEngine;
using UnityEngine.Rendering;
using UnityStandardAssets.ImageEffects;

namespace GameMod
{
    // GPU performance optimizations, all in one file.
    //
    //   SKIP IDLE FX PASSES  - stops paying for helper cameras and full-screen
    //                          effect passes that are not doing anything.
    //   DISABLE REFLECTIONS  - drops the deferred reflection shader pass.
    //   COMBINE SHIP MESHES  - merges other players' hull parts per material.
    //
    // All three apply and revert live. They are stored per pilot through olmod's
    // ModPrefs (the "<pilot>.xprefsmod" file), written from a ModPrefs.Flush
    // prefix and read from a ModPrefs.Load postfix so the ordering against
    // MPSetup's own pass is deterministic.

    static class GpuPerf
    {
        public const string KEY_IDLE_FX = "GFX_PERF_IDLE_FX";
        public const string KEY_REFL_OFF = "GFX_PERF_REFL_OFF";
        public const string KEY_SHIP_COMBINE = "GFX_PERF_SHIP_COMBINE";

        public static int idleFxVal = 1;
        public static int deferredReflVal = 0;
        public static int shipCombineVal = 1;

        public static void LoadPrefs()
        {
            idleFxVal = ModPrefs.GetInt(KEY_IDLE_FX, idleFxVal) == 1 ? 1 : 0;
            deferredReflVal = ModPrefs.GetInt(KEY_REFL_OFF, deferredReflVal) == 1 ? 1 : 0;
            shipCombineVal = ModPrefs.GetInt(KEY_SHIP_COMBINE, shipCombineVal) == 1 ? 1 : 0;
            DeferredReflections.Apply();
        }

        public static void StorePrefs()
        {
            ModPrefs.SetInt(KEY_IDLE_FX, idleFxVal);
            ModPrefs.SetInt(KEY_REFL_OFF, deferredReflVal);
            ModPrefs.SetInt(KEY_SHIP_COMBINE, shipCombineVal);
        }

        // The PERFORMANCE page of ADVANCED GRAPHICS OPTIONS.  The menu itself is
        // owned by Menus.cs, which paginates it; this is only the one page, so
        // these three toggles and their UI stay together.

        public static void DrawPage(UIElement uie, ref Vector2 position)
        {
            uie.SelectAndDrawStringOptionItem(Loc.LS("SKIP IDLE FX PASSES"), position, 0, MenuManager.GetToggleSetting(idleFxVal), Loc.LS("REMOVE OR SKIP EFFECT CAMERAS WHEN POSSIBLE"));
            position.y += 62f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("DISABLE REFLECTIONS"), position, 1, MenuManager.GetToggleSetting(deferredReflVal), Loc.LS("DISABLES THE DEFERRED REFLECTIONS RENDER PASS"));
            position.y += 62f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("COMBINE SHIP MESHES"), position, 2, MenuManager.GetToggleSetting(shipCombineVal), Loc.LS("MERGES SHIP COMPONENTS INTO ONE COMPLETE MESH"));
            // The separator, tooltip and page controls are placed relative to
            // the LAST item drawn, so a short page has to pin the cursor where
            // a full one would have ended.
            position.y = -232.5f + 7f * 62f;
        }

        public static void HandleSelection(int selection)
        {
            switch (selection)
            {
                case 0: idleFxVal = 1 - idleFxVal; break;
                case 1: deferredReflVal = 1 - deferredReflVal; DeferredReflections.Apply(); break;
                case 2: shipCombineVal = 1 - shipCombineVal; break;
                default: return;
            }
            MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
        }
    }

    [HarmonyPatch(typeof(ModPrefs), "Load")]
    class GpuPerf_ModPrefs_Load
    {
        // GamePreferences.Load clears the table first, so a pilot with no
        // olmod prefs file simply falls back to the defaults above.
        static void Postfix()
        {
            GpuPerf.LoadPrefs();
        }
    }

    [HarmonyPatch(typeof(ModPrefs), "Flush")]
    class GpuPerf_ModPrefs_Flush
    {
        // A prefix, so the values are in the hashtable no matter which of the
        // SavePreferences patches ends up triggering the flush.
        static void Prefix()
        {
            GpuPerf.StorePrefs();
        }
    }

    // ------------------------------------------------------------------
    // SKIP IDLE FX PASSES
    // ------------------------------------------------------------------

    static class IdleFxSkip
    {
        public static bool On { get { return GpuPerf.idleFxVal == 1; } }

        private static RadialBlur radialBlur;
        private static DrunkBlur drunkBlur;
        private static Camera flareCam;
        private static ProFlareBatch viewerBatch;
        private static ProFlareBatch[] flareBatches;
        private static bool lastTrim;
        private static int trimRefresh;
        private static int lastScreenW;
        private static int lastScreenH;

        public static void RegisterCamera(Camera cam, ProFlareBatch pfb)
        {
            viewerBatch = pfb;
            radialBlur = cam.GetComponent<RadialBlur>();
            drunkBlur = cam.GetComponent<DrunkBlur>();
        }

        private static void RefreshFlareRefs()
        {
            flareCam = null;
            PlayerShip ship = GameManager.m_player_ship;
            if (ship != null && ship.m_flare_cam_object != null)
                flareCam = ship.m_flare_cam_object.GetComponentInChildren<Camera>();
            flareBatches = Object.FindObjectsOfType<ProFlareBatch>();
        }

        private static bool BatchVisible(ProFlareBatch fb)
        {
            if (fb == null)
                return false;
            List<FlareData> list = fb.FlaresList;
            for (int i = 0, n = list.Count; i < n; i++)
                if (list[i] != null && list[i].flare != null && list[i].flare.isVisible)
                    return true;
            return false;
        }

        private static bool AnyFlareVisible()
        {
            if (BatchVisible(viewerBatch))
                return true;
            if (flareBatches == null)
                return true;
            for (int i = 0; i < flareBatches.Length; i++)
                if (flareBatches[i] != viewerBatch && BatchVisible(flareBatches[i]))
                    return true;
            return false;
        }

        public static void Tick()
        {
            bool trim = On;
            // UIManager raycasts mouse clicks through this camera; a disabled camera's
            // screen rect goes stale on window resize, so let it render one frame after one.
            bool resized = Screen.width != lastScreenW || Screen.height != lastScreenH;
            if (resized)
            {
                lastScreenW = Screen.width;
                lastScreenH = Screen.height;
            }
            Camera colCam = UIManager.gm != null ? UIManager.gm.m_ui_collision_camera : null;
            if (colCam != null)
            {
                bool want = !trim || resized;
                if (colCam.enabled != want)
                    colCam.enabled = want;
            }
            if (flareCam != null && !GameplayManager.VRActive)
            {
                bool want = !trim || AnyFlareVisible();
                if (flareCam.enabled != want)
                    flareCam.enabled = want;
            }
            if (radialBlur != null)
            {
                bool want = !trim || radialBlur.blurStrength > 0f;
                if (radialBlur.enabled != want)
                    radialBlur.enabled = want;
            }
            if (drunkBlur != null)
            {
                bool want = !trim || drunkBlur.strength > 0f || drunkBlur.overbrighten != 1f;
                if (drunkBlur.enabled != want)
                    drunkBlur.enabled = want;
            }
            if (trim != lastTrim || (trim && --trimRefresh <= 0))
            {
                foreach (Camera c in Camera.allCameras)
                    c.eventMask = trim ? 0 : -1;
                if (GameManager.m_gm != null)
                    GameManager.m_gm.useGUILayout = !trim;
                if (GameManager.m_player_ship != null)
                    GameManager.m_player_ship.useGUILayout = !trim;
                RefreshFlareRefs();
                trimRefresh = 120;
                lastTrim = trim;
            }
        }
    }

    [HarmonyPatch(typeof(Viewer), "Awake")]
    class IdleFxSkip_Viewer_Awake
    {
        static void Postfix(Viewer __instance)
        {
            if (__instance.c_camera != null)
                IdleFxSkip.RegisterCamera(__instance.c_camera, __instance.c_pfb);
        }
    }

    // The highlighting system re-renders its buffer every frame even when no
    // highlighter is on screen; clear it once and skip the pass until one is.
    [HarmonyPatch(typeof(HighlightingBase), "OnPreRender")]
    class IdleFxSkip_HighlightingBase_OnPreRender
    {
        private static readonly FieldInfo _highlighters = AccessTools.Field(typeof(Highlighter), "highlighters");
        private static readonly HashSet<int> armed = new HashSet<int>();
        private static readonly Dictionary<Highlighter, Renderer> renderers = new Dictionary<Highlighter, Renderer>();

        private static bool AnyHighlighterVisible(HashSet<Highlighter> hs)
        {
            foreach (Highlighter h in hs)
            {
                if (h == null)
                    continue;
                Renderer r;
                if (!renderers.TryGetValue(h, out r))
                    renderers[h] = r = h.GetComponentInChildren<Renderer>();
                if (r == null || !r.gameObject.activeInHierarchy || r.isVisible)
                    return true;
            }
            if (renderers.Count > hs.Count * 4 + 16)
                renderers.Clear();
            return false;
        }

        static bool Prefix(HighlightingBase __instance,
            CommandBuffer ___renderBuffer, RenderTexture ___highlightingBuffer)
        {
            int id = __instance.GetInstanceID();
            if (!IdleFxSkip.On)
            {
                armed.Remove(id);
                return true;
            }
            HashSet<Highlighter> hs = (HashSet<Highlighter>)_highlighters.GetValue(null);
            if (hs == null || (hs.Count > 0 && AnyHighlighterVisible(hs)))
            {
                armed.Remove(id);
                return true;
            }
            if (___renderBuffer == null || ___highlightingBuffer == null || !___highlightingBuffer.IsCreated())
                return true;
            if (!armed.Contains(id))
            {
                ___renderBuffer.Clear();
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = ___highlightingBuffer;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = prev;
                armed.Add(id);
            }
            return false;
        }
    }

    // ------------------------------------------------------------------
    // DISABLE REFLECTIONS
    // ------------------------------------------------------------------

    static class DeferredReflections
    {
        private static bool forced;
        private static BuiltinShaderMode savedMode = BuiltinShaderMode.UseBuiltin;

        public static void Apply()
        {
            if (GameplayManager.IsDedicatedServer())
                return;
            if (GpuPerf.deferredReflVal == 1)
            {
                if (!forced)
                {
                    savedMode = GraphicsSettings.GetShaderMode(BuiltinShaderType.DeferredReflections);
                    forced = true;
                }
                GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredReflections, BuiltinShaderMode.Disabled);
            }
            else if (forced)
            {
                GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredReflections, savedMode);
                forced = false;
            }
        }
    }

    // ------------------------------------------------------------------
    // COMBINE SHIP MESHES
    // ------------------------------------------------------------------

    static class ShipMeshCombine
    {
        public static bool Enabled { get { return GpuPerf.shipCombineVal == 1; } }

        public static void CombineHull(GameObject root)
        {
            try
            {
                Transform ext = null;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("playership_ext")) { ext = t; break; }
                if (ext == null)
                    return;

                var groups = new Dictionary<Material, List<MeshRenderer>>();
                foreach (var r in ext.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy || r.name.StartsWith("bodyWing"))
                        continue;
                    var mats = r.sharedMaterials;
                    if (mats.Length != 1 || mats[0] == null)
                        continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null)
                        continue;
                    List<MeshRenderer> list;
                    if (!groups.TryGetValue(mats[0], out list))
                        groups[mats[0]] = list = new List<MeshRenderer>();
                    list.Add(r);
                }

                int made = 0;
                foreach (var kv in groups)
                {
                    var list = kv.Value;
                    if (list.Count < 2)
                        continue;
                    var combine = new CombineInstance[list.Count];
                    for (int i = 0; i < list.Count; i++)
                    {
                        combine[i] = new CombineInstance
                        {
                            mesh = list[i].GetComponent<MeshFilter>().sharedMesh,
                            transform = ext.worldToLocalMatrix * list[i].transform.localToWorldMatrix
                        };
                    }
                    var mesh = new Mesh { name = "combined_" + kv.Key.name };
                    mesh.CombineMeshes(combine, true, true);

                    var go = new GameObject("combined_hull_" + made++);
                    go.layer = list[0].gameObject.layer;
                    go.transform.SetParent(ext, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = kv.Key;
                    mr.shadowCastingMode = list[0].shadowCastingMode;
                    mr.receiveShadows = list[0].receiveShadows;

                    foreach (var r in list)
                        Object.Destroy(r);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("ShipMeshCombine: " + e);
            }
        }
    }

    // ------------------------------------------------------------------
    // shared per-frame / per-level hooks
    // ------------------------------------------------------------------

    [HarmonyPatch(typeof(GameManager), "Update")]
    class GpuPerf_GameManager_Update
    {
        static int frame;
        static readonly HashSet<int> combined = new HashSet<int>();

        static void Postfix()
        {
            IdleFxSkip.Tick();
            TickShipCombine();
        }

        static void TickShipCombine()
        {
            if (++frame % 60 != 0) return;
            if (!ShipMeshCombine.Enabled) return;
            if (GameplayManager.IsDedicatedServer()) return;
            if (GameplayManager.m_gameplay_state != GameplayState.PLAYING) return;
            if (combined.Count > 256) combined.Clear();

            foreach (var player in Overload.NetworkManager.m_Players)
            {
                if (player == null || player.isLocalPlayer || player.m_spectator) continue;
                var ship = player.c_player_ship;
                if (ship == null || ship.m_dying || player.m_cloaked) continue;
                int id = ship.GetInstanceID();
                if (combined.Contains(id)) continue;
                combined.Add(id);
                ShipMeshCombine.CombineHull(ship.gameObject);
            }
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "StartLevel")]
    class GpuPerf_GameplayManager_StartLevel
    {
        static void Postfix()
        {
            DeferredReflections.Apply();
        }
    }
}
