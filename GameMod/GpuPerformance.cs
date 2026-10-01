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

    // ==================================================================
    // UI
    // ==================================================================
    //
    // The three toggles get a third page, "PERFORMANCE", on ADVANCED GRAPHICS
    // OPTIONS. Both menu methods are replaced wholesale: the engine paginates
    // by a hardcoded 2, so a page cannot be added from the outside.

    [HarmonyPatch(typeof(UIElement), "DrawGraphicsAdvancedMenu")]
    class GpuPerfMenu_UIElement_DrawGraphicsAdvancedMenu
    {
        const int PAGES = 3;

        static bool Prefix(UIElement __instance)
        {
            UIManager.ui_bg_dark = true;
            Vector2 position = __instance.m_position;
            __instance.DrawMenuBG();
            UIElement.ToolTipActive = false;
            position.y = UIManager.UI_TOP + 20f;
            __instance.DrawHeaderMedium(position, Loc.LS("ADVANCED GRAPHICS OPTIONS"));
            switch (MenuManager.m_menu_micro_state)
            {
                case 0:
                    position.y += 40f;
                    __instance.DrawSmallHeader1(position, Loc.LS("FULL-SCREEN EFFECTS"), 250f);
                    position.y = -232.5f;
                    __instance.DrawMenuSeparator(position - Vector2.up * 40f);
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("ANISOTROPIC FILTER"), position, 2, MenuManager.GetToggleSetting(MenuManager.gfx_ani_filter), string.Empty);
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("AMBIENT OCCLUSION"), position, 3, MenuManager.GetToggleSetting(MenuManager.gfx_ssao), string.Empty);
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("ANTI-ALIASING"), position, 4, MenuManager.GetAntiAliasing(), string.Empty);
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("BLOOM"), position, 5, MenuManager.GetBloom(), string.Empty);
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("POST-PROCESSING"), position, 6, MenuManager.GetPostProcessing(), string.Empty);
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("SCREEN-SPACE REFLECTIONS"), position, 7, MenuManager.GetToggleSetting(MenuManager.gfx_ssr), string.Empty);
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("LENS FLARES"), position, 1, MenuManager.GetToggleSetting(MenuManager.gfx_lens_flare), (!GameplayManager.VRActive) ? string.Empty : Loc.LS("LENS FLARES DON'T WORK IN VR.  PLEASE DISABLE MANUALLY."));
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("TEXTURE RESOLUTION"), position, 0, MenuManager.GetTextureLevel(), string.Empty);
                    break;
                case 1:
                    position.y += 40f;
                    __instance.DrawSmallHeader1(position, Loc.LS("CUSTOM OPTIMIZATIONS"), 250f);
                    position.y = -232.5f;
                    __instance.DrawMenuSeparator(position - Vector2.up * 40f);
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("POWERUP LIGHT FADING"), position, 5, MenuManager.GetLightPowerupFade(), Loc.LS("POWERUP-CREATED LIGHTS FADE AT A DISTANCE"));
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("DISTANCE LIGHT FADING"), position, 3, MenuManager.GetToggleSetting(MenuManager.gfx_light_distance_fade), Loc.LS("LEVEL LIGHTS FADE AWAY AT A DISTANCE AND OFF-SCREEN"));
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("REDUCED SHADERS"), position, 7, MenuManager.GetToggleSetting(MenuManager.gfx_shaders_simpler), Loc.LS("USES ALTERNATE DISTORTION AND ROBOT SHADER (GAME RESTART REQUIRED)"));
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("REDUCED SHADOW LIGHTS"), position, 2, MenuManager.GetToggleSetting(MenuManager.gfx_reduced_shadows), Loc.LS("CHANGES SOME LEVEL LIGHTS TO NON-SHADOW-CASTING (LEVEL RESTART REQUIRED)"));
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("LIGHT RANGE"), position, 4, MenuManager.GetGFXReduction(MenuManager.gfx_light_reduced_range), Loc.LS("ALL LEVEL LIGHTS HAVE A SLIGHTLY SMALLER RANGE (LEVEL RESTART REQUIRED)"));
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("PARTICLE COUNT"), position, 6, MenuManager.GetGFXReduction(MenuManager.gfx_particles_reduced), Loc.LS("REDUCES COUNT OF MOST TYPES OF PARTICLES (GAME RESTART REQUIRED)"));
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("ROBOT EXPLOSIONS"), position, 1, MenuManager.GetGFXReduction(MenuManager.gfx_explosions_reduced), Loc.LS("AFFECTS AMOUNT OF DEBRIS AND SOME PARTICLE TYPES FROM EXPLOSIONS"));
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("EFFECTS LIGHTING"), position, 0, MenuManager.GetFXLighting(), Loc.LS("AFFECTS LIGHTS FOR PROJECTILES AND OTHER DYNAMIC LIGHTS"));
                    break;
                case 2:
                    position.y += 40f;
                    __instance.DrawSmallHeader1(position, Loc.LS("PERFORMANCE"), 250f);
                    position.y = -232.5f;
                    __instance.DrawMenuSeparator(position - Vector2.up * 40f);
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("SKIP IDLE FX PASSES"), position, 0, MenuManager.GetToggleSetting(GpuPerf.idleFxVal), Loc.LS("SKIPS HELPER/FLARE CAMERAS, IDLE BLUR/HIGHLIGHT PASSES AND MOUSE RAYCASTS; NO VISUAL CHANGE"));
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("DISABLE REFLECTIONS"), position, 1, MenuManager.GetToggleSetting(GpuPerf.deferredReflVal), Loc.LS("REMOVES BAKED REFLECTIONS; VISIBLE CHANGE ON GLOSSY SURFACES"));
                    position.y += 62f;
                    __instance.SelectAndDrawStringOptionItem(Loc.LS("COMBINE SHIP MESHES"), position, 2, MenuManager.GetToggleSetting(GpuPerf.shipCombineVal), Loc.LS("MERGES OTHER SHIPS' HULL PARTS FOR FEWER DRAW CALLS; NO VISUAL CHANGE; APPLIES TO NEW SHIPS"));
                    // The separator/tooltip/page controls below are placed
                    // relative to the LAST item drawn, so a short page has to
                    // pin the cursor where a full one would have ended.
                    position.y = -232.5f + 7f * 62f;
                    break;
            }
            __instance.DrawMenuSeparator(position + Vector2.up * 40f);
            __instance.DrawMenuToolTip(position + Vector2.up * 40f);
            __instance.DrawPageControls(position + Vector2.up * 85f,
                string.Format(Loc.LS("PAGE {0} OF {1}"), MenuManager.m_menu_micro_state + 1, PAGES),
                true, true, false, false, 420, 302);
            position.x = 0f;
            position.y = UIManager.UI_BOTTOM - 30f;
            __instance.SelectAndDrawItem(Loc.LS("BACK"), position, 100, false);
            __instance.MaybeShowMpStatus();
            return false;
        }
    }

    [HarmonyPatch(typeof(MenuManager), "GraphicsAdvancedUpdate")]
    class GpuPerfMenu_MenuManager_GraphicsAdvancedUpdate
    {
        const int PAGES = 3;

        private static readonly MethodInfo _GoBack = AccessTools.Method(typeof(MenuManager), "GoBack");
        private static readonly MethodInfo _PlayHighlightSound = AccessTools.Method(typeof(MenuManager), "PlayHighlightSound");
        private static readonly FieldInfo _m_menu_state_timer = AccessTools.Field(typeof(MenuManager), "m_menu_state_timer");

        static void GoBack()
        {
            _GoBack.Invoke(null, null);
            UIManager.DestroyAll();
            MenuManager.PlaySelectSound();
        }

        static bool Prefix()
        {
            MenuManager.UpdateMPStatus();
            UIManager.MouseSelectUpdate();
            switch (MenuManager.m_menu_sub_state)
            {
                case MenuSubState.INIT:
                    if ((float)_m_menu_state_timer.GetValue(null) > 0.25f)
                    {
                        UIManager.CreateUIElement(UIManager.SCREEN_CENTER, 7000, UIElementType.GRAPHICS_ADVANCED_MENU);
                        MenuManager.m_menu_sub_state = MenuSubState.ACTIVE;
                        MenuManager.SetDefaultSelection(0);
                        MenuManager.m_menu_micro_state = 0;
                    }
                    break;
                case MenuSubState.ACTIVE:
                    UIManager.ControllerMenu();
                    if (Controls.JustPressed(CCInput.MENU_SECONDARY))
                    {
                        MenuManager.m_mp_status_minimized = !MenuManager.m_mp_status_minimized;
                        MenuManager.PlayCycleSound();
                    }
                    if (Controls.JustPressed(CCInput.MENU_PGUP) || (UIManager.PushedSelect() && UIManager.m_menu_selection == 198))
                    {
                        _PlayHighlightSound.Invoke(null, new object[] { 0.4f, 0.05f });
                        MenuManager.UIPulse();
                        MenuManager.m_menu_micro_state = (MenuManager.m_menu_micro_state + PAGES - 1) % PAGES;
                        break;
                    }
                    if (Controls.JustPressed(CCInput.MENU_PGDN) || (UIManager.PushedSelect() && UIManager.m_menu_selection == 199))
                    {
                        _PlayHighlightSound.Invoke(null, new object[] { 0.4f, 0.05f });
                        MenuManager.UIPulse();
                        MenuManager.m_menu_micro_state = (MenuManager.m_menu_micro_state + 1) % PAGES;
                        break;
                    }
                    if (UIManager.PushedSelect(100) || (MenuManager.option_dir && UIManager.PushedDir()))
                    {
                        MenuManager.MaybeReverseOption();
                        switch (MenuManager.m_menu_micro_state)
                        {
                            case 0:
                                switch (UIManager.m_menu_selection)
                                {
                                    case 0:
                                        MenuManager.gfx_tex_level = (MenuManager.gfx_tex_level + 3 + UIManager.m_select_dir) % 3;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        MenuManager.SetTextureLevel(MenuManager.gfx_tex_level);
                                        break;
                                    case 1:
                                        MenuManager.gfx_lens_flare = (MenuManager.gfx_lens_flare + 1) % 2;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        MenuManager.SetCameraGraphics();
                                        break;
                                    case 2:
                                        MenuManager.gfx_ani_filter = (MenuManager.gfx_ani_filter + 1) % 2;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        MenuManager.SetAniFilter(MenuManager.gfx_ani_filter == 1);
                                        break;
                                    case 3:
                                        MenuManager.gfx_ssao = (MenuManager.gfx_ssao + 1) % 2;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        MenuManager.SetCameraGraphics();
                                        break;
                                    case 4:
                                        MenuManager.gfx_smaa = (MenuManager.gfx_smaa + 1) % 2;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        MenuManager.SetCameraGraphics();
                                        break;
                                    case 5:
                                        MenuManager.gfx_bloom = (MenuManager.gfx_bloom + 3 + UIManager.m_select_dir) % 3;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        MenuManager.SetCameraGraphics();
                                        break;
                                    case 6:
                                        MenuManager.gfx_post = (MenuManager.gfx_post + 4 + UIManager.m_select_dir) % 4;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        MenuManager.SetCameraGraphics();
                                        break;
                                    case 7:
                                        MenuManager.gfx_ssr = (MenuManager.gfx_ssr + 1) % 2;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        MenuManager.SetCameraGraphics();
                                        break;
                                    case 100:
                                        GoBack();
                                        break;
                                }
                                break;
                            case 1:
                                switch (UIManager.m_menu_selection)
                                {
                                    case 0:
                                        MenuManager.gfx_fx_lighting = (MenuManager.gfx_fx_lighting + 3 + UIManager.m_select_dir) % 3;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        break;
                                    case 1:
                                        MenuManager.gfx_explosions_reduced = (MenuManager.gfx_explosions_reduced + 2 + UIManager.m_select_dir) % 2;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        break;
                                    case 2:
                                        MenuManager.gfx_reduced_shadows = (MenuManager.gfx_reduced_shadows + 2 + UIManager.m_select_dir) % 2;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        break;
                                    case 3:
                                        MenuManager.gfx_light_distance_fade = (MenuManager.gfx_light_distance_fade + 2 + UIManager.m_select_dir) % 2;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        break;
                                    case 4:
                                        MenuManager.gfx_light_reduced_range = (MenuManager.gfx_light_reduced_range + 2 + UIManager.m_select_dir) % 2;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        break;
                                    case 5:
                                        MenuManager.gfx_light_powerup_fade = (MenuManager.gfx_light_powerup_fade + 3 + UIManager.m_select_dir) % 3;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        break;
                                    case 6:
                                        MenuManager.gfx_particles_reduced = (MenuManager.gfx_particles_reduced + 2 + UIManager.m_select_dir) % 2;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        break;
                                    case 7:
                                        MenuManager.gfx_shaders_simpler = (MenuManager.gfx_shaders_simpler + 2 + UIManager.m_select_dir) % 2;
                                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                        break;
                                    case 100:
                                        GoBack();
                                        break;
                                }
                                break;
                            case 2:
                                switch (UIManager.m_menu_selection)
                                {
                                    case 0:
                                        GpuPerf.idleFxVal = 1 - GpuPerf.idleFxVal;
                                        break;
                                    case 1:
                                        GpuPerf.deferredReflVal = 1 - GpuPerf.deferredReflVal;
                                        DeferredReflections.Apply();
                                        break;
                                    case 2:
                                        GpuPerf.shipCombineVal = 1 - GpuPerf.shipCombineVal;
                                        break;
                                    case 100:
                                        GoBack();
                                        break;
                                }
                                if (UIManager.m_menu_selection != 100)
                                    MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                                break;
                        }
                    }
                    MenuManager.UnReverseOption();
                    break;
            }
            return false;
        }
    }
}
