using HarmonyLib;
using Overload;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace GameMod
{
    class LevelGeometryConverter
    {
        public static string DirectoryForParsedFiles = Path.Combine(Application.persistentDataPath, "ParsedLevelData");
        public static string file_extension = ".obj";

        /*
        [HarmonyPatch(typeof(LevelData), "Awake")]
        internal class LevelGeometryConverter_LevelData_Awake
        {
            static void Postfix()
            {

            }
        }*/


        public static void Save()
        {
            Debug.Log("Loaded a level, parsing its vertices");
            ParseLevelData(GameManager.m_level_data);
        }


        public static void ParseLevelData(LevelData lvl)
        {
            if (lvl == null)
            {
                Debug.Log(" level data was null");
            }

            if (!Directory.Exists(DirectoryForParsedFiles))
            {
                Debug.Log("Did not find a directory for the parsed files, creating one at: " + DirectoryForParsedFiles);
                Directory.CreateDirectory(DirectoryForParsedFiles);
            }


            Debug.Log(lvl.m_geometry.FileName);


            try
            {
                string filepath = Path.Combine(DirectoryForParsedFiles, lvl.m_geometry.FileName + file_extension);

                using (StreamWriter w = File.CreateText(filepath))
                {
                    w.WriteLine("o " + lvl.m_geometry.FileName);
                    ParseVertices(lvl.m_geometry.SegmentVerts, w);
                    ParseFaces(lvl.m_geometry, w);
                }
            }
            catch (Exception ex)
            {
                Debug.Log("Error in LevelGeometryConverter: " + ex);
            }


        }

        public static void ParseVertices(Vector3[] verts, StreamWriter w)
        {
            foreach (Vector3 v in verts)
            {
                w.WriteLine("v " + -v.x + " " + v.y + " " + v.z);
            }
        }


        public static bool isFaceValid(int[] face, int vertex_count)
        {
            foreach (int index in face)
            {
                if (index > vertex_count)
                    return false;
            }
            return true;
        }

        public static void ParseFaces(LevelGeometry lvl, StreamWriter w)
        {
            w.WriteLine("\nusemtl Default");
            Debug.Log("Converting faces");

            // create a list of all faces
            List<int[]> faces = new List<int[]>();
            foreach (SegmentData s in lvl.Segments)
            {
                // iterate over all faces of the segment
                for (int i = 0; i < 6; i++)
                {
                    int[] face = {
                        1 + s.VertIndices[SideVertexOrder[i][0]],
                        1 + s.VertIndices[SideVertexOrder[i][1]],
                        1 + s.VertIndices[SideVertexOrder[i][2]],
                        1 + s.VertIndices[SideVertexOrder[i][3]]
                    };
                    if (isFaceValid(face, lvl.SegmentVerts.Length))
                    {
                        faces.Add(face);
                    }
                    else
                    {
                        Debug.Log("Encountered an invalid face: f " + face[0] + " " + face[1] + " " + face[2] + " " + face[3]);
                    }

                }
            }
            Debug.Log(" amount of all faces: " + faces.Count);

            // eliminate all faces that occur more than once
            List<int[]> unique_faces = new List<int[]>();
            int uniques = 0;
            int duplicates = 0;
            foreach (int[] f in faces)
            {
                int amount_of_duplicates = 1 + faces.FindAll(x => x[0] == f[3] && x[1] == f[2] && x[2] == f[1] && x[3] == f[0]).Count;
                if (amount_of_duplicates == 1)
                {
                    uniques++;
                    unique_faces.Add(f);
                }
                else
                {
                    duplicates++;
                }
            }
            Debug.Log(" uniques:    " + uniques + "\n duplicates:   " + duplicates);

            // write the remaining faces back to the file
            foreach (int[] f in unique_faces)
            {
                w.WriteLine("f " + f[0] + " " + f[1] + " " + f[2] + " " + f[3]);
            }
        }



        public static readonly int[][] SideVertexOrder = new int[][]
        {
        new int[]
        {
            7,
            6,
            2,
            3
        },
        new int[]
        {
            0,
            4,
            7,
            3
        },
        new int[]
        {
            0,
            1,
            5,
            4
        },
        new int[]
        {
            2,
            6,
            5,
            1
        },
        new int[]
        {
            4,
            5,
            6,
            7
        },
        new int[]
        {
            3,
            2,
            1,
            0
        }
        };

    }
}
