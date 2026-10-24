using System.Collections.Generic;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod
{
    static class GfxQualitySettings
    {
        // setting options
        private static readonly int[] MaxQueuedValues = { 1, 2, 3, 4 };
        private static readonly int[] ParticleBudgetValues = { 8, 16, 64, 256, 1024, 4096 };
        private static readonly string[] ShadowModeNames = { "OFF", "HARD", "SOFT" };       // ShadowQuality
        private static readonly int[] ShadowDistanceValues = { 20, 35, 50, 75, 100 };
        private static readonly string[] ShadowResolutionNames = { "LOW", "MEDIUM", "HIGH", "VERY HIGH" };
        private static readonly int[] ShadowCascadeValues = { 1, 2, 4 };
        private static readonly string[] ShadowProjectionNames = { "CLOSE FIT", "STABLE FIT" };
        private static readonly float[] ShadowNearPlaneValues = { 0f, 1f, 2f, 3f };

        // loaded settings
        public static int maxQueuedIdx = -1;
        public static int softParticlesVal = -1;     
        public static int realtimeReflVal = -1;
        public static int particleBudgetIdx = -1;
        public static int shadowModeIdx = -1;         
        public static int shadowDistanceIdx = -1;
        public static int shadowResolutionIdx = -1;
        public static int shadowCascadeIdx = -1;
        public static int shadowProjectionIdx = -1;
        public static int shadowNearPlaneIdx = -1;

        private static bool initialized = false;
        private static int defMaxQueued, defParticleBudget;
        private static bool defSoftParticles, defRealtimeRefl;

        public static void ApplyQualitySettings(){
            if (GameplayManager.IsDedicatedServer())
                return;
            if (!initialized){
                defMaxQueued = QualitySettings.maxQueuedFrames;
                defSoftParticles = QualitySettings.softParticles;
                defRealtimeRefl = QualitySettings.realtimeReflectionProbes;
                defParticleBudget = QualitySettings.particleRaycastBudget;
                initialized = true;
            }
            QualitySettings.maxQueuedFrames = maxQueuedIdx >= 0 ? MaxQueuedValues[maxQueuedIdx] : defMaxQueued;
            QualitySettings.softParticles = softParticlesVal >= 0 ? softParticlesVal != 0 : defSoftParticles;
            QualitySettings.realtimeReflectionProbes = realtimeReflVal >= 0 ? realtimeReflVal != 0 : defRealtimeRefl;
            QualitySettings.particleRaycastBudget = particleBudgetIdx >= 0 ? ParticleBudgetValues[particleBudgetIdx] : defParticleBudget;
            MenuManager.SetGraphicsShadowQuality(MenuManager.gfx_shadow_quality);
            if (shadowModeIdx >= 0) QualitySettings.shadows = (ShadowQuality)shadowModeIdx;
            if (shadowDistanceIdx >= 0) QualitySettings.shadowDistance = ShadowDistanceValues[shadowDistanceIdx];
            if (shadowResolutionIdx >= 0) QualitySettings.shadowResolution = (ShadowResolution)shadowResolutionIdx;
            if (shadowCascadeIdx >= 0) QualitySettings.shadowCascades = ShadowCascadeValues[shadowCascadeIdx];
            if (shadowProjectionIdx >= 0) QualitySettings.shadowProjection = (ShadowProjection)shadowProjectionIdx;
            if (shadowNearPlaneIdx >= 0) QualitySettings.shadowNearPlaneOffset = ShadowNearPlaneValues[shadowNearPlaneIdx];
            ApplyConsoleValues();
        }

        // qs_set writes land here instead of on QualitySettings directly, so the
        // re-apply above repeats the console value on the next level load instead
        // of reverting it; cycling the same field in the menu takes ownership back
        private static readonly Dictionary<string, float> consoleValues = new Dictionary<string, float>();

        public static void SetFromConsole(string field, float value)
        {
            consoleValues[field] = value;
            ApplyConsoleValues();
        }

        private static void ApplyConsoleValues()
        {
            foreach (var entry in consoleValues)
            {
                float v = entry.Value;
                switch (entry.Key)
                {
                    case "maxqueuedframes": QualitySettings.maxQueuedFrames = (int)v; break;
                    case "softparticles": QualitySettings.softParticles = v != 0f; break;
                    case "realtimereflectionprobes": QualitySettings.realtimeReflectionProbes = v != 0f; break;
                    case "particleraycastbudget": QualitySettings.particleRaycastBudget = (int)v; break;
                    case "shadows": QualitySettings.shadows = (ShadowQuality)(int)v; break;
                    case "shadowdistance": QualitySettings.shadowDistance = v; break;
                    case "shadowresolution": QualitySettings.shadowResolution = (ShadowResolution)(int)v; break;
                    case "shadowcascades": QualitySettings.shadowCascades = (int)v; break;
                    case "shadowprojection": QualitySettings.shadowProjection = (ShadowProjection)(int)v; break;
                    case "shadownearplaneoffset": QualitySettings.shadowNearPlaneOffset = v; break;
                }
            }
        }

        private const string DEFAULT = "DEFAULT";
        private static string Toggle(int v) { return v < 0 ? Loc.LS(DEFAULT) : MenuManager.GetToggleSetting(v); }

        public static string GetMaxQueued() { return maxQueuedIdx < 0 ? Loc.LS(DEFAULT) : MaxQueuedValues[maxQueuedIdx].ToString(); }
        public static string GetSoftParticles() { return Toggle(softParticlesVal); }
        public static string GetRealtimeReflections() { return Toggle(realtimeReflVal); }
        public static string GetParticleBudget() { return particleBudgetIdx < 0 ? Loc.LS(DEFAULT) : ParticleBudgetValues[particleBudgetIdx].ToString(); }
        public static string GetShadowMode() { return shadowModeIdx < 0 ? Loc.LS(DEFAULT) : Loc.LS(ShadowModeNames[shadowModeIdx]); }
        public static string GetShadowDistance() { return shadowDistanceIdx < 0 ? Loc.LS(DEFAULT) : ShadowDistanceValues[shadowDistanceIdx].ToString(); }
        public static string GetShadowResolution() { return shadowResolutionIdx < 0 ? Loc.LS(DEFAULT) : Loc.LS(ShadowResolutionNames[shadowResolutionIdx]); }
        public static string GetShadowCascades() { return shadowCascadeIdx < 0 ? Loc.LS(DEFAULT) : ShadowCascadeValues[shadowCascadeIdx].ToString(); }
        public static string GetShadowProjection() { return shadowProjectionIdx < 0 ? Loc.LS(DEFAULT) : Loc.LS(ShadowProjectionNames[shadowProjectionIdx]); }
        public static string GetShadowNearPlane() { return shadowNearPlaneIdx < 0 ? Loc.LS(DEFAULT) : ShadowNearPlaneValues[shadowNearPlaneIdx].ToString("0.##"); }

        public static int CycleDefault(int value, int dir, int len){
            int total = len + 1;
            return ((value + 1 + dir) % total + total) % total - 1;
        }

        // Persistence goes through olmod's ModPrefs (<pilot>.xprefsmod), not
        // MenuManager.SavePreferences. A postfix on SavePreferences never
        // reaches disk: that method is DeleteAll() ... LocalSetInt ... Flush(),
        // Flush serialises the hashtable, and the next save clears it before
        // the postfix runs again - so the keys were written to the table and
        // dropped, every time. ModPrefs.Flush does not clear first, so a
        // prefix on it is in the table by the time it is serialised.

        public static void LoadPrefs()
        {
            maxQueuedIdx = Clamp(ModPrefs.GetInt("GFX_QS_MAXQUEUED", -1), MaxQueuedValues.Length);
            softParticlesVal = Clamp(ModPrefs.GetInt("GFX_QS_SOFT_PARTICLES", -1), 2);
            realtimeReflVal = Clamp(ModPrefs.GetInt("GFX_QS_RT_REFLECTIONS", -1), 2);
            particleBudgetIdx = Clamp(ModPrefs.GetInt("GFX_QS_PARTICLE_BUDGET", -1), ParticleBudgetValues.Length);
            shadowModeIdx = Clamp(ModPrefs.GetInt("GFX_QS_SHADOW_MODE", -1), ShadowModeNames.Length);
            shadowDistanceIdx = Clamp(ModPrefs.GetInt("GFX_QS_SHADOW_DIST", -1), ShadowDistanceValues.Length);
            shadowResolutionIdx = Clamp(ModPrefs.GetInt("GFX_QS_SHADOW_RES", -1), ShadowResolutionNames.Length);
            shadowCascadeIdx = Clamp(ModPrefs.GetInt("GFX_QS_SHADOW_CASCADES", -1), ShadowCascadeValues.Length);
            shadowProjectionIdx = Clamp(ModPrefs.GetInt("GFX_QS_SHADOW_PROJ", -1), ShadowProjectionNames.Length);
            shadowNearPlaneIdx = Clamp(ModPrefs.GetInt("GFX_QS_SHADOW_NEARPLANE", -1), ShadowNearPlaneValues.Length);
            ApplyQualitySettings();
        }

        public static void StorePrefs()
        {
            ModPrefs.SetInt("GFX_QS_MAXQUEUED", maxQueuedIdx);
            ModPrefs.SetInt("GFX_QS_SOFT_PARTICLES", softParticlesVal);
            ModPrefs.SetInt("GFX_QS_RT_REFLECTIONS", realtimeReflVal);
            ModPrefs.SetInt("GFX_QS_PARTICLE_BUDGET", particleBudgetIdx);
            ModPrefs.SetInt("GFX_QS_SHADOW_MODE", shadowModeIdx);
            ModPrefs.SetInt("GFX_QS_SHADOW_DIST", shadowDistanceIdx);
            ModPrefs.SetInt("GFX_QS_SHADOW_RES", shadowResolutionIdx);
            ModPrefs.SetInt("GFX_QS_SHADOW_CASCADES", shadowCascadeIdx);
            ModPrefs.SetInt("GFX_QS_SHADOW_PROJ", shadowProjectionIdx);
            ModPrefs.SetInt("GFX_QS_SHADOW_NEARPLANE", shadowNearPlaneIdx);
        }

        private static int Clamp(int idx, int len)
        {
            return Mathf.Clamp(idx, -1, len - 1);
        }

        // The QUALITY SETTINGS page of ADVANCED GRAPHICS OPTIONS. The menu
        // itself is owned by GpuPerformance.cs, which paginates it; this is
        // only the one page, so the settings and their UI stay together.

        public static void DrawPage(UIElement uie, ref Vector2 position)
        {
            uie.SelectAndDrawStringOptionItem(Loc.LS("MAX QUEUED FRAMES"), position, 0, GetMaxQueued(), Loc.LS("FRAMES THE CPU MAY QUEUE AHEAD OF THE GPU; LOWER REDUCES INPUT LAG"));
            position.y += 48f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("SOFT PARTICLES"), position, 1, GetSoftParticles(), Loc.LS("FADE PARTICLES NEAR GEOMETRY (COSTS DEPTH TEXTURE)"));
            position.y += 48f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("REALTIME REFLECTIONS"), position, 2, GetRealtimeReflections(), Loc.LS("UPDATE REFLECTION PROBES EVERY FRAME"));
            position.y += 48f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("PARTICLE RAYCAST BUDGET"), position, 3, GetParticleBudget(), Loc.LS("MAX RAYCASTS FOR APPROXIMATE PARTICLE COLLISIONS"));
            position.y += 48f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("SHADOW MODE"), position, 4, GetShadowMode(), Loc.LS("OFF/HARD/SOFT SHADOWS (OVERRIDES SHADOW SETTINGS)"));
            position.y += 48f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("SHADOW DISTANCE"), position, 5, GetShadowDistance(), Loc.LS("MAX DISTANCE SHADOWS ARE DRAWN (OVERRIDES SHADOW SETTINGS)"));
            position.y += 48f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("SHADOW RESOLUTION"), position, 6, GetShadowResolution(), Loc.LS("SHADOW MAP RESOLUTION (OVERRIDES SHADOW SETTINGS)"));
            position.y += 48f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("SHADOW CASCADES"), position, 7, GetShadowCascades(), Loc.LS("DIRECTIONAL SHADOW CASCADE COUNT (OVERRIDES SHADOW SETTINGS)"));
            position.y += 48f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("SHADOW PROJECTION"), position, 8, GetShadowProjection(), Loc.LS("STABLE FIT REDUCES SHADOW EDGE SHIMMER"));
            position.y += 48f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("SHADOW NEAR PLANE OFFSET"), position, 9, GetShadowNearPlane(), Loc.LS("REDUCES SHADOW SELF-SHADOWING ARTIFACTS"));
        }

        // Every item cycles through its own values plus a DEFAULT step, which
        // is what the -1 means.
        // the menu item order on the page; cycling an item takes the field back
        // from any qs_set console override
        private static readonly string[] ConsoleFieldBySelection = {
            "maxqueuedframes", "softparticles", "realtimereflectionprobes", "particleraycastbudget",
            "shadows", "shadowdistance", "shadowresolution", "shadowcascades", "shadowprojection",
            "shadownearplaneoffset" };

        public static void HandleSelection(int selection)
        {
            int dir = UIManager.m_select_dir;
            switch (selection)
            {
                case 0: maxQueuedIdx = CycleDefault(maxQueuedIdx, dir, MaxQueuedValues.Length); break;
                case 1: softParticlesVal = CycleDefault(softParticlesVal, dir, 2); break;
                case 2: realtimeReflVal = CycleDefault(realtimeReflVal, dir, 2); break;
                case 3: particleBudgetIdx = CycleDefault(particleBudgetIdx, dir, ParticleBudgetValues.Length); break;
                case 4: shadowModeIdx = CycleDefault(shadowModeIdx, dir, ShadowModeNames.Length); break;
                case 5: shadowDistanceIdx = CycleDefault(shadowDistanceIdx, dir, ShadowDistanceValues.Length); break;
                case 6: shadowResolutionIdx = CycleDefault(shadowResolutionIdx, dir, ShadowResolutionNames.Length); break;
                case 7: shadowCascadeIdx = CycleDefault(shadowCascadeIdx, dir, ShadowCascadeValues.Length); break;
                case 8: shadowProjectionIdx = CycleDefault(shadowProjectionIdx, dir, ShadowProjectionNames.Length); break;
                case 9: shadowNearPlaneIdx = CycleDefault(shadowNearPlaneIdx, dir, ShadowNearPlaneValues.Length); break;
                default: return;
            }
            consoleValues.Remove(ConsoleFieldBySelection[selection]);
            MenuManager.PlayCycleSound(1f, dir);
            ApplyQualitySettings();
        }

        [HarmonyPatch(typeof(MenuManager), "ApplyAllGraphicsSettings")]
        class GfxQuality_ApplyAllGraphicsSettings{
            static void Postfix() { ApplyQualitySettings(); }
        }

        [HarmonyPatch(typeof(GameplayManager), "StartLevel")]
        class GfxQuality_StartLevel{
            static void Postfix() { ApplyQualitySettings(); }
        }
    }

    [HarmonyPatch(typeof(ModPrefs), "Load")]
    class GfxQuality_ModPrefs_Load
    {
        static void Postfix() { GfxQualitySettings.LoadPrefs(); }
    }

    [HarmonyPatch(typeof(ModPrefs), "Flush")]
    class GfxQuality_ModPrefs_Flush
    {
        static void Prefix() { GfxQualitySettings.StorePrefs(); }
    }
}
