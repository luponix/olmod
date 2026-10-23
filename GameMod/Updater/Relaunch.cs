using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace GameMod.Updater
{
    static class Relaunch
    {
        public static string LastError { get; private set; }

        public static bool Now()
        {
            string[] commandLine = Environment.GetCommandLineArgs();
            if (commandLine.Length == 0)
            {
                LastError = "the command line is empty";
                UnityEngine.Debug.Log("Relaunch: " + LastError);
                return false;
            }

            StringBuilder arguments = new StringBuilder();
            for (int i = 1; i < commandLine.Length; i++)
            {
                if (arguments.Length > 0)
                    arguments.Append(' ');
                arguments.Append(Quote(commandLine[i]));
            }

            ProcessStartInfo start = new ProcessStartInfo(commandLine[0], arguments.ToString());
            start.UseShellExecute = false;
            start.WorkingDirectory = Environment.CurrentDirectory;

            UnityEngine.Debug.Log("Relaunch: starting " + start.FileName + " " + start.Arguments);
            try
            {
                if (Process.Start(start) == null)
                {
                    LastError = "the new process did not start";
                    UnityEngine.Debug.Log("Relaunch: " + LastError);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                UnityEngine.Debug.Log("Relaunch: " + ex);
                return false;
            }

            Application.Quit();
            return true;
        }

        internal static string Quote(string argument)
        {
            return Application.platform == RuntimePlatform.WindowsPlayer ||
                   Application.platform == RuntimePlatform.WindowsEditor
                ? QuoteForWindows(argument)
                : QuoteForShell(argument);
        }

        private static string QuoteForShell(string argument)
        {
            if (argument.Length > 0 && IsPlainShellWord(argument))
                return argument;
            return "'" + argument.Replace("'", "'\\''") + "'";
        }

        private static bool IsPlainShellWord(string argument)
        {
            foreach (char character in argument)
                if (!char.IsLetterOrDigit(character) && "@%_+=:,./-".IndexOf(character) < 0)
                    return false;
            return true;
        }

        private static string QuoteForWindows(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
                return argument;

            StringBuilder quoted = new StringBuilder("\"");
            for (int i = 0; i < argument.Length; i++)
            {
                int backslashes = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashes++;
                    i++;
                }
                if (i == argument.Length)
                {
                    quoted.Append('\\', backslashes * 2);
                    break;
                }
                if (argument[i] == '"')
                {
                    quoted.Append('\\', backslashes * 2 + 1);
                    quoted.Append('"');
                }
                else
                {
                    quoted.Append('\\', backslashes);
                    quoted.Append(argument[i]);
                }
            }
            quoted.Append('"');
            return quoted.ToString();
        }
    }
}
