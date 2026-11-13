using System.Collections.Generic;
using Overload;
using UnityEngine;

namespace GameMod.Bots
{
    static class BotSettingsPage
    {
        public const int MICRO_STATE = 16;
        public const int SELECTION_NAME = 20;
        const int SELECTION_TIER = 21;
        const int SELECTION_TEAM = 22;
        const int SELECTION_PRIMARY1 = 23;
        const int SELECTION_PRIMARY2 = 24;
        const int SELECTION_SECONDARY1 = 25;
        const int SELECTION_SECONDARY2 = 26;
        const int SELECTION_ADD = 27;
        const int SELECTION_SCROLLBAR = 28;
        const int SELECTION_ROW = 200;
        const int SELECTION_REMOVE = 300;
        const float FORM_TOP = -252f;
        const float FORM_ROW_STEP = 44f;
        const float FORM_HALF_HEIGHT = 19f;
        const float BOX_HALF_HEIGHT = 15f;
        const float COLUMN_X = 205f;
        const float COLUMN_WIDTH = 400f;
        const float VALUE_HALF_WIDTH = 115f;
        const float TABLE_HALF_WIDTH = 400f;
        const float TABLE_TOP = -6f;
        const float ROW_PITCH = 40f;
        const int ROWS_SHOWN = 7;
        const float TABLE_BOTTOM = TABLE_TOP + ROW_PITCH * ROWS_SHOWN;
        const float SCROLLBAR_X = TABLE_HALF_WIDTH + 20f;
        const float NAME_X = -385f;
        const float TIER_X = -100f;
        const float TEAM_X = 25f;
        const float LOADOUT_X = 125f;
        const float LOADOUT_STEP = 50f;
        const float REMOVE_X = 345f;

        public static BotSettings Settings = new BotSettings();
        public static BotSlot Draft = NewDraft();
        static int editedIndex = -1;
        static int firstRow;
        static bool isDraggingScrollbar;

        static List<BotSlot> Slots
        {
            get { return Settings.Slots; }
        }

        static int HiddenRows
        {
            get { return Mathf.Max(0, Slots.Count - ROWS_SHOWN); }
        }

        public static void Draw(UIElement uie)
        {
            uie.DrawLabelSmall(Vector2.up * (UIManager.UI_TOP + 70f), Loc.LS("BOT SETTINGS"), 250f);
            UIElement.ToolTipActive = false;

            uie.DrawMenuSeparator(new Vector2(0f, FORM_TOP - 34f));
            Vector2 left = new Vector2(-COLUMN_X, FORM_TOP);
            DrawNameRow(uie, left);
            left.y += FORM_ROW_STEP;
            DrawOptionRow(uie, left, SELECTION_TIER, Loc.LS("DIFFICULTY"), BotSettings.TierName(Draft.Tier), true);
            left.y += FORM_ROW_STEP;
            DrawOptionRow(uie, left, SELECTION_TEAM, Loc.LS("TEAM"), TeamName(Draft.Team), NetworkMatch.IsTeamMode(MenuManager.mms_mode));

            Vector2 right = new Vector2(COLUMN_X, FORM_TOP);
            DrawOptionRow(uie, right, SELECTION_PRIMARY1, Loc.LS("WEAPON 1"), WeaponName(Draft.Primary1), true);
            right.y += FORM_ROW_STEP;
            DrawOptionRow(uie, right, SELECTION_PRIMARY2, Loc.LS("WEAPON 2"), WeaponName(Draft.Primary2), true);
            right.y += FORM_ROW_STEP;
            DrawOptionRow(uie, right, SELECTION_SECONDARY1, Loc.LS("MISSILE 1"), MissileName(Draft.Secondary1), true);
            right.y += FORM_ROW_STEP;
            DrawOptionRow(uie, right, SELECTION_SECONDARY2, Loc.LS("MISSILE 2"), MissileName(Draft.Secondary2), true);

            bool isEditing = editedIndex >= 0;
            Vector2 button = new Vector2(0f, right.y + 46f);
            uie.SelectAndDrawItem(isEditing ? Loc.LS("UPDATE BOT") : Loc.LS("ADD BOT"), button, SELECTION_ADD, !isEditing && Slots.Count >= BotSettings.MAX_BOTS, 0.5f, 0.6f);
            uie.DrawMenuSeparator(button + Vector2.up * 34f);

            DrawTable(uie);
            uie.DrawMenuSeparator(new Vector2(0f, TABLE_BOTTOM + 24f));
        }

        static void DrawNameRow(UIElement uie, Vector2 position)
        {
            uie.TestMouseInRect(position, COLUMN_WIDTH * 0.5f, FORM_HALF_HEIGHT, SELECTION_NAME);
            bool isHighlighted = UIManager.m_menu_selection == SELECTION_NAME;
            DrawFormBackground(uie, position, isHighlighted);
            DrawFormLabel(uie, position, Loc.LS("BOT NAME"), isHighlighted, 1f);
            Vector2 value = ValuePosition(position);
            Color color = isHighlighted ? UIManager.m_col_ub3 : UIManager.m_col_ui4;
            bool showCursor = isHighlighted && (int)(Time.unscaledTime * 4f) % 2 == 0;
            DrawValueBox(uie, value, isHighlighted, 1f);
            uie.DrawStringSmall(Draft.Name + (showCursor ? "_" : string.Empty), value, 0.55f, StringOffset.CENTER, color, 1f, 2f * VALUE_HALF_WIDTH - 40f);
        }

        static void DrawOptionRow(UIElement uie, Vector2 position, int selection, string label, string value, bool isEnabled)
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
            DrawFormBackground(uie, position, isHighlighted);
            DrawFormLabel(uie, position, label, isHighlighted, alpha);
            DrawValueBox(uie, valuePosition, isHighlighted, alpha);
            Color color = isHighlighted ? UIManager.m_col_ub3 : UIManager.m_col_ui4;
            uie.DrawStringSmall(value, valuePosition, 0.55f, StringOffset.CENTER, color, alpha, 2f * VALUE_HALF_WIDTH - 60f);
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

        static void DrawFormBackground(UIElement uie, Vector2 position, bool isHighlighted)
        {
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

        static void DrawTable(UIElement uie)
        {
            Color header = UIManager.m_col_hi2;
            float headerY = TABLE_TOP - 14f;
            uie.DrawStringSmall(Loc.LS("NAME"), new Vector2(NAME_X, headerY), 0.45f, StringOffset.LEFT, header, 1f);
            uie.DrawStringSmall(Loc.LS("DIFFICULTY"), new Vector2(TIER_X, headerY), 0.45f, StringOffset.CENTER, header, 1f);
            uie.DrawStringSmall(Loc.LS("TEAM"), new Vector2(TEAM_X, headerY), 0.45f, StringOffset.CENTER, header, 1f);
            string[] loadoutHeaders = { "W1", "W2", "M1", "M2" };
            for (int i = 0; i < loadoutHeaders.Length; i++)
                uie.DrawStringSmall(loadoutHeaders[i], new Vector2(LOADOUT_X + LOADOUT_STEP * i, headerY), 0.45f, StringOffset.CENTER, header, 1f);

            Color frame = UIManager.m_col_ui0;
            DrawLine(new Vector2(-TABLE_HALF_WIDTH, TABLE_TOP), new Vector2(TABLE_HALF_WIDTH, TABLE_TOP), frame, 1f);
            DrawLine(new Vector2(-TABLE_HALF_WIDTH, TABLE_BOTTOM), new Vector2(TABLE_HALF_WIDTH, TABLE_BOTTOM), frame, 1f);
            DrawLine(new Vector2(-TABLE_HALF_WIDTH, TABLE_TOP), new Vector2(-TABLE_HALF_WIDTH, TABLE_BOTTOM), frame, 1f);
            DrawLine(new Vector2(TABLE_HALF_WIDTH, TABLE_TOP), new Vector2(TABLE_HALF_WIDTH, TABLE_BOTTOM), frame, 1f);

            if (Slots.Count == 0)
                uie.DrawStringSmall(Loc.LS("NO BOTS"), new Vector2(0f, TABLE_TOP + ROW_PITCH * 0.5f), 0.45f, StringOffset.CENTER, UIManager.m_col_ub1, 1f);
            firstRow = Mathf.Clamp(firstRow, 0, HiddenRows);
            for (int i = firstRow; i < Slots.Count && i < firstRow + ROWS_SHOWN; i++)
                DrawRow(uie, TABLE_TOP + ROW_PITCH * (i - firstRow + 0.5f), i);

            if (HiddenRows == 0)
                return;
            float firstRowY = TABLE_TOP + ROW_PITCH * 0.5f;
            uie.TestMouseInRect(new Vector2(SCROLLBAR_X, (TABLE_TOP + TABLE_BOTTOM) * 0.5f), 14f, (TABLE_BOTTOM - TABLE_TOP) * 0.5f, SELECTION_SCROLLBAR, false);
            Menus.DrawScrollbar(uie, ref firstRow, ref isDraggingScrollbar, SCROLLBAR_X, firstRowY, ROW_PITCH, ROWS_SHOWN, Slots.Count);
        }

        static void DrawRow(UIElement uie, float y, int index)
        {
            BotSlot slot = Slots[index];
            float rowRight = REMOVE_X - 50f;
            Vector2 rowCenter = new Vector2((rowRight - TABLE_HALF_WIDTH) * 0.5f, y);
            float rowHalfWidth = (rowRight + TABLE_HALF_WIDTH) * 0.5f;
            uie.TestMouseInRect(rowCenter, rowHalfWidth, ROW_PITCH * 0.5f, SELECTION_ROW + index);

            bool isEdited = index == editedIndex;
            bool isHighlighted = UIManager.m_menu_selection == SELECTION_ROW + index;
            float half = ROW_PITCH * 0.5f - 1f;
            if (index % 2 == 0)
                UIManager.DrawQuadUI(new Vector2(0f, y), TABLE_HALF_WIDTH - 4f, half - 2f, UIManager.m_col_ub0, uie.m_alpha * 0.3f, 13);
            if (isHighlighted && !isEdited)
                UIManager.DrawQuadUI(rowCenter, rowHalfWidth - 4f, half - 2f, UIManager.m_col_ui5, uie.m_alpha * 0.12f, 11);
            Color color = isEdited ? UIManager.m_col_hi5 : isHighlighted ? UIManager.m_col_ui6 : UIManager.m_col_ui3;
            if (isEdited)
            {
                DrawLine(new Vector2(-TABLE_HALF_WIDTH + 4f, y - half), new Vector2(TABLE_HALF_WIDTH - 4f, y - half), color, 1f);
                DrawLine(new Vector2(-TABLE_HALF_WIDTH + 4f, y + half), new Vector2(TABLE_HALF_WIDTH - 4f, y + half), color, 1f);
            }
            uie.DrawStringSmall(slot.PilotName, new Vector2(NAME_X, y), 0.5f, StringOffset.LEFT, color, 1f, TIER_X - NAME_X - 70f);
            uie.DrawStringSmall(BotSettings.TierName(slot.Tier), new Vector2(TIER_X, y), 0.5f, StringOffset.CENTER, color, 1f, 120f);
            string team = NetworkMatch.IsTeamMode(MenuManager.mms_mode) ? TeamName(slot.Team) : "-";
            uie.DrawStringSmall(team, new Vector2(TEAM_X, y), 0.5f, StringOffset.CENTER, color, 1f, 100f);
            DrawLoadoutIcon(uie, new Vector2(LOADOUT_X, y), (int)slot.Primary1, (int)WeaponType.NUM, 26, color);
            DrawLoadoutIcon(uie, new Vector2(LOADOUT_X + LOADOUT_STEP, y), (int)slot.Primary2, (int)WeaponType.NUM, 26, color);
            DrawLoadoutIcon(uie, new Vector2(LOADOUT_X + LOADOUT_STEP * 2f, y), (int)slot.Secondary1, (int)MissileType.NUM, 104, color);
            DrawLoadoutIcon(uie, new Vector2(LOADOUT_X + LOADOUT_STEP * 3f, y), (int)slot.Secondary2, (int)MissileType.NUM, 104, color);
            MPServerBrowser_UIElement_Draw.SelectAndDrawSmallerItem(uie, Loc.LS("REMOVE"), new Vector2(REMOVE_X, y), SELECTION_REMOVE + index, false, 0.15f, 0.45f);
        }

        static void DrawLoadoutIcon(UIElement uie, Vector2 position, int choice, int none, int firstIcon, Color color)
        {
            if (choice >= 0 && choice < none)
                UIManager.DrawSpriteUI(position, 0.25f, 0.25f, color, uie.m_alpha, firstIcon + choice);
            else
                uie.DrawStringSmall(choice == BotSettings.RANDOM ? "?" : "-", position, 0.5f, StringOffset.CENTER, color, 1f);
        }

        public static void Update()
        {
            if (UIManager.m_menu_selection < 0 && Input.GetMouseButtonDown(0))
                StopEditing();
            Vector2 mouse = UIManager.m_mouse_pos;
            float wheel = Input.mouseScrollDelta.y;
            bool isOverTable = Mathf.Abs(mouse.x) < SCROLLBAR_X + 14f && mouse.y > TABLE_TOP && mouse.y < TABLE_BOTTOM;
            if (UIManager.m_menu_use_mouse && isOverTable && wheel != 0f)
                firstRow = Mathf.Clamp(firstRow - (wheel > 0f ? 1 : -1), 0, HiddenRows);
        }

        public static int ScrollWithSelection(int previousSelection)
        {
            if (!Controls.JustPressedMenuDirection())
                return -1;
            int column = previousSelection >= SELECTION_REMOVE ? SELECTION_REMOVE : SELECTION_ROW;
            int index = previousSelection - column;
            if (index < 0 || index >= Slots.Count)
                return -1;
            float menuDirectionY = Controls.MenuDirection().y;
            if (menuDirectionY > 0.45f && index == firstRow + ROWS_SHOWN - 1 && index + 1 < Slots.Count)
            {
                firstRow++;
                return column + index + 1;
            }
            if (menuDirectionY < -0.45f && index == firstRow && firstRow > 0)
            {
                firstRow--;
                return column + index - 1;
            }
            return -1;
        }

        static void DrawLine(Vector2 from, Vector2 to, Color color, float thickness)
        {
            UIManager.DrawQuadCenterLine(from, to, thickness, 0f, color, 4);
        }

        public static void TypeName(string typed, bool entered)
        {
            Draft.Name = BotSettings.CleanName(typed);
            if (!entered)
                return;
            if (Draft.Name.Trim().Length == 0)
                Draft.Name = FreeName();
            MenuManager.SetDefaultSelection(SELECTION_TIER);
        }

        public static void Select(int selection)
        {
            int direction = UIManager.m_select_dir;
            switch (selection)
            {
                case SELECTION_ADD:
                    if (editedIndex < 0 && Slots.Count >= BotSettings.MAX_BOTS)
                        return;
                    if (Draft.Name.Trim().Length == 0 || Slots.Exists(s => s != EditedSlot && string.Equals(s.Name.Trim(), Draft.Name.Trim(), System.StringComparison.OrdinalIgnoreCase)))
                        Draft.Name = FreeName();
                    if (editedIndex < 0)
                    {
                        Slots.Add(Draft);
                        firstRow = HiddenRows;
                    }
                    else
                        Slots[editedIndex] = Draft;
                    StopEditing();
                    MenuManager.PlaySelectSound();
                    return;
                case SELECTION_TIER:
                    Draft.Tier = Cycle(Draft.Tier, direction, BotSettings.TierOptions(), int.MinValue);
                    break;
                case SELECTION_TEAM:
                    Draft.Team = Cycle(Draft.Team, direction, BotSettings.TeamOptions(MPTeams.MenuManagerTeamCount), int.MinValue);
                    break;
                case SELECTION_PRIMARY1:
                    Draft.Primary1 = (WeaponType)Cycle((int)Draft.Primary1, direction, BotSettings.LoadoutOptions((int)WeaponType.NUM, false), (int)Draft.Primary2);
                    break;
                case SELECTION_PRIMARY2:
                    Draft.Primary2 = (WeaponType)Cycle((int)Draft.Primary2, direction, BotSettings.LoadoutOptions((int)WeaponType.NUM, true), (int)Draft.Primary1);
                    break;
                case SELECTION_SECONDARY1:
                    Draft.Secondary1 = (MissileType)Cycle((int)Draft.Secondary1, direction, BotSettings.LoadoutOptions((int)MissileType.NUM, false), (int)Draft.Secondary2);
                    break;
                case SELECTION_SECONDARY2:
                    Draft.Secondary2 = (MissileType)Cycle((int)Draft.Secondary2, direction, BotSettings.LoadoutOptions((int)MissileType.NUM, true), (int)Draft.Secondary1);
                    break;
                default:
                    if (selection >= SELECTION_REMOVE && selection < SELECTION_REMOVE + Slots.Count)
                    {
                        Slots.RemoveAt(selection - SELECTION_REMOVE);
                        StopEditing();
                        MenuManager.PlaySelectSound();
                    }
                    else if (selection >= SELECTION_ROW && selection < SELECTION_ROW + Slots.Count)
                    {
                        if (selection - SELECTION_ROW == editedIndex)
                            StopEditing();
                        else
                            StartEditing(selection - SELECTION_ROW);
                        MenuManager.PlaySelectSound();
                    }
                    return;
            }
            MenuManager.PlayCycleSound(1f, direction);
        }

        public static void Open()
        {
            StopEditing();
            MenuManager.SetDefaultSelection(SELECTION_ADD);
        }

        public static void StopEditing()
        {
            editedIndex = -1;
            Draft = NewDraft();
        }

        static void StartEditing(int index)
        {
            editedIndex = index;
            Draft = Slots[index].Clone();
        }

        static BotSlot EditedSlot
        {
            get { return editedIndex >= 0 ? Slots[editedIndex] : null; }
        }

        static BotSlot NewDraft()
        {
            return new BotSlot { Name = FreeName() };
        }

        static int Cycle(int value, int direction, List<int> options, int taken)
        {
            int index = options.IndexOf(value);
            do
                index = (index + options.Count + direction) % options.Count;
            while (options[index] == taken && options[index] >= 0 && options.Count > 1);
            return options[index];
        }

        static string FreeName()
        {
            for (int number = 1; ; number++)
                if (!Settings.Slots.Exists(s => s.Name == number.ToString()))
                    return number.ToString();
        }

        static string TeamName(int team)
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
