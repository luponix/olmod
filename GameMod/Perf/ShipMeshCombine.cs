using Overload;
using System.Collections.Generic;
using UnityEngine;

namespace GameMod.Perf
{
    public class CombinedHullSource : MonoBehaviour { }

    public class ShipMeshCombine : MonoBehaviour
    {
        private const int RetryFrames = 60;
        private static int s_override = -1;

        public static bool Enabled
        {
            get { return s_override >= 0 ? s_override == 1 : GpuPerf.shipCombineVal == 1; }
        }

        private PlayerShip m_ship;
        private int m_build_delay;
        private int m_retry_frames;
        private readonly List<MeshRenderer> m_disabled = new List<MeshRenderer>();
        private readonly List<GameObject> m_combined = new List<GameObject>();

        public static void Attach(PlayerShip ship)
        {
            var comp = GetOrAdd(ship);
            if (comp != null)
                comp.ScheduleBuild();
        }

        public static void Respawned(PlayerShip ship)
        {
            var comp = GetOrAdd(ship);
            if (comp == null)
                return;
            if (comp.m_combined.Count > 0)
                comp.ReassertDisabled();
            else
                comp.ScheduleBuild();
        }

        public static void Reassert(PlayerShip ship)
        {
            if (ship == null)
                return;
            var comp = ship.GetComponent<ShipMeshCombine>();
            if (comp != null)
                comp.ReassertDisabled();
        }

        private static ShipMeshCombine GetOrAdd(PlayerShip ship)
        {
            if (ship == null || GameplayManager.IsDedicatedServer())
                return null;
            var comp = ship.GetComponent<ShipMeshCombine>();
            if (comp == null)
            {
                comp = ship.gameObject.AddComponent<ShipMeshCombine>();
                comp.m_ship = ship;
            }
            return comp;
        }

        public void ScheduleBuild()
        {
            m_build_delay = 3;
        }

        private bool Wanted
        {
            get { return Enabled && m_ship != null && m_ship.c_player != null && !m_ship.isLocalPlayer && !m_ship.c_player.m_spectator; }
        }

        void Update()
        {
            if (!Wanted)
            {
                if (m_combined.Count > 0)
                    Revert();
                return;
            }
            if (m_build_delay > 0)
            {
                if (--m_build_delay == 0)
                    Rebuild();
                return;
            }
            if (m_combined.Count == 0 && ++m_retry_frames >= RetryFrames)
            {
                m_retry_frames = 0;
                Build();
            }
        }

        void OnDestroy()
        {
            Revert();
        }

        public void ReassertDisabled()
        {
            if (m_combined.Count == 0)
                return;
            for (int i = 0; i < m_disabled.Count; i++)
                if (m_disabled[i] != null)
                    m_disabled[i].enabled = false;
        }

        public void Rebuild()
        {
            Revert();
            Build();
        }

        public void Revert()
        {
            bool visible = m_combined.Count == 0 || m_combined[0] == null || m_combined[0].GetComponent<MeshRenderer>().enabled;
            for (int i = 0; i < m_disabled.Count; i++)
            {
                var mr = m_disabled[i];
                if (mr == null)
                    continue;
                mr.enabled = visible;
                var mark = mr.GetComponent<CombinedHullSource>();
                if (mark != null)
                    Destroy(mark);
            }
            m_disabled.Clear();
            for (int i = 0; i < m_combined.Count; i++)
            {
                if (m_combined[i] == null)
                    continue;
                var mf = m_combined[i].GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    Destroy(mf.sharedMesh);
                Destroy(m_combined[i]);
            }
            m_combined.Clear();
        }

        private void Build()
        {
            if (!Wanted || m_ship.c_cockpit == null || m_ship.m_materials == null ||
                m_ship.m_materials.Count == 0 || (bool)m_ship.m_dying || m_ship.m_dead)
                return;

            var parent = m_ship.c_cockpit.transform;
            var matset = new HashSet<Material>(m_ship.m_materials);
            var mounts = new List<Transform>();
            CollectMounts(m_ship.m_weapon_mounts1, mounts);
            CollectMounts(m_ship.m_weapon_mounts2, mounts);

            var groups = new Dictionary<Material, List<CombineInstance>>();
            var sources = new List<MeshRenderer>();
            Matrix4x4 w2l = parent.worldToLocalMatrix;
            int totalVerts = 0;

            foreach (var mr in m_ship.c_cockpit.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy)
                    continue;
                if (mr.GetComponent<CombinedHullSource>() != null)
                    continue;
                if (string.CompareOrdinal(mr.name, 0, "cp_", 0, 3) == 0 || mr.name.StartsWith("bodyWing"))
                    continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null)
                    continue;
                if (!mf.sharedMesh.isReadable)
                    continue;
                bool underMount = false;
                for (int i = 0; i < mounts.Count && !underMount; i++)
                    underMount = mr.transform.IsChildOf(mounts[i]);
                if (underMount)
                    continue;
                var mats = mr.sharedMaterials;
                if (mats == null || mats.Length == 0)
                    continue;
                bool allShip = true;
                for (int i = 0; i < mats.Length && allShip; i++)
                    allShip = mats[i] != null && matset.Contains(mats[i]);
                if (!allShip)
                    continue;

                var mesh = mf.sharedMesh;
                int n = Mathf.Min(mats.Length, mesh.subMeshCount);
                for (int s = 0; s < n; s++)
                {
                    if (!groups.TryGetValue(mats[s], out var list))
                    {
                        list = new List<CombineInstance>();
                        groups[mats[s]] = list;
                    }
                    list.Add(new CombineInstance
                    {
                        mesh = mesh,
                        subMeshIndex = s,
                        transform = w2l * mr.transform.localToWorldMatrix
                    });
                }
                totalVerts += mesh.vertexCount;
                sources.Add(mr);
            }

            if (sources.Count < 2 || groups.Count == 0 || sources.Count <= groups.Count)
                return;

            var mode = sources[0].shadowCastingMode;
            if (RenderExperiments.ShipShadowsOff)
                mode = UnityEngine.Rendering.ShadowCastingMode.Off;

            foreach (var kv in groups)
            {
                var mesh = new Mesh();
                if (totalVerts > 60000)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                var go = new GameObject("combined_hull");
                go.layer = sources[0].gameObject.layer;
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var nmr = go.AddComponent<MeshRenderer>();
                nmr.sharedMaterial = kv.Key;
                nmr.shadowCastingMode = mode;
                nmr.receiveShadows = sources[0].receiveShadows;
                m_combined.Add(go);
            }
            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].enabled = false;
                sources[i].gameObject.AddComponent<CombinedHullSource>();
                m_disabled.Add(sources[i]);
            }
        }

        private static void CollectMounts(GameObject[] mounts, List<Transform> into)
        {
            if (mounts == null)
                return;
            for (int i = 0; i < mounts.Length; i++)
                if (mounts[i] != null)
                    into.Add(mounts[i].transform);
        }

        public static void CmdShipMeshCombine()
        {
            if (uConsole.GetNumParameters() > 0)
                s_override = uConsole.GetInt() == 0 ? 0 : 1;
            uConsole.Log("perf_ship_mesh_combine = " + (Enabled ? "1" : "0") +
                (s_override >= 0 ? " (console override)" : " (graphics option)"));
        }

        public static void ForceOn()
        {
            s_override = 1;
        }
    }
}
