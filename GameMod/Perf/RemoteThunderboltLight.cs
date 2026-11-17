using System.Collections.Generic;
using Overload;
using UnityEngine;

namespace GameMod.Perf
{
    static class RemoteThunderboltLight
    {
        const int ThunderboltLight = 4;
        const float DarkIntensity = 0.01f;

        public static bool Enabled = true;

        static readonly HashSet<PlayerShip> switchedOff = new HashSet<PlayerShip>();

        public static void SwitchOffWhenDark(PlayerShip ship)
        {
            if (!Enabled || !ship.m_light_tb_enabled || ship.c_player == null || ship.c_player.isLocalPlayer || ship.c_lights == null || ship.c_lights.Length <= ThunderboltLight)
                return;
            Light light = ship.c_lights[ThunderboltLight];
            if (light == null || light.intensity >= DarkIntensity)
                return;
            ship.m_light_tb_enabled = false;
            light.enabled = false;
            switchedOff.Add(ship);
        }

        public static void Forget()
        {
            switchedOff.Clear();
        }

        static void RestoreSwitchedOff()
        {
            foreach (PlayerShip ship in switchedOff)
                if (ship != null && !ship.m_dying && !ship.m_light_tb_enabled)
                {
                    ship.m_light_tb_enabled = true;
                    ship.c_lights[ThunderboltLight].enabled = true;
                }
            switchedOff.Clear();
        }

        public static void CmdToggle()
        {
            if (uConsole.GetNumParameters() > 0)
            {
                Enabled = uConsole.GetInt() != 0;
                if (!Enabled)
                    RestoreSwitchedOff();
            }
            uConsole.Log("perf_tb_light_off " + (Enabled ? "1" : "0"));
        }
    }
}
