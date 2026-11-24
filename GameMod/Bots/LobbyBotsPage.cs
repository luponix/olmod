using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod.Bots
{
    static class LobbyBotsPage
    {
        public const int MICRO_STATE = 20;
        const int SELECTION_OPEN = 40;
        const int SELECTION_NAME = 41;
        const int SELECTION_TIER = 42;
        const int SELECTION_ADD = 43;
        const int SELECTION_REMOVE = 22000;
        const int SELECTION_BACK = 100;
        static readonly Regex botName = new Regex(@"^BOT\[[0-9?]+\] ");
        static readonly FieldInfo privateMatchDataField = typeof(NetworkMatch).GetField("m_private_data", BindingFlags.NonPublic | BindingFlags.Static);

        static string name = string.Empty;
        static int tier = 2;
        static bool keyboardTaken;

        public static bool IsOpen
        {
            get { return MenuManager.m_menu_state == MenuState.MP_PRE_MATCH_MENU && MenuManager.m_menu_micro_state == MICRO_STATE; }
        }

        static bool IsTypingName
        {
            get { return IsOpen && UIManager.m_menu_selection == SELECTION_NAME; }
        }

        public static bool CanManageBots
        {
            get { return MenuManager.m_mp_private_match && privateMatchDataField.GetValue(null) != null; }
        }

        public static bool IsBot(PlayerLobbyData player)
        {
            return botName.IsMatch(player.m_name);
        }

        public static void DrawOpenButton(UIElement uie)
        {
            if (CanManageBots)
                uie.SelectAndDrawItem(Loc.LS("ADD BOT"), new Vector2(0f, 232f), SELECTION_OPEN, false, 0.5f, 0.75f);
        }

        public static void Draw(UIElement uie)
        {
            Vector2 position = new Vector2(0f, 170f + 62f * 2);
            uie.DrawTextEntryLabeled(position - Vector2.right * 250f, SELECTION_NAME, Loc.LS("BOT NAME"), name, false);
            uie.SelectAndDrawStringOptionItem(Loc.LS("DIFFICULTY"), position + Vector2.right * 250f, SELECTION_TIER,
                BotSettings.TierLabel(tier) + " " + BotSettings.TierName(tier), string.Empty, 0.75f);
            position.y += 62f;
            uie.SelectAndDrawItem(Loc.LS("ADD BOT"), position - Vector2.right * 250f, SELECTION_ADD, false, 0.75f, 0.75f);
            uie.SelectAndDrawItem(Loc.LS("BACK"), position + Vector2.right * 250f, SELECTION_BACK, false, 0.75f, 0.75f);
        }

        public static void DrawRemoveButton(UIElement uie, Vector2 position, PlayerLobbyData player, float highlightWidth)
        {
            if (MenuManager.m_menu_state != MenuState.MP_PRE_MATCH_MENU || !IsBot(player) || !CanManageBots)
                return;
            MPServerBrowser_UIElement_Draw.SelectAndDrawSmallerItem(uie, Loc.LS("REMOVE"), position + Vector2.right * (highlightWidth - 30f), SELECTION_REMOVE + player.m_id, false, 0.14f, 0.4f);
        }

        public static bool BeforeLobbyUpdate()
        {
            Controls.m_disable_menu_letter_keys = IsTypingName;
            keyboardTaken = IsTypingName && MenuManager.m_menu_sub_state == MenuSubState.ACTIVE && NetworkMatch.GetMatchState() != MatchState.LOBBY_LOAD_COUNTDOWN;
            if (!keyboardTaken)
                return true;
            UIManager.MouseSelectUpdate();
            UIManager.ControllerMenu();
            if (Controls.JustPressed(CCInput.MENU_BACK))
            {
                Close();
                return false;
            }
            foreach (char ch in Input.inputString)
            {
                if (ch == '\b')
                {
                    if (name.Length > 0)
                        name = name.Substring(0, name.Length - 1);
                }
                else if (ch == '\n' || ch == '\r')
                {
                    name = name.Trim();
                    MenuManager.SetDefaultSelection(SELECTION_ADD);
                    MenuManager.PlayCycleSound();
                }
                else if (!char.IsControl(ch))
                {
                    name = BotSettings.CleanName(name + ch);
                }
            }
            return false;
        }

        public static void AfterLobbyUpdate()
        {
            if (MenuManager.m_menu_sub_state != MenuSubState.ACTIVE)
                return;
            if (IsOpen && (NetworkMatch.GetMatchState() == MatchState.LOBBY_LOAD_COUNTDOWN || !CanManageBots))
            {
                Close();
                return;
            }
            if (keyboardTaken || PlayerShip.m_typing_in_chat || !CanManageBots)
                return;
            bool pushed = UIManager.PushedSelect(100) || (IsOpen && UIManager.m_menu_selection == SELECTION_TIER && UIManager.PushedDir());
            if (!pushed)
                return;
            int selection = UIManager.m_menu_selection;
            if (selection >= SELECTION_REMOVE && selection < SELECTION_REMOVE + 64)
            {
                SendCommand("/KICKBOT C:" + (selection - SELECTION_REMOVE));
                return;
            }
            if (MenuManager.m_menu_micro_state == 0 && selection == SELECTION_OPEN)
            {
                MenuManager.m_menu_micro_state = MICRO_STATE;
                MenuManager.SetDefaultSelection(SELECTION_NAME);
                MenuManager.PlaySelectSound();
                return;
            }
            if (!IsOpen)
                return;
            if (selection == SELECTION_BACK)
            {
                Close();
            }
            else if (selection == SELECTION_TIER)
            {
                tier = (tier + BotSettings.TIER_FILES.Length + UIManager.m_select_dir) % BotSettings.TIER_FILES.Length;
                MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
            }
            else if (selection == SELECTION_ADD)
            {
                SendCommand(("/ADDBOT " + BotSettings.TierName(tier) + " " + name.Trim()).Trim());
                name = string.Empty;
            }
        }

        static void Close()
        {
            Controls.m_disable_menu_letter_keys = false;
            MenuManager.m_menu_micro_state = 0;
            MenuManager.SetDefaultSelection(SELECTION_OPEN);
            MenuManager.PlayCycleSound();
        }

        static void SendCommand(string command)
        {
            Client.SendLobbyChatToServer(PilotManager.PilotName.ToUpper(), command);
            MenuManager.PlaySelectSound();
        }
    }

    [HarmonyPatch(typeof(MenuManager), "MpPreMatchMenuUpdate")]
    class LobbyBotsPage_MenuManager_MpPreMatchMenuUpdate
    {
        static bool Prefix()
        {
            return LobbyBotsPage.BeforeLobbyUpdate();
        }

        static void Postfix()
        {
            LobbyBotsPage.AfterLobbyUpdate();
        }
    }

    [HarmonyPatch(typeof(UIElement), "DrawPlayerName")]
    class LobbyBotsPage_UIElement_DrawPlayerName
    {
        static void Postfix(UIElement __instance, Vector2 pos, PlayerLobbyData pld, float highlight_width)
        {
            LobbyBotsPage.DrawRemoveButton(__instance, pos, pld, highlight_width);
        }
    }
}
