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
        // 
        // DONE 2. Load the ressources in that folder
        // DONE 3. Test wether these can be played by the press of a button ingame
        // DONE 4. check the length/size of these clips and reject those that are to long or to big
        // 5. create infrastructure for sharing the selected 6 clips of all players through the server before the match starts and handle join in progress properly
        // 6. build a gui that allows playing the audiotaunts, selecting 6 of them and binding them to keys
        // 7. store the keys and selected clips in the player.xprefsmod
        // 8. implement a 1 second cooldown between audiotaunt activation
        // 9. add a slider in the sound options that controls the volume of audiotaunts (at 0 dont play the clips in the first place)
        // 10. dont distribute audiotaunts and playsignals by people that are kicked/banned
        // 11. Update the local taunts when the pilot changes
        // 12. empty the external taunts dictionary when entering a game

        //  CLIENT
        public static bool isActive = true;
        public static bool initialized = false;
        public static string LocalAudioTauntDirectory = "";         // path towards the directory where the audiotaunts from the local installation are saved
        public static string ExternalAudioTauntDirectory = "";      // path towards the directory where the audiotaunts of other players get saved
        public static List<Taunt> taunts = new List<Taunt>();       // a list of all locally loaded audio taunts
        
        public static Taunt[] local_taunts = new Taunt[6];          // contains the audio taunts that this pilot has chosen, can not change during a game 
        public static int[] keybinds = new int[6];
        public static float audio_taunt_volume = 1f;
        
        public static Dictionary<string, Taunt> external_taunts = new Dictionary<string, Taunt>();  // contains the audio taunts of the other players during a game
        public const float default_taunt_cooldown = 3f;             // defines the minimum intervall between sending taunts for the client
        public static float remaining_cooldown = 0f;                // tracks the current state of the cooldown. gets reduced with each update. the client is allowed to send another taunt after 'remaining_cooldown' is at or below 0.0
        public const int audio_taunt_size_limit_kB = 131072;        // 128 kB

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

                local_taunts[0] = new Taunt
                {
                    hash = "EMPTY",
                    name = "EMPTY",
                    audioclip = null
                };
                local_taunts[1] = new Taunt
                {
                    hash = "EMPTY",
                    name = "EMPTY",
                    audioclip = null
                };
                local_taunts[2] = new Taunt
                {
                    hash = "EMPTY",
                    name = "EMPTY",
                    audioclip = null
                };
                local_taunts[3] = new Taunt
                {
                    hash = "EMPTY",
                    name = "EMPTY",
                    audioclip = null
                };
                local_taunts[4] = new Taunt
                {
                    hash = "EMPTY",
                    name = "EMPTY",
                    audioclip = null
                };
                local_taunts[5] = new Taunt
                {
                    hash = "EMPTY",
                    name = "EMPTY",
                    audioclip = null
                };

                //List<string> l = new List<string>();
                //l.Add("5376e3f20f495672db82035f2923170e-game-won.ogg");
                //ImportAudioTaunts(LocalAudioTauntDirectory, l);

                local_taunts[0] = taunts[0];
                local_taunts[1] = taunts[1];
                local_taunts[2] = taunts[2];
                local_taunts[3] = taunts[3];
                local_taunts[4] = taunts[4];
                local_taunts[5] = taunts[5];
                // POPULATE THIS WHEN LOADING PILOT FILES INSTEAD


                initialized = true;
            }
        }

        // if files_to_load is empty ImportAudioTaunts will load all eligible audio taunts in the 
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
                        hash = CalculateMD5(Path.Combine(path_to_directory, file.Name)),
                        name = file.Name,
                        audioclip = MPAudioTaunts.LoadAudioClip(file.Name, file.Extension, path_to_directory)//Resources.Load<AudioClip>("AudioTaunts/" + file.Name)
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


        // taken from https://stackoverflow.com/a/10520086/10955803
        static string CalculateMD5(string filename)
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


        public static AudioClip LoadAudioClip(string filename, string ext, string path2)
        {
            string path = Path.Combine(path2, filename);
            if (path != null)
            {
                WWW www = new WWW("file:///" + path);
                while (!www.isDone){ }
                if (string.IsNullOrEmpty(www.error))
                {
                    return www.GetAudioClip(true, false);
                }
                else Debug.Log("Error in 'LoadAudioClip': " + www.error + " :" + filename + ":" + ext + ":" + path2);
            }
            return null;
        }

        public static void PlayAudioTaunt(int id)
        {
            if (taunts == null) return;
            if (id >= taunts.Count | id < 0) return;

            if (taunts[id].audioclip == null) uConsole.Log("AUDIOCLIP IS EMPTY");

            AudioSource audioSource = new GameObject().AddComponent<AudioSource>();
            audioSource.clip = taunts[id].audioclip;
            audioSource.volume = 1f;
            audioSource.timeSamples = 0;
            audioSource.bypassReverbZones = true;
            audioSource.reverbZoneMix = 0f;//UnityEngine.Random.Range(0f, 0.002f);
            audioSource.Play();
            Debug.Log("started playing "+ taunts[id].name);
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






       // FOR DEBUGGING
       [HarmonyPatch(typeof(MenuManager), "ApplyResolution")]
        class MPAudioTaunts_GameManager_Awake2
        {
            static void Postfix()
            {
                //GameManager.m_audio.PlayVoiceMessage(taunts[0].audioclip, 0, true);
                if (taunts[0].audioclip == null) uConsole.Log("AUDIOCLIP IS EMPTY");

                AudioSource audioSource = new GameObject().AddComponent<AudioSource>();
                audioSource.clip = taunts[0].audioclip;
                audioSource.volume = 1f;
                audioSource.timeSamples = 0;
                audioSource.bypassReverbZones = true;
                audioSource.reverbZoneMix = 0f;//UnityEngine.Random.Range(0f, 0.002f);
                audioSource.Play();
                Debug.Log("started playing");//+ taunts[0].name);
            }
        }







        // FOR DEBUGGING
        [HarmonyPatch(typeof(GameManager), "Start")]
        internal class ATCmds
        {
            private static void Postfix(GameManager __instance)
            {
                uConsole.RegisterCommand("playat", "", new uConsole.DebugCommand(CmdPlay));
                uConsole.RegisterCommand("showat", "", new uConsole.DebugCommand(CmdPrintAllTaunts));
                uConsole.RegisterCommand("microstate", "", new uConsole.DebugCommand(CmdSetMicrostate));
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
                MenuManager.m_menu_micro_state = uConsole.GetInt();
            }

            private static void Cmd3()
            {

            }

        }

    }
}
