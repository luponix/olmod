// .olr v8 replay parser for the standalone lag-comp evaluator.
//
// The format is written by .NET BinaryWriter (little-endian), so BinaryReader
// natively matches the string encoding (7-bit-varint length-prefixed UTF-8).
// The only field BinaryReader cannot decode directly is the half-precision
// (IEEE 754 binary16) vel/vrot components, decoded manually below.
//
// Reference: tools/lagcomp_bench/olr.py + tools/lagcomp_bench/README.md
// ("`.olr` v8 binary format"). Events and chat are not needed for position
// evaluation and are skipped (we stop reading after the frame block).

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LagCompEval {

    public class OlrException : Exception {
        public OlrException(string msg) : base(msg) { }
    }

    public class OlrUnsupportedVersion : OlrException {
        public readonly int Version;
        public OlrUnsupportedVersion(string path, int version)
            : base(string.Format("{0}: unsupported .olr version {1} (expected {2})",
                                 Path.GetFileName(path), version, OlrReader.VERSION)) {
            Version = version;
        }
    }

    public class OlrPlayer {
        public string Name;
        public int Team;
    }

    // Per-roster-slot frame-aligned arrays (length == frame count).
    // Absent frames hold default values; use Present to mask.
    public class OlrSlot {
        public bool[] Present;
        public bool[] Alive;
        public Vector3[] Pos;
        public Vector3[] Vel;    // decoded from binary16
        public Vector3[] Vrot;   // decoded from binary16, rad/s
        public Quaternion[] Rot;
    }

    public class OlrReplay {
        public string Path;
        public string SceneName;
        public int MatchMode;
        public int TickRateHz;
        public float StoredDuration;
        public List<OlrPlayer> Players;
        public int FrameCount;
        public double[] T;       // per-frame timestamp (seconds)
        public OlrSlot[] Slots;
    }

    public static class OlrReader {
        public const uint MAGIC = 0x314C4F52; // "OLR1" as little-endian uint32
        public const int VERSION = 8;

        public static OlrReplay Load(string path) {
            byte[] data = File.ReadAllBytes(path);
            using (BinaryReader r = new BinaryReader(new MemoryStream(data))) {
                OlrReplay rep = new OlrReplay();
                rep.Path = path;

                uint magic = r.ReadUInt32();
                if (magic != MAGIC) {
                    throw new OlrException(string.Format("{0}: bad magic 0x{1:X8}",
                                                         Path.GetFileName(path), magic));
                }
                int version = r.ReadByte();
                if (version != VERSION) {
                    throw new OlrUnsupportedVersion(path, version);
                }

                rep.SceneName = r.ReadString();
                r.ReadString();                    // levelIdHash
                rep.MatchMode = r.ReadInt32();
                rep.TickRateHz = r.ReadInt32();
                rep.StoredDuration = r.ReadSingle();
                r.ReadSingle();                    // maxHitpoints
                r.ReadSingle();                    // matchTimeLimit
                r.ReadSingle();                    // matchStartOffset

                int playerCount = r.ReadInt32();
                if (playerCount < 0 || playerCount > 64) {
                    throw new OlrException(string.Format("{0}: implausible player count {1}",
                                                         Path.GetFileName(path), playerCount));
                }
                rep.Players = new List<OlrPlayer>(playerCount);
                for (int i = 0; i < playerCount; i++) {
                    OlrPlayer p = new OlrPlayer();
                    p.Name = r.ReadString();
                    p.Team = r.ReadInt32();
                    r.ReadSingle(); r.ReadSingle(); r.ReadSingle();   // color r,g,b
                    rep.Players.Add(p);
                }

                int frameCount = r.ReadInt32();
                if (frameCount < 0) {
                    throw new OlrException(Path.GetFileName(path) + ": negative frame count");
                }
                rep.FrameCount = frameCount;
                rep.T = new double[frameCount];
                rep.Slots = new OlrSlot[playerCount];
                for (int s = 0; s < playerCount; s++) {
                    OlrSlot sl = new OlrSlot();
                    sl.Present = new bool[frameCount];
                    sl.Alive = new bool[frameCount];
                    sl.Pos = new Vector3[frameCount];
                    sl.Vel = new Vector3[frameCount];
                    sl.Vrot = new Vector3[frameCount];
                    sl.Rot = new Quaternion[frameCount];
                    rep.Slots[s] = sl;
                }

                for (int f = 0; f < frameCount; f++) {
                    rep.T[f] = r.ReadSingle();
                    for (int s = 0; s < playerCount; s++) {
                        byte flags = r.ReadByte();
                        bool present = (flags & 1) != 0;
                        OlrSlot sl = rep.Slots[s];
                        sl.Present[f] = present;
                        sl.Alive[f] = (flags & 2) != 0;
                        if (!present) {
                            continue;
                        }
                        // 56-byte ship state (see lagcomp_bench README)
                        sl.Pos[f] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        sl.Rot[f] = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        r.ReadByte();              // weapon
                        r.ReadByte();              // missile
                        r.ReadByte();              // health
                        sl.Vel[f] = new Vector3(ReadHalf(r), ReadHalf(r), ReadHalf(r));
                        sl.Vrot[f] = new Vector3(ReadHalf(r), ReadHalf(r), ReadHalf(r));
                        r.ReadByte();              // energy
                        r.ReadUInt16();            // ammo
                        r.ReadByte();              // weaponsOwned
                        r.ReadByte();              // missilesOwned
                        r.ReadUInt64();            // missileAmmo
                    }
                    // drifting-item block: ushort count + count * (ushort id + 3*f32 pos)
                    int itemCount = r.ReadUInt16();
                    r.BaseStream.Seek(itemCount * 14, SeekOrigin.Current);
                }
                // events + chat follow; not needed for position evaluation
                return rep;
            }
        }

        static float ReadHalf(BinaryReader r) {
            return HalfToFloat(r.ReadUInt16());
        }

        // IEEE 754 binary16 -> binary32, incl. subnormals, inf and NaN.
        public static float HalfToFloat(ushort h) {
            int sign = (h >> 15) & 1;
            int exp = (h >> 10) & 0x1F;
            int mant = h & 0x3FF;
            int bits;
            if (exp == 0) {
                if (mant == 0) {
                    bits = sign << 31;                          // +/- 0
                } else {
                    // subnormal half: renormalize into a normal float
                    int e = -1;
                    do {
                        mant <<= 1;
                        e++;
                    } while ((mant & 0x400) == 0);
                    mant &= 0x3FF;
                    bits = (sign << 31) | ((127 - 15 - e) << 23) | (mant << 13);
                }
            } else if (exp == 31) {
                bits = (sign << 31) | (0xFF << 23) | (mant << 13);   // inf / NaN
            } else {
                bits = (sign << 31) | ((exp - 15 + 127) << 23) | (mant << 13);
            }
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }
    }
}
