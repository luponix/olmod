using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace GameMod.ModsHandler
{
    // The mod files in the olmod directory, against Catalog's list of what the site
    // publishes: this adds and removes them. Mods.cs owns which of the files present
    // there load at the next start.
    static class Library
    {
        public static string Downloading { get; private set; }
        public static string LastError { get; private set; }

        // Bumped whenever a mod file appears or disappears, so the menu knows to re-read
        // the directory. A download off a local mirror can finish inside one frame, which
        // is why watching Downloading for a transition misses it.
        public static int Changes { get; private set; }

        public static void Download(Catalog.Entry entry)
        {
            if (Downloading != null)
                return;
            LastError = null;
            Downloading = entry.File;
            GameManager.m_gm.StartCoroutine(Fetch(entry));
        }

        public static bool Delete(string file)
        {
            LastError = null;
            try
            {
                File.Delete(Path.Combine(Config.OLModDir, file));
            }
            catch (Exception ex)
            {
                // Windows keeps a loaded assembly mapped, so a mod that is running cannot
                // be removed until the next start.
                LastError = "COULD NOT DELETE " + file.ToUpper() + " -- " + ex.Message.ToUpper();
                return false;
            }
            try
            {
                File.Delete(DescriptionPath(file));
            }
            catch (Exception ex)
            {
                Debug.Log("[Mods] cannot delete the description of " + file + " -- " + ex.Message);
            }
            Mods.SetEnabled(file, false);
            Changes++;
            return true;
        }

        // A mod's description is Mod-<name>.txt beside Mod-<name>.dll: plain text, a blank
        // line between paragraphs. Installing from the mod site writes it, and a mod copied
        // in by hand can bring its own. Null when there is none.
        public static string DescriptionOf(string file)
        {
            string path = DescriptionPath(file);
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex)
            {
                Debug.Log("[Mods] cannot read " + path + " -- " + ex.Message);
                return null;
            }
        }

        private static string DescriptionPath(string file)
        {
            return Path.ChangeExtension(Path.Combine(Config.OLModDir, file), ".txt");
        }

        // The hash the catalog publishes is what tells the file the site serves from one
        // that merely took its name. Read fresh every call -- a cache keyed on size and
        // mtime would keep calling an in-place swap "published". Null when the file is
        // gone or unreadable.
        public static string HashOf(string file)
        {
            FileInfo info = new FileInfo(Path.Combine(Config.OLModDir, file));
            if (!info.Exists)
                return null;
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream contents = info.OpenRead())
                    return Hex(sha.ComputeHash(contents));
            }
            catch (Exception ex)
            {
                Debug.Log("[Mods] cannot hash " + info.FullName + " -- " + ex.Message);
                return null;
            }
        }

        private const ulong DOWNLOAD_BYTES_MAX = 64 * 1024 * 1024;

        private static IEnumerator Fetch(Catalog.Entry entry)
        {
            ulong sizeLimit = entry.Size > 0 && (ulong)entry.Size < DOWNLOAD_BYTES_MAX
                ? (ulong)entry.Size : DOWNLOAD_BYTES_MAX;
            using (UnityWebRequest request = UnityWebRequest.Get(entry.Url))
            {
                request.timeout = 10;
                UnityWebRequestAsyncOperation sending = request.SendWebRequest();
                while (!sending.isDone)
                {
                    if (request.downloadedBytes > sizeLimit)
                        request.Abort();
                    yield return null;
                }
                if (request.downloadedBytes > sizeLimit)
                {
                    Fail(entry.File, "the download is larger than published");
                    yield break;
                }
                if (request.isNetworkError || request.isHttpError)
                {
                    Fail(entry.File, request.error);
                    yield break;
                }

                byte[] payload = request.downloadHandler.data;
                string hex;
                using (SHA256 sha = SHA256.Create())
                    hex = Hex(sha.ComputeHash(payload));
                if (!string.Equals(hex, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Fail(entry.File, "the download does not match the published hash");
                    yield break;
                }

                string problem = Write(Path.Combine(Config.OLModDir, entry.File), payload);
                if (problem != null)
                {
                    Fail(entry.File, problem);
                    yield break;
                }
            }
            if (!string.IsNullOrEmpty(entry.Description))
            {
                string descriptionProblem = Write(DescriptionPath(entry.File), Encoding.UTF8.GetBytes(entry.Description));
                if (descriptionProblem != null)
                    Debug.Log("[Mods] cannot write the description of " + entry.File + " -- " + descriptionProblem);
            }
            Downloading = null;
            Changes++;
            Debug.Log("[Mods] downloaded " + entry.File + " from " + entry.Url);
        }

        // Temp file + rename, so a crash mid-write cannot leave a truncated DLL under
        // the final name that the next start would try to load.
        private static string Write(string path, byte[] payload)
        {
            try
            {
                string temporary = path + ".tmp";
                File.WriteAllBytes(temporary, payload);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temporary, path);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static void Fail(string file, string reason)
        {
            Downloading = null;
            LastError = "COULD NOT DOWNLOAD " + file.ToUpper() + " -- " + reason.ToUpper();
            Debug.Log("[Mods] " + LastError);
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
