using System.Reflection;
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
                    string shownVersion = RunningVersion.ToString(3) + maybeRevision;
                    // a release candidate names the coming version here, its assembly version stays below it so the updater still offers the release
                    var informational = (AssemblyInformationalVersionAttribute[])typeof(OlmodVersion).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
                    if (informational.Length > 0 && !string.IsNullOrEmpty(informational[0].InformationalVersion))
                        shownVersion = informational[0].InformationalVersion;
                    _fullVersionString = $"Pyro {shownVersion}{(Modded ? " **MODDED**" : "")}";
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
