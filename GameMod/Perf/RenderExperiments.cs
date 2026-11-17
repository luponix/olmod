using Overload;
using System.Collections.Generic;
using UnityEngine;

namespace GameMod.Perf
{
    static class RenderExperiments
    {
        public static bool PhysicsAutoSyncOff;

        public static void ReadFlags()
        {
            PhysicsAutoSyncOff = Core.GameMod.FindArg("-physics-autosync-off");
            ApplyPhysicsAutoSync();
            NoBloom = Core.GameMod.FindArg("-nobloom");
            if (Core.GameMod.FindArgVal("-projlight-dist", out string pld) && float.TryParse(pld, out float pldv))
                ProjLightDist = Mathf.Max(0f, pldv);
            if (Core.GameMod.FindArgVal("-projlight-max", out string plm) && int.TryParse(plm, out int plmv))
                ProjLightMax = Mathf.Max(0, plmv);
            if (Core.GameMod.FindArgVal("-lightflash-dist", out string lfd) && float.TryParse(lfd, out float lfdv))
                LightFlashDist = Mathf.Max(0f, lfdv);
            ShipShadowsOff = Core.GameMod.FindArg("-ship-shadows-off");
            if (Core.GameMod.FindArgVal("-shiplight-dist", out string sld) && float.TryParse(sld, out float sldv))
                ShipLightDist = Mathf.Max(0f, sldv);
        }

        public static void ApplyPhysicsAutoSync()
        {
            Physics.autoSyncTransforms = !PhysicsAutoSyncOff;
        }

        public static void CmdPerfAutoSync()
        {
            if (uConsole.GetNumParameters() > 0)
            {
                PhysicsAutoSyncOff = uConsole.GetInt() == 0 ? false : true;
                ApplyPhysicsAutoSync();
            }
            uConsole.Log("perf_physics_autosync_off = " + (PhysicsAutoSyncOff ? "1" : "0") +
                " (Physics.autoSyncTransforms = " + Physics.autoSyncTransforms + ")");
        }

        public static bool NoBloom;

        public static void KeepBloomOff(Viewer viewer)
        {
            if (!NoBloom || viewer.c_camera == null)
                return;
            var bloomOpt = viewer.c_camera.GetComponent<UnityStandardAssets.ImageEffects.BloomOptimized>();
            if (bloomOpt != null)
                bloomOpt.enabled = false;
            var senBloom = viewer.c_camera.GetComponent<SENaturalBloomAndDirtyLens>();
            if (senBloom != null)
                senBloom.enabled = false;
        }

        public static bool AllowLightFlash(Vector3 pos, float range)
        {
            if (LightFlashDist <= 0f || GameplayManager.IsDedicatedServer())
                return true;
            if (!TryGetCameraPos(out Vector3 camPos))
                return true;
            float d = LightFlashDist + range;
            return (pos - camPos).sqrMagnitude <= d * d;
        }

        public static void Tick()
        {
            if (GameplayManager.IsDedicatedServer() || (ProjLightDist <= 0f && ProjLightMax <= 0) || GameplayManager.m_gameplay_state == GameplayState.MENUS)
                return;
            UpdateProjectileLights();
        }

        public static void ApplyBloom()
        {
            var viewer = GameManager.m_viewer;
            if (viewer == null || viewer.c_camera == null)
                return;
            if (NoBloom)
            {
                var bloomOpt = viewer.c_camera.GetComponent<UnityStandardAssets.ImageEffects.BloomOptimized>();
                if (bloomOpt != null)
                    bloomOpt.enabled = false;
                var senBloom = viewer.c_camera.GetComponent<SENaturalBloomAndDirtyLens>();
                if (senBloom != null)
                    senBloom.enabled = false;
            }
            else
            {
                MenuManager.SetCameraGraphics();
            }
        }

        public static void CmdPerfBloom()
        {
            if (uConsole.GetNumParameters() > 0)
            {
                NoBloom = uConsole.GetInt() == 0 ? false : true;
                ApplyBloom();
            }
            uConsole.Log("perf_bloom_off = " + (NoBloom ? "1" : "0"));
        }

        public static float ProjLightDist;
        public static int ProjLightMax;

        static Projectile[] s_cand;
        static float[] s_cand_d2;

        public static bool TryGetCameraPos(out Vector3 pos)
        {
            var ship = GameManager.m_player_ship;
            if (ship != null && ship.c_camera_transform != null)
            {
                pos = ship.c_camera_transform.position;
                return true;
            }
            pos = Vector3.zero;
            return false;
        }

        public static void UpdateProjectileLights(bool restore = false)
        {
            if (MenuManager.gfx_fx_lighting != 1)
                return;
            if (!TryGetCameraPos(out Vector3 camPos))
                return;

            var projs = UpdateDynamicManager.m_projectile;
            int count = UpdateDynamicManager.m_proj_count;
            if (projs == null || count > projs.Length)
                return;
            if (s_cand == null || s_cand.Length < projs.Length)
            {
                s_cand = new Projectile[projs.Length];
                s_cand_d2 = new float[projs.Length];
            }

            float maxD2 = ProjLightDist > 0f ? ProjLightDist * ProjLightDist : float.MaxValue;
            int nCand = 0;
            for (int i = 0; i < count; i++)
            {
                var p = projs[i];
                if (p == null || !p.m_alive || p.c_light == null)
                    continue;
                if (p.m_type == ProjPrefab.proj_flare || p.m_type == ProjPrefab.proj_flare_sticky)
                    continue;
                if (restore)
                {
                    if (!p.c_light.enabled)
                        p.c_light.enabled = true;
                    continue;
                }
                float d2 = (p.c_transform.position - camPos).sqrMagnitude;
                if (d2 > maxD2)
                {
                    if (p.c_light.enabled)
                        p.c_light.enabled = false;
                    continue;
                }
                s_cand[nCand] = p;
                s_cand_d2[nCand] = d2;
                nCand++;
            }
            if (restore)
                return;

            int budget = ProjLightMax > 0 ? ProjLightMax : nCand;
            if (budget < nCand)
            {
                for (int a = 0; a < budget; a++)
                {
                    int best = a;
                    for (int b = a + 1; b < nCand; b++)
                        if (s_cand_d2[b] < s_cand_d2[best])
                            best = b;
                    if (best != a)
                    {
                        var tp = s_cand[a]; s_cand[a] = s_cand[best]; s_cand[best] = tp;
                        var td = s_cand_d2[a]; s_cand_d2[a] = s_cand_d2[best]; s_cand_d2[best] = td;
                    }
                }
            }
            for (int i = 0; i < nCand; i++)
            {
                bool want = i < budget;
                if (s_cand[i].c_light.enabled != want)
                    s_cand[i].c_light.enabled = want;
                s_cand[i] = null;
            }
        }

        public static void CmdProjLightDist()
        {
            if (uConsole.GetNumParameters() > 0)
            {
                ProjLightDist = Mathf.Max(0f, uConsole.GetFloat());
                if (ProjLightDist == 0f && ProjLightMax == 0)
                    UpdateProjectileLights(restore: true);
            }
            uConsole.Log("perf_projlight_dist = " + ProjLightDist + " (0 = off)");
        }

        public static void CmdProjLightMax()
        {
            if (uConsole.GetNumParameters() > 0)
            {
                ProjLightMax = Mathf.Max(0, uConsole.GetInt());
                if (ProjLightDist == 0f && ProjLightMax == 0)
                    UpdateProjectileLights(restore: true);
            }
            uConsole.Log("perf_projlight_max = " + ProjLightMax + " (0 = off)");
        }

        public static float LightFlashDist;

        public static void CmdLightFlashDist()
        {
            if (uConsole.GetNumParameters() > 0)
                LightFlashDist = Mathf.Max(0f, uConsole.GetFloat());
            uConsole.Log("perf_lightflash_dist = " + LightFlashDist + " (0 = off)");
        }

        public static bool ShipShadowsOff;

        public static void ApplyShipShadows(PlayerShip ps)
        {
            if (ps == null || ps.c_trans_ship == null)
                return;
            var camT = ps.c_camera != null ? ps.c_camera.transform : null;
            foreach (var mr in ps.c_trans_ship.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null)
                    continue;
                if (mr.GetComponent<UnityRenderLayer>() != null)
                    continue;
                if (camT != null && mr.transform.IsChildOf(camT))
                    continue;
                if (ShipShadowsOff)
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                else
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            if (!ShipShadowsOff)
                ps.RefreshCockpit(false);
        }

        public static void KeepShipShadowsOff(PlayerShip ship)
        {
            if (ShipShadowsOff)
                ApplyShipShadows(ship);
        }

        public static void ApplyShipShadowsAll()
        {
            foreach (var p in Overload.NetworkManager.m_Players)
                if (p != null)
                    ApplyShipShadows(p.c_player_ship);
            if (GameManager.m_player_ship != null)
                ApplyShipShadows(GameManager.m_player_ship);
        }

        public static void CmdShipShadows()
        {
            if (uConsole.GetNumParameters() > 0)
            {
                ShipShadowsOff = uConsole.GetInt() == 0 ? false : true;
                ApplyShipShadowsAll();
            }
            uConsole.Log("perf_ship_shadows_off = " + (ShipShadowsOff ? "1" : "0"));
        }

        public static float ShipLightDist;

        public static void CmdShipLightDist()
        {
            if (uConsole.GetNumParameters() > 0)
                ShipLightDist = Mathf.Max(0f, uConsole.GetFloat());
            uConsole.Log("perf_shiplight_dist = " + ShipLightDist + " (0 = off)");
        }
    }

}
