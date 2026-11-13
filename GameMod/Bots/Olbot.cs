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
            var startInfo = new ProcessStartInfo(PlayerPath, string.Join(" ", arguments.ConvertAll(Quote).ToArray()))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = PlayerFolder
            };
            foreach (string variable in new[] { "LD_PRELOAD", "DYLD_INSERT_LIBRARIES", "OLMODDIR" })
                startInfo.EnvironmentVariables.Remove(variable);
            return Process.Start(startInfo);
        }

        public static string Quote(string argument)
        {
            var quoted = new System.Text.StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
                backslashes = 0;
                quoted.Append(c);
            }
            quoted.Append('\\', backslashes * 2);
            return quoted.Append('"').ToString();
        }
    }
}
