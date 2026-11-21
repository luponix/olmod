using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Overload;
using UnityEngine;

namespace GameMod.Perf
{
    static class FramePacer
    {
        public static bool Precise = true;
        public static double SpinMarginMs = 1.5;

        static int deadlineFramerate;
        static long nextDeadline;
        static bool started;
        static bool timerResolutionRaised;

        public static void ApplyTargetFramerate()
        {
            if (GameplayManager.IsDedicatedServer())
            {
                Application.targetFrameRate = 120;
                return;
            }
            int target;
            if (FramerateLimiter.target_framerate != 0)
                target = FramerateLimiter.target_framerate;
            else
                target = GameManager.m_game_state == GameManager.GameState.GAMEPLAY ? -1 : 120;
            Application.targetFrameRate = Precise ? -1 : target;
            deadlineFramerate = Precise && QualitySettings.vSyncCount == 0 ? target : -1;
        }

        public static void StartOnce()
        {
            if (started || GameplayManager.IsDedicatedServer())
                return;
            started = true;
            Camera.onPreCull += FrameIntervals.MarkRender;
            GameManager.m_gm.StartCoroutine(EndOfFrameLoop());
        }

        static IEnumerator EndOfFrameLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                long endOfFrameTimestamp = Stopwatch.GetTimestamp();
                if (deadlineFramerate > 0)
                    WaitForDeadline(deadlineFramerate);
                FrameIntervals.Record(endOfFrameTimestamp, Stopwatch.GetTimestamp());
            }
        }

        static void WaitForDeadline(int framerate)
        {
            RaiseTimerResolution();
            long interval = Stopwatch.Frequency / framerate;
            if (Stopwatch.GetTimestamp() - nextDeadline > interval)
            {
                nextDeadline = Stopwatch.GetTimestamp() + interval;
                return;
            }
            long spinMargin = (long)(SpinMarginMs * Stopwatch.Frequency / 1000.0);
            while (nextDeadline - Stopwatch.GetTimestamp() > spinMargin)
                Thread.Sleep(1);
            while (Stopwatch.GetTimestamp() < nextDeadline)
                Thread.SpinWait(20);
            nextDeadline += interval;
        }

        [DllImport("winmm.dll")]
        static extern uint timeBeginPeriod(uint milliseconds);

        static void RaiseTimerResolution()
        {
            if (timerResolutionRaised)
                return;
            timerResolutionRaised = true;
            if (Application.platform == RuntimePlatform.WindowsPlayer)
                timeBeginPeriod(1);
        }

        public static void CmdFramePacing()
        {
            string word = uConsole.GetNumParameters() > 0 ? uConsole.GetString() : "";
            switch (word)
            {
                case "engine":
                case "precise":
                    Precise = word == "precise";
                    nextDeadline = 0;
                    break;
                case "margin":
                    double margin;
                    if (double.TryParse(uConsole.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out margin))
                        SpinMarginMs = Mathf.Max(0f, (float)margin);
                    break;
                case "reset":
                    FrameIntervals.Reset();
                    break;
                case "stats":
                    UnityEngine.Debug.Log("[FramePacing] " + Status() + "\n" + FrameIntervals.Summary());
                    return;
                case "dump":
                    string path = uConsole.GetNumParameters() > 1 ? uConsole.GetString() : "framepacing.tsv";
                    UnityEngine.Debug.Log("[FramePacing] wrote " + FrameIntervals.Count + " frames to " + FrameIntervals.Dump(path));
                    return;
            }
            UnityEngine.Debug.Log("[FramePacing] " + Status());
        }

        public static void DrawOption(UIElement uie, Vector2 position)
        {
            uie.SelectAndDrawStringOptionItem(Loc.LS("FRAME LIMITER"), position, 4, Precise ? Loc.LS("PRECISE") : Loc.LS("ENGINE"),
                Loc.LS("PRECISE HOLDS THE FRAMERATE LIMIT EXACTLY FOR EVEN FRAME PACING, ENGINE USES UNITY'S OWN LIMITER"));
        }

        public static void HandleSelection(int selection)
        {
            if (selection != 4)
                return;
            Precise = !Precise;
            nextDeadline = 0;
            MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
        }

        static string Status()
        {
            string limiter = Precise ? "precise" : "engine";
            string target = QualitySettings.vSyncCount > 0 ? "vsync " + QualitySettings.vSyncCount :
                Precise ? (deadlineFramerate > 0 ? deadlineFramerate + " fps" : "uncapped") :
                Application.targetFrameRate > 0 ? Application.targetFrameRate + " fps" : "uncapped";
            return string.Format(CultureInfo.InvariantCulture, "limiter {0}, {1}, spin margin {2:F2} ms, {3} frames recorded",
                limiter, target, SpinMarginMs, FrameIntervals.Count);
        }
    }
}
