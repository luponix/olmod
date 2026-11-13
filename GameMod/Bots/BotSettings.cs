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
            get { return BotSettings.NAME_PREFIX + Name; }
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
            slot.Tier = Pick(Tier, BotSettings.TIER_FILES.Length, BotSettings.RANDOM);
            slot.Team = Pick(Team, MPTeams.NetworkMatchTeamCount, BotSettings.RANDOM);
            slot.Primary1 = (WeaponType)Pick((int)Primary1, (int)WeaponType.NUM, (int)Primary2);
            slot.Primary2 = (WeaponType)Pick((int)Primary2, (int)WeaponType.NUM, (int)slot.Primary1);
            slot.Secondary1 = (MissileType)Pick((int)Secondary1, (int)MissileType.NUM, (int)Secondary2);
            slot.Secondary2 = (MissileType)Pick((int)Secondary2, (int)MissileType.NUM, (int)slot.Secondary1);
            return slot;
        }

        static int Pick(int value, int count, int taken)
        {
            if (value != BotSettings.RANDOM || count == 0)
                return value;
            int pick;
            do
                pick = UnityEngine.Random.Range(0, count);
            while (pick == taken && count > 1);
            return pick;
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

    class BotSettings
    {
        public const int MAX_BOTS = 15;
        public const int AUTO_TEAM = -1;
        public const int RANDOM = -2;
        public const string NAME_PREFIX = "BOT ";
        public const int MAX_NAME_LENGTH = 16 - 4;

        public static readonly string[] TIER_FILES = { "trainee", "rookie", "hotshot", "ace", "insane", "insaneplus" };
        public static readonly string[] TIER_NAMES = { "TRAINEE", "ROOKIE", "HOTSHOT", "ACE", "INSANE", "INSANE+" };

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
                if (name.Length == 0 || settings.Slots.Count == MAX_BOTS)
                    continue;
                settings.Slots.Add(new BotSlot {
                    Name = name,
                    Tier = OneOf(bot["tier"].GetInt(2), TierOptions(), 2),
                    Team = OneOf(bot["team"].GetInt(AUTO_TEAM), TeamOptions(MPTeams.Max), AUTO_TEAM),
                    Primary1 = (WeaponType)OneOf(bot["w1"].GetInt(0), LoadoutOptions((int)WeaponType.NUM, false), 0),
                    Primary2 = (WeaponType)OneOf(bot["w2"].GetInt((int)WeaponType.NUM), LoadoutOptions((int)WeaponType.NUM, true), (int)WeaponType.NUM),
                    Secondary1 = (MissileType)OneOf(bot["m1"].GetInt(0), LoadoutOptions((int)MissileType.NUM, false), 0),
                    Secondary2 = (MissileType)OneOf(bot["m2"].GetInt((int)MissileType.NUM), LoadoutOptions((int)MissileType.NUM, true), (int)MissileType.NUM),
                });
            }
            return settings;
        }

        public string ToPrefsValue()
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(ToJson().ToString(Newtonsoft.Json.Formatting.None)));
        }

        public static BotSettings FromPrefsValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new BotSettings();
            return FromJson(JToken.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(value))));
        }

        public static string CleanName(string name)
        {
            foreach (char c in "\"\\,:")
                name = name.Replace(c.ToString(), string.Empty);
            return name.Length > MAX_NAME_LENGTH ? name.Substring(0, MAX_NAME_LENGTH) : name;
        }

        static int OneOf(int value, List<int> options, int fallback)
        {
            return options.Contains(value) ? value : fallback;
        }
    }
}
