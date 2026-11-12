using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace GameMod.Bots
{
    static class Olbot
    {
        static string PlayerFolder
        {
            get { return Path.Combine(Path.Combine(Config.OLModDir, "olbot"), Application.platform == RuntimePlatform.WindowsPlayer ? "win64" : "linux64"); }
        }

        public static string PlayerPath
        {
            get { return Path.Combine(PlayerFolder, Application.platform == RuntimePlatform.WindowsPlayer ? "olbot.exe" : "olbot.x86_64"); }
        }

        public static string ShippedGeometryDir
        {
            get { return Path.Combine(PlayerFolder, "geometry"); }
        }

        public static Process Start(List<string> arguments)
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer)
                VersionHandling.SelfUpdate.MakeExecutable(PlayerPath);
            var startInfo = new ProcessStartInfo(PlayerPath, "\"" + string.Join("\" \"", arguments.ToArray()) + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = PlayerFolder
            };
            foreach (string variable in new[] { "LD_PRELOAD", "DYLD_INSERT_LIBRARIES", "OLMODDIR" })
                startInfo.EnvironmentVariables.Remove(variable);
            Process process = Process.Start(startInfo);
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
            return process;
        }
    }
}
