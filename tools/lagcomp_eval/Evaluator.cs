// Standalone lag-compensation evaluator: runs olmod's REAL C# prediction core
// (GameMod/MPShipPredictionCore.cs, compiled into this exe against the
// UnityShim) plus the legacy extrapolation modes over recorded .olr v8
// replays and reports position error statistics per (mode, horizon).
//
// Methodology mirrors tools/lagcomp_bench/ (tracks.py + bench.py) so the
// numbers are directly comparable:
//   - segments: per roster slot, contiguous present-AND-alive frame runs,
//     split on dt > 0.5 s, dt <= 0, or position jump > 20 m; segments with
//     fewer than 3 frames or shorter than 2 s are discarded.
//   - ground truth: linear interpolation of the recorded positions at t+h
//     within the same segment.
//   - causality: the smart mode streams snapshots into a fresh ShipTrack in
//     frame order, exactly like the game's capture path; the prediction at
//     frame i uses only samples <= i (the ledger scores itself as later
//     snapshots arrive, exactly as in-game).
//
// Modes:
//   hold  - last received position (what pure interpolation shows)
//   cv    - pos + vel*h (legacy modes 0/1 position math)
//   arc   - legacy Motion Arc (mode 2) position math at rotation strength
//           100% (rotScale 1.0, arcRotScale 0.34)
//   smart - the real core: tracker + hazard + adaptive horizon + ledger +
//           model selection (no projective smoothing, no wall clamp -- those
//           are render-side)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using GameMod;
using UnityEngine;

namespace LagCompEval {

    class Segment {
        public string File;
        public string Player;
        public double[] T;
        public Vector3[] Pos;
        public Vector3[] Vel;
        public Vector3[] Vrot;
        public Quaternion[] Rot;

        public int Count { get { return T.Length; } }
        public double Duration { get { return T[T.Length - 1] - T[0]; } }
    }

    static class Evaluator {
        const double MaxGap = 0.5;      // s   (tracks.py DEFAULT_MAX_GAP)
        const double MaxJump = 20.0;    // m   (tracks.py DEFAULT_MAX_JUMP)
        const double MinDuration = 2.0; // s   (tracks.py DEFAULT_MIN_DURATION)

        static readonly string[] AllModes = { "hold", "cv", "arc", "smart" };

        static int Main(string[] rawArgs) {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            List<string> paths = new List<string>();
            double[] horizons = { 0.1, 0.2, 0.3 };
            List<string> modes = new List<string>(AllModes);
            List<string> focus = new List<string> { "Schmodderkatze" };
            List<string> sets = new List<string>();
            string csvOut = null;

            for (int i = 0; i < rawArgs.Length; i++) {
                string a = rawArgs[i];
                switch (a) {
                    case "--replays":
                        paths.Add(Need(rawArgs, ref i, a));
                        break;
                    case "--horizons": {
                        string[] parts = Need(rawArgs, ref i, a).Split(',');
                        List<double> hs = new List<double>();
                        foreach (string p in parts) {
                            if (p.Trim().Length > 0) {
                                hs.Add(double.Parse(p.Trim(), CultureInfo.InvariantCulture));
                            }
                        }
                        horizons = hs.ToArray();
                        break;
                    }
                    case "--modes": {
                        modes.Clear();
                        foreach (string m in Need(rawArgs, ref i, a).Split(',')) {
                            string mm = m.Trim().ToLowerInvariant();
                            if (mm.Length == 0) {
                                continue;
                            }
                            if (Array.IndexOf(AllModes, mm) < 0) {
                                Console.Error.WriteLine("unknown mode '{0}' (valid: {1})", mm, string.Join(",", AllModes));
                                return 2;
                            }
                            modes.Add(mm);
                        }
                        break;
                    }
                    case "--focus":
                        focus.Clear();
                        foreach (string f in Need(rawArgs, ref i, a).Split(',')) {
                            if (f.Trim().Length > 0) {
                                focus.Add(f.Trim());
                            }
                        }
                        break;
                    case "--set":
                        sets.Add(Need(rawArgs, ref i, a));
                        break;
                    case "--csv":
                        csvOut = Need(rawArgs, ref i, a);
                        break;
                    case "--selftest":
                        return SelfTest();
                    case "-h":
                    case "--help":
                        Usage();
                        return 0;
                    default:
                        if (a.StartsWith("-")) {
                            Console.Error.WriteLine("unknown option '{0}'", a);
                            Usage();
                            return 2;
                        }
                        paths.Add(a);
                        break;
                }
            }

            // ---- parameter overrides (PredictionParams statics via reflection) ----
            foreach (string kv in sets) {
                int eq = kv.IndexOf('=');
                if (eq <= 0) {
                    Console.Error.WriteLine("--set expects Param=Value, got '{0}'", kv);
                    return 2;
                }
                string name = kv.Substring(0, eq).Trim();
                string val = kv.Substring(eq + 1).Trim();
                FieldInfo fi = typeof(PredictionParams).GetField(name, BindingFlags.Public | BindingFlags.Static);
                if (fi == null) {
                    Console.Error.WriteLine("--set: no such PredictionParams field '{0}'. Available:", name);
                    foreach (FieldInfo f in typeof(PredictionParams).GetFields(BindingFlags.Public | BindingFlags.Static)) {
                        Console.Error.WriteLine("  {0} {1} = {2}", f.FieldType.Name, f.Name, f.GetValue(null));
                    }
                    return 2;
                }
                object parsed;
                if (fi.FieldType == typeof(float)) {
                    parsed = float.Parse(val, CultureInfo.InvariantCulture);
                } else if (fi.FieldType == typeof(int)) {
                    parsed = int.Parse(val, CultureInfo.InvariantCulture);
                } else if (fi.FieldType == typeof(bool)) {
                    parsed = bool.Parse(val);
                } else {
                    Console.Error.WriteLine("--set: unsupported field type {0} for '{1}'", fi.FieldType, name);
                    return 2;
                }
                fi.SetValue(null, parsed);
                Console.WriteLine("override: PredictionParams.{0} = {1}", name, parsed);
            }

            // ---- corpus ----
            if (paths.Count == 0) {
                string home = Environment.GetEnvironmentVariable("HOME") ?? "";
                paths.Add(Path.Combine(home, ".config/unity3d/Revival/Overload/Replays"));
            }
            List<string> files = new List<string>();
            foreach (string p in paths) {
                if (Directory.Exists(p)) {
                    string[] found = Directory.GetFiles(p, "*.olr");
                    Array.Sort(found, StringComparer.Ordinal);
                    files.AddRange(found);
                } else if (File.Exists(p)) {
                    files.Add(p);
                } else {
                    Console.Error.WriteLine("not found: {0}", p);
                    return 2;
                }
            }
            if (files.Count == 0) {
                Console.Error.WriteLine("no .olr files found");
                return 2;
            }

            // ---- load + segment + evaluate ----
            // errors[mode][hIdx] : player -> error list
            Dictionary<string, Dictionary<int, Dictionary<string, List<float>>>> errors =
                new Dictionary<string, Dictionary<int, Dictionary<string, List<float>>>>();
            foreach (string m in modes) {
                errors[m] = new Dictionary<int, Dictionary<string, List<float>>>();
                for (int hi = 0; hi < horizons.Length; hi++) {
                    errors[m][hi] = new Dictionary<string, List<float>>();
                }
            }

            int parsedFiles = 0;
            Dictionary<int, int> skippedVersions = new Dictionary<int, int>();
            int segCount = 0;
            double flightSeconds = 0;
            Dictionary<string, double> flightByPlayer = new Dictionary<string, double>();
            DateTime t0 = DateTime.UtcNow;

            foreach (string file in files) {
                OlrReplay rep;
                try {
                    rep = OlrReader.Load(file);
                } catch (OlrUnsupportedVersion e) {
                    int c;
                    skippedVersions.TryGetValue(e.Version, out c);
                    skippedVersions[e.Version] = c + 1;
                    continue;
                } catch (OlrException e) {
                    Console.Error.WriteLine("skipping {0}: {1}", Path.GetFileName(file), e.Message);
                    continue;
                }
                parsedFiles++;

                foreach (Segment seg in ExtractSegments(rep)) {
                    segCount++;
                    flightSeconds += seg.Duration;
                    double fs;
                    flightByPlayer.TryGetValue(seg.Player, out fs);
                    flightByPlayer[seg.Player] = fs + seg.Duration;
                    EvaluateSegment(seg, horizons, modes, errors);
                }
            }
            double elapsed = (DateTime.UtcNow - t0).TotalSeconds;

            // ---- report ----
            Console.WriteLine(new string('=', 78));
            Console.WriteLine("Corpus: {0} file(s), {1} parsed (v8) in {2:F1} s", files.Count, parsedFiles, elapsed);
            foreach (KeyValuePair<int, int> kv in skippedVersions) {
                Console.WriteLine("  version {0}: {1} file(s) skipped (unsupported)", kv.Key, kv.Value);
            }
            Console.WriteLine("Tracks: {0} segments, {1:F0} flight-seconds total", segCount, flightSeconds);

            // focus name resolution (substring, case-insensitive)
            Dictionary<string, List<string>> focusNames = new Dictionary<string, List<string>>();
            foreach (string label in focus) {
                List<string> names = new List<string>();
                foreach (string n in flightByPlayer.Keys) {
                    if (n.ToLowerInvariant().Contains(label.ToLowerInvariant())) {
                        names.Add(n);
                    }
                }
                focusNames[label] = names;
                double secs = 0;
                foreach (string n in names) {
                    secs += flightByPlayer[n];
                }
                Console.WriteLine("Focus '{0}': matched [{1}] -> {2:F0} flight-seconds",
                                  label, string.Join(", ", names.ToArray()), secs);
            }

            PrintTable("=== OVERALL ===", modes, horizons, errors, null);
            foreach (string label in focus) {
                List<string> names = focusNames[label];
                if (names.Count == 0) {
                    Console.WriteLine();
                    Console.WriteLine("=== {0} === (no matching tracks)", label);
                    continue;
                }
                PrintTable(string.Format("=== {0} ({1}) ===", label, string.Join("+", names.ToArray())),
                           modes, horizons, errors, names);
            }

            // ---- CSV ----
            if (csvOut != null) {
                using (StreamWriter w = new StreamWriter(csvOut)) {
                    w.WriteLine("scope,mode,horizon_ms,samples,mean_m,median_m,p90_m,p99_m");
                    foreach (string m in modes) {
                        for (int hi = 0; hi < horizons.Length; hi++) {
                            EmitCsv(w, "overall", m, horizons[hi], Concat(errors[m][hi], null));
                        }
                    }
                    foreach (string label in focus) {
                        foreach (string m in modes) {
                            for (int hi = 0; hi < horizons.Length; hi++) {
                                EmitCsv(w, "focus:" + label, m, horizons[hi], Concat(errors[m][hi], focusNames[label]));
                            }
                        }
                    }
                    List<string> players = new List<string>(flightByPlayer.Keys);
                    players.Sort(StringComparer.Ordinal);
                    foreach (string name in players) {
                        List<string> one = new List<string> { name };
                        foreach (string m in modes) {
                            for (int hi = 0; hi < horizons.Length; hi++) {
                                EmitCsv(w, "player:" + name, m, horizons[hi], Concat(errors[m][hi], one));
                            }
                        }
                    }
                }
                Console.WriteLine();
                Console.WriteLine("Wrote {0}", csvOut);
            }
            return 0;
        }

        static string Need(string[] args, ref int i, string opt) {
            if (i + 1 >= args.Length) {
                throw new ArgumentException(opt + " needs a value");
            }
            return args[++i];
        }

        static void Usage() {
            Console.WriteLine("usage: mono lagcomp_eval.exe [--replays DIR|files...] [--horizons 0.1,0.2,0.3]");
            Console.WriteLine("                             [--modes hold,cv,arc,smart] [--focus NAME]");
            Console.WriteLine("                             [--set Param=Value ...] [--csv OUT] [--selftest]");
            Console.WriteLine("default replay dir: ~/.config/unity3d/Revival/Overload/Replays");
        }

        // ------------------------------------------------------------------
        // Segment extraction -- mirrors tools/lagcomp_bench/tracks.py.
        // ------------------------------------------------------------------
        static IEnumerable<Segment> ExtractSegments(OlrReplay rep) {
            for (int s = 0; s < rep.Slots.Length; s++) {
                OlrSlot sl = rep.Slots[s];
                List<int> keep = new List<int>();
                for (int f = 0; f < rep.FrameCount; f++) {
                    if (sl.Present[f] && sl.Alive[f]) {
                        keep.Add(f);
                    }
                }
                if (keep.Count < 2) {
                    continue;
                }
                int a = 0;
                for (int k = 1; k <= keep.Count; k++) {
                    bool cut = k == keep.Count;
                    if (!cut) {
                        double dt = rep.T[keep[k]] - rep.T[keep[k - 1]];
                        double jump = (sl.Pos[keep[k]] - sl.Pos[keep[k - 1]]).magnitude;
                        cut = dt > MaxGap || dt <= 0 || jump > MaxJump;
                    }
                    if (!cut) {
                        continue;
                    }
                    int n = k - a;
                    if (n >= 3 && rep.T[keep[k - 1]] - rep.T[keep[a]] >= MinDuration) {
                        Segment seg = new Segment();
                        seg.File = Path.GetFileName(rep.Path);
                        seg.Player = rep.Players[s].Name;
                        seg.T = new double[n];
                        seg.Pos = new Vector3[n];
                        seg.Vel = new Vector3[n];
                        seg.Vrot = new Vector3[n];
                        seg.Rot = new Quaternion[n];
                        for (int j = 0; j < n; j++) {
                            int f = keep[a + j];
                            seg.T[j] = rep.T[f];
                            seg.Pos[j] = sl.Pos[f];
                            seg.Vel[j] = sl.Vel[f];
                            seg.Vrot[j] = sl.Vrot[f];
                            seg.Rot[j] = sl.Rot[f];
                        }
                        yield return seg;
                    }
                    a = k;
                }
            }
        }

        // ------------------------------------------------------------------
        // Evaluation of one segment: for every mode and horizon, compare the
        // prediction from the snapshot at frame i against the interpolated
        // recorded position at t[i]+h. Only frames with t[i]+h inside the
        // segment are scored (same n_valid rule as bench.py).
        // ------------------------------------------------------------------
        static void EvaluateSegment(Segment seg, double[] horizons, List<string> modes,
                                    Dictionary<string, Dictionary<int, Dictionary<string, List<float>>>> errors) {
            int n = seg.Count;
            double tEnd = seg.T[n - 1];

            // ground truth per horizon
            int[] nValid = new int[horizons.Length];
            double[][] gtx = new double[horizons.Length][];
            double[][] gty = new double[horizons.Length][];
            double[][] gtz = new double[horizons.Length][];
            for (int hi = 0; hi < horizons.Length; hi++) {
                double h = horizons[hi];
                int nv = UpperBound(seg.T, tEnd - h);   // count of i with t[i] <= tEnd - h
                nValid[hi] = nv;
                if (nv <= 0) {
                    continue;
                }
                gtx[hi] = new double[nv];
                gty[hi] = new double[nv];
                gtz[hi] = new double[nv];
                int j = 0;
                for (int i = 0; i < nv; i++) {
                    double tq = seg.T[i] + h;
                    while (j < n - 2 && seg.T[j + 1] < tq) {
                        j++;
                    }
                    double span = seg.T[j + 1] - seg.T[j];
                    double f = span > 0 ? (tq - seg.T[j]) / span : 0.0;
                    if (f < 0) { f = 0; } else if (f > 1) { f = 1; }
                    gtx[hi][i] = seg.Pos[j].x + (seg.Pos[j + 1].x - seg.Pos[j].x) * f;
                    gty[hi][i] = seg.Pos[j].y + (seg.Pos[j + 1].y - seg.Pos[j].y) * f;
                    gtz[hi][i] = seg.Pos[j].z + (seg.Pos[j + 1].z - seg.Pos[j].z) * f;
                }
            }

            foreach (string mode in modes) {
                for (int hi = 0; hi < horizons.Length; hi++) {
                    int nv = nValid[hi];
                    if (nv <= 0 && mode != "smart") {
                        continue;
                    }
                    float h = (float)horizons[hi];
                    List<float> outList = PlayerList(errors[mode][hi], seg.Player);

                    if (mode == "smart") {
                        // Stream snapshots into a fresh ShipTrack exactly like the
                        // game's capture path; one track per (segment, horizon) so
                        // the ledger scores the same horizon it renders (in-game
                        // the horizon is the roughly constant ping-derived delta).
                        ShipTrack tr = new ShipTrack(0);
                        tr.velFromDiff = false;
                        for (int i = 0; i < n; i++) {
                            ShipSnap sn;
                            sn.tServer = (float)seg.T[i];
                            sn.tLocal = (float)seg.T[i];
                            sn.pos = seg.Pos[i];
                            sn.vel = seg.Vel[i];
                            sn.vrot = seg.Vrot[i];
                            sn.rot = seg.Rot[i];
                            tr.Append(ref sn);
                            ShipPredictionCore.ConsumeFresh(tr);
                            int model;
                            float tEff, hz;
                            Vector3 pred = ShipPredictionCore.PredictSmart(
                                tr, seg.Pos[i], seg.Vel[i], h, (float)seg.T[i],
                                out model, out tEff, out hz);
                            if (i < nv) {
                                outList.Add(Err(pred, gtx[hi][i], gty[hi][i], gtz[hi][i]));
                            }
                        }
                        continue;
                    }

                    for (int i = 0; i < nv; i++) {
                        Vector3 pred;
                        switch (mode) {
                            case "hold":
                                pred = seg.Pos[i];
                                break;
                            case "cv":
                                // legacy modes 0/1 position math:
                                // Vector3.LerpUnclamped(pos, pos + vel, t)
                                pred = seg.Pos[i] + seg.Vel[i] * h;
                                break;
                            default: {
                                // legacy Motion Arc (mode 2), rotation strength 100%
                                // (rotScale = 1.0, arcRotScale = 0.34); exact copy of
                                // MPClientExtrapolation.extrapolatePlayer
                                Quaternion newRot = Quaternion.Euler(seg.Vrot[i] * (Mathf.Rad2Deg * h * 1.0f)) * seg.Rot[i];
                                Vector3 lin = seg.Pos[i] + seg.Vel[i] * h;
                                Vector3 rotPos = Vector3.LerpUnclamped(
                                    seg.Pos[i],
                                    seg.Pos[i] + ((Quaternion.Inverse(seg.Rot[i]) * newRot) * seg.Vel[i]),
                                    h);
                                pred = Vector3.Lerp(lin, rotPos, 0.34f);
                                break;
                            }
                        }
                        outList.Add(Err(pred, gtx[hi][i], gty[hi][i], gtz[hi][i]));
                    }
                }
            }
        }

        static float Err(Vector3 pred, double gx, double gy, double gz) {
            double dx = pred.x - gx, dy = pred.y - gy, dz = pred.z - gz;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        static List<float> PlayerList(Dictionary<string, List<float>> d, string player) {
            List<float> l;
            if (!d.TryGetValue(player, out l)) {
                l = new List<float>();
                d[player] = l;
            }
            return l;
        }

        // count of elements <= x (numpy searchsorted side='right')
        static int UpperBound(double[] t, double x) {
            int lo = 0, hi = t.Length;
            while (lo < hi) {
                int mid = (lo + hi) / 2;
                if (t[mid] <= x) {
                    lo = mid + 1;
                } else {
                    hi = mid;
                }
            }
            return lo;
        }

        // ------------------------------------------------------------------
        // Statistics + output
        // ------------------------------------------------------------------
        static List<float> Concat(Dictionary<string, List<float>> byPlayer, List<string> names) {
            List<float> all = new List<float>();
            foreach (KeyValuePair<string, List<float>> kv in byPlayer) {
                if (names != null && !names.Contains(kv.Key)) {
                    continue;
                }
                all.AddRange(kv.Value);
            }
            return all;
        }

        // numpy-style linear-interpolation percentile on a SORTED array
        static double Percentile(float[] sorted, double q) {
            if (sorted.Length == 1) {
                return sorted[0];
            }
            double rank = q / 100.0 * (sorted.Length - 1);
            int lo = (int)Math.Floor(rank);
            int hi = (int)Math.Ceiling(rank);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
        }

        static void PrintTable(string title, List<string> modes, double[] horizons,
                               Dictionary<string, Dictionary<int, Dictionary<string, List<float>>>> errors,
                               List<string> names) {
            Console.WriteLine();
            Console.WriteLine(title);
            string hdr = string.Format("{0,-8} {1,-6} {2,10} {3,9} {4,9} {5,9} {6,9}",
                                       "mode", "h(ms)", "samples", "mean", "median", "p90", "p99");
            Console.WriteLine(hdr);
            Console.WriteLine(new string('-', hdr.Length));
            foreach (string m in modes) {
                for (int hi = 0; hi < horizons.Length; hi++) {
                    List<float> e = Concat(errors[m][hi], names);
                    if (e.Count == 0) {
                        Console.WriteLine("{0,-8} {1,-6} {2,10}", m, Math.Round(horizons[hi] * 1000), "-");
                        continue;
                    }
                    float[] arr = e.ToArray();
                    double mean = 0;
                    for (int i = 0; i < arr.Length; i++) {
                        mean += arr[i];
                    }
                    mean /= arr.Length;
                    Array.Sort(arr);
                    Console.WriteLine("{0,-8} {1,-6} {2,10} {3,9:F3} {4,9:F3} {5,9:F3} {6,9:F3}",
                                      m, Math.Round(horizons[hi] * 1000), arr.Length, mean,
                                      Percentile(arr, 50), Percentile(arr, 90), Percentile(arr, 99));
                }
                Console.WriteLine();
            }
        }

        static void EmitCsv(StreamWriter w, string scope, string mode, double h, List<float> e) {
            if (e.Count == 0) {
                return;
            }
            float[] arr = e.ToArray();
            double mean = 0;
            for (int i = 0; i < arr.Length; i++) {
                mean += arr[i];
            }
            mean /= arr.Length;
            Array.Sort(arr);
            w.WriteLine("{0},{1},{2},{3},{4:F4},{5:F4},{6:F4},{7:F4}",
                        scope.Replace(",", ";"), mode, Math.Round(h * 1000), arr.Length,
                        mean, Percentile(arr, 50), Percentile(arr, 90), Percentile(arr, 99));
        }

        // ------------------------------------------------------------------
        // --selftest: shim sanity (documented Unity conventions)
        // ------------------------------------------------------------------
        static int SelfTest() {
            int fails = 0;

            // Quaternion.Euler test vector (verified against the engine)
            Quaternion q = Quaternion.Euler(30f, 45f, 60f);
            fails += Check("Euler.x", q.x, 0.39190f);
            fails += Check("Euler.y", q.y, 0.20056f);
            fails += Check("Euler.z", q.z, 0.36042f);
            fails += Check("Euler.w", q.w, 0.82236f);

            // quaternion vector rotation: 90 deg about Y takes +Z to +X
            Vector3 v = Quaternion.Euler(0f, 90f, 0f) * new Vector3(0f, 0f, 1f);
            fails += Check("RotY90.x", v.x, 1f);
            fails += Check("RotY90.z", v.z, 0f);

            // half decode
            fails += CheckExact("half 1.0", OlrReader.HalfToFloat(0x3C00), 1.0f);
            fails += CheckExact("half -2.0", OlrReader.HalfToFloat(0xC000), -2.0f);
            fails += CheckExact("half 65504", OlrReader.HalfToFloat(0x7BFF), 65504f);
            fails += CheckExact("half min-sub", OlrReader.HalfToFloat(0x0001), 5.9604645E-08f);
            fails += CheckExact("half 0", OlrReader.HalfToFloat(0x0000), 0f);

            // Unity approximate Vector3 equality
            if (!(new Vector3(1f, 2f, 3f) == new Vector3(1f, 2f, 3.0000001f))) {
                Console.WriteLine("FAIL Vector3 == approximate");
                fails++;
            }
            if (new Vector3(1f, 2f, 3f) == new Vector3(1f, 2f, 3.001f)) {
                Console.WriteLine("FAIL Vector3 == distinct");
                fails++;
            }

            // Mathf.Sign(0) == 1 (Unity semantics)
            if (Mathf.Sign(0f) != 1f) {
                Console.WriteLine("FAIL Mathf.Sign(0)");
                fails++;
            }

            Console.WriteLine(fails == 0 ? "selftest OK" : string.Format("selftest: {0} FAILURE(S)", fails));
            return fails == 0 ? 0 : 1;
        }

        static int Check(string name, float got, float want) {
            if (Math.Abs(got - want) > 1e-4f) {
                Console.WriteLine("FAIL {0}: got {1} want {2}", name, got, want);
                return 1;
            }
            return 0;
        }

        static int CheckExact(string name, float got, float want) {
            if (got != want) {
                Console.WriteLine("FAIL {0}: got {1} want {2}", name, got, want);
                return 1;
            }
            return 0;
        }
    }
}
