using Overload;
using UnityEngine;

namespace GameMod.Perf
{
    static class ShipGuiLayout
    {
        public static bool Off = true;

        public static void Apply(PlayerShip ship)
        {
            ship.useGUILayout = !Off;
        }

        public static void CmdShipGuiLayout()
        {
            if (uConsole.GetNumParameters() > 0)
            {
                Off = uConsole.GetInt() != 0;
                foreach (var ship in Object.FindObjectsOfType<PlayerShip>())
                    Apply(ship);
            }
            uConsole.Log("perf_ship_gui_layout_off = " + (Off ? "1" : "0"));
        }
    }
}
