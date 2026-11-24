using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Overload;

namespace GameMod.Bots
{
    class BotSlot
    {
        public string Name;
        public int Tier = 2;
        public int Team = BotSettings.AUTO_TEAM;
        public WeaponType Primary1 = WeaponType.DRILLER;
        public WeaponType Primary2 = WeaponType.CYCLONE;
        public MissileType Secondary1 = MissileType.HUNTER;
        public MissileType Secondary2 = MissileType.FALCON;

        public string PilotName
        {
            get { return BotSettings.TierLabel(Tier) + " " + Name; }
        }

        public string TierFile
        {
            get { return BotSettings.TIER_FILES[Tier]; }
        }

        public BotSlot Clone()
        {
            return (BotSlot)MemberwiseClone();
        }

        public BotSlot Resolve()
        {
            BotSlot slot = Clone();
            slot.Tier = Pick(Tier, Enumerable.Range(0, BotSettings.TIER_FILES.Length).ToList(), BotSettings.RANDOM);
            slot.Team = Pick(Team, Enumerable.Range(0, MPTeams.NetworkMatchTeamCount).ToList(), BotSettings.RANDOM);
            List<int> primaries = BotSettings.RandomPrimaries();
            List<int> secondaries = BotSettings.RandomSecondaries();
            slot.Primary1 = (WeaponType)Pick((int)Primary1, primaries, (int)Primary2);
            slot.Primary2 = (WeaponType)Pick((int)Primary2, primaries, (int)slot.Primary1);
            slot.Secondary1 = (MissileType)Pick((int)Secondary1, secondaries, (int)Secondary2);
            slot.Secondary2 = (MissileType)Pick((int)Secondary2, secondaries, (int)slot.Secondary1);
            if (NetworkMatch.m_force_loadout == 1)
                slot.ApplyForcedLoadout();
            return slot;
        }

        void ApplyForcedLoadout()
        {
            Primary1 = NetworkMatch.m_force_w1;
            Primary2 = NetworkMatch.m_force_w2;
            if (NetworkMatch.m_force_m1 != MissileType.NUM)
            {
                Secondary1 = NetworkMatch.m_force_m1;
                Secondary2 = NetworkMatch.m_force_m2;
            }
            else if (NetworkMatch.m_force_m2 != MissileType.NUM)
            {
                Secondary1 = NetworkMatch.m_force_m2;
                Secondary2 = MissileType.NUM;
            }
        }

        static int Pick(int value, List<int> pool, int taken)
        {
            if (value != BotSettings.RANDOM || pool.Count == 0)
                return value;
            List<int> free = pool.Where(choice => choice != taken).ToList();
            if (free.Count == 0)
                free = pool;
            return free[UnityEngine.Random.Range(0, free.Count)];
        }

        public List<WeaponType> Primaries
        {
            get { return Primary2 == WeaponType.NUM || Primary2 == Primary1 ? new List<WeaponType> { Primary1 } : new List<WeaponType> { Primary1, Primary2 }; }
        }

        public List<MissileType> Secondaries
        {
            get { return Secondary2 == MissileType.NUM || Secondary2 == Secondary1 ? new List<MissileType> { Secondary1 } : new List<MissileType> { Secondary1, Secondary2 }; }
        }

        public string LoadoutSpec
        {
            get { return string.Join("+", Primaries.ConvertAll(w => w.ToString()).ToArray()) + "/" + string.Join("+", Secondaries.ConvertAll(m => m.ToString()).ToArray()); }
        }
    }

    class BotPreset
    {
        public string Name;
        public BotSettings Settings;
    }

    class BotSettings
    {
        public const int MAX_BOTS = 15;
        public const int AUTO_TEAM = -1;
        public const int RANDOM = -2;
        public const int MAX_NAME_LENGTH = 17 - 8;
        public const int MAX_PRESET_NAME_LENGTH = 16;

        public static readonly string[] TIER_FILES = { "trainee", "rookie", "hotshot", "ace", "insane", "brutal", "merciless", "nightmare" };
        public static readonly string[] TIER_NAMES = { "TRAINEE", "ROOKIE", "HOTSHOT", "ACE", "INSANE", "BRUTAL", "MERCILESS", "NIGHTMARE" };

        public List<BotSlot> Slots = new List<BotSlot>();
        public bool Enabled = true;
        public bool ReplaceWithHumans;

        public static List<int> TierOptions()
        {
            return Options(TIER_FILES.Length, RANDOM);
        }

        public static List<int> TeamOptions(int teamCount)
        {
            List<int> options = Options(teamCount, RANDOM);
            options.Insert(0, AUTO_TEAM);
            return options;
        }

        public static List<int> LoadoutOptions(int count, bool isSecondSlot)
        {
            return isSecondSlot ? Options(count, count, RANDOM) : Options(count, RANDOM);
        }

        public static List<int> RandomPrimaries()
        {
            List<int> choosable = Enumerable.Range(0, (int)MPLoadouts.DefaultWeaponEnd).Where(w => w != (int)WeaponType.REFLEX).ToList();
            List<int> allowed = choosable.Where(w => MPLoadouts.IsAllowed((WeaponType)w)).ToList();
            return allowed.Count > 0 ? allowed : choosable;
        }

        public static List<int> RandomSecondaries()
        {
            List<int> choosable = Enumerable.Range(0, (int)MPLoadouts.DefaultMissileEnd).ToList();
            List<int> allowed = choosable.Where(m => MPLoadouts.IsAllowed((MissileType)m)).ToList();
            return allowed.Count > 0 ? allowed : choosable;
        }

        static List<int> Options(int count, params int[] extra)
        {
            List<int> options = Enumerable.Range(0, count).ToList();
            options.AddRange(extra);
            return options;
        }

        public static string TierName(int tier)
        {
            return tier == RANDOM ? "RANDOM" : TIER_NAMES[tier];
        }

        public static string TierTag(int tier)
        {
            return tier == RANDOM ? "?" : tier.ToString();
        }

        public static string TierLabel(int tier)
        {
            return "[" + TierTag(tier) + "] BOT";
        }

        public static int TierFromText(string text)
        {
            text = text.Trim().ToUpperInvariant();
            if (text.StartsWith("BOT"))
                text = text.Substring(3);
            text = text.Trim('[', ']');
            if (text == TierTag(RANDOM) || text == TierName(RANDOM))
                return RANDOM;
            for (int tier = 0; tier < TIER_FILES.Length; tier++)
                if (text == TierTag(tier) || text == TIER_NAMES[tier])
                    return tier;
            return -1;
        }

        public JObject ToJson()
        {
            var bots = new JArray();
            foreach (BotSlot slot in Slots)
                bots.Add(new JObject {
                    { "name", slot.Name },
                    { "tier", slot.Tier },
                    { "team", slot.Team },
                    { "w1", (int)slot.Primary1 },
                    { "w2", (int)slot.Primary2 },
                    { "m1", (int)slot.Secondary1 },
                    { "m2", (int)slot.Secondary2 },
                });
            return new JObject { { "bots", bots }, { "enabled", Enabled }, { "replace", ReplaceWithHumans } };
        }

        public static BotSettings FromJson(JToken root)
        {
            var settings = new BotSettings();
            var bots = root == null ? null : root["bots"] as JArray;
            if (bots == null)
                return settings;
            settings.Enabled = root["enabled"].GetBool(true);
            settings.ReplaceWithHumans = root["replace"].GetBool(false);
            foreach (JToken bot in bots)
            {
                string name = CleanName(bot["name"].GetString(string.Empty)).Trim();
                if (name.Length == 0 || settings.Slots.Count == MAX_BOTS
                    || settings.Slots.Exists(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                settings.Slots.Add(new BotSlot {
                    Name = name,
                    Tier = TierOrStrongest(bot["tier"].GetInt(2)),
                    Team = OneOf(bot["team"].GetInt(AUTO_TEAM), TeamOptions(MPTeams.Max), AUTO_TEAM),
                    Primary1 = (WeaponType)OneOf(bot["w1"].GetInt(0), LoadoutOptions((int)WeaponType.NUM, false), 0),
                    Primary2 = (WeaponType)OneOf(bot["w2"].GetInt((int)WeaponType.NUM), LoadoutOptions((int)WeaponType.NUM, true), (int)WeaponType.NUM),
                    Secondary1 = (MissileType)OneOf(bot["m1"].GetInt(0), LoadoutOptions((int)MissileType.NUM, false), 0),
                    Secondary2 = (MissileType)OneOf(bot["m2"].GetInt((int)MissileType.NUM), LoadoutOptions((int)MissileType.NUM, true), (int)MissileType.NUM),
                });
            }
            return settings;
        }

        public BotSettings Copy()
        {
            return FromJson(ToJson());
        }

        public string ToPrefsValue()
        {
            return Encode(ToJson());
        }

        public static BotSettings FromPrefsValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new BotSettings();
            return FromJson(Decode(value));
        }

        public static string PresetsToPrefsValue(List<BotPreset> presets)
        {
            var array = new JArray();
            foreach (BotPreset preset in presets)
                array.Add(new JObject { { "name", preset.Name }, { "settings", preset.Settings.ToJson() } });
            return Encode(array);
        }

        public static List<BotPreset> PresetsFromPrefsValue(string value)
        {
            var presets = new List<BotPreset>();
            var array = string.IsNullOrEmpty(value) ? null : Decode(value) as JArray;
            if (array == null)
                return presets;
            foreach (JToken preset in array)
            {
                string name = CleanPresetName(preset["name"].GetString(string.Empty));
                presets.Add(new BotPreset { Name = name.Length > 0 ? name : "PRESET", Settings = FromJson(preset["settings"]) });
            }
            return presets;
        }

        static string Encode(JToken token)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(token.ToString(Newtonsoft.Json.Formatting.None)));
        }

        static JToken Decode(string value)
        {
            return JToken.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(value)));
        }

        static readonly string[][] BASE_NAME_SETS = {
            new[] { "DIABLO", "MEPHISTO", "BAAL", "ANDARIEL", "DURIEL", "AZMODAN", "AKARA", "LILITH",
                "INARIUS", "TYRAEL", "IZUAL", "MALTHAEL", "LEORIC", "AMPLISA", "DECKARD" },
            new[] { "BANELING", "HYDRALISK", "ULTRALISK", "MUTALISK", "ROACH", "INFESTOR", "LURKER", "QUEEN",
                "VIPER", "CORRUPTOR", "BROODLORD", "GHOST", "STALKER", "ARCHON", "COLOSSUS" },
        };

        static BotPreset BasePreset(string name, params int[] tiers)
        {
            var settings = new BotSettings();
            string[] nameSet = BASE_NAME_SETS[UnityEngine.Random.Range(0, BASE_NAME_SETS.Length)];
            List<string> names = nameSet.OrderBy(n => UnityEngine.Random.value).ToList();
            for (int i = 0; i < tiers.Length; i++)
                settings.Slots.Add(new BotSlot {
                    Name = names[i],
                    Tier = tiers[i],
                    Primary1 = (WeaponType)RANDOM,
                    Primary2 = (WeaponType)RANDOM,
                    Secondary1 = (MissileType)RANDOM,
                    Secondary2 = (MissileType)RANDOM,
                });
            return new BotPreset { Name = name, Settings = settings };
        }

        public static void AddBasePresets(List<BotPreset> presets)
        {
            var basePresets = new List<BotPreset> {
                BasePreset("EASY", 0, 1, 1, 2),
                BasePreset("MEDIUM", 2, 3, 3, 4),
                BasePreset("HARD", 4, 5, 5, 6),
                BasePreset("NIGHTMARE", 6, 6, 7, 7),
                BasePreset("MIXED", 0, 1, 2, 3, 4, 5, 6, 7),
            };
            foreach (BotPreset preset in basePresets)
                if (!presets.Exists(p => p.Name == preset.Name))
                    presets.Add(preset);
            presets.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        }

        public static string CleanPresetName(string name)
        {
            name = name.Trim().ToUpperInvariant();
            return name.Length > MAX_PRESET_NAME_LENGTH ? name.Substring(0, MAX_PRESET_NAME_LENGTH) : name;
        }

        public static string CleanName(string name)
        {
            foreach (char c in "\"\\,:")
                name = name.Replace(c.ToString(), string.Empty);
            name = name.ToUpperInvariant();
            return name.Length > MAX_NAME_LENGTH ? name.Substring(0, MAX_NAME_LENGTH) : name;
        }

        public static string AddBotCommand(BotSlot slot)
        {
            string loadout = WeaponText((int)slot.Primary1) + "+" + WeaponText((int)slot.Primary2) + "/" + MissileText((int)slot.Secondary1) + "+" + MissileText((int)slot.Secondary2);
            return "/ADDBOT " + TierName(slot.Tier) + " " + slot.Name.Trim() + ":" + loadout + ":" + TeamText(slot.Team);
        }

        public static bool ReadLoadout(string text, BotSlot slot)
        {
            string[] groups = text.Split('/');
            if (groups.Length != 2)
                return false;
            string[] primaries = groups[0].Split('+');
            string[] secondaries = groups[1].Split('+');
            if (primaries.Length != 2 || secondaries.Length != 2)
                return false;
            int primary1, primary2, secondary1, secondary2;
            if (!TryChoice(primaries[0], LoadoutOptions((int)WeaponType.NUM, false), WeaponText, out primary1)
                || !TryChoice(primaries[1], LoadoutOptions((int)WeaponType.NUM, true), WeaponText, out primary2)
                || !TryChoice(secondaries[0], LoadoutOptions((int)MissileType.NUM, false), MissileText, out secondary1)
                || !TryChoice(secondaries[1], LoadoutOptions((int)MissileType.NUM, true), MissileText, out secondary2))
                return false;
            slot.Primary1 = (WeaponType)primary1;
            slot.Primary2 = (WeaponType)primary2;
            slot.Secondary1 = (MissileType)secondary1;
            slot.Secondary2 = (MissileType)secondary2;
            return true;
        }

        public static bool ReadTeam(string text, int teamCount, BotSlot slot)
        {
            int team;
            if (!TryChoice(text, TeamOptions(teamCount), TeamText, out team))
                return false;
            slot.Team = team;
            return true;
        }

        static bool TryChoice(string text, List<int> options, Func<int, string> name, out int choice)
        {
            choice = options[0];
            foreach (int option in options)
                if (string.Equals(name(option), text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    choice = option;
                    return true;
                }
            return false;
        }

        static string WeaponText(int weapon)
        {
            return weapon == RANDOM ? "RANDOM" : weapon == (int)WeaponType.NUM ? "NONE" : ((WeaponType)weapon).ToString();
        }

        static string MissileText(int missile)
        {
            return missile == RANDOM ? "RANDOM" : missile == (int)MissileType.NUM ? "NONE" : ((MissileType)missile).ToString();
        }

        static string TeamText(int team)
        {
            return team == AUTO_TEAM ? "AUTO" : team == RANDOM ? "RANDOM" : (team + 1).ToString();
        }

        static int TierOrStrongest(int tier)
        {
            return tier >= TIER_FILES.Length ? TIER_FILES.Length - 1 : OneOf(tier, TierOptions(), 2);
        }

        static int OneOf(int value, List<int> options, int fallback)
        {
            return options.Contains(value) ? value : fallback;
        }
    }
}
