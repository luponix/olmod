using Version = System.Version;

namespace GameMod.VersionHandling {

    public static class OlmodVersion
    {
        public static Version RunningVersion { get; set; }

        public static bool Modded { get; set; }

        private static string _fullVersionString;

        public static string FullVersionString
        {
            get
            {
                if (_fullVersionString == null)
                {
                    Setup();

                    // do not include revision unless explicitly set to non-zero in the assembly version
                    string maybeRevision = RunningVersion.Revision > 0 ? $".{RunningVersion.Revision}" : "";
                    _fullVersionString = $"Pyro {RunningVersion.ToString(3)}{maybeRevision}{(Modded ? " **MODDED**" : "")}";
                }
                return _fullVersionString;
            }
        }

        public static void Setup()
        {
            RunningVersion = typeof(OlmodVersion).Assembly.GetName().Version;
        }
    }
}
