using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overload;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.Networking;

namespace GameMod
{

    /// <summary>
    /// MP Match Presets Support for quick switching between OTL, pub anarchy etc
    /// Author: Tobias
    /// </summary>
    class MPMatchPresets
    {
        static List<MPMatchPreset> presets = new List<MPMatchPreset>();
        static int mms_match_preset = 0;

        public static string GetMatchPreset()
        {
            return presets[mms_match_preset].title;
        }

        public static void CyclePreset()
        {
            mms_match_preset = (mms_match_preset + presets.Count + UIManager.m_select_dir) % presets.Count;
            presets[mms_match_preset].Apply();
        }

        static MPMatchPresets()
        {
            presets.Add(new MPMatchPreset
            {
                title = "PILOT PROFILE",
                matchMode = MenuManager.mms_mode,
                maxPlayers = MenuManager.mms_max_players,
                friendlyFire = MenuManager.mms_friendly_fire,
                timeLimit = Menus.mms_match_time_limit,
                scoreLimit = MenuManager.mms_score_limit,
                respawnTime = MenuManager.mms_respawn_time,
                respawnInvuln = MenuManager.mms_respawn_invuln,
                showNames = MenuManager.mms_show_names,
                turnSpeedLimit = MenuManager.mms_turn_speed_limit,
                forceLoadout = MenuManager.mms_force_loadout,
                forcePrimary1 = MenuManager.mms_force_w1,
                forcePrimary2 = MenuManager.mms_force_w2,
                forceSecondary1 = MenuManager.mms_force_m1,
                forceSecondary2 = MenuManager.mms_force_m2,
                forceModifier1 = MenuManager.mms_force_modifier1,
                forceModifier2 = MenuManager.mms_force_modifier2,
                powerupSpawn = MenuManager.mms_powerup_spawn,
                powerupInitial = MenuManager.mms_powerup_initial,
                powerupBigSpawn = MenuManager.mms_powerup_big_spawn,
                powerupFilter = MenuManager.mms_powerup_filter,
                jipEnabled = MPJoinInProgress.MenuManagerEnabled,
                suddenDeathOvertime = MPSuddenDeath.SuddenDeathMenuEnabled,
                lapLimit = ExtMenuManager.mms_ext_lap_limit,
                sniperPackets = MPSniperPackets.enabled,
                noCompression = MPNoPositionCompression.enabled,
                allowRearView = RearView.MPMenuManagerEnabled,
                scaleRespawnTime = Menus.mms_scale_respawn_time,
                ctfCarrierBoostEnabled = Menus.mms_ctf_boost,
                classicSpawnsEnabled = Menus.mms_classic_spawns,
                alwaysCloaked = Menus.mms_always_cloaked,
                allowSmash = Menus.mms_allow_smash,
                boostFire = Menus.mms_boost_fire,
                instareap = Menus.mms_instareap,
                damageNumbers = Menus.mms_damage_numbers,
                assistScoring = Menus.mms_assist_scoring,
                teamCount = MPTeams.MenuManagerTeamCount,
                shipMeshCollider = Menus.mms_collision_mesh,
                thunderboltPassthrough = MPThunderboltPassthrough.isAllowed,
                loadoutFilterBitmask = MPLoadouts.LoadoutFilterBitmask,
                serverOptimizations = MPServerOptimization.prefEnabled,
                rollSpeedLimit = MPServerOptimization.RollSpeedLimit
            });

            presets.Add(new MPMatchPreset
            {
                title = "PUBLIC ANARCHY",
                matchMode = MatchMode.ANARCHY,
                maxPlayers = 16,
                friendlyFire = 0,
                timeLimit = 15 * 60,
                scoreLimit = 0,
                respawnTime = 2,
                respawnInvuln = 2,
                showNames = MatchShowEnemyNames.NORMAL,
                turnSpeedLimit = 2,
                forceLoadout = 0,
                forcePrimary1 = WeaponType.IMPULSE,
                forcePrimary2 = WeaponType.NUM,
                forceSecondary1 = MissileType.FALCON,
                forceSecondary2 = MissileType.NUM,
                forceModifier1 = 4,
                forceModifier2 = 4,
                powerupSpawn = 2,
                powerupInitial = 2,
                powerupBigSpawn = 1,
                powerupFilter = new bool[] { true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true },
                jipEnabled = true,
                suddenDeathOvertime = false,
                lapLimit = 0,
                sniperPackets = true,
                noCompression = true,
                allowRearView = true,
                scaleRespawnTime = false,
                ctfCarrierBoostEnabled = false,
                classicSpawnsEnabled = false,
                alwaysCloaked = false,
                allowSmash = false,
                boostFire = false,
                instareap = false,
                damageNumbers = true,
                assistScoring = true,
                teamCount = 2,
                shipMeshCollider = 2,
                thunderboltPassthrough = false,
                loadoutFilterBitmask = MPLoadouts.MASK_DEFAULT,
                serverOptimizations = true
            });

            presets.Add(new MPMatchPreset
            {
                title = "PUBLIC TEAM ANARCHY",
                matchMode = MatchMode.TEAM_ANARCHY,
                maxPlayers = 16,
                friendlyFire = 0,
                timeLimit = 15 * 60,
                scoreLimit = 0,
                respawnTime = 2,
                respawnInvuln = 2,
                showNames = MatchShowEnemyNames.NORMAL,
                turnSpeedLimit = 2,
                forceLoadout = 0,
                forcePrimary1 = WeaponType.IMPULSE,
                forcePrimary2 = WeaponType.NUM,
                forceSecondary1 = MissileType.FALCON,
                forceSecondary2 = MissileType.NUM,
                forceModifier1 = 4,
                forceModifier2 = 4,
                powerupSpawn = 2,
                powerupInitial = 2,
                powerupBigSpawn = 1,
                powerupFilter = new bool[] { true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true },
                jipEnabled = true,
                suddenDeathOvertime = false,
                lapLimit = 0,
                sniperPackets = true,
                noCompression = true,
                allowRearView = true,
                scaleRespawnTime = false,
                ctfCarrierBoostEnabled = false,
                classicSpawnsEnabled = false,
                alwaysCloaked = false,
                allowSmash = false,
                boostFire = false,
                instareap = false,
                assistScoring = true,
                teamCount = 2,
                shipMeshCollider = 2,
                damageNumbers = true,
                thunderboltPassthrough = false,
                loadoutFilterBitmask = MPLoadouts.MASK_DEFAULT,
                serverOptimizations = true
        });

            presets.Add(new MPMatchPreset
            {
                title = "INSTAREAP",
                matchMode = ExtMatchMode.ANARCHY,
                maxPlayers = 16,
                friendlyFire = 0,
                timeLimit = 15 * 60,
                scoreLimit = 0,
                respawnTime = 0,
                respawnInvuln = 0,
                showNames = MatchShowEnemyNames.NORMAL,
                turnSpeedLimit = 2,
                forceLoadout = 1,
                forcePrimary1 = WeaponType.DRILLER,
                forcePrimary2 = WeaponType.NUM,
                forceSecondary1 = MissileType.NUM,
                forceSecondary2 = MissileType.NUM,
                forceModifier1 = 4,
                forceModifier2 = 4,
                powerupSpawn = 2,
                powerupInitial = 2,
                powerupBigSpawn = 1,
                powerupFilter = new bool[] { false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false },
                jipEnabled = true,
                suddenDeathOvertime = false,
                lapLimit = 0,
                sniperPackets = true,
                noCompression = true,
                allowRearView = true,
                scaleRespawnTime = false,
                ctfCarrierBoostEnabled = false,
                classicSpawnsEnabled = false,
                alwaysCloaked = false,
                allowSmash = false,
                boostFire = false,
                instareap = true,
                assistScoring = true,
                teamCount = 2,
                shipMeshCollider = 0,
                damageNumbers = false,
                thunderboltPassthrough = false
            });

            GameManager.m_gm.StartCoroutine(GetMatchPresets());
        }        

        class MPMatchPreset
        {
            public string title { get; set; }
            public MatchMode matchMode;
            public int maxPlayers;
            public int friendlyFire;
            public int timeLimit;
            public int scoreLimit;
            public int respawnTime;
            public int respawnInvuln;
            public MatchShowEnemyNames showNames;
            public int turnSpeedLimit;
            public int forceLoadout;
            public WeaponType forcePrimary1;
            public WeaponType forcePrimary2;
            public MissileType forceSecondary1;
            public MissileType forceSecondary2;
            public int forceModifier1;
            public int forceModifier2;
            public int powerupSpawn;
            public int powerupInitial;
            public int powerupBigSpawn;
            public bool[] powerupFilter;
            public bool jipEnabled;
            public bool suddenDeathOvertime;
            public int lapLimit;
            public bool sniperPackets;
            public bool noCompression;
            public bool allowRearView;
            public bool scaleRespawnTime;
            public bool classicSpawnsEnabled;
            public bool ctfCarrierBoostEnabled;
            public bool alwaysCloaked;
            public bool allowSmash;
            public bool boostFire;
            public bool instareap;
            public bool damageNumbers;
            public bool assistScoring = true;
            public int teamCount = 2;
            public int shipMeshCollider = 2;
            public float colliderScale = 1f;
            public bool thunderboltPassthrough;
            public int loadoutFilterBitmask = MPLoadouts.MASK_DEFAULT;
            public bool serverOptimizations = true;
            public int rollSpeedLimit = 7;

            public void Apply()
            {
                // part of the reflex sidearm addition (possibly unnecessary workaround for remote presets)
                // disables reflex drops if classic spawns is off, regardless of the preset setting
                if (!this.classicSpawnsEnabled)
                {
                    this.powerupFilter[2] = false;
                }

                MenuManager.mms_mode = this.matchMode;
                MenuManager.mms_max_players = this.maxPlayers;
                MenuManager.mms_friendly_fire = this.friendlyFire;
                Menus.mms_match_time_limit = this.timeLimit;
                MenuManager.mms_score_limit = this.scoreLimit;
                MenuManager.mms_respawn_time = this.respawnTime;
                MenuManager.mms_respawn_invuln = this.respawnInvuln;
                MenuManager.mms_show_names = this.showNames;
                MenuManager.mms_turn_speed_limit = this.turnSpeedLimit;
                MenuManager.mms_force_loadout = this.forceLoadout;
                MenuManager.mms_force_w1 = this.forcePrimary1;
                MenuManager.mms_force_w2 = this.forcePrimary2;
                MenuManager.mms_force_m1 = this.forceSecondary1;
                MenuManager.mms_force_m2 = this.forceSecondary2;
                MenuManager.mms_force_modifier1 = this.forceModifier1;
                MenuManager.mms_force_modifier2 = this.forceModifier2;
                MenuManager.mms_powerup_spawn = this.powerupSpawn;
                MenuManager.mms_powerup_initial = this.powerupInitial;
                MenuManager.mms_powerup_big_spawn = this.powerupBigSpawn;
                MenuManager.mms_powerup_filter = this.powerupFilter;
                MPJoinInProgress.MenuManagerEnabled = this.jipEnabled;
                MPSuddenDeath.SuddenDeathMenuEnabled = this.suddenDeathOvertime;
                ExtMenuManager.mms_ext_lap_limit = this.lapLimit;
                MPSniperPackets.enabled = this.sniperPackets;
                MPNoPositionCompression.enabled = this.noCompression;
                RearView.MPMenuManagerEnabled = this.allowRearView;
                Menus.mms_scale_respawn_time = this.scaleRespawnTime;
                Menus.mms_classic_spawns = this.classicSpawnsEnabled;
                Menus.mms_ctf_boost = this.ctfCarrierBoostEnabled;
                Menus.mms_always_cloaked = this.alwaysCloaked;
                Menus.mms_allow_smash = this.allowSmash;
                Menus.mms_boost_fire = this.boostFire;
                Menus.mms_instareap = this.instareap;
                Menus.mms_damage_numbers = this.damageNumbers;
                Menus.mms_assist_scoring = this.assistScoring;
                MPTeams.MenuManagerTeamCount = this.teamCount;
                Menus.mms_collision_mesh = this.shipMeshCollider;
                MPColliderSwap.colliderScale = this.colliderScale;
                MPThunderboltPassthrough.isAllowed = this.thunderboltPassthrough;
                MPLoadouts.LoadoutFilterBitmask = this.loadoutFilterBitmask;
                MPServerOptimization.prefEnabled = this.serverOptimizations;
                MPServerOptimization.RollSpeedLimit = this.rollSpeedLimit;
            }
        }

        static IEnumerator GetMatchPresets()
        {
            HashSet<string> urls = new HashSet<string>() { "https://otl.gg/olmod/settings.json", "https://octcache.playoverload.online/O1L.json" };
            JToken jUrls = null;
            Config.Settings.TryGetValue("matchPresetUrls", out jUrls);
            if (jUrls != null)
            {
                foreach (var jUrl in jUrls)
                {
                    urls.Add(jUrl.Value<string>());
                }
            }            

            foreach (var url in urls)
            {
                UnityWebRequest www = UnityWebRequest.Get(url);
                yield return www.SendWebRequest();

                if (www.isNetworkError || www.isHttpError)
                {
                    uConsole.Log(www.error);
                }
                else
                {
                    List<MPMatchPreset> _presets = JsonConvert.DeserializeObject<List<MPMatchPreset>>(www.downloadHandler.text);
                    foreach (var preset in _presets)
                    {
                        // Convert old timeLimit enums to new timeLimit ints.
                        if (preset.timeLimit < 60) {
                            switch (preset.timeLimit) {
                                case ((int)MatchTimeLimit.MIN_3):
                                    preset.timeLimit = 3 * 60;
                                    break;
                                case ((int)MatchTimeLimit.MIN_5):
                                    preset.timeLimit = 5 * 60;
                                    break;
                                case ((int)MatchTimeLimit.MIN_7):
                                    preset.timeLimit = 7 * 60;
                                    break;
                                case ((int)MatchTimeLimit.MIN_10):
                                    preset.timeLimit = 10 * 60;
                                    break;
                                case ((int)MatchTimeLimit.MIN_15):
                                    preset.timeLimit = 15 * 60;
                                    break;
                                case ((int)MatchTimeLimit.MIN_20):
                                    preset.timeLimit = 20 * 60;
                                    break;
                            }
                        }
                        presets.Add(preset);
                    }
                }
            }
        }

    }
}
