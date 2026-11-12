using System;
using System.Collections.Generic;
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
        public const int MAX_BOTS = 8;
        public const int AUTO_TEAM = -1;
        public const string NAME_PREFIX = "BOT ";
        public const int MAX_NAME_LENGTH = 16 - 4;

        public static readonly string[] TIER_FILES = { "trainee", "rookie", "hotshot", "ace", "insane", "insaneplus" };
        public static readonly string[] TIER_NAMES = { "TRAINEE", "ROOKIE", "HOTSHOT", "ACE", "INSANE", "INSANE+" };

        public List<BotSlot> Slots = new List<BotSlot>();
        public bool ReplaceWithHumans;

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
            return new JObject { { "bots", bots }, { "replace", ReplaceWithHumans } };
        }

        public static BotSettings FromJson(JToken root)
        {
            var settings = new BotSettings();
            var bots = root == null ? null : root["bots"] as JArray;
            if (bots == null)
                return settings;
            settings.ReplaceWithHumans = root["replace"].GetBool(false);
            foreach (JToken bot in bots)
            {
                string name = CleanName(bot["name"].GetString(string.Empty)).Trim();
                if (name.Length == 0 || settings.Slots.Count == MAX_BOTS)
                    continue;
                settings.Slots.Add(new BotSlot {
                    Name = name,
                    Tier = Clamp(bot["tier"].GetInt(2), 0, TIER_FILES.Length - 1),
                    Team = Clamp(bot["team"].GetInt(AUTO_TEAM), AUTO_TEAM, MPTeams.Max - 1),
                    Primary1 = (WeaponType)Clamp(bot["w1"].GetInt(0), 0, (int)WeaponType.NUM - 1),
                    Primary2 = (WeaponType)Clamp(bot["w2"].GetInt((int)WeaponType.NUM), 0, (int)WeaponType.NUM),
                    Secondary1 = (MissileType)Clamp(bot["m1"].GetInt(0), 0, (int)MissileType.NUM - 1),
                    Secondary2 = (MissileType)Clamp(bot["m2"].GetInt((int)MissileType.NUM), 0, (int)MissileType.NUM),
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

        static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
