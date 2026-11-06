using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod.ModsHandler
{
    public static class Menu
    {
        public static readonly MenuState msMods = (MenuState)82;
        public static readonly UIElementType uiMods = (UIElementType)95;

        internal const int SEL_MAIN_MENU_MODS = 14;

        private const int SEL_BACK = 100;
        private const int SEL_APPLY = 110;
        private const int SEL_SWITCH = 120;
        private const int SEL_INSTALL = 130;
        private const int SEL_LIST_BASE = 1000;
        private const int ROWS_SHOWN = 8;

        // The list on the left, the selected mod's details and actions on the right,
        // laid out like the DEMOS menu.
        private const float LIST_X = -330f;
        private const float DETAILS_X = 330f;
        private const float COLUMN_HALF_WIDTH = 212f;
        private const float LIST_TOP = -279f;
        private const float ROW_STRIDE = 62f;
        private const float ITEM_SCALE = 0.85f;

        private enum FileCheck { Unknown, Published, Unlisted, Mismatched }

        private class Row
        {
            public string File;
            public string Name;
            public bool Installed;
            public Catalog.Entry Listed;
            public FileCheck Check;
            public string Description;
        }

        private static readonly List<Row> rows = new List<Row>();
        private static int seenChanges;
        private static int firstRow;
        private static int selectedRow = -1;
        private static int previousSelection = -1;

        public static void ModsUpdate(ref float timer)
        {
            UIManager.MouseSelectUpdate();
            switch (MenuManager.m_menu_sub_state)
            {
                case MenuSubState.INIT:
                    if (timer > 0.25f)
                    {
                        seenChanges = Library.Changes;
                        firstRow = 0;
                        selectedRow = 0;
                        RefreshRows();
                        UIManager.CreateUIElement(UIManager.SCREEN_CENTER, 7000, uiMods);
                        MenuManager.m_menu_sub_state = MenuSubState.ACTIVE;
                        timer = 0f;
                        MenuManager.SetDefaultSelection(rows.Count > 0 ? SEL_LIST_BASE : SEL_BACK);
                    }
                    break;

                case MenuSubState.ACTIVE:
                    UIManager.ControllerMenu();

                    if (seenChanges != Library.Changes)
                    {
                        seenChanges = Library.Changes;
                        RefreshRows();
                    }

                    if (timer > 0.25f)
                    {
                        float wheel = Input.GetAxis("Mouse ScrollWheel");
                        if (wheel != 0f)
                            ScrollTo(firstRow + (wheel < 0f ? 1 : -1));
                        ScrollWithSelection();
                    }

                    // The keyboard selects by moving onto a row, the mouse only by clicking:
                    // hovering would re-select every row the pointer crosses on its way
                    // to the actions.
                    int pointedRow = UIManager.m_menu_selection - SEL_LIST_BASE;
                    if (!UIManager.m_menu_use_mouse && pointedRow >= 0 && pointedRow < rows.Count)
                        selectedRow = pointedRow;

                    if (timer > 0.25f && UIManager.PushedSelect(SEL_BACK))
                    {
                        int selection = UIManager.m_menu_selection;
                        if (selection == SEL_BACK)
                        {
                            MenuManager.PlaySelectSound(1f);
                            timer = 0f;
                            UIManager.DestroyAll(false);
                            MenuManager.ChangeMenuState(MenuState.MAIN_MENU, true);
                        }
                        else if (selection == SEL_APPLY)
                        {
                            if (Mods.SelectionChanged)
                            {
                                MenuManager.PlaySelectSound(1f);
                                Relaunch.Now();
                            }
                        }
                        else if (selection >= SEL_LIST_BASE && selection < SEL_LIST_BASE + rows.Count)
                        {
                            if (selectedRow != selection - SEL_LIST_BASE)
                            {
                                selectedRow = selection - SEL_LIST_BASE;
                                MenuManager.PlayCycleSound(1f, 1f);
                            }
                        }
                        else if (selection == SEL_SWITCH)
                        {
                            if (selectedRow >= 0 && selectedRow < rows.Count)
                                SwitchRow(rows[selectedRow]);
                        }
                        else if (selection == SEL_INSTALL)
                        {
                            if (selectedRow >= 0 && selectedRow < rows.Count)
                                InstallOrRemoveRow(rows[selectedRow]);
                        }
                    }
                    break;
            }
        }

        private static void ScrollTo(int first)
        {
            firstRow = Mathf.Clamp(first, 0, Math.Max(0, rows.Count - ROWS_SHOWN));
        }

        // Scrolls the list when UP/DOWN is pressed on the edge row of the visible
        // window, so the keyboard can reach the off-screen rows.
        private static void ScrollWithSelection()
        {
            int row = previousSelection - SEL_LIST_BASE;
            if (row >= 0 && row < rows.Count && Controls.JustPressedMenuDirection())
            {
                float menuDirY = Controls.MenuDirection().y;
                if (menuDirY > 0.45f && row == firstRow + ROWS_SHOWN - 1 && row + 1 < rows.Count)
                {
                    ScrollTo(firstRow + 1);
                    SelectRow(previousSelection + 1);
                }
                else if (menuDirY < -0.45f && row == firstRow && row > 0)
                {
                    ScrollTo(firstRow - 1);
                    SelectRow(previousSelection - 1);
                }
            }
            previousSelection = UIManager.m_menu_selection;
        }

        private static void SelectRow(int selection)
        {
            bool engineAlreadyMoved = UIManager.m_menu_selection != previousSelection;
            UIManager.m_menu_selection = selection;
            UIManager.m_sl_prev_valid = false;
            if (!engineAlreadyMoved)
                MenuManager.PlayMoveSound();
        }

        public static void DrawMods(UIElement ui)
        {
            ui.m_alpha = 1f;
            UIManager.ui_bg_dark = true;
            ui.DrawMenuBG();
            UIElement.ToolTipActive = false;
            ui.DrawHeaderMedium(Vector2.up * (UIManager.UI_TOP + 30f), Loc.LS("MODS"), 265f);
            ui.DrawMenuSeparator(new Vector2(0f, UIManager.UI_TOP + 55f));

            Row selected = selectedRow >= 0 && selectedRow < rows.Count ? rows[selectedRow] : null;
            if (rows.Count == 0)
            {
                ui.DrawLabelSmall(new Vector2(LIST_X, LIST_TOP), Loc.LS("NO MODS INSTALLED OR AVAILABLE"), COLUMN_HALF_WIDTH);
            }
            else
            {
                float y = LIST_TOP;
                for (int i = firstRow; i < rows.Count && i < firstRow + ROWS_SHOWN; i++)
                {
                    DrawRow(ui, rows[i], i, y);
                    y += ROW_STRIDE;
                }
                if (rows.Count > ROWS_SHOWN)
                    ui.DrawStringSmall(string.Format(Loc.LS("{0} - {1} OF {2}"), firstRow + 1,
                            Math.Min(firstRow + ROWS_SHOWN, rows.Count), rows.Count),
                        new Vector2(LIST_X + COLUMN_HALF_WIDTH, y - 26f), 0.35f, StringOffset.RIGHT,
                        UIManager.m_col_ui0, 1f);
            }

            if (selected != null)
                DrawDetails(ui, selected);

            bool downloading = selected != null && selected.File == Library.Downloading;
            bool switchUsable = selected != null && selected.Installed;
            bool installUsable = selected != null && !downloading &&
                (selected.Installed || (selected.Listed != null && selected.Listed.Downloadable));
            string switchLabel = selected != null && Mods.IsEnabled(selected.File) ? Loc.LS("DISABLE") : Loc.LS("ENABLE");
            string installLabel = downloading ? Loc.LS("DOWNLOADING")
                : selected != null && selected.Installed ? Loc.LS("DELETE")
                : Loc.LS("INSTALL");
            ui.SelectAndDrawItem(switchLabel, new Vector2(DETAILS_X, 65f), SEL_SWITCH, !switchUsable, ITEM_SCALE, 0.6f);
            ui.SelectAndDrawItem(installLabel, new Vector2(DETAILS_X, 117f), SEL_INSTALL, !installUsable, ITEM_SCALE, 0.6f);

            ui.SelectAndDrawItem(Loc.LS("APPLY AND RESTART"), new Vector2(DETAILS_X, 197f), SEL_APPLY,
                !Mods.SelectionChanged, ITEM_SCALE, 0.6f);
            if (UIManager.m_menu_selection == SEL_APPLY && Mods.SelectionChanged)
            {
                UIElement.ToolTipActive = true;
                UIElement.ToolTipTitle = Loc.LS("APPLY AND RESTART");
                UIElement.ToolTipDescription =
                    Loc.LS("CLOSES OVERLOAD AND STARTS IT AGAIN SO THE SELECTED MODS LOAD");
            }

            ui.DrawMenuSeparator(new Vector2(0f, UIManager.UI_BOTTOM - 90f));

            // One line fits between the separator and BACK, and a failure matters more
            // than the hint for a button the player is already pointing at.
            string problem = Library.LastError ?? Relaunch.LastError;
            if (!string.IsNullOrEmpty(problem))
                ui.DrawLabelSmallRed(new Vector2(0f, UIManager.UI_BOTTOM - 70f), problem, 900f);
            else if (UIElement.ToolTipActive)
                ui.DrawMenuToolTip(new Vector2(0f, UIManager.UI_BOTTOM - 85f));
            else if (!Catalog.Loaded)
                ui.DrawLabelSmall(new Vector2(0f, UIManager.UI_BOTTOM - 70f),
                    Loc.LS("MOD SITE ") + (Catalog.Status ?? Loc.LS("not checked")).ToUpper(), 600f);

            ui.SelectAndDrawItem(Loc.LS("BACK"), new Vector2(0f, UIManager.UI_BOTTOM - 30f), SEL_BACK, false,
                ITEM_SCALE, 0.6f);
        }

        // Name and version stacked on the left, the state on the right. The engine draws
        // a highlighted item's bar bright, so its text turns dark the way DrawSmallItem's does.
        private static void DrawRow(UIElement ui, Row row, int index, float y)
        {
            Vector2 position = new Vector2(LIST_X, y);
            if (index == selectedRow)
            {
                Vector2 boxHalf = new Vector2(500f * ITEM_SCALE * 0.5f + 32f, 28f);
                Color boxColor = UIManager.m_col_ui2;
                boxColor.a = ui.m_alpha;
                UIManager.DrawBoxOutline(position - boxHalf, position + boxHalf, boxColor, 1.5f, 13);
            }
            ui.SelectAndDrawItem(string.Empty, position, SEL_LIST_BASE + index, false, ITEM_SCALE, 0.5f);

            bool highlighted = UIManager.m_menu_selection == SEL_LIST_BASE + index;
            Color stateColor;
            string state = StateText(row, out stateColor);
            Color nameColor = !row.Installed ? UIManager.m_col_ui0
                : Mods.IsEnabled(row.File) ? UIManager.m_col_ui5
                : UIManager.m_col_ui2;
            Color lineColor = row.Check == FileCheck.Mismatched ? UIManager.m_col_em5 : UIManager.m_col_ui0;
            if (highlighted)
                nameColor = lineColor = stateColor = UIManager.m_col_ub3;

            float left = LIST_X - COLUMN_HALF_WIDTH + 8f;
            float right = LIST_X + COLUMN_HALF_WIDTH - 8f;
            float stateWidth = UIManager.GetStringWidth(state, 20f * 0.4f);
            float nameWidth = right - left - stateWidth - 16f;
            ui.DrawStringSmall(row.Name, new Vector2(left, y - 8f), 0.5f, StringOffset.LEFT, nameColor, 1f, nameWidth);
            ui.DrawStringSmall(RowLine(row), new Vector2(left, y + 11f), 0.3f, StringOffset.LEFT, lineColor, 1f, nameWidth);
            ui.DrawStringSmall(state, new Vector2(right, y), 0.4f, StringOffset.RIGHT, stateColor, 1f);
        }

        // The selected mod's name and description, above its actions.
        private static void DrawDetails(UIElement ui, Row row)
        {
            float left = DETAILS_X - COLUMN_HALF_WIDTH + 6f;
            float width = COLUMN_HALF_WIDTH * 2f - 12f;
            ui.DrawStringSmall(row.Name, new Vector2(left, LIST_TOP - 6f), 0.6f, StringOffset.LEFT,
                UIManager.m_col_ui5, 1f, width);
            ui.DrawVariableSeparator(new Vector2(DETAILS_X, LIST_TOP + 18f), COLUMN_HALF_WIDTH);

            Vector2 position = new Vector2(left, LIST_TOP + 42f);
            string warning = Warning(row);
            if (warning != null)
            {
                ui.DrawStringSmall(warning, position, 0.4f, StringOffset.LEFT,
                    row.Check == FileCheck.Mismatched ? UIManager.m_col_em5 : UIManager.m_col_hi3, 1f, width);
                position.y += 34f;
            }

            foreach (string line in WrapToWidth(row.Description, 0.4f, width))
            {
                if (position.y > 30f) // stay clear of the action buttons
                    break;
                ui.DrawStringSmall(line, position, 0.4f, StringOffset.LEFT, UIManager.m_col_ui3, 1f);
                position.y += 22f;
            }
        }

        // The engine squishes an over-wide string instead of wrapping it, so the lines are
        // measured here at the size they will be drawn. A blank line in the text starts a
        // paragraph and is kept as an empty line; single line breaks are wrapped over.
        private static IEnumerable<string> WrapToWidth(string text, float scale, float width)
        {
            if (string.IsNullOrEmpty(text))
                yield break;
            string[] paragraphs = Regex.Split(text.Replace("\r", "").Trim(), @"\n[ \t]*\n\s*");
            for (int i = 0; i < paragraphs.Length; i++)
            {
                if (i > 0)
                    yield return "";
                string line = "";
                foreach (string word in paragraphs[i].Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string grown = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && UIManager.GetStringWidth(grown, 20f * scale) > width)
                    {
                        yield return line;
                        line = word;
                    }
                    else
                    {
                        line = grown;
                    }
                }
                yield return line;
            }
        }

        // Loaded is what runs now, Enabled what runs after the next start; a mod whose two
        // disagree is waiting for APPLY AND RESTART.
        private static string StateText(Row row, out Color color)
        {
            bool enabled = Mods.IsEnabled(row.File);
            bool loaded = Mods.IsLoaded(row.File);
            if (row.File == Library.Downloading)
            {
                color = UIManager.m_col_hi5;
                return Loc.LS("DOWNLOADING");
            }
            if (!row.Installed)
            {
                color = enabled ? UIManager.m_col_em5 : UIManager.m_col_ui0;
                return enabled ? Loc.LS("MISSING") : Loc.LS("NOT INSTALLED");
            }
            if (enabled != loaded)
            {
                color = UIManager.m_col_hi5;
                return enabled ? Loc.LS("ON AFTER RESTART") : Loc.LS("OFF AFTER RESTART");
            }
            color = enabled ? UIManager.m_col_good_ping : UIManager.m_col_ui1;
            return enabled ? Loc.LS("ACTIVE") : Loc.LS("OFF");
        }

        private static string RowLine(Row row)
        {
            if (row.Check == FileCheck.Unlisted || row.Check == FileCheck.Mismatched)
                return CheckText(row.Check);
            string version = row.Listed != null ? row.Listed.Version : null;
            return string.IsNullOrEmpty(version) ? row.File : Loc.LS("VERSION") + " " + version;
        }

        private static string CheckText(FileCheck check)
        {
            return check == FileCheck.Mismatched
                ? Loc.LS("DOES NOT MATCH THE PUBLISHED FILE")
                : Loc.LS("NOT LISTED ON THE MOD SITE");
        }

        private static string Warning(Row row)
        {
            if (row.Check == FileCheck.Unlisted || row.Check == FileCheck.Mismatched)
                return CheckText(row.Check);
            if (!row.Installed && (row.Listed == null || !row.Listed.Downloadable))
                return Loc.LS("NO VERIFIABLE DOWNLOAD PUBLISHED");
            return null;
        }

        private static void SwitchRow(Row row)
        {
            if (!row.Installed)
            {
                MenuManager.PlayCycleSound(1f, -1f);
                return;
            }
            Mods.SetEnabled(row.File, !Mods.IsEnabled(row.File));
            MenuManager.PlayCycleSound(1f, 1f);
        }

        private static void InstallOrRemoveRow(Row row)
        {
            if (row.Installed)
            {
                if (Library.Delete(row.File))
                    MenuManager.PlaySelectSound(1f);
                else
                    MenuManager.PlayCycleSound(1f, -1f);
                return;
            }
            if (row.Listed == null || !row.Listed.Downloadable || Library.Downloading != null)
            {
                MenuManager.PlayCycleSound(1f, -1f);
                return;
            }
            MenuManager.PlaySelectSound(1f);
            Library.Download(row.Listed);
        }

        private static void RefreshRows()
        {
            rows.Clear();
            foreach (string file in Mods.Installed())
                rows.Add(RowFor(file, true));
            foreach (string file in Mods.Enabled)
                if (!Mods.IsInstalled(file))
                    rows.Add(RowFor(file, false));
            foreach (string file in Catalog.NotInstalled())
                if (!rows.Exists(row => string.Equals(row.File, file, StringComparison.OrdinalIgnoreCase)))
                    rows.Add(RowFor(file, false));
            ScrollTo(firstRow);
            selectedRow = rows.Count > 0 ? Mathf.Clamp(selectedRow, 0, rows.Count - 1) : -1;
        }

        private static Row RowFor(string file, bool installed)
        {
            Catalog.Entry listed = Catalog.Find(file);
            FileCheck check = FileCheck.Unknown;
            if (installed && Catalog.Loaded)
                check = listed == null || string.IsNullOrEmpty(listed.Sha256) ? FileCheck.Unlisted
                    : string.Equals(Library.HashOf(file), listed.Sha256, StringComparison.OrdinalIgnoreCase)
                        ? FileCheck.Published : FileCheck.Mismatched;
            // The site's text is current for a published file; a local build or an
            // unlisted mod is described only by the .txt it came with.
            string description = listed != null && check != FileCheck.Mismatched && !string.IsNullOrEmpty(listed.Description)
                ? listed.Description
                : (installed ? Library.DescriptionOf(file) : null) ?? (listed != null ? listed.Description : null);
            return new Row
            {
                File = file,
                Name = Catalog.DisplayName(file),
                Installed = installed,
                Listed = listed,
                Check = check,
                Description = description
            };
        }
    }

    [HarmonyPatch(typeof(MenuManager), "MainMenuUpdate")]
    class Menu_MenuManager_MainMenuUpdate
    {
        static void Postfix()
        {
            if (MenuManager.m_menu_sub_state == MenuSubState.ACTIVE &&
                !NetworkManager.IsHeadless() && UIManager.PushedSelect() &&
                UIManager.m_menu_selection == Menu.SEL_MAIN_MENU_MODS)
            {
                UIManager.DestroyAll();
                MenuManager.ChangeMenuState(Menu.msMods);
                MenuManager.PlaySelectSound(1f);
            }
        }
    }

    [HarmonyPatch(typeof(MenuManager), "Update")]
    class Menu_MenuManager_Update
    {
        static void Postfix(ref float ___m_menu_state_timer)
        {
            if (MenuManager.m_menu_state == Menu.msMods)
                Menu.ModsUpdate(ref ___m_menu_state_timer);
        }
    }

    [HarmonyPatch(typeof(UIElement), "Draw")]
    class Menu_UIElement_Draw
    {
        static void Postfix(UIElement __instance)
        {
            if (__instance.m_type == Menu.uiMods && !__instance.m_fade_die)
                Menu.DrawMods(__instance);
        }
    }
}
