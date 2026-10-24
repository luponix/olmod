using System;
using System.Collections;
using System.Collections.Generic;
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

        private static IEnumerator Fetch()
        {
            string url = Mods.CatalogUrl;
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                if (request.isNetworkError || request.isHttpError)
                {
                    Status = "unreachable";
                    Debug.Log("[Mods] catalog: " + url + " -- " + request.error);
                    yield break;
                }
                try
                {
                    Read(request.downloadHandler.text, url);
                }
                catch (Exception ex)
                {
                    Status = "unreadable";
                    Debug.Log("[Mods] catalog: " + url + " is not a readable catalog -- " + ex.Message);
                    yield break;
                }
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

        // The catalog names the file olmod writes into its own directory, so anything but
        // a bare Mod-*.dll is refused rather than resolved.
        private static bool IsPlainModFileName(string file)
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
                (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps))
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
