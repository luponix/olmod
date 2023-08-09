using HarmonyLib;
using Overload;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

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
         *    INPUT
         *      - movement
         *      - turning
         *      - shoot weapon
         *      - shoot missile
         *      - swap weapon
         *      - swap missile
         *      
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


        public static bool isIngame = false;


        [HarmonyPatch(typeof(Overload.GameManager), "Awake")]
        class MPBot_GameManager_Awake
        {
            private static void Postfix()
            {
                isBot = Core.GameMod.FindArgVal("-bot", out string botConfig);
                if (isBot)
                {
                    Initialisation(botConfig);
                    Debug.Log(botConfig);
                    MenuManager.PlaySelectSound(1f);
                    UIManager.DestroyAll(false);
                    MenuManager.ChangeMenuState(MenuState.MP_MENU, false);
                }

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
        /*
        static float time_till_executing_commands = 3f;
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

                    if (!Overload.NetworkManager.IsHeadless())
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
                if (time_till_executing_commands <= 0f && executed == 1)
                {
                    executed = 2;

                    // temporary commands for testing:
                    Library.JoinMatch("194.59.206.166_pt");
                }
                
            }
        }
        */




        class Library
        {
            // Tested: works only after resetting the network initialisation. figure out what you initialise incorrectly
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

            // not tested
            public static void SetReadyFlag()
            {
                Client.SendReadyForCountdownMessage();
            }

            // not tested
            public static void SendLoadoutToServer()
            {
                Client.SendPlayerLoadoutToServer();
            }

            // not tested
            public static void SetLoadout(int Mp_loadout1, int Mp_loadout2, int Mp_modifier1, int Mp_modifier2, WeaponType Mp_custom1_w1, MissileType Mp_custom1_m1, MissileType Mp_custom1_m2, WeaponType Mp_custom2_w1, WeaponType Mp_custom2_w2, MissileType Mp_custom2_m1, int glow_color, int decal_color, int decal_pattern, int mesh_wings, int mesh_body)
            {
                Player.Mp_loadout1 = Mp_loadout1;
                Player.Mp_loadout2 = Mp_loadout2;
                Player.Mp_modifier1 = Mp_modifier1;
                Player.Mp_modifier2 = Mp_modifier2;
                Player.Mp_custom1_w1 = Mp_custom1_w1;
                Player.Mp_custom1_m1 = Mp_custom1_m1;
                Player.Mp_custom1_m2 = Mp_custom1_m2;
                Player.Mp_custom2_w1 = Mp_custom2_w1;
                Player.Mp_custom2_w2 = Mp_custom2_w2;
                Player.Mp_custom2_m1 = Mp_custom2_m1;
                MenuManager.mpc_glow_color = glow_color;
                MenuManager.mpc_decal_color = decal_color;
                MenuManager.mpc_decal_pattern = decal_pattern;
                MenuManager.mpc_mesh_wings = mesh_wings;
                MenuManager.mpc_mesh_body = mesh_body;
            }

            // not tested
            public static void ExitMatch()
            {
                NetworkMatch.ExitMatchToMainMenu();
            }

            public class Input
            {
                /* m_input_count[index]
                 * index:
                 * 14   = fire weapon
                 * 15   = fire missile
                 * 27   = slide modifier
                 * 19   = use boost
                 * 12   = roll left 90
                 * 13   = roll right 90
                 * 18   = fire flare
                 * 20   = toggle headlight
                 * 
                 * the rest of the relevant informations gets encoded from
                 * .cc_move_vec.x   = SLIDE_LEFT, SLIDE_RIGHT
                 * .cc_move_vec.y   = SLIDE_DOWN, SLIDE_UP
                 * .cc_move_vec.z   = MOVE_BACK, MOVE_FORE
                 * 
                 * .cc_turn_vec.x   = PITCH_UP, PITCH_DOWN
                 * .cc_turn_vec.y   = TURN_LEFT, TURN_RIGHT
                 * .cc_turn_vec.z   = ROLL_LEFT, ROLL_RIGHT
                 */

                public static void SetButton(CCInput button, int val)
                {
                    GameManager.m_local_player.m_input_count[(int)button] = val;
                }

                public static void SetMovementAxis(CCInput axis, float val)
                {
                    switch (axis)
                    {
                        case CCInput.SLIDE_LEFT:
                            GameManager.m_local_player.cc_move_vec.x = -val;
                            break;
                        case CCInput.SLIDE_RIGHT:
                            GameManager.m_local_player.cc_move_vec.x = val;
                            break;
                        case CCInput.SLIDE_DOWN:
                            GameManager.m_local_player.cc_move_vec.y = -val;
                            break;
                        case CCInput.SLIDE_UP:
                            GameManager.m_local_player.cc_move_vec.y = val;
                            break;
                        case CCInput.MOVE_BACK:
                            GameManager.m_local_player.cc_move_vec.z = -val;
                            break;
                        case CCInput.MOVE_FORE:
                            GameManager.m_local_player.cc_move_vec.z = val;
                            break;
                        case CCInput.PITCH_UP:
                            GameManager.m_local_player.cc_turn_vec.x = -val;
                            break;
                        case CCInput.PITCH_DOWN:
                            GameManager.m_local_player.cc_turn_vec.x = val;
                            break;
                        case CCInput.TURN_LEFT:
                            GameManager.m_local_player.cc_turn_vec.y = -val;
                            break;
                        case CCInput.TURN_RIGHT:
                            GameManager.m_local_player.cc_turn_vec.y = val;
                            break;
                        case CCInput.ROLL_LEFT:
                            GameManager.m_local_player.cc_turn_vec.z = -val;
                            break;
                        case CCInput.ROLL_RIGHT:
                            GameManager.m_local_player.cc_turn_vec.z = -val;
                            break;
                        default:
                            Debug.Log("MPBot.Library.SetMovementAxis: passed invalid ccinput: " + axis);
                            break;
                    }
                }


                public static void MoveTowards(Vector3 target)
                {
                    TurnTowards(target);
                    MoveTowardsUncoupled(target);
                }

                public static void TrichordTowards(Vector3 target)
                {

                }

                // moves towards the target point without changing the orientation
                public static void MoveTowardsUncoupled(Vector3 target)
                {
                    Vector3 target_vector = target - GameManager.m_local_player.c_player_ship.c_transform.position;
                    target_vector.Normalize();
                    target_vector = GameManager.m_local_player.c_player_ship.c_transform.rotation * target_vector;

                    if (GameManager.m_local_player.cc_move_vec.x < -0.5f) target_vector.x = -1f;
                    if (GameManager.m_local_player.cc_move_vec.x > 0.5f) target_vector.x = 1f;
                    if (GameManager.m_local_player.cc_move_vec.y < -0.5f) target_vector.y = -1f;
                    if (GameManager.m_local_player.cc_move_vec.y > 0.5f) target_vector.y = 1f;
                    if (GameManager.m_local_player.cc_move_vec.z < -0.5f) target_vector.z = -1f;
                    if (GameManager.m_local_player.cc_move_vec.z > 0.5f) target_vector.z = 1f;

                    GameManager.m_local_player.cc_move_vec.x = target_vector.x;
                    GameManager.m_local_player.cc_move_vec.y = target_vector.y;
                    GameManager.m_local_player.cc_move_vec.z = target_vector.z;

                }

                /*
                public static float debug1 = 0f;
                public static float debug2 = 0f;
                public static float debug3 = 0f;
                public static Vector3 debug4 = Vector3.zero;
                public static Vector3 debug5 = Vector3.zero;
                public static Vector3 debug6 = Vector3.zero;

                [HarmonyPatch(typeof(Overload.UIElement), "DrawHUD")]
                class MPBot_GameManager_Awake
                {
                    private static void Postfix(UIElement __instance)
                    {
                        Vector2 v = new Vector2(-600, -300f);
                        __instance.DrawStringSmall("vertical angle: ", v, 0.6f, StringOffset.LEFT, Color.red, 0.6f);
                        v.x += 200f;
                        __instance.DrawStringSmall(debug1.ToString("n2"), v, 0.6f, StringOffset.LEFT, Color.red, 0.6f);
                        v.x -= 200f;
                        v.y += 50f;
                        __instance.DrawStringSmall("horizont angle: ", v, 0.6f, StringOffset.LEFT, Color.red, 0.6f);
                        v.x += 200f;
                        __instance.DrawStringSmall(debug2.ToString("n2"), v, 0.6f, StringOffset.LEFT, Color.red, 0.6f);
                        v.y += 50f;
                        v.x -= 200f;
                        __instance.DrawStringSmall("rolling angle: ", v, 0.6f, StringOffset.LEFT, Color.red, 0.6f);
                        v.x += 200f;
                        __instance.DrawStringSmall(debug3.ToString("n2"), v, 0.6f, StringOffset.LEFT, Color.red, 0.6f);


                        v.y += 80f;
                        v.x -= 200f;
                        __instance.DrawStringSmall("targeted pos: ", v, 0.6f, StringOffset.LEFT, Color.red, 0.6f);
                        v.x += 200f;
                        __instance.DrawStringSmall(debug4.ToString(), v, 0.6f, StringOffset.LEFT, Color.red, 0.6f);

                        v.y += 50f;
                        v.x -= 200f;
                        __instance.DrawStringSmall("current pos: ", v, 0.6f, StringOffset.LEFT, Color.red, 0.6f);
                        v.x += 200f;
                        __instance.DrawStringSmall(debug5.ToString(), v, 0.6f, StringOffset.LEFT, Color.red, 0.6f);


                        v.y += 50f;
                        v.x -= 200f;
                        __instance.DrawStringSmall("angular vel: ", v, 0.6f, StringOffset.LEFT, Color.red, 0.6f);
                        v.x += 200f;
                        __instance.DrawStringSmall((GameManager.m_local_player.c_player_ship.c_transform.localRotation * debug6).ToString(), v, 0.6f, StringOffset.LEFT, Color.red, 0.6f);
                        v.x += 150f;
                        __instance.DrawStringSmall(debug6.magnitude.ToString(), v, 0.6f, StringOffset.LEFT, Color.red, 0.6f);

                    }
                }

                public static PlayerSnapshot GetPlayerSnapshotFromInterpolationBuffer(Player p, PlayerSnapshotToClientMessage msg)
                {
                    for (int i = 0; i < msg.m_num_snapshots; i++)
                    {
                        PlayerSnapshot playerSnapshot = msg.m_snapshots[i];
                        if (GetPlayerFromNetId(playerSnapshot.m_net_id) == p)
                        {
                            return playerSnapshot;
                        }
                    }
                    return null;
                }

                private static Player GetPlayerFromNetId(NetworkInstanceId net_id)
                {
                    GameObject gameObject = ClientScene.FindLocalObject(net_id);
                    if (gameObject == null)
                    {
                        return null;
                    }
                    Player component = gameObject.GetComponent<Player>();
                    if (component == null)
                    {
                        Debug.LogErrorFormat("Failed to find Player component on gameObject {0} with netId {1}", new object[]
                        {
                    gameObject.name,
                    net_id
                        });
                        return null;
                    }
                    return component;
                }*/
                
                private static Vector3 diff_angle_old = Vector3.zero;
                private static Vector3 diff_angle_cur = Vector3.zero;

                // needs improvement when estimating the force necessary to catch the ships momentum when closing in on the target vector
                // target = world coordinates
                public static void TurnTowards(Vector3 target)
                {
                    //debug4 = target;
                    //debug5 = GameManager.m_local_player.c_player_ship.c_transform.position;
                    //debug6 = GameManager.m_local_player.c_player_ship.c_rigidbody.angularVelocity;

                    Vector3 target_vector = target - GameManager.m_local_player.c_player_ship.c_transform.position;
                    target_vector.Normalize();

                    Quaternion a = Quaternion.identity * Quaternion.Inverse(GameManager.m_local_player.c_player_ship.c_transform.rotation);
                    Quaternion b = Quaternion.identity * Quaternion.Inverse(Quaternion.LookRotation(target_vector, GameManager.m_local_player.c_player_ship.c_transform.rotation * Vector3.up));
                    Vector3 delta_rot = (b * Quaternion.Inverse(a)).eulerAngles;
                    diff_angle_old = diff_angle_cur;
                    diff_angle_cur = delta_rot;
                    // UP,DOWN
                    if (delta_rot.x <= 180) GameManager.m_local_player.cc_turn_vec.x = calculateForce(-delta_rot.x);//-0.6f;
                    else GameManager.m_local_player.cc_turn_vec.x = calculateForce(delta_rot.x - 180f);//0.6f;

                    // LEFT,RIGHT
                    if (delta_rot.y <= 180) GameManager.m_local_player.cc_turn_vec.y = calculateForce(-delta_rot.y);//-0.6f;//Mathf.Lerp(0f, 1f, diff_rotation.x / 180f);
                    else GameManager.m_local_player.cc_turn_vec.y = calculateForce(delta_rot.y - 180f);//0.6f;//Mathf.Lerp(0f, -1f, diff_rotation.x / 180f);

                    // ROLL L,ROLL R
                    //float roll_median = (target_angle > 180f) ? target_angle - 180f : target_angle + 180f;
                    //if (diff_rotation.z < 179.9f) GameManager.m_local_player.cc_turn_vec.z = 0.6f;
                    //if (diff_rotation.z > 180.1f) GameManager.m_local_player.cc_turn_vec.z = -0.6f;

                    // debug1 = delta_rot.x;
                    // debug2 = delta_rot.y;
                    // debug3 = delta_rot.z;
                }

                private const float speed_multiplicator = 6f;
                private static float calculateForce(float angle_diff)
                {
                    int s = 1;
                    if (angle_diff < 0f)
                    {
                        angle_diff *= -1;
                        s = -1;
                    }
                    if (angle_diff < 0.4f) return 0f;

                    Vector3 angularMomentum = new Vector3(
                        GameManager.m_local_player.c_player_ship.c_rigidbody.inertiaTensor.x * GameManager.m_local_player.c_player_ship.c_rigidbody.angularVelocity.x,
                        GameManager.m_local_player.c_player_ship.c_rigidbody.inertiaTensor.y * GameManager.m_local_player.c_player_ship.c_rigidbody.angularVelocity.y,
                        GameManager.m_local_player.c_player_ship.c_rigidbody.inertiaTensor.z * GameManager.m_local_player.c_player_ship.c_rigidbody.angularVelocity.z
                    );


                    float ang_vel = GameManager.m_local_player.c_player_ship.c_rigidbody.angularVelocity.magnitude;

                    //uConsole.Log(angle_difference+" : "+GameManager.m_local_player.c_player_ship.c_rigidbody.angularVelocity.magnitude.ToString());

                    if (angle_diff > 7) return 1f * s;
                    else
                    {
                        float force = Mathf.Pow(angle_diff / 7f, 2) * (1f - (ang_vel / 8f));// (Mathf.Pow((ang_vel / 7f), 2) * (angle_diff / 0.2f)) * -1;

                        //float accel_force = (Mathf.Pow(angle_difference, 2f) * 1f - (ang_vel.magnitude / 7f)) * s; 
                        //float deccel_force = (Mathf.Pow((ang_vel / 7f), 2) * (angle_difference / 0.2f)) * -1;//((1f - angle_difference) * (ang_vel.magnitude / 7f)) * -s;//(1f - angle_difference) * -s;
                        return force * -1f;
                    }

                }

                public static void TrichordTowards()
                {

                }








            }










        }





        public class Behaviour
        {
            public static Vector3 preferred_movement_direction = Vector3.zero;
            public static Vector3 target = Vector3.zero;            // holds the world coordinates of the currently prioritized pilot
            public static Vector3 movement_vector = Vector3.zero;   // preserves the chosen movement inputs to apply them later during the tick
            public static Vector3 turn_vector = Vector3.zero;       // preserves the chosen turn inputs to apply them later during the tick

            ComputeShader cs = (ComputeShader)Resources.Load("NameOfShader");


            // process the situation and decide on button presses
            [HarmonyPatch(typeof(PlayerShip), "FixedUpdateAll")]
            class MPBot_Behaviour_PlayerShip_FixedUpdateAll
            {
                static void Prefix(PlayerShip __instance)
                {
                    UpdateSituation();
                    UpdateMovementAndTurnInput();
                    UpdateKeyInput();
                }

                static void Postfix()
                {

                }
            }

            // apply movement and turn inputs
            [HarmonyPatch(typeof(PlayerShip), "FixedUpdateReadCachedControls")]
            class MPBot_Behaviour_PlayerShip_FixedUpdateReadCachedControls
            {
                static void Postfix()
                {
                    ApplyInputs();
                }
            }


            // assesses the status and updates where to move and who to prioritize
            private static void UpdateSituation()
            {
                // go through all projectiles and categorize them into cubes around the player to figure out undesirable path
                // also eliminate the cubes that are close and in direct line of enemy player orientations



                // go through all projectiles and rate which ones need to be dodged and wether trichording is necessary
                //  ignore: driller, crusher, flak projectiles

                // map 


                // try to find the vector that dodges the most projectiles



            }

            private static void UpdateMovementAndTurnInput()
            {


                Library.Input.TurnTowards(target);



                movement_vector = GameManager.m_local_player.cc_move_vec;
                turn_vector = GameManager.m_local_player.cc_turn_vec;
            }

            private static void UpdateKeyInput()
            {
                Library.Input.SetButton(CCInput.FIRE_WEAPON, 1);
            }





            private static void UpdateTarget()
            {
                // targeting will store several informations to calculate the targeted point according to the current weapon
            }

            // assesses the current situation and changes the state and target for movement, turning, firing accordingly
            private static void UpdateGoals()
            {

            }


            private static void ApplyInputs()
            {
                GameManager.m_local_player.cc_move_vec = movement_vector;
                GameManager.m_local_player.cc_turn_vec = turn_vector;
            }


            public static bool isActive()
            {
                return isBot && GameplayManager.IsMultiplayer && !GameManager.m_local_player.c_player_ship.m_dead;
            }




            // other events
            // on death
            // on spawn
            // on game start
            // on game end
            // on exit
            // on disconnect





        }

    }
}
