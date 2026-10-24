using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using GameMod.VersionHandling;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace GameMod.ModsHandler
{
    // What the mod site publishes: one entry per mod, each naming the file it installs
    // as, where to get it and the hash it must have.
    static class Catalog
    {
        public class Entry
        {
            public string File;
            public string Name;
            public string Description;
            public string Version;
            public string Url;
            public string Sha256;
            public long Size;

            // Without a published hash a download cannot be told from anything else the
            // address happens to serve, so it is not offered.
            public bool Downloadable
            {
                get { return !string.IsNullOrEmpty(Url) && !string.IsNullOrEmpty(Sha256); }
            }
        }

        public static readonly List<Entry> Entries = new List<Entry>();
        public static string Status { get; private set; }

        // False until the site has actually answered. Without it "the catalog does not
        // list this file" and "we could not ask" look the same, and every installed mod
        // gets accused of being unpublished whenever the site is down.
        public static bool Loaded { get; private set; }

        public static Entry Find(string file)
        {
            return Entries.Find(entry => string.Equals(entry.File, file, StringComparison.OrdinalIgnoreCase));
        }

        public static string DisplayName(string file)
        {
            Entry entry = Find(file);
            return entry != null && !string.IsNullOrEmpty(entry.Name) ? entry.Name : file;
        }

        public static IEnumerable<string> NotInstalled()
        {
            List<string> installed = new List<string>(Mods.Installed());
            foreach (Entry entry in Entries)
                if (!installed.Exists(file => string.Equals(file, entry.File, StringComparison.OrdinalIgnoreCase)))
                    yield return entry.File;
        }

        private const ulong CATALOG_BYTES_MAX = 1024 * 1024;

        private static IEnumerator Fetch()
        {
            string url = Mods.CatalogUrl;
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Status = "not https";
                Debug.Log("[Mods] catalog: refusing non-https url " + url);
                yield break;
            }

            byte[] catalogBytes;
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 10;
                UnityWebRequestAsyncOperation sending = request.SendWebRequest();
                while (!sending.isDone)
                {
                    if (request.downloadedBytes > CATALOG_BYTES_MAX)
                        request.Abort();
                    yield return null;
                }
                if (request.downloadedBytes > CATALOG_BYTES_MAX)
                {
                    Status = "too large";
                    Debug.Log("[Mods] catalog: " + url + " exceeds " + CATALOG_BYTES_MAX + " bytes");
                    yield break;
                }
                if (request.isNetworkError || request.isHttpError)
                {
                    Status = "unreachable";
                    Debug.Log("[Mods] catalog: " + url + " -- " + request.error);
                    yield break;
                }
                catalogBytes = request.downloadHandler.data;
            }

            // UnityWebRequest on this engine validates no TLS certificate, so the
            // detached signature is what makes the catalog trustworthy, not https.
            string signature;
            using (UnityWebRequest request = UnityWebRequest.Get(url + ".sig"))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();
                if (request.isNetworkError || request.isHttpError)
                {
                    Status = "signature missing";
                    Debug.Log("[Mods] catalog: " + url + ".sig -- " + request.error);
                    yield break;
                }
                signature = request.downloadHandler.text;
            }
            if (!SignedIndex.Verify(catalogBytes, signature))
            {
                Status = "signature invalid";
                Debug.Log("[Mods] catalog: " + url + " does not match its signature, ignoring it");
                yield break;
            }

            try
            {
                Read(Encoding.UTF8.GetString(catalogBytes), url);
            }
            catch (Exception ex)
            {
                Status = "unreadable";
                Debug.Log("[Mods] catalog: " + url + " is not a readable catalog -- " + ex.Message);
                yield break;
            }
            Loaded = true;
            Status = Entries.Count + " listed";
            Debug.Log("[Mods] catalog: " + Entries.Count + " mod(s) listed at " + url);
        }

        private static void Read(string json, string catalogUrl)
        {
            JArray mods = JObject.Parse(json)["mods"] as JArray;
            Entries.Clear();
            if (mods == null)
                return;
            foreach (JToken mod in mods)
            {
                string file = mod.Value<string>("file");
                if (!IsPlainModFileName(file))
                {
                    Debug.Log("[Mods] catalog: ignoring entry with file name " + (file ?? "(none)"));
                    continue;
                }
                Entries.Add(new Entry
                {
                    File = file,
                    Name = mod.Value<string>("name"),
                    Description = mod.Value<string>("description"),
                    Version = mod.Value<string>("version"),
                    Url = WebAddress(mod.Value<string>("url"), catalogUrl),
                    Sha256 = mod.Value<string>("sha256"),
                    Size = mod.Value<long?>("size") ?? 0L
                });
            }
        }

        // The catalog and olmodstartup.json name files in olmod's own directory, so
        // anything but a bare Mod-*.dll is refused rather than resolved.
        internal static bool IsPlainModFileName(string file)
        {
            return !string.IsNullOrEmpty(file) &&
                file == System.IO.Path.GetFileName(file) &&
                file.StartsWith("Mod-", StringComparison.OrdinalIgnoreCase) &&
                file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        }

        private static string WebAddress(string address, string catalogUrl)
        {
            if (string.IsNullOrEmpty(address))
                return null;
            Uri resolved;
            if (!Uri.TryCreate(new Uri(catalogUrl), address, out resolved) ||
                resolved.Scheme != Uri.UriSchemeHttps)
                return null;
            return resolved.AbsoluteUri;
        }

        [HarmonyPatch(typeof(GameManager), "Start")]
        internal class Catalog_GameManager_Start
        {
            private static void Postfix()
            {
                if (GameplayManager.IsDedicatedServer())
                    return;
                Status = "checking";
                GameManager.m_gm.StartCoroutine(Fetch());
            }
        }
    }
}
