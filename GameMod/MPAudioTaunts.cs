using HarmonyLib;
using Overload;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Security.Cryptography;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine.Events;
using System.Linq;

namespace GameMod
{
    class MPAudioTaunts
    {

        // 5. create infrastructure for sharing the selected 6 clips of all players through the server before the match starts and handle join in progress properly
        // 10. dont distribute audiotaunts and playsignals by people that are kicked/banned
        // 12. empty the external taunts dictionary when entering a game
        // 13. Allow playing audiotaunts ingame by the press of a button
        // 14. Server/Client communication
        // 15. Audio visualisation using the Spectrum Data

        //  CLIENT
        public static bool initialized = false;
        public static string LocalAudioTauntDirectory = "";         // path towards the directory where the audiotaunts from the local installation are saved
        public static string ExternalAudioTauntDirectory = "";      // path towards the directory where the audiotaunts of other players get saved
        public static string loaded_local_taunts = "";              // holds the hashes of the local taunts

        public static List<Taunt> taunts = new List<Taunt>();       // a list of all locally loaded audio taunts
        public static Dictionary<string, Taunt> external_taunts = new Dictionary<string, Taunt>();  // contains the audio taunts of the other players during a game
        public static Taunt[] local_taunts = new Taunt[6];          // contains the audio taunts that this pilot has chosen, can not change during a game 
        public static int[] keybinds = new int[6];                  // 
        public static AudioSource[] audioSources = new AudioSource[3];

        public static int selected_audio_slot = 0;
        public const int audio_taunt_size_limit_kB = 131072;        // 128 kB
        public static int audio_taunt_volume = 50;
        public const float default_taunt_cooldown = 4.5f;             // defines the minimum intervall between sending taunts for the client
        public static float remaining_cooldown = 0f;                // tracks the current state of the cooldown. gets reduced with each update. the client is allowed to send another taunt after 'remaining_cooldown' is at or below 0.0
        public static bool server_supports_audiotaunts = false;

        public class Taunt
        {
            public string hash = "";
            public string name = "";
            public AudioClip audioclip;
        }

        public class FileData
        {
            public uint netid;
            public byte[] bytes;
        }

        // INITIALISATION:
        //  - check if the directory exists
        //  - load local audio taunts
        [HarmonyPatch(typeof(GameManager), "Awake")]
        class MPAudioTaunts_GameManager_Awake
        {
            static void Postfix()
            {
                if (String.IsNullOrEmpty(LocalAudioTauntDirectory))
                {
                    LocalAudioTauntDirectory = Path.Combine(Application.streamingAssetsPath, "AudioTaunts");
                    if (!Directory.Exists(LocalAudioTauntDirectory))
                    {
                        Debug.Log("Did not find a directory for local audiotaunts, creating one at: " + LocalAudioTauntDirectory);
                        Directory.CreateDirectory(LocalAudioTauntDirectory);
                    }

                    ExternalAudioTauntDirectory = Path.Combine(LocalAudioTauntDirectory, "external");
                    if (!Directory.Exists(ExternalAudioTauntDirectory))
                    {
                        Debug.Log("Did not find a directory for external audiotaunts, creating one at: " + ExternalAudioTauntDirectory);
                        Directory.CreateDirectory(ExternalAudioTauntDirectory);
                    }
                }

                if (!GameplayManager.IsDedicatedServer())
                {
                    ImportAudioTaunts(LocalAudioTauntDirectory, new List<string>());
                    for (int i = 0; i < 6; i++)
                    {
                        local_taunts[i] = new Taunt
                        {
                            hash = "EMPTY",
                            name = "EMPTY",
                            audioclip = null
                        };
                        keybinds[i] = -1;
                    }
                    LoadLocalAudioTauntsFromPilotPrefs();
                }
                else
                {

                }
                initialized = true;
            }
        }

        // Imports either the in 'files_to_load' specified taunts or all taunts from that directory
        // (under the condition that they have yet to be imported and are valid formats and that their size is not beyond 128 kB) 
        public static void ImportAudioTaunts(string path_to_directory, List<String> files_to_load)
        {
            Debug.Log("Attempting to import AudioTaunts from: " + path_to_directory);
            bool load_all_files = files_to_load == null | files_to_load.Count == 0;
            var fileInfo = new DirectoryInfo(path_to_directory).GetFiles();
            foreach (FileInfo file in fileInfo)
            {
                if ((files_to_load.Contains(file.Name) | load_all_files) && taunts.Find(t => t.name.Equals(file.Name)) == null && (file.Extension.Equals(".ogg") || file.Extension.Equals(".wav")) && file.Length <= audio_taunt_size_limit_kB)
                {

                    Taunt t = new Taunt
                    {
                        hash = CalculateMD5ForFile(Path.Combine(path_to_directory, file.Name)),
                        name = file.Name,
                        audioclip = MPAudioTaunts.LoadAsAudioClip(file.Name, file.Extension, path_to_directory)//Resources.Load<AudioClip>("AudioTaunts/" + file.Name)
                    };

                    if (t.name.StartsWith(t.hash))
                    {
                        t.name = t.name.Remove(0, t.hash.Length + 1);
                    }
                    else
                    {
                        File.Move(Path.Combine(path_to_directory, file.Name), Path.Combine(path_to_directory, t.hash + "-" + file.Name));
                    }

                    taunts.Add(t);
                    Debug.Log("  Added " + file.Name + "  size: " + file.Length + " as an AudioTaunt");

                }
            }
        }

        private static AudioClip LoadAsAudioClip(string filename, string ext, string path2)
        {
            string path = Path.Combine(path2, filename);
            if (path != null)
            {
                WWW www = new WWW("file:///" + path);
                while (!www.isDone) { }
                if (string.IsNullOrEmpty(www.error))
                {
                    return www.GetAudioClip(true, false);
                }
                else Debug.Log("Error in 'LoadAudioClip': " + www.error + " :" + filename + ":" + ext + ":" + path2);
            }
            return null;
        }

        // used to calculate a hash for each audio taunt file to avoid filename collisions
        private static string CalculateMD5ForFile(string filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        // Splits the hashes that get stored as a single string in loaded_local_taunts and finds the corresponding taunts
        // to populate the 6 audio taunt slots. '/' is used as the seperator of the slots
        public static void LoadLocalAudioTauntsFromPilotPrefs()
        {
            string[] file_hashes = loaded_local_taunts.Split('/');
            int index = 0;
            foreach (string hash in file_hashes)
            {
                if (index < 6)
                {
                    Taunt at = taunts.Find(t => t.hash.Equals(hash));
                    if (at == null)
                    {
                        at = new Taunt
                        {
                            hash = "EMPTY",
                            name = "EMPTY",
                            audioclip = null
                        };
                    }
                    local_taunts[index] = at;
                }
                index++;
            }
        }

        public static void PlayAudioTauntFromAudioclip(AudioClip audioClip)
        {
            if (audio_taunt_volume == 0 | audioClip == null)
                return;

            //AudioSource audioSource = new GameObject().AddComponent<AudioSource>();
            int index = -1;
            for (int i = 0; i < audioSources.Length; i++)
            {
                if (audioSources[i] == null)
                {
                    audioSources[i] = new GameObject().AddComponent<AudioSource>();
                    uConsole.Log("Had to instantiate a new AudioSource");
                }
                if (!audioSources[i].isPlaying) index = i;
            }

            if (index == -1)
            {
                uConsole.Log("Couldnt play Audio taunt. All audio sources are occupied!");
                return;
            }
            audioSources[index].clip = audioClip;
            audioSources[index].volume = audio_taunt_volume / 100f;
            audioSources[index].timeSamples = 0;
            audioSources[index].bypassReverbZones = true;
            audioSources[index].reverbZoneMix = 0f;
            audioSources[index].PlayScheduled(AudioSettings.dspTime);
            audioSources[index].SetScheduledEndTime(AudioSettings.dspTime + 4);
        }


        public static float[] calculateFrequencyBand()
        {
            float[] freqBand = new float[8];
            for (int z = 0; z < 3; z++)
            {

                if (MPAudioTaunts.audioSources[z] != null && MPAudioTaunts.audioSources[z].isPlaying)
                {
                    float[] samples = new float[512];

                    MPAudioTaunts.audioSources[z].GetSpectrumData(samples, 0, FFTWindow.Rectangular);

                    int count = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        float average = 0;
                        int sampleCount = (int)Mathf.Pow(2, i) * 2;

                        if (i == 7) sampleCount += 2;

                        for (int j = 0; j < sampleCount; j++)
                        {
                            average += samples[count] * (count + 1);
                            count++;
                        }

                        average /= count;
                        freqBand[i] = average * 10;
                        freqBand[i] = Mathf.Min(1.6f, freqBand[i]);
                    }



                    return freqBand;
                }
            }
            return new float[8];
        }




        [HarmonyPatch(typeof(PlayerShip), "UpdateReadImmediateControls")]
        internal class MPAudioTaunts_PlayerShip_UpdateReadImmediateControls
        {
            static void Prefix()
            {
                if (!GameplayManager.IsDedicatedServer() && server_supports_audiotaunts)
                {
                    if (remaining_cooldown > 0f)
                        remaining_cooldown -= Time.deltaTime;
                    for (int i = 0; i < 6; i++)
                    {
                        if (remaining_cooldown <= 0f && keybinds[i] > 0 && Input.GetKeyDown((KeyCode)keybinds[i]))
                        {
                            remaining_cooldown = default_taunt_cooldown;
                            PlayAudioTauntFromAudioclip(local_taunts[i].audioclip);
                        }
                    }
                }
            }
        }


        // Send the file names of your audio taunts to the server when entering a game
        [HarmonyPatch(typeof(Client), "OnAcceptedToLobby")]
        class MPAudioTaunts_Client_OnAcceptedToLobby
        {
            static void Postfix()
            {
                if (GameplayManager.IsDedicatedServer() | Client.GetClient() == null) //| !server_supports_audiotaunts
                    return;

                string fileNames = "";
                for(int i = 0; i < local_taunts.Length; i++)
                {
                    if(local_taunts[i].hash != null && !local_taunts[i].hash.Equals("EMPTY"))
                    {
                        fileNames += local_taunts[i].hash + "-" + local_taunts[i].name;
                        if (i != local_taunts.Length - 1 )
                            fileNames += "/";
                    }
                }

                Client.GetClient().Send(MessageTypes.MsgShareAudioTauntIdentifiers,
                    new ShareAudioTauntIdentifiers
                    {
                        netId = GameManager.m_local_player.netId.Value,
                        identifiers = fileNames
                    });
            }
        }


        // resets match specific informations
        [HarmonyPatch(typeof(NetworkMatch), "InitBeforeEachMatch")]
        class MPAudioTaunts_NetworkMatch_InitBeforeEachMatch
        {
            static void Postfix()
            {
                if (!GameplayManager.IsDedicatedServer())
                    return;

                connectionids = new List<int>();
                active_files = new Dictionary<string, int>();
            }
        }





        [HarmonyPatch(typeof(Client), "RegisterHandlers")]
        class MPAudioTaunts_Client_RegisterHandlers
        {
            private static void OnShareAudioTauntIdentifiers(NetworkMessage rawMsg)
            {
                Debug.Log("NETWORK: Received AudioTauntIdentifiers from Server id:4");
                var msg = rawMsg.ReadMessage<ShareAudioTauntIdentifiers>();
                string[] file_hashes = msg.identifiers.Split('/');
                foreach (string hash in file_hashes)
                {
                    Debug.Log("  Hashes received: " + hash);
                }

                // compare the hashes with the loaded ones
                // and attempt to load unknown ones locally
                // and request those that dont exist locally
            }

            private static void OnRequestAudioTauntFromClient(NetworkMessage rawMsg)
            {
                // start a transmission progress to the server
            }

            private static void OnUploadTauntToClient(NetworkMessage rawMsg)
            {
                // receive a transmission from the server
            }

            static void Postfix()
            {
                NetworkServer.RegisterHandler(MessageTypes.MsgShareAudioTauntIdentifiers, OnShareAudioTauntIdentifiers);
                NetworkServer.RegisterHandler(MessageTypes.MsgRequestAudioTauntFromClient, OnRequestAudioTauntFromClient);
                NetworkServer.RegisterHandler(MessageTypes.MsgUploadTauntToClient, OnUploadTauntToClient);
            }
        }

        public static List<int> connectionids;                  // contains the connection ids of the clients that support audiotaunts
        public static Dictionary<string, int> active_files;     // contains the filenames of all audiotaunts that can get used and requested in the current game. string = filename, int = connection id
        public static Dictionary<string, FileData> server_audio_taunts = new Dictionary<string, FileData>();

        [HarmonyPatch(typeof(Server), "RegisterHandlers")]
        class MPAudioTaunts_Server_RegisterHandlers
        {
            private static void OnShareAudioTauntIdentifiers(NetworkMessage rawMsg)
            {
                // Read the message and add unknown hashes to active_files
                Debug.Log("NETWORK: Received AudioTauntIdentifiers from Client");
                var msg = rawMsg.ReadMessage<ShareAudioTauntIdentifiers>();
                string[] file_hashes = msg.identifiers.Split('/');
                int index = 0;
                foreach (string hash in file_hashes)
                {
                    if (index < 6 && !active_files.ContainsKey(hash))
                    {
                        active_files.Add(hash, rawMsg.conn.connectionId);
                        Debug.Log("  Added to actives_files: "+hash);
                    }
                    index++;
                }

                // Send the fully updated list of hashes to all connected clients
                string filenames = "";
                foreach (var file in active_files)
                    filenames += file.Key + "/";

                ShareAudioTauntIdentifiers packet = new ShareAudioTauntIdentifiers
                {
                    netId = 0,
                    identifiers = filenames
                };
                connectionids.Add(rawMsg.conn.connectionId);
                foreach (int connid in connectionids)
                    NetworkServer.SendToClient(connid, MessageTypes.MsgShareAudioTauntIdentifiers, packet);

            }

            private static void OnRequestAudioTauntFromServer(NetworkMessage rawMsg)
            {
                // check if the file exists in server_audio_taunts and start a coroutine that uses
                // a series of UploadTauntToClient packets to deliver the file to the client


                // if it does not exist in server_audio_taunts put the request on hold and request the file from the client that brought it to this game
                // and deliver it once it has been received from that client

            }

            private static void OnUploadTauntToServer(NetworkMessage rawMsg)
            {
                // receive the transmission
                // track the progress
                // add it to the server files
                // check unanswered requests and start an upload to a client if one of them matches
            }

            static void Postfix()
            {
                NetworkServer.RegisterHandler(MessageTypes.MsgShareAudioTauntIdentifiers, OnShareAudioTauntIdentifiers);
                NetworkServer.RegisterHandler(MessageTypes.MsgRequestAudioTauntFromServer, OnRequestAudioTauntFromServer);
                NetworkServer.RegisterHandler(MessageTypes.MsgUploadTauntToServer, OnUploadTauntToServer);
            }
        }

        // send up to 6 identifiers at once
        public class ShareAudioTauntIdentifiers : MessageBase
        {
            public uint netId;
            public string identifiers;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(netId);
                writer.Write(identifiers);
            }
            public override void Deserialize(NetworkReader reader)
            {
                netId = reader.ReadUInt32();
                identifiers = reader.ReadString();
            }
        }

        public class RequestAudioTauntFromClient : MessageBase
        {
            public uint netId;
            public string identifier;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(netId);
                writer.Write(identifier);
            }
            public override void Deserialize(NetworkReader reader)
            {
                netId = reader.ReadUInt32();
                identifier = reader.ReadString();
            }
        }

        public class RequestAudioTauntFromServer : MessageBase
        {
            public uint netId;
            public string identifier;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(netId);
                writer.Write(identifier);
            }
            public override void Deserialize(NetworkReader reader)
            {
                netId = reader.ReadUInt32();
                identifier = reader.ReadString();
            }
        }

        // add a space for identifiers to this place
        public class UploadTauntToClient : MessageBase
        {
            public uint netId;
            public int pos_of_first_byte;
            public int size_of_file;
            public int amount_of_bytes_sent;
            public byte[] data;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(netId);
                writer.Write(pos_of_first_byte);
                writer.Write(size_of_file);
                writer.Write(amount_of_bytes_sent);
                if (data.Length > 3000) Debug.Log("WARNING: Attempting to send a messsage with to many bytes: " + data.Length);
                for (int i = 0; i < data.Length; i++)
                {
                    writer.Write(data[i]);
                }

            }
            public override void Deserialize(NetworkReader reader)
            {
                netId = reader.ReadUInt32();
                pos_of_first_byte = reader.ReadInt32();
                size_of_file = reader.ReadInt32();
                amount_of_bytes_sent = (reader.ReadInt32());
                data = reader.ReadBytes(amount_of_bytes_sent);
            }
        }

        // add a space for identifiers to this place
        public class UploadTauntToServer : MessageBase
        {
            public uint netId;
            public int pos_of_first_byte;
            public int size_of_file;
            public int amount_of_bytes_sent;
            public byte[] data;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(netId);
                writer.Write(pos_of_first_byte);
                writer.Write(size_of_file);
                writer.Write(amount_of_bytes_sent);
                if (data.Length > 3000) Debug.Log("WARNING: Attempting to send a messsage with to many bytes: " + data.Length);
                for (int i = 0; i < data.Length; i++)
                {
                    writer.Write(data[i]);
                }

            }
            public override void Deserialize(NetworkReader reader)
            {
                netId = reader.ReadUInt32();
                pos_of_first_byte = reader.ReadInt32();
                size_of_file = reader.ReadInt32();
                amount_of_bytes_sent = (reader.ReadInt32());
                data = reader.ReadBytes(amount_of_bytes_sent);
            }
        }



    }
}
