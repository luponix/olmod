// Minimal UnityEngine value-type shim for the standalone lag-comp evaluator.
// Compiled ONLY into lagcomp_eval.exe (never into GameMod.dll) so that
// GameMod/MPShipPredictionCore.cs -- the REAL shipped prediction core -- can
// be compiled without the Unity assemblies.
//
// Only the members the core (and the evaluator's legacy-mode math) actually
// use are implemented. Semantics follow UnityEngine where they differ from
// the naive implementation:
//   - Vector3.operator== is approximate ((a-b).sqrMagnitude < 9.99999944E-11);
//     the core's synthetic-tick detection relies on this exact comparison.
//   - Vector3.normalized returns zero below magnitude 1e-5.
//   - Mathf.Sign(0) == 1.
//   - Mathf.Lerp / Vector3.Lerp clamp t to [0,1].
//   - Quaternion.Euler applies world-axis rotations in Z, X, Y order
//     (q = qy * qx * qz). Verified against the engine:
//     Euler(30,45,60) == (0.39190, 0.20056, 0.36042, 0.82236).
// All arithmetic is float, like the engine.

using System;

namespace UnityEngine {

    public struct Vector3 {
        public float x, y, z;

        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public static Vector3 zero { get { return new Vector3(0f, 0f, 0f); } }
        public static Vector3 right { get { return new Vector3(1f, 0f, 0f); } }
        public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }

        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator -(Vector3 a) { return new Vector3(-a.x, -a.y, -a.z); }
        public static Vector3 operator *(Vector3 a, float d) { return new Vector3(a.x * d, a.y * d, a.z * d); }
        public static Vector3 operator *(float d, Vector3 a) { return new Vector3(a.x * d, a.y * d, a.z * d); }
        public static Vector3 operator /(Vector3 a, float d) { return new Vector3(a.x / d, a.y / d, a.z / d); }

        // UnityEngine semantics: approximate equality (kEpsilon = 1e-5).
        public static bool operator ==(Vector3 a, Vector3 b) { return (a - b).sqrMagnitude < 9.99999944E-11f; }
        public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
        public override bool Equals(object other) { return other is Vector3 && this == (Vector3)other; }
        public override int GetHashCode() { return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2); }

        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public float magnitude { get { return Mathf.Sqrt(x * x + y * y + z * z); } }

        public Vector3 normalized {
            get {
                float m = magnitude;
                return m > 1E-05f ? this / m : zero;
            }
        }

        public static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }

        public static Vector3 Cross(Vector3 a, Vector3 b) {
            return new Vector3(a.y * b.z - a.z * b.y,
                               a.z * b.x - a.x * b.z,
                               a.x * b.y - a.y * b.x);
        }

        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) {
            t = Mathf.Clamp01(t);
            return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) {
            return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        public override string ToString() { return string.Format("({0:F3}, {1:F3}, {2:F3})", x, y, z); }
    }

    public struct Quaternion {
        public float x, y, z, w;

        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public static Quaternion identity { get { return new Quaternion(0f, 0f, 0f, 1f); } }

        // Hamilton product, Unity convention (q1*q2 applies q2 first).
        public static Quaternion operator *(Quaternion a, Quaternion b) {
            return new Quaternion(
                a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
                a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x,
                a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        }

        // Rotate a vector: q * v * q^-1 (unit quaternion fast form).
        public static Vector3 operator *(Quaternion q, Vector3 v) {
            Vector3 u = new Vector3(q.x, q.y, q.z);
            Vector3 t = 2f * Vector3.Cross(u, v);
            return v + t * q.w + Vector3.Cross(u, t);
        }

        public static Quaternion Inverse(Quaternion q) {
            float n = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (n <= 0f) {
                return identity;
            }
            float inv = 1f / n;
            return new Quaternion(-q.x * inv, -q.y * inv, -q.z * inv, q.w * inv);
        }

        // Unity applies the world-axis rotations in Z, X, Y order: q = qy*qx*qz.
        public static Quaternion Euler(float xDeg, float yDeg, float zDeg) {
            float hx = xDeg * Mathf.Deg2Rad * 0.5f;
            float hy = yDeg * Mathf.Deg2Rad * 0.5f;
            float hz = zDeg * Mathf.Deg2Rad * 0.5f;
            Quaternion qx = new Quaternion(Mathf.Sin(hx), 0f, 0f, Mathf.Cos(hx));
            Quaternion qy = new Quaternion(0f, Mathf.Sin(hy), 0f, Mathf.Cos(hy));
            Quaternion qz = new Quaternion(0f, 0f, Mathf.Sin(hz), Mathf.Cos(hz));
            return qy * qx * qz;
        }

        public static Quaternion Euler(Vector3 deg) { return Euler(deg.x, deg.y, deg.z); }

        public override string ToString() { return string.Format("({0:F5}, {1:F5}, {2:F5}, {3:F5})", x, y, z, w); }
    }

    public static class Mathf {
        public const float PI = 3.14159274f;             // UnityEngine.Mathf.PI
        public const float Rad2Deg = 57.29578f;          // UnityEngine.Mathf.Rad2Deg
        public const float Deg2Rad = 0.0174532924f;      // UnityEngine.Mathf.Deg2Rad

        public static float Sqrt(float f) { return (float)Math.Sqrt(f); }
        public static float Exp(float f) { return (float)Math.Exp(f); }
        public static float Cos(float f) { return (float)Math.Cos(f); }
        public static float Sin(float f) { return (float)Math.Sin(f); }
        public static float Abs(float f) { return Math.Abs(f); }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Min(float a, float b) { return a < b ? a : b; }

        // UnityEngine semantics: Sign(0) == 1.
        public static float Sign(float f) { return f >= 0f ? 1f : -1f; }

        public static float Clamp01(float f) { return f < 0f ? 0f : (f > 1f ? 1f : f); }
        public static float Clamp(float f, float lo, float hi) { return f < lo ? lo : (f > hi ? hi : f); }

        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
    }
}
