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
        const int SELECTION_PRESET = 29;
        const int SELECTION_PRESET_NEW = 30;
        const int SELECTION_PRESET_DELETE = 31;
        const int SELECTION_ROW = 200;
        const int SELECTION_REMOVE = 300;
        const float FORM_TOP = -220f;
        const float TABLE_HALF_WIDTH = 400f;
        const float TABLE_TOP = 26f;
        const float ROW_PITCH = 38f;
        const int ROWS_SHOWN = 6;
        const float TABLE_BOTTOM = TABLE_TOP + ROW_PITCH * ROWS_SHOWN;
        const float SCROLLBAR_X = TABLE_HALF_WIDTH + 20f;
        const float NAME_X = -385f;
        const float TIER_X = -100f;
        const float TEAM_X = 25f;
        const float LOADOUT_X = 125f;
        const float LOADOUT_STEP = 50f;
        const float REMOVE_X = 345f;
        const float PRESET_Y = TABLE_BOTTOM + 44f;
        const float PRESET_BUTTON_OFFSET = 100f;

        public static BotSettings Unsaved = new BotSettings();
        public static List<BotPreset> Presets = new List<BotPreset>();
        public static BotSlot Draft = NewDraft();
        static int presetIndex = -1;
        static BotSlot lastAdded;
        static int editedIndex = -1;
        static int firstRow;
        static bool isDraggingScrollbar;

        public static BotSettings Settings
        {
            get { return SelectedPreset != null ? SelectedPreset.Settings : Unsaved; }
        }

        public static string SelectedPresetName
        {
            get { return SelectedPreset != null ? SelectedPreset.Name : string.Empty; }
            set { presetIndex = Presets.FindIndex(p => p.Name == value); }
        }

        public static string PresetName
        {
            get { return SelectedPreset != null ? SelectedPreset.Name : string.Empty; }
        }

        public static bool IsRenamingPreset
        {
            get { return SelectedPreset != null && (UIManager.m_menu_selection == SELECTION_PRESET || UIManager.m_menu_selection == SELECTION_PRESET + 500); }
        }

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
            BotForm.Draw(uie, Draft, SELECTION_NAME, FORM_TOP, NetworkMatch.IsTeamMode(MenuManager.mms_mode));

            bool isEditing = editedIndex >= 0;
            Vector2 button = new Vector2(0f, FORM_TOP + BotForm.ROW_STEP * 3f + 46f);
            uie.SelectAndDrawItem(isEditing ? Loc.LS("UPDATE BOT") : Loc.LS("ADD BOT"), button, SELECTION_ADD, !isEditing && Slots.Count >= BotSettings.MAX_BOTS, 0.5f, 0.6f);
            uie.DrawMenuSeparator(button + Vector2.up * 34f);

            DrawTable(uie);
            uie.DrawMenuSeparator(new Vector2(0f, TABLE_BOTTOM + 14f));
            DrawPresetRow(uie);
        }

        static void DrawPresetRow(UIElement uie)
        {
            Vector2 left = new Vector2(-BotForm.COLUMN_X, PRESET_Y);
            BotForm.DrawOptionRow(uie, left, SELECTION_PRESET, Loc.LS("PRESET"), SelectedPreset == null ? Loc.LS("NONE") : SelectedPreset.Name, Presets.Count > 0, IsRenamingPreset);
            MPServerBrowser_UIElement_Draw.SelectAndDrawSmallerItem(uie, Loc.LS("NEW PRESET"), new Vector2(BotForm.COLUMN_X - PRESET_BUTTON_OFFSET, PRESET_Y), SELECTION_PRESET_NEW, false, 0.3f, 0.45f);
            MPServerBrowser_UIElement_Draw.SelectAndDrawSmallerItem(uie, Loc.LS("DELETE PRESET"), new Vector2(BotForm.COLUMN_X + PRESET_BUTTON_OFFSET, PRESET_Y), SELECTION_PRESET_DELETE, SelectedPreset == null, 0.3f, 0.45f);
        }

        static BotPreset SelectedPreset
        {
            get { return presetIndex >= 0 && presetIndex < Presets.Count ? Presets[presetIndex] : null; }
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
            string team = NetworkMatch.IsTeamMode(MenuManager.mms_mode) ? BotForm.TeamName(slot.Team) : "-";
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

        public static void RenamePreset(string typed)
        {
            BotPreset preset = SelectedPreset;
            if (preset == null)
                return;
            preset.Name = BotSettings.CleanPresetName(typed);
            SortPresets(preset);
        }

        static void SortPresets(BotPreset selected)
        {
            Presets.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            presetIndex = Presets.IndexOf(selected);
        }

        static void SelectPreset(int index)
        {
            bool isEnabled = Settings.Enabled;
            presetIndex = index;
            Settings.Enabled = isEnabled;
            firstRow = 0;
            StopEditing();
        }

        static string FreePresetName()
        {
            for (int number = 1; ; number++)
                if (!Presets.Exists(p => p.Name == "PRESET" + number))
                    return "PRESET" + number;
        }

        public static void Select(int selection)
        {
            int direction = UIManager.m_select_dir;
            if (BotForm.Step(Draft, selection - SELECTION_NAME, direction, MPTeams.MenuManagerTeamCount))
            {
                MenuManager.PlayCycleSound(1f, direction);
                return;
            }
            switch (selection)
            {
                case SELECTION_PRESET:
                    if (Presets.Count == 0)
                        return;
                    SelectPreset((presetIndex + 1 + direction + Presets.Count + 1) % (Presets.Count + 1) - 1);
                    break;
                case SELECTION_PRESET_NEW:
                    var preset = new BotPreset { Name = FreePresetName(), Settings = Settings.Copy() };
                    Presets.Add(preset);
                    SortPresets(preset);
                    MenuManager.SetDefaultSelection(SELECTION_PRESET);
                    MenuManager.PlaySelectSound();
                    return;
                case SELECTION_PRESET_DELETE:
                    if (SelectedPreset == null)
                        return;
                    Unsaved = SelectedPreset.Settings;
                    Presets.RemoveAt(presetIndex);
                    presetIndex = -1;
                    MenuManager.PlaySelectSound();
                    return;
                case SELECTION_ADD:
                    if (editedIndex < 0 && Slots.Count >= BotSettings.MAX_BOTS)
                        return;
                    if (Draft.Name.Trim().Length == 0 || Slots.Exists(s => s != EditedSlot && string.Equals(s.Name.Trim(), Draft.Name.Trim(), System.StringComparison.OrdinalIgnoreCase)))
                        Draft.Name = FreeName();
                    if (editedIndex < 0)
                    {
                        Slots.Add(Draft);
                        lastAdded = Draft.Clone();
                        firstRow = HiddenRows;
                    }
                    else
                        Slots[editedIndex] = Draft;
                    StopEditing();
                    MenuManager.PlaySelectSound();
                    return;
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
            BotSlot draft = lastAdded == null ? new BotSlot() : lastAdded.Clone();
            draft.Name = FreeName();
            return draft;
        }

        static string FreeName()
        {
            for (int number = 1; ; number++)
                if (!Settings.Slots.Exists(s => s.Name == number.ToString()))
                    return number.ToString();
        }

    }
}
