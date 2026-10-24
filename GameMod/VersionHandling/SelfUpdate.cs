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
    /// with its sha256 and its changelog in /pyro-releases/index.json; the index is believed
    /// only once its detached signature verifies (SignedIndex), which through the sha256 also
    /// vouches for the zip.
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

        // caps on what a hostile or broken server can make the client take
        private const int MAX_INDEX_BYTES = 1 << 20;
        private const long MAX_ARCHIVE_BYTES = 256L << 20;

        // operator-owned: server name, notes and password live here, an update must not touch it
        private static readonly string[] KEEP_INSTALLED = { "olmodsettings.json" };

        private static bool checkStarted;
        private static bool checkRunning;
        private static bool cancelRequested;

        public class Release
        {
            public Version version;
            public string name;      // as published, e.g. "0.1.2"
            public string url;       // absolute https, resolved against the index when read
            public string sha256;
            public string changelog;
        }

        private static string StagingDir
        {
            get { return Path.Combine(Config.OLModDir, "update-staging"); }
        }

        // written as a swap's last step: on the next launch it tells a finished swap (clear
        // the .replaced copies) from an interrupted one (restore them)
        private static string SwapMarker
        {
            get { return Path.Combine(Config.OLModDir, "update-swap-complete"); }
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
            FinishLastSwap();
            StartCheck();
        }

        public static void StartCheck()
        {
            GameManager.m_gm.StartCoroutine(ReadPublishedReleases());
        }

        public static void StartInstall()
        {
            if (State == SelfUpdateState.Downloading)
            {
                Debug.LogFormat("PYROUPDATE: install refused, one is already running");
                return;
            }
            if (Latest == null)
            {
                Fail("no release has been found to install");
                return;
            }
            cancelRequested = false;
            State = SelfUpdateState.Downloading;
            StatusDetail = "STARTING";
            GameManager.m_gm.StartCoroutine(Install(Latest));
        }

        // flags the running download; Install notices, cleans up and returns to Available
        public static void CancelInstall()
        {
            if (State == SelfUpdateState.Downloading)
                cancelRequested = true;
        }

        internal static IEnumerator ReadPublishedReleases()
        {
            if (checkRunning || State == SelfUpdateState.Downloading)
            {
                Debug.LogFormat("PYROUPDATE: check refused, an update is already in progress");
                yield break;
            }
            checkRunning = true;
            try
            {
                JObject index = null;
                yield return FetchIndex(result => index = result);
                if (index == null)
                    yield break; // an unreachable site stays Unknown; a bad signature already read as Failed

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
                StatusDetail = "";
                Debug.LogFormat("PYROUPDATE: running {0}, {1} newer release(s) published -> {2}",
                    OlmodVersion.RunningVersion, newer.Count, State);
            }
            finally
            {
                checkRunning = false;
            }
        }

        // one index entry as a Release, or null for anything malformed: one bad entry must
        // not take down the whole check
        private static Release ReadRelease(JToken entry)
        {
            if (!(entry is JObject))
            {
                Debug.LogFormat("PYROUPDATE: skipped non-object index entry");
                return null;
            }
            Release release;
            try
            {
                release = new Release
                {
                    name = (string)entry["version"],
                    url = (string)entry["url"],
                    sha256 = (string)entry["sha256"],
                    changelog = (string)entry["changelog"] ?? ""
                };
            }
            catch (ArgumentException) // a field holding an object or array instead of text
            {
                Debug.LogFormat("PYROUPDATE: skipped malformed index entry");
                return null;
            }
            release.version = ParseVersion(release.name);
            if (release.version == null || string.IsNullOrEmpty(release.url) || string.IsNullOrEmpty(release.sha256))
            {
                Debug.LogFormat("PYROUPDATE: rejected index entry {0}", release.name);
                return null;
            }
            release.url = AbsoluteUrl(release.url);
            if (!release.url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogFormat("PYROUPDATE: rejected non-https release url {0}", release.url);
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
            if (!TryCreateDirectory(StagingDir))
            {
                Fail("THE PYRO FOLDER IS NOT WRITABLE");
                yield break;
            }

            // Step 2: fetch the release, and refuse it unless it hashes to what the site published
            string archive = Path.Combine(StagingDir, release.name + ".zip");
            yield return Download(release, archive);
            if (cancelRequested)
            {
                cancelRequested = false;
                TryDeleteDirectory(StagingDir);
                State = SelfUpdateState.Available;
                StatusDetail = "";
                Debug.LogFormat("PYROUPDATE: install of {0} cancelled", release.name);
                yield break;
            }
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
                TryDeleteDirectory(StagingDir);
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

        // Fetches the release zip to destination, refusing it unless it hashes to the sha256
        // the signed index published; on any failure or cancel nothing is left at destination.
        private static IEnumerator Download(Release release, string destination)
        {
            string partial = destination + ".part";
            TryDelete(partial);

            string failure = null;
            using (UnityWebRequest www = UnityWebRequest.Get(release.url))
            {
                www.downloadHandler = new DownloadHandlerFile(partial);
                UnityWebRequestAsyncOperation request = www.SendWebRequest();
                ulong receivedBytes = 0;
                float lastReceivedTime = Time.unscaledTime;
                while (!request.isDone)
                {
                    if (www.downloadedBytes > receivedBytes)
                    {
                        receivedBytes = www.downloadedBytes;
                        lastReceivedTime = Time.unscaledTime;
                    }

                    long declaredBytes;
                    string contentLength = www.GetResponseHeader("Content-Length");
                    if (cancelRequested)
                        failure = "cancelled";
                    else if (Time.unscaledTime - lastReceivedTime > 30f)
                        failure = "no data for 30 seconds"; // a hung connection must not hold the menu
                    else if (receivedBytes > MAX_ARCHIVE_BYTES || // checked per frame: DownloadHandlerFile itself cannot refuse bytes, so a frame's worth may land past the cap
                        (contentLength != null && long.TryParse(contentLength, out declaredBytes) &&
                            declaredBytes > MAX_ARCHIVE_BYTES))
                        failure = "larger than the " + (MAX_ARCHIVE_BYTES >> 20) + " MB cap";
                    if (failure != null)
                    {
                        www.Abort();
                        break;
                    }

                    StatusDetail = "DOWNLOADING PYRO " + release.name + "   " +
                        Mathf.RoundToInt(www.downloadProgress * 100f) + "%";
                    yield return null;
                }
                if (failure == null && (www.isNetworkError || www.isHttpError))
                    failure = www.error;
            }
            if (failure != null)
            {
                Debug.LogFormat("PYROUPDATE: download of {0} failed: {1}", release.url, failure);
                TryDelete(partial);
                yield break;
            }

            bool keep = false;
            try
            {
                string hash = HashFile(partial);
                if (SameHash(hash, release.sha256))
                {
                    File.Move(partial, destination);
                    keep = true;
                }
                else
                {
                    Debug.LogFormat("PYROUPDATE: hash mismatch on {0}, published {1} got {2}",
                        release.url, release.sha256, hash);
                }
            }
            catch (IOException ex)
            {
                Debug.LogFormat("PYROUPDATE: could not keep the download: {0}", ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.LogFormat("PYROUPDATE: could not keep the download: {0}", ex.Message);
            }
            if (!keep)
                TryDelete(partial);
        }

        // Unpacks the release into its own folder and answers with the paths to install,
        // relative to that folder; null when the archive cannot be read or oversteps a cap.
        private static List<string> Unpack(string archive, string into)
        {
            var files = new List<string>();
            try
            {
                Directory.CreateDirectory(into);
                using (ZipFile zip = ZipFile.Read(archive))
                {
                    string topFolder = SharedTopFolder(zip);
                    long unpackedTotal = 0;
                    foreach (ZipEntry entry in zip)
                    {
                        if (entry.IsDirectory)
                            continue;
                        unpackedTotal += entry.UncompressedSize;
                        if (entry.UncompressedSize > MAX_ARCHIVE_BYTES || unpackedTotal > 4 * MAX_ARCHIVE_BYTES)
                        {
                            Debug.LogFormat("PYROUPDATE: refused oversized archive content at {0}", entry.FileName);
                            return null; // a zip bomb must be refused before it fills the disk
                        }
                        string path = entry.FileName.Replace('\\', '/');
                        if (topFolder != null)
                            path = path.Substring(topFolder.Length + 1);
                        if (!IsSafeRelativePath(path))
                        {
                            Debug.LogFormat("PYROUPDATE: rejected archive entry {0}", entry.FileName);
                            return null;
                        }
                        if (Array.Exists(KEEP_INSTALLED, keep => string.Equals(keep, path, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        string target = Path.Combine(into, path.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (FileStream output = File.Create(target))
                            entry.Extract(output);
                        files.Add(path);
                    }
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
            catch (UnauthorizedAccessException ex)
            {
                Debug.LogFormat("PYROUPDATE: unpacking failed: {0}", ex.Message);
                return null;
            }
            catch (ArgumentException ex)
            {
                Debug.LogFormat("PYROUPDATE: unpacking failed: {0}", ex.Message);
                return null;
            }
            return files.Count == 0 ? null : files;
        }

        // The zip normally wraps everything in one pyro-<version>/ folder that an install does
        // not carry. It is stripped only when every entry sits under the same single folder;
        // anything else installs with its paths as they are.
        private static string SharedTopFolder(ZipFile zip)
        {
            string shared = null;
            foreach (ZipEntry entry in zip)
            {
                string path = entry.FileName.Replace('\\', '/');
                int firstSlash = path.IndexOf('/');
                if (firstSlash <= 0)
                    return null;
                string top = path.Substring(0, firstSlash);
                if (shared == null)
                    shared = top;
                else if (top != shared)
                    return null;
            }
            return shared;
        }

        // the archive chooses the paths that get written, so anything that could reach outside
        // the olmod folder is refused before it touches the filesystem
        private static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.StartsWith("/") || path.Contains("\\") || path.Contains(":"))
                return false;
            if (path.IndexOfAny(new[] { '<', '>', '|', '*', '?', '"' }) >= 0)
                return false; // invalid in Windows paths; File.Create would throw ArgumentException
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
                TryDelete(SwapMarker); // a stale marker must not bless this swap if it is cut short
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
                File.WriteAllText(SwapMarker, "");
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
            catch (ArgumentException ex)
            {
                Debug.LogFormat("PYROUPDATE: swap failed, rolling back: {0}", ex.Message);
            }
            foreach (KeyValuePair<string, string> entry in replaced)
                TryRestore(entry.Key, entry.Value);
            return false;
        }

        // puts the previous version of one file back, replacing whatever the swap left there
        private static void TryRestore(string installed, string aside)
        {
            try
            {
                if (File.Exists(installed))
                    File.Delete(installed);
                File.Move(aside, installed);
            }
            catch (IOException ex)
            {
                Debug.LogFormat("PYROUPDATE: could not restore {0}: {1}", installed, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.LogFormat("PYROUPDATE: could not restore {0}: {1}", installed, ex.Message);
            }
        }

        // Starts the updated build and quits, through a script that waits for this process to
        // exit first: two live instances truncate each other's Player.log and race the pilot
        // prefs. The script lives in the install folder rather than the shared OS temp dir,
        // where another local user could pre-place or rewrite it, and deletes itself when done.
        private static void Relaunch()
        {
            List<string> command = RelaunchCommand();
            string script = Path.Combine(Config.OLModDir, OnWindows ? "pyro-relaunch.cmd" : "pyro-relaunch.sh");
            try
            {
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
            }
            catch (IOException ex)
            {
                Debug.LogFormat("PYROUPDATE: relaunch failed, restart the game by hand: {0}", ex.Message);
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.LogFormat("PYROUPDATE: relaunch failed, restart the game by hand: {0}", ex.Message);
                return;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Debug.LogFormat("PYROUPDATE: relaunch failed, restart the game by hand: {0}", ex.Message);
                return;
            }
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
            if (gameDir.EndsWith(".app"))
                gameDir = Directory.GetParent(gameDir).FullName; // olmod.sh wants the folder HOLDING Overload.app; dataPath is inside the bundle
            if (!OnWindows && gameDir != Config.OLModDir)
            {
                command.Add("-gamedir");
                command.Add(gameDir);
            }
            return command;
        }

        private static string RelaunchScript(List<string> command)
        {
            int parentPid = Process.GetCurrentProcess().Id;
            var script = new StringBuilder();
            if (OnWindows)
            {
                script.AppendLine("@echo off");
                // wait for this process to exit, 15 s at most; ping is the sleep that needs no console
                script.AppendLine("for /l %%i in (1,1,15) do (");
                script.AppendLine("  tasklist /fi \"PID eq " + parentPid + "\" 2>nul | find \" " + parentPid + " \" >nul || goto launch");
                script.AppendLine("  ping -n 2 127.0.0.1 >nul");
                script.AppendLine(")");
                script.AppendLine(":launch");
                script.Append("start \"\"");
                foreach (string part in command)
                    script.Append(" " + CmdQuoted(part));
                script.AppendLine();
                script.AppendLine("(goto) 2>nul & del \"%~f0\""); // ends the batch context; the rest of the line still deletes the script
            }
            else
            {
                script.AppendLine("#!/bin/sh");
                script.AppendLine("waited=0");
                script.AppendLine("while kill -0 " + parentPid + " 2>/dev/null && [ $waited -lt 150 ]; do");
                script.AppendLine("  sleep 0.1");
                script.AppendLine("  waited=$((waited+1))");
                script.AppendLine("done");
                script.AppendLine("rm -- \"$0\""); // sh keeps reading from the open fd, so the file can go before the exec
                script.Append("exec");
                foreach (string part in command)
                    script.Append(" '" + part.Replace("'", "'\\''") + "'");
                script.AppendLine();
            }
            return script.ToString();
        }

        // quotes one argument for the .cmd: CommandLineToArgvW's rules for embedded quotes and
        // backslashes, with % doubled so cmd does not expand it as a variable
        private static string CmdQuoted(string arg)
        {
            var quoted = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char ch in arg)
            {
                if (ch == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (ch == '"')
                    quoted.Append('\\', backslashes * 2 + 1).Append('"');
                else
                    quoted.Append('\\', backslashes).Append(ch);
                backslashes = 0;
            }
            return quoted.Append('\\', backslashes * 2).Append('"').Replace("%", "%%").ToString();
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

        // Settles what the previous launch's swap left behind. The marker is written only after
        // every file is in place, so .replaced copies without it mean the swap was cut short --
        // those are rolled back; with it they are just the old version, cleared out now that the
        // process that was holding them has exited.
        private static void FinishLastSwap()
        {
            if (!Directory.Exists(Config.OLModDir))
                return;
            bool swapCompleted = File.Exists(SwapMarker);
            foreach (string aside in Directory.GetFiles(Config.OLModDir, "*" + REPLACED_SUFFIX, SearchOption.AllDirectories))
            {
                if (swapCompleted)
                    TryDelete(aside);
                else
                    TryRestore(aside.Substring(0, aside.Length - REPLACED_SUFFIX.Length), aside);
            }
            if (Directory.GetFiles(Config.OLModDir, "*" + REPLACED_SUFFIX, SearchOption.AllDirectories).Length == 0)
                TryDelete(SwapMarker); // a lingering .replaced keeps the marker, so the next launch retries rather than rolls back
        }

        // The index is trusted only once its detached signature checks out: UnityWebRequest on
        // this engine validates no TLS certificate, so the signature is the whole trust root.
        private static IEnumerator FetchIndex(Action<JObject> onDone)
        {
            byte[] indexBytes = null;
            yield return FetchSmall(IndexUrl, bytes => indexBytes = bytes);
            if (indexBytes == null)
            {
                onDone(null);
                yield break;
            }

            byte[] signature = null;
            yield return FetchSmall(IndexUrl + ".sig", bytes => signature = bytes);
            if (signature == null)
            {
                Fail("THE RELEASE INDEX IS NOT SIGNED");
                onDone(null);
                yield break;
            }
            if (!SignedIndex.Verify(indexBytes, Encoding.ASCII.GetString(signature)))
            {
                Fail("THE RELEASE INDEX SIGNATURE IS INVALID");
                onDone(null);
                yield break;
            }

            JObject parsed = null;
            try
            {
                parsed = JObject.Parse(Encoding.UTF8.GetString(indexBytes));
            }
            catch (Newtonsoft.Json.JsonReaderException ex)
            {
                Debug.LogFormat("PYROUPDATE: release index is not valid json: {0}", ex.Message);
            }
            onDone(parsed);
        }

        // fetches a small resource whole; hands back null on any failure or past the index cap
        private static IEnumerator FetchSmall(string url, Action<byte[]> onDone)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.timeout = 15;
                UnityWebRequestAsyncOperation request = www.SendWebRequest();
                while (!request.isDone)
                {
                    if (www.downloadedBytes > MAX_INDEX_BYTES)
                    {
                        www.Abort();
                        break;
                    }
                    yield return null;
                }
                if (www.isNetworkError || www.isHttpError || www.downloadedBytes > MAX_INDEX_BYTES)
                {
                    Debug.LogFormat("PYROUPDATE: request for {0} failed: {1}", url,
                        www.downloadedBytes > MAX_INDEX_BYTES
                            ? "larger than the " + (MAX_INDEX_BYTES >> 10) + " KB cap"
                            : www.error);
                    onDone(null);
                    yield break;
                }
                onDone(www.downloadHandler.data);
            }
        }

        // "0.1.2" or "0.1.2.1" -> a four-part Version (a missing part reads 0) so it compares
        // against the assembly version; null for anything else. The regex is also what keeps
        // release.name safe to use in a staging path.
        private static Version ParseVersion(string text)
        {
            Match number = Regex.Match(text ?? "", @"^(\d{1,4})\.(\d{1,4})\.(\d{1,4})(?:\.(\d{1,4}))?$");
            if (!number.Success)
                return null;
            return new Version(int.Parse(number.Groups[1].Value), int.Parse(number.Groups[2].Value),
                int.Parse(number.Groups[3].Value),
                number.Groups[4].Success ? int.Parse(number.Groups[4].Value) : 0);
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

        private static bool TryCreateDirectory(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                return true;
            }
            catch (IOException ex)
            {
                Debug.LogFormat("PYROUPDATE: could not create {0}: {1}", path, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.LogFormat("PYROUPDATE: could not create {0}: {1}", path, ex.Message);
            }
            return false;
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
                    SelfUpdate.StartCheck();
                    uConsole.Log("Checking " + SelfUpdate.IndexUrl);
                    break;
                case "install":
                    SelfUpdate.StartInstall();
                    break;
                case "url":
                    string url = uConsole.GetString();
                    if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        uConsole.Log("Only https:// index urls are accepted");
                        break;
                    }
                    SelfUpdate.IndexUrl = url;
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
