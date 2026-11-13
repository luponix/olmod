using System.Collections.Generic;
using System.Linq;
using Overload;
using UnityEngine;

namespace GameMod.Bots
{
    static class InputStats
    {
        const float WINDOW = 30f;

        class Counts
        {
            public string Name;
            public int Starved;
            public int Skipped;
            public int CaughtUp;
        }

        static readonly Dictionary<Player, Counts> counts = new Dictionary<Player, Counts>();
        static float windowEnd;

        static Counts For(Player player)
        {
            Counts entry;
            if (!counts.TryGetValue(player, out entry))
                counts[player] = entry = new Counts();
            entry.Name = player.m_mp_name;
            return entry;
        }

        public static void Missed(Player player)
        {
            if (!Hosting.HasBots || player == null)
                return;
            if (player.m_InputToProcessOnServer.Count > 0)
                For(player).Skipped++;
            else
                For(player).Starved++;
        }

        public static void CaughtUp(Player player)
        {
            if (Hosting.HasBots && player != null)
                For(player).CaughtUp++;
        }

        public static void Report()
        {
            if (Time.unscaledTime < windowEnd)
                return;
            windowEnd = Time.unscaledTime + WINDOW;
            foreach (Counts entry in counts.Values.Where(c => c.Starved + c.Skipped + c.CaughtUp > 0))
                Debug.LogFormat("[Bots] input {0:0} s: {1} starved {2}, skipped {3}, caught up {4}",
                    WINDOW, entry.Name, entry.Starved, entry.Skipped, entry.CaughtUp);
            counts.Clear();
        }
    }
}
