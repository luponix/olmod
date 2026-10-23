using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
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
            Mods.SetEnabled(file, false);
            Changes++;
            return true;
        }

        // The hash the catalog publishes is what tells the file the site serves from one
        // that merely took its name. Null when the file is gone or unreadable.
        public static string HashOf(string file)
        {
            FileInfo info = new FileInfo(Path.Combine(Config.OLModDir, file));
            if (!info.Exists)
                return null;

            Known known;
            if (hashes.TryGetValue(file, out known) &&
                known.Length == info.Length && known.Written == info.LastWriteTimeUtc)
                return known.Hex;

            string hex;
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream contents = info.OpenRead())
                    hex = Hex(sha.ComputeHash(contents));
            }
            catch (Exception ex)
            {
                Debug.Log("Mods: cannot hash " + info.FullName + " -- " + ex.Message);
                return null;
            }
            hashes[file] = new Known { Length = info.Length, Written = info.LastWriteTimeUtc, Hex = hex };
            return hex;
        }

        private class Known
        {
            public long Length;
            public DateTime Written;
            public string Hex;
        }

        private static readonly Dictionary<string, Known> hashes = new Dictionary<string, Known>();

        private static IEnumerator Fetch(Catalog.Entry entry)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(entry.Url))
            {
                yield return request.SendWebRequest();
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

                string problem = Write(entry.File, payload);
                if (problem != null)
                {
                    Fail(entry.File, problem);
                    yield break;
                }
            }
            Downloading = null;
            Changes++;
            Debug.Log("Mods: downloaded " + entry.File + " from " + entry.Url);
        }

        private static string Write(string file, byte[] payload)
        {
            try
            {
                File.WriteAllBytes(Path.Combine(Config.OLModDir, file), payload);
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
            Debug.Log("Mods: " + LastError);
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
