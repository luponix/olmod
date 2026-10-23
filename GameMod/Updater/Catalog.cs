using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace GameMod.Updater
{
    static class Catalog
    {
        public class Entry
        {
            public string File;
            public string Name;
            public string Description;
            public string Version;
        }

        public static readonly List<Entry> Entries = new List<Entry>();
        public static string Status { get; private set; }

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
                    Debug.Log("ModCatalog: " + url + " -- " + request.error);
                    yield break;
                }
                try
                {
                    Read(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    Status = "unreadable";
                    Debug.Log("ModCatalog: " + url + " is not a readable catalog -- " + ex.Message);
                    yield break;
                }
            }
            Status = Entries.Count + " listed";
            Debug.Log("ModCatalog: " + Entries.Count + " mod(s) listed at " + url);
        }

        private static void Read(string json)
        {
            JArray mods = JObject.Parse(json)["mods"] as JArray;
            Entries.Clear();
            if (mods == null)
                return;
            foreach (JToken mod in mods)
            {
                string file = mod.Value<string>("file");
                if (string.IsNullOrEmpty(file))
                    continue;
                Entries.Add(new Entry
                {
                    File = file,
                    Name = mod.Value<string>("name"),
                    Description = mod.Value<string>("description"),
                    Version = mod.Value<string>("version")
                });
            }
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
