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
        static bool telemetry_enabled = true;   // todo: pull this from a commandline argument

        static float event_boosting = 0f;       // bool
        static float event_primary_fire = 0f;   // bool
        static float event_secondary_fire = 0f; // bool
        static float event_picked_up_item = 0f; // bool
        static float event_damage_taken = 0f;   // amount of damage taken since last fixed update

        [HarmonyPatch(typeof(PlayerShip), "FixedUpdateProcessControlsInternal")]
        class TelemetryMod_PlayerShip_FixedUpdateProcessControlsInternal
        {
            static void Prefix()
            {
                if(telemetry_enabled)
                {
                    event_primary_fire = GameManager.m_local_player.IsPressed(CCInput.FIRE_WEAPON) ? 1f : 0f;
                    event_secondary_fire = GameManager.m_local_player.IsPressed(CCInput.FIRE_MISSILE) ? 1f : 0f;
                }
            }
        }

        [HarmonyPatch(typeof(Item), "PlayItemPickupFX")]
        class TelemetryMod_Item_PlayItemPickupFX
        {
            static void Postfix(Player player)
            {
                if (telemetry_enabled)
                {
                    if(player != null && player.isLocalPlayer)
                    {
                        event_secondary_fire = 1f;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(PlayerShip), "ApplyDamage")]
        class TelemetryMod_PlayerShip_ApplyDamage
        {
            static void Postfix(DamageInfo di, PlayerShip __instance)
            {
                if (telemetry_enabled)
                {
                    if( __instance != null && __instance.isLocalPlayer)
                    {
                        event_damage_taken += di.damage;
                    }
                }
            }
        }


        static Telemetry telemetryComponent;
        static bool initialized = false;
        static GameObject udpSenderObject;
        static Vector3 previousVelocity = Vector3.zero;
        [HarmonyPatch(typeof(GameManager), "FixedUpdate")]
        class TelemetryMod_GameManager_FixedUpdate
        {
            static void Postfix()
            {
                if (!initialized & GameManager.m_local_player != null)
                {
                    initialized = true;
                    udpSenderObject = new GameObject("UdpTelemetrySender");
                    telemetryComponent = udpSenderObject.AddComponent<Telemetry>();
                    telemetryComponent.IP = "127.0.0.1";
                    telemetryComponent.port = 4123;
                }
                else if (initialized)
                {
                    event_boosting = GameManager.m_local_player.c_player_ship.m_boosting ? 1f : 0f;

                    if (GameplayManager.m_gameplay_state == GameplayState.PLAYING)
                    {

                        Rigidbody rigidbody = GameManager.m_local_player.c_player_ship.c_rigidbody;
                        Vector3 euler = rigidbody.rotation.eulerAngles;
                        Vector3 angularVelocity = rigidbody.angularVelocity;
                        Vector3 gforce = ((rigidbody.velocity - previousVelocity) / Time.fixedDeltaTime) / 9.81f;
                        previousVelocity = rigidbody.velocity;
                        Telemetry.Telemetry_SendTelemetry(
                            euler.z > 180 ? euler.z - 360 : euler.z, // Roll, Pitch, and Yaw angles in degrees (-180 to 180)
                            euler.x > 180 ? euler.x - 360 : euler.x,
                            euler.y > 180 ? euler.y - 360 : euler.y,
                            angularVelocity.z, // in (rad/sec)
                            angularVelocity.x,
                            angularVelocity.y,
                            gforce.x,
                            gforce.y,
                            gforce.z,
                            event_boosting,
                            event_primary_fire,
                            event_secondary_fire,
                            event_picked_up_item,
                            event_damage_taken
                            );


                    }
                    else
                    {
                        Telemetry.Telemetry_SendTelemetry(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
                    }

                    // reset events after using them if necessary
                    event_boosting = 0f;
                    event_primary_fire = 0f;
                    event_secondary_fire = 0f;
                    event_picked_up_item = 0f;
                    event_damage_taken = 0f;
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
            public float EventBoosting;
            public float EventPrimaryFire;
            public float EventSecondaryFire;
            public float EventItemPickup;
            public float EventDamageTaken;

            public PlayerData() { }
            public PlayerData(float Roll, float Pitch, float Yaw, float Heave, float Sway, float Surge, float Extra1, float Extra2, float Extra3, float Boosting, float PrimaryFire, float SecondaryFire, float ItemPickup, float DamageTaken)
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
                this.EventBoosting = Boosting;
                this.EventPrimaryFire = PrimaryFire;
                this.EventSecondaryFire = SecondaryFire;
                this.EventItemPickup = ItemPickup;
                this.EventDamageTaken = DamageTaken;
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
                DontDestroyOnLoad(this.gameObject);
                remoteEndPoint = new IPEndPoint(IPAddress.Parse(IP), port);
                client = new UdpClient();
                local_player_data = new PlayerData();
                StartCoroutine("Telemetry_Start");
            }

            public static void Telemetry_SendTelemetry(float Roll, float Pitch, float Yaw, float Heave, float Sway, float Surge, float Extra1, float Extra2, float Extra3, float Boosting, float PrimaryFire, float SecondaryFire, float ItemPickup, float DamageTaken)
            {
                local_player_data = new PlayerData(Roll, Pitch, Yaw, Heave, Sway, Surge, Extra1, Extra2, Extra3, Boosting, PrimaryFire, SecondaryFire, ItemPickup, DamageTaken);
            }

            IEnumerator Telemetry_Start()
            {
                while (true)
                {
                    string info = String.Format("{0};{1};{2};{3};{4};{5};{6};{7};{8};{9};{10};{11};{12};{13}", 
                        local_player_data.Roll, local_player_data.Pitch, local_player_data.Yaw, local_player_data.Heave, local_player_data.Sway, 
                        local_player_data.Surge, local_player_data.Extra1, local_player_data.Extra2, local_player_data.Extra3,
                        local_player_data.EventBoosting, local_player_data.EventPrimaryFire, local_player_data.EventSecondaryFire,
                        local_player_data.EventItemPickup, local_player_data.EventDamageTaken
                        );
                    byte[] data = Encoding.Default.GetBytes(info);
                    //uConsole.Log("Send: "+info);
                    client.Send(data, data.Length, remoteEndPoint);
                    yield return null;
                }
            }
        }
    }
}