using HarmonyLib;
using Overload;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace GameMod
{
    class MatchModeInstareap
    {


        [HarmonyPatch(typeof(GameplayManager), "StartLevel")]
        class MatchModeInstareap_GameplayManager_StartLevel
        {
            static void Prefix()
            {
                if (MPModPrivateData.MatchMode != ExtMatchMode.INSTAREAP)
                    return;



                foreach (var item in GameObject.FindObjectsOfType<Item>())
                {
                    Debug.Log("Destroyed an Item!");
                    UnityEngine.Object.Destroy(item.c_go);
                }


            }















            // Todo
            //  - Add the raycast
            //  - Shake the camera when shooting a shot
            //  - Add the meshes of the beam and halo
            //  - refine the materials, meshes
            //  - scale the tori with a sinus curve + offset
            //  - reduce the transparency exponentially with the time passed (take a look at propery non blocked for this)
            //  - add a zoom function
            //  - DONE define an instareap preset
            //
            //  - DONE (maybe) fix the scoreboard
            //  - fix the item array resizing
            //
            // add the sound effects
            // make it oneshot
            // remove all items and spawns on the map
            // remove death spew
            // remove the indicator for changed proj data files in instareap and grey out the option to do so
            // dont show the loadouts while dead

            private const float MD_COOLDOWN_TIME = 1.5f;            // the time in seconds that needs to pass after firing a shot to be able to fire again
            private const float MD_TIME_TILL_ZOOM_KICKS_IN = 2f;    // the time in seconds between pressing the fire button and the camera starting to zoom in
            private static float massdriver_refire_time = 0f;


            // Block Firing inputs to avoid setting off a bunch of the original logic and handle firing the massdriver yourself instead
            [HarmonyPatch(typeof(Controls), "UpdateKey")]
            class MatchModeInstareap_Controls_UpdateKey
            {
                static bool Prefix(CCInput cc_type)
                {
                    //if(GameManager.m_local_player != null && GameManager.m_local_player.c_player_ship != null) uConsole.Log(GameManager.m_local_player.c_player_ship.c_transform_position.ToString());

                    if (MPModPrivateData.MatchMode != ExtMatchMode.INSTAREAP)
                        return true;

                    if (cc_type == CCInput.FIRE_WEAPON)
                    {
                        // Todo: handle md fire here
                        if (GameplayManager.m_game_type == GameType.MULTIPLAYER && NetworkMatch.GetMatchState() == MatchState.PLAYING && massdriver_refire_time <= 0)
                        {

                            return false;
                        }
                    }
                    if (cc_type == CCInput.FIRE_MISSILE)
                    {
                        return false;
                    }
                    return true; // Dont block other inputs
                }
            }


            [HarmonyPatch(typeof(PlayerShip), "Awake")]
            class MatchModeInstareap_PlayerShip_Awake
            {
                static void Postfix(PlayerShip __instance)
                {
                    if (MPModPrivateData.MatchMode != ExtMatchMode.INSTAREAP)
                        return;


                    massdriver_refire_time = 0f;


                }
            }

            public static void InitialiseMeshesAndMaterials()
            {
                // create beam mesh
                mesh_beam = GenerateBeamMesh();
                // create halo mesh
                mesh_halo = GenerateHaloMesh();
                // create torus mesh
                mesh_torus = GenerateTorusMesh();

                // generate materials
            }


            public static Mesh GenerateTorusMesh()
            {
                var mesh = new Mesh
                {
                    name = "Massdriver Projectile Torus Mesh"
                };
                mesh.vertices = new Vector3[] {
                   new Vector3(-0.001664f,-1.249905f,0.073384f),
                   new Vector3(0.123023f,-1.227861f,-0.064259f),
                   new Vector3(0.215399f,-1.137394f,-0.074549f),
                   new Vector3(0.216761f,-0.884123f,0.056394f),
                   new Vector3(0.127542f,-0.782722f,-0.103047f),
                   new Vector3(-0.001834f,-0.754625f,0.005378f),
                   new Vector3(-0.126107f,-0.782817f,-0.099301f),
                   new Vector3(-0.215995f,-0.878904f,-0.133426f),
                   new Vector3(-0.250626f,-1.008130f,0.132723f),
                   new Vector3(-0.218047f,-1.099983f,-0.297882f),
                   new Vector3(-0.123362f,-1.223461f,0.014431f),
                   new Vector3(0.001684f,-1.227725f,-0.250364f),
                   new Vector3(0.249630f,-0.856977f,-0.559072f),
                   new Vector3(0.215995f,-0.760941f,-0.459612f),
                   new Vector3(-0.000000f,-0.711708f,-0.241593f),
                   new Vector3(-0.250000f,-0.880732f,-0.508491f),
                   new Vector3(-0.125373f,-1.131388f,-0.468637f),
                   new Vector3(-0.002880f,-1.081881f,-0.638258f),
                   new Vector3(0.125095f,-1.094000f,-0.539501f),
                   new Vector3(0.216855f,-0.972042f,-0.577732f),
                   new Vector3(0.122034f,-0.680654f,-0.392975f),
                   new Vector3(-0.123429f,-0.683658f,-0.388541f),
                   new Vector3(-0.000000f,-0.569871f,-0.499763f),
                   new Vector3(-0.216415f,-0.688961f,-0.546909f),
                   new Vector3(0.125711f,-0.808812f,-0.922274f),
                   new Vector3(-0.216855f,-0.809629f,-0.789392f),
                   new Vector3(-0.127440f,-0.857455f,-0.869186f),
                   new Vector3(-0.000000f,-0.825935f,-0.941799f),
                   new Vector3(0.217074f,-0.617518f,-0.953555f),
                   new Vector3(0.126273f,-0.448578f,-0.652583f),
                   new Vector3(-0.123841f,-0.411546f,-0.681363f),
                   new Vector3(0.216415f,-0.427224f,-0.768932f),
                   new Vector3(-0.216271f,-0.376046f,-0.799788f),
                   new Vector3(-0.249766f,-0.455911f,-0.900790f),
                   new Vector3(-0.000000f,-0.481067f,-1.161397f),
                   new Vector3(0.250000f,-0.400007f,-0.922454f),
                   new Vector3(0.001834f,-0.283814f,-0.699241f),
                   new Vector3(-0.217074f,-0.380585f,-1.070397f),
                   new Vector3(-0.125712f,-0.473775f,-1.127509f),
                   new Vector3(0.123959f,-0.395852f,-1.154106f),
                   new Vector3(0.216701f,-0.210010f,-1.107274f),
                   new Vector3(0.215701f,-0.055355f,-0.879973f),
                   new Vector3(0.124942f,-0.162073f,-0.768071f),
                   new Vector3(0.001664f,-0.090389f,-1.248791f),
                   new Vector3(0.125355f,-0.075375f,-1.216554f),
                   new Vector3(0.250319f,0.123783f,-1.010447f),
                   new Vector3(-0.002440f,0.057367f,-0.755018f),
                   new Vector3(-0.126107f,-0.003727f,-0.789081f),
                   new Vector3(-0.216271f,0.074229f,-0.880660f),
                   new Vector3(-0.249681f,0.123783f,-1.010447f),
                   new Vector3(-0.122646f,-0.079822f,-1.217843f),
                   new Vector3(0.217542f,0.218239f,-1.112425f),
                   new Vector3(0.123322f,0.095164f,-0.781519f),
                   new Vector3(-0.216163f,0.149554f,-1.123063f),
                   new Vector3(-0.001684f,0.238524f,-1.230080f),
                   new Vector3(0.127605f,0.327853f,-1.177618f),
                   new Vector3(-0.128742f,0.385422f,-1.163700f),
                   new Vector3(0.216646f,0.430981f,-0.778289f),
                   new Vector3(-0.124771f,0.254991f,-0.742460f),
                   new Vector3(0.000000f,0.554037f,-1.123476f),
                   new Vector3(0.125670f,0.349789f,-0.703668f),
                   new Vector3(0.000000f,0.377126f,-0.653202f),
                   new Vector3(-0.216304f,0.490193f,-0.733626f),
                   new Vector3(-0.217175f,0.566926f,-0.976412f),
                   new Vector3(0.125711f,0.808812f,-0.922274f),
                   new Vector3(0.216541f,0.626705f,-0.937931f),
                   new Vector3(0.250000f,0.820214f,-0.629372f),
                   new Vector3(-0.123322f,0.485326f,-0.619909f),
                   new Vector3(-0.250234f,0.657752f,-0.765924f),
                   new Vector3(0.000000f,0.825936f,-0.941798f),
                   new Vector3(0.122590f,0.597438f,-0.516151f),
                   new Vector3(-0.125829f,0.908537f,-0.827226f),
                   new Vector3(0.216167f,0.955888f,-0.615584f),
                   new Vector3(0.001834f,0.601958f,-0.455120f),
                   new Vector3(-0.216167f,0.934527f,-0.647552f),
                   new Vector3(0.000000f,1.041548f,-0.695940f),
                   new Vector3(0.216415f,0.754453f,-0.452303f),
                   new Vector3(-0.124466f,0.716147f,-0.336719f),
                   new Vector3(-0.216773f,0.762489f,-0.441738f),
                   new Vector3(0.125091f,1.086793f,-0.551729f),
                   new Vector3(-0.250319f,0.980900f,-0.272321f),
                   new Vector3(0.001664f,1.182845f,-0.410520f),
                   new Vector3(0.126205f,0.788381f,-0.114895f),
                   new Vector3(-0.001728f,0.729793f,-0.188214f),
                   new Vector3(-0.123959f,1.154107f,-0.395852f),
                   new Vector3(0.123281f,1.213945f,-0.165655f),
                   new Vector3(0.218200f,1.141735f,0.019554f),
                   new Vector3(0.215952f,0.869972f,-0.126986f),
                   new Vector3(-0.215308f,0.886355f,-0.002361f),
                   new Vector3(-0.217287f,1.145473f,-0.000000f),
                   new Vector3(0.003057f,1.257708f,-0.008963f),
                   new Vector3(0.249766f,1.008062f,0.055565f),
                   new Vector3(-0.124466f,0.788561f,0.066466f),
                   new Vector3(-0.125355f,1.216554f,-0.075375f),
                   new Vector3(0.002440f,0.744139f,0.140001f),
                   new Vector3(0.126293f,1.161193f,0.401729f),
                   new Vector3(0.216770f,0.842054f,0.270049f),
                   new Vector3(-0.249698f,0.942534f,0.378530f),
                   new Vector3(-0.127439f,1.181465f,0.307984f),
                   new Vector3(-0.003449f,1.186035f,0.402604f),
                   new Vector3(0.124939f,0.743919f,0.252526f),
                   new Vector3(0.216766f,0.943234f,0.630248f),
                   new Vector3(0.250000f,0.841681f,0.562393f),
                   new Vector3(-0.126273f,0.652583f,0.448578f),
                   new Vector3(-0.216400f,0.762818f,0.440413f),
                   new Vector3(-0.217542f,0.940922f,0.632285f),
                   new Vector3(0.000000f,1.041548f,0.695939f),
                   new Vector3(0.124542f,0.593993f,0.520917f),
                   new Vector3(-0.002596f,0.574446f,0.495384f),
                   new Vector3(-0.126355f,1.017664f,0.670249f),
                   new Vector3(0.127440f,0.869186f,0.857455f),
                   new Vector3(0.215899f,0.542360f,0.706817f),
                   new Vector3(0.000000f,0.765266f,0.997315f),
                   new Vector3(-0.215995f,0.429189f,0.778506f),
                   new Vector3(-0.250000f,0.525678f,0.870320f),
                   new Vector3(-0.123023f,0.696494f,1.013246f),
                   new Vector3(0.216282f,0.627778f,0.937527f),
                   new Vector3(0.250440f,0.256048f,0.999844f),
                   new Vector3(-0.124167f,0.345871f,0.711776f),
                   new Vector3(-0.217450f,0.453366f,1.048901f),
                   new Vector3(0.125372f,0.468637f,1.131388f),
                   new Vector3(0.124792f,0.313359f,0.722637f),
                   new Vector3(0.002440f,0.250825f,0.714444f),
                   new Vector3(0.000000f,0.402654f,1.186181f),
                   new Vector3(0.217219f,0.277205f,1.095872f),
                   new Vector3(0.215701f,0.055356f,0.879973f),
                   new Vector3(-0.127605f,0.171338f,1.210337f),
                   new Vector3(0.001684f,0.075926f,1.250690f),
                   new Vector3(0.123023f,-0.096558f,1.225744f),
                   new Vector3(0.124760f,0.000000f,0.788710f),
                   new Vector3(-0.124127f,0.046128f,0.783456f),
                   new Vector3(-0.216646f,-0.131871f,0.879823f),
                   new Vector3(-0.250000f,-0.066206f,1.010114f),
                   new Vector3(0.216809f,-0.242412f,1.110133f),
                   new Vector3(-0.000000f,-0.040436f,0.750368f),
                   new Vector3(-0.215594f,-0.147958f,1.123855f),
                   new Vector3(-0.001664f,-0.252617f,1.226309f),
                   new Vector3(-0.126205f,-0.315027f,0.731781f),
                   new Vector3(-0.123972f,-0.227728f,1.197711f),
                   new Vector3(0.249698f,-0.498316f,0.885063f),
                   new Vector3(0.217368f,-0.392105f,0.795112f),
                   new Vector3(0.124542f,-0.349431f,0.708576f),
                   new Vector3(-0.002596f,-0.329827f,0.683087f),
                   new Vector3(0.001684f,-0.559592f,1.121092f),
                   new Vector3(0.128742f,-0.673476f,1.024294f),
                   new Vector3(-0.249630f,-0.683077f,0.761824f),
                   new Vector3(-0.218200f,-0.710558f,0.893896f),
                   new Vector3(-0.125322f,-0.593967f,1.069040f),
                   new Vector3(0.216167f,-0.763992f,0.842010f),
                   new Vector3(-0.215701f,-0.579610f,0.664431f),
                   new Vector3(-0.000000f,-0.836661f,0.931972f),
                   new Vector3(0.215158f,-0.732653f,0.489544f),
                   new Vector3(0.124127f,-0.586604f,0.521370f),
                   new Vector3(-0.000000f,-0.598388f,0.459160f),
                   new Vector3(-0.124542f,-0.627342f,0.476318f),
                   new Vector3(-0.125091f,-0.906964f,0.814212f),
                   new Vector3(-0.001664f,-1.045758f,0.688505f),
                   new Vector3(0.129057f,-1.095034f,0.555461f),
                   new Vector3(0.250461f,-0.997421f,0.277692f),
                   new Vector3(0.217665f,-1.042659f,0.431884f),
                   new Vector3(0.124167f,-0.747869f,0.258734f),
                   new Vector3(-0.216401f,-0.813777f,0.337077f),
                   new Vector3(-0.216809f,-1.082610f,0.345131f),
                   new Vector3(-0.127605f,-1.124750f,0.478758f),
                   new Vector3(0.001684f,-1.188423f,0.397041f),
                   new Vector3(0.000998f,-0.709707f,0.246313f),
                   new Vector3(-0.123429f,-0.758159f,0.208680f),
                };

                mesh.triangles = new int[] {
                    11,1,0,12,3,158,14,6,5,7,15,8,9,16,10,16,11,10,11,18,1,18,2,1,19,12,2,13,20,4,21,7,6,22,21,14,25,16,9,27,24,17,28,12,19,29,22,20,22,30,21,30,23,21,23,33,15,34,24,27,28,35,12,35,31,12,36,30,22,37,26,25,38,27,26,34,39,24,39,28,24,40,35,28,31,42,29,42,36,29,43,39,34,44,40,39,40,45,35,45,41,35,36,47,30,47,32,30,32,49,33,49,37,33,50,34,38,51,45,40,41,52,42,52,46,42,53,50,37,54,44,43,55,51,44,53,56,50,50,54,43,57,52,41,58,48,47,59,55,54,60,46,52,61,58,46,55,65,51,65,45,51,66,57,45,67,62,58,68,63,49,69,64,59,63,71,56,71,59,56,73,67,61,74,71,63,75,64,69,66,76,57,76,70,57,67,78,62,78,68,62,75,79,64,79,72,64,78,80,68,80,74,68,81,79,75,82,73,70,74,84,71,84,75,71,81,85,79,85,72,79,86,66,72,66,87,76,87,82,76,88,80,78,80,89,74,89,84,74,90,85,81,91,87,66,89,93,84,84,90,81,94,92,83,95,86,85,96,82,87,88,97,80,89,98,93,96,100,82,100,94,82,98,99,90,86,102,91,102,96,91,92,104,88,104,97,88,106,95,99,100,108,94,108,103,94,105,109,98,109,99,98,110,101,95,102,111,96,111,107,96,112,110,106,104,114,97,114,105,97,115,106,109,110,116,101,116,102,101,118,113,103,119,115,105,112,120,110,120,116,110,111,121,107,121,108,107,122,118,108,123,120,112,124,117,116,119,126,115,126,123,115,127,120,123,125,129,121,129,122,121,130,113,118,113,132,114,132,119,114,129,134,122,134,130,122,135,126,119,138,127,126,139,125,117,140,129,125,129,142,134,142,137,134,143,128,136,144,133,128,132,146,135,135,147,138,147,136,138,149,145,131,139,151,140,140,152,141,152,142,141,153,137,142,154,149,137,146,155,147,155,150,147,150,157,144,157,148,144,148,158,139,158,151,139,159,158,148,160,153,152,154,161,149,161,145,149,146,163,155,163,156,155,164,157,156,165,154,153,166,161,154,8,162,145,3,160,151,164,1,157,157,2,159,160,5,165,5,166,165,10,164,163,4,5,160,5,6,166,6,7,166,10,11,0,2,12,158,12,13,3,3,13,4,4,14,5,8,15,9,11,17,18,18,19,2,4,20,14,14,21,6,16,17,11,20,22,14,21,23,7,7,23,15,17,24,18,18,24,19,15,25,9,25,26,16,16,26,17,24,28,19,13,29,20,26,27,17,12,31,13,13,31,29,30,32,23,23,32,33,15,33,25,29,36,22,33,37,25,37,38,26,38,34,27,39,40,28,35,41,31,31,41,42,43,44,39,42,46,36,36,46,47,47,48,32,32,48,49,37,50,38,50,43,34,44,51,40,49,53,37,54,55,44,50,56,54,45,57,41,46,58,47,57,60,52,60,61,46,58,62,48,48,62,49,49,63,53,53,63,56,56,59,54,59,64,55,55,64,65,65,66,45,61,67,58,62,68,49,57,70,60,60,70,61,71,69,59,64,72,65,65,72,66,70,73,61,68,74,63,73,77,67,67,77,78,71,75,69,76,82,70,82,83,73,73,83,77,84,81,75,85,86,72,77,88,78,86,91,66,83,92,77,77,92,88,84,93,90,82,94,83,90,95,85,91,96,87,80,97,89,93,98,90,90,99,95,95,101,86,86,101,102,94,103,92,92,103,104,97,105,89,89,105,98,96,107,100,100,107,108,109,106,99,106,110,95,103,113,104,104,113,114,105,115,109,115,112,106,116,117,102,102,117,111,108,118,103,114,119,105,121,122,108,120,124,116,115,123,112,117,125,111,111,125,121,127,128,120,120,128,124,122,130,118,130,131,113,113,131,132,126,127,123,128,133,124,124,133,117,132,135,119,127,136,128,134,137,130,130,137,131,135,138,126,138,136,127,133,139,117,139,140,125,140,141,129,129,141,142,143,144,128,131,145,132,132,145,146,135,146,147,147,143,136,144,148,133,133,148,139,137,149,131,143,150,144,147,150,143,140,151,152,152,153,142,153,154,137,150,156,157,155,156,150,157,159,148,151,160,152,145,162,146,146,162,163,160,165,153,165,166,154,161,8,145,163,164,156,158,3,151,164,0,1,157,1,2,159,2,158,166,7,161,161,7,8,162,10,163,10,0,164,3,4,160,8,9,162,162,9,10,
                };

                mesh.uv = new Vector2[] {
                                       new Vector2(0.592000f, 0.750000f),
                   new Vector2(0.490669f, 0.500000f),
                   new Vector2(0.585329f, 0.666667f),
                   new Vector2(0.456618f, 0.750000f),
                   new Vector2(0.582271f, 0.083333f),
                   new Vector2(0.523902f, 0.166667f),
                   new Vector2(0.498893f, 0.000000f),
                   new Vector2(0.583227f, 0.250000f),
                   new Vector2(0.542173f, 0.333333f),
                   new Vector2(0.562464f, 0.416667f),
                   new Vector2(0.572917f, 0.583333f),
                   new Vector2(0.508295f, 0.583333f),
                   new Vector2(0.489873f, 0.833333f),
                   new Vector2(0.583312f, 0.916667f),
                   new Vector2(0.520115f, 0.083333f),
                   new Vector2(0.479061f, 0.250000f),
                   new Vector2(0.498146f, 0.416667f),
                   new Vector2(0.658559f, 0.666667f),
                   new Vector2(0.510427f, 0.666667f),
                   new Vector2(0.586567f, 0.833333f),
                   new Vector2(0.520791f, 0.916667f),
                   new Vector2(0.614575f, 0.000000f),
                   new Vector2(0.623005f, 0.333333f),
                   new Vector2(0.675418f, 0.250000f),
                   new Vector2(0.584809f, 0.500000f),
                   new Vector2(0.635417f, 0.500000f),
                   new Vector2(0.686657f, 0.416667f),
                   new Vector2(0.635411f, 0.583333f),
                   new Vector2(0.654144f, 0.916667f),
                   new Vector2(0.614575f, 1.000000f),
                   new Vector2(0.606771f, 0.166667f),
                   new Vector2(0.688608f, 0.000000f),
                   new Vector2(0.769459f, 0.250000f),
                   new Vector2(0.687529f, 0.500000f),
                   new Vector2(0.684897f, 0.750000f),
                   new Vector2(0.669269f, 0.833333f),
                   new Vector2(0.695617f, 0.333333f),
                   new Vector2(0.626062f, 0.416667f),
                   new Vector2(0.697410f, 0.583333f),
                   new Vector2(0.720172f, 0.666667f),
                   new Vector2(0.716897f, 0.916667f),
                   new Vector2(0.749206f, 0.083333f),
                   new Vector2(0.688608f, 1.000000f),
                   new Vector2(0.740153f, 0.583333f),
                   new Vector2(0.769318f, 0.750000f),
                   new Vector2(0.740002f, 0.833333f),
                   new Vector2(0.771034f, 0.333333f),
                   new Vector2(0.680067f, 0.166667f),
                   new Vector2(0.822917f, 0.500000f),
                   new Vector2(0.739583f, 0.416667f),
                   new Vector2(0.780831f, 0.666667f),
                   new Vector2(0.769303f, 0.916667f),
                   new Vector2(0.762077f, 1.000000f),
                   new Vector2(0.780481f, 0.500000f),
                   new Vector2(0.793247f, 0.583333f),
                   new Vector2(0.800898f, 0.416667f),
                   new Vector2(0.830569f, 0.833333f),
                   new Vector2(0.802653f, 0.083333f),
                   new Vector2(0.763400f, 0.166667f),
                   new Vector2(0.823423f, 0.916667f),
                   new Vector2(0.833304f, 0.000000f),
                   new Vector2(0.762077f, 0.000000f),
                   new Vector2(0.862918f, 0.250000f),
                   new Vector2(0.864583f, 0.500000f),
                   new Vector2(0.882433f, 0.416667f),
                   new Vector2(0.895956f, 0.750000f),
                   new Vector2(0.855747f, 0.083333f),
                   new Vector2(0.843753f, 0.166667f),
                   new Vector2(0.833710f, 0.333333f),
                   new Vector2(0.864583f, 0.583333f),
                   new Vector2(0.916465f, 0.166667f),
                   new Vector2(0.896941f, 0.000000f),
                   new Vector2(0.956946f, 0.250000f),
                   new Vector2(0.903542f, 0.333333f),
                   new Vector2(0.906250f, 0.500000f),
                   new Vector2(0.914062f, 0.833333f),
                   new Vector2(0.886604f, 0.916667f),
                   new Vector2(0.959808f, 1.000000f),
                   new Vector2(0.925230f, 0.583333f),
                   new Vector2(0.908957f, 0.666667f),
                   new Vector2(0.946831f, 0.500000f),
                   new Vector2(0.973687f, 0.916667f),
                   new Vector2(0.896941f, 1.000000f),
                   new Vector2(0.053016f, 0.583333f),
                   new Vector2(0.978373f, 0.583333f),
                   new Vector2(0.988582f, 0.666667f),
                   new Vector2(0.976947f, 0.833333f),
                   new Vector2(0.988234f, 0.166667f),
                   new Vector2(0.987231f, 0.333333f),
                   new Vector2(0.991876f, 0.500000f),
                   new Vector2(0.989583f, 0.750000f),
                   new Vector2(0.990153f, 0.416667f),
                   new Vector2(0.959808f, 0.000000f),
                   new Vector2(1.000000f, 0.000000f),
                   new Vector2(0.989583f, 0.083333f),
                   new Vector2(0.023051f, 0.666667f),
                   new Vector2(0.009067f, 0.916667f),
                   new Vector2(0.000000f, 0.250000f),
                   new Vector2(0.000000f, 0.416667f),
                   new Vector2(0.011502f, 0.500000f),
                   new Vector2(0.029587f, 1.000000f),
                   new Vector2(0.145962f, 0.833333f),
                   new Vector2(0.014248f, 0.750000f),
                   new Vector2(0.029587f, 0.000000f),
                   new Vector2(0.093750f, 0.500000f),
                   new Vector2(0.092691f, 0.416667f),
                   new Vector2(0.113245f, 1.000000f),
                   new Vector2(0.145863f, 0.500000f),
                   new Vector2(0.113245f, 0.000000f),
                   new Vector2(0.123935f, 0.583333f),
                   new Vector2(0.093746f, 0.666667f),
                   new Vector2(0.114583f, 0.916667f),
                   new Vector2(0.163546f, 0.250000f),
                   new Vector2(0.185099f, 0.333333f),
                   new Vector2(0.154144f, 0.416667f),
                   new Vector2(0.156092f, 0.666667f),
                   new Vector2(0.178016f, 0.083333f),
                   new Vector2(0.227637f, 0.416667f),
                   new Vector2(0.169736f, 0.166667f),
                   new Vector2(0.187536f, 0.583333f),
                   new Vector2(0.184894f, 0.916667f),
                   new Vector2(0.196253f, 0.000000f),
                   new Vector2(0.197917f, 0.500000f),
                   new Vector2(0.210552f, 0.666667f),
                   new Vector2(0.210289f, 0.750000f),
                   new Vector2(0.250034f, 0.916667f),
                   new Vector2(0.240352f, 0.500000f),
                   new Vector2(0.240010f, 0.833333f),
                   new Vector2(0.282336f, 0.500000f),
                   new Vector2(0.196253f, 1.000000f),
                   new Vector2(0.260427f, 0.250000f),
                   new Vector2(0.406252f, 0.833333f),
                   new Vector2(0.258564f, 1.000000f),
                   new Vector2(0.356970f, 0.333333f),
                   new Vector2(0.258564f, 0.000000f),
                   new Vector2(0.279900f, 0.416667f),
                   new Vector2(0.342565f, 0.583333f),
                   new Vector2(0.323686f, 0.500000f),
                   new Vector2(0.331693f, 0.750000f),
                   new Vector2(0.322927f, 0.833333f),
                   new Vector2(0.321578f, 1.000000f),
                   new Vector2(0.395863f, 0.000000f),
                   new Vector2(0.321578f, 0.000000f),
                   new Vector2(0.262538f, 0.583333f),
                   new Vector2(0.284236f, 0.666667f),
                   new Vector2(0.273594f, 0.166667f),
                   new Vector2(0.330729f, 0.416667f),
                   new Vector2(0.364159f, 0.166667f),
                   new Vector2(0.437520f, 0.666667f),
                   new Vector2(0.366149f, 0.250000f),
                   new Vector2(0.425277f, 0.583333f),
                   new Vector2(0.384357f, 0.916667f),
                   new Vector2(0.322917f, 0.916667f),
                   new Vector2(0.396674f, 0.083333f),
                   new Vector2(0.383564f, 0.416667f),
                   new Vector2(0.366436f, 0.500000f),
                   new Vector2(0.448686f, 0.500000f),
                   new Vector2(0.367290f, 0.666667f),
                   new Vector2(0.552083f, 0.000000f),
                   new Vector2(0.446984f, 0.916667f),
                   new Vector2(0.395863f, 1.000000f),
                   new Vector2(0.435970f, 0.416667f),
                   new Vector2(0.498893f, 1.000000f),
                   new Vector2(0.407336f, 0.500000f),
                   new Vector2(0.446831f, 0.000000f),
                   new Vector2(0.457269f, 0.083333f),
                   new Vector2(0.450913f, 0.333333f),
                };

                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                return mesh;
            }

            public static Mesh GenerateBeamMesh()
            {
                var mesh = new Mesh
                {
                    name = "Massdriver Projectile Beam Mesh"
                };
                mesh.vertices = new Vector3[] {
                   new Vector3(1.004956f,0.000000f,-0.200000f),
                   new Vector3(3.004956f,-0.000000f,-0.200000f),
                   new Vector3(1.004956f,-0.039018f,-0.196157f),
                   new Vector3(3.004956f,-0.039018f,-0.196157f),
                   new Vector3(1.004956f,-0.076537f,-0.184776f),
                   new Vector3(3.004956f,-0.076537f,-0.184776f),
                   new Vector3(1.004956f,-0.111114f,-0.166294f),
                   new Vector3(3.004956f,-0.111114f,-0.166294f),
                   new Vector3(1.004956f,-0.141421f,-0.141421f),
                   new Vector3(3.004956f,-0.141421f,-0.141421f),
                   new Vector3(1.004956f,-0.166294f,-0.111114f),
                   new Vector3(3.004956f,-0.166294f,-0.111114f),
                   new Vector3(1.004956f,-0.184776f,-0.076537f),
                   new Vector3(3.004956f,-0.184776f,-0.076537f),
                   new Vector3(1.004956f,-0.196157f,-0.039018f),
                   new Vector3(3.004956f,-0.196157f,-0.039018f),
                   new Vector3(1.004956f,-0.200000f,0.000000f),
                   new Vector3(3.004956f,-0.200000f,0.000000f),
                   new Vector3(1.004956f,-0.196157f,0.039018f),
                   new Vector3(3.004956f,-0.196157f,0.039018f),
                   new Vector3(1.004956f,-0.184776f,0.076537f),
                   new Vector3(3.004956f,-0.184776f,0.076537f),
                   new Vector3(1.004956f,-0.166294f,0.111114f),
                   new Vector3(3.004956f,-0.166294f,0.111114f),
                   new Vector3(1.004956f,-0.141421f,0.141421f),
                   new Vector3(3.004956f,-0.141421f,0.141421f),
                   new Vector3(1.004956f,-0.111114f,0.166294f),
                   new Vector3(3.004956f,-0.111114f,0.166294f),
                   new Vector3(1.004956f,-0.076537f,0.184776f),
                   new Vector3(3.004956f,-0.076537f,0.184776f),
                   new Vector3(1.004956f,-0.039018f,0.196157f),
                   new Vector3(3.004956f,-0.039018f,0.196157f),
                   new Vector3(1.004956f,0.000000f,0.200000f),
                   new Vector3(3.004956f,-0.000000f,0.200000f),
                   new Vector3(1.004956f,0.039018f,0.196157f),
                   new Vector3(3.004956f,0.039018f,0.196157f),
                   new Vector3(1.004956f,0.076537f,0.184776f),
                   new Vector3(3.004956f,0.076537f,0.184776f),
                   new Vector3(1.004956f,0.111114f,0.166294f),
                   new Vector3(3.004956f,0.111114f,0.166294f),
                   new Vector3(1.004956f,0.141421f,0.141421f),
                   new Vector3(3.004956f,0.141421f,0.141421f),
                   new Vector3(1.004956f,0.166294f,0.111114f),
                   new Vector3(3.004956f,0.166294f,0.111114f),
                   new Vector3(1.004956f,0.184776f,0.076537f),
                   new Vector3(3.004956f,0.184776f,0.076537f),
                   new Vector3(1.004956f,0.196157f,0.039018f),
                   new Vector3(3.004956f,0.196157f,0.039018f),
                   new Vector3(1.004956f,0.200000f,0.000000f),
                   new Vector3(3.004956f,0.200000f,0.000000f),
                   new Vector3(1.004956f,0.196157f,-0.039018f),
                   new Vector3(3.004956f,0.196157f,-0.039018f),
                   new Vector3(1.004956f,0.184776f,-0.076537f),
                   new Vector3(3.004956f,0.184776f,-0.076537f),
                   new Vector3(1.004956f,0.166294f,-0.111114f),
                   new Vector3(3.004956f,0.166294f,-0.111114f),
                   new Vector3(1.004956f,0.141421f,-0.141421f),
                   new Vector3(3.004956f,0.141421f,-0.141421f),
                   new Vector3(1.004956f,0.111114f,-0.166294f),
                   new Vector3(3.004956f,0.111114f,-0.166294f),
                   new Vector3(1.004956f,0.076537f,-0.184776f),
                   new Vector3(3.004956f,0.076537f,-0.184776f),
                   new Vector3(1.004956f,0.039018f,-0.196157f),
                   new Vector3(3.004956f,0.039018f,-0.196157f),
                };

                mesh.triangles = new int[] {
                    1,2,0,3,4,2,5,6,4,7,8,6,9,10,8,11,12,10,13,14,12,15,16,14,17,18,16,19,20,18,21,22,20,23,24,22,25,26,24,27,28,26,29,30,28,31,32,30,33,34,32,35,36,34,37,38,36,39,40,38,41,42,40,43,44,42,45,46,44,47,48,46,49,50,48,51,52,50,53,54,52,55,56,54,57,58,56,59,60,58,37,21,5,61,62,60,63,0,62,30,46,62,1,3,2,3,5,4,5,7,6,7,9,8,9,11,10,11,13,12,13,15,14,15,17,16,17,19,18,19,21,20,21,23,22,23,25,24,25,27,26,27,29,28,29,31,30,31,33,32,33,35,34,35,37,36,37,39,38,39,41,40,41,43,42,43,45,44,45,47,46,47,49,48,49,51,50,51,53,52,53,55,54,55,57,56,57,59,58,59,61,60,5,3,1,1,63,61,61,59,57,57,55,53,53,51,49,49,47,45,45,43,41,41,39,37,37,35,33,33,31,29,29,27,25,25,23,21,21,19,17,17,15,13,13,11,9,9,7,5,5,1,61,61,57,53,53,49,45,45,41,37,37,33,29,29,25,21,21,17,13,13,9,5,5,61,53,53,45,37,37,29,21,21,13,5,5,53,37,61,63,62,63,1,0,62,0,2,2,4,6,6,8,10,10,12,14,14,16,18,18,20,22,22,24,26,26,28,30,30,32,34,34,36,38,38,40,42,42,44,46,46,48,50,50,52,54,54,56,58,58,60,62,62,2,6,6,10,14,14,18,22,22,26,30,30,34,38,38,42,46,46,50,54,54,58,62,62,6,14,14,22,30,30,38,46,46,54,62,62,14,30,
                };

                mesh.uv = new Vector2[] {
                   new Vector2(0.968750f, 1.000000f),
                   new Vector2(0.968750f, 0.500000f),
                   new Vector2(1.000000f, 0.500000f),
                   new Vector2(0.937500f, 0.500000f),
                   new Vector2(0.937500f, 1.000000f),
                   new Vector2(0.906250f, 0.500000f),
                   new Vector2(0.906250f, 1.000000f),
                   new Vector2(0.875000f, 0.500000f),
                   new Vector2(0.875000f, 1.000000f),
                   new Vector2(0.843750f, 0.500000f),
                   new Vector2(0.843750f, 1.000000f),
                   new Vector2(0.812500f, 0.500000f),
                   new Vector2(0.812500f, 1.000000f),
                   new Vector2(0.781250f, 0.500000f),
                   new Vector2(0.781250f, 1.000000f),
                   new Vector2(0.750000f, 0.500000f),
                   new Vector2(0.750000f, 1.000000f),
                   new Vector2(0.718750f, 0.500000f),
                   new Vector2(0.718750f, 1.000000f),
                   new Vector2(0.687500f, 0.500000f),
                   new Vector2(0.687500f, 1.000000f),
                   new Vector2(0.656250f, 0.500000f),
                   new Vector2(0.656250f, 1.000000f),
                   new Vector2(0.625000f, 0.500000f),
                   new Vector2(0.625000f, 1.000000f),
                   new Vector2(0.593750f, 0.500000f),
                   new Vector2(0.593750f, 1.000000f),
                   new Vector2(0.562500f, 0.500000f),
                   new Vector2(0.562500f, 1.000000f),
                   new Vector2(0.531250f, 0.500000f),
                   new Vector2(0.531250f, 1.000000f),
                   new Vector2(0.500000f, 0.500000f),
                   new Vector2(0.500000f, 1.000000f),
                   new Vector2(0.468750f, 0.500000f),
                   new Vector2(0.468750f, 1.000000f),
                   new Vector2(0.437500f, 0.500000f),
                   new Vector2(0.437500f, 1.000000f),
                   new Vector2(0.406250f, 0.500000f),
                   new Vector2(0.406250f, 1.000000f),
                   new Vector2(0.375000f, 0.500000f),
                   new Vector2(0.375000f, 1.000000f),
                   new Vector2(0.343750f, 0.500000f),
                   new Vector2(0.343750f, 1.000000f),
                   new Vector2(0.312500f, 0.500000f),
                   new Vector2(0.312500f, 1.000000f),
                   new Vector2(0.281250f, 0.500000f),
                   new Vector2(0.281250f, 1.000000f),
                   new Vector2(0.250000f, 0.500000f),
                   new Vector2(0.250000f, 1.000000f),
                   new Vector2(0.218750f, 0.500000f),
                   new Vector2(0.218750f, 1.000000f),
                   new Vector2(0.187500f, 0.500000f),
                   new Vector2(0.187500f, 1.000000f),
                   new Vector2(0.156250f, 0.500000f),
                   new Vector2(0.156250f, 1.000000f),
                   new Vector2(0.125000f, 0.500000f),
                   new Vector2(0.125000f, 1.000000f),
                   new Vector2(0.093750f, 0.500000f),
                   new Vector2(0.093750f, 1.000000f),
                   new Vector2(0.062500f, 0.500000f),
                   new Vector2(0.158156f, 0.028269f),
                   new Vector2(0.031250f, 0.500000f),
                   new Vector2(0.031250f, 1.000000f),
                   new Vector2(0.000000f, 0.500000f),
                };

                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                return mesh;
            }

            public static Mesh GenerateHaloMesh()
            {
                var mesh = new Mesh
                {
                    name = "Massdriver Projectile Halo Mesh"
                };
                mesh.vertices = new Vector3[] {
                   new Vector3(1.004956f,0.000000f,-0.200000f),
                   new Vector3(3.004956f,-0.000000f,-0.200000f),
                   new Vector3(1.004956f,-0.039018f,-0.196157f),
                   new Vector3(3.004956f,-0.039018f,-0.196157f),
                   new Vector3(1.004956f,-0.076537f,-0.184776f),
                   new Vector3(3.004956f,-0.076537f,-0.184776f),
                   new Vector3(1.004956f,-0.111114f,-0.166294f),
                   new Vector3(3.004956f,-0.111114f,-0.166294f),
                   new Vector3(1.004956f,-0.141421f,-0.141421f),
                   new Vector3(3.004956f,-0.141421f,-0.141421f),
                   new Vector3(1.004956f,-0.166294f,-0.111114f),
                   new Vector3(3.004956f,-0.166294f,-0.111114f),
                   new Vector3(1.004956f,-0.184776f,-0.076537f),
                   new Vector3(3.004956f,-0.184776f,-0.076537f),
                   new Vector3(1.004956f,-0.196157f,-0.039018f),
                   new Vector3(3.004956f,-0.196157f,-0.039018f),
                   new Vector3(1.004956f,-0.200000f,0.000000f),
                   new Vector3(3.004956f,-0.200000f,0.000000f),
                   new Vector3(1.004956f,-0.196157f,0.039018f),
                   new Vector3(3.004956f,-0.196157f,0.039018f),
                   new Vector3(1.004956f,-0.184776f,0.076537f),
                   new Vector3(3.004956f,-0.184776f,0.076537f),
                   new Vector3(1.004956f,-0.166294f,0.111114f),
                   new Vector3(3.004956f,-0.166294f,0.111114f),
                   new Vector3(1.004956f,-0.141421f,0.141421f),
                   new Vector3(3.004956f,-0.141421f,0.141421f),
                   new Vector3(1.004956f,-0.111114f,0.166294f),
                   new Vector3(3.004956f,-0.111114f,0.166294f),
                   new Vector3(1.004956f,-0.076537f,0.184776f),
                   new Vector3(3.004956f,-0.076537f,0.184776f),
                   new Vector3(1.004956f,-0.039018f,0.196157f),
                   new Vector3(3.004956f,-0.039018f,0.196157f),
                   new Vector3(1.004956f,0.000000f,0.200000f),
                   new Vector3(3.004956f,-0.000000f,0.200000f),
                   new Vector3(1.004956f,0.039018f,0.196157f),
                   new Vector3(3.004956f,0.039018f,0.196157f),
                   new Vector3(1.004956f,0.076537f,0.184776f),
                   new Vector3(3.004956f,0.076537f,0.184776f),
                   new Vector3(1.004956f,0.111114f,0.166294f),
                   new Vector3(3.004956f,0.111114f,0.166294f),
                   new Vector3(1.004956f,0.141421f,0.141421f),
                   new Vector3(3.004956f,0.141421f,0.141421f),
                   new Vector3(1.004956f,0.166294f,0.111114f),
                   new Vector3(3.004956f,0.166294f,0.111114f),
                   new Vector3(1.004956f,0.184776f,0.076537f),
                   new Vector3(3.004956f,0.184776f,0.076537f),
                   new Vector3(1.004956f,0.196157f,0.039018f),
                   new Vector3(3.004956f,0.196157f,0.039018f),
                   new Vector3(1.004956f,0.200000f,0.000000f),
                   new Vector3(3.004956f,0.200000f,0.000000f),
                   new Vector3(1.004956f,0.196157f,-0.039018f),
                   new Vector3(3.004956f,0.196157f,-0.039018f),
                   new Vector3(1.004956f,0.184776f,-0.076537f),
                   new Vector3(3.004956f,0.184776f,-0.076537f),
                   new Vector3(1.004956f,0.166294f,-0.111114f),
                   new Vector3(3.004956f,0.166294f,-0.111114f),
                   new Vector3(1.004956f,0.141421f,-0.141421f),
                   new Vector3(3.004956f,0.141421f,-0.141421f),
                   new Vector3(1.004956f,0.111114f,-0.166294f),
                   new Vector3(3.004956f,0.111114f,-0.166294f),
                   new Vector3(1.004956f,0.076537f,-0.184776f),
                   new Vector3(3.004956f,0.076537f,-0.184776f),
                   new Vector3(1.004956f,0.039018f,-0.196157f),
                   new Vector3(3.004956f,0.039018f,-0.196157f),
                };

                mesh.triangles = new int[] {
                    1,2,0,3,4,2,5,6,4,7,8,6,9,10,8,11,12,10,13,14,12,15,16,14,17,18,16,19,20,18,21,22,20,23,24,22,25,26,24,27,28,26,29,30,28,31,32,30,33,34,32,35,36,34,37,38,36,39,40,38,41,42,40,43,44,42,45,46,44,47,48,46,49,50,48,51,52,50,53,54,52,55,56,54,57,58,56,59,60,58,37,21,5,61,62,60,63,0,62,30,46,62,1,3,2,3,5,4,5,7,6,7,9,8,9,11,10,11,13,12,13,15,14,15,17,16,17,19,18,19,21,20,21,23,22,23,25,24,25,27,26,27,29,28,29,31,30,31,33,32,33,35,34,35,37,36,37,39,38,39,41,40,41,43,42,43,45,44,45,47,46,47,49,48,49,51,50,51,53,52,53,55,54,55,57,56,57,59,58,59,61,60,5,3,1,1,63,61,61,59,57,57,55,53,53,51,49,49,47,45,45,43,41,41,39,37,37,35,33,33,31,29,29,27,25,25,23,21,21,19,17,17,15,13,13,11,9,9,7,5,5,1,61,61,57,53,53,49,45,45,41,37,37,33,29,29,25,21,21,17,13,13,9,5,5,61,53,53,45,37,37,29,21,21,13,5,5,53,37,61,63,62,63,1,0,62,0,2,2,4,6,6,8,10,10,12,14,14,16,18,18,20,22,22,24,26,26,28,30,30,32,34,34,36,38,38,40,42,42,44,46,46,48,50,50,52,54,54,56,58,58,60,62,62,2,6,6,10,14,14,18,22,22,26,30,30,34,38,38,42,46,46,50,54,54,58,62,62,6,14,14,22,30,30,38,46,46,54,62,62,14,30,
                };

                mesh.uv = new Vector2[] {
                   new Vector2(0.968750f, 1.000000f),
                   new Vector2(0.968750f, 0.500000f),
                   new Vector2(1.000000f, 0.500000f),
                   new Vector2(0.937500f, 0.500000f),
                   new Vector2(0.937500f, 1.000000f),
                   new Vector2(0.906250f, 0.500000f),
                   new Vector2(0.906250f, 1.000000f),
                   new Vector2(0.875000f, 0.500000f),
                   new Vector2(0.875000f, 1.000000f),
                   new Vector2(0.843750f, 0.500000f),
                   new Vector2(0.843750f, 1.000000f),
                   new Vector2(0.812500f, 0.500000f),
                   new Vector2(0.812500f, 1.000000f),
                   new Vector2(0.781250f, 0.500000f),
                   new Vector2(0.781250f, 1.000000f),
                   new Vector2(0.750000f, 0.500000f),
                   new Vector2(0.750000f, 1.000000f),
                   new Vector2(0.718750f, 0.500000f),
                   new Vector2(0.718750f, 1.000000f),
                   new Vector2(0.687500f, 0.500000f),
                   new Vector2(0.687500f, 1.000000f),
                   new Vector2(0.656250f, 0.500000f),
                   new Vector2(0.656250f, 1.000000f),
                   new Vector2(0.625000f, 0.500000f),
                   new Vector2(0.625000f, 1.000000f),
                   new Vector2(0.593750f, 0.500000f),
                   new Vector2(0.593750f, 1.000000f),
                   new Vector2(0.562500f, 0.500000f),
                   new Vector2(0.562500f, 1.000000f),
                   new Vector2(0.531250f, 0.500000f),
                   new Vector2(0.531250f, 1.000000f),
                   new Vector2(0.500000f, 0.500000f),
                   new Vector2(0.500000f, 1.000000f),
                   new Vector2(0.468750f, 0.500000f),
                   new Vector2(0.468750f, 1.000000f),
                   new Vector2(0.437500f, 0.500000f),
                   new Vector2(0.437500f, 1.000000f),
                   new Vector2(0.406250f, 0.500000f),
                   new Vector2(0.406250f, 1.000000f),
                   new Vector2(0.375000f, 0.500000f),
                   new Vector2(0.375000f, 1.000000f),
                   new Vector2(0.343750f, 0.500000f),
                   new Vector2(0.343750f, 1.000000f),
                   new Vector2(0.312500f, 0.500000f),
                   new Vector2(0.312500f, 1.000000f),
                   new Vector2(0.281250f, 0.500000f),
                   new Vector2(0.281250f, 1.000000f),
                   new Vector2(0.250000f, 0.500000f),
                   new Vector2(0.250000f, 1.000000f),
                   new Vector2(0.218750f, 0.500000f),
                   new Vector2(0.218750f, 1.000000f),
                   new Vector2(0.187500f, 0.500000f),
                   new Vector2(0.187500f, 1.000000f),
                   new Vector2(0.156250f, 0.500000f),
                   new Vector2(0.156250f, 1.000000f),
                   new Vector2(0.125000f, 0.500000f),
                   new Vector2(0.125000f, 1.000000f),
                   new Vector2(0.093750f, 0.500000f),
                   new Vector2(0.093750f, 1.000000f),
                   new Vector2(0.062500f, 0.500000f),
                   new Vector2(0.158156f, 0.028269f),
                   new Vector2(0.031250f, 0.500000f),
                   new Vector2(0.031250f, 1.000000f),
                   new Vector2(0.000000f, 0.500000f),
                };

                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                return mesh;
            }

            static Mesh mesh_beam;
            static Mesh mesh_halo;
            static Mesh mesh_torus;


            public class MassdriverProjectile
            {
                public bool active;
                float time;

                Vector3 start_position;
                Vector3 end_position;
                Vector3 position_delta;
                Quaternion rotation;

                Material mat_beam;
                Material mat_halo;
                Material mat_torus;

                Matrix4x4 translation_matrix_beam;
                Vector3 scaling_vector_beam;

                Matrix4x4 translation_matrix_halo;
                Vector3 scaling_vector_halo;

                Matrix4x4[] translation_matrix_torus;
                Vector3[] scaling_vectors_torus;
                int number_of_tori = 8;

                public void Fire(Vector3 pos, Quaternion rot)
                {
                    this.start_position = pos;
                    CalculateAndSetEndPosition();
                    this.position_delta = end_position - start_position;
                    this.rotation = rot;

                    this.translation_matrix_beam = new Matrix4x4();
                    this.translation_matrix_halo = new Matrix4x4();
                    this.translation_matrix_torus = new Matrix4x4[number_of_tori];
                    this.scaling_vectors_torus = new Vector3[number_of_tori];

                    this.time = 0;

                    if (mat_beam == null | mat_halo == null | mat_torus == null)
                    {
                        mat_beam = GenerateBeamMaterial();
                        mat_halo = GenerateHaloMaterial();
                        mat_torus = GenerateTorusMaterial();
                    }




                    Draw();
                }

                public void CalculateAndSetEndPosition()
                {
                    // Do a raycast in the scene to figure out at what position this shot should end
                    const int layerMask = (1 << (int)UnityObjectLayers.LEVEL) | (1 << (int)UnityObjectLayers.LAVA | (1 << (int)UnityObjectLayers.PLAYER_MESH));
                    RaycastHit hitInfo;
                    Vector3 direction = rotation.eulerAngles;

                    if (Physics.Raycast(start_position, direction, out hitInfo, dist, layerMask, QueryTriggerInteraction.Ignore))
                    {

                    }
                    else
                    {

                    }



                    // set number_of_tori by dividing the distance of the hit with the desired space per torus

                        // If this shot hit a player then send a packet to the server to kill that player unless he is invulnerable
                }

                public void Update()
                {
                    // update the time

                    // update the translation matrizes of the shot 

                    // update the transparency of the materials with an reverse exponential curve
                }

                private void Draw()
                {
                    // Todo: use MaterialPropertyBlock to make sure that you can adjust the color of materials after its instantiation
                    // Todo: check if layer 0 is the correct layer


                    // Draw Beam
                    // Todo: scale the beam with the length of the position delta
                    scaling_vector_beam.x = 1f;
                    scaling_vector_beam.y = 1f;
                    scaling_vector_beam.z = 1f;
                    translation_matrix_beam.SetTRS(start_position + (0.5f * position_delta), rotation, scaling_vector_beam);
                    Graphics.DrawMesh(mesh_beam, translation_matrix_beam, mat_beam, 0, GameManager.m_viewer.c_camera);

                    // Draw Halo
                    // Todo: scale the halo with the length of the position delta
                    scaling_vector_halo.x = 1f;
                    scaling_vector_halo.y = 1f;
                    scaling_vector_halo.z = 1f;
                    translation_matrix_beam.SetTRS(start_position + (0.5f * position_delta), rotation, scaling_vector_halo);
                    Graphics.DrawMesh(mesh_halo, translation_matrix_halo, mat_halo, 0, GameManager.m_viewer.c_camera);

                    // Draw Tori
                    for (int i = 0; i < number_of_tori; i++)
                    {
                        // determine scaling with a sinus curve, Mathf.Sin(t + (i / 3f)), i * 0.3f)
                        scaling_vectors_torus[i].x = 1f;
                        scaling_vectors_torus[i].y = 1f;
                        scaling_vectors_torus[i].z = 1f;
                        translation_matrix_torus[i].SetTRS(start_position + (0.1f * position_delta), rotation, scaling_vectors_torus[i]);
                        Graphics.DrawMesh(mesh_torus, translation_matrix_torus[i], mat_torus, 0, GameManager.m_viewer.c_camera);
                    }

                }

                public Material GenerateTorusMaterial()
                {
                    Material material = new Material(Shader.Find("Standard"));//"Transparent/Diffuse"));
                    material.color = new Color(0.8f, 0.8f, 0.8f, 0.8f);
                    material.SetFloat("_Mode", 3);
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = 3000;
                    // Set the emissive color
                    //material.EnableKeyword("_EMISSION"); // Enable emission on the material shader
                    //material.SetColor("_EmissionColor", Color.white * 0.7f);

                    return material;
                }

                public Material GenerateBeamMaterial()
                {
                    Material material = new Material(Shader.Find("Standard"));//"Transparent/Diffuse"));
                    material.color = new Color(1f, 1f, 1f, 0.6f);
                    material.SetFloat("_Mode", 3);
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = 3000;
                    // Set the emissive color
                    //material.EnableKeyword("_EMISSION"); // Enable emission on the material shader
                    //material.SetColor("_EmissionColor", Color.white * 0.7f);

                    return material;
                }

                public Material GenerateHaloMaterial()
                {
                    Material material = new Material(Shader.Find("Standard"));
                    material.color = new Color(0.5f, 0f, 0.5f, 0.3f);
                    material.SetFloat("_Mode", 3);
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = 3000;
                    return material;
                }
            }




























        }
    }
}
