using System.Collections.Generic;
using Overload;
using UnityEngine;

namespace GameMod.Replay
{
    public static class DamageFlash
    {
        private struct Hit
        {
            public float t;
            public float damageMin;
            public int burstStart;
        }

        // After a gap this long both curves have decayed to exactly zero
        // (fast 1.5/3, slow 0.6/1), so a burst can be walked from a known state.
        private const float RESET_GAP_SECONDS = 0.6f;

        private static List<Hit>[] hits;
        private static MeshFilter[][] hullMeshes;
        private static bool[] lit;
        private static MaterialPropertyBlock glowBlock;
        private static MaterialPropertyBlock overlayBlock;

        public static void Begin(Recording replay)
        {
            End();
            if (replay == null || replay.players.Count == 0)
                return;
            hits = new List<Hit>[replay.players.Count];
            hullMeshes = new MeshFilter[replay.players.Count][];
            lit = new bool[replay.players.Count];
            for (int i = 0; i < hits.Length; i++)
                hits[i] = new List<Hit>();

            foreach (var ev in replay.events)
            {
                if (ev.type != MatchEventType.DamageImpact || ev.iA < 0 || ev.iA >= hits.Length)
                    continue;
                // An invulnerable ship still broadcasts the impact but never takes the
                // damage the flash is set from.
                if (WasInvulnerable(replay, ev.iA, ev.t))
                    continue;
                // PlayerShip.ApplyDamage: damage_min is 0.5 + di.damage, and di.damage is
                // what the impact event carries.
                hits[ev.iA].Add(new Hit { t = ev.t, damageMin = 0.5f + ev.fA });
            }

            foreach (var playerHits in hits)
            {
                int burstStart = 0;
                for (int i = 0; i < playerHits.Count; i++)
                {
                    if (i > 0 && playerHits[i].t - playerHits[i - 1].t >= RESET_GAP_SECONDS)
                        burstStart = i;
                    var hit = playerHits[i];
                    hit.burstStart = burstStart;
                    playerHits[i] = hit;
                }
            }
        }

        public static void End()
        {
            hits = null;
            hullMeshes = null;
            lit = null;
        }

        public static void Apply(int rosterIndex, MeshRenderer[] renderers, bool visible, float clock)
        {
            if (hits == null || renderers == null || rosterIndex < 0 || rosterIndex >= hits.Length)
                return;

            float fast = 0f, slow = 0f;
            if (visible)
                CurvesAt(rosterIndex, clock, out fast, out slow);
            bool nowLit = fast > 0.001f || slow > 0.001f;
            if (!nowLit && !lit[rosterIndex])
                return;
            lit[rosterIndex] = nowLit;

            if (glowBlock == null)
                glowBlock = new MaterialPropertyBlock();
            glowBlock.SetFloat(Robot.matid_dmg_glow_base, fast * 0.7f + slow * 0.3f);
            glowBlock.SetFloat(Robot.matid_dmg_glow, slow * 5f);
            glowBlock.SetFloat(Robot.matid_edge_noise_mult, fast * slow * 0.3f);
            // A ghost shares the local ship's material instances, so the flash goes
            // through a property block instead of the materials themselves.
            foreach (var renderer in renderers)
                if (renderer != null)
                    renderer.SetPropertyBlock(glowBlock);

            if (nowLit)
                DrawOverlay(rosterIndex, renderers, fast);
        }

        private static void CurvesAt(int rosterIndex, float clock, out float fast, out float slow)
        {
            fast = 0f;
            slow = 0f;
            var playerHits = hits[rosterIndex];
            int last = LastHitAtOrBefore(playerHits, clock);
            if (last < 0)
                return;
            int first = playerHits[last].burstStart;
            float previous = playerHits[first].t;
            for (int i = first; i <= last; i++)
            {
                Decay(ref fast, ref slow, playerHits[i].t - previous);
                previous = playerHits[i].t;
                fast = Mathf.Min(1.5f, fast + Mathf.Min(0.9f, playerHits[i].damageMin * 0.1f));
                slow = Mathf.Min(0.6f, slow + playerHits[i].damageMin * 0.05f);
            }
            Decay(ref fast, ref slow, clock - previous);
        }

        private static void Decay(ref float fast, ref float slow, float seconds)
        {
            fast = Mathf.Clamp(fast - seconds * 3f, 0f, 1.5f);
            slow = Mathf.Max(0f, slow - seconds * 1f);
        }

        private static int LastHitAtOrBefore(List<Hit> playerHits, float t)
        {
            int low = 0, high = playerHits.Count - 1, found = -1;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                if (playerHits[mid].t <= t) { found = mid; low = mid + 1; }
                else high = mid - 1;
            }
            return found;
        }

        private static bool WasInvulnerable(Recording replay, int rosterIndex, float t)
        {
            if (replay.frames.Count == 0)
                return false;
            int low = 0, high = replay.frames.Count - 1, found = 0;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                if (replay.frames[mid].t <= t) { found = mid; low = mid + 1; }
                else high = mid - 1;
            }
            var ships = replay.frames[found].ships;
            if (ships == null || rosterIndex >= ships.Length)
                return false;
            return ships[rosterIndex].present && ships[rosterIndex].IsInvulnerable;
        }

        private static void DrawOverlay(int rosterIndex, MeshRenderer[] renderers, float fast)
        {
            var material = UIManager.gm != null ? UIManager.gm.m_damage_material : null;
            var ship = GameManager.m_player_ship;
            var camera = ship != null ? ship.c_camera : null;
            if (material == null || camera == null)
                return;
            if (overlayBlock == null)
                overlayBlock = new MaterialPropertyBlock();
            overlayBlock.SetFloat(Robot.matid_anim_state, fast * 0.75f);
            foreach (var filter in HullMeshesOf(rosterIndex, renderers))
            {
                if (filter == null || filter.sharedMesh == null ||
                    !filter.gameObject.activeInHierarchy)
                    continue;
                Graphics.DrawMesh(filter.sharedMesh, filter.transform.localToWorldMatrix,
                    material, 0, camera, 0, overlayBlock, false);
            }
        }

        private static MeshFilter[] HullMeshesOf(int rosterIndex, MeshRenderer[] renderers)
        {
            if (hullMeshes[rosterIndex] != null)
                return hullMeshes[rosterIndex];
            var filters = new List<MeshFilter>(renderers.Length);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null)
                    filters.Add(filter);
            }
            hullMeshes[rosterIndex] = filters.ToArray();
            return hullMeshes[rosterIndex];
        }
    }
}
