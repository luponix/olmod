using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod.Perf
{
    static class ShipEffectMesh
    {
        public static bool CacheMountFilters = true;
        public static bool SkipEffectMeshOutsideView = true;

        const float EFFECT_MESH_RADIUS = 10f;
        const float EFFECT_MESH_ANGULAR_MARGIN = 0.15f;
        static readonly Plane[] viewPlanes = new Plane[6];
        static int viewPlanesFrame = -1;
        static Camera viewPlanesCamera;

        class ReferenceComparer : IEqualityComparer<GameObject>
        {
            public bool Equals(GameObject a, GameObject b) { return ReferenceEquals(a, b); }
            public int GetHashCode(GameObject go) { return RuntimeHelpers.GetHashCode(go); }
        }

        const int MAX_CACHED_MOUNTS = 1024;
        static readonly Dictionary<GameObject, MeshFilter[]> filtersByMount = new Dictionary<GameObject, MeshFilter[]>(new ReferenceComparer());

        public static void CmdEffectMeshViewSkip()
        {
            if (uConsole.GetNumParameters() > 0)
                SkipEffectMeshOutsideView = uConsole.GetInt() != 0;
            uConsole.Log("perf_effect_mesh_view_skip = " + (SkipEffectMeshOutsideView ? "1" : "0"));
        }

        public static bool EffectMeshOutsideView(PlayerShip ship)
        {
            if (!SkipEffectMeshOutsideView || ship.c_player.m_spectator)
                return false;
            Player local = GameManager.m_local_player;
            if (local == null || local.c_player_ship == null)
                return false;
            Camera camera = local.c_player_ship.c_camera;
            if (camera == null)
                return false;
            if (viewPlanesFrame != Time.frameCount || !ReferenceEquals(camera, viewPlanesCamera))
            {
                GeometryUtility.CalculateFrustumPlanes(camera, viewPlanes);
                viewPlanesFrame = Time.frameCount;
                viewPlanesCamera = camera;
            }
            Vector3 center = ship.c_transform.position;
            float margin = EFFECT_MESH_RADIUS + EFFECT_MESH_ANGULAR_MARGIN * Vector3.Distance(center, camera.transform.position);
            for (int i = 0; i < viewPlanes.Length; i++)
            {
                if (viewPlanes[i].GetDistanceToPoint(center) < -margin)
                    return true;
            }
            return false;
        }

        public static void KeepEffectMeshProperties(ref MaterialPropertyBlock block, float strength, bool cloak)
        {
            if (block == null)
                block = new MaterialPropertyBlock();
            if (strength > 0f)
                block.SetFloat(Robot.matid_anim_state, strength);
            if (cloak && MenuManager.gfx_shaders_simpler == 1)
                block.SetFloat(Robot.matid_opacity, MPCloak_PlayerShip_DrawEffectMesh.adjustedReducedShaderCloakOpacity);
        }

        public static void CmdMountFilterCache()
        {
            if (uConsole.GetNumParameters() > 0)
            {
                CacheMountFilters = uConsole.GetInt() != 0;
                filtersByMount.Clear();
            }
            uConsole.Log("perf_mount_filter_cache = " + (CacheMountFilters ? "1" : "0"));
        }

        public static MeshFilter[] MountFilters(GameObject mount)
        {
            if (!CacheMountFilters)
                return mount.GetComponentsInChildren<MeshFilter>();
            MeshFilter[] filters;
            if (!filtersByMount.TryGetValue(mount, out filters) || AnyDestroyed(filters))
            {
                if (filtersByMount.Count >= MAX_CACHED_MOUNTS)
                    filtersByMount.Clear();
                filters = mount.GetComponentsInChildren<MeshFilter>(true);
                filtersByMount[mount] = filters;
            }
            return AllActive(filters) ? filters : mount.GetComponentsInChildren<MeshFilter>();
        }

        static bool AnyDestroyed(MeshFilter[] filters)
        {
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i] == null)
                    return true;
            }
            return false;
        }

        static bool AllActive(MeshFilter[] filters)
        {
            for (int i = 0; i < filters.Length; i++)
            {
                if (!filters[i].gameObject.activeInHierarchy)
                    return false;
            }
            return true;
        }

        public static IEnumerable<CodeInstruction> UseCachedMountFilters(IEnumerable<CodeInstruction> codes, string target)
        {
            MethodInfo replacement = AccessTools.Method(typeof(ShipEffectMesh), "MountFilters");
            int replaced = 0;
            foreach (var code in codes)
            {
                var method = code.operand as MethodInfo;
                if ((code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) && method != null
                    && method.Name == "GetComponentsInChildren" && method.DeclaringType == typeof(GameObject)
                    && method.IsGenericMethod && method.GetGenericArguments()[0] == typeof(MeshFilter)
                    && method.GetParameters().Length == 0)
                {
                    code.opcode = OpCodes.Call;
                    code.operand = replacement;
                    replaced++;
                }
                yield return code;
            }
            if (replaced != 2)
                Debug.Log("ShipEffectMesh: " + target + ": replaced " + replaced + " of 2 mount filter lookups");
        }
    }
}
