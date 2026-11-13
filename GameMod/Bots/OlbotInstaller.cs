using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Ionic.Zip;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GameMod.Bots
{
    static class OlbotInstaller
    {
        const string STAMP = "installed-from.sha256";
        const string NOT_INSTALLED = "BOTS ARE NOT INSTALLED ON THIS SERVER";
        const string UNSUPPORTED = "THE BOTS DO NOT SUPPORT THIS OVERLOAD INSTALL";
        const string INSTALL_FAILED = "THE BOTS COULD NOT BE INSTALLED ON THIS SERVER";
        static readonly TimeSpan STALE_LOCK = TimeSpan.FromMinutes(2);
        static readonly TimeSpan LOCK_WAIT = TimeSpan.FromSeconds(15);
        const int MOVE_ATTEMPTS = 10;
        const int MOVE_PAUSE_MS = 300;

        public static string Failure { get; private set; }
        public static string Detail { get; private set; }

        static LockFile heldLock;

        class InstallException : Exception
        {
            public readonly string Notice;

            public InstallException(string notice, string detail) : base(detail)
            {
                Notice = notice;
            }
        }

        static string PackagePath
        {
            get { return Path.Combine(Config.OLModDir, "olbot.zip"); }
        }

        static string LockPath
        {
            get { return Path.Combine(Config.OLModDir, "olbot.lock"); }
        }

        static string Platform
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer:
                        return "win64";
                    case RuntimePlatform.LinuxPlayer:
                        return "linux64";
                    default:
                        return null;
                }
            }
        }

        public static bool Ensure()
        {
            Failure = null;
            Detail = null;
            string folder = Olbot.PlayerFolder;
            try
            {
                if (!File.Exists(PackagePath))
                {
                    if (File.Exists(Olbot.PlayerPath))
                        return true;
                    throw new InstallException(NOT_INSTALLED, "no " + PackagePath + " and no " + Olbot.PlayerPath);
                }
                if (IsLink(folder) || IsLink(Path.GetDirectoryName(folder)))
                {
                    if (File.Exists(Olbot.PlayerPath))
                        return true;
                    throw new InstallException(NOT_INSTALLED, folder + " is behind a link and has no " + Olbot.PlayerPath);
                }
                if (Platform == null)
                    throw new InstallException(NOT_INSTALLED, "no bots for " + Application.platform);
                string packageHash = HashFile(PackagePath);
                if (IsInstalled(folder, packageHash))
                    return true;
                using (heldLock = Lock())
                {
                    if (IsInstalled(folder, packageHash))
                        return true;
                    float started = Time.realtimeSinceStartup;
                    Install(folder, packageHash, Path.GetDirectoryName(Application.dataPath));
                    Debug.LogFormat("[Bots] installed {0} from {1} in {2:0.00} s", folder, PackagePath, Time.realtimeSinceStartup - started);
                }
                return true;
            }
            catch (InstallException ex)
            {
                return Fail(ex.Notice, ex.Message);
            }
            catch (Exception ex)
            {
                return Fail(INSTALL_FAILED, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                heldLock = null;
            }
        }

        static bool Fail(string notice, string detail)
        {
            Failure = notice;
            Detail = detail;
            Debug.LogFormat("[Bots] {0}: {1}", notice, detail);
            return false;
        }

        static bool IsInstalled(string folder, string packageHash)
        {
            string stamp = Path.Combine(folder, STAMP);
            return File.Exists(Olbot.PlayerPath) && new FileInfo(Olbot.PlayerPath).Length > 0
                && File.Exists(stamp) && File.ReadAllText(stamp).Trim() == packageHash;
        }

        static void Install(string folder, string packageHash, string gameRoot)
        {
            string building = folder + ".new";
            string retired = folder + ".old";
            if (!Directory.Exists(folder) && Directory.Exists(retired))
                Move(retired, folder);
            TryDelete(retired);
            DeleteFolder(building);
            try
            {
                Build(building, gameRoot);
                File.WriteAllText(Path.Combine(building, STAMP), packageHash);
                if (Directory.Exists(folder))
                    Move(folder, retired);
                Move(building, folder);
            }
            catch
            {
                if (!Directory.Exists(folder) && Directory.Exists(retired))
                    TryMove(retired, folder);
                TryDelete(building);
                throw;
            }
            if (Platform == "linux64")
                VersionHandling.SelfUpdate.MakeExecutable(Olbot.PlayerPath);
            TryDelete(retired);
        }

        static void Build(string building, string gameRoot)
        {
            using (ZipFile zip = ZipFile.Read(PackagePath))
            {
                JObject files = ReadManifest(zip)["platforms"][Platform] as JObject;
                if (files == null)
                    throw new InstallException(NOT_INSTALLED, PackagePath + " has no " + Platform + " files");
                string prefix = Platform + "/";
                foreach (ZipEntry entry in zip)
                {
                    if (entry.IsDirectory || !entry.FileName.StartsWith(prefix))
                        continue;
                    using (FileStream output = File.Create(TargetPath(building, entry.FileName.Substring(prefix.Length))))
                        entry.Extract(output);
                    heldLock.Refresh();
                }
                foreach (JProperty file in files.Properties())
                {
                    string source = Path.Combine(gameRoot, SafeRelative((string)file.Value[0]).Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(source))
                        throw new InstallException(UNSUPPORTED, "missing " + source);
                    if (!string.Equals(HashFile(source), (string)file.Value[1], StringComparison.OrdinalIgnoreCase))
                        throw new InstallException(UNSUPPORTED, source + " is not the expected file");
                    string target = TargetPath(building, file.Name);
                    File.Copy(source, target, true);
                    File.SetAttributes(target, FileAttributes.Normal);
                    heldLock.Refresh();
                }
            }
        }

        static JObject ReadManifest(ZipFile zip)
        {
            ZipEntry manifest = zip["manifest.json"];
            if (manifest == null)
                throw new InstallException(NOT_INSTALLED, PackagePath + " has no manifest.json");
            using (var buffer = new MemoryStream())
            {
                manifest.Extract(buffer);
                return JObject.Parse(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
            }
        }

        static string TargetPath(string root, string relative)
        {
            string target = Path.Combine(root, SafeRelative(relative).Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            return target;
        }

        static string SafeRelative(string path)
        {
            bool safe = !string.IsNullOrEmpty(path) && !path.StartsWith("/") && path.IndexOfAny(new[] { '\\', ':', '<', '>', '|', '*', '?', '"' }) < 0;
            if (safe)
                foreach (string part in path.Split('/'))
                    safe &= part.Length > 0 && part != "." && part != "..";
            if (!safe)
                throw new InstallException(NOT_INSTALLED, PackagePath + " holds an unsafe path: " + path);
            return path;
        }

        static bool IsLink(string folder)
        {
            return Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0;
        }

        static void Move(string from, string to)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.Move(from, to);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == MOVE_ATTEMPTS || !(ex is IOException || ex is UnauthorizedAccessException))
                        throw;
                    Thread.Sleep(MOVE_PAUSE_MS);
                }
            }
        }

        static void TryMove(string from, string to)
        {
            try
            {
                Move(from, to);
            }
            catch (Exception ex)
            {
                Debug.LogFormat("[Bots] cannot move {0} back: {1}", from, ex.Message);
            }
        }

        static void DeleteFolder(string folder)
        {
            if (!Directory.Exists(folder))
                return;
            if (IsLink(folder))
                throw new InstallException(INSTALL_FAILED, folder + " is a link, it is not removed");
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(folder, true);
        }

        static void TryDelete(string folder)
        {
            try
            {
                DeleteFolder(folder);
            }
            catch (Exception ex)
            {
                Debug.LogFormat("[Bots] cannot remove {0}: {1}", folder, ex.Message);
            }
        }

        class LockFile : IDisposable
        {
            readonly FileStream stream;
            readonly byte[] token = System.Text.Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N"));

            public LockFile(FileStream stream)
            {
                this.stream = stream;
                Refresh();
            }

            public void Refresh()
            {
                stream.Seek(0, SeekOrigin.Begin);
                stream.Write(token, 0, token.Length);
                stream.Flush();
            }

            public void Dispose()
            {
                stream.Close();
                try
                {
                    if (File.ReadAllText(LockPath) == System.Text.Encoding.ASCII.GetString(token))
                        File.Delete(LockPath);
                }
                catch (Exception ex)
                {
                    Debug.LogFormat("[Bots] cannot remove {0}: {1}", LockPath, ex.Message);
                }
            }
        }

        static LockFile Lock()
        {
            DateTime giveUp = DateTime.UtcNow + LOCK_WAIT;
            while (true)
            {
                try
                {
                    return new LockFile(new FileStream(LockPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
                }
                catch (IOException)
                {
                    if (IsStale())
                        continue;
                    if (DateTime.UtcNow > giveUp)
                        throw new InstallException(INSTALL_FAILED, LockPath + " is held by another server");
                    Thread.Sleep(250);
                }
            }
        }

        static bool IsStale()
        {
            try
            {
                if (!File.Exists(LockPath) || DateTime.UtcNow - File.GetLastWriteTimeUtc(LockPath) <= STALE_LOCK)
                    return false;
                File.Delete(LockPath);
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

        static string HashFile(string path)
        {
            using (FileStream input = File.OpenRead(path))
            using (SHA256Managed sha = new SHA256Managed())
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
    }
}
