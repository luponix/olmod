using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace GameMod
{
    // Loc.LS only knows the game's baked keys and returns any other key unchanged, so olmod
    // adds its own "ENGLISH KEY<TAB>TRANSLATION" lines to that table. Engine keys win.
    static class Localization
    {
        private static readonly FieldInfo locStringsField = AccessTools.Field(typeof(Loc), "m_strings");

        public static void AddOlmodStrings()
        {
            var locStrings = locStringsField.GetValue(null) as Dictionary<string, string>;
            if (locStrings == null)
                return; // Loc not initialized yet
            string code = Loc.CurrentLanguageCode.ToLowerInvariant();
            var olmodStrings = new Dictionary<string, string>();
            ReadEmbeddedFile(code, olmodStrings);
            ReadOverrideFile(code, olmodStrings);
            int added = 0;
            foreach (var entry in olmodStrings)
            {
                if (locStrings.ContainsKey(entry.Key))
                    continue;
                locStrings.Add(entry.Key, entry.Value);
                added++;
            }
            if (olmodStrings.Count > 0)
                Debug.LogFormat("[Loc] added {0} of {1} olmod strings for language {2}", added, olmodStrings.Count, code);
        }

        private static void ReadEmbeddedFile(string code, Dictionary<string, string> olmodStrings)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GameMod.Resources.lang." + code + ".txt"))
            {
                if (stream == null)
                    return;
                using (var reader = new StreamReader(stream))
                    ReadLines(reader, olmodStrings);
            }
        }

        // <olmod dir>/lang/<code>.txt lets translators test text without a rebuild
        private static void ReadOverrideFile(string code, Dictionary<string, string> olmodStrings)
        {
            if (string.IsNullOrEmpty(Config.OLModDir))
                return;
            string path = Path.Combine(Path.Combine(Config.OLModDir, "lang"), code + ".txt");
            if (!File.Exists(path))
                return;
            try
            {
                using (var reader = new StreamReader(path))
                    ReadLines(reader, olmodStrings);
                Debug.LogFormat("[Loc] read override file {0}", path);
            }
            catch (IOException ex)
            {
                Debug.LogErrorFormat("[Loc] cannot read override file {0}: {1}", path, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.LogErrorFormat("[Loc] cannot read override file {0}: {1}", path, ex.Message);
            }
        }

        private static void ReadLines(TextReader reader, Dictionary<string, string> olmodStrings)
        {
            for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
            {
                line = line.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#')
                    continue;
                int tab = line.IndexOf('\t');
                if (tab <= 0 || tab == line.Length - 1)
                    continue; // no key or no translation
                olmodStrings[line.Substring(0, tab)] = line.Substring(tab + 1);
            }
        }
    }
}
