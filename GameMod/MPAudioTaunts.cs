using HarmonyLib;
using Overload;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace GameMod
{
    class MPAudioTaunts
    {
        // 
        // 2. Load the ressources in that folder
        // 3. Test wether these can be played by the press of a button ingame
        // 4. check the length/size of these clips and reject those that are to long or to big
        // 5. create infrastructure for sharing the selected 6 clips of all players through the server before the match starts and handle join in progress properly
        // 6. build a gui that allows playing the audiotaunts, selecting 6 of them and binding them to keys
        // 7. store the keys and selected clips in the player.xprefsmod
        // 8. implement a 1 second cooldown between audiotaunt activation
        // 9. add a slider in the sound options that controls the volume of audiotaunts (at 0 dont play the clips in the first place)
        // 10. dont distribute audiotaunts and playsignals by people that are kicked/banned

        public static string AudioTauntDirectoryPath = "";
        public static List<Taunt> taunts = new List<Taunt>(); 

        public class Taunt
        {
            public string name = "";
            public AudioClip audioclip;
        }

        [HarmonyPatch(typeof(GameManager), "Awake")]
        class MPAudioTaunts_GameManager_Awake
        {
            static void Postfix()
            {
                if(String.IsNullOrEmpty(AudioTauntDirectoryPath))
                {
                    AudioTauntDirectoryPath = Path.Combine(Application.streamingAssetsPath, "AudioTaunts");
                    if (!Directory.Exists(AudioTauntDirectoryPath))
                    {
                        Debug.Log("Did not find a audiotaunt directory, creating one at: "+AudioTauntDirectoryPath);
                        Directory.CreateDirectory(AudioTauntDirectoryPath);
                    }
                }

                // Loading the audio clips
                var fileInfo = new DirectoryInfo(AudioTauntDirectoryPath).GetFiles();
                foreach(FileInfo file in fileInfo)
                {
                    if ((file.Extension.Equals(".ogg") || file.Extension.Equals(".wav")))//&& file.Length <= 262144)
                    {
                        taunts.Add(new Taunt { 
                            name = file.Name,
                            audioclip = MPAudioTaunts.LoadAudioClip(file.Name, file.Extension)//Resources.Load<AudioClip>("AudioTaunts/" + file.Name)
                        });
                        Debug.Log("  Added " + file.Name + "  size: " + file.Length + " as an AudioTaunt");
                        
                    }
                }
                

            }
        }

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


        public static AudioClip LoadAudioClip(string filename, string ext)
        {
            string path = Path.Combine(AudioTauntDirectoryPath, filename);
            if (path != null)
            {
                Debug.Log("text is " + path);
                WWW www = new WWW("file:///" + path);
                while (!www.isDone)
                {
                }
                if (string.IsNullOrEmpty(www.error))
                {
                    Debug.Log("returned audioclip");
                    return www.GetAudioClip(true, false);
                }
                else
                {
                    Debug.Log("RAN INTO ERROR: " + www.error);
                }
            }
            Debug.Log("returned null");
            return null;
        }

    }
}
