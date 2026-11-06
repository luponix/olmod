using Overload;

namespace GameMod
{
    public static class MPBoostFire
    {
        public static bool Enabled = false;

        public static bool AllowsFire(PlayerShip ship)
        {
            bool boostBlocksFire = ship.m_boosting && !(Enabled && GameplayManager.IsMultiplayerActive);
            return !boostBlocksFire && ship.m_wheel_select_state == WheelSelectState.NONE;
        }

        public static void ResetAfterMatch()
        {
            Enabled = false;
        }
    }
}
