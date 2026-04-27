using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace GameMod
{
    class MPAutoSelection
    {
        public static WeaponType[] PrimaryPriority = new WeaponType[8];
        public static MissileType[] SecondaryPriority = new MissileType[8];
        public static bool[] PrimaryNeverSelect = new bool[8];
        public static bool[] SecondaryNeverSelect = new bool[8];

        public static bool primarySwapFlag = true;
        public static bool secondarySwapFlag = true;
        public static bool zorc = false;
        public static bool miasmic = false;
        public static bool swapWhileFiring = false;
        public static bool dontAutoselectAfterFiring = false;

        public static string last_valid_description = "CHANGE THE ORDER BY CLICKING AT THE TWO WEAPONS YOU WANT TO SWAP";

        private static WeaponType pendingWeaponSwap = WeaponType.NUM;
        private static float thunderboltSwapDelay = 0.025f;

        private static MissileType pendingMissileSwap = MissileType.NUM;
        private static int missileSwapDelay = 0;
        private static int pendingMissileAutoSwapDelay = 0;

        private static bool IsEnergyWeapon(WeaponType wt) =>
            wt == WeaponType.IMPULSE || wt == WeaponType.CYCLONE || wt == WeaponType.REFLEX ||
            wt == WeaponType.THUNDERBOLT || wt == WeaponType.LANCER;

        public static int getWeaponPriority(WeaponType wt)
        {
            for (int i = 0; i < 8; i++)
                if (PrimaryPriority[i] == wt) return i;
            return -1;
        }

        public static int getMissilePriority(MissileType mt)
        {
            for (int i = 0; i < 8; i++)
                if (SecondaryPriority[i] == mt) return i;
            return -1;
        }

        private static bool areThereAllowedPrimaries()
        {
            for (int i = 0; i < 8; i++)
                if (!PrimaryNeverSelect[i]) return true;
            return false;
        }

        public static bool areThereAllowedSecondaries()
        {
            for (int i = 0; i < 8; i++)
                if (!SecondaryNeverSelect[i]) return true;
            return false;
        }

        public static int findHighestPrioritizedUseableMissile()
        {
            for (int i = 0; i < 8; i++)
            {
                if (SecondaryNeverSelect[i]) continue;
                int idx = (int)SecondaryPriority[i];
                if (GameManager.m_local_player.m_missile_level[idx] != WeaponUnlock.LOCKED
                    && GameManager.m_local_player.m_missile_ammo[idx] > 0)
                    return idx;
            }
            return -1;
        }

        public static bool maybeSwapPrimary(bool silent = false)
        {
            if (!areThereAllowedPrimaries()) return false;
            var p = GameManager.m_local_player;
            bool energyOnly = p.m_energy > 0 && !(p.m_ammo > 0);
            bool ammoOnly = !(p.m_energy > 0) && p.m_ammo > 0;

            for (int i = 0; i < 8; i++)
            {
                WeaponType wt = PrimaryPriority[i];
                if (PrimaryNeverSelect[i]) continue;
                if (p.m_weapon_level[(int)wt] == WeaponUnlock.LOCKED) continue;
                if (energyOnly && !IsEnergyWeapon(wt)) continue;
                if (ammoOnly && IsEnergyWeapon(wt)) continue;
                swapToWeapon(wt, silent);
                return true;
            }
            return false;
        }

        public static void maybeSwapMissiles(bool silent = false)
        {
            int idx = findHighestPrioritizedUseableMissile();
            if (idx >= 0) swapToMissile(idx, silent);
        }

        private static void swapToWeapon(WeaponType wt, bool silent = false)
        {
            if (GameManager.m_local_player.m_weapon_type == wt) return;

            GameManager.m_local_player.Networkm_weapon_type = wt;
            GameManager.m_local_player.CallCmdSetCurrentWeapon(wt);

            if (GameManager.m_game_state != GameManager.GameState.GAMEPLAY) return;

            GameManager.m_local_player.c_player_ship.m_thunder_power = 0f;
            GameManager.m_local_player.c_player_ship.SwitchVisibleWeapon(false, WeaponType.NUM);

            if (silent) return;

            UIElement.WEAPON_SELECT_FLASH = 1.25f;
            UIElement.WEAPON_SELECT_NAME = string.Format(Loc.LS("{0} SELECTED"), Player.WeaponNames[wt]);
            SFXCueManager.PlayCue2D(SFXCue.hud_cycle_typeA1, 1f, 0f, 0f, false);
            GameManager.m_audio.PlayCue2D(363, 0.1f, 0f, 0f, false);
            GameManager.m_local_player.c_player_ship.SetRefireDelayAfterWeaponSwitch();
            SFXCueManager.PlayRawSoundEffect2D(SoundEffect.hud_notify_message1, 1f, 0.15f, 0.1f, false);
        }

        public static void swapToMissile(int weapon_num, bool silent = false)
        {
            var p = GameManager.m_local_player;
            if (p.m_missile_level[weapon_num] == WeaponUnlock.LOCKED || p.m_missile_ammo[weapon_num] == 0)
                return;
            if (p.m_missile_type == (MissileType)weapon_num) return;

            p.Networkm_missile_type = (MissileType)weapon_num;
            p.CallCmdSetCurrentMissile(p.Networkm_missile_type);

            if (GameManager.m_game_state != GameManager.GameState.GAMEPLAY) return;

            p.UpdateCurrentMissileName();

            if (silent) return;

            UIElement.WEAPON_SELECT_FLASH = 1.25f;
            UIElement.WEAPON_SELECT_NAME = string.Format(Loc.LS("{0} SELECTED"), Player.MissileNames[p.m_missile_type]);
            SFXCueManager.PlayCue2D(SFXCue.hud_cycle_typeA2, 1f, 0f, 0f, false);
            GameManager.m_audio.PlayCue2D(362, 0.1f, 0f, 0f, false);
            p.c_player_ship.SetRefireDelayAfterMissileSwitch();
            if (p.m_missile_type == MissileType.DEVASTATOR)
                SFXCueManager.PlayCue2D(SFXCue.hud_warning_selected_dev, 1f, 0f, 0f, false);
        }


        [HarmonyPatch(typeof(GameManager), "Start")]
        internal class CommandsAndInitialisationPatch
        {
            private static void Postfix()
            {
                uConsole.RegisterCommand("toggleprimaryorder", "toggles all Weapon Selection logic related to primary weapons", new uConsole.DebugCommand(CmdTogglePrimary));
                uConsole.RegisterCommand("togglesecondaryorder", "toggles all Weapon Selection logic related to secondary weapons", new uConsole.DebugCommand(CmdToggleSecondary));
                uConsole.RegisterCommand("toggle_hud", "Toggles some HUD elements", new uConsole.DebugCommand(CmdToggleHud));
                MenuManager.opt_primary_autoswitch = 0;
            }

            private static void CmdToggleHud()
            {
                miasmic = !miasmic;
                uConsole.Log("Toggled HUD! current state : " + miasmic);
                ExtendedConfig.Section_AutoSelect.Set(true);
            }

            private static void CmdTogglePrimary()
            {
                primarySwapFlag = !primarySwapFlag;
                uConsole.Log("[AS] Primary weapon swapping: " + primarySwapFlag);
                ExtendedConfig.Section_AutoSelect.Set(true);
            }

            private static void CmdToggleSecondary()
            {
                secondarySwapFlag = !secondarySwapFlag;
                uConsole.Log("[AS] Secondary weapon swapping: " + secondarySwapFlag);
                ExtendedConfig.Section_AutoSelect.Set(true);
            }
        }

        [HarmonyPatch(typeof(UIElement), "DrawHUDArmor")]
        internal class MaybeDrawHUDElement1
        {
            public static bool Prefix() => !miasmic;
        }

        [HarmonyPatch(typeof(UIElement), "DrawHUDEnergyAmmo")]
        internal class MaybeDrawHUDElement2
        {
            public static bool Prefix() => !miasmic;
        }

        [HarmonyPatch(typeof(UIElement), "DrawHUDIndicators")]
        internal class MaybeDrawHUDElement3
        {
            public static bool Prefix() => !miasmic;
        }

        [HarmonyPatch(typeof(Player), "UnlockWeaponClient")]
        internal class WeaponPickup
        {
            public static void Postfix(WeaponType wt, Player __instance)
            {
                UnlockWeaponEvent(wt, __instance);
            }

            public static void UnlockWeaponEvent(WeaponType wt, Player __instance)
            {
                if (MenuManager.opt_primary_autoswitch != 0 || !primarySwapFlag) return;
                if (__instance != GameManager.m_local_player) return;

                int newPriority = getWeaponPriority(wt);
                int curPriority = getWeaponPriority(GameManager.m_local_player.m_weapon_type);
                if (PrimaryNeverSelect[newPriority]) return;
                bool impulseUpgrade = MPClassic.matchEnabled
                    && GameManager.m_local_player.m_weapon_type == WeaponType.IMPULSE
                    && GameManager.m_local_player.m_weapon_level[0] == WeaponUnlock.LEVEL_1;
                if (newPriority >= curPriority && !impulseUpgrade) return;

                if (!Controls.IsPressed(CCInput.FIRE_WEAPON) || swapWhileFiring)
                {
                    swapToWeapon(wt);
                    GameManager.m_local_player.UpdateCurrentWeaponName();
                }
                else
                {
                    pendingWeaponSwap = wt;
                }
            }
        }

        [HarmonyPatch(typeof(Player), "SwitchToAmmoWeapon")]
        internal class OutOfAmmo
        {
            private static bool Prefix(Player __instance)
            {
                if (MenuManager.opt_primary_autoswitch != 0 || !primarySwapFlag) return true;
                if (__instance != GameManager.m_local_player) return true;
                return !maybeSwapPrimary();
            }
        }

        [HarmonyPatch(typeof(Player), "SwitchToEnergyWeapon")]
        internal class OutOfEnergy
        {
            private static bool Prefix(Player __instance)
            {
                if (MenuManager.opt_primary_autoswitch != 0 || !primarySwapFlag) return true;
                if (__instance != GameManager.m_local_player) return true;
                return !maybeSwapPrimary();
            }
        }

        [HarmonyPatch(typeof(Player), "MaybeSwitchToNextMissile")]
        internal class NextLastMissileBasedOnPriority
        {
            public static bool Prefix(Player __instance)
            {
                if (!secondarySwapFlag || __instance != GameManager.m_local_player) return true;
                if (!__instance.CanFireMissileAmmo(MissileType.NUM))
                {
                    pendingMissileAutoSwapDelay = 15;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(GameManager), "Update")]
        internal class ProcessDelayedSwap
        {
            public static void Postfix()
            {
                if (GameplayManager.IsMultiplayerActive && NetworkMatch.InGameplay()
                    && !dontAutoselectAfterFiring && !Controls.IsPressed(CCInput.FIRE_WEAPON)
                    && pendingWeaponSwap != WeaponType.NUM)
                {
                    if (GameManager.m_local_player.m_weapon_type == WeaponType.THUNDERBOLT)
                    {
                        if (thunderboltSwapDelay > 0f)
                        {
                            thunderboltSwapDelay -= Time.deltaTime;
                            return;
                        }
                        thunderboltSwapDelay = 0.025f;
                    }
                    swapToWeapon(pendingWeaponSwap);
                    GameManager.m_local_player.UpdateCurrentWeaponName();
                    pendingWeaponSwap = WeaponType.NUM;
                }

                if (pendingMissileSwap != MissileType.NUM && missileSwapDelay == 0)
                {
                    swapToMissile((int)pendingMissileSwap);
                    pendingMissileSwap = MissileType.NUM;
                }
                else if (missileSwapDelay > 0) missileSwapDelay--;

                if (pendingMissileAutoSwapDelay > 0 && --pendingMissileAutoSwapDelay == 0)
                {
                    int idx = findHighestPrioritizedUseableMissile();
                    if (idx >= 0)
                    {
                        var p = GameManager.m_local_player;
                        if (p.m_missile_type != (MissileType)idx
                            && (p.NumUnlockedMissilesWithAmmo() > 1
                                || (p.NumUnlockedMissilesWithAmmo() == 1 && p.m_missile_ammo[(int)p.m_missile_type] == 0)))
                        {
                            p.Networkm_missile_type = (MissileType)idx;
                            p.CallCmdSetCurrentMissile(p.m_missile_type);
                            p.UpdateCurrentMissileName();
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(MenuManager), "LoadPreferences")]
        class MPAutoSelection_MenuManager_LoadPreferences
        {
            public static void Postfix()
            {
                MenuManager.opt_primary_autoswitch = 0;
            }
        }

        [HarmonyPatch(typeof(Player), "AddMissileAmmo")]
        class MPAutoSelection_Player_AddMissileAmmo
        {
            public static void Postfix(MissileType mt, Player __instance)
            {
                if (GameplayManager.IsMultiplayerActive || !secondarySwapFlag) return;
                if (__instance != GameManager.m_local_player) return;

                int newPriority = getMissilePriority(mt);
                int curPriority = getMissilePriority(GameManager.m_local_player.m_missile_type);
                if (newPriority >= curPriority || SecondaryNeverSelect[newPriority]) return;

                pendingMissileSwap = mt;
                missileSwapDelay = 1;

                if (mt == MissileType.DEVASTATOR && zorc)
                {
                    SFXCueManager.PlayCue2D(SFXCue.enemy_boss1_alert, 1f, 0f, 0f, false);
                    GameplayManager.AlertPopup(Loc.LS("DEVASTATOR SELECTED"), string.Empty, 5f);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Client), "OnRespawnMsg")]
    class MPAutoSelection_Client_OnRespawnMessage
    {
        public static void Postfix(NetworkMessage msg)
        {
            msg.reader.SeekZero();
            RespawnMessage respawnMessage = msg.ReadMessage<RespawnMessage>();

            GameObject gameObject = ClientScene.FindLocalObject(respawnMessage.m_net_id);
            if (gameObject == null) return;

            Player player = gameObject.GetComponent<Player>();
            if (player == null || !player.isLocalPlayer) return;

            if (!GameplayManager.IsMultiplayerActive || !NetworkMatch.InGameplay()) return;

            if (MenuManager.opt_primary_autoswitch == 0 && MPAutoSelection.primarySwapFlag)
                MPAutoSelection.maybeSwapPrimary(true);

            if (MPAutoSelection.secondarySwapFlag)
                MPAutoSelection.maybeSwapMissiles(true);
        }
    }
}
