using System.Collections.Generic;
using Overload;
using UnityEngine;

namespace GameMod.Bots
{
    static class BotSettingsPage
    {
        public const int MICRO_STATE = 16;
        public const int SELECTION_NAME = 20;
        const int SELECTION_ADD = 8;
        const int SELECTION_REPLACE = 9;
        const int SELECTION_TIER = 21;
        const int SELECTION_TEAM = 22;
        const int SELECTION_PRIMARY1 = 23;
        const int SELECTION_PRIMARY2 = 24;
        const int SELECTION_SECONDARY1 = 25;
        const int SELECTION_SECONDARY2 = 26;
        const int SELECTION_REMOVE = 27;

        public static BotSettings Settings = new BotSettings();
        static int editedIndex;

        static List<BotSlot> Slots
        {
            get { return Settings.Slots; }
        }

        public static BotSlot EditedSlot
        {
            get { return editedIndex < Slots.Count ? Slots[editedIndex] : null; }
        }

        public static void Draw(UIElement uie, Vector2 position)
        {
            uie.DrawLabelSmall(Vector2.up * (UIManager.UI_TOP + 70f), Loc.LS("BOT SETTINGS"), 250f);
            UIElement.ToolTipActive = false;

            Vector2 left = position + new Vector2(-300f, -250f);
            uie.DrawMenuSeparator(left - Vector2.up * 40f);
            for (int i = 0; i < Slots.Count; i++)
            {
                string label = (i == editedIndex ? "> " : string.Empty) + Slots[i].PilotName;
                uie.SelectAndDrawStringOptionItem(label, left, i, BotSettings.TIER_NAMES[Slots[i].Tier], string.Empty, 1f);
                left.y += 55f;
            }
            if (Slots.Count < BotSettings.MAX_BOTS)
            {
                uie.SelectAndDrawItem(Loc.LS("ADD BOT"), left, SELECTION_ADD, false, 1f, 0.75f);
                left.y += 55f;
            }
            uie.SelectAndDrawStringOptionItem(Loc.LS("HUMANS REPLACE BOTS"), left, SELECTION_REPLACE, MenuManager.GetToggleSetting(Settings.ReplaceWithHumans ? 1 : 0),
                Loc.LS("A JOINING PLAYER TAKES THE PLACE OF A BOT, A LEAVING PLAYER BRINGS IT BACK"), 1f);

            Vector2 right = position + new Vector2(300f, -250f);
            BotSlot slot = EditedSlot;
            if (slot == null)
            {
                uie.DrawStringSmall(Loc.LS("ADD A BOT TO SET IT UP"), right, 0.5f, StringOffset.CENTER, UIManager.m_col_ui1, 1f);
            }
            else
            {
                uie.DrawTextEntryLabeled(right, SELECTION_NAME, Loc.LS("BOT NAME"), slot.Name, false);
                right.y += 62f;
                uie.SelectAndDrawStringOptionItem(Loc.LS("DIFFICULTY"), right, SELECTION_TIER, BotSettings.TIER_NAMES[slot.Tier], string.Empty, 1f);
                right.y += 62f;
                uie.SelectAndDrawStringOptionItem(Loc.LS("TEAM"), right, SELECTION_TEAM, TeamName(slot.Team), string.Empty, 1f, MenuManager.mms_mode == MatchMode.ANARCHY);
                right.y += 62f;
                uie.SelectAndDrawStringOptionItem(Loc.LS("PRIMARY 1"), right, SELECTION_PRIMARY1, Player.GetWeaponNameNoDefault(slot.Primary1), string.Empty, 1f);
                right.y += 62f;
                uie.SelectAndDrawStringOptionItem(Loc.LS("PRIMARY 2"), right, SELECTION_PRIMARY2, WeaponName(slot.Primary2), string.Empty, 1f);
                right.y += 62f;
                uie.SelectAndDrawStringOptionItem(Loc.LS("SECONDARY 1"), right, SELECTION_SECONDARY1, Player.GetMissileNameNoDefault(slot.Secondary1), string.Empty, 1f);
                right.y += 62f;
                uie.SelectAndDrawStringOptionItem(Loc.LS("SECONDARY 2"), right, SELECTION_SECONDARY2, MissileName(slot.Secondary2), string.Empty, 1f);
                right.y += 62f;
                uie.SelectAndDrawItem(Loc.LS("REMOVE BOT"), right, SELECTION_REMOVE, false, 1f, 0.75f);
            }

            Vector2 bottom = new Vector2(position.x, Mathf.Max(left.y, right.y));
            uie.DrawMenuSeparator(bottom + Vector2.up * 40f);
            uie.DrawMenuToolTip(bottom + Vector2.up * 40f);
        }

        public static void TypeName(string typed, bool entered)
        {
            string name = BotSettings.CleanName(typed);
            EditedSlot.Name = name;
            if (!entered)
                return;
            if (name.Length == 0)
                EditedSlot.Name = FreeName();
            MenuManager.SetDefaultSelection(SELECTION_TIER);
        }

        public static void Select(int selection)
        {
            int direction = UIManager.m_select_dir;
            BotSlot slot = EditedSlot;
            switch (selection)
            {
                case SELECTION_ADD:
                    BotSlot added = slot == null ? new BotSlot() : slot.Clone();
                    added.Name = FreeName();
                    Slots.Add(added);
                    editedIndex = Slots.Count - 1;
                    MenuManager.PlaySelectSound();
                    return;
                case SELECTION_REMOVE:
                    Slots.RemoveAt(editedIndex);
                    editedIndex = Mathf.Max(0, editedIndex - 1);
                    MenuManager.SetDefaultSelection(Slots.Count == 0 ? SELECTION_ADD : editedIndex);
                    MenuManager.PlaySelectSound();
                    return;
                case SELECTION_REPLACE:
                    Settings.ReplaceWithHumans = !Settings.ReplaceWithHumans;
                    break;
                case SELECTION_TIER:
                    slot.Tier = Cycle(slot.Tier, direction, BotSettings.TIER_FILES.Length, -1);
                    break;
                case SELECTION_TEAM:
                    slot.Team = Cycle(slot.Team + 1, direction, MPTeams.MenuManagerTeamCount + 1, -1) - 1;
                    break;
                case SELECTION_PRIMARY1:
                    slot.Primary1 = (WeaponType)Cycle((int)slot.Primary1, direction, (int)WeaponType.NUM, (int)slot.Primary2);
                    break;
                case SELECTION_PRIMARY2:
                    slot.Primary2 = (WeaponType)Cycle((int)slot.Primary2, direction, (int)WeaponType.NUM + 1, (int)slot.Primary1);
                    break;
                case SELECTION_SECONDARY1:
                    slot.Secondary1 = (MissileType)Cycle((int)slot.Secondary1, direction, (int)MissileType.NUM, (int)slot.Secondary2);
                    break;
                case SELECTION_SECONDARY2:
                    slot.Secondary2 = (MissileType)Cycle((int)slot.Secondary2, direction, (int)MissileType.NUM + 1, (int)slot.Secondary1);
                    break;
                default:
                    if (selection < Slots.Count)
                    {
                        editedIndex = selection;
                        MenuManager.PlaySelectSound();
                    }
                    return;
            }
            MenuManager.PlayCycleSound(1f, direction);
        }

        static int Cycle(int value, int direction, int count, int taken)
        {
            value = (value + count + direction) % count;
            return value == taken ? (value + count + direction) % count : value;
        }

        static string FreeName()
        {
            for (int number = 1; ; number++)
                if (!Slots.Exists(s => s.Name == number.ToString()))
                    return number.ToString();
        }

        static string TeamName(int team)
        {
            return team == BotSettings.AUTO_TEAM ? Loc.LS("AUTO") : MPTeams.TeamName(MPTeams.AllTeams[team]);
        }

        static string WeaponName(WeaponType weapon)
        {
            return weapon == WeaponType.NUM ? Loc.LS("NONE") : Player.GetWeaponNameNoDefault(weapon);
        }

        static string MissileName(MissileType missile)
        {
            return missile == MissileType.NUM ? Loc.LS("NONE") : Player.GetMissileNameNoDefault(missile);
        }
    }
}
