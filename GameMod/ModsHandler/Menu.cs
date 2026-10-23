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

        public static void ModsUpdate(ref float timer)
        {
            UIManager.MouseSelectUpdate();
            switch (MenuManager.m_menu_sub_state)
            {
                case MenuSubState.INIT:
                    if (timer > 0.25f)
                    {
                        seenChanges = Library.Changes;
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

            float y = UIManager.UI_TOP + 150f;
            if (rows.Count == 0)
            {
                ui.DrawLabelSmall(new Vector2(0f, y), Loc.LS("NO MODS INSTALLED OR AVAILABLE"), 500f);
            }
            else
            {
                for (int i = 0; i < rows.Count && i < ROWS_SHOWN; i++)
                {
                    DrawRow(ui, rows[i], i, y);
                    y += 62f;
                }
                if (rows.Count > ROWS_SHOWN)
                    ui.DrawLabelSmall(new Vector2(0f, y),
                        string.Format(Loc.LS("{0} MORE NOT SHOWN"), rows.Count - ROWS_SHOWN), 500f);
            }

            Vector2 position = new Vector2(0f, UIManager.UI_BOTTOM - 150f);
            ui.DrawMenuSeparator(position - Vector2.up * 30f);
            ui.SelectAndDrawItem(Loc.LS("APPLY AND RESTART"), position, SEL_APPLY, !Mods.SelectionChanged);
            if (UIManager.m_menu_selection == SEL_APPLY && Mods.SelectionChanged)
            {
                UIElement.ToolTipActive = true;
                UIElement.ToolTipTitle = Loc.LS("APPLY AND RESTART");
                UIElement.ToolTipDescription =
                    Loc.LS("CLOSES OVERLOAD AND STARTS IT AGAIN SO THE SELECTED MODS LOAD");
            }

            string problem = Library.LastError ?? Relaunch.LastError;
            if (!string.IsNullOrEmpty(problem))
                ui.DrawLabelSmallRed(new Vector2(0f, UIManager.UI_BOTTOM - 76f), problem, 900f);

            ui.DrawMenuToolTip(new Vector2(0f, UIManager.UI_BOTTOM - 110f));
            ui.SelectAndDrawItem(Loc.LS("BACK"), new Vector2(0f, UIManager.UI_BOTTOM - 30f), SEL_BACK, false);
        }

        private static void DrawRow(UIElement ui, Row row, int index, float y)
        {
            int switchSelection = SEL_LIST_BASE + index * 2;
            int installSelection = switchSelection + 1;
            bool downloading = row.File == Library.Downloading;

            bool flagged = row.Check == FileCheck.Unlisted || row.Check == FileCheck.Mismatched;
            ui.DrawStringSmall(row.Name, new Vector2(-520f, flagged ? y - 9f : y), 0.5f, StringOffset.LEFT,
                UIManager.m_col_ui1, 1f, 480f);
            if (flagged)
                ui.DrawStringSmall(CheckText(row.Check), new Vector2(-520f, y + 15f), 0.3f, StringOffset.LEFT,
                    row.Check == FileCheck.Mismatched ? UIManager.m_col_em5 : UIManager.m_col_ui0, 1f, 480f);

            ui.SelectAndDrawHalfItem(Mods.IsEnabled(row.File) ? Loc.LS("ON") : Loc.LS("OFF"),
                new Vector2(130f, y), switchSelection, !row.Installed);

            string installLabel = downloading ? Loc.LS("DOWNLOADING")
                : row.Installed ? Loc.LS("DELETE")
                : Loc.LS("DOWNLOAD");
            bool installUsable = !downloading &&
                (row.Installed || (row.Listed != null && row.Listed.Downloadable));
            ui.SelectAndDrawHalfItem(installLabel, new Vector2(400f, y), installSelection, !installUsable);

            if (UIManager.m_menu_selection == switchSelection || UIManager.m_menu_selection == installSelection)
            {
                UIElement.ToolTipActive = true;
                UIElement.ToolTipTitle = row.Name;
                UIElement.ToolTipDescription = Tooltip(row);
            }
        }

        private static string CheckText(FileCheck check)
        {
            return check == FileCheck.Mismatched
                ? Loc.LS("DOES NOT MATCH THE PUBLISHED FILE")
                : Loc.LS("NOT LISTED ON THE MOD SITE");
        }

        private static string Tooltip(Row row)
        {
            List<string> parts = new List<string>();
            if (row.Listed != null && !string.IsNullOrEmpty(row.Listed.Description))
                parts.Add(row.Listed.Description);
            if (row.Listed != null && !string.IsNullOrEmpty(row.Listed.Version))
                parts.Add(Loc.LS("VERSION") + " " + row.Listed.Version);
            if (!row.Installed && row.Listed != null && row.Listed.Size > 0)
                parts.Add((row.Listed.Size / 1048576f).ToString("0.0") + " MB");
            if (!row.Installed && (row.Listed == null || !row.Listed.Downloadable))
                parts.Add(Loc.LS("THE MOD SITE PUBLISHES NO VERIFIABLE DOWNLOAD FOR THIS FILE"));
            parts.Add(row.File);
            return string.Join(" -- ", parts.ToArray());
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
