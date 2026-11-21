using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace GameMod.Perf
{
    static class FrameIntervals
    {
        const int Capacity = 1 << 17;

        static readonly float[] startIntervalsMs = new float[Capacity];
        static readonly float[] presentIntervalsMs = new float[Capacity];
        static readonly bool[] collectedGarbage = new bool[Capacity];
        static readonly byte[] fixedSteps = new byte[Capacity];
        static readonly long[] updateTimestamps = new long[Capacity];
        static readonly long[] renderTimestamps = new long[Capacity];
        static readonly long[] endOfFrameTimestamps = new long[Capacity];
        static readonly long[] releaseTimestamps = new long[Capacity];
        static readonly long[] mainThreadRunNs = new long[Capacity];
        static readonly long[] mainThreadQueuedNs = new long[Capacity];
        static readonly long[] heapBytes = new long[Capacity];
        static int recorded;
        static long lastReleaseTimestamp;
        static int lastCollectionCount;

        static long updateTimestamp;
        static long renderTimestamp;
        static int frameFixedSteps;
        static float lastFixedTime;

        public static void MarkUpdate()
        {
            updateTimestamp = Stopwatch.GetTimestamp();
            renderTimestamp = 0;
            frameFixedSteps = Mathf.RoundToInt((Time.fixedTime - lastFixedTime) / Time.fixedDeltaTime);
            lastFixedTime = Time.fixedTime;
        }

        public static void MarkRender(Camera camera)
        {
            if (renderTimestamp == 0)
                renderTimestamp = Stopwatch.GetTimestamp();
        }

        public static void Record(long endOfFrameTimestamp, long releaseTimestamp)
        {
            int collectionCount = GC.CollectionCount(0);
            if (lastReleaseTimestamp != 0)
            {
                int slot = recorded % Capacity;
                startIntervalsMs[slot] = Time.unscaledDeltaTime * 1000f;
                presentIntervalsMs[slot] = (float)((releaseTimestamp - lastReleaseTimestamp) * 1000.0 / Stopwatch.Frequency);
                collectedGarbage[slot] = collectionCount != lastCollectionCount;
                fixedSteps[slot] = (byte)Mathf.Clamp(frameFixedSteps, 0, 255);
                updateTimestamps[slot] = updateTimestamp;
                renderTimestamps[slot] = renderTimestamp;
                endOfFrameTimestamps[slot] = endOfFrameTimestamp;
                releaseTimestamps[slot] = releaseTimestamp;
                ReadMainThreadSchedstat(out mainThreadRunNs[slot], out mainThreadQueuedNs[slot]);
                heapBytes[slot] = GC.GetTotalMemory(false);
                recorded++;
            }
            lastReleaseTimestamp = releaseTimestamp;
            lastCollectionCount = collectionCount;
        }

        public static void Reset()
        {
            recorded = 0;
            lastReleaseTimestamp = 0;
        }

        public static int Count
        {
            get { return Math.Min(recorded, Capacity); }
        }

        public static string Summary()
        {
            int count = Count;
            if (count == 0)
                return "no frames recorded";
            int collections = 0;
            for (int i = 0; i < count; i++)
                if (collectedGarbage[i])
                    collections++;
            return count + " frames, " + collections + " with a GC\n" +
                "  frame start: " + Describe(startIntervalsMs, count) + "\n" +
                "  present:     " + Describe(presentIntervalsMs, count);
        }

        static string Describe(float[] intervalsMs, int count)
        {
            float[] sorted = new float[count];
            Array.Copy(intervalsMs, sorted, count);
            Array.Sort(sorted);
            double sum = 0, sumOfSquares = 0;
            foreach (float interval in sorted)
            {
                sum += interval;
                sumOfSquares += interval * interval;
            }
            double mean = sum / count;
            double deviation = Math.Sqrt(Math.Max(0, sumOfSquares / count - mean * mean));
            float median = sorted[count / 2];
            int offByHalfMs = 0, hitches = 0;
            foreach (float interval in sorted)
            {
                if (Math.Abs(interval - median) > 0.5f)
                    offByHalfMs++;
                if (interval > median * 1.5f)
                    hitches++;
            }
            return string.Format(CultureInfo.InvariantCulture,
                "mean {0:F3} sd {1:F3} p1 {2:F3} p50 {3:F3} p99 {4:F3} p99.9 {5:F3} max {6:F3} ms, {7:F2}% off median by >0.5 ms, {8} over 1.5x median",
                mean, deviation, Percentile(sorted, 0.01), median, Percentile(sorted, 0.99), Percentile(sorted, 0.999), sorted[count - 1],
                100.0 * offByHalfMs / count, hitches);
        }

        static float Percentile(float[] sorted, double fraction)
        {
            return sorted[Math.Min(sorted.Length - 1, (int)(fraction * sorted.Length))];
        }

        public static string Dump(string path)
        {
            if (!Path.IsPathRooted(path))
                path = Path.Combine(Application.persistentDataPath, path);
            long anchorTimestamp = Stopwatch.GetTimestamp();
            long anchorNs = MonotonicNs();
            int count = Count;
            int first = recorded > Capacity ? recorded % Capacity : 0;
            var text = new StringBuilder("start_ms\tpresent_ms\tgc\tfixed_steps\tupdate_ns\trender_ns\tend_of_frame_ns\trelease_ns\tmain_run_ns\tmain_queued_ns\theap_bytes\n");
            for (int i = 0; i < count; i++)
            {
                int slot = (first + i) % Capacity;
                text.AppendFormat(CultureInfo.InvariantCulture, "{0:F4}\t{1:F4}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}\t{10}\n",
                    startIntervalsMs[slot], presentIntervalsMs[slot], collectedGarbage[slot] ? 1 : 0, fixedSteps[slot],
                    ToMonotonicNs(updateTimestamps[slot], anchorTimestamp, anchorNs),
                    renderTimestamps[slot] == 0 ? 0 : ToMonotonicNs(renderTimestamps[slot], anchorTimestamp, anchorNs),
                    ToMonotonicNs(endOfFrameTimestamps[slot], anchorTimestamp, anchorNs),
                    ToMonotonicNs(releaseTimestamps[slot], anchorTimestamp, anchorNs),
                    mainThreadRunNs[slot], mainThreadQueuedNs[slot], heapBytes[slot]);
            }
            File.WriteAllText(path, text.ToString());
            return path;
        }

        static long ToMonotonicNs(long timestamp, long anchorTimestamp, long anchorNs)
        {
            return anchorNs + (long)((timestamp - anchorTimestamp) * (1e9 / Stopwatch.Frequency));
        }

        static bool IsLinux
        {
            get { return Application.platform == RuntimePlatform.LinuxPlayer; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Timespec
        {
            public long seconds;
            public long nanoseconds;
        }

        [DllImport("libc")]
        static extern int clock_gettime(int clock, out Timespec time);

        static long MonotonicNs()
        {
            Timespec time;
            if (!IsLinux || clock_gettime(1, out time) != 0)
                return 0;
            return time.seconds * 1000000000L + time.nanoseconds;
        }

        [DllImport("libc")]
        static extern int open(string path, int flags);

        [DllImport("libc")]
        static extern IntPtr pread(int file, byte[] buffer, UIntPtr count, IntPtr offset);

        static int schedstatFile = -2;
        static readonly byte[] schedstat = new byte[96];

        static void ReadMainThreadSchedstat(out long runNs, out long queuedNs)
        {
            runNs = queuedNs = 0;
            if (schedstatFile == -2)
                schedstatFile = IsLinux ? open("/proc/thread-self/schedstat", 0) : -1;
            if (schedstatFile < 0)
                return;
            int length = (int)pread(schedstatFile, schedstat, (UIntPtr)schedstat.Length, IntPtr.Zero);
            int position = 0;
            runNs = ParseNumber(length, ref position);
            queuedNs = ParseNumber(length, ref position);
        }

        static long ParseNumber(int length, ref int position)
        {
            while (position < length && (schedstat[position] < '0' || schedstat[position] > '9'))
                position++;
            long number = 0;
            while (position < length && schedstat[position] >= '0' && schedstat[position] <= '9')
                number = number * 10 + (schedstat[position++] - '0');
            return number;
        }
    }
}
