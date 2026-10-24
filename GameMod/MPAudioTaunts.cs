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
using System.Diagnostics;
using System.Threading;
using UnityEngine.Audio;
using Debug = UnityEngine.Debug;

namespace GameMod
{
    class MPAudioTaunts
    {

        /* 
         * - write a handler that dynamically schedules downloads/uploads and adjusts the packet size based on the resend rate
         * - replace the AudioTaunt.received_packet list with a dynamically sized bool array 
         */

        public const int AUDIO_TAUNT_SIZE_LIMIT = 131072;           // 128 kB, maximum allowed audio taunt file size in bytes
        public const int PACKET_PAYLOAD_SIZE = 800;                 // maximum payload size in bytes for packets that carry chunks of an audio taunt file
        public const int AMOUNT_OF_TAUNTS_PER_CLIENT = 12;          // the amount of taunts a client is allowed to bring into a match
        public const int TAUNTS_MAX_VISIBLE = 8;                    // amount of taunt slots the menu shows at once before it starts scrolling
        public const float DEFAULT_TAUNT_COOLDOWN = 4f;             // defines the minimum interval between sending taunts for the client
        public const float TAUNT_PLAYTIME = 3f;                     // defines the time in seconds that taunts are allowed to play till they get cutoff on the client
        public const float DEFAULT_SPECTRUM_UPDATE_COOLDOWN = 0.07f;
        public static WaitForSecondsRealtime delay = new WaitForSecondsRealtime(0.016f);    // interval between sending packets


        public class AudioTaunt
        {
            public string hash;                     // a MD5 hash over the file data
            public string name;                     // the original filename
            public byte[] audio_taunt_data;         // temporary holder of incoming bytes till the full audio clip is assembled (or permanently on the server to buffer it incase other clients request this data)
            public bool is_data_complete = false;   // indicates wether audio_taunt_data has been fully populated 
            public int timestamp = 0;               // holds the time (Time.frameCount) of when this object was last used in a major action like a file transfer 

            // Client
            public bool is_external_taunt;          // indicates wether this is a taunt that got added by this client (local) or wether it got downloaded in order to allow its activation by other clients during a match (external). external taunts will not show up as selectable taunts in the menu
            public bool ready_to_play;              // indicates wether the audio clip is in a playable state
            public bool requested_taunt;            // holds wether the client has requested the audiotaunt byte data for this.hash from the server
            public AudioClip audioclip;             // contains the playable taunt

            // Server
            public List<int> providing_clients;     // holds the connection ids of clients that use the taunt and can provide this taunt 
            public List<int> requesting_clients;    // A list of all connection ids that have requested this audioclip
            public List<int> received_packets;
        }

        public class AudioSourceContainer
        {
            public string player_name = "";         // the player that initiated the clip that is playing on this source. 
            public float[] frequency_band = { -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f };
            public AudioSource source = new GameObject().AddComponent<AudioSource>();

            public void UpdateFrequencyBand()
            {
                if (source == null)
                {
                   Debug.LogWarning("[AudioTaunts] UpdateFrequencyBand had to reinstantiate the audio source");
                    source = new GameObject().AddComponent<AudioSource>();
                }


                if (source.isPlaying)
                {
                    float[] samples = new float[512];
                    source.GetSpectrumData(samples, 0, FFTWindow.Rectangular);
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
                        frequency_band[i] = average * 10;
                        frequency_band[i] = Mathf.Min(1.6f, frequency_band[i]);
                    }
                }
                else
                {
                    for (int i = 0; i < frequency_band.Length; i++)
                    {
                        frequency_band[i] = -1f;
                    }
                }
            }
        }

        public static class AClient
        {
            public static bool active = true;
            public static bool initialized = false;
            public static string loaded_local_taunts = "";              // temporary holder (necessary till the execution of LoadLocalAudioTauntsFromPilotPrefs()) of the hashes of the local taunts, gets populated from the pilots config files

            public static string LocalAudioTauntDirectory = "";         // path towards the directory where the audiotaunts from the local installation are saved
            public static string ExternalAudioTauntDirectory = "";      // path towards the directory where the audiotaunts of other players get saved
            public static int[] keybinds = new int[AMOUNT_OF_TAUNTS_PER_CLIENT];                  // keybinds[i] holds the keycode that triggers the playing of local_taunts[i] 
            public static int selected_audio_slot = 0;                  // temporary variable that is used in the menu code to differentiate between the local taunt slots
            public static int audio_taunt_volume = 50;                  // range: 0-100
            public static float remaining_cooldown = 0f;                // time in seconds till the client is allowed to send an audiotaunt

            public static List<AudioTaunt> taunts = new List<AudioTaunt>();                                     // a list of all locally loaded audio taunts
            public static Dictionary<string, AudioTaunt> match_taunts = new Dictionary<string, AudioTaunt>();// contains the audio taunts of the other players during a game
            public static AudioTaunt[] local_taunts = new AudioTaunt[AMOUNT_OF_TAUNTS_PER_CLIENT];// contains the audio taunts that this client has chosen, can not change during a game 

            public static AudioSourceContainer[] asc = new AudioSourceContainer[3];
            public static float spectrum_update_cooldown = 0f;
            public static bool display_audio_spectrum = true;

            [HarmonyPatch(typeof(GameManager), "Awake")]
            class MPAudioTaunts_GameManager_Awake
            {
                static void Postfix(){
                    if (String.IsNullOrEmpty(LocalAudioTauntDirectory)){
                        LocalAudioTauntDirectory = Path.Combine(Application.persistentDataPath, "AudioTaunts");
                        if (!Directory.Exists(LocalAudioTauntDirectory)){
                            Debug.Log("[AudioTaunts] creating the missing local taunt directory: " + LocalAudioTauntDirectory);
                            Directory.CreateDirectory(LocalAudioTauntDirectory);
                        }

                        ExternalAudioTauntDirectory = Path.Combine(LocalAudioTauntDirectory, "external");
                        if (!Directory.Exists(ExternalAudioTauntDirectory)){
                            Debug.Log("[AudioTaunts] creating the missing external taunt directory: " + ExternalAudioTauntDirectory);
                            Directory.CreateDirectory(ExternalAudioTauntDirectory);
                        }
                    }

                    if (!GameplayManager.IsDedicatedServer()){
                        for (int i = 0; i < asc.Length; i++){
                            asc[i] = new AudioSourceContainer();
                        }

                        for (int i = 0; i < AMOUNT_OF_TAUNTS_PER_CLIENT; i++){
                            local_taunts[i] = new AudioTaunt{
                                hash = "EMPTY",
                                name = "EMPTY",
                                audioclip = null,
                                ready_to_play = false,
                                requested_taunt = false
                            };
                        }
                        // hashing + file IO run on a worker thread, clips get created a few
                        // per frame from a GameManager.Update pump; a full 1000-file library
                        // used to block this Awake for ~4 seconds. Pilot-pref resolution and
                        // the sort run when the import completes.
                        StartAsyncImport();
                    }

                    initialized = true;
                }



                // from https://github.com/derhass/olmod/commit/fa897b3384dfd6f228d4c95a385af6b7f37d99b5
                // Fix patching GameManager.Awake
                // When "GameManager.Awake" gets patched, two things happen:
                // 1. GameManager.Version gets set to 0.0.0.0 becuae it internally uses
                //    GetExecutingAssembly() to query the version, but the patched version
                //     lives in another assembly
                // 2. The "anticheat" injection detector is triggered. But that thing
                //    doesn't do anything useful anyway, so disable it
                // return the Assembly which contains Overload.GameManager
                public static Assembly GetOverloadAssembly()
                {
                    return Assembly.GetAssembly(typeof(Overload.GameManager));
                }

                // transpiler
                static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) {
                    // This patches the next call of Server.IsDedicatedServer() call after
                    // a StartCoroutine was called to just pushing true onto the stack instead.
                    // We play safe here becuase other patches might add IsDedicatedServer() calls
                    // to that method, so we search specifically for the first one after
                    // StartCoroutine was called.
                    int state = 0;

                    foreach (var code in codes){
                        // patch GetExecutingAssembly to use GetOverloadAssembly instead
                        if (code.opcode == OpCodes.Call && ((MethodInfo)code.operand).Name == "GetExecutingAssembly"){
                            var method = AccessTools.Method(typeof(MPAudioTaunts_GameManager_Awake), "GetOverloadAssembly");
                            yield return new CodeInstruction(OpCodes.Call, method);
                            continue;
                        }
                        if (state == 0 && code.opcode == OpCodes.Call && ((MethodInfo)code.operand).Name == "StartCoroutine"){
                            state = 1;
                        }
                        else if (state == 1 && code.opcode == OpCodes.Call && ((MethodInfo)code.operand).Name == "IsDedicatedServer"){
                            // this is the first IsDedicatedServer call after StartCoroutine
                            yield return new CodeInstruction(OpCodes.Ldc_I4_1); // push true on the stack instead
                            state = 2; // do not patch other invocations of StartCoroutine
                            continue;
                        }

                        yield return code;
                    }
                }
            }

            [HarmonyPatch(typeof(MenuManager), "ApplySpeakerMode")]
            internal class MPAudioTaunts_MenuManager_ApplySpeakerMode
            {
                private static AudioSpeakerMode speaker_mode;

                static void Prefix()
                {
                    speaker_mode = AudioSettings.speakerMode;
                }

                static void Postfix()
                {
                    if(speaker_mode != AudioSettings.speakerMode)
                        Reload();
                }
            }

            public static void Reload(){

                if (String.IsNullOrEmpty(LocalAudioTauntDirectory)){
                    LocalAudioTauntDirectory = Path.Combine(Application.persistentDataPath, "AudioTaunts");

                    if (!Directory.Exists(LocalAudioTauntDirectory)){
                        Debug.Log("[AudioTaunts] creating the missing local taunt directory: " + LocalAudioTauntDirectory);
                        Directory.CreateDirectory(LocalAudioTauntDirectory);
                    }

                    ExternalAudioTauntDirectory = Path.Combine(LocalAudioTauntDirectory, "external");
                    if (!Directory.Exists(ExternalAudioTauntDirectory)){
                        Debug.Log("[AudioTaunts] creating the missing external taunt directory: " + ExternalAudioTauntDirectory);
                        Directory.CreateDirectory(ExternalAudioTauntDirectory);
                    }
                }

                if (!GameplayManager.IsDedicatedServer()){
                    AClient.taunts = new List<AudioTaunt>();
                    for (int i = 0; i < AClient.asc.Length; i++)
                        AClient.asc[i] = new AudioSourceContainer();

                    // the import resolves the slots from this string when it completes, and it
                    // otherwise still holds the pilot-load snapshot -- every slot picked since
                    // would be dropped by a reload. While an import is still running the
                    // slots read EMPTY, so snapshotting then would wipe the pilot's selection
                    if (!AClient.ImportInProgress)
                        AClient.loaded_local_taunts = AClient.ChainTogetherHashesOfLocalTaunts();

                    for (int i = 0; i < AMOUNT_OF_TAUNTS_PER_CLIENT; i++){
                        AClient.local_taunts[i] = new AudioTaunt
                        {
                            hash = "EMPTY",
                            name = "EMPTY",
                            audioclip = null,
                            ready_to_play = false,
                            requested_taunt = false
                        };
                    }
                    // cancels a still-running import (generation bump) and re-imports the
                    // now-empty list; pilot-pref resolution + sort run at completion.
                    AClient.StartAsyncImport();
                }
            }

            // Imports either the in 'files_to_load' (contains the filenames, not the hashes) specified taunts or all taunts from that directory
            // (under the condition that they have yet to be imported and are valid formats and that their size is not beyond 128 kB) 
            public static void ImportAudioTaunts(string path_to_directory, List<String> files_to_load, bool external_taunts = false)
            {
                // measure the time it takes to load the taunts
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                int imported = 0;

                bool load_all_files = files_to_load == null | files_to_load.Count == 0;
                var fileInfo = new DirectoryInfo(path_to_directory).GetFiles();
                foreach (FileInfo file in fileInfo){
                    if ((files_to_load.Contains(file.Name) | load_all_files)    // if this file or all files got requested
                        && taunts.Find(t => t.name.Equals(file.Name)) == null   // if it hasnt been loaded before
                        && file.Extension.Equals(".ogg")                        // we only accept .ogg for now
                        && file.Length <= AUDIO_TAUNT_SIZE_LIMIT)               // limit the taunt size
                    {

                        AudioTaunt t = new AudioTaunt{
                            hash = CalculateMD5ForFile(Path.Combine(path_to_directory, file.Name)),
                            name = file.Name,
                            audio_taunt_data = File.ReadAllBytes(Path.Combine(path_to_directory, file.Name)),
                            is_data_complete = true,
                            timestamp = Time.frameCount,
                            is_external_taunt = external_taunts,
                            audioclip = LoadAsAudioClip(file.Name, path_to_directory),//Resources.Load<AudioClip>("AudioTaunts/" + file.Name)
                            ready_to_play = true,
                            requested_taunt = false
                        };
                        
                        if (t.name.StartsWith(t.hash)){
                            t.name = t.name.Remove(0, t.hash.Length + 1);
                        }
                        else{
                            File.Move(Path.Combine(path_to_directory, file.Name), Path.Combine(path_to_directory, t.hash + "-" + file.Name));
                        }

                        taunts.Add(t);
                        imported++;
                    }
                }
                stopwatch.Stop();

                Debug.LogFormat("[AudioTaunts] imported {0} taunts from {1} in {2:F2}s",
                    imported, path_to_directory, stopwatch.ElapsedMilliseconds / 1000f);
            }

            // ==== asynchronous full-directory import ====
            // ImportAudioTaunts above stays for the network path (a handful of just-downloaded
            // files mid-lobby); the full-library imports on startup and audio reset go through
            // this pipeline instead: directory scan, MD5 and File.ReadAllBytes on a worker
            // thread, AudioClip creation (WWW, main-thread-only) a few at a time per frame from
            // a GameManager.Update pump. A generation counter cancels a superseded import when
            // an audio reset triggers a new one mid-run.

            private class PendingTauntFile
            {
                public int gen;
                public string directory;
                public string file_name;      // on-disk name (hash-prefixed)
                public string display_name;   // name without the hash prefix
                public string hash;
                public byte[] data;
                public bool external;
            }

            private class InflightClipLoad
            {
                public PendingTauntFile rec;
                public WWW www;
            }

            private const int IMPORT_MAX_INFLIGHT_CLIPS = 4;
            private static readonly object import_lock = new object();
            private static readonly Queue<PendingTauntFile> import_ready = new Queue<PendingTauntFile>();
            private static volatile int import_generation = 0;
            private static volatile bool import_scan_done = false;
            private static readonly List<InflightClipLoad> import_inflight = new List<InflightClipLoad>();
            private static Stopwatch import_watch;
            private static int import_count;
            private static int import_skipped;

            // the taunt slots read EMPTY while this is true, so the pilot file must not be
            // written from them
            public static bool ImportInProgress { get { return import_watch != null; } }

            public static void StartAsyncImport()
            {
                int gen;
                lock (import_lock)
                {
                    gen = ++import_generation;
                    import_ready.Clear();
                    import_scan_done = false;
                }
                import_inflight.Clear();  // main thread only, stale entries are gen-checked anyway
                import_count = 0;
                import_skipped = 0;
                import_watch = Stopwatch.StartNew();

                // display names already produced -- mirrors the original import's dedup
                // (skip a file whose on-disk name equals any existing t.name), which is also
                // what keeps a plain "X.ogg" from being hash-renamed onto an existing
                // "<hash>-X.ogg": the prefixed twin sorts first and claims the name
                var known = new HashSet<string>();
                foreach (var t in taunts)
                    known.Add(t.name);

                string localDir = LocalAudioTauntDirectory;
                string extDir = ExternalAudioTauntDirectory;
                var thread = new Thread(() => ScanTauntFiles(gen, localDir, extDir, known));
                thread.IsBackground = true;
                thread.Name = "olmod-taunt-import";
                thread.Start();
            }

            private static void ScanTauntFiles(int gen, string localDir, string extDir, HashSet<string> known)
            {
                try
                {
                    ScanTauntDirectory(gen, localDir, false, known);
                    ScanTauntDirectory(gen, extDir, true, known);
                }
                catch (Exception ex)
                {
                    Debug.LogErrorFormat("[AudioTaunts] async import scan failed: {0}", ex);
                }
                finally
                {
                    lock (import_lock)
                    {
                        if (gen == import_generation)
                            import_scan_done = true;
                    }
                }
            }

            private static void ScanTauntDirectory(int gen, string directory, bool external, HashSet<string> known)
            {
                foreach (FileInfo file in new DirectoryInfo(directory).GetFiles())
                {
                    if (gen != import_generation)
                        return;
                    if (!file.Extension.Equals(".ogg") || file.Length > AUDIO_TAUNT_SIZE_LIMIT || known.Contains(file.Name))
                        continue;

                    try
                    {
                        string hash = CalculateMD5ForFile(Path.Combine(directory, file.Name));
                        string diskName = file.Name;
                        string displayName;
                        if (diskName.StartsWith(hash))
                        {
                            displayName = diskName.Remove(0, hash.Length + 1);
                        }
                        else
                        {
                            displayName = diskName;
                            diskName = hash + "-" + file.Name;
                            // same hash means a byte-identical twin this scan imports on its
                            // own, and File.Move onto an existing target throws on Windows
                            if (File.Exists(Path.Combine(directory, diskName)))
                            {
                                Interlocked.Increment(ref import_skipped);
                                continue;
                            }
                            File.Move(Path.Combine(directory, file.Name), Path.Combine(directory, diskName));
                        }
                        byte[] data = File.ReadAllBytes(Path.Combine(directory, diskName));
                        known.Add(displayName);

                        lock (import_lock)
                        {
                            if (gen != import_generation)
                                return;
                            import_ready.Enqueue(new PendingTauntFile
                            {
                                gen = gen,
                                directory = directory,
                                file_name = diskName,
                                display_name = displayName,
                                hash = hash,
                                data = data,
                                external = external
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        // one unreadable/unmovable file must not abort the rest of the library,
                        // but silent skips make a wholesale failure look like an empty folder
                        Interlocked.Increment(ref import_skipped);
                        Debug.LogWarning("[AudioTaunts] async import: skipping " + file.Name + ": " + ex.Message);
                    }
                }
            }

            // runs every frame on the main thread (GameManager.Update postfix)
            public static void PumpAsyncImport()
            {
                if (import_watch == null)
                    return;

                while (import_inflight.Count < IMPORT_MAX_INFLIGHT_CLIPS)
                {
                    PendingTauntFile rec = null;
                    lock (import_lock)
                    {
                        if (import_ready.Count > 0)
                            rec = import_ready.Dequeue();
                    }
                    if (rec == null)
                        break;
                    if (rec.gen != import_generation)
                        continue;
                    import_inflight.Add(new InflightClipLoad
                    {
                        rec = rec,
                        www = new WWW("file:///" + Path.Combine(rec.directory, rec.file_name))
                    });
                }

                for (int i = import_inflight.Count - 1; i >= 0; i--)
                {
                    InflightClipLoad load = import_inflight[i];
                    if (!load.www.isDone)
                        continue;
                    import_inflight.RemoveAt(i);
                    if (load.rec.gen != import_generation)
                        continue;
                    if (!string.IsNullOrEmpty(load.www.error))
                    {
                        Debug.LogError("[AudioTaunts] cannot load the taunt clip: " + load.www.error + " : " + load.rec.file_name);
                        continue;
                    }
                    // true duplicate only (a network import may have raced us); display names
                    // alone collide legitimately -- every external "<hash>.ogg" strips to "ogg"
                    if (taunts.Find(t => t.hash.Equals(load.rec.hash) && t.name.Equals(load.rec.display_name)) != null)
                        continue;
                    taunts.Add(new AudioTaunt
                    {
                        hash = load.rec.hash,
                        name = load.rec.display_name,
                        audio_taunt_data = load.rec.data,
                        is_data_complete = true,
                        timestamp = Time.frameCount,
                        is_external_taunt = load.rec.external,
                        audioclip = load.www.GetAudioClip(true, false),
                        ready_to_play = true,
                        requested_taunt = false
                    });
                    import_count++;
                }

                bool done;
                lock (import_lock)
                {
                    done = import_scan_done && import_ready.Count == 0;
                }
                if (done && import_inflight.Count == 0)
                {
                    import_watch.Stop();
                    Debug.Log(string.Format("[AudioTaunts] imported {0} taunts asynchronously over {1:F2}s, total: {2}, skipped: {3}",
                        import_count, import_watch.ElapsedMilliseconds / 1000f, taunts.Count, import_skipped));
                    import_watch = null;
                    LoadLocalAudioTauntsFromPilotPrefs();
                    taunts.Sort((x, y) => x.name.CompareTo(y.name));
                }
            }

            [HarmonyPatch(typeof(GameManager), "Update")]
            class MPAudioTaunts_AsyncImport_GameManager_Update
            {
                static void Postfix()
                {
                    PumpAsyncImport();
                }
            }

            // finds the next client audiotaunt in the taunts list.
            // this is necessary because the taunts list also contains the taunts from other players
            // which would clutter the selection if we were to just select the next index
            public static int GetNextSelectableAudioTauntIndex(int start_index, int direction)
            {
                // nonsensical parameters
                if (start_index < 0 | start_index >= taunts.Count | direction > 1 | direction < -1)
                    return -1;

                // iterate through the taunts till you find another local taunt
                int tmp_index = start_index;
                int count = MPAudioTaunts.AClient.taunts.Count;
                while (MPAudioTaunts.AClient.taunts[tmp_index].is_external_taunt
                    | MPAudioTaunts.AClient.IsContainedInLocalTaunts(MPAudioTaunts.AClient.taunts[tmp_index].hash))
                {
                    tmp_index += direction;
                    // check wether the bounds got violated
                    if (tmp_index < 0)
                        tmp_index = MPAudioTaunts.AClient.taunts.Count - 1;
                    else if (tmp_index >= MPAudioTaunts.AClient.taunts.Count)
                        tmp_index = 0;

                    count--;
                    if (count <= 0) break;
                }

                if (count > 0)
                {
                    return tmp_index;
                }
                return -1;
            }

            public static int GetNextIndexThatStartsWithStringSequence(int start_index, int current_index,string start_sequence){
                if (start_index < 0 | start_index >= taunts.Count | current_index < 0 | current_index >= taunts.Count)
                    return -1;

                if (taunts[current_index].name.ToUpper().StartsWith(start_sequence.ToUpper())){
                    int next_index = GetNextSelectableAudioTauntIndex(current_index, 1);
                    if(taunts[next_index].name.ToUpper().StartsWith(start_sequence.ToUpper()))
                    return next_index;
                }

                for (int i = 0; i < taunts.Count; i++)
                    if(taunts[i].name.ToUpper().StartsWith(start_sequence.ToUpper()) & !taunts[i].is_external_taunt)
                        return i;

                return -1;
            }

            public static bool IsContainedInLocalTaunts(string hash){
                if (String.IsNullOrEmpty(hash))
                    return false;

                bool isContained = false;
                for (int j = 0; j < local_taunts.Length; j++){
                    if (local_taunts[j].hash.Equals(hash)){
                        isContained = true;
                        break;
                    }
                }
                return isContained;
            }

            private static AudioClip LoadAsAudioClip(string filename, string directory_path){
                //Debug.Log("  Attempting to load file as audio clip: " + filename);
                string path = Path.Combine(directory_path, filename);
                if (path != null){
                    WWW www = new WWW("file:///" + path);
                    while (!www.isDone) { }
                    if (string.IsNullOrEmpty(www.error)){
                        return www.GetAudioClip(true, false);
                    }
                    else Debug.LogError("[AudioTaunts] LoadAsAudioClip failed: " + www.error + " : " + filename + " : " + directory_path);
                }
                return null;
            }

            public static bool IsKeyCodeAlreadyUsed(int keycode){
                for (int i = 0; i < keybinds.Length; i++){
                    if (keybinds[i] == keycode)
                        return true;
                }
                return false;
            }

            // used to calculate a hash for each audio taunt file to avoid filename collisions
            public static string CalculateMD5ForFile(string path_to_file){
                using (var md5 = MD5.Create()){
                    using (var stream = File.OpenRead(path_to_file)){
                        var hash = md5.ComputeHash(stream);
                        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }
            }

            // Splits the hashes that get stored as a single string in loaded_local_taunts and finds the corresponding taunts
            // to populate the audio taunt slots. '/' is used as the seperator of the slots
            public static void LoadLocalAudioTauntsFromPilotPrefs(){
                string[] file_hashes = loaded_local_taunts.Split('/');
                int index = 0;
                foreach (string hash in file_hashes){
                    if (index < AMOUNT_OF_TAUNTS_PER_CLIENT){
                        AudioTaunt at = taunts.Find(t => t.hash.Equals(hash) && !t.is_external_taunt);
                        if (at == null){
                            at = new AudioTaunt{
                                hash = "EMPTY",
                                name = "EMPTY",
                                audioclip = null,
                                ready_to_play = false
                            };
                        }
                        local_taunts[index] = at;
                    }
                    index++;
                }
            }

            // the inverse of LoadLocalAudioTauntsFromPilotPrefs, EMPTY placeholders included
            // so a hash keeps its slot across the round trip
            public static string ChainTogetherHashesOfLocalTaunts(){
                string[] hashes = new string[AMOUNT_OF_TAUNTS_PER_CLIENT];
                for (int i = 0; i < AMOUNT_OF_TAUNTS_PER_CLIENT; i++)
                    hashes[i] = local_taunts[i] != null && local_taunts[i].hash != null ? local_taunts[i].hash : "EMPTY";

                return string.Join("/", hashes);
            }

            public static bool IsPlayerMuted(string player_name)
            {
                if (string.IsNullOrEmpty(player_name))
                    return false;

                player_name = player_name.ToUpper();
                foreach(PlayerLobbyData pld in NetworkMatch.m_players.Values)
                {
                    if (pld.m_name.ToUpper().Equals(player_name) & ExtendedConfig.Section_AudiotauntMutedPlayers.ids.Contains(pld.m_player_id))
                        return true;
                }
                return false;
            }

            // the clip_id should only be added when the activation of this audioclip should be shared in a multiplayer match
            public static void PlayAudioTauntFromAudioclip(AudioClip audioClip, string player_name, string clip_id = null)
            {
                if (audio_taunt_volume == 0 | audioClip == null | !active | IsPlayerMuted(player_name))
                    return;

                int index = -1;
                for (int i = 0; i < asc.Length; i++)
                {
                    if (asc[i].source == null)
                        asc[i].source = new GameObject().AddComponent<AudioSource>();

                    if (!asc[i].source.isPlaying) index = i;
                }

                if (index == -1)
                {
                    Debug.LogWarning("[AudioTaunts] cannot play the taunt, all audio sources are occupied");
                    return;
                }

                if (player_name != null)
                    asc[index].player_name = player_name;
                else
                    asc[index].player_name = "EMPTY";


                asc[index].source.enabled = true;
                asc[index].source.priority = 0;
                asc[index].source.clip = audioClip;
                asc[index].source.volume = audio_taunt_volume / 100f;
                asc[index].source.bypassReverbZones = true;
                asc[index].source.reverbZoneMix = 0.001f;
                asc[index].source.PlayScheduled(AudioSettings.dspTime);
                asc[index].source.SetScheduledEndTime(AudioSettings.dspTime + TAUNT_PLAYTIME);

                if (GameplayManager.IsMultiplayer
                    && clip_id != null
                    && Client.GetClient() != null
                    && (NetworkMatch.GetMatchState() == MatchState.PLAYING | NetworkMatch.GetMatchState() == MatchState.LOBBY))
                {
                    //Debug.Log("Client -> Server: Share the activation of " + clip_id);
                    Client.GetClient().SendByChannel(MessageTypes.MsgPlayAudioTaunt,
                                        new PlayAudioTaunt
                                        {
                                            hash = clip_id,
                                            sender_name = GameManager.m_local_player.m_mp_name
                                        },0);
                }
            }

            public static float[] calculateFrequencyBand()
            {
                float[] freqBand = new float[8];
                for (int z = 0; z < asc.Length; z++)
                {

                    if (asc[z].source != null && asc[z].source.isPlaying)
                    {
                        float[] samples = new float[512];

                        asc[z].source.GetSpectrumData(samples, 0, FFTWindow.Rectangular);

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

            
            [HarmonyPatch(typeof(UIManager), "PushedDir")]
            class MPAudioTaunts_UIManager_PushedDir
            {
                static bool Prefix(ref bool __result)
                {
                    if (MenuManager.m_menu_state == MenuState.MP_OPTIONS & !UIManager.PushedSelect(100) & MenuManager.m_menu_micro_state == 3 & UIManager.m_menu_selection > 15 & UIManager.m_menu_selection < 22)
                    {
                        if (Input.inputString.ToUpper().Contains('A') | Input.inputString.ToUpper().Contains('D'))//Input.GetKeyDown((KeyCode)'A') | Input.GetKeyDown((KeyCode)'D'))
                        {
                            __result = false;
                            return false; 
                        }
                        else
                            return true;

                    }
                    return true;
                }
            }

            // Send the file names of your audio taunts to the server when entering a game // Client OnAcceptedToLobby OnMatchStart
            [HarmonyPatch(typeof(Client), "OnAcceptedToLobby")]
            class MPAudioTaunts_Client_OnAcceptedToLobby
            {
                static void Postfix()
                {
                    if (GameplayManager.IsDedicatedServer() | Client.GetClient() == null | !active)
                        return;


                    AServer.server_supports_audiotaunts = false;

                    string fileHashes = "";
                    for (int i = 0; i < local_taunts.Length; i++)
                    {
                        if (local_taunts[i].hash != null && !local_taunts[i].hash.Equals("EMPTY"))
                        {
                            fileHashes += local_taunts[i].hash;
                            if (i != local_taunts.Length - 1)
                                fileHashes += "/";
                        }
                    }

                    Client.GetClient().SendByChannel(MessageTypes.MsgShareAudioTauntIdentifiers,
                        new ShareAudioTauntIdentifiers
                        {
                            hashes = fileHashes
                        },0);
                }
            }

            


            
            [HarmonyPatch(typeof(UIElement), "DrawPlayerName")]
            class MPAudioTaunts_UIElement_DrawPlayerName
            {
                public static List<Vector2> curve1 = CalculatePointsOnASphericalCurve(new Vector2(12, -3), new Vector2(12, 3), new Vector2(3, 0), 5);
                public static List<Vector2> curve2 = CalculatePointsOnASphericalCurve(new Vector2(15, -5.5f), new Vector2(15, 5.5f), new Vector2(3, 0), 7);
                public static List<Vector2> curve3 = CalculatePointsOnASphericalCurve(new Vector2(18, -8), new Vector2(18, 8), new Vector2(3, 0), 9);

                static void Postfix(UIElement __instance, Vector2 pos, PlayerLobbyData pld, bool bg_bar, float highlight_width, float name_offset, float max_width = -1f)
                {
                    if (!active | string.IsNullOrEmpty(pld.m_player_id) | pld.m_name.ToUpper().Equals(GameManager.m_local_player.m_mp_name.ToUpper()))
                        return;

                    bool muted = ExtendedConfig.Section_AudiotauntMutedPlayers.ids.Contains(pld.m_player_id);
                    DrawMuteIcon(__instance, pos + (Vector2.right * 20f) + Vector2.right * (-name_offset - 5f), pld, muted); // 20
                }

                public static void DrawMuteIcon(UIElement uie, Vector2 pos, PlayerLobbyData pld, bool muted)
                {
                    uie.TestMouseInRect(pos + new Vector2(5f, 0f), 23f, 15f, 20000 + pld.m_id, true);
                    bool highlighted = UIManager.m_menu_selection == 20000 + pld.m_id;

                    Color color = Color.green;
                    if (muted) color = Color.red;
                    if (highlighted) color = Color.white;
                    
                    if (!muted)
                    {
                        for (int i = 1; i < curve1.Count; i++)
                            UIManager.DrawQuadCenterLine(pos + curve1[i - 1], pos + curve1[i], 0.5f, 0f, color, 4);

                        for (int i = 1; i < curve2.Count; i++)
                            UIManager.DrawQuadCenterLine(pos + curve2[i - 1], pos + curve2[i], 0.5f, 0f, color, 4);
                        
                        for (int i = 1; i < curve3.Count; i++)
                            UIManager.DrawQuadCenterLine(pos + curve3[i - 1], pos + curve3[i], 0.5f, 0f, color, 4);
                    }
                    else
                    {
                        // 5.497790f = 315°, 0.785398f = 45°
                        UIManager.DrawSpriteUIRotated(pos + new Vector2(12.4f, 0f), 0.15f, 0.15f, 5.497790f, color, 0.5f, 41);
                        UIManager.DrawSpriteUIRotated(pos + new Vector2(12.4f, 0f), 0.15f, 0.15f, 0.785398f, color, 0.5f, 41);
                    }

                    // 81 = triangle, 131 = cross, 199 = block, 41 = shortest_line, [6,7,34] = border, 11 = clean block
                    UIManager.DrawSpriteUI(pos, 0.18f, 0.18f, color, 0.5f, 199);
                    pos.x += 3f;
                    UIManager.DrawSpriteUI(pos, 0.18f, 0.18f, color, 0.5f, 81);
                }

                public static List<Vector2> CalculatePointsOnASphericalCurve(Vector2 start, Vector2 end, Vector2 center, int amt_lines)
                {
                    List<Vector2> points = new List<Vector2>();
                    points.Add(start);

                    float radius = (float)Math.Sqrt(Math.Pow(start.x - center.x, 2) + Math.Pow(start.y - center.y, 2));
                    float angle1 = (float)Math.Atan2(start.y - center.y, start.x - center.x);
                    float angle2 = (float)Math.Atan2(end.y - center.y, end.x - center.x);
                    float angleDelta = (angle2 - angle1) / amt_lines;

                    for (int i = 0; i < amt_lines; i++)
                    {
                        float angle = angle1 + i * angleDelta;
                        float x = center.x + radius * (float)Math.Cos(angle);
                        float y = center.y + radius * (float)Math.Sin(angle);
                        Vector2 point = new Vector2(x, y);
                        points.Add(point);
                    }
                    points.Add(end);
                    return points;
                }
            }

            

            [HarmonyPatch(typeof(MenuManager), "MpPreMatchMenuUpdate")]
            class MPAudioTaunts_MenuManager_MpPreMatchMenuUpdate
            {
                static void Postfix()
                {
                    foreach(PlayerLobbyData p in NetworkMatch.m_players.Values)
                    {
                        if(UIManager.PushedSelect(100) && UIManager.m_menu_selection == 20000 + p.m_id)
                        {
                            if (string.IsNullOrEmpty(p.m_player_id))
                                continue;

                            if (ExtendedConfig.Section_AudiotauntMutedPlayers.ids.Contains(p.m_player_id))
                                ExtendedConfig.Section_AudiotauntMutedPlayers.ids.Remove(p.m_player_id);
                            else
                                ExtendedConfig.Section_AudiotauntMutedPlayers.ids.Add(p.m_player_id);
                            MenuManager.PlaySelectSound(1f);
                        }
                    }
                }
            }



            [HarmonyPatch(typeof(GameManager), "Update")]
            class MPAudioTaunts_GameManager_Update
            {
                public static void Postfix()
                {
                    if (!GameplayManager.IsDedicatedServer() && active)
                    {
                        // Checks wether the slot for uploading a taunt to the server is free and if so queues the next potential upload
                        if (!MPAudioTaunts_Client_RegisterHandlers.isUploading && MPAudioTaunts_Client_RegisterHandlers.queued_uploads.Count > 0)
                        {
                            GameManager.m_gm.StartCoroutine(MPAudioTaunts_Client_RegisterHandlers.UploadAudioTauntToServer(MPAudioTaunts_Client_RegisterHandlers.queued_uploads[0]));
                            MPAudioTaunts_Client_RegisterHandlers.queued_uploads.Remove(MPAudioTaunts_Client_RegisterHandlers.queued_uploads[0]);
                        }

                        //uConsole.Log("Menustate: "+ MenuManager.m_menu_state.ToString());

                        // Checks for Keyinput on this client for triggering the playing of an audio taunt
                        if (!uConsole.IsOn() & AServer.server_supports_audiotaunts & !PlayerShip.m_typing_in_chat & (GameplayManager.IsMultiplayerActive | MenuManager.m_menu_state == MenuState.MP_PRE_MATCH_MENU))
                        {
                            if (remaining_cooldown > 0f)
                                remaining_cooldown -= Time.unscaledDeltaTime;

                            for (int i = 0; i < AMOUNT_OF_TAUNTS_PER_CLIENT; i++)
                            {
                                if (remaining_cooldown <= 0f && keybinds[i] > 0 && (Input.GetKeyDown((KeyCode)keybinds[i]) && local_taunts[i].audioclip != null || Controls.JustPressed((CCInput)(61+i))))
                                {
                                    remaining_cooldown = DEFAULT_TAUNT_COOLDOWN;
                                    PlayAudioTauntFromAudioclip(local_taunts[i].audioclip, GameManager.m_local_player.m_mp_name, local_taunts[i].hash);
                                    break;
                                }
                            }
                        }

                        if (display_audio_spectrum)
                        {
                            spectrum_update_cooldown -= Time.unscaledDeltaTime;
                            if (spectrum_update_cooldown <= 0f)
                            {
                                for (int i = 0; i < asc.Length; i++)
                                {
                                    asc[i].UpdateFrequencyBand();
                                }
                                spectrum_update_cooldown = DEFAULT_SPECTRUM_UPDATE_COOLDOWN;
                            }
                        }

                    }
                }
            }


            public static void DrawVisualIndicator(UIElement __instance)
            {
                // Draws an indicator of who is currently playing an audiotaunt
                if ((NetworkMatch.GetMatchState() == MatchState.PLAYING | NetworkMatch.GetMatchState() == MatchState.LOBBY) & MenuManager.m_menu_state != MenuState.MP_OPTIONS)
                {
                    //uConsole.Log(UIManager.m_mouse_pos.x + " : " + UIManager.m_mouse_pos.y);

                    int position = -1;
                    for (int i = 0; i < asc.Length; i++)
                    {
                        if (asc[i].source == null)
                            asc[i].source = new GameObject().AddComponent<AudioSource>();

                        if (asc[i].source != null & asc[i].player_name != null && asc[i].source.isPlaying)
                        {
                            position++;


                            // draw an indicator for actively playing audio taunts
                            Vector2 pos = new Vector2(-600, -40);

                            // shift the element down depending on how many audio sources are playing at the same time
                            pos.y += position * 32;



                            // draw the player name
                            __instance.DrawStringSmall(asc[i].player_name + (display_audio_spectrum ? ":" : ""), pos, 0.32f, StringOffset.LEFT, UIManager.m_col_ui1, 1f, -1f);

                            // draw the frequency band
                            if (display_audio_spectrum)
                            {
                                // check wether there is a non zero change to the default values
                                bool should_display = false;
                                for (int x = 0; x < 8; x++)
                                {
                                    if (asc[i].frequency_band[x] != -1)
                                        should_display = true;
                                }

                                if (should_display)
                                {
                                    pos.x += UIManager.GetStringWidth(asc[i].player_name + ":", 8f);
                                    pos.y += 3f;
                                    for (int x = 0; x < 8; x++)
                                    {
                                        // new Vector2(1.7f, 1f)
                                        UIManager.DrawBarVertical(pos, new Vector2(2f, 1.2f), asc[i].frequency_band[x] * 20f, Color.green * 0.7f, 199);//UIManager.DrawQuadBarVertical(pos, 6f, 1f, freqBand[i] * 50f, Color.yellow, 199); //(pos, new Vector2(pos.x, -freqBand[i] * 200f), 1f, 0f, Color.yellow, 4);
                                        pos.x += 5.5f;
                                    }
                                }
                            }




                        }
                    }
                }
            }

            [HarmonyPatch(typeof(UIElement), "DrawHUD")]
            class MPAudioTaunts_UIElement_DrawHUD
            {
                static void Postfix(UIElement __instance)
                {
                    DrawVisualIndicator(__instance);
                }

            }

            [HarmonyPatch(typeof(UIElement), "DrawMpPreMatchMenu")]
            class MPAudioTaunts_UIElement_DrawMpPreMatchMenu
            {
                static void Postfix(UIElement __instance)
                {
                    DrawVisualIndicator(__instance);
                }
            }


            [HarmonyPatch(typeof(UIElement), "DrawMpMiniScoreboard")]
            class MPDeathReview_UIElement_DrawMpMiniScoreboard
            {
                static void Postfix(UIElement __instance)
                {
                    if (!MPDeathReview.stickyDeathReview | !MPDeathReview.showDeathReviewDetails)
                    {
                        DrawVisualIndicator(__instance);
                    }
                }
            }


            [HarmonyPatch(typeof(Client), "RegisterHandlers")]
            class MPAudioTaunts_Client_RegisterHandlers
            {

                public static bool isUploading = false;
                public static List<AudioTaunt> queued_uploads = new List<AudioTaunt>();

                private static void OnShareAudioTauntIdentifiers(NetworkMessage rawMsg)
                {
                    if (!active) return;

                    var msg = rawMsg.ReadMessage<ShareAudioTauntIdentifiers>();
                    List<string> file_hashes = msg.hashes.Split('/').ToList();
                    int known_before = match_taunts.Count;

                    ImportAudioTaunts(ExternalAudioTauntDirectory, file_hashes);

                    foreach (string hash in file_hashes)
                    {

                        // if the taunt has been downloaded before then load it locally and add it to the game taunts
                        if (!string.IsNullOrEmpty(hash) & !match_taunts.ContainsKey(hash))
                        {

                            AudioTaunt taunt = taunts.Find(t => t.hash.Equals(hash));
                            if (taunt != null)
                            {
                                match_taunts.Add(hash, taunt);
                            }
                            else
                            {
                                if (Client.GetClient() == null)
                                {
                                    Debug.LogError("[AudioTaunts] client register handlers: no client?");
                                    continue;
                                }
                                // to prepare for an incoming answer to our request we create a context to collect the bytes from the server
                                match_taunts.Add(hash, new AudioTaunt
                                {
                                    hash = hash,
                                    name = "",
                                    audio_taunt_data = new byte[AUDIO_TAUNT_SIZE_LIMIT],
                                    is_data_complete = false,
                                    timestamp = -1,
                                    ready_to_play = false,
                                    requested_taunt = true
                                });

                                Client.GetClient().SendByChannel(MessageTypes.MsgRequestAudioTaunt,
                                    new RequestAudioTaunt
                                    {
                                        hash = hash
                                    },0);

                            }

                        }
                    }

                    // The server splits a long announcement over several messages, so a run of
                    // these lines belongs to ONE roster change: their hash counts must add up to
                    // the server's "announced N hashes", and "match total" must end at that N.
                    Debug.Log("[AudioTaunts] announcement: " + file_hashes.Count + " hashes, "
                        + msg.hashes.Length + " chars, " + (match_taunts.Count - known_before)
                        + " new; match total " + match_taunts.Count);
                }

                private static void OnAudioTauntRequest(NetworkMessage rawMsg)
                {
                    if (!active) return;


                    var msg = rawMsg.ReadMessage<RequestAudioTaunt>();
                    Debug.Log("[AudioTaunts.Client]  Server requested audiotaunt: " + msg.hash);

                    // find the audiotaunt data that the server requested
                    int index = -1;
                    for (int i = 0; i < AMOUNT_OF_TAUNTS_PER_CLIENT; i++)
                    {
                        if (local_taunts[i].hash.Equals(msg.hash))
                            index = i;
                    }

                    // start the upload if we actually have the data
                    if (index != -1)
                    {
                        if (local_taunts[index].is_data_complete)
                        {
                            Debug.Log("[AudioTaunts]  starting the upload or putting it in the queue: " + msg.hash);
                            if (!isUploading)
                                GameManager.m_gm.StartCoroutine(UploadAudioTauntToServer(new AudioTaunt
                                {
                                    hash = msg.hash,
                                    audio_taunt_data = local_taunts[index].audio_taunt_data
                                }));
                            else
                                queued_uploads.Add(new AudioTaunt
                                {
                                    hash = msg.hash,
                                    audio_taunt_data = local_taunts[index].audio_taunt_data
                                });
                        }
                        else
                        {
                            Debug.LogWarning("[AudioTaunts] local taunt byte data is declared incomplete");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[AudioTaunts] the requested taunt is not part of the selected local taunts");
                    }
                }

                public static IEnumerator UploadAudioTauntToServer(AudioTaunt data)
                {
                    isUploading = true;
                    Debug.Log("[AudioTaunts] Started uploading an AudioTaunt to server");
                    int _packet_id = 0;
                    int position = 0;
                    while (position < data.audio_taunt_data.Length)
                    {
                        // write a packet
                        int index = 0;
                        byte[] to_send = new byte[PACKET_PAYLOAD_SIZE];
                        while (index < PACKET_PAYLOAD_SIZE & ((position + index) < data.audio_taunt_data.Length))
                        {
                            to_send[index] = data.audio_taunt_data[position + index];
                            index++;
                        }

                        AudioTauntPacket packet = new AudioTauntPacket
                        {
                            filesize = data.audio_taunt_data.Length,
                            amount_of_bytes_sent = index,
                            hash = data.hash,
                            packet_id = _packet_id,
                            data = to_send,//Convert.ToBase64String(to_send)
                        };

                        Client.GetClient().connection.SendByChannel(MessageTypes.MsgAudioTauntPacket, packet,0);
                       
                        yield return delay;
                        position += PACKET_PAYLOAD_SIZE;
                        _packet_id++;
                    }
                    uConsole.Log("[AudioTaunts]    completed the upload for " + data.hash);
                    isUploading = false;
                }

                private static void OnAudioTauntPacket(NetworkMessage rawMsg)
                {
                    if (!active)
                        return;

                    //Debug.Log("[AudioTaunts]        Received a Fragment of an Audiotaunt");

                    try
                    {
                        var msg = rawMsg.ReadMessage<AudioTauntPacket>();
                        if (match_taunts.ContainsKey(msg.hash))
                        {
                            // initialise the array with the correct size
                            if (match_taunts[msg.hash].timestamp == -1)
                            {
                                match_taunts[msg.hash].timestamp = Time.frameCount;
                                match_taunts[msg.hash].audio_taunt_data = new byte[msg.filesize];
                            }

                            if (match_taunts[msg.hash].received_packets == null)
                                match_taunts[msg.hash].received_packets = new List<int>();

                            if (!match_taunts[msg.hash].received_packets.Contains(msg.packet_id))
                            {
                                // add data of the fragment to the context
                                int startindex = msg.packet_id * PACKET_PAYLOAD_SIZE;
                                for (int i = 0; i < msg.amount_of_bytes_sent; i++)
                                {
                                    match_taunts[msg.hash].audio_taunt_data[startindex + i] = msg.data[i];
                                }
                                match_taunts[msg.hash].received_packets.Add(msg.packet_id);
                                //Debug.Log("     Added the data of the fragment");

                                // if this completes the data then check wether there are unanswered requests
                                if (match_taunts[msg.hash].received_packets.Count * PACKET_PAYLOAD_SIZE >= msg.filesize)
                                {
                                    //DEBUG.MARKER Debug.Log("     This Fragment completed the audio taunt data");
                                    match_taunts[msg.hash].is_data_complete = true;



                                    // write the byte data to a file
                                    string path = Path.Combine(ExternalAudioTauntDirectory, msg.hash + ".ogg");

                                    File.WriteAllBytes(path, match_taunts[msg.hash].audio_taunt_data);

                                    match_taunts[msg.hash].audioclip = LoadAsAudioClip(msg.hash + ".ogg", ExternalAudioTauntDirectory);
                                    match_taunts[msg.hash].ready_to_play = true;


                                }
                            }
                            else
                            {
                                //DEBUG.MARKER Debug.Log("     This Fragment is a duplicate. packet_id: " + msg.packet_id);
                            }

                        }
                        else
                        {
                            //DEBUG.MARKER Debug.LogWarning("[AudioTaunts] no context for this taunt, ignoring the fragment from connection " + rawMsg.conn.connectionId);
                        }
                    }
                    catch (FormatException fe)
                    {
                        Debug.LogError("[AudioTaunts] fragment payload is not a Base64 string: " + fe);
                        return;
                    }


                }

                private static void OnPlayAudioTaunt(NetworkMessage rawMsg)
                {
                    if (!active) return;

                    var msg = rawMsg.ReadMessage<PlayAudioTaunt>();

                    if (match_taunts.ContainsKey(msg.hash) && match_taunts[msg.hash].ready_to_play)
                    {
                        PlayAudioTauntFromAudioclip(match_taunts[msg.hash].audioclip, msg.sender_name);
                    }
                    // If someone tries to play a taunt that we dont even know existed before then we missed the ShareAudioTauntIdentifiers packet
                    else if (!match_taunts.ContainsKey(msg.hash))
                    {
                        Debug.Log("[AudioTaunts] Client was unaware of a taunts existence. Resending MsgShareAudioTauntIdentifiers!");
                        string fileHashes = "";
                        for (int i = 0; i < local_taunts.Length; i++)
                        {
                            if (local_taunts[i].hash != null && !local_taunts[i].hash.Equals("EMPTY"))
                            {
                                fileHashes += local_taunts[i].hash;
                                if (i != local_taunts.Length - 1)
                                    fileHashes += "/";
                            }
                        }

                        Client.GetClient().SendByChannel(MessageTypes.MsgShareAudioTauntIdentifiers,
                            new ShareAudioTauntIdentifiers
                            {
                                hashes = fileHashes
                            }, 0);
                    }
                }



                static void Postfix()
                {
                    if (GameplayManager.IsDedicatedServer())
                        return;

                    if (Client.GetClient() == null)
                    {
                        Debug.LogError("[AudioTaunts] cannot set up the client message handlers");
                        return;
                    }

                    Client.GetClient().RegisterHandler(MessageTypes.MsgShareAudioTauntIdentifiers, OnShareAudioTauntIdentifiers);
                    Client.GetClient().RegisterHandler(MessageTypes.MsgRequestAudioTaunt, OnAudioTauntRequest);
                    Client.GetClient().RegisterHandler(MessageTypes.MsgAudioTauntPacket, OnAudioTauntPacket);
                    Client.GetClient().RegisterHandler(MessageTypes.MsgPlayAudioTaunt, OnPlayAudioTaunt);
                }
            }
        }















        // Responsible for sharing the required audiotaunts and their activation across clients
        public static class AServer
        {
            public static bool active = true;
            public static bool server_supports_audiotaunts = false;

            public static Dictionary<string, AudioTaunt> match_taunts = new Dictionary<string, AudioTaunt>();
            public static Dictionary<string, AudioTaunt> taunt_buffer = new Dictionary<string, AudioTaunt>(); // this holds taunts whose data has been uploaded 


            [HarmonyPatch(typeof(Server), "OnDisconnect")]
            class MPAudioTaunts_Server_OnDisconnect
            {
                static void Prefix(NetworkMessage msg)
                {
                    if (msg == null || msg.conn == null)
                    {
                        Debug.LogWarning("[AudioTaunts] OnDisconnect without a network message object");
                        return;
                    }

                    // remove taunts where this client is the sole provider from
                    Dictionary<string, AudioTaunt> new_match_taunts = new Dictionary<string, AudioTaunt>();
                    foreach (var item in match_taunts)
                    {
                        bool sole_provider = true;
                        foreach (int provider in match_taunts[item.Key].providing_clients)
                        {
                            if (provider != msg.conn.connectionId)
                                sole_provider = false;
                        }

                        if (!sole_provider & !new_match_taunts.ContainsKey(item.Key))
                        {
                            new_match_taunts.Add(item.Key, item.Value);
                        }

                    }
                    match_taunts = new_match_taunts;
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


                    string content = "\n";
                    foreach (var item in match_taunts)
                    {
                        content += "    " + item.Key + "\n";
                    }

                    string content2 = "\n";
                    foreach (var item in taunt_buffer)
                    {
                        content2 += "    " + item.Key + "\n";
                    }

                    match_taunts.Clear();
                    Debug.Log("[AudioTaunts] Initialisation:"
                        + "\n[match_taunt]: " + match_taunts.Count
                        + "\n" + content
                        + "\n[buffered taunts]: " + taunt_buffer.Count
                        + "\n" + content2
                        );
                }
            }

            [HarmonyPatch(typeof(Server), "RegisterHandlers")]
            class MPAudioTaunts_Server_RegisterHandlers
            {
                private static void OnShareAudioTauntIdentifiers(NetworkMessage rawMsg)
                {


                    try
                    {
                        var msg = rawMsg.ReadMessage<ShareAudioTauntIdentifiers>();


                        if (string.IsNullOrEmpty(msg.hashes))
                        {
                            Debug.LogWarning("[AudioTaunts] OnShareAudioTauntIdentifiers message was empty");
                            return;
                        }

                        // Split the string to retrieve the individual hashes
                        List<string> file_hashes = msg.hashes.Split('/').ToList();
                        Debug.Log("[AudioTaunts] client " + rawMsg.conn.connectionId + " offers "
                            + file_hashes.Count + " hashes, " + msg.hashes.Length + " chars");

                        // each unknown hash costs a 128 kB buffer, so a client only gets to
                        // provide as many taunts as it has slots; the count is derived from
                        // match_taunts so it survives split announcements and resets with it
                        int accepted = 0;
                        foreach (var t in match_taunts.Values)
                            if (t.providing_clients != null && t.providing_clients.Contains(rawMsg.conn.connectionId))
                                accepted++;
                        int ignored = 0;

                        // Update match_taunts
                        int index = 0;
                        foreach (string hash in file_hashes)
                        {
                            if (hash == null || hash.Length < 5)
                                continue;

                            if (!string.IsNullOrEmpty(hash))
                            {
                                if (hash.StartsWith(" "))
                                    continue;

                                try
                                {

                                    // this taunt already exists, add the sender as a possible provider
                                    if (match_taunts.ContainsKey(hash))
                                    {

                                        if (match_taunts[hash].providing_clients == null)
                                        {
                                            match_taunts[hash].providing_clients = new List<int>();
                                        }
                                        if(!match_taunts[hash].providing_clients.Contains(rawMsg.conn.connectionId))
                                        {
                                            if (accepted >= AMOUNT_OF_TAUNTS_PER_CLIENT)
                                            {
                                                ignored++;
                                            }
                                            else
                                            {
                                                match_taunts[hash].providing_clients.Add(rawMsg.conn.connectionId);
                                                accepted++;
                                            }
                                        }
                                    }
                                    // this taunt doesnt exist, add it
                                    else if (accepted >= AMOUNT_OF_TAUNTS_PER_CLIENT)
                                    {
                                        ignored++;
                                    }
                                    else
                                    {
                                        AudioTaunt taunt = new AudioTaunt
                                        {
                                            hash = hash,
                                            name = "EMPTY",
                                            audio_taunt_data = new byte[AUDIO_TAUNT_SIZE_LIMIT],
                                            is_data_complete = false,
                                            timestamp = -1,
                                            ready_to_play = false,
                                            requested_taunt = false,
                                            audioclip = null,
                                            providing_clients = new List<int>(),
                                            requesting_clients = new List<int>(),
                                            received_packets = new List<int>()
                                        };
                                        taunt.providing_clients.Add(rawMsg.conn.connectionId);
                                        match_taunts.Add(hash, taunt);
                                        accepted++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogError("[AudioTaunts] OnShareAudioTaunts failed"
                                        + "\n loop:" + index
                                        + "\n hash:" + hash
                                        + "\n Exception: " + ex
                                        );
                                }

                            }
                            index++;
                        }

                        if (ignored > 0)
                            Debug.LogFormat("[AudioTaunts] client {0} offered more than {1} taunts, ignored {2} hashes",
                                rawMsg.conn.connectionId, AMOUNT_OF_TAUNTS_PER_CLIENT, ignored);

                        try
                        {
                            //Send the updated list of hashes to all connected clients that support audiotaunts
                            var listeners = new List<NetworkConnection>();
                            foreach (NetworkConnection networkConnection in NetworkServer.connections)
                            {
                                if (networkConnection != null && MPTweaks.ClientHasTweak(networkConnection.connectionId, "audiotaunts"))
                                    listeners.Add(networkConnection);
                            }

                            var batch = new List<string>();
                            int announced = 0;
                            int messages = 0;
                            foreach (var t in match_taunts)
                            {
                                if (t.Value == null || t.Value.hash == null)
                                {
                                    Debug.LogWarning("[AudioTaunts] OnShareAudioTaunts: empty element in the match taunts");
                                    continue;
                                }

                                batch.Add(t.Value.hash);
                                announced++;
                                if (batch.Count == 24)      // 24 * 33 bytes leaves room inside the 1100 byte packet
                                {
                                    SendIdentifiers(listeners, batch);
                                    messages++;
                                    batch.Clear();
                                }
                            }
                            if (batch.Count > 0)
                            {
                                SendIdentifiers(listeners, batch);
                                messages++;
                            }

                            Debug.Log("[AudioTaunts] announced " + announced + " hashes in "
                                + messages + " message(s) to " + listeners.Count + " client(s)");
                        }
                        catch(Exception ex)
                        {
                            Debug.LogError("[AudioTaunts] OnShareAudioTauntIdentifiers failed: " + ex);
                        }



                       
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("[AudioTaunts] OnShareAudioTauntIdentifiers failed: "+ex);
                        return;
                    }


                }


                // Sends one batch of the match hash list to every listener. Channel 0 is
                // ReliableSequenced with PacketSize 1100 and never fragments, so a long list
                // goes out as several messages; both handlers merge hashes additively, which
                // is what keeps the split invisible to a client that knows nothing about it.
                private static void SendIdentifiers(List<NetworkConnection> listeners, List<string> hashes)
                {
                    var msg = new ShareAudioTauntIdentifiers { hashes = string.Join("/", hashes.ToArray()) };
                    foreach (NetworkConnection networkConnection in listeners)
                        networkConnection.Send(MessageTypes.MsgShareAudioTauntIdentifiers, msg);

                    // The char count is the check that matters: anything near 1100 means the
                    // batch size is too generous and UNET will start dropping the send.
                    Debug.Log("[AudioTaunts] announce batch: " + hashes.Count + " hashes, "
                        + msg.hashes.Length + " chars");
                }

                private static void OnAudioTauntRequest(NetworkMessage rawMsg)
                {
                    try
                    {
                        var msg = rawMsg.ReadMessage<RequestAudioTaunt>();
                        Debug.Log("[AudioTaunts] Received audio taunt file request");
                        string hash = msg.hash;

                        AudioTaunt taunt = null;
                        if (taunt_buffer.ContainsKey(hash))
                        {
                            taunt = taunt_buffer[hash];
                        }
                        else if (match_taunts.ContainsKey(hash))
                        {
                            taunt = match_taunts[hash];
                        }


                        if (taunt != null)
                        {
                            // if its byte data is populated send that to the client that requested it
                            if (taunt.is_data_complete)
                            {
                                // start upload
                                GameManager.m_gm.StartCoroutine(UploadAudioTauntToClient(hash, taunt.audio_taunt_data, rawMsg.conn.connectionId));
                            }

                            // if it is not then request that data from the client the shared this audiotaunt
                            // and add Client A to the request list
                            else
                            {
                                foreach (NetworkConnection networkConnection in NetworkServer.connections)
                                {
                                    if (networkConnection != null && taunt.providing_clients.Count > 0 && networkConnection.connectionId == taunt.providing_clients[0])
                                    {
                                        networkConnection.SendByChannel(MessageTypes.MsgRequestAudioTaunt, new RequestAudioTaunt { hash = hash }, 0);
                                    }
                                }
                                if (taunt.requesting_clients == null)
                                    taunt.requesting_clients = new List<int>();

                                taunt.requesting_clients.Add(rawMsg.conn.connectionId);
                            }
                        }
                        else
                        {
                            Debug.LogWarning("[AudioTaunts] a client requested a taunt that has no context on the server");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("[AudioTaunts] processing the network request failed: " + ex);
                    }
                }

                public static IEnumerator UploadAudioTauntToClient(string hash, byte[] data, int connectionId)
                {
                    Debug.Log("[AudioTaunts] Started uploading AudioTaunt to client");
                    bool ended_early = false;
                    if (data.Length < PACKET_PAYLOAD_SIZE)
                    {
                        AudioTauntPacket packet = new AudioTauntPacket
                        {
                            filesize = data.Length,
                            amount_of_bytes_sent = data.Length,
                            hash = hash,
                            packet_id = 0,
                            data = data,
                        };
                        NetworkServer.SendToClient(connectionId, MessageTypes.MsgAudioTauntPacket, packet);
                    }
                    else
                    {
                        int position = 0;
                        int packet_id = 0;
                        while (position < data.Length)
                        {
                            // stop the transmission if the game is over
                            if(NetworkMatch.m_match_state == MatchState.POSTGAME | NetworkMatch.m_match_state == MatchState.SCOREBOARD)
                            {
                                ended_early = true;
                                break;
                            }

                            int index = 0;
                            byte[] to_send = new byte[PACKET_PAYLOAD_SIZE];

                            while (index < PACKET_PAYLOAD_SIZE & ((position + index) < data.Length))
                            {
                                to_send[index] = data[position + index];
                                index++;
                            }
                            AudioTauntPacket packet = new AudioTauntPacket
                            {
                                filesize = data.Length,
                                amount_of_bytes_sent = index,
                                hash = hash,
                                packet_id = packet_id,
                                data = to_send,
                            };
                            NetworkServer.SendToClient(connectionId, MessageTypes.MsgAudioTauntPacket, packet);
                            position += PACKET_PAYLOAD_SIZE;
                            packet_id++;
                            yield return null;
                        }
                    }
                    if (ended_early)
                        Debug.LogFormat("[AudioTaunts] upload of {0} to client {1} stopped, the match ended", hash, connectionId);
                }



                private static void OnAudioTauntPacket(NetworkMessage rawMsg)
                {
                    if (!active)
                        return;
                    try
                    {

                        //Debug.Log("[AudioTaunts]        Received a Fragment of an Audiotaunt");
                        var msg = rawMsg.ReadMessage<AudioTauntPacket>();

                        if (msg.data == null)
                            return;

                        if (match_taunts.ContainsKey(msg.hash))
                        {
                            // initialise the array with the correct size
                            if (match_taunts[msg.hash].timestamp == -1)
                            {
                                match_taunts[msg.hash].timestamp = Time.frameCount;
                                match_taunts[msg.hash].audio_taunt_data = new byte[msg.filesize];
                            }

                            if (!match_taunts[msg.hash].received_packets.Contains(msg.packet_id))
                            {
                                // add data of the fragment to the context
                                int startindex = msg.packet_id * PACKET_PAYLOAD_SIZE;
                                for (int i = 0; i < msg.amount_of_bytes_sent; i++)
                                {
                                    match_taunts[msg.hash].audio_taunt_data[startindex + i] = msg.data[i];
                                }
                                match_taunts[msg.hash].received_packets.Add(msg.packet_id);
                                //Debug.Log("     Added the data of the fragment");

                                // if this completes the data then check wether there are unanswered requests
                                if (match_taunts[msg.hash].received_packets.Count * PACKET_PAYLOAD_SIZE >= msg.filesize)
                                {
                                    //Debug.Log("     This Fragment completed the audio taunt data");
                                    match_taunts[msg.hash].is_data_complete = true;

                                    if(!taunt_buffer.ContainsKey(msg.hash))
                                    {
                                        taunt_buffer.Add(msg.hash, new AudioTaunt
                                        {
                                            hash = msg.hash,
                                            name = "",
                                            audio_taunt_data = match_taunts[msg.hash].audio_taunt_data,
                                            is_data_complete = true,
                                            timestamp = -1,
                                            ready_to_play = false,
                                            requested_taunt = false
                                        });
                                    }
                                    else
                                        Debug.LogError("[AudioTaunts] the taunt was already in the buffer: " + msg.hash);

                                    List<int> duplicate_free_requests = new List<int>();
                                    foreach (int connectionId in match_taunts[msg.hash].requesting_clients)
                                    {
                                        if(!duplicate_free_requests.Contains(connectionId))
                                        {
                                            duplicate_free_requests.Add(connectionId);
                                            GameManager.m_gm.StartCoroutine(UploadAudioTauntToClient(msg.hash, match_taunts[msg.hash].audio_taunt_data, connectionId));
                                        }
                                    }

                                }
                            }

                        }
                        else
                        {
                            Debug.LogWarning("[AudioTaunts] no context for this taunt, ignoring the fragment from connection " + rawMsg.conn.connectionId);
                        }
                    }
                    catch(Exception ex)
                    {
                        Debug.LogError("[AudioTaunts] OnAudioTauntPacket failed: " + ex);
                    }
                }


                private static void OnPlayAudioTaunt(NetworkMessage rawMsg)
                {
                    try
                    {
                        var msg = rawMsg.ReadMessage<PlayAudioTaunt>();

                        if (string.IsNullOrEmpty(msg.hash) | string.IsNullOrEmpty(msg.sender_name))
                        {
                            Debug.LogWarning("[AudioTaunts] OnPlayAudioTaunt message was incomplete, client: " + MPTweaks.ClientInfos[rawMsg.conn.connectionId]);
                            return;
                        }

                        //Debug.Log("     msg.hash: " + msg.hash + "\n  msg.sender: " + msg.sender_name);

                        if (match_taunts != null && match_taunts.ContainsKey(msg.hash))
                        {
                            // distribute it to all other clients
                            PlayAudioTaunt packet = new PlayAudioTaunt
                            {
                                hash = msg.hash,
                                sender_name = msg.sender_name
                            };

                            foreach (NetworkConnection networkConnection in NetworkServer.connections)
                            {
                                if (networkConnection != null & rawMsg.conn.connectionId != networkConnection.connectionId & MPTweaks.ClientHasTweak(networkConnection.connectionId, "audiotaunts"))
                                    networkConnection.SendByChannel(MessageTypes.MsgPlayAudioTaunt, packet, 0);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // This is necessary because there are 7 test versions in circulation with different formatting for their network messages
                    }
                }

                static void Postfix()
                {
                    NetworkServer.RegisterHandler(MessageTypes.MsgShareAudioTauntIdentifiers, OnShareAudioTauntIdentifiers);
                    NetworkServer.RegisterHandler(MessageTypes.MsgRequestAudioTaunt, OnAudioTauntRequest);
                    NetworkServer.RegisterHandler(MessageTypes.MsgAudioTauntPacket, OnAudioTauntPacket);
                    NetworkServer.RegisterHandler(MessageTypes.MsgPlayAudioTaunt, OnPlayAudioTaunt);
                }
            }
        }

        public class ShareAudioTauntIdentifiers : MessageBase
        {
            public string hashes;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(hashes);
            }
            public override void Deserialize(NetworkReader reader)
            {
                reader.SeekZero();
                hashes = reader.ReadString();
            }
        }

        public class RequestAudioTaunt : MessageBase
        {
            public string hash;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(hash);
            }
            public override void Deserialize(NetworkReader reader)
            {
                reader.SeekZero();
                hash = reader.ReadString();
            }
        }

        public class PlayAudioTaunt : MessageBase
        {
            public string hash;
            public string sender_name = "";

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(hash + "/" + sender_name);
            }
            public override void Deserialize(NetworkReader reader)
            {
                try
                {
                    reader.SeekZero();
                    var msg = reader.ReadString().Split('/');
                    hash = msg[0];
                    sender_name = msg[1];
                }
                catch(Exception)
                {
                    // This is necessary because there are 7 test versions in circulation with different formatting for their network messages
                }
            }
        }

        public class Ack : MessageBase
        {
            public string identifier;
            public int packet_id;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(packet_id.ToString() + "/" + identifier);
            }
            public override void Deserialize(NetworkReader reader)
            {
                reader.SeekZero();
                var msg = reader.ReadString().Split('/');
                packet_id = int.Parse(msg[0]);
                identifier = msg[1];
            }
        }



        public class AudioTauntPacket : MessageBase
        {
            public int filesize;
            public int amount_of_bytes_sent;
            public string hash;
            public int packet_id;
            public byte[] data;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(packet_id.ToString() + "/"
                    + filesize.ToString() + "/"
                    + amount_of_bytes_sent.ToString() + "/"
                    + hash + "/"
                    + Convert.ToBase64String(data)
                    );
            }
            public override void Deserialize(NetworkReader reader)
            {
                //reader.SeekZero();
                string msg = reader.ReadString();

                //Debug.Log("CONTENT OF PACKET:\n"+msg);

                int pos = msg.IndexOf("/");
                packet_id = int.Parse(msg.Substring(0, pos));
                msg = msg.Remove(0, pos + 1);

                pos = msg.IndexOf("/");
                filesize = int.Parse(msg.Substring(0, pos));
                msg = msg.Remove(0, pos + 1);

                pos = msg.IndexOf("/");
                amount_of_bytes_sent = int.Parse(msg.Substring(0, pos));
                msg = msg.Remove(0, pos + 1);

                pos = msg.IndexOf("/");
                hash = msg.Substring(0, pos);
                msg = msg.Remove(0, pos + 1);
                try
                {
                    data = Convert.FromBase64String(msg);
                }
                catch(Exception ex)
                {
                    Debug.Log($"[AudioTaunts] AudioTauntPacket.Deserialize: data couldn't format properly: {ex.ToString()}");
                }
                
            }
        }



    }
}
