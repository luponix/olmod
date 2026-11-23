using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod
{
    static class MenuDriver
    {
        const float MIN_STEP_SECONDS = 0.3f;
        const float ITEM_VISIBLE_SECONDS = 0.25f;

        public static readonly bool Enabled = Core.GameMod.FindArg("-menu-driver");

        static readonly Queue<string> pending = new Queue<string>();
        static readonly Dictionary<int, KeyValuePair<string, float>> itemsBySelection = new Dictionary<int, KeyValuePair<string, float>>();
        static string inboxPath;
        static long inboxOffset;
        static float nextStepTime;
        static CCInput? pressInput;
        static int pressSelection = -1;

        public static void RegisterCommands()
        {
            if (!Enabled)
                return;
            uConsole.RegisterCommand("ui", "Menu driver: state | items | select <id|text> | press <CCInput> | set <Type.field> <value>", CmdUi);
        }

        static void CmdUi()
        {
            var words = new List<string>();
            for (int i = 0, n = uConsole.GetNumParameters(); i < n; i++)
                words.Add(uConsole.GetString());
            Run(string.Join(" ", words.ToArray()));
        }

        public static void Tick()
        {
            if (!Enabled)
                return;
            ReadInbox();
            if (pending.Count == 0 || Time.realtimeSinceStartup < nextStepTime)
                return;
            string line = pending.Dequeue();
            nextStepTime = Time.realtimeSinceStartup + MIN_STEP_SECONDS;
            Log("> " + line);
            string[] words = line.Split(new[] { ' ' }, 2);
            if (words[0] == "wait")
            {
                int milliseconds;
                if (words.Length > 1 && int.TryParse(words[1], out milliseconds))
                    nextStepTime = Time.realtimeSinceStartup + milliseconds / 1000f;
                return;
            }
            if (words[0] == "ui")
            {
                Run(words.Length > 1 ? words[1] : string.Empty);
                return;
            }
            object result = uConsole.RunCommand(line);
            if (result != null)
                Log("= " + result);
        }

        static void ReadInbox()
        {
            if (inboxPath == null)
                inboxPath = Path.Combine(Application.persistentDataPath, "menudriver.txt");
            if (!File.Exists(inboxPath))
                return;
            using (var stream = new FileStream(inboxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length < inboxOffset)
                    inboxOffset = 0;
                if (stream.Length == inboxOffset)
                    return;
                stream.Seek(inboxOffset, SeekOrigin.Begin);
                string text = new StreamReader(stream).ReadToEnd();
                int lastNewline = text.LastIndexOf('\n');
                if (lastNewline < 0)
                    return;
                inboxOffset += System.Text.Encoding.UTF8.GetByteCount(text.Substring(0, lastNewline + 1));
                foreach (string line in text.Substring(0, lastNewline).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")))
                    pending.Enqueue(line);
            }
        }

        static void Run(string command)
        {
            string[] words = command.Trim().Split(new[] { ' ' }, 2);
            string argument = words.Length > 1 ? words[1].Trim() : string.Empty;
            switch (words[0])
            {
                case "state":
                    LogState();
                    break;
                case "items":
                    LogItems();
                    break;
                case "select":
                    Select(argument);
                    break;
                case "press":
                    Press(argument);
                    break;
                case "set":
                    SetField(argument);
                    break;
                default:
                    Log("unknown command: " + command);
                    break;
            }
        }

        static void LogState()
        {
            Log(string.Format("menu {0}/{1}/{2} selection {3}, game {4}, gameplay {5}, match {6}, connected {7}, players {8}",
                MenuManager.m_menu_state, MenuManager.m_menu_sub_state, MenuManager.m_menu_micro_state, UIManager.m_menu_selection,
                GameManager.m_game_state, GameplayManager.m_gameplay_state, NetworkMatch.GetMatchState(),
                Client.IsConnected(), Overload.NetworkManager.m_Players.Count));
            LogItems();
        }

        static void LogItems()
        {
            foreach (var item in VisibleItems().OrderBy(entry => entry.Key))
                Log(string.Format("  {0}: {1}", item.Key, item.Value));
        }

        static IEnumerable<KeyValuePair<int, string>> VisibleItems()
        {
            float since = Time.realtimeSinceStartup - ITEM_VISIBLE_SECONDS;
            return itemsBySelection.Where(entry => entry.Value.Value >= since)
                .Select(entry => new KeyValuePair<int, string>(entry.Key, entry.Value.Key));
        }

        static void Select(string idOrText)
        {
            int selection;
            if (!int.TryParse(idOrText, out selection))
            {
                var match = VisibleItems().Where(item => item.Value.IndexOf(idOrText, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (match.Count != 1)
                {
                    Log(string.Format("select: {0} items match \"{1}\"", match.Count, idOrText));
                    LogItems();
                    return;
                }
                selection = match[0].Key;
            }
            pressSelection = selection;
            pressInput = CCInput.MENU_SELECT;
        }

        static void Press(string inputName)
        {
            try
            {
                pressInput = (CCInput)Enum.Parse(typeof(CCInput), inputName, true);
            }
            catch (ArgumentException)
            {
                Log("press: no CCInput " + inputName);
            }
        }

        static void SetField(string argument)
        {
            string[] words = argument.Split(new[] { ' ' }, 2);
            int dot = words[0].LastIndexOf('.');
            if (dot < 0)
            {
                Log("set: expected <Type.field> <value>");
                return;
            }
            string typeName = words[0].Substring(0, dot);
            Type type = AccessTools.TypeByName(typeName) ?? AccessTools.TypeByName("Overload." + typeName);
            FieldInfo field = type == null ? null : AccessTools.Field(type, words[0].Substring(dot + 1));
            if (field == null || !field.IsStatic)
            {
                Log("set: no static field " + words[0]);
                return;
            }
            string text = words.Length > 1 ? words[1] : string.Empty;
            object value = field.FieldType.IsEnum ? Enum.Parse(field.FieldType, text, true)
                : Convert.ChangeType(text, field.FieldType, CultureInfo.InvariantCulture);
            field.SetValue(null, value);
            Log(string.Format("set {0} = {1}", words[0], value));
        }

        public static void RecordItem(int selection, string text)
        {
            itemsBySelection[selection] = new KeyValuePair<string, float>(text, Time.realtimeSinceStartup);
        }

        public static void InjectPress()
        {
            if (pressInput == null)
                return;
            if (pressSelection >= 0)
                UIManager.m_menu_selection = pressSelection;
            Controls.m_input_count[(int)pressInput.Value] = 1;
            pressInput = null;
            pressSelection = -1;
        }

        static void Log(string line)
        {
            uConsole.Log(line);
            Debug.Log("[MenuDriver] " + line);
        }
    }
}
