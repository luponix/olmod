using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Timers;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace GameMod
{
    class AutoSelect
    {
        public class Missile
        {
            public MissileType missile_type;
            public bool neverselect;
            public bool excluded_from_weapon_cycling;
        }

        public class Weapon
        {
            public WeaponType weapon_type;
            public RessourceType required_ressource;
            public bool neverselect;
            public bool excluded_from_weapon_cycling;
        }

        public enum RessourceType{
            ENERGY,
            AMMO,
            NUM
        }


        public static Weapon[] Weapons;
        public static Missile[] Missiles;

        public static bool initialised_preferences = false;
        public static bool primary_logic = false;
        public static bool secondary_logic = false;
        public static bool zorc;                        // extra alert for old men when the devastator gets autoselected, still need to find an annoying sound for that
        public static bool miasmic;                     // reduced hud
        public static bool swap_while_firing;
        public static bool dont_autoselect_after_firing;

        public static void InitialiseSettings(string[] primary_weapons, string[] secondary_weapons, bool[] primary_neverselect, bool[] secondary_neverselect, bool pl, bool sl, bool dev_alert, bool reduced_hud, bool swf, bool daaf)
        {
            // Check if one of the loaded settings is incomplete and restore the default values in that case
            if((primary_weapons == null || primary_weapons.Length < 8) 
                | (secondary_weapons == null || secondary_weapons.Length < 8)
                | (primary_neverselect == null || primary_neverselect.Length < 8)
                | (secondary_neverselect == null || secondary_neverselect.Length < 8))
            {
                Debug.Log("ERROR: [AutoSelect.InitialiseSettings()]: loaded settings were incomplete    (0)");
                SetDefaultSettings();
            }


            // Creates the primary weapon type structure
            // excluded_from_weapon_cycling gets treated differently because we may have to carry over the value of the current structure
            // depending on wether the weapon exclusion section got loaded before the AutoSelect Section or not
            Weapon[] tmp_weapons = new Weapon[8];
            for(int i = 0; i < tmp_weapons.Length; i++)
            {
                WeaponType weapon_type = GetWeaponTypeForString(primary_weapons[i]);
                if(weapon_type == WeaponType.NUM)
                {
                    SetDefaultSettings();
                    break;
                }

                tmp_weapons[i] = new Weapon { 
                    weapon_type = weapon_type, 
                    required_ressource = GetRessourceTypeForWeaponType(weapon_type), 
                    neverselect = primary_neverselect[i], 
                    excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false 
                };
            }


            // Creates the secondary weapon type structure
            Missile[] tmp_missiles = new Missile[8];
            for (int i = 0; i < tmp_missiles.Length; i++)
            {
                MissileType missile_type = GetMissileTypeForString(secondary_weapons[i]);
                if (missile_type == MissileType.NUM)
                {
                    SetDefaultSettings();
                    break;
                }

                tmp_missiles[i] = new Missile
                {
                    missile_type = missile_type,
                    neverselect = secondary_neverselect[i],
                    excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false
                };
            }

            // Check wether each weapon/missile only appears once in the arrays
            if (tmp_weapons.Length != tmp_weapons.Distinct().Count() | tmp_missiles.Length != tmp_missiles.Distinct().Count())
            {
                Debug.Log("ERROR: [AutoSelect.InitialiseSettings()]: loaded settings contained duplicates   (1)");
                SetDefaultSettings();
            }


            Weapons = tmp_weapons;
            Missiles = tmp_missiles;
            primary_logic = pl;
            secondary_logic = sl;
            zorc = dev_alert;
            miasmic = reduced_hud;
            swap_while_firing = swf;
            dont_autoselect_after_firing = daaf;

            initialised_preferences = true;
        }

        public static WeaponType GetWeaponTypeForString(string weapon_name)
        {
            if (string.IsNullOrEmpty(weapon_name))
                return WeaponType.NUM;

            weapon_name = weapon_name.ToUpper();
            switch (weapon_name)
            {
                case "THUNDERBOLT":
                    return WeaponType.THUNDERBOLT;
                case "IMPULSE":
                    return WeaponType.IMPULSE;
                case "CYCLONE":
                    return WeaponType.CYCLONE;
                case "REFLEX":
                    return WeaponType.REFLEX;
                case "LANCER":
                    return WeaponType.LANCER;
                case "CRUSHER":
                    return WeaponType.CRUSHER;
                case "DRILLER":
                    return WeaponType.DRILLER;
                case "FLAK":
                    return WeaponType.FLAK;
                default:
                    return WeaponType.NUM;
            }
        }

        public static MissileType GetMissileTypeForString(string missile_name)
        {
            if (string.IsNullOrEmpty(missile_name))
                return MissileType.NUM;

            missile_name = missile_name.ToUpper();
            switch (missile_name)
            {
                case "DEVASTATOR":
                    return MissileType.DEVASTATOR;
                case "CREEPER":
                    return MissileType.CREEPER;
                case "FALCON":
                    return MissileType.FALCON;
                case "HUNTER":
                    return MissileType.HUNTER;
                case "MISSILE_POD":
                    return MissileType.MISSILE_POD;
                case "NOVA":
                    return MissileType.NOVA;
                case "TIMEBOMB":
                    return MissileType.TIMEBOMB;
                case "VORTEX":
                    return MissileType.VORTEX;
                default:
                    return MissileType.NUM;
            }
        }

        public static RessourceType GetRessourceTypeForWeaponType(WeaponType wt)
        {
            switch(wt)
            {
                case WeaponType.THUNDERBOLT:
                case WeaponType.IMPULSE:
                case WeaponType.CYCLONE:
                case WeaponType.REFLEX:
                case WeaponType.LANCER:
                    return RessourceType.ENERGY;
                case WeaponType.CRUSHER:
                case WeaponType.DRILLER:
                case WeaponType.FLAK:
                    return RessourceType.AMMO;
                default:
                    return RessourceType.NUM;
            }
        }

        public static void SetDefaultSettings()
        {
            Weapons = new Weapon[8];
            Weapons[0] = new Weapon { weapon_type = WeaponType.THUNDERBOLT, required_ressource = RessourceType.ENERGY,  neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Weapons[1] = new Weapon { weapon_type = WeaponType.IMPULSE,     required_ressource = RessourceType.ENERGY,  neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Weapons[2] = new Weapon { weapon_type = WeaponType.CRUSHER,     required_ressource = RessourceType.AMMO,    neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Weapons[3] = new Weapon { weapon_type = WeaponType.CYCLONE,     required_ressource = RessourceType.ENERGY,  neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Weapons[4] = new Weapon { weapon_type = WeaponType.LANCER,      required_ressource = RessourceType.ENERGY,  neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Weapons[5] = new Weapon { weapon_type = WeaponType.DRILLER,     required_ressource = RessourceType.AMMO,    neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Weapons[6] = new Weapon { weapon_type = WeaponType.FLAK,        required_ressource = RessourceType.AMMO,    neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Weapons[7] = new Weapon { weapon_type = WeaponType.REFLEX,      required_ressource = RessourceType.ENERGY,  neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };

            Missiles = new Missile[8];
            Missiles[0] = new Missile { missile_type = MissileType.DEVASTATOR,  neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Missiles[1] = new Missile { missile_type = MissileType.TIMEBOMB,    neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Missiles[2] = new Missile { missile_type = MissileType.NOVA,        neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Missiles[3] = new Missile { missile_type = MissileType.VORTEX,      neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Missiles[4] = new Missile { missile_type = MissileType.HUNTER,      neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Missiles[5] = new Missile { missile_type = MissileType.MISSILE_POD, neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Missiles[6] = new Missile { missile_type = MissileType.CREEPER,     neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };
            Missiles[7] = new Missile { missile_type = MissileType.FALCON,      neverselect = false, excluded_from_weapon_cycling = initialised_preferences ? Weapons[i].excluded_from_weapon_cycling : false };

            primary_logic = false;
            secondary_logic = false;
            zorc = true;
            miasmic = false;
            swap_while_firing = false;
            dont_autoselect_after_firing = false;

            initialised_preferences = true;
            Debug.Log("[AutoSelect.SetDefaultSettings()]: Reset the AutoSelect configuration to default values!");
        }



















        [HarmonyPatch(typeof(GameManager), "Start")]
        internal class CommandsAndInitialisationPatch
        {
            private static void Postfix(GameManager __instance)
            {
                uConsole.RegisterCommand("toggleprimaryorder", "toggles all Weapon Selection logic related to primary weapons", new uConsole.DebugCommand(CommandsAndInitialisationPatch.CmdTogglePrimary));
                uConsole.RegisterCommand("togglesecondaryorder", "toggles all Weapon Selection logic related to secondary weapons", new uConsole.DebugCommand(CommandsAndInitialisationPatch.CmdToggleSecondary));
                uConsole.RegisterCommand("toggle_hud", "Toggles some HUD elements", new uConsole.DebugCommand(CommandsAndInitialisationPatch.CmdToggleHud));
                Initialise();
            }

            // COMMANDS
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
            public static bool Prefix(UIElement __instance)
            {
                return !miasmic;
            }
        }

        [HarmonyPatch(typeof(UIElement), "DrawHUDEnergyAmmo")]
        internal class MaybeDrawHUDElement2
        {
            public static bool Prefix()
            {
                return !miasmic;
            }
        }

        [HarmonyPatch(typeof(UIElement), "DrawHUDIndicators")]
        internal class MaybeDrawHUDElement3
        {
            public static bool Prefix(UIElement __instance)
            {
                return !miasmic;
            }
        }
    }
}
