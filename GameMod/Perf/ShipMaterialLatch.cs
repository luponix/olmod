using Overload;

namespace GameMod.Perf
{
    static class ShipMaterialLatch
    {
        public static void Close(PlayerShip ship, ref float prevHeadlight, float headlightBrightness)
        {
            prevHeadlight = headlightBrightness;
            ship.m_update_mp_color = false;
        }

        public static void Reopen(PlayerShip ship)
        {
            ship.m_update_mp_color = true;
        }
    }
}
