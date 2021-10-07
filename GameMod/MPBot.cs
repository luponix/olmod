using HarmonyLib;
using Overload;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace GameMod
{
    class MPBot
    {
        /*
         * 0: Toggle this Overload instance to act as a bot when the -bot argument gets passed
         * 1: Load name, settings, initial command from a file specified through the -bot argument 
         * 2: Allow sending commands to the bot through socket ? -> join ip_pw, shutdown, reload, etc
         * 3: Small Library for handling
         *      - join game/lobby
         *      - set ready flag
         *      - send loadout
         *      - pick loadout
         *      - movement
         *      - turning
         *      - shoot weapon
         *      - shoot missile
         *      - swap weapon
         *      - swap missile
         *      - exit game
         *      - get position
         *      - get enemy position
         *      - check enemy visibility
         *      - check projectile intersection with sphere
         *      - autojoin ip
         * 4: build bot behaviour on top of these functions
         *      - am i getting shot at ? -> dodge -> active dodging and reactive dodging
         *      - who should i shoot at/should i change the target ? -> context: did i damage xyz, is xyz damaged, am i getting focused by xyz, am i low
         *      - when do i shoot ? target + line of sight on the target or strong prediction on the target
         *      - what do i shoot
         *      - where do i shoot
         *      - where do i move (macro) ? tied to current target, + random roaming, roughly guided by player density (add all player positions together and divide by player count and get the closest segment to that position and travel to that one)
         *      - where do i move (micro) ? tied to dodging, aiming ?, when getting into a fight stay away from walls
         */

        public static bool isBot = false;   // determines wether this overload/olmod instance runs as a bot, gets set by passing the -bot "path to config_file" argument to olmod

        [HarmonyPatch(typeof(Overload.GameManager), "Awake")]
        class MPBot_GameManager_Awake
        {
            private static void Postfix()
            {
                isBot = Core.GameMod.FindArgVal("-bot", out string botConfig);
                if (isBot)
                    Initialisation(botConfig);
            }
        }

        private static void Initialisation(string configfile_path)
        {


            Debug.Log("MPBot: this overload instance is started as a bot. Attempting to load config: " + configfile_path);
            LoadConfigFile(configfile_path);

            // Setup to enable joining mp games
            


        }

        private static void LoadConfigFile(string file_path)
        {

        }

        // FOR TESTING
        static float time_till_executing_commands = 10f;
        static int executed = 0;
        [HarmonyPatch(typeof(Overload.GameManager), "Update")]
        class MPBot_GameManager_Awake2
        {
            private static void Postfix()
            {
                time_till_executing_commands -= Time.fixedDeltaTime;
                if (time_till_executing_commands <= 0f && executed == 0)
                {
                    executed = 1;


                    GameplayManager.SetGameType(GameType.MULTIPLAYER);
                    MPInternet.Enabled = true;
                    MenuManager.m_game_paused = false;
                    GameplayManager.DifficultyLevel = 3;
                    PlayerShip.DeathPaused = false;

                    if (!NetworkManager.IsHeadless())
                    {
                        Action<string, string> callback = delegate (string error, string player_id)
                        {
                            if (error != null)
                            {
                                NetworkMatch.SetPlayerId("00000000-0000-0000-0000-000000000000");
                            }
                            else
                            {
                                //Debug.Log("MPServerBrowser: Set player id to " + player_id);
                                NetworkMatch.SetPlayerId(player_id);
                            }
                        };
                        NetworkMatch.GetMyPlayerId(PilotManager.PilotName, callback);
                    }

                    //NetworkMatch.SetPlayerId("00000000-0000-0000-0000-000000000000");

                    MenuManager.m_mp_lan_match = true;
                    MenuManager.m_mp_private_match = true;
                    NetworkMatch.SetNetworkGameClientMode(NetworkMatch.NetworkGameClientMode.Invalid);
                    MenuManager.ClearMpStatus();







                    time_till_executing_commands = 1f;

                }
                if(time_till_executing_commands <= 0f && executed == 1)
                {
                    executed = 2;

                    // temporary commands for testing:
                    Library.JoinMatch("188.228.46.89_pt");
                }

            }
        }





        class Library
        {
            private static FieldInfo _InternetMatch_ServerAddress_Field = typeof(GameManager).Assembly.GetType("InternetMatch").GetField("ServerAddress", BindingFlags.Static | BindingFlags.Public);
            public static void JoinMatch(string ip)
            {
                UIManager.DestroyAll(false);
                NetworkMatch.SetNetworkGameClientMode(NetworkMatch.NetworkGameClientMode.LocalLAN);
                NetworkMatch.m_match_req_password = ip;
                MPInternet.ServerAddress = MPInternet.FindPasswordAddress(ip, out string msg);
                MPInternet.MenuPassword = ip;
                if (Core.GameMod.HasInternetMatch())
                {
                    _InternetMatch_ServerAddress_Field.SetValue(null, MPInternet.ServerAddress);
                }
                MenuManager.m_mp_status = Loc.LS("JOINING " + MPInternet.ClientModeName());
                NetworkMatch.JoinPrivateLobby(MPInternet.MenuPassword);
            }
        }

        class Behaviour
        {

        }




    }
}
