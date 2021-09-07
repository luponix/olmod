﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace GameMod
{

    public class MPDeathReview
    {
        public static DeathReviewMessage lastDeathReview;
    }

    static class ServerDamageLog
    {
        public static Dictionary<NetworkInstanceId, List<DamageEvent>> damageEvents;

        public static void AddDamage(NetworkInstanceId player_id, DamageEvent de)
        {
            if (!damageEvents.ContainsKey(player_id))
                damageEvents.Add(player_id, new List<DamageEvent>());

            damageEvents[player_id].Add(de);
        }

        public static void Clear(NetworkInstanceId player_id)
        {
            damageEvents[player_id] = new List<DamageEvent>();
        }

        public static Dictionary<NetworkInstanceId, List<DamageSummary>> GetSummaryForDeadPlayer(Player player)
        {
            Dictionary<NetworkInstanceId, List<DamageSummary>> players = new Dictionary<NetworkInstanceId, List<DamageSummary>>();
            foreach (var dmgAttacker in damageEvents[player.netId].GroupBy(x => x.attacker == null ? player : x.attacker))
            {
                players.Add(dmgAttacker.Key.netId, new List<DamageSummary>());
                foreach (var dmgStats in dmgAttacker.Select(x => x).GroupBy(x => x.weapon))
                {
                    players[dmgAttacker.Key.netId].Add(new DamageSummary { weapon = dmgStats.Key, damage = dmgStats.Sum(x => x.damage) });
                }
            }

            return players;
        }
    }

    class DamageEvent
    {
        public Player attacker;
        public ProjPrefab weapon;
        public float damage;
    }

    public class DamageSummary
    {
        public ProjPrefab weapon;
        public float damage;
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    class MPDeathReview_Client_RegisterHandlers
    {
        static void Postfix()
        {
            if (Client.GetClient() == null)
                return;

            Client.GetClient().RegisterHandler(MessageTypes.MsgDeathReview, OnDeathReview);
        }

        private static void OnDeathReview(NetworkMessage rawMsg)
        {
            DeathReviewMessage rs = rawMsg.ReadMessage<DeathReviewMessage>();
            MPDeathReview.lastDeathReview = rs;
        }
    }

    public class DeathReviewMessage : MessageBase
    {
        public override void Serialize(NetworkWriter writer)
        {
            writer.Write((byte)0); // version
            writer.Write(m_killer_id); // Killer
            writer.Write(m_assister_id); // Assister
            writer.WritePackedUInt32((uint)players.Count);
            foreach (var player in players)
            {
                writer.Write(player.Key);
                writer.WritePackedUInt32((uint)player.Value.Count);
                foreach (var damageType in player.Value)
                {
                    writer.WritePackedUInt32((uint)damageType.weapon);
                    writer.Write(damageType.damage);
                }
            }
        }

        public override void Deserialize(NetworkReader reader)
        {
            players = new Dictionary<NetworkInstanceId, List<DamageSummary>>();
            var version = reader.ReadByte();
            m_killer_id = reader.ReadNetworkId();
            m_assister_id = reader.ReadNetworkId();
            var numPlayers = reader.ReadPackedUInt32();
            for (int i = 0; i < numPlayers; i++)
            {
                NetworkInstanceId m_player_id = reader.ReadNetworkId();
                players.Add(m_player_id, new List<DamageSummary>());
                uint damageCount = reader.ReadPackedUInt32();
                for (int j = 0; j < damageCount; j++)
                {
                    ProjPrefab weapon = (ProjPrefab)reader.ReadPackedUInt32();
                    float damage = reader.ReadSingle();
                    players[m_player_id].Add(new DamageSummary { weapon = weapon, damage = damage });
                }
            }
        }

        public Dictionary<NetworkInstanceId, List<DamageSummary>> players;
        public NetworkInstanceId m_killer_id;
        public NetworkInstanceId m_assister_id;
    }

    // Latch on to mini scoreboard to avoid alpha alteration through death overlay
    [HarmonyPatch(typeof(UIElement), "DrawMpMiniScoreboard")]
    class MPDeathReview_UIElement_DrawMpMiniScoreboard
    {
        static void Postfix(UIElement __instance)
        {
            if (MPDeathReview.lastDeathReview == null)
                return;

            DrawRightOverlay(__instance);

            if (!GameplayManager.ShowMpScoreboard)
                DrawDeathSummary(__instance);
        }

        static void DrawDeathSummary(UIElement uie)
        {
            Vector2 pos = new Vector2(UIManager.UI_LEFT + 160f, UIManager.UI_TOP + 220f);
            Player killer = Overload.NetworkManager.m_Players.FirstOrDefault(x => x.netId == MPDeathReview.lastDeathReview.m_killer_id);
            Player assister = Overload.NetworkManager.m_Players.FirstOrDefault(x => x.netId == MPDeathReview.lastDeathReview.m_assister_id);

            Color c = UIManager.m_col_red;
            float alpha_mod = 1f;
            float w = 120f;

            if (killer)
            {
                c = NetworkMatch.IsTeamMode(NetworkMatch.GetMode()) ? MPTeams.TeamColor(killer.m_mp_team, 0) : UIManager.m_col_red;
                var damages = MPDeathReview.lastDeathReview.players.FirstOrDefault(x => x.Key == killer.netId).Value;
                DrawHeader(uie, pos, $"KILLER: {killer.m_mp_name} [{(damages.Sum(x => x.damage) / MPDeathReview.lastDeathReview.players.SelectMany(x => x.Value).Sum(x => x.damage)):P0}]", w, c, 0.35f);
                pos.y += 32f;
                pos.x += 70f;
                DrawDamageSummary(uie, ref pos, c, 0.35f, alpha_mod, damages);
                pos.x -= 70f;
                pos.y += 48f;
            }

            if (assister != null && assister.netId != killer.netId)
            {
                c = NetworkMatch.IsTeamMode(NetworkMatch.GetMode()) ? MPTeams.TeamColor(assister.m_mp_team, 0) : UIManager.m_col_white;
                var damages = MPDeathReview.lastDeathReview.players.FirstOrDefault(x => x.Key == assister.netId).Value;
                DrawHeader(uie, pos, $"ASSIST: {assister.m_mp_name} [{(damages.Sum(x => x.damage) / MPDeathReview.lastDeathReview.players.SelectMany(x => x.Value).Sum(x => x.damage)):P0}]", w, c, 0.35f);
                pos.y += 32f;
                pos.x += 70f;
                DrawDamageSummary(uie, ref pos, c, 0.35f, alpha_mod, damages);
                pos.x -= 70f;
                pos.y += 48f;
            }

            // Self and misc damage
            var selfIds = Overload.NetworkManager.m_Players.Where(x => x.netId == GameManager.m_local_player.netId || (NetworkMatch.GetMode() == MatchMode.TEAM_ANARCHY && x.m_mp_team == GameManager.m_local_player.m_mp_team)).Select(x => x.netId);
            if (MPDeathReview.lastDeathReview.players.Any(x => selfIds.Contains(x.Key)))
            {
                var selfDamages = MPDeathReview.lastDeathReview.players.Where(x => selfIds.Contains(x.Key)).SelectMany(x => x.Value);
                c = NetworkMatch.IsTeamMode(NetworkMatch.GetMode()) ? MPTeams.TeamColor(GameManager.m_local_player.m_mp_team, 0) : UIManager.m_col_white;
                DrawHeader(uie, pos, "SELF/MISC", w, c, 0.35f);
                pos.y += 32f;
                pos.x += 70f;
                DrawDamageSummary(uie, ref pos, c, 0.35f, alpha_mod, selfDamages);
                pos.x -= 70f;
                pos.y += 48f;
            }            
        }

        static void DrawHeader(UIElement uie, Vector2 pos, string s, float w, Color c, float sc)
        {
            UIManager.DrawQuadUI(pos, w + 30f, 13f, c, 1f * 0.5f, 20);
            uie.DrawStringSmall(s, pos, sc, StringOffset.CENTER, c, 1f, w * 1.9f);
            UIManager.DrawSpriteUI(pos - Vector2.right * (w - 6f), 0.2f, 0.2f, c, 1f, 42);
            UIManager.DrawSpriteUI(pos + Vector2.right * (w - 6f), 0.2f, 0.2f, c, 1f, 42);
            pos.y += 16f;
            UIManager.DrawQuadUI(pos, w, 3f, c, 1f, 22);
            uie.DrawWideBox(pos, w, 1.2f, c, 1f, 4);
            pos.y -= 32f;
            UIManager.DrawQuadUI(pos, w, 3f, c, 1f, 22);
            uie.DrawWideBox(pos, w, 1.2f, c, 1f, 4);
        }

        static void DrawDamageSummary(UIElement uie, ref Vector2 pos, Color c, float sc, float m_alpha, IEnumerable<DamageSummary> ds)
        {
            float w = 60f * sc;

            var grouped = ds.GroupBy(x => x.weapon).OrderByDescending(x => x.Sum(y => y.damage));
            foreach (var g in grouped)
            {
                uie.DrawDigitsVariable(pos + Vector2.right * w, (int)g.Sum(y => y.damage), sc, StringOffset.RIGHT, c, m_alpha);
                int tex_index = GetTextureIndex(g.Key);
                if (tex_index >= 0)
                {
                    UIManager.DrawSpriteUI(pos, 0.3f * sc, 0.3f * sc, c, m_alpha, GetTextureIndex(g.Key));
                }
                else
                {
                    uie.DrawStringSmall(">", pos, sc * 0.75f, StringOffset.CENTER, c, m_alpha);
                }
                pos.y += 48f * sc;
            }
        }

        static void DrawRightOverlay(UIElement uie)
        {
            Vector2 pos = new Vector2();
            pos.x = UIManager.UI_RIGHT - 100f;
            pos.y = UIManager.UI_TOP + 400f;

            DrawDamageSummary(uie, ref pos, UIManager.m_col_ui0, 0.75f, 1f, MPDeathReview.lastDeathReview.players.SelectMany(x => x.Value));
        }

        public static int GetTextureIndex(ProjPrefab prefab)
        {
            switch (prefab)
            {
                case ProjPrefab.proj_vortex:
                    return 27;
                case ProjPrefab.proj_thunderbolt:
                    return 32;
                case ProjPrefab.proj_shotgun:
                    return 29;
                case ProjPrefab.proj_reflex:
                    return 28;
                case ProjPrefab.proj_impulse:
                    return 26;
                case ProjPrefab.proj_flak_cannon:
                    return 31;
                case ProjPrefab.proj_driller_mini:
                case ProjPrefab.proj_driller:
                    return 30;
                case ProjPrefab.proj_beam:
                    return 33;
                case ProjPrefab.missile_creeper:
                    return 107;
                case ProjPrefab.missile_devastator:
                case ProjPrefab.missile_devastator_mini:
                    return 109;
                case ProjPrefab.missile_falcon:
                    return 104;
                case ProjPrefab.missile_hunter:
                    return 106;
                case ProjPrefab.missile_pod:
                    return 105;
                case ProjPrefab.missile_smart:
                case ProjPrefab.missile_smart_mini:
                    return 108;
                case ProjPrefab.missile_timebomb:
                    return 110;
                case ProjPrefab.missile_vortex:
                    return 111;
                default:
                    return -1;
            }
        }
    }

    // Hook in before this.CallRpcApplyDamage() call in PlayerShip.ApplyDamage() so we can truncate overkill
    [HarmonyPatch(typeof(PlayerShip), "ApplyDamage")]
    class MPDeathReview_PlayerShip_ApplyDamage
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
        {
            int state = 0;
            foreach (var code in codes)
            {
                if (state == 0 && code.opcode == OpCodes.Call && code.operand == AccessTools.Method(typeof(GameplayManager), "AddStatsPlayerDamage"))
                    state = 1;

                if (state == 1 && code.opcode == OpCodes.Ldarg_0)
                {
                    state = 2;
                    yield return new CodeInstruction(OpCodes.Ldarg_0) { labels = code.labels };
                    yield return new CodeInstruction(OpCodes.Ldarg_1); //DamageInfo
                    yield return new CodeInstruction(OpCodes.Ldloc_3); // num2 float
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MPDeathReview_PlayerShip_ApplyDamage), "AddDamageEvent"));
                    code.labels = null;
                }

                yield return code;
            }
        }

        static void AddDamageEvent(PlayerShip playerShip, DamageInfo di, float damage_scaled)
        {
            if (!Overload.NetworkManager.IsHeadless() || di.damage == 0f ||
                playerShip.m_death_stats_recorded || playerShip.m_cannot_die || playerShip.c_player.m_invulnerable)
                return;

            float hitpoints = playerShip.c_player.m_hitpoints;
            ProjPrefab weapon = di.weapon;
            switch(weapon)
            {
                case ProjPrefab.missile_devastator_mini:
                    weapon = ProjPrefab.missile_devastator;
                    break;
                case ProjPrefab.missile_smart_mini:
                    weapon = ProjPrefab.missile_smart;
                    break;
            }

            DamageEvent de = new DamageEvent
            {
                attacker = di.owner?.GetComponent<Player>(),
                weapon = weapon,
                damage = (hitpoints - damage_scaled <= 0f) ? hitpoints : damage_scaled
            };
            ServerDamageLog.AddDamage(playerShip.c_player.netId, de);
        }
    }

    // Server call
    [HarmonyPatch(typeof(Player), "OnKilledByPlayer")]
    class MPDeathReview_Player_OnKilledByPlayer
    {
        static void ReportPlayerDeath(Player player, int num, int num2)
        {
            Player killer, assister = null;
            NetworkInstanceId killer_id = default(NetworkInstanceId);
            NetworkInstanceId assister_id = default(NetworkInstanceId);
            PlayerLobbyData playerLobbyData;
            if (num2 != -1 && NetworkMatch.m_players.TryGetValue(num2, out playerLobbyData))
            {
                killer = Server.FindPlayerByConnectionId(playerLobbyData.m_id);
                if (killer != null)
                    killer_id = killer.netId;
            }
            if (num != -1 && NetworkMatch.m_players.TryGetValue(num, out playerLobbyData))
            {
                assister = Server.FindPlayerByConnectionId(playerLobbyData.m_id);
                if (assister != null)
                    assister_id = assister.netId;
            }

            // Send stats to client and clear out
            NetworkServer.SendToClient(player.connectionToClient.connectionId, MessageTypes.MsgDeathReview, new DeathReviewMessage { m_killer_id = killer_id, m_assister_id = assister_id, players = ServerDamageLog.GetSummaryForDeadPlayer(player) });
            ServerDamageLog.Clear(player.netId);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
        {
            foreach (var code in codes)
            {
                if (code.opcode == OpCodes.Call && code.operand == AccessTools.Method(typeof(Telemetry), "ReportPlayerDeath"))
                {
                    yield return code;
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldloc_3);
                    yield return new CodeInstruction(OpCodes.Ldloc_2);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MPDeathReview_Player_OnKilledByPlayer), "ReportPlayerDeath"));
                    continue;
                }
                yield return code;
            }
        }
    }

    // Server call
    [HarmonyPatch(typeof(NetworkMatch), "InitBeforeEachMatch")]
    class MPDeathReview_NetworkMatch_InitBeforeEachMatch
    {
        private static void Postfix()
        {
            ServerDamageLog.damageEvents = new Dictionary<NetworkInstanceId, List<DamageEvent>>();
        }
    }
}
