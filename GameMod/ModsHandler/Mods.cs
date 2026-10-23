using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GameMod.ModsHandler
{
    static class Mods
    {
        private const string DEFAULT_CATALOG_URL = "https://voidlog.duckdns.org/overload-mods/index.json";

        public static readonly List<string> Enabled = new List<string>();
        public static readonly List<string> Loaded = new List<string>();

        public static string CatalogUrl
        {
            get
            {
                string configured = Config.Settings.Value<string>("modCatalogUrl");
                return string.IsNullOrEmpty(configured) ? DEFAULT_CATALOG_URL : configured;
            }
        }

        public static bool SelectionWritable { get; private set; }

        private static string startupPath;
        private static bool startupUnreadable;

        public static void LoadEnabled(Harmony harmony)
        {
            uConsole.RegisterCommand("mods",
                "lists installed mods and whether they are on, or \"mods enable|disable <file>\" -- takes effect on the next start",
                new uConsole.DebugCommand(CmdMods));

            if (string.IsNullOrEmpty(Config.OLModDir))
            {
                Debug.Log("Mods: olmod directory unknown, loading nothing");
                return;
            }

            startupPath = Path.Combine(Config.OLModDir, "olmodstartup.json");
            SelectionWritable = DirectoryWritable(Config.OLModDir);

            if (File.Exists(startupPath))
            {
                JObject startup = ReadStartup();
                if (startup == null)
                {
                    startupUnreadable = true;
                    Debug.Log("Mods: " + startupPath + " could not be read, loading nothing and leaving it untouched");
                    return;
                }
                JArray selection = startup["mods"] as JArray;
                if (selection != null)
                    Enabled.AddRange(selection.Values<string>().Where(name => !string.IsNullOrEmpty(name)));
            }
            else
            {
                if (Core.GameMod.FindArg("-modded"))
                    Enabled.AddRange(Installed());
                Save();
                Debug.Log("Mods: seeded " + startupPath + " with " + Enabled.Count + " mod(s)");
            }

            foreach (string name in Enabled)
            {
                string path = Path.Combine(Config.OLModDir, name);
                if (!File.Exists(path))
                {
                    Debug.Log("Mods: " + name + " is enabled but not installed");
                    continue;
                }
                Debug.Log("Loading mod " + name);
                try
                {
                    harmony.PatchAll(Assembly.LoadFile(path));
                    Loaded.Add(name);
                }
                catch (Exception ex)
                {
                    Debug.Log("Running mod " + name + ": " + ex.ToString());
                }
            }

            Core.GameMod.ModsLoaded = string.Join(",", Loaded.ToArray());
            Core.GameMod.Modded = Loaded.Count > 0;

            Debug.Log("Mods: loaded " + Loaded.Count + " of " + Installed().Count() +
                " installed in " + Config.OLModDir +
                (SelectionWritable ? "" : " -- selection NOT writable"));
        }

        public static IEnumerable<string> Installed()
        {
            if (string.IsNullOrEmpty(Config.OLModDir) || !Directory.Exists(Config.OLModDir))
                return new string[0];
            return Directory.GetFiles(Config.OLModDir, "Mod-*.dll")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
        }

        public static bool SelectionChanged
        {
            get { return !Enabled.SequenceEqual(Loaded, StringComparer.OrdinalIgnoreCase); }
        }

        public static bool IsInstalled(string file)
        {
            return Installed().Any(name => string.Equals(name, file, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsEnabled(string file)
        {
            return Enabled.Any(name => string.Equals(name, file, StringComparison.OrdinalIgnoreCase));
        }

        public static void SetEnabled(string file, bool enabled)
        {
            if (enabled == IsEnabled(file))
                return;
            if (enabled)
                Enabled.Add(file);
            else
                Enabled.RemoveAll(name => string.Equals(name, file, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        public static bool Save()
        {
            if (startupPath == null || startupUnreadable)
                return false;
            JObject startup = File.Exists(startupPath) ? ReadStartup() : new JObject();
            if (startup == null)
                return false;
            startup["mods"] = new JArray(Enabled.ToArray());
            try
            {
                string temporary = startupPath + ".tmp";
                File.WriteAllText(temporary, startup.ToString(Formatting.Indented) + Environment.NewLine);
                if (File.Exists(startupPath))
                    File.Delete(startupPath);
                File.Move(temporary, startupPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Log("Mods: cannot write " + startupPath + " -- " + ex.Message);
                return false;
            }
        }

        private static JObject ReadStartup()
        {
            try
            {
                return JObject.Parse(File.ReadAllText(startupPath));
            }
            catch (Exception ex)
            {
                Debug.Log("Mods: " + startupPath + " is not valid JSON -- " + ex.Message);
                return null;
            }
        }

        private static bool DirectoryWritable(string directory)
        {
            string probe = Path.Combine(directory, "olmodstartup.probe");
            try
            {
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void CmdMods()
        {
            if (uConsole.GetNumParameters() == 0)
            {
                PrintState();
                return;
            }

            string verb = uConsole.GetString();
            if (uConsole.GetNumParameters() < 2 || (verb != "enable" && verb != "disable"))
            {
                uConsole.Log("mods: expected \"mods\", \"mods enable <file>\" or \"mods disable <file>\"");
                return;
            }

            string name = uConsole.GetString();
            string installed = Installed().FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (installed == null)
            {
                uConsole.Log("mods: " + name + " is not installed in " + Config.OLModDir);
                return;
            }

            bool wasEnabled = Enabled.Any(n => string.Equals(n, installed, StringComparison.OrdinalIgnoreCase));
            if (verb == "enable" && !wasEnabled)
                Enabled.Add(installed);
            else if (verb == "disable" && wasEnabled)
                Enabled.RemoveAll(n => string.Equals(n, installed, StringComparison.OrdinalIgnoreCase));

            uConsole.Log(Save()
                ? "mods: " + installed + " is now " + verb + "d, restart Overload to apply"
                : "mods: could not save the selection");
        }

        private static void PrintState()
        {
            uConsole.Log("mods: selection in " + (startupPath ?? "(nowhere)") +
                (SelectionWritable ? "" : " (NOT WRITABLE)"));
            foreach (string name in Installed().Union(Enabled, StringComparer.OrdinalIgnoreCase))
            {
                bool enabled = Enabled.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                bool loaded = Loaded.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                string state = !enabled ? "off" : loaded ? "on" : "on, NOT INSTALLED";
                uConsole.Log("  " + name + " -- " + state);
            }
            if (SelectionChanged)
                uConsole.Log("mods: the selection differs from what is loaded, restart Overload to apply");
        }
    }
}
