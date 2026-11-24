using System.Collections.Generic;
using Overload;
using UnityEngine;

namespace GameMod.Bots
{
    static class BotForm
    {
        public const float ROW_STEP = 44f;
        public const float COLUMN_X = 205f;
        const float COLUMN_WIDTH = 400f;
        const float FORM_HALF_HEIGHT = 19f;
        const float BOX_HALF_HEIGHT = 15f;
        const float VALUE_HALF_WIDTH = 115f;
        const int TIER = 1;
        const int TEAM = 2;
        const int PRIMARY1 = 3;
        const int PRIMARY2 = 4;
        const int SECONDARY1 = 5;
        const int SECONDARY2 = 6;

        public static void Draw(UIElement uie, BotSlot slot, int nameSelection, float top, bool isTeamMode)
        {
            Vector2 left = new Vector2(-COLUMN_X, top);
            DrawTextRow(uie, left, nameSelection, Loc.LS("BOT NAME"), slot.PilotName);
            left.y += ROW_STEP;
            DrawOptionRow(uie, left, nameSelection + TIER, Loc.LS("DIFFICULTY"), BotSettings.TierName(slot.Tier), true);
            left.y += ROW_STEP;
            DrawOptionRow(uie, left, nameSelection + TEAM, Loc.LS("TEAM"), TeamName(slot.Team), isTeamMode);

            Vector2 right = new Vector2(COLUMN_X, top);
            DrawOptionRow(uie, right, nameSelection + PRIMARY1, Loc.LS("WEAPON 1"), WeaponName(slot.Primary1), true);
            right.y += ROW_STEP;
            DrawOptionRow(uie, right, nameSelection + PRIMARY2, Loc.LS("WEAPON 2"), WeaponName(slot.Primary2), true);
            right.y += ROW_STEP;
            DrawOptionRow(uie, right, nameSelection + SECONDARY1, Loc.LS("MISSILE 1"), MissileName(slot.Secondary1), true);
            right.y += ROW_STEP;
            DrawOptionRow(uie, right, nameSelection + SECONDARY2, Loc.LS("MISSILE 2"), MissileName(slot.Secondary2), true);
        }

        public static bool Step(BotSlot slot, int row, int direction, int teamCount)
        {
            switch (row)
            {
                case TIER:
                    slot.Tier = Cycle(slot.Tier, direction, BotSettings.TierOptions(), int.MinValue);
                    return true;
                case TEAM:
                    slot.Team = Cycle(slot.Team, direction, BotSettings.TeamOptions(teamCount), int.MinValue);
                    return true;
                case PRIMARY1:
                    slot.Primary1 = (WeaponType)Cycle((int)slot.Primary1, direction, BotSettings.LoadoutOptions((int)WeaponType.NUM, false), (int)slot.Primary2);
                    return true;
                case PRIMARY2:
                    slot.Primary2 = (WeaponType)Cycle((int)slot.Primary2, direction, BotSettings.LoadoutOptions((int)WeaponType.NUM, true), (int)slot.Primary1);
                    return true;
                case SECONDARY1:
                    slot.Secondary1 = (MissileType)Cycle((int)slot.Secondary1, direction, BotSettings.LoadoutOptions((int)MissileType.NUM, false), (int)slot.Secondary2);
                    return true;
                case SECONDARY2:
                    slot.Secondary2 = (MissileType)Cycle((int)slot.Secondary2, direction, BotSettings.LoadoutOptions((int)MissileType.NUM, true), (int)slot.Secondary1);
                    return true;
            }
            return false;
        }

        public static void DrawTextRow(UIElement uie, Vector2 position, int selection, string label, string text)
        {
            uie.TestMouseInRect(position, COLUMN_WIDTH * 0.5f, FORM_HALF_HEIGHT, selection);
            bool isHighlighted = UIManager.m_menu_selection == selection;
            DrawFormBackground(uie, position, isHighlighted, 1f);
            DrawFormLabel(uie, position, label, isHighlighted, 1f);
            Vector2 value = ValuePosition(position);
            Color color = isHighlighted ? UIManager.m_col_ub3 : UIManager.m_col_ui4;
            DrawValueBox(uie, value, isHighlighted, 1f);
            float maxWidth = 2f * VALUE_HALF_WIDTH - 40f;
            uie.DrawStringSmall(text, value, 0.55f, StringOffset.CENTER, color, 1f, maxWidth);
            if (isHighlighted)
                DrawCursor(uie, value, text, color, maxWidth);
        }

        static void DrawCursor(UIElement uie, Vector2 position, string text, Color color, float maxWidth)
        {
            if ((int)(Time.unscaledTime * 4f) % 2 != 0)
                return;
            float textHalfWidth = Mathf.Min(UIManager.GetStringWidth(text, 20f * 0.55f), maxWidth) * 0.5f;
            uie.DrawStringSmall("_", position + Vector2.right * textHalfWidth, 0.55f, StringOffset.LEFT, color, 1f);
        }

        public static void DrawOptionRow(UIElement uie, Vector2 position, int selection, string label, string value, bool isEnabled, bool isTyping = false)
        {
            Vector2 valuePosition = ValuePosition(position);
            if (isEnabled)
            {
                uie.TestMouseInRect(position, COLUMN_WIDTH * 0.5f, FORM_HALF_HEIGHT, selection);
                uie.TestMouseInRect(valuePosition - Vector2.right * VALUE_HALF_WIDTH * 0.5f, VALUE_HALF_WIDTH * 0.5f, FORM_HALF_HEIGHT, selection + 500, false);
            }
            bool isHighlighted = isEnabled && (UIManager.m_menu_selection == selection || UIManager.m_menu_selection == selection + 500);
            if (isHighlighted)
                MenuManager.option_dir = true;
            float alpha = isEnabled ? 1f : 0.3f;
            DrawFormBackground(uie, position, isHighlighted, alpha);
            DrawFormLabel(uie, position, label, isHighlighted, alpha);
            DrawValueBox(uie, valuePosition, isHighlighted, alpha);
            Color color = isHighlighted ? UIManager.m_col_ub3 : UIManager.m_col_ui4;
            uie.DrawStringSmall(value, valuePosition, 0.55f, StringOffset.CENTER, color, alpha, 2f * VALUE_HALF_WIDTH - 60f);
            if (isTyping)
                DrawCursor(uie, valuePosition, value, color, 2f * VALUE_HALF_WIDTH - 60f);
            Color tab = isHighlighted ? UIManager.m_col_ub3 : UIManager.m_col_ui2;
            Color arrow = isHighlighted ? UIManager.m_col_ui6 : UIManager.m_col_ub3;
            float tabHalfWidth = 10f;
            Vector2 arrowOffset = Vector2.right * (VALUE_HALF_WIDTH - tabHalfWidth);
            UIManager.DrawQuadUI(valuePosition - arrowOffset, tabHalfWidth, BOX_HALF_HEIGHT, tab, uie.m_alpha * alpha, 13);
            UIManager.DrawQuadUI(valuePosition + arrowOffset, tabHalfWidth, BOX_HALF_HEIGHT, tab, uie.m_alpha * alpha, 13);
            UIManager.DrawSpriteUI(valuePosition - arrowOffset, 0.22f, 0.22f, arrow, uie.m_alpha * alpha, 81);
            UIManager.DrawSpriteUI(valuePosition + arrowOffset, 0.22f, 0.22f, arrow, uie.m_alpha * alpha, 80);
        }

        static Vector2 ValuePosition(Vector2 row)
        {
            return row + Vector2.right * (COLUMN_WIDTH * 0.5f - VALUE_HALF_WIDTH - 8f);
        }

        static void DrawValueBox(UIElement uie, Vector2 position, bool isHighlighted, float alpha)
        {
            Color box = isHighlighted ? UIManager.m_col_ub3 : UIManager.m_col_ui2;
            box.a = alpha;
            uie.DrawOutlineBackdrop(position, BOX_HALF_HEIGHT, 2f * (VALUE_HALF_WIDTH - BOX_HALF_HEIGHT), box, 2);
        }

        static void DrawFormBackground(UIElement uie, Vector2 position, bool isHighlighted, float alpha)
        {
            Color frame = isHighlighted ? UIManager.m_col_ub3 : UIManager.m_col_ui2;
            frame.a = alpha;
            uie.DrawOutlineBackdrop(position, FORM_HALF_HEIGHT, COLUMN_WIDTH - 2f * FORM_HALF_HEIGHT, frame, 2);
            if (isHighlighted)
            {
                Color bar = Color.Lerp(UIManager.m_col_ui5, UIManager.m_col_ui6, UIManager.m_select_flash * 0.3f);
                bar.a = uie.m_alpha;
                UIManager.DrawQuadBarHorizontal(position, FORM_HALF_HEIGHT, FORM_HALF_HEIGHT, COLUMN_WIDTH - 2f * FORM_HALF_HEIGHT, bar, 12);
                return;
            }
            Color faint = UIManager.m_col_ub0;
            faint.a = uie.m_alpha * 0.08f;
            UIManager.DrawQuadBarHorizontal(position, FORM_HALF_HEIGHT, FORM_HALF_HEIGHT, COLUMN_WIDTH - 2f * FORM_HALF_HEIGHT, faint, 12);
        }

        static void DrawFormLabel(UIElement uie, Vector2 position, string label, bool isHighlighted, float alpha)
        {
            Color color = isHighlighted ? UIManager.m_col_ub3 : UIManager.m_col_ui2;
            uie.DrawStringSmall(label, position - Vector2.right * (COLUMN_WIDTH * 0.5f - 12f), 0.55f, StringOffset.LEFT, color, alpha, COLUMN_WIDTH - 2f * VALUE_HALF_WIDTH - 30f);
        }

        static int Cycle(int value, int direction, List<int> options, int taken)
        {
            int index = options.IndexOf(value);
            do
                index = (index + options.Count + direction) % options.Count;
            while (options[index] == taken && options[index] >= 0 && options.Count > 1);
            return options[index];
        }

        public static string TeamName(int team)
        {
            if (team == BotSettings.AUTO_TEAM)
                return Loc.LS("AUTO");
            if (team == BotSettings.RANDOM)
                return Loc.LS("RANDOM");
            return Loc.LS("TEAM") + " " + (team + 1);
        }

        static string WeaponName(WeaponType weapon)
        {
            if (weapon == WeaponType.NUM)
                return Loc.LS("NONE");
            if ((int)weapon == BotSettings.RANDOM)
                return Loc.LS("RANDOM");
            return Player.GetWeaponNameNoDefault(weapon);
        }

        static string MissileName(MissileType missile)
        {
            if (missile == MissileType.NUM)
                return Loc.LS("NONE");
            if ((int)missile == BotSettings.RANDOM)
                return Loc.LS("RANDOM");
            return Player.GetMissileNameNoDefault(missile);
        }
    }
}
