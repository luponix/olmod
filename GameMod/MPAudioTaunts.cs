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
using System.Reflection;
using System.Reflection.Emit;

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

        public static bool active = true;

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
        public const int audio_taunt_size_limit = 131072;        // 128 kB
        public static int audio_taunt_volume = 50;
        public const float default_taunt_cooldown = 4.5f;             // defines the minimum intervall between sending taunts for the client
        public static float remaining_cooldown = 0f;                // tracks the current state of the cooldown. gets reduced with each update. the client is allowed to send another taunt after 'remaining_cooldown' is at or below 0.0
        public static bool server_supports_audiotaunts = false;
        public static WaitForSecondsRealtime delay = new WaitForSecondsRealtime(0.016f);

        public class Taunt
        {
            public string hash = "";
            public string name = "";
            public AudioClip audioclip;
        }

        public class FileData
        {
            public int netid;
            public int pos;
            public string identifier;
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
                    ImportAudioTaunts(ExternalAudioTauntDirectory, new List<string>());
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


            // from https://github.com/derhass/olmod/commit/fa897b3384dfd6f228d4c95a385af6b7f37d99b5
            // Fix patching GameManager.Awake
            // When "GameManager.Awake" gets patched, two things happen:
            // 1. GameManager.Version gets set to 0.0.0.0 becuae it internally uses
            //    GetExecutingAssembly() to query the version, but the patched version
            //     lives in another assembly
            // 2. The "anitcheat" injection detector is triggered. But that thing
            //    doesn't do anything useful anyway, so disable it
            // return the Assembly which contains Overload.GameManager
            public static Assembly GetOverloadAssembly()
            {
                return Assembly.GetAssembly(typeof(Overload.GameManager));
            }

            // transpiler
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
            {
                // This patches the next call of Server.IsDedicatedServer() call after
                // a StartCoroutine was called to just pushing true onto the stack instead.
                // We play safe here becuase other patches might add IsDedicatedServer() calls
                // to that method, so we search specifically for the first one after
                // StartCoroutine was called.
                int state = 0;

                foreach (var code in codes)
                {
                    // patch GetExecutingAssembly to use GetOverloadAssembly instead
                    if (code.opcode == OpCodes.Call && ((MethodInfo)code.operand).Name == "GetExecutingAssembly")
                    {
                        var method = AccessTools.Method(typeof(MPAudioTaunts_GameManager_Awake), "GetOverloadAssembly");
                        yield return new CodeInstruction(OpCodes.Call, method);
                        continue;
                    }
                    if (state == 0 && code.opcode == OpCodes.Call && ((MethodInfo)code.operand).Name == "StartCoroutine")
                    {
                        state = 1;
                    }
                    else if (state == 1 && code.opcode == OpCodes.Call && ((MethodInfo)code.operand).Name == "IsDedicatedServer")
                    {
                        // this is the first IsDedicatedServer call after StartCoroutine
                        yield return new CodeInstruction(OpCodes.Ldc_I4_1); // push true on the stack instead
                        state = 2; // do not patch other invocations of StartCoroutine
                        continue;
                    }

                    yield return code;
                }
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
                if ((files_to_load.Contains(file.Name) | load_all_files) && taunts.Find(t => t.name.Equals(file.Name)) == null && (file.Extension.Equals(".ogg") || file.Extension.Equals(".wav")) && file.Length <= audio_taunt_size_limit)
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

        public static void PlayAudioTauntFromAudioclip(AudioClip audioClip, string clip_id = null)
        {
            if (audio_taunt_volume == 0 | audioClip == null | !active )
                return;

            //AudioSource audioSource = new GameObject().AddComponent<AudioSource>();
            int index = -1;
            for (int i = 0; i < audioSources.Length; i++)
            {
                if (audioSources[i] == null)
                {
                    audioSources[i] = new GameObject().AddComponent<AudioSource>();
                    Debug.Log("Had to instantiate a new AudioSource");
                }
                if (!audioSources[i].isPlaying) index = i;
            }

            if (index == -1)
            {
                Debug.Log("Couldnt play Audio taunt. All audio sources are occupied!");
                return;
            }
            audioSources[index].clip = audioClip;
            audioSources[index].volume = audio_taunt_volume / 100f;
            audioSources[index].timeSamples = 0;
            audioSources[index].bypassReverbZones = true;
            audioSources[index].reverbZoneMix = 0f;
            audioSources[index].PlayScheduled(AudioSettings.dspTime);
            audioSources[index].SetScheduledEndTime(AudioSettings.dspTime + 4);

            if(GameplayManager.IsMultiplayer && clip_id != null && Client.GetClient() != null && (GameplayManager.m_gameplay_state == GameplayState.PLAYING | MenuManager.m_menu_state == MenuState.MP_LOBBY))
            {
                Client.GetClient().Send(MessageTypes.MsgPlayAudioTaunt,
                                    new PlayAudioTaunt
                                    {
                                        identifier = clip_id 
                                    });
            }
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
                            Debug.Log("-----Found Keyinput. playing audiotaunt and sending its hash to the server to distribute");
                            PlayAudioTauntFromAudioclip(local_taunts[i].audioclip, local_taunts[i].hash);
                        }
                    }
                }
            }
        }


        // Send the file names of your audio taunts to the server when entering a game // Client OnAcceptedToLobby OnMatchStart
        [HarmonyPatch(typeof(Client), "OnAcceptedToLobby")]
        class MPAudioTaunts_Client_OnAcceptedToLobby
        {
            static void Postfix()
            {
                if (GameplayManager.IsDedicatedServer() | Client.GetClient() == null) //| !server_supports_audiotaunts
                {
                    Debug.Log("-----Bailed Early OnAcceptedToLobby");
                    return;
                }


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
                Debug.Log("-----Sent Information about my audio taunts to the server");
                Client.GetClient().Send(MessageTypes.MsgShareAudioTauntIdentifiers,
                    new ShareAudioTauntIdentifiers
                    {
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
                {
                    Debug.Log("-----Bailed out of NetworkMatch.InitBeforeEachMatch");
                    return;
                }
                    

                Debug.Log("-----Reset Audio taunt specific settings on the server");
                connectionids = new List<int>();
                active_files = new Dictionary<string, int>();
                MPAudioTaunts_Server_RegisterHandlers.requests = new List<MPAudioTaunts_Server_RegisterHandlers.Request>();
            }
        }

        // Todo: move the key check here
        [HarmonyPatch(typeof(GameManager), "Update")]
        class MPAudioTaunts_GameManager_Update
        {
            public static void Prefix()
            {
                if (!GameplayManager.IsDedicatedServer() && active)
                {
                    if (!MPAudioTaunts_Client_RegisterHandlers.isUploading && MPAudioTaunts_Client_RegisterHandlers.queued_uploads.Count > 0)
                    {
                        Debug.Log("-----Processing uploading the requested audio taunts to the server");
                        GameManager.m_gm.StartCoroutine(MPAudioTaunts_Client_RegisterHandlers.UploadAudioTauntToServer(MPAudioTaunts_Client_RegisterHandlers.queued_uploads[0]));
                        MPAudioTaunts_Client_RegisterHandlers.queued_uploads.Remove(MPAudioTaunts_Client_RegisterHandlers.queued_uploads[0]);
                    }
                }
            }
        }

                                       
        [HarmonyPatch(typeof(Client), "RegisterHandlers")]
        class MPAudioTaunts_Client_RegisterHandlers
        {
            private static void OnShareAudioTauntIdentifiers(NetworkMessage rawMsg)
            {
                //if (!active) return;

                Debug.Log("[AudioTaunts]  Received AudioTauntIdentifiers from Server");
                //rawMsg.reader.SeekZero();
                var msg = rawMsg.ReadMessage<ShareAudioTauntIdentifiers>();
                List<string> file_hashes = msg.identifiers.Split('/').ToList();
                ImportAudioTaunts(ExternalAudioTauntDirectory, file_hashes);
                
                string[] p;
                foreach (string hash in file_hashes)
                {
                    Debug.Log("  Hashes received: " + hash);
                    // if the taunt has been downloaded before then load it locally and add it to the game taunts
                    if (!external_taunts.ContainsKey(hash))
                    {
                        p = hash.Split('-');
                        if (p.Length == 2 && p[0] != null && p[1] != null)
                        {
                            Taunt taunt = taunts.Find(t => t.hash.Equals(p[0]) & t.name.Equals(p[1]));
                            if (taunt != null)
                            {
                                Debug.Log("[AudioTaunts]  Found Audiotaunt in the local data: " + hash);
                                external_taunts.Add(hash, taunt);
                            }
                            else
                            {
                                Debug.Log("[AudioTaunts]  Requesting Audiotaunt from Server: " + hash);
                                if (Client.GetClient() == null)
                                {
                                    Debug.Log("[AudioTaunts]  MPAudioTaunts_Client_RegisterHandlers: no client?");
                                    return;
                                }
                                Client.GetClient().Send(MessageTypes.MsgRequestAudioTaunt,
                                    new RequestAudioTaunt
                                    {
                                        identifier = hash // 'hash' + '-' + 'name'
                                    });

                            }
                        }
                    }
                }
            }

            public static List<FileData> queued_uploads = new List<FileData>();
            public static bool isUploading = false;
            private static void OnRequestAudioTaunt(NetworkMessage rawMsg)
            {
                //if (!active) return;

                // start a transmission to the server
                //rawMsg.reader.SeekZero();
                var msg = rawMsg.ReadMessage<RequestAudioTaunt>();
                string filename = msg.identifier;
                Debug.Log("[AudioTaunts]  Server requested audiotaunt: " + filename);

                string path = Path.Combine(LocalAudioTauntDirectory, filename);
                if(File.Exists(path))
                {
                    byte[] data = File.ReadAllBytes(path);
                    // start uploading the file;
                    Debug.Log("[AudioTaunts]  starting the upload or putting it in the queue: " + filename);
                    if (!isUploading)

                        GameManager.m_gm.StartCoroutine(UploadAudioTauntToServer(new FileData
                        {
                            netid = 0,
                            pos = 0,
                            identifier = filename,
                            bytes = data
                        }));
                    else
                        queued_uploads.Add(new FileData
                        {
                            netid = 0,
                            pos = 0,
                            identifier = filename,
                            bytes = data
                        });
                }
                else
                {
                    Debug.Log("[AudioTaunts]  The requested taunt doesnt exist on this client: "+path);
                }
            }

            public static IEnumerator UploadAudioTauntToServer( FileData data )
            {
                isUploading = true;
                Debug.Log("[AudioTaunts] Started uploading AudioTaunt to server");

                int position = 0;
                while (position < data.bytes.Length)
                {
                    int index = 0;
                    byte[] to_send = new byte[512];
                    while (index < 512 & ((position + index) < data.bytes.Length))
                    {
                        to_send[index] = data.bytes[position + index];
                        index++;
                    }

                    UploadAudioTaunt packet = new UploadAudioTaunt
                    {
                        size_of_file = data.bytes.Length,
                        amount_of_bytes_sent = index,
                        identifier = data.identifier,
                        data = to_send,//Convert.ToBase64String(to_send)
                    };
                    
                    Client.GetClient().connection.Send(MessageTypes.MsgUploadAudioTaunt,packet);
                    Debug.Log("[AudioTaunts]    upload: " + (position + index) + " / " + data.bytes.Length + "  for" + data.identifier);
                    yield return delay;
                    position += 512;
                }

                isUploading = false;
            }

            public static List<FileData> received_files = new List<FileData>();
            private static void OnUploadAudioTaunt(NetworkMessage rawMsg)
            {
                if (!active ) 
                    return;

                var msg = rawMsg.ReadMessage<UploadAudioTaunt>();

                // split message into identifier,file_size and data
                //int index = msg.data.IndexOf('/');
                string file_identifier = msg.identifier;//msg.data.Substring(0, index);
                int total_file_size = msg.size_of_file;//Int32.Parse(msg.data.Substring(index + 1, msg.data.IndexOf('/', index + 1) - (index + 1)));
                byte[] decoded_data = msg.data;//Convert.FromBase64String(msg.data);//Convert.FromBase64String(msg.data.Remove(0, msg.data.IndexOf('/', index + 1) + 1));

                FileData fd = received_files.Find(f => f.identifier.Equals(file_identifier));
                if (fd == null)
                {
                    // Create a new file context
                    Debug.Log("[AudioTaunts]    Created context for incoming file transfer: " + file_identifier);
                    FileData file_data = new FileData
                    {
                        netid = rawMsg.conn.connectionId,
                        pos = 0,
                        identifier = file_identifier,
                        bytes = new byte[total_file_size]
                    };

                    for (int i = 0; i < decoded_data.Length; i++)
                    {
                        fd.bytes[fd.pos + i] = decoded_data[i];
                        
                    }
                    fd.pos += decoded_data.Length;
                    received_files.Add(file_data);
                }
                else if (fd.pos < audio_taunt_size_limit)
                {
                    // add data to existing context
                    Debug.Log("[AudioTaunts]        Added Fragment to existing file transfer context: " + fd.identifier);
                    for (int i = 0; i < decoded_data.Length; i++)
                    {
                        fd.bytes[fd.pos + i] = decoded_data[i];

                    }
                    fd.pos += decoded_data.Length;

                    
                    if(fd.pos >= total_file_size)
                    {
                        // write the file if this was the final packet
                        Debug.Log("[AudioTaunts]    Fully received an audio taunt: " + fd.identifier);
                        File.WriteAllBytes(Path.Combine(ExternalAudioTauntDirectory, fd.identifier), fd.bytes);
                        received_files.Remove(fd);
                        List<string> file = new List<string>();
                        file.Add(fd.identifier);
                        ImportAudioTaunts(ExternalAudioTauntDirectory, file);

                        Taunt t = taunts.Find(c => (c.hash + "-" + c.name).Equals(fd.identifier));
                        if (t != null)
                            external_taunts.Add(fd.identifier, t);
                        else
                            Debug.Log("[AudioTaunts] Received file should have been loaded but was not");
                    }
                }
                else
                {
                    Debug.Log("[AudioTaunts]        Adding the Fragment would exceed the file size limit" + fd.identifier);
                }


                /*
                string file_identifier = msg.data.Substring(0, msg.data.IndexOf('/'));
                msg.data = msg.data.Remove(0, msg.data.IndexOf('/') + 1);
                int total_file_size = Int32.Parse(msg.data.Substring(0, msg.data.IndexOf('/')));
                byte[] decoded_data = Convert.FromBase64String(msg.data.Remove(0, msg.data.IndexOf('/') + 1));
                */
                /*
                // receive a transmission from the server
                //rawMsg.reader.SeekZero();
                var msg = rawMsg.ReadMessage<UploadAudioTaunt>();
                if (msg.pos_of_first_byte == 0 && msg.size_of_file < audio_taunt_size_limit)
                {
                    FileData fd = new FileData
                    {
                        netid = rawMsg.conn.connectionId,
                        identifier = msg.identifier,
                        bytes = new byte[msg.size_of_file]
                    };

                    byte[] received_bytes = Convert.FromBase64String(msg.data);
                    for(int i = 0; i < msg.amount_of_bytes_sent; i++)
                    {
                        fd.bytes[msg.pos_of_first_byte + i] = received_bytes[i];
                    }
                    received_files.Add(fd);
                }
                else
                {
                    // find the file
                    FileData fd = received_files.Find(f => f.bytes.Equals(msg.identifier));
                    if(fd != null)
                    {
                        // add the data of the packet
                        byte[] received_bytes = Convert.FromBase64String(msg.data);
                        for (int i = 0; i < msg.amount_of_bytes_sent; i++)
                        {
                            fd.bytes[msg.pos_of_first_byte + i] = received_bytes[i];
                        }

                        // write the file if this was the final packet
                        if(1 + msg.pos_of_first_byte + msg.amount_of_bytes_sent >= fd.bytes.Length )
                        {
                            Debug.Log("[AudioTaunts] Fully received an audio taunt: "+fd.identifier);
                            File.WriteAllBytes(Path.Combine(ExternalAudioTauntDirectory, fd.identifier), fd.bytes);
                            received_files.Remove(fd);
                            List<string> file = new List<string>();
                            file.Add(fd.identifier);
                            ImportAudioTaunts(ExternalAudioTauntDirectory, file);

                            Taunt t = taunts.Find(c => (c.hash+"-"+c.name).Equals(fd.identifier));
                            if (t != null)
                                external_taunts.Add(fd.identifier, t);
                            else
                                Debug.Log("[AudioTaunts] Received file should have been loaded but was not");
                        }
                    }
                }*/
            }

            private static void OnPlayAudioTaunt(NetworkMessage rawMsg)
            {
                //if (!active) return;

                rawMsg.reader.SeekZero();
                var msg = rawMsg.ReadMessage<PlayAudioTaunt>();
                Debug.Log("[AudioTaunts] playing external audiotaunt: "+msg.identifier);
                if (external_taunts.ContainsKey(msg.identifier))
                {
                    external_taunts.TryGetValue(msg.identifier, out Taunt t);
                    PlayAudioTauntFromAudioclip(t.audioclip);
                }
            }

            static void Postfix()
            {
                if (Client.GetClient() == null)
                {
                    Debug.Log("Couldnt setup MessageHandlers for Audiotaunts on the Client");
                    return;
                }

                Client.GetClient().RegisterHandler(MessageTypes.MsgShareAudioTauntIdentifiers, OnShareAudioTauntIdentifiers);
                Client.GetClient().RegisterHandler(MessageTypes.MsgRequestAudioTaunt, OnRequestAudioTaunt);
                Client.GetClient().RegisterHandler(MessageTypes.MsgUploadAudioTaunt, OnUploadAudioTaunt);
                Client.GetClient().RegisterHandler(MessageTypes.MsgPlayAudioTaunt, OnPlayAudioTaunt);
            }
        }










        public static List<int> connectionids = new List<int>();                 // contains the connection ids of the clients that support audiotaunts
        public static Dictionary<string, int> active_files = new Dictionary<string, int>();     // contains the filenames of all audiotaunts that can get used and requested in the current game. string = filename, int = connection id
        public static Dictionary<string, FileData> server_audio_taunts = new Dictionary<string, FileData>();

        [HarmonyPatch(typeof(Server), "RegisterHandlers")]
        class MPAudioTaunts_Server_RegisterHandlers
        {
            private static void OnShareAudioTauntIdentifiers(NetworkMessage rawMsg)
            {
                // Read the message and add unknown hashes to active_files
                Debug.Log("[Audiotaunt] : Received AudioTauntIdentifiers from Client");
                //rawMsg.reader.SeekZero();
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


                foreach (NetworkConnection networkConnection in NetworkServer.connections)
                {
                    if (MPTweaks.ClientHasTweak(networkConnection.connectionId, "audiotaunts"))
                    {
                        networkConnection.Send(MessageTypes.MsgShareAudioTauntIdentifiers, new ShareAudioTauntIdentifiers{identifiers = filenames});
                    }
                }

            }

            public class Request
            {
                public int connectionId;
                public string identifier;
            }

            public static List<Request> requests = new List<Request>();
            private static void OnRequestAudioTaunt(NetworkMessage rawMsg)
            {
                // check if the file exists in server_audio_taunts and start a coroutine that uses
                // a series of UploadAudioTaunt packets to deliver the file to the client
                //rawMsg.reader.SeekZero();
                Debug.Log("[AudioTaunts] Received audio taunt file request");
                var msg = rawMsg.ReadMessage<RequestAudioTaunt>();
                if(active_files.ContainsKey(msg.identifier))
                {
                    if (server_audio_taunts.ContainsKey(msg.identifier))
                    {
                        Debug.Log("[AudioTaunts]    Found file. Starting the upload");
                        server_audio_taunts.TryGetValue(msg.identifier, out FileData fd);

                        GameManager.m_gm.StartCoroutine(UploadAudioTauntToClient(new FileData
                        {
                            netid = 0,
                            pos = 0,
                            identifier = fd.identifier,
                            bytes = fd.bytes
                        }));
                    }
                    else
                    {
                        Debug.Log("[AudioTaunts]    Requesting that file from another client");
                        requests.Add(new Request
                        {
                            connectionId = rawMsg.conn.connectionId,
                            identifier = msg.identifier
                        });

                        active_files.TryGetValue(msg.identifier, out int connId);
                        NetworkServer.SendToClient(connId, MessageTypes.MsgRequestAudioTaunt,
                                    new RequestAudioTaunt
                                    {
                                        identifier = msg.identifier, // 'hash' + '-' + 'name'
                                    });
                    }
                }


            }

            public static IEnumerator UploadAudioTauntToClient(FileData data)
            {
                Debug.Log("[AudioTaunts] Started uploading AudioTaunt to client");

                int position = 0;
                while (position < data.bytes.Length)
                {
                    int index = 0;
                    byte[] to_send = new byte[512];
                    while (index < 512 & ((position + index) < data.bytes.Length))
                    {
                        to_send[index] = data.bytes[position + index];
                        index++;
                    }

                    UploadAudioTaunt packet = new UploadAudioTaunt
                    {
                        size_of_file = data.bytes.Length,
                        amount_of_bytes_sent = index,
                        identifier = data.identifier,
                        data = to_send,//Convert.ToBase64String(to_send)
                    };
                    NetworkServer.SendToClient(data.netid, MessageTypes.MsgUploadAudioTaunt, packet);
                    Debug.Log("[AudioTaunts]    upload: "+(position+index)+" / "+data.bytes.Length+"  for" + data.identifier);
                    yield return delay;
                    position += 512;
                }
                Debug.Log("[AudioTaunts] successfully transmitted taunt to client"+data.identifier);
            }


            private static void OnUploadAudioTaunt(NetworkMessage rawMsg)
            {
                if (!active)
                    return;

                Debug.Log("[AudioTaunts]        Received a Fragment of an Audiotaunt");
                var msg = rawMsg.ReadMessage<UploadAudioTaunt>();
                Debug.Log("[AudioTaunts]            Marker ");
                // split message into identifier,file_size and data
                //int index = msg.data.IndexOf('/');
                string file_identifier = msg.identifier;//msg.data.Substring(0, index);
                Debug.Log("[AudioTaunts]            Marker ");
                int total_file_size = msg.size_of_file;//Int32.Parse(msg.data.Substring(index + 1, msg.data.IndexOf('/', index + 1) - (index + 1)));
                Debug.Log("[AudioTaunts]            Marker ");
                byte[] decoded_data = msg.data;//Convert.FromBase64String(msg.data);//Convert.FromBase64String(msg.data.Remove(0, msg.data.IndexOf('/', index + 1) + 1));
                Debug.Log("[AudioTaunts]            Marker ");
                server_audio_taunts.TryGetValue(file_identifier, out FileData fd);
                if (fd == null)
                {
                    // Create a new file context
                    Debug.Log("[AudioTaunts]    Created context for incoming file transfer: " + file_identifier);
                    FileData file_data = new FileData
                    {
                        netid = rawMsg.conn.connectionId,
                        pos = 0,
                        identifier = file_identifier,
                        bytes = new byte[total_file_size]
                    };

                    for (int i = 0; i < decoded_data.Length; i++)
                    {
                        fd.bytes[fd.pos + i] = decoded_data[i];

                    }
                    fd.pos += decoded_data.Length;
                    server_audio_taunts.Add(file_identifier,file_data);
                }
                else if (fd.pos < audio_taunt_size_limit)
                {
                    // add data to existing context
                    Debug.Log("[AudioTaunts]        Added Fragment to existing file transfer context: " + fd.identifier);
                    for (int i = 0; i < decoded_data.Length; i++)
                    {
                        fd.bytes[fd.pos + i] = decoded_data[i];

                    }
                    fd.pos += decoded_data.Length;


                    if (fd.pos >= total_file_size)
                    {
                        Debug.Log("[AudioTaunts] Fully received an audio taunt: " + fd.identifier);
                        foreach (Request req in requests)
                        {
                            if (req.identifier.Equals(file_identifier))
                            {
                                Debug.Log("[AudioTaunts] Transmitting received file to connId: " + req.connectionId);
                                GameManager.m_gm.StartCoroutine(UploadAudioTauntToClient(new FileData
                                {
                                    netid = req.connectionId,
                                    pos = 0,
                                    identifier = fd.identifier,
                                    bytes = fd.bytes
                                }));
                            }
                        }
                        requests.RemoveAll(r => r.identifier.Equals(file_identifier));
                    }
                }
                else
                {
                    Debug.Log("[AudioTaunts]        Adding the Fragment would exceed the file size limit" + fd.identifier);
                }


                // receive a transmission from a client
                
                //rawMsg.reader.SeekZero();
                /*
                var msg = rawMsg.ReadMessage<UploadAudioTaunt>();
                if (msg.pos_of_first_byte == 0 && msg.size_of_file <= audio_taunt_size_limit)
                {
                    FileData fd = new FileData
                    {
                        netid = rawMsg.conn.connectionId,
                        identifier = msg.identifier,
                        bytes = new byte[msg.size_of_file]
                    };
                    byte[] received_bytes = Convert.FromBase64String(msg.data);
                    for (int i = 0; i < msg.amount_of_bytes_sent; i++)
                    {
                        fd.bytes[msg.pos_of_first_byte + i] = received_bytes[i];
                    }
                    server_audio_taunts.Add(msg.identifier, fd);
                    Debug.Log("[AudioTaunts]        Created a context for receiving more fragments");
                }
                else
                {
                    Debug.Log("[AudioTaunts]        Didnt create context: pos of first byte:" + msg.pos_of_first_byte +"   amount of bytes sent:"+ msg.amount_of_bytes_sent + "   size of file:"+msg.size_of_file+"   audiotaunt size limit:"+audio_taunt_size_limit);
                    // find the file
                    server_audio_taunts.TryGetValue(msg.identifier, out FileData fd);
                    if (fd != null)
                    {
                        // add the data of the packet
                        byte[] received_bytes = Convert.FromBase64String(msg.data);
                        for (int i = 0; i < msg.amount_of_bytes_sent; i++)
                        {
                            fd.bytes[msg.pos_of_first_byte + i] = received_bytes[i];
                        }
                        server_audio_taunts[msg.identifier] = fd;
                        Debug.Log("[AudioTaunts]        Added the fragment to existing context progress: "+ (1 + msg.pos_of_first_byte + msg.amount_of_bytes_sent)+" / "+ fd.bytes.Length);

                        // write the file if this was the final packet
                        if (1 + msg.pos_of_first_byte + msg.amount_of_bytes_sent >= fd.bytes.Length)
                        {
                            Debug.Log("[AudioTaunts] Fully received an audio taunt: " + fd.identifier);
                            foreach(Request req in requests)
                            {
                                if(req.identifier.Equals(msg.identifier))
                                {
                                    Debug.Log("[AudioTaunts] Transmitting received file to connId: " + req.connectionId);
                                    GameManager.m_gm.StartCoroutine(UploadAudioTauntToClient(new FileData
                                    {
                                        netid = 0,
                                        identifier = fd.identifier,
                                        bytes = fd.bytes
                                    }));
                                }
                            }
                            requests.RemoveAll(r => r.identifier.Equals(msg.identifier));
                        }
                    }
                    
                }
                */


            }


            private static void OnPlayAudioTaunt(NetworkMessage rawMsg)
            {
                // check wether it fits to the shared audio taunt identifiers
                //rawMsg.reader.SeekZero();
                var msg = rawMsg.ReadMessage<PlayAudioTaunt>();
                if (server_audio_taunts.ContainsKey(msg.identifier))
                {
                    // distribute it to all other clients
                    PlayAudioTaunt packet = new PlayAudioTaunt
                    {
                        identifier = msg.identifier
                    };
                    foreach (int connid in connectionids)
                        if(connid != rawMsg.conn.connectionId) NetworkServer.SendToClient(connid, MessageTypes.MsgPlayAudioTaunt, packet);

                }
                
            }

            static void Postfix()
            {
                NetworkServer.RegisterHandler(MessageTypes.MsgShareAudioTauntIdentifiers, OnShareAudioTauntIdentifiers);
                NetworkServer.RegisterHandler(MessageTypes.MsgRequestAudioTaunt, OnRequestAudioTaunt);
                NetworkServer.RegisterHandler(MessageTypes.MsgUploadAudioTaunt, OnUploadAudioTaunt);
                NetworkServer.RegisterHandler(MessageTypes.MsgPlayAudioTaunt, OnPlayAudioTaunt);
            }
        }

        public class ShareAudioTauntIdentifiers : MessageBase
        {
            public string identifiers;
            
            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(identifiers);
            }
            public override void Deserialize(NetworkReader reader)
            {
                reader.SeekZero();
                identifiers = reader.ReadString();
            }
        }

        public class RequestAudioTaunt : MessageBase
        {
            public string identifier;
            
            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(identifier);
            }
            public override void Deserialize(NetworkReader reader)
            {
                reader.SeekZero();
                identifier = reader.ReadString();
            }
        }

        public class UploadAudioTaunt : MessageBase
        {
            //public int pos_of_first_byte;
            public int size_of_file;
            public int amount_of_bytes_sent;
            public string identifier;
            public byte[] data;


            public override void Serialize(NetworkWriter writer)
            {
                //writer.Write(pos_of_first_byte);
                writer.Write(size_of_file);
                writer.Write(amount_of_bytes_sent);
                writer.Write(identifier);
                for(int i = 0; i < amount_of_bytes_sent; i++)
                {
                    writer.Write(data[i]);
                } 
            }
            public override void Deserialize(NetworkReader reader)
            {
                reader.SeekZero();
                //pos_of_first_byte = reader.ReadInt32();
                size_of_file = reader.ReadInt32();
                amount_of_bytes_sent = reader.ReadInt32();
                identifier = reader.ReadString();
                data = reader.ReadBytes(amount_of_bytes_sent);

            }
        }

        public class PlayAudioTaunt : MessageBase
        {
            public string identifier;
            
            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(identifier);
            }
            public override void Deserialize(NetworkReader reader)
            {
                reader.SeekZero();
                identifier = reader.ReadString();
            }
        }



    }
}
