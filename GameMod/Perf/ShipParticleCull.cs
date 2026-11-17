using System.Collections.Generic;
using System.Linq;
using Overload;
using UnityEngine;

namespace GameMod.Perf
{
    static class ShipParticleCull
    {
        enum Mode { Engine, Off, Cull }

        class ShipParticles
        {
            public ParticleSystem[] Systems;
            public Renderer[] Renderers;
            public readonly List<ParticleSystem> Paused = new List<ParticleSystem>();
        }

        static readonly Mode mode = ReadMode();
        static readonly Dictionary<PlayerShip, ShipParticles> particlesByShip = new Dictionary<PlayerShip, ShipParticles>();

        static Mode ReadMode()
        {
            string text;
            if (!Core.GameMod.FindArgVal("-ship-particles", out text))
                return Mode.Engine;
            return text == "off" ? Mode.Off : text == "cull" ? Mode.Cull : Mode.Engine;
        }

        public static void Tick()
        {
            if (mode == Mode.Engine || !GameplayManager.IsMultiplayerActive)
                return;
            foreach (Player player in Overload.NetworkManager.m_Players)
            {
                if (player == null || player.isLocalPlayer || player.c_player_ship == null)
                    continue;
                ShipParticles ship;
                if (!particlesByShip.TryGetValue(player.c_player_ship, out ship))
                    particlesByShip[player.c_player_ship] = ship = new ShipParticles
                    {
                        Systems = player.c_player_ship.GetComponentsInChildren<ParticleSystem>(true),
                        Renderers = player.c_player_ship.GetComponentsInChildren<MeshRenderer>(true),
                    };
                if (mode == Mode.Off)
                    StopAll(ship);
                else
                    CullHidden(ship);
            }
            if (Time.frameCount % 600 == 0)
                foreach (PlayerShip gone in particlesByShip.Keys.Where(ship => ship == null).ToList())
                    particlesByShip.Remove(gone);
        }

        static void StopAll(ShipParticles ship)
        {
            foreach (ParticleSystem system in ship.Systems)
                if (system.isPlaying)
                    system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        static void CullHidden(ShipParticles ship)
        {
            bool visible = false;
            for (int i = 0; i < ship.Renderers.Length && !visible; i++)
                visible = ship.Renderers[i] != null && ship.Renderers[i].isVisible;
            if (!visible && ship.Paused.Count == 0)
            {
                foreach (ParticleSystem system in ship.Systems)
                    if (system.isPlaying)
                    {
                        system.Pause(false);
                        ship.Paused.Add(system);
                    }
            }
            else if (visible && ship.Paused.Count > 0)
            {
                foreach (ParticleSystem system in ship.Paused)
                {
                    system.Clear(false);
                    system.Play(false);
                }
                ship.Paused.Clear();
            }
        }
    }
}
