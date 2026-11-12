using System;
using System.IO;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod
{
    static class FastUserLevelLoad
    {
        [HarmonyPatch(typeof(OverloadLevelConvertSerializer), MethodType.Constructor, new[] { typeof(Stream) })]
        class BufferReads
        {
            static void Prefix(ref Stream readStream)
            {
                if (readStream != null)
                    readStream = new BufferedStream(readStream, 65536);
            }
        }

        [HarmonyPatch(typeof(OverloadLevelConvertSerializer), "SerializeIn_array")]
        class FastArrays
        {
            static bool Prefix(OverloadLevelConvertSerializer __instance, Type elementType, Stream ___m_stream, ref Array __result)
            {
                int size = ElementSize(elementType);
                if (size == 0 || (!GameplayManager.IsDedicatedServer() && elementType != typeof(Color32)))
                    return true;

                int count = __instance.SerializeIn_int32();
                if (count == -1)
                {
                    __result = null;
                    return false;
                }
                var data = new byte[count * size];
                ReadFully(___m_stream, data);
                __result = Decode(elementType, data, count);
                return false;
            }
        }

        static int ElementSize(Type t)
        {
            if (t == typeof(byte)) return 1;
            if (t == typeof(ushort) || t == typeof(short)) return 2;
            if (t == typeof(int) || t == typeof(uint) || t == typeof(float) || t == typeof(Color32)) return 4;
            if (t == typeof(Vector2)) return 8;
            if (t == typeof(Vector3)) return 12;
            if (t == typeof(Vector4) || t == typeof(Color) || t == typeof(Quaternion)) return 16;
            if (t == typeof(BoneWeight)) return 32;
            if (t == typeof(Matrix4x4)) return 64;
            return 0;
        }

        static void ReadFully(Stream stream, byte[] data)
        {
            int done = 0;
            while (done < data.Length)
            {
                int n = stream.Read(data, done, data.Length - done);
                if (n <= 0)
                    throw new SerializationFailureException();
                done += n;
            }
        }

        static float F(byte[] d, int at) { return BitConverter.ToSingle(d, at); }

        static Vector4 V4(byte[] d, int at) { return new Vector4(F(d, at), F(d, at + 4), F(d, at + 8), F(d, at + 12)); }

        static Array Decode(Type t, byte[] d, int count)
        {
            if (t == typeof(byte))
                return (byte[])d.Clone();
            if (t == typeof(ushort) || t == typeof(short) || t == typeof(int) || t == typeof(uint) || t == typeof(float))
            {
                var a = Array.CreateInstance(t, count);
                Buffer.BlockCopy(d, 0, a, 0, d.Length);
                return a;
            }
            if (t == typeof(Vector2))
            {
                var a = new Vector2[count];
                for (int i = 0, at = 0; i < count; i++, at += 8)
                    a[i] = new Vector2(F(d, at), F(d, at + 4));
                return a;
            }
            if (t == typeof(Vector3))
            {
                var a = new Vector3[count];
                for (int i = 0, at = 0; i < count; i++, at += 12)
                    a[i] = new Vector3(F(d, at), F(d, at + 4), F(d, at + 8));
                return a;
            }
            if (t == typeof(Vector4))
            {
                var a = new Vector4[count];
                for (int i = 0; i < count; i++)
                    a[i] = V4(d, i * 16);
                return a;
            }
            if (t == typeof(Color))
            {
                var a = new Color[count];
                for (int i = 0, at = 0; i < count; i++, at += 16)
                    a[i] = new Color(F(d, at), F(d, at + 4), F(d, at + 8), F(d, at + 12));
                return a;
            }
            if (t == typeof(Quaternion))
            {
                var a = new Quaternion[count];
                for (int i = 0, at = 0; i < count; i++, at += 16)
                    a[i] = new Quaternion(F(d, at), F(d, at + 4), F(d, at + 8), F(d, at + 12));
                return a;
            }
            if (t == typeof(Color32))
            {
                var a = new Color32[count];
                for (int i = 0, at = 0; i < count; i++, at += 4)
                    a[i] = new Color32(d[at], d[at + 1], d[at + 2], d[at + 3]);
                return a;
            }
            if (t == typeof(BoneWeight))
            {
                var a = new BoneWeight[count];
                for (int i = 0, at = 0; i < count; i++, at += 32)
                {
                    a[i].boneIndex0 = BitConverter.ToInt32(d, at);
                    a[i].boneIndex1 = BitConverter.ToInt32(d, at + 4);
                    a[i].boneIndex2 = BitConverter.ToInt32(d, at + 8);
                    a[i].boneIndex3 = BitConverter.ToInt32(d, at + 12);
                    a[i].weight0 = F(d, at + 16);
                    a[i].weight1 = F(d, at + 20);
                    a[i].weight2 = F(d, at + 24);
                    a[i].weight3 = F(d, at + 28);
                }
                return a;
            }
            {
                var a = new Matrix4x4[count];
                for (int i = 0, at = 0; i < count; i++, at += 64)
                {
                    a[i].SetRow(0, V4(d, at));
                    a[i].SetRow(1, V4(d, at + 16));
                    a[i].SetRow(2, V4(d, at + 32));
                    a[i].SetRow(3, V4(d, at + 48));
                }
                return a;
            }
        }
    }
}
