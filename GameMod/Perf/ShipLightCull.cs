using Overload;
using System.Collections.Generic;
using UnityEngine;

namespace GameMod.Perf
{
    public class ShipLightCull : MonoBehaviour
    {
        public Light[] m_lights;
        public PlayerShip m_ship;
        private bool m_culled;

        public void ResetCull()
        {
            m_culled = false;
        }

        void LateUpdate()
        {
            float dist = RenderExperiments.ShipLightDist;
            bool want = false;
            if (dist > 0f && RenderExperiments.TryGetCameraPos(out Vector3 cam))
                want = (transform.position - cam).sqrMagnitude > dist * dist;
            if (want == m_culled)
                return;
            m_culled = want;
            if (want)
            {
                for (int i = 0; i < m_lights.Length; i++)
                    if (m_lights[i] != null && m_lights[i].enabled)
                        m_lights[i].enabled = false;
            }
            else
            {
                if (m_ship != null && (bool)m_ship.m_dying)
                    return;
                for (int i = 0; i < m_lights.Length; i++)
                    if (m_lights[i] != null && !m_lights[i].enabled)
                        m_lights[i].enabled = true;
            }
        }

        public static void InstallOnShip(PlayerShip ship)
        {
            if (GameplayManager.IsDedicatedServer())
                return;
            Light[] exclude = null;
            if (ship.c_lights != null && ship.c_lights.Length >= 3)
                exclude = new Light[] { ship.c_lights[0], ship.c_lights[1], ship.c_lights[2] };
            Install(ship.gameObject, ship, exclude);
            if (RenderExperiments.ShipShadowsOff)
                RenderExperiments.ApplyShipShadows(ship);
        }

        public static void Rearm(PlayerShip ship)
        {
            var cull = ship.GetComponent<ShipLightCull>();
            if (cull != null)
                cull.ResetCull();
        }

        static void Install(GameObject root, PlayerShip ship, Light[] exclude)
        {
            var all = root.GetComponentsInChildren<Light>(true);
            var track = new List<Light>();
            foreach (var l in all)
            {
                if (l == null || !l.enabled)
                    continue;
                bool excluded = false;
                if (exclude != null)
                    for (int i = 0; i < exclude.Length; i++)
                        if (ReferenceEquals(exclude[i], l))
                        {
                            excluded = true;
                            break;
                        }
                if (!excluded)
                    track.Add(l);
            }
            if (track.Count == 0)
                return;
            var comp = root.AddComponent<ShipLightCull>();
            comp.m_lights = track.ToArray();
            comp.m_ship = ship;
        }
    }
}
