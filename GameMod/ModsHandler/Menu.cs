using System;
using System.Collections.Generic;
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
        private const int SEL_LIST_BASE = 1000; // two buttons per row: +0 switches it on, +1 installs it
        private const int ROWS_SHOWN = 7;

        // A row is name, two buttons only as wide as their longest label, then the details
        // panel the freed space pays for.
        private const float NAME_LEFT = -480f;
        private const float NAME_WIDTH = 380f;
        private const float DETAILS_LEFT = 240f;
        private const float DETAILS_WIDTH = 380f;

        private enum FileCheck { Unknown, Published, Unlisted, Mismatched }

        private class Row
        {
            public string File;
            public string Name;
            public bool Installed;
            public Catalog.Entry Listed;
            public FileCheck Check;
        }

        private static readonly List<Row> rows = new List<Row>();
        private static int seenChanges;
        private static int firstRow;
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
                        else if (selection >= SEL_LIST_BASE && selection < SEL_LIST_BASE + rows.Count * 2)
                        {
                            int button = selection - SEL_LIST_BASE;
                            if (button % 2 == 0)
                                SwitchRow(rows[button / 2]);
                            else
                                InstallOrRemoveRow(rows[button / 2]);
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
            int button = previousSelection - SEL_LIST_BASE;
            int row = button / 2;
            if (button >= 0 && row < rows.Count && Controls.JustPressedMenuDirection())
            {
                float menuDirY = Controls.MenuDirection().y;
                if (menuDirY > 0.45f && row == firstRow + ROWS_SHOWN - 1 && row + 1 < rows.Count)
                {
                    ScrollTo(firstRow + 1);
                    SelectButton(previousSelection + 2);
                }
                else if (menuDirY < -0.45f && row == firstRow && row > 0)
                {
                    ScrollTo(firstRow - 1);
                    SelectButton(previousSelection - 2);
                }
            }
            previousSelection = UIManager.m_menu_selection;
        }

        private static void SelectButton(int selection)
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

            if (!Catalog.Loaded)
                ui.DrawLabelSmall(new Vector2(0f, UIManager.UI_TOP + 105f),
                    Loc.LS("MOD SITE ") + (Catalog.Status ?? Loc.LS("not checked")).ToUpper(), 600f);

            float top = UIManager.UI_TOP + 150f;
            float y = top;
            if (rows.Count == 0)
            {
                ui.DrawLabelSmall(new Vector2(0f, y), Loc.LS("NO MODS INSTALLED OR AVAILABLE"), 500f);
            }
            else
            {
                Row underMouse = null;
                Row selected = null;
                for (int i = firstRow; i < rows.Count && i < firstRow + ROWS_SHOWN; i++)
                {
                    DrawRow(ui, rows[i], i, y);
                    if (MouseOverRow(y))
                        underMouse = rows[i];
                    int switchSelection = SEL_LIST_BASE + i * 2;
                    if (UIManager.m_menu_selection == switchSelection ||
                        UIManager.m_menu_selection == switchSelection + 1)
                        selected = rows[i];
                    y += 62f;
                }
                if (rows.Count > ROWS_SHOWN)
                    ui.DrawLabelSmall(new Vector2(-140f, y),
                        string.Format(Loc.LS("{0} - {1} OF {2}"), firstRow + 1,
                            Math.Min(firstRow + ROWS_SHOWN, rows.Count), rows.Count), 500f);

                // The mouse wins over the keyboard's selection: it can rest on a row
                // without having moved the selection there.
                Row details = underMouse ?? selected;
                if (details != null)
                    DrawDetails(ui, details, top);
            }

            Vector2 position = new Vector2(0f, UIManager.UI_BOTTOM - 150f);
            ui.SelectAndDrawItem(Loc.LS("APPLY AND RESTART"), position, SEL_APPLY, !Mods.SelectionChanged);
            if (UIManager.m_menu_selection == SEL_APPLY && Mods.SelectionChanged)
            {
                UIElement.ToolTipActive = true;
                UIElement.ToolTipTitle = Loc.LS("APPLY AND RESTART");
                UIElement.ToolTipDescription =
                    Loc.LS("CLOSES OVERLOAD AND STARTS IT AGAIN SO THE SELECTED MODS LOAD");
            }

            ui.DrawMenuSeparator(new Vector2(0f, UIManager.UI_BOTTOM - 110f));

            // One line fits between the separator and BACK, and a failure matters more
            // than the hint for a button the player is already pointing at.
            string problem = Library.LastError ?? Relaunch.LastError;
            if (!string.IsNullOrEmpty(problem))
                ui.DrawLabelSmallRed(new Vector2(0f, UIManager.UI_BOTTOM - 80f), problem, 900f);
            else
                ui.DrawMenuToolTip(new Vector2(0f, UIManager.UI_BOTTOM - 110f));

            ui.SelectAndDrawItem(Loc.LS("BACK"), new Vector2(0f, UIManager.UI_BOTTOM - 30f), SEL_BACK, false);
        }

        private static void DrawRow(UIElement ui, Row row, int index, float y)
        {
            int switchSelection = SEL_LIST_BASE + index * 2;
            int installSelection = switchSelection + 1;
            bool downloading = row.File == Library.Downloading;

            bool flagged = row.Check == FileCheck.Unlisted || row.Check == FileCheck.Mismatched;
            ui.DrawStringSmall(row.Name, new Vector2(NAME_LEFT, flagged ? y - 9f : y), 0.5f, StringOffset.LEFT,
                UIManager.m_col_ui1, 1f, NAME_WIDTH);
            if (flagged)
                ui.DrawStringSmall(CheckText(row.Check), new Vector2(NAME_LEFT, y + 15f), 0.3f, StringOffset.LEFT,
                    row.Check == FileCheck.Mismatched ? UIManager.m_col_em5 : UIManager.m_col_ui0, 1f, NAME_WIDTH);

            ui.SelectAndDrawItem(Mods.IsEnabled(row.File) ? Loc.LS("ON") : Loc.LS("OFF"),
                new Vector2(-30f, y), switchSelection, !row.Installed, 0.14f, 0.5f);

            string installLabel = downloading ? Loc.LS("DOWNLOADING")
                : row.Installed ? Loc.LS("DELETE")
                : Loc.LS("INSTALL");
            bool installUsable = !downloading &&
                (row.Installed || (row.Listed != null && row.Listed.Downloadable));
            ui.SelectAndDrawItem(installLabel, new Vector2(120f, y), installSelection, !installUsable, 0.22f, 0.5f);
        }

        // The whole row answers for the pointer, not just its two hit boxes -- they are
        // now too small to be the only way to read a mod's details.
        private static bool MouseOverRow(float y)
        {
            Vector2 mouse = UIManager.m_mouse_pos;
            return UIManager.m_menu_use_mouse &&
                mouse.x > NAME_LEFT - 20f && mouse.x < DETAILS_LEFT - 40f &&
                mouse.y > y - 31f && mouse.y < y + 31f;
        }

        // Everything about a mod the row has no room for. Replaces what the bottom
        // tooltip used to say about the selected row.
        private static void DrawDetails(UIElement ui, Row row, float y)
        {
            ui.DrawStringSmall(row.Name, new Vector2(DETAILS_LEFT, y), 0.5f, StringOffset.LEFT,
                UIManager.m_col_ui1, 1f, DETAILS_WIDTH);
            ui.DrawVariableSeparator(new Vector2(DETAILS_LEFT + DETAILS_WIDTH * 0.5f, y + 20f), DETAILS_WIDTH);
            y += 48f;

            ui.DrawStringSmall(Facts(row), new Vector2(DETAILS_LEFT, y), 0.35f, StringOffset.LEFT,
                UIManager.m_col_ui0, 1f, DETAILS_WIDTH);
            y += 32f;

            string warning = Warning(row);
            if (warning != null)
            {
                ui.DrawStringSmall(warning, new Vector2(DETAILS_LEFT, y), 0.35f, StringOffset.LEFT,
                    row.Check == FileCheck.Mismatched ? UIManager.m_col_em5 : UIManager.m_col_hi1, 1f, DETAILS_WIDTH);
                y += 32f;
            }

            if (row.Listed == null)
                return;
            foreach (string line in WrapToWidth(row.Listed.Description, 0.4f, DETAILS_WIDTH))
            {
                if (y > UIManager.UI_BOTTOM - 200f) // stay clear of APPLY AND RESTART
                    break;
                ui.DrawStringSmall(line, new Vector2(DETAILS_LEFT, y), 0.4f, StringOffset.LEFT,
                    UIManager.m_col_ui0, 1f);
                y += 24f;
            }
        }

        // The engine squishes an over-wide string instead of wrapping it, so the lines are
        // measured here at the size they will be drawn.
        private static IEnumerable<string> WrapToWidth(string text, float scale, float width)
        {
            if (string.IsNullOrEmpty(text))
                yield break;
            string line = "";
            foreach (string word in text.Split(' '))
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

        private static string CheckText(FileCheck check)
        {
            return check == FileCheck.Mismatched
                ? Loc.LS("DOES NOT MATCH THE PUBLISHED FILE")
                : Loc.LS("NOT LISTED ON THE MOD SITE");
        }

        // A mod's identity in one line: the file it installs as, its version, and what a
        // download would cost while it is not installed yet.
        private static string Facts(Row row)
        {
            List<string> parts = new List<string>();
            parts.Add(row.File);
            if (row.Listed != null && !string.IsNullOrEmpty(row.Listed.Version))
                parts.Add(Loc.LS("VERSION") + " " + row.Listed.Version);
            if (!row.Installed && row.Listed != null && row.Listed.Size > 0)
                parts.Add((row.Listed.Size / 1048576f).ToString("0.0") + " MB");
            return string.Join("  --  ", parts.ToArray());
        }

        private static string Warning(Row row)
        {
            if (row.Check == FileCheck.Unlisted || row.Check == FileCheck.Mismatched)
                return CheckText(row.Check);
            if (!row.Installed && (row.Listed == null || !row.Listed.Downloadable))
                return Loc.LS("THE MOD SITE PUBLISHES NO VERIFIABLE DOWNLOAD FOR THIS FILE");
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
        }

        private static Row RowFor(string file, bool installed)
        {
            Catalog.Entry listed = Catalog.Find(file);
            FileCheck check = FileCheck.Unknown;
            if (installed && Catalog.Loaded)
                check = listed == null || string.IsNullOrEmpty(listed.Sha256) ? FileCheck.Unlisted
                    : string.Equals(Library.HashOf(file), listed.Sha256, StringComparison.OrdinalIgnoreCase)
                        ? FileCheck.Published : FileCheck.Mismatched;
            return new Row
            {
                File = file,
                Name = Catalog.DisplayName(file),
                Installed = installed,
                Listed = listed,
                Check = check
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
