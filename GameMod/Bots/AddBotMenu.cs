using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Overload;
using UnityEngine;

namespace GameMod.Bots
{
    static class AddBotMenu
    {
        public const int OPEN_SELECTION = 5;
        const int MICRO_STATE = 7;
        const int SELECTION_NAME = 0;
        const int SELECTION_TIER = 1;
        const int SELECTION_ADD = 2;
        const int SELECTION_BACK = 100;
        const string NAME_PREFIX = "BOT ";
        const int MAX_NAME_LENGTH = 16 - 4;

        static readonly string[] TIER_FILES = { "trainee", "rookie", "hotshot", "ace", "insane", "insaneplus" };
        static readonly string[] TIER_NAMES = { "TRAINEE", "ROOKIE", "HOTSHOT", "ACE", "INSANE", "INSANE+" };

        static readonly FieldInfo privateMatchDataField = typeof(NetworkMatch).GetField("m_private_data", BindingFlags.NonPublic | BindingFlags.Static);

        static string botName = "1";
        static int tierIndex = 2;
        static int botsStarted;
        static bool keyboardTaken;

        public static bool HasStartedBots
        {
            get { return botsStarted > 0; }
        }

        public static bool IsOpen
        {
            get { return MenuManager.m_menu_state == MenuState.MP_PRE_MATCH_MENU && MenuManager.m_menu_micro_state == MICRO_STATE; }
        }

        public static bool IsTypingName
        {
            get { return IsOpen && UIManager.m_menu_selection == SELECTION_NAME; }
        }

        public static void Draw(UIElement uie)
        {
            Vector2 position;
            position.x = 0f;
            position.y = 170f + 62f * 2;
            uie.DrawTextEntryLabeled(position - Vector2.right * 250f, SELECTION_NAME, Loc.LS("BOT NAME"), botName, false);
            uie.SelectAndDrawStringOptionItem(Loc.LS("DIFFICULTY"), position + Vector2.right * 250f, SELECTION_TIER, TIER_NAMES[tierIndex], string.Empty, 0.75f);
            position.y += 62f;
            uie.SelectAndDrawItem(Loc.LS("ADD BOT"), position - Vector2.right * 250f, SELECTION_ADD, false, 0.75f, 0.75f);
            uie.SelectAndDrawItem(Loc.LS("BACK"), position + Vector2.right * 250f, SELECTION_BACK, false, 0.75f, 0.75f);
        }

        public static bool BeforeLobbyUpdate()
        {
            Controls.m_disable_menu_letter_keys = IsTypingName;
            keyboardTaken = IsTypingName && NetworkMatch.GetMatchState() != MatchState.LOBBY_LOAD_COUNTDOWN;
            if (!keyboardTaken)
                return true;
            UIManager.MouseSelectUpdate();
            if (Controls.JustPressed(CCInput.MENU_BACK))
            {
                Close();
                return false;
            }
            foreach (char ch in Input.inputString)
            {
                if (ch == '\b')
                {
                    if (botName.Length > 0)
                        botName = botName.Substring(0, botName.Length - 1);
                }
                else if (ch == '\n' || ch == '\r')
                {
                    botName = botName.Trim();
                    MenuManager.SetDefaultSelection(SELECTION_ADD);
                    MenuManager.PlayCycleSound();
                }
                else if (botName.Length < MAX_NAME_LENGTH && !char.IsControl(ch) && ch != '"' && ch != '\\')
                {
                    botName += ch;
                }
            }
            return false;
        }

        public static void AfterLobbyUpdate()
        {
            if (MenuManager.m_menu_state != MenuState.MP_PRE_MATCH_MENU || MenuManager.m_menu_sub_state != MenuSubState.ACTIVE)
                return;
            if (IsOpen && NetworkMatch.GetMatchState() == MatchState.LOBBY_LOAD_COUNTDOWN)
            {
                Close();
                return;
            }
            if (keyboardTaken || PlayerShip.m_typing_in_chat)
                return;
            if (MenuManager.m_menu_micro_state == 0 && UIManager.m_menu_selection == OPEN_SELECTION && UIManager.PushedSelect(-1))
            {
                MenuManager.m_menu_micro_state = MICRO_STATE;
                MenuManager.SetDefaultSelection(SELECTION_NAME);
                MenuManager.PlaySelectSound();
                return;
            }
            if (!IsOpen || IsTypingName)
                return;
            if (UIManager.m_menu_selection == SELECTION_TIER && (UIManager.PushedSelect(-1) || UIManager.PushedDir()))
            {
                tierIndex = (tierIndex + UIManager.m_select_dir + TIER_FILES.Length) % TIER_FILES.Length;
                MenuManager.PlayCycleSound();
            }
            else if (UIManager.PushedSelect(SELECTION_BACK))
            {
                if (UIManager.m_menu_selection == SELECTION_ADD)
                    StartBot();
                else if (UIManager.m_menu_selection == SELECTION_BACK)
                    Close();
            }
        }

        static void Close()
        {
            Controls.m_disable_menu_letter_keys = false;
            MenuManager.m_menu_micro_state = 0;
            MenuManager.SetDefaultSelection(OPEN_SELECTION);
            MenuManager.PlayCycleSound();
        }

        static void StartBot()
        {
            string name = NAME_PREFIX + botName.Trim();
            string connect = MatchAddress();
            string player = Olbot.PlayerPath;
            if (botName.Trim().Length == 0)
            {
                MenuManager.AddMpStatus(Loc.LS("THE BOT NEEDS A NAME"), 1f);
                return;
            }
            if (connect == null)
            {
                MenuManager.AddMpStatus(Loc.LS("BOTS CAN ONLY JOIN AN INTERNET MATCH"), 1f);
                return;
            }
            if (Application.platform == RuntimePlatform.OSXPlayer || !File.Exists(player))
            {
                UnityEngine.Debug.LogFormat("[Bots] no bot player at {0}", player);
                MenuManager.AddMpStatus(Loc.LS("BOTS ARE NOT INSTALLED FOR THIS SYSTEM"), 1f);
                return;
            }

            string logDir = Path.Combine(Application.persistentDataPath, "BotLogs");
            Directory.CreateDirectory(logDir);
            botsStarted++;
            var arguments = new List<string> {
                "-batchmode", "-nographics",
                "-logFile", Path.Combine(logDir, "bot" + botsStarted + ".log"),
                "-olbot-connect", connect,
                "-olbot-pilot", name,
                "-olbot-brain", "-olbot-policy", "pilot",
                "-olbot-tier", TIER_FILES[tierIndex],
                "-olbot-id", Guid.NewGuid().ToString(),
                "-olbot-parent", Process.GetCurrentProcess().Id.ToString(),
                "-olbot-geometry-cache", LevelCapture.CacheDir,
            };
            string level = MatchLevelKey();
            if (level != null)
            {
                arguments.Add("-olbot-level");
                arguments.Add(level);
            }
            try
            {
                Olbot.Start(arguments).Dispose();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogFormat("[Bots] cannot start {0}: {1}", player, ex.Message);
                MenuManager.AddMpStatus(Loc.LS("THE BOT DID NOT START"), 1f);
                return;
            }
            UnityEngine.Debug.LogFormat("[Bots] started {0} ({1}), log {2}", name, TIER_FILES[tierIndex], arguments[3]);
            MenuManager.AddMpStatus(string.Format(Loc.LS("{0} IS JOINING"), name.ToUpperInvariant()), 1f);
            botName = (botsStarted + 1).ToString();
        }

        static string MatchLevelKey()
        {
            LobbyStatusMessage status = NetworkMatch.m_last_lobby_status;
            if (status == null)
                return null;
            if (!string.IsNullOrEmpty(status.m_match_playlist_addon_idstringhash))
                return status.m_match_playlist_addon_idstringhash;
            int index = status.m_match_playlist_level_index;
            Mission mission = GameManager.MultiplayerMission;
            if (index < 0 || index >= mission.NumLevels)
                return null;
            return mission.IsLevelAnAddon(index) ? mission.GetAddOnLevelIdStringHash(index) : mission.GetLevelFileName(index);
        }

        static string MatchAddress()
        {
            if (!MPInternet.Enabled)
                return null;
            string token = NetworkMatch.m_match_req_password;
            if (string.IsNullOrEmpty(token))
            {
                var privateData = (PrivateMatchDataMessage)privateMatchDataField.GetValue(null);
                token = privateData == null ? null : privateData.m_password;
            }
            return string.IsNullOrEmpty(token) ? null : token.Trim();
        }
    }
}
