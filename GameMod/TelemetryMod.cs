using UnityEngine;
using System.Collections;
using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using HarmonyLib;
using Overload;

namespace GameMod
{
    internal class TelemetryMod
    {
        static Telemetry telemetryComponent;
        static bool initialized = false;
        //static Vector3 previousVelocity = Vector3.zero;
        [HarmonyPatch(typeof(GameManager), "FixedUpdate")]
        class TelemetryMod_GameManager_FixedUpdate
        {
            static void Postfix()
            {
                if (!initialized & GameManager.m_local_player != null)
                {
                    initialized = true;
                    telemetryComponent = GameManager.m_local_player.gameObject.AddComponent<Telemetry>();
                    telemetryComponent.IP = "127.0.0.1";
                    telemetryComponent.port = 4123;
                }
                else if (initialized)
                {
                    if (GameplayManager.m_gameplay_state == GameplayState.PLAYING)
                    {

                        Rigidbody rigidbody = GameManager.m_local_player.c_player_ship.c_rigidbody;
                        Vector3 euler = rigidbody.rotation.eulerAngles;
                        Vector3 angularVelocity = rigidbody.angularVelocity;
                        //Vector3 gforce = ((rigidbody.velocity - previousVelocity) / Time.fixedDeltaTime) / 9.81f;
                        Telemetry.Telemetry_SendTelemetry(
                            euler.z > 180 ? euler.z - 360 : euler.z, // Roll, Pitch, and Yaw angles in degrees (-180 to 180)
                            euler.x > 180 ? euler.x - 360 : euler.x,
                            euler.y > 180 ? euler.y - 360 : euler.y,
                            angularVelocity.z, // in (rad/sec)
                            angularVelocity.x,
                            angularVelocity.y,
                            0,
                            0,
                            0
                            //gforce.x,
                            //gforce.y,
                            //gforce.z
                            );


                    }
                    else
                    {
                        Telemetry.Telemetry_SendTelemetry(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
                    }


                }
            }
        }

        class PlayerData
        {
            public float Roll;
            public float Pitch;
            public float Yaw;
            public float Heave;
            public float Sway;
            public float Surge;
            public float Extra1;
            public float Extra2;
            public float Extra3;
            public PlayerData() { }
            public PlayerData(float Roll, float Pitch, float Yaw, float Heave, float Sway, float Surge, float Extra1, float Extra2, float Extra3)
            {
                this.Roll = Roll;
                this.Pitch = Pitch;
                this.Yaw = Yaw;
                this.Heave = Heave;
                this.Sway = Sway;
                this.Surge = Surge;
                this.Extra1 = Extra1;
                this.Extra2 = Extra2;
                this.Extra3 = Extra3;
            }
        }

        public class Telemetry : MonoBehaviour
        {
            public string IP = "127.0.0.1";
            public int port = 4123;

            IPEndPoint remoteEndPoint;
            static UdpClient client;
            static PlayerData local_player_data;

            void Start()
            {
                remoteEndPoint = new IPEndPoint(IPAddress.Parse(IP), port);
                client = new UdpClient();
                local_player_data = new PlayerData();
                StartCoroutine("Telemetry_Start");
            }

            public static void Telemetry_SendTelemetry(float Roll, float Pitch, float Yaw, float Heave, float Sway, float Surge, float Extra1, float Extra2, float Extra3)
            {
                local_player_data = new PlayerData(Roll, Pitch, Yaw, Heave, Sway, Surge, Extra1, Extra2, Extra3);
               /* uConsole.Log(
                      Roll.ToString() + ", "
                    + Pitch.ToString() + ", " 
                    + Yaw.ToString() + ", " 
                    + Heave.ToString() + ", " 
                    + Sway.ToString() + ", " 
                    + Surge.ToString() + ", " 
                    + Extra1.ToString() + ", " 
                    + Extra2.ToString() + ", " 
                    + Extra3.ToString()
                    );*/
            }

            IEnumerator Telemetry_Start()
            {
                while (true)
                {
                    string info = String.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}", local_player_data.Roll, local_player_data.Pitch, local_player_data.Yaw, local_player_data.Heave, local_player_data.Sway, local_player_data.Surge, local_player_data.Extra1, local_player_data.Extra2, local_player_data.Extra3);
                    byte[] data = Encoding.Default.GetBytes(info);
                    client.Send(data, data.Length, remoteEndPoint);
                    yield return null;
                }
            }
        }
    }
}