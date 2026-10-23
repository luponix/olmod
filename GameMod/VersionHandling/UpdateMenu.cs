using System.Collections.Generic;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod.VersionHandling
{
    /// <summary>
    /// The player's half of the self updater: a pulsing UPDATE item in the left main menu
    /// column, and the popup it opens -- what is being installed, what changed since the
    /// running version, and BACK / UPDATE.
    ///
    /// The popup is drawn over the live main menu rather than as a menu state of its own, so
    /// it owns the main menu's input while it is up: the draw leaves only its own two items
    /// in the selection list, and the MainMenuUpdate prefix below replaces the stock handling.
    /// </summary>
    public static class UpdateMenu
    {
        // 12 is the slot the dead GET OLMOD item used, in the left column with the stock items
        internal const int SEL_UPDATE = 12;
        private const int SEL_BACK = 2610;
        private const int SEL_INSTALL = 2611;

        private const int VISIBLE_LINES = 12;
        private const float BOX_HALF_WIDTH = 470f;
        private const float BOX_HALF_HEIGHT = 250f;

        private static List<string> changelog;
        private static int firstLine;
        private static int selection = SEL_BACK;
        private static bool popupDrawn;

        public static bool PopupOpen { get; private set; }

        private static bool CanInstall
        {
            get { return SelfUpdate.State == SelfUpdateState.Available && SelfUpdate.Latest != null; }
        }

        public static void OpenPopup()
        {
            PopupOpen = true;
            popupDrawn = false;
            firstLine = 0;
            selection = SEL_BACK;
            changelog = null;
            MenuManager.PlaySelectSound();
        }

        public static void ClosePopup()
        {
            PopupOpen = false;
            MenuManager.PlaySelectSound();
        }

        // text for the main menu item, or null when there is nothing to offer the player
        private static string ItemLabel
        {
            get
            {
                switch (SelfUpdate.State)
                {
                    case SelfUpdateState.Available: return "UPDATE";
                    case SelfUpdateState.Downloading: return "UPDATING";
                    case SelfUpdateState.Installed: return "RESTART TO FINISH";
                    case SelfUpdateState.Failed: return "UPDATE FAILED";
                    default: return null;
                }
            }
        }

        // ---- the main menu item ----

        // A slow weak pulse behind the stock half item geometry: an update is worth noticing
        // without competing with the item the player is actually on.
        public static void DrawUpdateItem(UIElement ui)
        {
            string label = ItemLabel;
            if (label == null)
                return;

            // two rows above the discord half item, which the demos row keeps at y 279
            Vector2 pos = new Vector2(-500f, 171f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f);

            Color glow = UIManager.m_col_hi5;
            glow.a = (0.1f + 0.18f * pulse) * ui.m_alpha;
            UIManager.DrawQuadBarHorizontal(pos, 26f + 5f * pulse, 26f + 5f * pulse, 200f, glow, 7);

            ui.TestMouseInRect(pos, 134.5f, 24f, SEL_UPDATE);
            ui.DrawSmallItemOrange(label, pos, UIManager.m_menu_selection == SEL_UPDATE, false, 0.4f, 0.5f);
        }

        // ---- the popup ----

        public static void DrawPopup(UIElement ui)
        {
            // the stock items drew first and registered themselves; nothing behind a modal
            // may be navigated to
            UIManager.ResetSelectionList();

            // The menu behind is dimmed rather than hidden, so the popup reads as sitting on
            // top of it. A black fill never reaches opaque over this material, which is why
            // the border is four bars rather than the engine's recipe of a smaller fill on
            // top of a coloured one (DrawBackdrop).
            UIManager.DrawQuadUI(Vector2.zero, UIManager.UI_RIGHT, UIManager.UI_BOTTOM, Color.black, 0.8f * ui.m_alpha, 12);
            UIManager.DrawQuadUI(Vector2.zero, BOX_HALF_WIDTH, BOX_HALF_HEIGHT, Color.black, ui.m_alpha, 12);
            DrawFrame(ui);

            SelfUpdate.Release latest = SelfUpdate.Latest;
            string title = latest == null
                ? "PYRO UPDATE"
                : "DO YOU WANT TO UPDATE TO PYRO " + latest.name + "?";
            ui.DrawStringSmall(title, new Vector2(0f, -205f), 0.6f, StringOffset.CENTER, UIManager.m_col_hi5, 1f);

            DrawChangelog(ui);

            if (!string.IsNullOrEmpty(SelfUpdate.StatusDetail))
                ui.DrawStringSmall(SelfUpdate.StatusDetail, new Vector2(0f, 110f), 0.45f, StringOffset.CENTER,
                    SelfUpdate.State == SelfUpdateState.Failed ? UIManager.m_col_em4 : UIManager.m_col_ui4, 1f);

            DrawPopupButtons(ui);
            popupDrawn = true;
        }

        private static void DrawFrame(UIElement ui)
        {
            Color edge = UIManager.m_col_ui3;
            float a = 0.9f * ui.m_alpha;
            UIManager.DrawQuadUI(new Vector2(0f, -BOX_HALF_HEIGHT), BOX_HALF_WIDTH, 1.5f, edge, a, 12);
            UIManager.DrawQuadUI(new Vector2(0f, BOX_HALF_HEIGHT), BOX_HALF_WIDTH, 1.5f, edge, a, 12);
            UIManager.DrawQuadUI(new Vector2(-BOX_HALF_WIDTH, 0f), 1.5f, BOX_HALF_HEIGHT, edge, a, 12);
            UIManager.DrawQuadUI(new Vector2(BOX_HALF_WIDTH, 0f), 1.5f, BOX_HALF_HEIGHT, edge, a, 12);
        }

        private static void DrawChangelog(UIElement ui)
        {
            if (changelog == null)
                changelog = WrapChangelog(BOX_HALF_WIDTH * 2f - 60f);

            int last = Mathf.Min(firstLine + VISIBLE_LINES, changelog.Count);
            Vector2 pos = new Vector2(-BOX_HALF_WIDTH + 30f, -155f);
            for (int i = firstLine; i < last; i++)
            {
                ui.DrawStringSmall(changelog[i], pos, 0.4f, StringOffset.LEFT, UIManager.m_col_ui2, 1f);
                pos.y += 20f;
            }

            if (changelog.Count > VISIBLE_LINES)
                ui.DrawStringSmall(last + "/" + changelog.Count, new Vector2(BOX_HALF_WIDTH - 30f, 110f),
                    0.35f, StringOffset.RIGHT, UIManager.m_col_ui0, 1f);
        }

        private static void DrawPopupButtons(UIElement ui)
        {
            // there is no way back out of a running download, so it offers no buttons at all
            if (SelfUpdate.State == SelfUpdateState.Downloading)
                return;

            Vector2 back = new Vector2(CanInstall ? -150f : 0f, 180f);
            ui.TestMouseInRect(back, 134.5f, 24f, SEL_BACK);
            ui.DrawSmallItem("BACK", back, selection == SEL_BACK, false, 0.4f, 0.5f);

            if (!CanInstall)
                return;
            Vector2 install = new Vector2(150f, 180f);
            ui.TestMouseInRect(install, 134.5f, 24f, SEL_INSTALL);
            ui.DrawSmallItemOrange("UPDATE", install, selection == SEL_INSTALL, false, 0.4f, 0.5f);
        }

        // ---- popup input, in place of the stock main menu handling ----

        public static void UpdatePopup()
        {
            // the selection list still holds the stock items until the popup has drawn once
            if (!popupDrawn)
                return;

            if (!CanInstall)
                selection = SEL_BACK;

            UIManager.MouseSelectUpdate();
            UIManager.ControllerMenu();
            if (UIManager.m_menu_selection == SEL_BACK || UIManager.m_menu_selection == SEL_INSTALL)
                selection = UIManager.m_menu_selection;
            else
                UIManager.m_menu_selection = selection; // a stock item under the popup cannot be picked

            ScrollChangelog();

            if (!UIManager.PushedSelect(SEL_BACK))
                return;
            if (UIManager.m_menu_selection == SEL_INSTALL && CanInstall)
            {
                SelfUpdate.StartInstall();
                MenuManager.PlaySelectSound();
                selection = SEL_BACK;
            }
            else if (SelfUpdate.State != SelfUpdateState.Downloading)
            {
                ClosePopup();
            }
        }

        // The two buttons sit side by side, so up/down move no selection and are free to
        // scroll; the wheel and the paging keys do the same.
        private static void ScrollChangelog()
        {
            if (changelog == null || changelog.Count <= VISIBLE_LINES)
                return;

            int step = 0;
            if (Controls.JustPressed(CCInput.MENU_UP)) step -= 1;
            if (Controls.JustPressed(CCInput.MENU_DOWN)) step += 1;
            if (Controls.JustPressed(CCInput.MENU_PGUP)) step -= VISIBLE_LINES;
            if (Controls.JustPressed(CCInput.MENU_PGDN)) step += VISIBLE_LINES;
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (wheel != 0f) step += wheel < 0f ? 3 : -3;
            if (step == 0)
                return;

            firstLine = Mathf.Clamp(firstLine + step, 0, changelog.Count - VISIBLE_LINES);
        }

        // ---- changelog text ----

        // Every release between the running version and the newest one, newest first, as lines
        // that fit the popup. The published text is markdown for the website, so the markers
        // that carry no meaning here are dropped rather than shown.
        private static List<string> WrapChangelog(float width)
        {
            var lines = new List<string>();
            if (SelfUpdate.NewReleases == null)
                return lines;

            foreach (SelfUpdate.Release release in SelfUpdate.NewReleases)
            {
                if (lines.Count > 0)
                    lines.Add("");
                lines.Add("PYRO " + release.name);
                foreach (string raw in release.changelog.Split('\n'))
                {
                    string line = Clean(raw);
                    if (line.Length == 0 && (lines.Count == 0 || lines[lines.Count - 1].Length == 0))
                        continue;
                    AddWrapped(lines, line, width);
                }
            }
            return lines;
        }

        private static string Clean(string line)
        {
            string text = line.Replace("**", "").Replace("`", "").TrimEnd();
            string bare = text.Trim();
            if (bare.Length > 0 && bare.Replace("-", "").Length == 0)
                return ""; // a rule under a heading, which has no line to sit under here
            return text.ToUpperInvariant();
        }

        private static void AddWrapped(List<string> lines, string line, float width)
        {
            if (UIManager.GetStringWidth(line, 20f * 0.4f) <= width)
            {
                lines.Add(line);
                return;
            }

            // keep the indent the published text uses: it is what marks a feature's body
            string indent = line.Substring(0, line.Length - line.TrimStart().Length);
            string current = "";
            foreach (string word in line.Trim().Split(' '))
            {
                string candidate = current.Length == 0 ? indent + word : current + " " + word;
                if (current.Length > 0 && UIManager.GetStringWidth(candidate, 20f * 0.4f) > width)
                {
                    lines.Add(current);
                    current = indent + "  " + word;
                }
                else
                {
                    current = candidate;
                }
            }
            if (current.Length > 0)
                lines.Add(current);
        }
    }

    // Draw the update item, and the popup over everything the main menu drew before it.
    [HarmonyPatch(typeof(UIElement), "DrawMainMenu")]
    class UpdateMenu_UIElement_DrawMainMenu
    {
        static void Postfix(UIElement __instance)
        {
            UpdateMenu.DrawUpdateItem(__instance);
            if (UpdateMenu.PopupOpen)
                UpdateMenu.DrawPopup(__instance);
        }
    }

    // The popup is modal: while it is up it owns the main menu's input.
    [HarmonyPatch(typeof(MenuManager), "MainMenuUpdate")]
    class UpdateMenu_MenuManager_MainMenuUpdate
    {
        static bool Prefix()
        {
            if (!UpdateMenu.PopupOpen || MenuManager.m_menu_sub_state != MenuSubState.ACTIVE)
                return true;
            UpdateMenu.UpdatePopup();
            return false;
        }

        static void Postfix()
        {
            if (UpdateMenu.PopupOpen || MenuManager.m_menu_sub_state != MenuSubState.ACTIVE ||
                NetworkManager.IsHeadless())
                return;
            if (UIManager.PushedSelect(-1) && UIManager.m_menu_selection == UpdateMenu.SEL_UPDATE)
                UpdateMenu.OpenPopup();
        }
    }
}
