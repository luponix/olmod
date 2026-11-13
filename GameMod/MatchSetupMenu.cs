using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod
{
    static class MatchSetupMenu
    {
        private static readonly MethodInfo _UIElement_DrawMiniLevelImage_Method = AccessTools.Method(typeof(UIElement), "DrawMiniLevelImage");
        private static readonly MethodInfo _MenuManager_ProcessInputField_Method = AccessTools.Method(typeof(MenuManager), "ProcessInputField");
        private static readonly MethodInfo _MenuManager_BuildPrivateMatchData_Method = AccessTools.Method(typeof(MenuManager), "BuildPrivateMatchData");
        private static readonly Type _InternetMatch_Type = AccessTools.TypeByName("InternetMatch");
        private static readonly MethodInfo _InternetMatch_ClientModeName_Method = AccessTools.Method(_InternetMatch_Type, "ClientModeName");
        private static readonly MethodInfo _InternetMatch_PasswordFieldName_Method = AccessTools.Method(_InternetMatch_Type, "PasswordFieldName");
        private static readonly MethodInfo _InternetMatch_ParsePassword_Method = AccessTools.Method(_InternetMatch_Type, "ParsePassword");

        public static void Draw(UIElement uie)
        {
            if (MenuManager.m_menu_micro_state == 8)
            {
                MPLevelSelect.DrawPage(uie);
                return;
            }

            uie.DrawMenuBG();
            Vector2 position = uie.m_position;
            uie.DrawHeaderMedium(Vector2.up * (UIManager.UI_TOP + 30f), uie.m_display_string1);
            if (IsTab(MenuManager.m_menu_micro_state))
                DrawTabBar(uie);
            switch (MenuManager.m_menu_micro_state)
            {
                case 0:
                    DrawJoinOrCreate(uie, position);
                    break;
                case 1:
                case 4:
                    DrawPassword(uie, position);
                    break;
                case 2:
                    DrawMatchSettings(uie, position);
                    break;
                case TAB_RULES:
                    DrawRules(uie);
                    break;
                case TAB_POWERUPS:
                    DrawPowerupSettings(uie);
                    break;
                case TAB_LOADOUTS:
                    DrawLoadoutSettings(uie);
                    break;
                case 9:
                    MPModifiers.DrawAllowedModifiersPage(uie);
                    break;
                case TAB_TEAMS:
                    DrawSettingsColumn(uie, new Vector2(0f, TAB_CONTENT_TOP), teamRows, 1.5f);
                    break;
                case TAB_SERVER:
                    DrawSettingsColumn(uie, new Vector2(0f, TAB_CONTENT_TOP), serverRows, 1.5f);
                    break;
                case 15:
                    MPLoadouts.DrawAllowedLoadoutsPage(uie);
                    break;
                case Bots.BotSettingsPage.MICRO_STATE:
                    Bots.BotSettingsPage.Draw(uie);
                    break;
            }

            position.x = 0f;
            position.y = UIManager.UI_BOTTOM - 30f;
            if (MenuManager.m_menu_micro_state != 2 || !MenuManager.m_updating_pm_settings)
            {
                uie.SelectAndDrawItem(Loc.LS("BACK"), position, 100, false);
            }
            else
            {
                uie.SelectAndDrawItem(Loc.LS("CANCEL"), position, 100, false);
                uie.MaybeShowMpStatus();
            }
        }

        private static void DrawJoinOrCreate(UIElement uie, Vector2 position)
        {
            position.y -= 31f;
            uie.DrawMenuSeparator(position - Vector2.up * 40f);
            uie.SelectAndDrawItem(Loc.LS("JOIN MATCH"), position, 0, false);
            position.y += 62f;
            MPJoinInProgress.DrawCreateOpenMatch(uie, ref position);
            uie.SelectAndDrawItem(Loc.LS("CREATE MATCH"), position, 1, false);
            uie.DrawMenuSeparator(position + Vector2.up * 40f);
        }

        private static void DrawPassword(UIElement uie, Vector2 position)
        {
            if (MPInternet.Enabled)
            {
                position.y = -270f;
                uie.DrawStringSmall(Loc.LS("TO FIND A GAME SERVER, VISIT THE OVERLOAD SERVER BROWSER OR THE OVERLOAD DISCORD SERVER"), position, 0.6f, StringOffset.CENTER, UIManager.m_col_ui1, 1f);
                position.y += 46.5f;
                uie.SelectAndDrawTextOnlyItem(Loc.LS("OPEN THE OVERLOAD SERVER BROWSER"), position, 21, 0.4f, StringOffset.CENTER, false);
                position.y += 31f;
                uie.SelectAndDrawTextOnlyItem(Loc.LS("OPEN THE OVERLOAD DISCORD SERVER"), position, 22, 0.4f, StringOffset.CENTER, false);
                position.y = 0f;
            }
            position.y -= 62f;
            uie.DrawMenuSeparator(position - Vector2.up * 40f);
            uie.DrawTextEntryLabeled(position, 11, PasswordFieldName(), MenuManager.mms_match_password, MenuManager.mms_hide_password);
            position.y += 62f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("HIDE"), position, 10, MenuManager.GetToggleSetting(MenuManager.mms_hide_password ? 1 : 0), string.Empty, 1f);
            position.y += 62f;
            bool passwordTooShort = MenuManager.mms_match_password.Length <= 2;
            if (MenuManager.m_menu_micro_state == 1)
                uie.SelectAndDrawItem(Loc.LS("JOIN MATCH"), position, 0, passwordTooShort);
            else
                uie.SelectAndDrawItem(Loc.LS("CREATE MATCH"), position, 1, passwordTooShort);
            uie.DrawMenuSeparator(position + Vector2.up * 40f);
        }

        private static void DrawMatchSettings(UIElement uie, Vector2 position)
        {
            position.y -= 277f;
            uie.DrawMenuSeparator(position - Vector2.up * 40f);
            uie.SelectAndDrawStringOptionItem("MATCH PRESET", position, 9, MPMatchPresets.GetMatchPreset(), "PRECONFIGURED SETTINGS FOR MATCHES");
            position.y += 55f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("GAME MODE"), position, 0, MenuManager.GetMMSGameMode());
            position.y += 55f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("MAX PLAYERS"), position, 1, MenuManager.GetMMSNumPlayers());
            position.y += 55f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("BOTS"), position, 31, MenuManager.GetToggleSetting(Bots.BotSettingsPage.Settings.Enabled ? 1 : 0), Loc.LS("THE SERVER ADDS THE BOTS FROM BOT SETTINGS TO THE MATCH"));
            position.y += 55f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("LEVEL"), position, 2, MenuManager.GetMMSLevel());
            position.y += 55f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("TIME LIMIT"), position, 4, MenuManager.GetMMSTimeLimit());
            position.y += 55f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("SCORE LIMIT"), position, 5, MenuManager.GetMMSScoreLimit());
            position.y += 55f;
            if (MenuManager.mms_mode == ExtMatchMode.RACE)
            {
                string lapLimit = ExtMenuManager.mms_ext_lap_limit == 0 ? "NONE" : ExtMenuManager.mms_ext_lap_limit.ToString();
                uie.SelectAndDrawStringOptionItem("LAP LIMIT", position, 10, lapLimit);
            }
            position.y += 55f;
            MPServerBrowser.DrawMatchNotes(uie, position);
            position.y += 55f;
            uie.SelectAndDrawItem(Loc.LS("ADVANCED SETTINGS"), position, 6, false);
            position.y += 55f;
            if (MenuManager.m_updating_pm_settings)
                uie.SelectAndDrawItemOutline(Loc.LS("APPLY SETTINGS"), position, 7, false);
            else
                uie.SelectAndDrawItemOutline(Loc.LS("CREATE MATCH"), position, 7, false);

            position.y = -31f;
            position.x = -535f;
            _UIElement_DrawMiniLevelImage_Method.Invoke(uie, new object[] { position, 380f, true });
            position.y -= 70f;
            uie.DrawMiniHeaderBright(position, MenuManager.GetMMSLevel(), 75f);
            position.y += 140f;
            uie.DrawMiniHeader(position, Loc.LS("PLAYERS"), 75f);
            position.y += 22f;
            uie.DrawStringSmall(MenuManager.GetMMSLevelPlayers(), position, 0.4f, StringOffset.CENTER, UIManager.m_col_ui2, 1f);
            position.y += 30f;
            uie.DrawMiniHeader(position, Loc.LS("TEAM"), 75f);
            position.y += 22f;
            uie.DrawStringSmall(MenuManager.GetMMSLevelPlayersTeam(), position, 0.4f, StringOffset.CENTER, UIManager.m_col_ui2, 1f);
        }

        private const int TAB_RULES = 3;
        private const int TAB_POWERUPS = 5;
        private const int TAB_LOADOUTS = 6;
        private const int TAB_TEAMS = 10;
        private const int TAB_SERVER = 17;
        private static readonly int[] tabs = { TAB_RULES, TAB_TEAMS, TAB_POWERUPS, TAB_LOADOUTS, Bots.BotSettingsPage.MICRO_STATE, TAB_SERVER };
        private static readonly string[] tabNames = { "RULES", "TEAMS", "POWERUPS", "LOADOUTS", "BOTS", "SERVER" };
        private const int TAB_SELECTION = 400;
        private const float TAB_CONTENT_TOP = -220f;
        private const float ROW_PITCH = 55f;

        private static readonly int[] rulesLeftRows = { 0, 1, 2, 10, 7, 18, 21, 11, 13 };
        private static readonly int[] rulesRightRows = { 6, 24, 15, 17, 26, 25, 20, 27, 28 };
        private static readonly int[] teamRows = { 3, 8, 12, 14 };
        private static readonly int[] serverRows = { 23, 22, 16, 32 };

        private static readonly MethodInfo _MenuManager_PlayHighlightSound_Method = AccessTools.Method(typeof(MenuManager), "PlayHighlightSound");
        private static int previousSelection = -1;

        private static bool IsTab(int microState)
        {
            return Array.IndexOf(tabs, microState) >= 0;
        }

        private static void DrawTabBar(UIElement uie)
        {
            UIElement.ToolTipActive = false;
            Vector2 bar = new Vector2(0f, UIManager.UI_TOP + 85f);
            float tabHalfWidth = tabNames.Max(name => UIManager.GetStringWidth(name, 12f)) * 0.5f + 4f;
            float spacing = 2f * (tabHalfWidth + 16f) + 12f;
            float barHalfWidth = (tabNames.Length - 1) * 0.5f * spacing + tabHalfWidth + 16f + 8f - 22f;
            Menus_UIElement_DrawMpOptions.DrawTabs(uie, bar, Array.IndexOf(tabs, MenuManager.m_menu_micro_state), tabNames, TAB_SELECTION, spacing, tabHalfWidth, barHalfWidth);
            uie.DrawMenuSeparator(bar + Vector2.up * 40f);
        }

        private static void DrawTabFooter(UIElement uie, float lastRowY)
        {
            Vector2 footer = new Vector2(0f, lastRowY + 40f);
            uie.DrawMenuSeparator(footer);
            uie.DrawMenuToolTip(footer);
        }

        private static void DrawRules(UIElement uie)
        {
            float leftBottom = DrawSettingsColumn(uie, new Vector2(-300f, TAB_CONTENT_TOP), rulesLeftRows, 1f, false);
            float rightBottom = DrawSettingsColumn(uie, new Vector2(300f, TAB_CONTENT_TOP), rulesRightRows, 1f, false);
            DrawTabFooter(uie, Math.Max(leftBottom, rightBottom));
        }

        private static float DrawSettingsColumn(UIElement uie, Vector2 position, int[] rows, float widthScale, bool withFooter = true)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0)
                    position.y += ROW_PITCH;
                DrawSettingRow(uie, position, rows[i], widthScale);
            }
            if (withFooter)
                DrawTabFooter(uie, position.y);
            return position.y;
        }

        private static void DrawSettingRow(UIElement uie, Vector2 position, int selection, float widthScale)
        {
            switch (selection)
            {
                case 0:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("SHOW ENEMY NAMES"), position, 0, MenuManager.GetMMSEnemyNames(), string.Empty, widthScale);
                    break;
                case 1:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("RESPAWN TIME"), position, 1, MenuManager.GetMMSRespawnTime(), Loc.LS("SECONDS AFTER EXPLODING BEFORE A PLAYER RESPAWNS"), widthScale, Menus.mms_scale_respawn_time);
                    break;
                case 2:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("RESPAWN SHIELD"), position, 2, MenuManager.GetMMSRespawnShield(), Loc.LS("SECONDS A PLAYER IS INVULNERABLE WHEN RESPAWNING"), widthScale, MenuManager.mms_mode == MatchMode.MONSTERBALL);
                    break;
                case 3:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("FRIENDLY FIRE"), position, 3, MenuManager.GetMMSFriendlyFire(), string.Empty, widthScale, MenuManager.mms_mode == MatchMode.ANARCHY);
                    break;
                case 6:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("TURN SPEED LIMITING"), position, 6, MenuManager.GetMMSTurnSpeedLimit(), Loc.LS("ABSOLUTE LIMIT OF PITCH AND YAW ROTATION SPEED (ALL CONTROL TYPES)"), widthScale);
                    break;
                case 7:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("ALLOW JOIN IN PROGRESS"), position, 7,
                        MenuManager.GetToggleSetting(MPJoinInProgress.MenuManagerEnabled || MPJoinInProgress.SingleMatchEnable ? 1 : 0),
                        Loc.LS("ALLOW PLAYERS TO JOIN MATCH AFTER IT HAS STARTED"), widthScale, !MenuManager.m_mp_lan_match || MPJoinInProgress.SingleMatchEnable);
                    break;
                case 8:
                    uie.SelectAndDrawStringOptionItem("TEAM COUNT", position, 8, MPTeams.MenuManagerTeamCount.ToString(), string.Empty, widthScale,
                        MenuManager.mms_mode == MatchMode.ANARCHY || !MenuManager.m_mp_lan_match);
                    break;
                case 10:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("SUDDEN DEATH OVERTIME"), position, 10, MenuManager.GetToggleSetting(Convert.ToInt32(MPSuddenDeath.SuddenDeathMenuEnabled)), string.Empty, widthScale, MenuManager.mms_mode != MatchMode.NUM);
                    break;
                case 11:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("ALLOW REAR VIEW CAMERA"), position, 11, Menus.GetMMSRearViewPIP(), Loc.LS("CLIENTS CAN CHOOSE TO HAVE REAR VIEW"), widthScale);
                    break;
                case 12:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("SCALE RESPAWN TO TEAM SIZE"), position, 12, Menus.GetMMSScaleRespawnTime(), Loc.LS("AUTOMATICALLY SCALE RESPAWN TIME TO TEAM SIZE (e.g. 4 = 4 seconds)"), widthScale,
                        !(MenuManager.mms_mode == ExtMatchMode.TEAM_ANARCHY || MenuManager.mms_mode == ExtMatchMode.CTF || MenuManager.mms_mode == ExtMatchMode.MONSTERBALL));
                    break;
                case 13:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("CLASSIC SPAWNS"), position, 13, Menus.GetMMSClassicSpawns(), Loc.LS("SPAWN WITH IMPULSE+ DUALS AND FALCONS"), widthScale);
                    break;
                case 14:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("CTF CARRIER BOOSTING"), position, 14, Menus.GetMMSCtfCarrierBoost(), Loc.LS("FLAG CARRIER CAN USE BOOST IN CTF"), widthScale, MenuManager.mms_mode != ExtMatchMode.CTF);
                    break;
                case 15:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("ALWAYS CLOAKED"), position, 15, Menus.GetMMSAlwaysCloaked(), Loc.LS("SHIPS ARE ALWAYS CLOAKED"), widthScale);
                    break;
                case 16:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("PROJECTILE DATA"), position, 16, Menus.mms_mp_projdata_fn == "STOCK" ? "STOCK" : Path.GetFileName(Menus.mms_mp_projdata_fn), string.Empty, widthScale);
                    break;
                case 17:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("ALLOW SMASH ATTACK"), position, 17, Menus.GetMMSAllowSmash(), Loc.LS("ALLOWS PLAYERS TO USE THE SMASH ATTACK"), widthScale);
                    break;
                case 18:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("ASSISTS"), position, 18, Menus.GetMMSAssistScoring(), Loc.LS("AWARD POINTS FOR ASSISTING WITH KILLS"), widthScale, MenuManager.mms_mode == ExtMatchMode.CTF);
                    break;
                case 20:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("TB PENETRATION"), position, 20, MPThunderboltPassthrough.isAllowed ? "ON" : "OFF", Loc.LS("ALLOWS THUNDERBOLT SHOTS TO PENETRATE SHIPS"), widthScale);
                    break;
                case 21:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("DAMAGE NUMBERS"), position, 21, Menus.GetMMSDamageNumbers(), Loc.LS("SHOWS THE DAMAGE YOU DO TO OTHER SHIPS"), widthScale);
                    break;
                case 22:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("COLLISION MESH"), position, 22, Menus.GetMMSCollisionMesh(), Loc.LS("COLLIDER TO USE FOR PROJECTILE->SHIP COLLISIONS"), widthScale);
                    break;
                case 23:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("CLIENT-SIDE PHYSICS"), position, 23, MPServerOptimization.prefEnabled ? "ENABLED" : "DISABLED", Loc.LS("ALLOWS CLIENTS TO PASS PRE-PROCESSED PHYSICS RATHER THAN RESIMULATING INPUTS ON THE SERVER"), widthScale);
                    break;
                case 24:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("ROLL SPEED LIMIT"), position, 24, "+" + (MPServerOptimization.RollSpeedLimit - 3), Loc.LS("MAXIMUM ROLL SPEED MODIFIER TO ALLOW FOR THIS ROUND"), widthScale);
                    break;
                case 25:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("DESTRUCTIBLE MISSILES"), position, 25, Menus.GetMMSDestructibleMissiles(), Loc.LS("ALLOWS MISSILES TO BE SHOT DOWN IN FLIGHT"), widthScale);
                    break;
                case 26:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("FIRE WHILE BOOSTING"), position, 26, Menus.GetMMSBoostFire(), Loc.LS("ALLOWS PLAYERS TO FIRE WEAPONS AND MISSILES WHILE BOOSTING"), widthScale);
                    break;
                case 27:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("INSTAREAP"), position, 27, Menus.GetMMSInstareap(), Loc.LS("EVERY SHIP CARRIES ONLY A MASSDRIVER THAT KILLS IN ONE HIT, NO PICKUPS"), widthScale, MenuManager.mms_mode == ExtMatchMode.RACE);
                    break;
                case 32:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("BOTS AUTOMATIC LEAVE"), position, 32, MenuManager.GetToggleSetting(Bots.BotSettingsPage.Settings.ReplaceWithHumans ? 1 : 0),
                        Loc.LS("IF SET THEN EVERYTIME A PLAYER JOINS/LEAVES ONE BOT LEAVES/JOINS"), widthScale, !Bots.BotSettingsPage.Settings.Enabled);
                    break;
                case 28:
                    uie.SelectAndDrawStringOptionItem(Loc.LS("MASSDRIVER RAPID FIRE"), position, 28, Menus.GetMMSInstareapRapidFire(), Loc.LS("INSTAREAP WITHOUT THE MASSDRIVER COOLDOWN, EVERY SHOT STILL PUSHES YOU BACK AND SHAKES YOUR AIM"), widthScale, !Menus.mms_instareap || MenuManager.mms_mode == ExtMatchMode.RACE);
                    break;
            }
        }

        private static void SelectRow(int selection)
        {
            bool engineAlreadyMoved = UIManager.m_menu_selection != previousSelection;
            UIManager.m_menu_selection = selection;
            UIManager.m_sl_prev_valid = false;
            if (!engineAlreadyMoved)
                MenuManager.PlayMoveSound();
        }

        private static void DrawPowerupSettings(UIElement uie)
        {
            bool isMonsterball = MenuManager.mms_mode == MatchMode.MONSTERBALL;
            Vector2 position = new Vector2(0f, TAB_CONTENT_TOP);
            uie.SelectAndDrawStringOptionItem(Loc.LS("OVERALL FREQUENCY"), position, 40, MenuManager.GetMMSPowerupSpawn(), Loc.LS("HOW OFTEN POWERUPS AND SECONDARIES SPAWN"), 1.5f, isMonsterball);
            position.y += ROW_PITCH;
            uie.SelectAndDrawStringOptionItem(Loc.LS("INITIAL SECONDARIES"), position, 41, MenuManager.GetMMSPowerupInitial(), Loc.LS("HOW MANY SECONDARIES SPAWN AT THE START OF THE MATCH"), 1.5f, isMonsterball);
            position.y += ROW_PITCH;
            uie.SelectAndDrawStringOptionItem(Loc.LS("SUPER FREQUENCY"), position, 42, MenuManager.GetMMSPowerupBigSpawn(), Loc.LS("HOW OFTEN SUPER POWERUPS WILL SPAWN"), 1.5f, isMonsterball);

            position.y += 103f;
            uie.DrawSmallHeader1(position - Vector2.up * 44f, Loc.LS("ALLOWED PRIMARY WEAPONS"), 300f);
            DrawPowerupCheckboxes(uie, ref position, 0, i => Player.GetWeaponNameNoDefault((WeaponType)i));
            position.y += 92f;
            uie.DrawSmallHeader1(position - Vector2.up * 44f, Loc.LS("ALLOWED SECONDARY WEAPONS"), 300f);
            DrawPowerupCheckboxes(uie, ref position, 8, i => Player.GetMissileNameNoDefault((MissileType)i));
            position.y += 58f;
            uie.SelectAndDrawHalfItem2(Loc.LS("CLEAR"), position - Vector2.right * 160f, 20, false);
            uie.SelectAndDrawHalfItem2(Loc.LS("RESET"), position + Vector2.right * 160f, 21, false);
            DrawTabFooter(uie, position.y);
        }

        private static void DrawPowerupCheckboxes(UIElement uie, ref Vector2 position, int firstSelection, Func<int, string> name)
        {
            for (int i = 0; i < 8; i++)
            {
                if (i == 4)
                    position.y += 52f;
                int column = i % 4;
                uie.SelectAndDrawCheckboxItem(name(i), position + Vector2.right * ((column - 1.5f) * 320f), firstSelection + i, MenuManager.mms_powerup_filter[firstSelection + i], false, 0.5f);
            }
        }

        private static void DrawLoadoutSettings(UIElement uie)
        {
            bool isFree = MenuManager.mms_force_loadout == 0;
            Vector2 position = new Vector2(0f, TAB_CONTENT_TOP);
            uie.SelectAndDrawItem("ALLOWED LOADOUT WEAPONS", position - Vector2.right * 280f, 23, false, 0.9f, 0.6f);
            uie.SelectAndDrawItem("ALLOWED MODIFIERS", position + Vector2.right * 280f, 8, false, 0.9f, 0.6f);
            position.y += 40f;
            uie.DrawMenuSeparator(position);
            position.y += 40f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("USE CUSTOM LOADOUT"), position, 0, MenuManager.GetToggleSetting(MenuManager.mms_force_loadout), Loc.LS("FORCE ALL PLAYERS TO START WITH IDENTICAL WEAPONS"));
            position.y += ROW_PITCH;
            uie.SelectAndDrawStringOptionItem(Loc.LS("CUSTOM WEAPON 1"), position, 1, MenuManager.GetMMSForceWeapon(MenuManager.mms_force_w1), string.Empty, 1.5f, isFree);
            position.y += ROW_PITCH;
            uie.SelectAndDrawStringOptionItem(Loc.LS("CUSTOM WEAPON 2"), position, 2, MenuManager.GetMMSForceWeapon(MenuManager.mms_force_w2), string.Empty, 1.5f, isFree);
            position.y += ROW_PITCH;
            uie.SelectAndDrawStringOptionItem(Loc.LS("CUSTOM MISSILE 1"), position, 3, MenuManager.GetMMSForceMissile(MenuManager.mms_force_m1), string.Empty, 1.5f, isFree);
            position.y += ROW_PITCH;
            uie.SelectAndDrawStringOptionItem(Loc.LS("CUSTOM MISSILE 2"), position, 4, MenuManager.GetMMSForceMissile(MenuManager.mms_force_m2), string.Empty, 1.5f, isFree);
            position.y += ROW_PITCH;
            uie.SelectAndDrawStringOptionItem(Loc.LS("CUSTOM MODIFIER 1"), position, 5, MenuManager.GetMMSCustomModifier1(), Loc.LS("FORCE ALL PLAYERS TO USE A SPECIFIC MODIFIER"));
            position.y += ROW_PITCH;
            uie.SelectAndDrawStringOptionItem(Loc.LS("CUSTOM MODIFIER 2"), position, 6, MenuManager.GetMMSCustomModifier2(), Loc.LS("FORCE ALL PLAYERS TO USE A SPECIFIC MODIFIER"));
            DrawTabFooter(uie, position.y);
        }

        public static void Update(bool online, ref float menuStateTimer)
        {
            if (MenuManager.m_menu_sub_state == MenuSubState.ACTIVE && MenuManager.m_menu_micro_state == 8)
                MPLevelSelect.UpdatePage();
            else
                UpdateCurrentPage(online, ref menuStateTimer);

            if (MenuManager.m_menu_micro_state == 0)
                MPJoinInProgress.SingleMatchEnable = false;
            MPLoadouts.KeepReflexOutOfPowerups();
            if (MenuManager.m_menu_sub_state == MenuSubState.ACTIVE && MenuManager.m_menu_micro_state != 2)
            {
                PresetData.UpdateLobbyStatus();
                MPModifiers.UpdateLobbyStatus();
            }
        }

        private static void UpdateCurrentPage(bool online, ref float menuStateTimer)
        {
            UIManager.MouseSelectUpdate();
            switch (MenuManager.m_menu_sub_state)
            {
                case MenuSubState.INIT:
                    if (menuStateTimer > 0.25f)
                        OpenMenu(online);
                    break;
                case MenuSubState.ACTIVE:
                    Controls.m_disable_menu_letter_keys = false;
                    if ((MenuManager.m_menu_micro_state == 1 || MenuManager.m_menu_micro_state == 4) && UIManager.m_menu_selection == 11)
                    {
                        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.V))
                            MenuManager.mms_match_password = GUIUtility.systemCopyBuffer;
                        Controls.m_disable_menu_letter_keys = true;
                        string password = MenuManager.mms_match_password;
                        bool entered = ProcessInputField(ref password, PasswordFieldName(), MenuManager.mms_hide_password);
                        MenuManager.mms_match_password = password;
                        if (!entered)
                            break;
                    }
                    if (MenuManager.m_menu_micro_state == Bots.BotSettingsPage.MICRO_STATE)
                        Bots.BotSettingsPage.Update();
                    if (MenuManager.m_menu_micro_state == Bots.BotSettingsPage.MICRO_STATE && UIManager.m_menu_selection == Bots.BotSettingsPage.SELECTION_NAME)
                    {
                        Controls.m_disable_menu_letter_keys = true;
                        string name = Bots.BotSettingsPage.Draft.Name;
                        bool entered = ProcessInputField(ref name, Loc.LS("BOT NAME"), false);
                        Bots.BotSettingsPage.TypeName(name, entered);
                        break;
                    }
                    if (MenuManager.m_menu_micro_state == 2 && UIManager.m_menu_selection == 303)
                    {
                        Controls.m_disable_menu_letter_keys = true;
                        if (MPServerBrowser.UpdateMatchNotes(ref menuStateTimer))
                            break;
                    }
                    UIManager.ControllerMenu();
                    if (IsTab(MenuManager.m_menu_micro_state))
                    {
                        int tab = Array.IndexOf(tabs, MenuManager.m_menu_micro_state);
                        if (Controls.JustPressed(CCInput.MENU_PGUP))
                            OpenTab((tab + tabs.Length - 1) % tabs.Length);
                        else if (Controls.JustPressed(CCInput.MENU_PGDN))
                            OpenTab((tab + 1) % tabs.Length);
                    }
                    if (MenuManager.m_menu_micro_state == Bots.BotSettingsPage.MICRO_STATE)
                    {
                        int row = Bots.BotSettingsPage.ScrollWithSelection(previousSelection);
                        if (row >= 0)
                            SelectRow(row);
                    }
                    previousSelection = UIManager.m_menu_selection;
                    if (!UIManager.PushedSelect(100) && (!MenuManager.option_dir || !UIManager.PushedDir()))
                        break;
                    MenuManager.MaybeReverseOption();
                    SelectOnCurrentPage(online, ref menuStateTimer);
                    break;
                case MenuSubState.BACK:
                    if (menuStateTimer > 0.25f)
                        MenuManager.ChangeMenuState(MenuState.MP_MENU);
                    break;
                case MenuSubState.CONTINUE:
                    if (menuStateTimer > 4f)
                    {
                        MenuManager.m_menu_sub_state = MenuSubState.ACTIVE;
                        menuStateTimer = 0f;
                    }
                    break;
            }
        }

        private static void OpenMenu(bool online)
        {
            int numLevels = GameManager.MultiplayerMission.NumLevels;
            if (MenuManager.mms_level_num < 0 || MenuManager.mms_level_num >= numLevels)
                MenuManager.mms_level_num = 0;
            if (online)
            {
                for (int i = 0; i < numLevels; i++)
                {
                    if (!GameManager.MultiplayerMission.IsLevelAnAddon(MenuManager.mms_level_num))
                        break;
                    MenuManager.mms_level_num = (MenuManager.mms_level_num + 1) % numLevels;
                }
            }
            UIManager.CreateUIElement(UIManager.SCREEN_CENTER, 7000, UIElementType.MP_MATCH_SETUP, online ? Loc.LS("PRIVATE MATCH") : ClientModeName());
            MenuManager.SetDefaultSelection(0);
            MenuManager.m_game_paused = false;
            MenuManager.m_menu_sub_state = MenuSubState.ACTIVE;
            if (MenuManager.m_updating_pm_settings)
            {
                MenuManager.m_menu_micro_state = 2;
            }
            else
            {
                MenuManager.m_menu_micro_state = 0;
                MPServerBrowser.OpenSelectedServerPage();
                string password = MenuManager._mms_match_password;
                if (password == string.Empty || password.Length < 3 || password.Length > 16)
                {
                    MenuManager._mms_match_password = UnityEngine.Random.Range(1000, 10000).ToString();
                    MenuManager.mms_hide_password = false;
                }
            }
            UIManager.SetLevelTexture(GameManager.MultiplayerMission, MenuManager.mms_level_num);
        }

        private static void SelectOnCurrentPage(bool online, ref float menuStateTimer)
        {
            int selection = UIManager.m_menu_selection;
            if (IsTab(MenuManager.m_menu_micro_state))
            {
                if (selection >= TAB_SELECTION && selection < TAB_SELECTION + tabs.Length)
                {
                    OpenTab(selection - TAB_SELECTION);
                    return;
                }
                if (selection == 100)
                {
                    Bots.BotSettingsPage.StopEditing();
                    MenuManager.SetDefaultSelection(7);
                    GoToPage(2);
                    return;
                }
            }
            switch (MenuManager.m_menu_micro_state)
            {
                case 0:
                    switch (selection)
                    {
                        case 0:
                            GoToPage(1);
                            break;
                        case 1:
                            GoToPage(2);
                            MenuManager.SetDefaultSelection(7);
                            break;
                        case 2:
                            GoToPage(2);
                            MenuManager.SetDefaultSelection(7);
                            MPJoinInProgress.SingleMatchEnable = true;
                            break;
                        case 100:
                            UIManager.DestroyAll();
                            menuStateTimer = 0f;
                            MenuManager.m_menu_sub_state = MenuSubState.BACK;
                            MenuManager.PlaySelectSound();
                            break;
                    }
                    break;
                case 1:
                case 4:
                    SelectOnPasswordPage(online, selection, ref menuStateTimer);
                    break;
                case 2:
                    SelectOnMatchSettings(online, selection);
                    break;
                case TAB_RULES:
                case TAB_TEAMS:
                case TAB_SERVER:
                    SelectOnSettingsTab(selection);
                    break;
                case TAB_POWERUPS:
                    SelectOnPowerupSettings(selection);
                    break;
                case TAB_LOADOUTS:
                    SelectOnLoadoutSettings(selection);
                    break;
                case 9:
                    if (selection == 100)
                        GoToPage(6);
                    else
                        MPModifiers.SelectOnAllowedModifiersPage(selection);
                    break;
                case Bots.BotSettingsPage.MICRO_STATE:
                    Bots.BotSettingsPage.Select(selection);
                    break;
                case 15:
                    if (selection == 22)
                        OpenTab(Array.IndexOf(tabs, TAB_POWERUPS));
                    else if (selection == 100)
                        GoToPage(6);
                    else
                        MPLoadouts.SelectOnAllowedLoadoutsPage(selection);
                    break;
            }
        }

        private static void SelectOnPasswordPage(bool online, int selection, ref float menuStateTimer)
        {
            switch (selection)
            {
                case 0:
                    if (online)
                        MenuManager.m_mp_status = Loc.LS("JOINING PRIVATE MATCH");
                    else
                        MenuManager.m_mp_status = MPInternet.Enabled ? Loc.LS("JOINING INTERNET MATCH") : Loc.LS("JOINING LAN MATCH");
                    MenuManager.PlaySelectSound();
                    if (MPInternet.Enabled && !ParsePassword(MenuManager.mms_match_password))
                    {
                        menuStateTimer = 0f;
                        MenuManager.m_menu_sub_state = MenuSubState.CONTINUE;
                    }
                    else
                    {
                        NetworkMatch.JoinPrivateLobby(MenuManager.mms_match_password);
                        UIManager.DestroyAll();
                    }
                    break;
                case 1:
                    var privateMatchData = (PrivateMatchDataMessage)_MenuManager_BuildPrivateMatchData_Method.Invoke(null, new object[] { online });
                    if (online)
                        MenuManager.m_mp_status = Loc.LS("CREATING PRIVATE MATCH");
                    else
                        MenuManager.m_mp_status = MPInternet.Enabled ? Loc.LS("CREATING INTERNET MATCH") : Loc.LS("CREATING LAN MATCH");
                    if (!MenuManager.mms_hide_password)
                        MenuManager.AddMpStatus(PasswordFieldName() + ": " + MenuManager.mms_match_password);
                    MenuManager.PlaySelectSound();
                    if (MPInternet.Enabled && !ParsePassword(MenuManager.mms_match_password))
                    {
                        menuStateTimer = 0f;
                        MenuManager.m_menu_sub_state = MenuSubState.CONTINUE;
                    }
                    else
                    {
                        NetworkMatch.StartPrivateLobby(privateMatchData);
                        UIManager.DestroyAll();
                    }
                    break;
                case 10:
                    MenuManager.mms_hide_password = !MenuManager.mms_hide_password;
                    MenuManager.PlayCycleSound();
                    break;
                case 11:
                    MenuManager.SetDefaultSelection(MenuManager.m_menu_micro_state == 1 ? 0 : 1);
                    MenuManager.PlayCycleSound();
                    break;
                case 21:
                    Application.OpenURL("https://tracker.otl.gg/");
                    break;
                case 22:
                    Application.OpenURL("https://discord.gg/WgG2CnS");
                    break;
                case 100:
                    GoToPage(MenuManager.m_menu_micro_state == 1 ? 0 : 2);
                    MenuManager.SetDefaultSelection(0);
                    break;
            }
        }

        private static void SelectOnMatchSettings(bool online, int selection)
        {
            switch (selection)
            {
                case 0:
                    MenuManager.mms_mode = (MatchMode)(((int)MenuManager.mms_mode + (int)ExtMatchMode.NUM + UIManager.m_select_dir) % (int)ExtMatchMode.NUM);
                    MenuManager.mms_respawn_time = MenuManager.mms_mode == ExtMatchMode.RACE ? 0 : 2;
                    PlayCycleSound();
                    break;
                case 1:
                    MenuManager.mms_max_players = Mathf.Clamp(MenuManager.mms_max_players + UIManager.m_select_dir, online ? 2 : 1, 16);
                    PlayCycleSound();
                    break;
                case 2:
                    if (MPLevelSelect.OpenOnLevelClick())
                        break;
                    int numLevels = GameManager.MultiplayerMission.NumLevels;
                    for (int i = 0; i < numLevels; i++)
                    {
                        MenuManager.mms_level_num = (MenuManager.mms_level_num + numLevels + UIManager.m_select_dir) % numLevels;
                        if (!online || !GameManager.MultiplayerMission.IsLevelAnAddon(MenuManager.mms_level_num))
                            break;
                    }
                    UIManager.SetLevelTexture(GameManager.MultiplayerMission, MenuManager.mms_level_num);
                    PlayCycleSound();
                    break;
                case 4:
                    Menus.mms_match_time_limit = ((Menus.mms_match_time_limit / 60 + 21 + UIManager.m_select_dir) % 21) * 60;
                    PlayCycleSound();
                    break;
                case 5:
                    CycleScoreLimit();
                    PlayCycleSound();
                    break;
                case 6:
                    MenuManager.SetDefaultSelection(0);
                    GoToPage(TAB_RULES);
                    break;
                case 7:
                    if (MenuManager.m_updating_pm_settings)
                    {
                        MenuManager.m_updating_pm_settings = false;
                        UIManager.DestroyAll();
                        MenuManager.ChangeMenuState(MenuState.MP_PRE_MATCH_MENU);
                    }
                    else
                    {
                        MenuManager.SetDefaultSelection(0);
                        MenuManager.m_menu_micro_state = 4;
                        MenuManager.UIPulse(2f);
                    }
                    MenuManager.PlaySelectSound();
                    break;
                case 9:
                    MPMatchPresets.CyclePreset();
                    PlayCycleSound();
                    break;
                case 31:
                    Bots.BotSettingsPage.Settings.Enabled = !Bots.BotSettingsPage.Settings.Enabled;
                    PlayCycleSound();
                    break;
                case 10:
                    ExtMenuManager.mms_ext_lap_limit = Math.Max(0, Math.Min(50, ExtMenuManager.mms_ext_lap_limit + UIManager.m_select_dir * 5));
                    PlayCycleSound();
                    break;
                case 100:
                    if (MenuManager.m_updating_pm_settings)
                    {
                        MenuManager.m_updating_pm_settings = false;
                        UIManager.DestroyAll();
                        MenuManager.ChangeMenuState(MenuState.MP_PRE_MATCH_MENU);
                    }
                    else
                    {
                        GoToPage(0);
                    }
                    break;
            }
        }

        private static void CycleScoreLimit()
        {
            if (UIManager.m_select_dir > 0)
            {
                MenuManager.mms_score_limit += MenuManager.mms_score_limit > 45 ? 10 : 5;
                if (MenuManager.mms_score_limit > 100)
                    MenuManager.mms_score_limit = 0;
            }
            else
            {
                MenuManager.mms_score_limit -= MenuManager.mms_score_limit > 50 ? 10 : 5;
                if (MenuManager.mms_score_limit < 0)
                    MenuManager.mms_score_limit = 100;
            }
        }

        private static void SelectOnSettingsTab(int selection)
        {
            switch (selection)
            {
                case 0:
                    MenuManager.mms_show_names = (MatchShowEnemyNames)(((int)MenuManager.mms_show_names + 3 + UIManager.m_select_dir) % 3);
                    PlayCycleSound();
                    break;
                case 1:
                    MenuManager.mms_respawn_time = Mathf.Clamp(MenuManager.mms_respawn_time + UIManager.m_select_dir, 0, 10);
                    PlayCycleSound();
                    break;
                case 2:
                    MenuManager.mms_respawn_invuln = Mathf.Clamp(MenuManager.mms_respawn_invuln + UIManager.m_select_dir, 0, 5);
                    PlayCycleSound();
                    break;
                case 6:
                    MenuManager.mms_turn_speed_limit = (MenuManager.mms_turn_speed_limit + 5 - UIManager.m_select_dir) % 5;
                    PlayCycleSound();
                    break;
                case 7:
                    MPJoinInProgress.MenuManagerEnabled = !MPJoinInProgress.MenuManagerEnabled;
                    PlayCycleSound();
                    break;
                case 10:
                    MPSuddenDeath.SuddenDeathMenuEnabled = !MPSuddenDeath.SuddenDeathMenuEnabled;
                    PlayCycleSound();
                    break;
                case 11:
                    RearView.MPMenuManagerEnabled = !RearView.MPMenuManagerEnabled;
                    PlayCycleSound();
                    break;
                case 13:
                    Menus.mms_classic_spawns = !Menus.mms_classic_spawns;
                    PlayCycleSound();
                    if (Menus.mms_classic_spawns)
                        MenuManager.mms_powerup_filter[2] = true;
                    break;
                case 14:
                    Menus.mms_ctf_boost = !Menus.mms_ctf_boost;
                    PlayCycleSound();
                    break;
                case 15:
                    Menus.mms_always_cloaked = !Menus.mms_always_cloaked;
                    PlayCycleSound();
                    break;
                case 16:
                    if (UIManager.PushedSelect(100))
                    {
                        Menus.mms_mp_projdata_fn = NextProjdataFile(Menus.mms_mp_projdata_fn);
                        MenuManager.PlayCycleSound(1f, 1f);
                    }
                    break;
                case 17:
                    Menus.mms_allow_smash = !Menus.mms_allow_smash;
                    PlayCycleSound();
                    break;
                case 18:
                    Menus.mms_assist_scoring = !Menus.mms_assist_scoring;
                    PlayCycleSound();
                    break;
                case 20:
                    MPThunderboltPassthrough.isAllowed = !MPThunderboltPassthrough.isAllowed;
                    PlayCycleSound();
                    break;
                case 21:
                    Menus.mms_damage_numbers = !Menus.mms_damage_numbers;
                    PlayCycleSound();
                    break;
                case 22:
                    Menus.mms_collision_mesh = (4 + Menus.mms_collision_mesh + UIManager.m_select_dir) % 4;
                    PlayCycleSound();
                    break;
                case 23:
                    MPServerOptimization.prefEnabled = !MPServerOptimization.prefEnabled;
                    PlayCycleSound();
                    break;
                case 24:
                    MPServerOptimization.RollSpeedLimit = 3 + ((2 + MPServerOptimization.RollSpeedLimit + UIManager.m_select_dir) % 5);
                    PlayCycleSound();
                    break;
                case 25:
                    Menus.mms_destructible_missiles = !Menus.mms_destructible_missiles;
                    PlayCycleSound();
                    break;
                case 26:
                    Menus.mms_boost_fire = !Menus.mms_boost_fire;
                    PlayCycleSound();
                    break;
                case 27:
                    Menus.mms_instareap = !Menus.mms_instareap;
                    PlayCycleSound();
                    break;
                case 28:
                    if (!Menus.mms_instareap)
                        break;
                    Menus.mms_instareap_rapid_fire = !Menus.mms_instareap_rapid_fire;
                    PlayCycleSound();
                    break;
                case 32:
                    if (!Bots.BotSettingsPage.Settings.Enabled)
                        break;
                    Bots.BotSettingsPage.Settings.ReplaceWithHumans = !Bots.BotSettingsPage.Settings.ReplaceWithHumans;
                    PlayCycleSound();
                    break;
                case 3:
                    MenuManager.mms_friendly_fire = 1 - MenuManager.mms_friendly_fire;
                    MenuManager.PlayCycleSound(1f, 1f);
                    break;
                case 8:
                    int teamChoices = MPTeams.Max - MPTeams.Min + 1;
                    MPTeams.MenuManagerTeamCount = MPTeams.Min + (MPTeams.MenuManagerTeamCount - MPTeams.Min + teamChoices + UIManager.m_select_dir) % teamChoices;
                    PlayCycleSound();
                    break;
                case 12:
                    Menus.mms_scale_respawn_time = !Menus.mms_scale_respawn_time;
                    PlayCycleSound();
                    break;
            }
        }

        private static string NextProjdataFile(string current)
        {
            string[] files = new string[] { "STOCK" }.AddRangeToArray(Directory.GetFiles(Config.OLModDir, "projdata-*.txt"));
            int index = Array.IndexOf(files, current);
            return index < 0 ? current : files[(index + 1) % files.Length];
        }

        private static void SelectOnPowerupSettings(int selection)
        {
            switch (selection)
            {
                case 40:
                    MenuManager.mms_powerup_spawn = (MenuManager.mms_powerup_spawn + 4 + UIManager.m_select_dir) % 4;
                    PlayCycleSound();
                    break;
                case 41:
                    MenuManager.mms_powerup_initial = (MenuManager.mms_powerup_initial + 4 + UIManager.m_select_dir) % 4;
                    PlayCycleSound();
                    break;
                case 42:
                    MenuManager.mms_powerup_big_spawn = (MenuManager.mms_powerup_big_spawn + 4 + UIManager.m_select_dir) % 4;
                    PlayCycleSound();
                    break;
                case 20:
                case 21:
                    for (int i = 0; i < MenuManager.mms_powerup_filter.Length; i++)
                        MenuManager.mms_powerup_filter[i] = selection == 21;
                    MenuManager.PlaySelectSound();
                    break;
                default:
                    if (selection >= 0 && selection < MenuManager.mms_powerup_filter.Length)
                    {
                        MenuManager.mms_powerup_filter[selection] = !MenuManager.mms_powerup_filter[selection];
                        MenuManager.PlayCycleSound();
                    }
                    break;
            }
        }

        private static void SelectOnLoadoutSettings(int selection)
        {
            switch (selection)
            {
                case 0:
                    MenuManager.mms_force_loadout = 1 - MenuManager.mms_force_loadout;
                    MenuManager.PlayCycleSound();
                    break;
                case 1:
                    MenuManager.mms_force_w1 = (WeaponType)(((int)MenuManager.mms_force_w1 + 8 + UIManager.m_select_dir) % 8);
                    if (MenuManager.mms_force_w1 == MenuManager.mms_force_w2)
                        MenuManager.mms_force_w1 = (WeaponType)(((int)MenuManager.mms_force_w1 + 8 + UIManager.m_select_dir) % 8);
                    PlayCycleSound();
                    break;
                case 2:
                    MenuManager.mms_force_w2 = (WeaponType)(((int)MenuManager.mms_force_w2 + 9 + UIManager.m_select_dir) % 9);
                    if (MenuManager.mms_force_w1 == MenuManager.mms_force_w2)
                        MenuManager.mms_force_w2 = (WeaponType)(((int)MenuManager.mms_force_w2 + 9 + UIManager.m_select_dir) % 9);
                    PlayCycleSound();
                    break;
                case 3:
                    MenuManager.mms_force_m1 = (MissileType)(((int)MenuManager.mms_force_m1 + 9 + UIManager.m_select_dir) % 9);
                    if (MenuManager.mms_force_m1 == MenuManager.mms_force_m2 && MenuManager.mms_force_m1 != MissileType.NUM)
                        MenuManager.mms_force_m1 = (MissileType)(((int)MenuManager.mms_force_m1 + 9 + UIManager.m_select_dir) % 9);
                    PlayCycleSound();
                    break;
                case 4:
                    MenuManager.mms_force_m2 = (MissileType)(((int)MenuManager.mms_force_m2 + 9 + UIManager.m_select_dir) % 9);
                    if (MenuManager.mms_force_m1 == MenuManager.mms_force_m2 && MenuManager.mms_force_m2 != MissileType.NUM)
                        MenuManager.mms_force_m2 = (MissileType)(((int)MenuManager.mms_force_m2 + 9 + UIManager.m_select_dir) % 9);
                    PlayCycleSound();
                    break;
                case 5:
                    MenuManager.mms_force_modifier1 = (MenuManager.mms_force_modifier1 + 5 + UIManager.m_select_dir) % 5;
                    PlayCycleSound();
                    break;
                case 6:
                    MenuManager.mms_force_modifier2 = (MenuManager.mms_force_modifier2 + 5 + UIManager.m_select_dir) % 5;
                    PlayCycleSound();
                    break;
                case 8:
                    GoToPage(9);
                    break;
                case 23:
                    GoToPage(15);
                    break;
            }
        }

        private static void OpenTab(int tab)
        {
            if (MenuManager.m_menu_micro_state == Bots.BotSettingsPage.MICRO_STATE)
                Bots.BotSettingsPage.StopEditing();
            MenuManager.m_menu_micro_state = tabs[tab];
            if (tabs[tab] == Bots.BotSettingsPage.MICRO_STATE)
                Bots.BotSettingsPage.Open();
            UIManager.m_menu_selection = TAB_SELECTION + tab;
            MenuManager.UIPulse(1f);
            _MenuManager_PlayHighlightSound_Method.Invoke(null, new object[] { 0.4f, 0.07f });
        }

        private static void GoToPage(int microState)
        {
            MenuManager.m_menu_micro_state = microState;
            MenuManager.UIPulse(2f);
            MenuManager.PlaySelectSound();
        }

        private static void PlayCycleSound()
        {
            MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
        }

        private static string ClientModeName()
        {
            return (string)_InternetMatch_ClientModeName_Method.Invoke(null, null);
        }

        private static string PasswordFieldName()
        {
            return (string)_InternetMatch_PasswordFieldName_Method.Invoke(null, null);
        }

        private static bool ParsePassword(string password)
        {
            return (bool)_InternetMatch_ParsePassword_Method.Invoke(null, new object[] { password });
        }

        private static bool ProcessInputField(ref string text, string title, bool hide)
        {
            object[] args = { text, title, hide };
            bool entered = (bool)_MenuManager_ProcessInputField_Method.Invoke(null, args);
            text = (string)args[0];
            return entered;
        }
    }
}
