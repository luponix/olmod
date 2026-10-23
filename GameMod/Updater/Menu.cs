using System;
using System.Collections.Generic;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod.Updater
{
    public static class Menu
    {
        public static readonly MenuState msMods = (MenuState)82;
        public static readonly UIElementType uiMods = (UIElementType)95;

        internal const int SEL_OPTIONS_MODS = 5;

        private const int SEL_BACK = 100;
        private const int SEL_APPLY = 110;
        private const int SEL_LIST_BASE = 1000;

        private static readonly List<string> rows = new List<string>();

        public static void ModsUpdate(ref float timer)
        {
            UIManager.MouseSelectUpdate();
            switch (MenuManager.m_menu_sub_state)
            {
                case MenuSubState.INIT:
                    if (timer > 0.25f)
                    {
                        RefreshRows();
                        UIManager.CreateUIElement(UIManager.SCREEN_CENTER, 7000, uiMods);
                        MenuManager.m_menu_sub_state = MenuSubState.ACTIVE;
                        timer = 0f;
                        MenuManager.SetDefaultSelection(rows.Count > 0 ? SEL_LIST_BASE : SEL_BACK);
                    }
                    break;

                case MenuSubState.ACTIVE:
                    UIManager.ControllerMenu();
                    if (timer > 0.25f && UIManager.PushedSelect(SEL_BACK))
                    {
                        int selection = UIManager.m_menu_selection;
                        if (selection == SEL_BACK)
                        {
                            MenuManager.PlaySelectSound(1f);
                            timer = 0f;
                            UIManager.DestroyAll(false);
                            MenuManager.ChangeMenuState(MenuState.OPTIONS, true);
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
                            Toggle(rows[selection - SEL_LIST_BASE]);
                        }
                    }
                    break;
            }
        }

        public static void DrawMods(UIElement ui)
        {
            const float rowStride = 48f;

            ui.m_alpha = 1f;
            UIManager.ui_bg_dark = true;
            ui.DrawMenuBG();
            UIElement.ToolTipActive = false;
            ui.DrawHeaderMedium(Vector2.up * (UIManager.UI_TOP + 30f), Loc.LS("MODS"), 265f);
            ui.DrawMenuSeparator(new Vector2(0f, UIManager.UI_TOP + 55f));

            Vector2 position = new Vector2(0f, UIManager.UI_TOP + 130f);
            if (rows.Count == 0)
            {
                ui.DrawLabelSmall(position, Loc.LS("NO MODS INSTALLED OR AVAILABLE"), 500f);
            }
            else
            {
                foreach (string file in rows)
                {
                    bool installed = Mods.IsInstalled(file);
                    bool enabled = Mods.IsEnabled(file);
                    string value = !installed
                        ? Loc.LS("NOT INSTALLED")
                        : enabled ? Loc.LS("ON") : Loc.LS("OFF");
                    ui.SelectAndDrawStringOptionItem(Catalog.DisplayName(file), position,
                        SEL_LIST_BASE + rows.IndexOf(file), value, Tooltip(file), 1.5f, !installed);
                    position.y += rowStride;
                }
            }

            position.y = UIManager.UI_BOTTOM - 150f;
            ui.DrawMenuSeparator(position - Vector2.up * 30f);
            ui.SelectAndDrawItem(Loc.LS("APPLY AND RESTART"), position, SEL_APPLY, !Mods.SelectionChanged);
            if (UIManager.m_menu_selection == SEL_APPLY && Mods.SelectionChanged)
            {
                UIElement.ToolTipActive = true;
                UIElement.ToolTipTitle = Loc.LS("APPLY AND RESTART");
                UIElement.ToolTipDescription =
                    Loc.LS("CLOSES OVERLOAD AND STARTS IT AGAIN SO THE SELECTED MODS LOAD");
            }

            if (!string.IsNullOrEmpty(Relaunch.LastError))
                ui.DrawLabelSmallRed(new Vector2(0f, UIManager.UI_BOTTOM - 76f),
                    Loc.LS("COULD NOT RESTART -- ") + Relaunch.LastError, 700f);

            ui.DrawMenuToolTip(new Vector2(0f, UIManager.UI_BOTTOM - 110f));
            ui.SelectAndDrawItem(Loc.LS("BACK"), new Vector2(0f, UIManager.UI_BOTTOM - 30f), SEL_BACK, false);
        }

        private static string Tooltip(string file)
        {
            Catalog.Entry entry = Catalog.Find(file);
            if (!Mods.IsInstalled(file))
                return Loc.LS("LISTED BUT NOT INSTALLED -- ") + file;
            return entry != null && !string.IsNullOrEmpty(entry.Description) ? entry.Description : file;
        }

        private static void Toggle(string file)
        {
            if (!Mods.IsInstalled(file))
            {
                MenuManager.PlayCycleSound(1f, -1f);
                return;
            }
            Mods.SetEnabled(file, !Mods.IsEnabled(file));
            MenuManager.PlayCycleSound(1f, 1f);
        }

        private static void RefreshRows()
        {
            rows.Clear();
            rows.AddRange(Mods.Installed());
            foreach (string file in Mods.Enabled)
                if (!Mods.IsInstalled(file))
                    rows.Add(file);
            foreach (string file in Catalog.NotInstalled())
                if (!rows.Contains(file))
                    rows.Add(file);
        }
    }

    [HarmonyPatch(typeof(UIElement), "DrawOptionsMenu")]
    class Menu_UIElement_DrawOptionsMenu
    {
        static void Postfix(UIElement __instance)
        {
            __instance.SelectAndDrawItem(Loc.LS("MODS"),
                new Vector2(0f, UIManager.UI_BOTTOM - 92f), Menu.SEL_OPTIONS_MODS, false);
        }
    }

    [HarmonyPatch(typeof(MenuManager), "OptionsUpdate")]
    class Menu_MenuManager_OptionsUpdate
    {
        static void Postfix()
        {
            if (MenuManager.m_menu_sub_state == MenuSubState.ACTIVE &&
                UIManager.PushedSelect() &&
                UIManager.m_menu_selection == Menu.SEL_OPTIONS_MODS)
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
