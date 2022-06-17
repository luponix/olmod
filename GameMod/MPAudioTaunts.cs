using HarmonyLib;
using Overload;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Security.Cryptography;
using System.Collections;

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

        public static int selected_audio_slot = 0;
        public const int audio_taunt_size_limit_kB = 131072;        // 128 kB
        public static int audio_taunt_volume = 50;
        public const float default_taunt_cooldown = 6f;             // defines the minimum intervall between sending taunts for the client
        public static float remaining_cooldown = 0f;                // tracks the current state of the cooldown. gets reduced with each update. the client is allowed to send another taunt after 'remaining_cooldown' is at or below 0.0
        

        public class Taunt
        {
            public string hash = "";
            public string name = "";
            public AudioClip audioclip;
        }

        // INITIALISATION:
        //  - check if the directory exists
        //  - load local audio taunts
        [HarmonyPatch(typeof(GameManager), "Awake")]
        class MPAudioTaunts_GameManager_Awake
        {
            static void Postfix()
            {
                if(String.IsNullOrEmpty(LocalAudioTauntDirectory))
                {
                    LocalAudioTauntDirectory = Path.Combine(Application.streamingAssetsPath, "AudioTaunts");
                    if (!Directory.Exists(LocalAudioTauntDirectory))
                    {
                        Debug.Log("Did not find a directory for local audiotaunts, creating one at: "+ LocalAudioTauntDirectory);
                        Directory.CreateDirectory(LocalAudioTauntDirectory);
                    }

                    ExternalAudioTauntDirectory = Path.Combine(LocalAudioTauntDirectory, "external");
                    if (!Directory.Exists(ExternalAudioTauntDirectory))
                    {
                        Debug.Log("Did not find a directory for external audiotaunts, creating one at: " + ExternalAudioTauntDirectory);
                        Directory.CreateDirectory(ExternalAudioTauntDirectory);
                    }
                }

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

                initialized = true;
            }
        }

        // Imports either the in 'files_to_load' specified taunts or all taunts from that directory
        // (under the condition that they have yet to be imported and are valid formats and that their size is not beyond 128 kB) 
        public static void ImportAudioTaunts(string path_to_directory, List<String> files_to_load)
        {
            Debug.Log("Attempting to import AudioTaunts from: "+path_to_directory);
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

                    if(t.name.StartsWith(t.hash))
                    {
                        t.name = t.name.Remove(0,t.hash.Length+1);
                    }
                    else
                    {
                        File.Move(Path.Combine(path_to_directory, file.Name), Path.Combine(path_to_directory, t.hash+"-"+file.Name));
                    }

                    taunts.Add(t);
                    Debug.Log("  Added "+ file.Name + "  size: " + file.Length + " as an AudioTaunt");

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
            foreach(string hash in file_hashes)
            {
                if(index < 6)
                {
                    Taunt at = taunts.Find(t => t.hash.Equals(hash) );
                    if(at == null)
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

            AudioSource audioSource = new GameObject().AddComponent<AudioSource>();
            audioSource.clip = audioClip;
            audioSource.volume = audio_taunt_volume / 100f;
            audioSource.timeSamples = 0;
            audioSource.bypassReverbZones = true;
            audioSource.reverbZoneMix = 0f;
            audioSource.PlayScheduled(AudioSettings.dspTime);
            audioSource.SetScheduledEndTime(AudioSettings.dspTime + 5);
        }

        [HarmonyPatch(typeof(PlayerShip), "UpdateReadImmediateControls")]
        internal class MPAudioTaunts_PlayerShip_UpdateReadImmediateControls
        {
            static void Postfix()
            {
                if (remaining_cooldown > 0f)
                    remaining_cooldown -= Time.deltaTime;
                for(int i = 0; i < 6; i++)
                {
                    if(remaining_cooldown <= 0f && keybinds[i] > 0 && Input.GetKeyDown((KeyCode)keybinds[i]))
                    {
                        remaining_cooldown = default_taunt_cooldown;
                        PlayAudioTauntFromAudioclip(local_taunts[i].audioclip);
                    }
                }
            }


        }









        //  SERVER CODE
        public static Dictionary<string, Taunt> server_audio_taunts = new Dictionary<string, Taunt>();           // holds the audio taunts of all players in the current game

        // 1. communicate with the clients to obtain all currently selected local audio taunts and distribute those that are unknown to the different clients
        // 2. do this only with clients that support this feature
        // 3. Announce what is currently available and let the clients ask for the files that they need
        // 4. Handle clients joining/leaving and the downloads/uploads
        // 5. distribute requests to play audio taunts and crosscheck wether they provide the Hashsum and exist
        // 6. Let the clients handle the rest of checking the validity
        // 7. anticipate malicious clients to some extent
        // 8. Handle the exchange of files parallel to the game execution









        public static int index = 0;

        public static void PlayAudioTaunt(int id)
        {
            if (audio_taunt_volume == 0 | taunts == null | id >= taunts.Count | id < 0 | taunts[id].audioclip == null)
                return;

            AudioSource audioSource = new GameObject().AddComponent<AudioSource>();
            audioSource.clip = taunts[id].audioclip;
            audioSource.volume = audio_taunt_volume / 100f;
            audioSource.timeSamples = 0;
            audioSource.bypassReverbZones = true;
            audioSource.reverbZoneMix = 0f;
            audioSource.PlayScheduled(AudioSettings.dspTime);
            audioSource.SetScheduledEndTime(AudioSettings.dspTime + 5);
        }

        // FOR DEBUGGING
        [HarmonyPatch(typeof(GameManager), "Start")]
        internal class ATCmds
        {
            private static void Postfix(GameManager __instance)
            {
                uConsole.RegisterCommand("playat", "", new uConsole.DebugCommand(CmdPlay));
                uConsole.RegisterCommand("showat", "", new uConsole.DebugCommand(CmdPrintAllTaunts));
                uConsole.RegisterCommand("setatvolume", "", new uConsole.DebugCommand(CmdSetMicrostate));
            }

            private static void CmdPlay()
            {
                int id = uConsole.GetInt();
                PlayAudioTaunt(id);
            }

            private static void CmdPrintAllTaunts()
            {
               foreach(Taunt taunt in taunts)
               {
                    uConsole.Log(taunt.name);
               }
            }

            private static void CmdSetMicrostate()
            {
                audio_taunt_volume = (int)(uConsole.GetFloat() * 100);

            }

            private static void Cmd3()
            {

            }

        }

    }
}
