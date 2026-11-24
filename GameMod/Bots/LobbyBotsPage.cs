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
        const int SELECTION_ADD = 48;
        const int SELECTION_REMOVE = 22000;
        const int SELECTION_BACK = 100;
        const int SOLID = 199;
        const float BOX_HALF_WIDTH = 440f;
        const float BOX_HALF_HEIGHT = 175f;
        const float EDGE_HALF = 1.5f;
        const float FORM_TOP = -95f;
        static readonly Regex botName = new Regex(@"^BOT\[[0-9?]+\] ");
        static readonly FieldInfo privateMatchDataField = typeof(NetworkMatch).GetField("m_private_data", BindingFlags.NonPublic | BindingFlags.Static);

        static BotSlot draft = new BotSlot { Name = string.Empty };
        static bool handledByOverlay;

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
            UIManager.ResetSelectionList();
            UIManager.m_mouse_menu_selection = -1;
            UIManager.DrawQuadUI(Vector2.zero, UIManager.UI_RIGHT, UIManager.UI_BOTTOM, Color.black, 0.8f * uie.m_alpha, SOLID);
            UIManager.DrawQuadUI(Vector2.zero, BOX_HALF_WIDTH, BOX_HALF_HEIGHT, Color.black, uie.m_alpha, SOLID);
            DrawFrame(uie);
            uie.DrawStringSmall(Loc.LS("ADD BOT"), new Vector2(0f, -145f), 0.6f, StringOffset.CENTER, UIManager.m_col_hi5, 1f);
            BotForm.Draw(uie, draft, SELECTION_NAME, FORM_TOP, NetworkMatch.IsTeamMode(NetworkMatch.GetMode()));
            uie.SelectAndDrawItem(Loc.LS("ADD BOT"), new Vector2(-200f, 125f), SELECTION_ADD, false, 0.5f, 0.6f);
            uie.SelectAndDrawItem(Loc.LS("BACK"), new Vector2(200f, 125f), SELECTION_BACK, false, 0.5f, 0.6f);
        }

        static void DrawFrame(UIElement uie)
        {
            Color edge = UIManager.m_col_ui3;
            float alpha = 0.9f * uie.m_alpha;
            UIManager.DrawQuadUI(new Vector2(0f, -BOX_HALF_HEIGHT), BOX_HALF_WIDTH + EDGE_HALF, EDGE_HALF, edge, alpha, SOLID);
            UIManager.DrawQuadUI(new Vector2(0f, BOX_HALF_HEIGHT), BOX_HALF_WIDTH + EDGE_HALF, EDGE_HALF, edge, alpha, SOLID);
            UIManager.DrawQuadUI(new Vector2(-BOX_HALF_WIDTH, 0f), EDGE_HALF, BOX_HALF_HEIGHT - EDGE_HALF, edge, alpha, SOLID);
            UIManager.DrawQuadUI(new Vector2(BOX_HALF_WIDTH, 0f), EDGE_HALF, BOX_HALF_HEIGHT - EDGE_HALF, edge, alpha, SOLID);
        }

        public static void DrawRemoveButton(UIElement uie, Vector2 position, PlayerLobbyData player, float nameOffset)
        {
            if (MenuManager.m_menu_state != MenuState.MP_PRE_MATCH_MENU || IsOpen || !IsBot(player) || !CanManageBots)
                return;
            MPServerBrowser_UIElement_Draw.SelectAndDrawSmallerItem(uie, Loc.LS("REMOVE"), position + Vector2.right * (-nameOffset - 100f), SELECTION_REMOVE + player.m_id, false, 0.14f, 0.4f);
        }

        public static bool BeforeLobbyUpdate()
        {
            handledByOverlay = false;
            Controls.m_disable_menu_letter_keys = IsTypingName;
            if (!IsOpen || MenuManager.m_menu_sub_state != MenuSubState.ACTIVE)
                return true;
            if (NetworkMatch.GetMatchState() == MatchState.LOBBY_LOAD_COUNTDOWN || !CanManageBots)
            {
                Close();
                return true;
            }
            handledByOverlay = true;
            UIManager.MouseSelectUpdate();
            UIManager.ControllerMenu();
            if (Controls.JustPressed(CCInput.MENU_BACK))
                Close();
            else if (IsTypingName)
                TypeName();
            else if (UIManager.PushedSelect(100) || (MenuManager.option_dir && UIManager.PushedDir()))
                SelectInOverlay();
            return false;
        }

        static void TypeName()
        {
            foreach (char ch in Input.inputString)
            {
                if (ch == '\b')
                {
                    if (draft.Name.Length > 0)
                        draft.Name = draft.Name.Substring(0, draft.Name.Length - 1);
                }
                else if (ch == '\n' || ch == '\r')
                {
                    draft.Name = draft.Name.Trim();
                    MenuManager.SetDefaultSelection(SELECTION_NAME + 1);
                    MenuManager.PlayCycleSound();
                }
                else if (!char.IsControl(ch))
                {
                    draft.Name = BotSettings.CleanName(draft.Name + ch);
                }
            }
        }

        static void SelectInOverlay()
        {
            MenuManager.MaybeReverseOption();
            int selection = UIManager.m_menu_selection;
            int direction = UIManager.m_select_dir;
            if (selection == SELECTION_BACK)
            {
                Close();
            }
            else if (selection == SELECTION_ADD)
            {
                SendCommand(BotSettings.AddBotCommand(draft));
                draft.Name = string.Empty;
                Close();
            }
            else if (BotForm.Step(draft, selection - SELECTION_NAME, direction, MPTeams.NetworkMatchTeamCount))
            {
                MenuManager.PlayCycleSound(1f, direction);
            }
        }

        public static void AfterLobbyUpdate()
        {
            if (handledByOverlay || MenuManager.m_menu_sub_state != MenuSubState.ACTIVE)
                return;
            if (PlayerShip.m_typing_in_chat || !CanManageBots || !UIManager.PushedSelect(100))
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
        static void Postfix(UIElement __instance, Vector2 pos, PlayerLobbyData pld, float name_offset)
        {
            LobbyBotsPage.DrawRemoveButton(__instance, pos, pld, name_offset);
        }
    }
}
