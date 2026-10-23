using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Ionic.Zip;
using Newtonsoft.Json.Linq;
using Overload;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;
using Version = System.Version;

namespace GameMod.VersionHandling
{
    public enum SelfUpdateState
    {
        Unknown,        // no check has finished yet
        UpToDate,
        Available,
        Downloading,
        Installed,      // the files are in place but the relaunch did not take
        Failed
    }

    /// <summary>
    /// Installs a newer Pyro release over the running one and relaunches the game with the
    /// arguments it was started with. A release is one zip on the project's own site, listed
    /// with its sha256 and its changelog in /pyro-releases/index.json.
    ///
    /// Both loaders read GameMod.dll and 0Harmony.dll into memory and close the handle
    /// (olmod/olmod.cpp load_image, linux/olmod.c load_image), so the managed half can be
    /// replaced while the game runs. The native loader cannot be: on Windows olmod.exe is the
    /// running process image, which Windows refuses to overwrite but does allow renaming --
    /// hence every file is moved aside rather than overwritten.
    /// </summary>
    public static class SelfUpdate
    {
        public static string IndexUrl = "https://voidlog.duckdns.org/pyro-releases/index.json";

        public static SelfUpdateState State { get; private set; }

        // what the popup reports under its buttons: download progress, or why an install stopped
        public static string StatusDetail { get; private set; }

        // every published release newer than the running one, newest first
        public static List<Release> NewReleases { get; private set; }

        public static Release Latest
        {
            get { return NewReleases == null || NewReleases.Count == 0 ? null : NewReleases[0]; }
        }

        // the file a failed swap is rolled back from, and what the next launch clears out
        private const string REPLACED_SUFFIX = ".replaced";

        // operator-owned: server name, notes and password live here, an update must not touch it
        private static readonly string[] KEEP_INSTALLED = { "olmodsettings.json" };

        private static bool checkStarted;

        public class Release
        {
            public Version version;
            public string name;      // as published, e.g. "0.1.2"
            public string url;       // absolute, or relative to the index
            public string sha256;
            public string changelog;
        }

        private static string StagingDir
        {
            get { return Path.Combine(Config.OLModDir, "update-staging"); }
        }

        private static bool OnWindows
        {
            get { return Application.platform == RuntimePlatform.WindowsPlayer; }
        }

        private static bool PlatformSupported
        {
            get
            {
                return Application.platform == RuntimePlatform.WindowsPlayer ||
                    Application.platform == RuntimePlatform.LinuxPlayer ||
                    Application.platform == RuntimePlatform.OSXPlayer;
            }
        }

        // starts the once-per-session check; the outcome shows up in State
        public static void CheckForUpdate()
        {
            if (checkStarted || Overload.NetworkManager.IsHeadless() || !PlatformSupported ||
                string.IsNullOrEmpty(Config.OLModDir))
                return;
            checkStarted = true;
            DeleteReplacedFiles();
            GameManager.m_gm.StartCoroutine(ReadPublishedReleases());
        }

        public static void StartInstall()
        {
            if (Latest == null)
            {
                Fail("no release has been found to install");
                return;
            }
            State = SelfUpdateState.Downloading;
            StatusDetail = "STARTING";
            GameManager.m_gm.StartCoroutine(Install(Latest));
        }

        internal static IEnumerator ReadPublishedReleases()
        {
            JObject index = null;
            yield return FetchIndex(result => index = result);
            if (index == null)
                yield break; // stays Unknown: a site that is down must not read as an update

            var newer = new List<Release>();
            foreach (JToken entry in index["releases"] ?? new JArray())
            {
                Release release = ReadRelease(entry);
                if (release != null && release.version > OlmodVersion.RunningVersion)
                    newer.Add(release);
            }
            newer.Sort((a, b) => b.version.CompareTo(a.version));

            NewReleases = newer;
            State = newer.Count > 0 ? SelfUpdateState.Available : SelfUpdateState.UpToDate;
            Debug.LogFormat("PYROUPDATE: running {0}, {1} newer release(s) published -> {2}",
                OlmodVersion.RunningVersion, newer.Count, State);
        }

        private static Release ReadRelease(JToken entry)
        {
            var release = new Release
            {
                name = (string)entry["version"],
                url = (string)entry["url"],
                sha256 = (string)entry["sha256"],
                changelog = (string)entry["changelog"] ?? ""
            };
            release.version = ParseVersion(release.name);
            if (release.version == null || string.IsNullOrEmpty(release.url) || string.IsNullOrEmpty(release.sha256))
            {
                Debug.LogFormat("PYROUPDATE: rejected index entry {0}", release.name);
                return null;
            }
            return release;
        }

        private static IEnumerator Install(Release release)
        {
            // Step 1: make sure the install can be written before anything is fetched
            if (!IsWritable(Config.OLModDir))
            {
                Fail("THE PYRO FOLDER IS NOT WRITABLE");
                yield break;
            }
            TryDeleteDirectory(StagingDir);
            Directory.CreateDirectory(StagingDir);

            // Step 2: fetch the release, and refuse it unless it hashes to what the site published
            string archive = Path.Combine(StagingDir, release.name + ".zip");
            yield return Download(release, archive);
            if (!File.Exists(archive))
            {
                Fail("DOWNLOAD FAILED");
                TryDeleteDirectory(StagingDir);
                yield break;
            }

            // Step 3: unpack beside the install, so a broken archive is found before a swap
            StatusDetail = "UNPACKING";
            yield return null;
            string unpacked = Path.Combine(StagingDir, "files");
            List<string> files = Unpack(archive, unpacked);
            if (files == null)
            {
                Fail("THE DOWNLOADED RELEASE COULD NOT BE UNPACKED");
                TryDeleteDirectory(StagingDir);
                yield break;
            }

            // Step 4: swap, and only now, with the whole release verified and on disk
            StatusDetail = "INSTALLING";
            yield return null;
            if (!SwapInStagedFiles(unpacked, files))
            {
                Fail("INSTALLING THE DOWNLOADED FILES FAILED, THE PREVIOUS VERSION IS INTACT");
                yield break;
            }
            TryDeleteDirectory(StagingDir);
            Debug.LogFormat("PYROUPDATE: installed {0} ({1} files)", release.name, files.Count);

            // Step 5: hand over to the new build. State is set first so that a relaunch which
            // does not come up leaves the player reading RESTART TO FINISH rather than a stall.
            State = SelfUpdateState.Installed;
            StatusDetail = "PYRO " + release.name + " INSTALLED";
            Relaunch();
        }

        private static IEnumerator Download(Release release, string destination)
        {
            string partial = destination + ".part";
            TryDelete(partial);

            using (UnityWebRequest www = UnityWebRequest.Get(AbsoluteUrl(release.url)))
            {
                www.downloadHandler = new DownloadHandlerFile(partial);
                UnityWebRequestAsyncOperation request = www.SendWebRequest();
                while (!request.isDone)
                {
                    StatusDetail = "DOWNLOADING PYRO " + release.name + "   " +
                        Mathf.RoundToInt(www.downloadProgress * 100f) + "%";
                    yield return null;
                }
                if (www.isNetworkError || www.isHttpError)
                {
                    Debug.LogFormat("PYROUPDATE: download of {0} failed: {1}", release.url, www.error);
                    TryDelete(partial);
                    yield break;
                }
            }

            string hash = HashFile(partial);
            if (!SameHash(hash, release.sha256))
            {
                Debug.LogFormat("PYROUPDATE: hash mismatch on {0}, published {1} got {2}",
                    release.url, release.sha256, hash);
                TryDelete(partial);
                yield break;
            }
            File.Move(partial, destination);
        }

        // Unpacks the release into its own folder and answers with the paths to install,
        // relative to that folder; null when the archive cannot be read. The zip holds one
        // pyro-<version>/ folder, which is dropped: what is inside it is what an install holds.
        private static List<string> Unpack(string archive, string into)
        {
            Directory.CreateDirectory(into);
            var files = new List<string>();
            try
            {
                using (ZipFile zip = ZipFile.Read(archive))
                    foreach (ZipEntry entry in zip)
                    {
                        if (entry.IsDirectory)
                            continue;
                        string path = WithoutTopFolder(entry.FileName);
                        if (!IsSafeRelativePath(path))
                        {
                            Debug.LogFormat("PYROUPDATE: rejected archive entry {0}", entry.FileName);
                            return null;
                        }
                        if (Array.IndexOf(KEEP_INSTALLED, path) >= 0)
                            continue;

                        string target = Path.Combine(into, path.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (FileStream output = File.Create(target))
                            entry.Extract(output);
                        files.Add(path);
                    }
            }
            catch (ZipException ex)
            {
                Debug.LogFormat("PYROUPDATE: archive unreadable: {0}", ex.Message);
                return null;
            }
            catch (IOException ex)
            {
                Debug.LogFormat("PYROUPDATE: unpacking failed: {0}", ex.Message);
                return null;
            }
            return files.Count == 0 ? null : files;
        }

        private static string WithoutTopFolder(string entryName)
        {
            string path = entryName.Replace('\\', '/');
            int firstSlash = path.IndexOf('/');
            return firstSlash < 0 ? path : path.Substring(firstSlash + 1);
        }

        // the archive chooses the paths that get written, so anything that could reach outside
        // the olmod folder is refused before it touches the filesystem
        private static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.StartsWith("/") || path.Contains("\\") || path.Contains(":"))
                return false;
            foreach (string part in path.Split('/'))
                if (part.Length == 0 || part == "." || part == "..")
                    return false;
            return true;
        }

        // Moves the unpacked files into the olmod folder, renaming each replaced file to
        // <name>.replaced instead of deleting it: that is both what makes replacing the running
        // olmod.exe possible on Windows and what a failed swap is restored from.
        private static bool SwapInStagedFiles(string unpacked, List<string> files)
        {
            var replaced = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (string path in files)
                {
                    string installed = Path.Combine(Config.OLModDir, path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(installed));
                    if (File.Exists(installed))
                    {
                        string aside = installed + REPLACED_SUFFIX;
                        TryDelete(aside);
                        File.Move(installed, aside);
                        replaced.Add(new KeyValuePair<string, string>(installed, aside));
                    }
                    File.Move(Path.Combine(unpacked, path.Replace('/', Path.DirectorySeparatorChar)), installed);
                    if (!OnWindows && path.EndsWith(".sh"))
                        MakeExecutable(installed);
                }
                return true;
            }
            catch (IOException ex)
            {
                Debug.LogFormat("PYROUPDATE: swap failed, rolling back: {0}", ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.LogFormat("PYROUPDATE: swap not permitted, rolling back: {0}", ex.Message);
            }
            foreach (KeyValuePair<string, string> entry in replaced)
            {
                TryDelete(entry.Key);
                File.Move(entry.Value, entry.Key);
            }
            return false;
        }

        // Starts the updated build and quits. The replacement waits a few seconds first so the
        // two processes never overlap: an install keeps one Unity player log and a second
        // instance truncates the first one's.
        private static void Relaunch()
        {
            List<string> command = RelaunchCommand();
            string script = Path.Combine(Path.GetTempPath(), OnWindows ? "pyro-relaunch.cmd" : "pyro-relaunch.sh");
            File.WriteAllText(script, RelaunchScript(command));
            Debug.LogFormat("PYROUPDATE: relaunching via {0}: {1}", script, string.Join(" ", command.ToArray()));

            if (OnWindows)
                // a .cmd cannot be handed to CreateProcess, so this one goes through the shell
                Process.Start(new ProcessStartInfo(script)
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            else
                Process.Start(new ProcessStartInfo("/bin/sh", "\"" + script + "\"")
                {
                    UseShellExecute = false
                });
            Application.Quit();
        }

        // Rebuilds the command this process was started with. On Windows the process is olmod.exe
        // itself, so its own arguments are the right ones -- but not its argv[0], which the loader
        // hooks to report Overload.exe. Elsewhere the process is Overload and the entry point is
        // olmod.sh, which consumes -gamedir before the arguments reach us.
        private static List<string> RelaunchCommand()
        {
            var command = new List<string> { Path.Combine(Config.OLModDir, OnWindows ? "olmod.exe" : "olmod.sh") };

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
                command.Add(args[i]);

            string gameDir = Directory.GetParent(Application.dataPath).FullName;
            if (!OnWindows && gameDir != Config.OLModDir)
            {
                command.Add("-gamedir");
                command.Add(gameDir);
            }
            return command;
        }

        private static string RelaunchScript(List<string> command)
        {
            var script = new StringBuilder();
            if (OnWindows)
            {
                script.AppendLine("@echo off");
                script.AppendLine("ping -n 4 127.0.0.1 >nul"); // timeout needs a console, ping does not
                script.Append("start \"\"");
                foreach (string part in command)
                    script.Append(" \"" + part + "\"");
            }
            else
            {
                script.AppendLine("#!/bin/sh");
                script.AppendLine("sleep 3");
                script.Append("exec");
                foreach (string part in command)
                    script.Append(" '" + part.Replace("'", "'\\''") + "'");
            }
            script.AppendLine();
            return script.ToString();
        }

        // Restores the executable bit, which a freshly written file does not carry. This shells
        // out rather than P/Invoking chmod because Mono's dllmap points "libc" at libc.so.6,
        // which does not exist on macOS.
        private static void MakeExecutable(string path)
        {
            using (Process chmod = Process.Start(new ProcessStartInfo("/bin/chmod", "755 \"" + path + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }))
                chmod.WaitForExit();
        }

        // clears what the last update renamed aside; those files can only go once the process
        // that was holding them has exited
        private static void DeleteReplacedFiles()
        {
            if (!Directory.Exists(Config.OLModDir))
                return;
            foreach (string path in Directory.GetFiles(Config.OLModDir, "*" + REPLACED_SUFFIX, SearchOption.AllDirectories))
                TryDelete(path);
        }

        private static IEnumerator FetchIndex(Action<JObject> onDone)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(IndexUrl))
            {
                www.timeout = 15;
                yield return www.SendWebRequest();
                if (www.isNetworkError || www.isHttpError)
                {
                    Debug.LogFormat("PYROUPDATE: release index request failed: {0}", www.error);
                    onDone(null);
                    yield break;
                }
                JObject parsed = null;
                try
                {
                    parsed = JObject.Parse(www.downloadHandler.text);
                }
                catch (Newtonsoft.Json.JsonReaderException ex)
                {
                    Debug.LogFormat("PYROUPDATE: release index is not valid json: {0}", ex.Message);
                }
                onDone(parsed);
            }
        }

        // "0.1.2" -> 0.1.2.0 so it compares against the four-part assembly version; null when the
        // index does not carry a plain three-part number
        private static Version ParseVersion(string text)
        {
            Match number = Regex.Match(text ?? "", @"^(\d{1,4})\.(\d{1,4})\.(\d{1,4})$");
            if (!number.Success)
                return null;
            return new Version(int.Parse(number.Groups[1].Value), int.Parse(number.Groups[2].Value),
                int.Parse(number.Groups[3].Value), 0);
        }

        private static string AbsoluteUrl(string url)
        {
            if (url.StartsWith("http://") || url.StartsWith("https://"))
                return url;
            return IndexUrl.Substring(0, IndexUrl.LastIndexOf('/') + 1) + url;
        }

        private static bool SameHash(string one, string other)
        {
            return one != null && string.Equals(one, other, StringComparison.OrdinalIgnoreCase);
        }

        private static string HashFile(string path)
        {
            using (FileStream input = File.OpenRead(path))
            using (SHA256Managed sha = new SHA256Managed())
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }

        private static bool IsWritable(string dir)
        {
            string probe = Path.Combine(dir, ".pyro-write-probe");
            try
            {
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException ex)
            {
                Debug.LogFormat("PYROUPDATE: could not delete {0}: {1}", path, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.LogFormat("PYROUPDATE: could not delete {0}: {1}", path, ex.Message);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (IOException ex)
            {
                Debug.LogFormat("PYROUPDATE: could not clear {0}: {1}", path, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.LogFormat("PYROUPDATE: could not clear {0}: {1}", path, ex.Message);
            }
        }

        private static void Fail(string reason)
        {
            State = SelfUpdateState.Failed;
            StatusDetail = reason;
            Debug.LogFormat("PYROUPDATE: {0}", reason);
        }
    }

    // offer the update once the game has finished starting up
    [HarmonyPatch(typeof(GameplayManager), "Initialize")]
    class SelfUpdate_GameplayManager_Initialize
    {
        static void Postfix()
        {
            SelfUpdate.CheckForUpdate();
        }
    }

    [HarmonyPatch(typeof(Console), "RegisterCommands")]
    class SelfUpdate_Console_RegisterCommands
    {
        static void Postfix()
        {
            uConsole.RegisterCommand("pyro_update",
                "Pyro self update -- \"pyro_update [check|install|url <index url>]\"",
                new uConsole.DebugCommand(CmdPyroUpdate));
        }

        private static void CmdPyroUpdate()
        {
            string subcommand = uConsole.GetNumParameters() > 0 ? uConsole.GetString() : "";
            switch (subcommand)
            {
                case "check":
                    GameManager.m_gm.StartCoroutine(SelfUpdate.ReadPublishedReleases());
                    uConsole.Log("Checking " + SelfUpdate.IndexUrl);
                    break;
                case "install":
                    SelfUpdate.StartInstall();
                    break;
                case "url":
                    SelfUpdate.IndexUrl = uConsole.GetString();
                    uConsole.Log("Release index url is now " + SelfUpdate.IndexUrl);
                    break;
                default:
                    uConsole.Log("Running " + OlmodVersion.RunningVersion + ", state " + SelfUpdate.State +
                        (SelfUpdate.Latest == null ? "" : ", published " + SelfUpdate.Latest.name));
                    break;
            }
        }
    }
}
