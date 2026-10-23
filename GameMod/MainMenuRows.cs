using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace GameMod
{
    // The rows olmod adds to the stock main menu, drawn under PLAY MULTIPLAYER in
    // registration order. This owns only where the rows sit -- each feature keeps its
    // own MainMenuUpdate postfix for what its row does.
    public static class MainMenuRows
    {
        private class Row
        {
            public string Label;
            public int Selection;
        }

        private static readonly List<Row> rows = new List<Row>();
        private static bool transpilerApplied;

        public static void Add(string label, int selection)
        {
            rows.Add(new Row { Label = label, Selection = selection });
        }

        // The stock 8 rows at 62 px span 434 px; keeping that span leaves the separator
        // and the discord row below it where vanilla put them. Stop shrinking once a row
        // is as tall as its own 48 px mouse rect -- below that neighbouring rows fight
        // over the same clicks.
        public static float Spacing
        {
            get { return Mathf.Max(434f / (7 + rows.Count), 48f); }
        }

        public static void Draw(UIElement ui, ref Vector2 position)
        {
            foreach (Row row in rows)
            {
                ui.SelectAndDrawItem(Loc.LS(row.Label), position, row.Selection, false);
                position.y += Spacing;
            }
        }

        // Rewrites DrawMainMenu to advance by Spacing and to draw the registered rows
        // where the stock stack ends. Hands back the untouched IL if either anchor is gone.
        public static IEnumerable<CodeInstruction> InsertRows(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            int seedIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R4 && codes[i].operand is float &&
                    (float)codes[i].operand == -155f) // the stock menu stack's top y seeds the layout
                {
                    if (seedIndex >= 0) { seedIndex = -1; break; }
                    seedIndex = i;
                }
            }

            var outlineMethod = AccessTools.Method(typeof(UIElement), "SelectAndDrawItemOutline");
            int storeIndex = -1;
            CodeInstruction positionLoad = null;
            for (int i = 0; i < codes.Count && storeIndex < 0; i++)
            {
                if (!(codes[i].opcode == OpCodes.Call || codes[i].opcode == OpCodes.Callvirt) ||
                    !Equals(codes[i].operand, outlineMethod))
                    continue;
                bool isMultiplayerItem = false;
                for (int j = Math.Max(0, i - 8); j < i; j++)
                    if (codes[j].opcode == OpCodes.Ldc_I4_7)
                        isMultiplayerItem = true;
                if (!isMultiplayerItem)
                    continue;
                for (int j = i + 1; j < Math.Min(codes.Count, i + 14); j++)
                {
                    if (codes[j].opcode == OpCodes.Ldloca || codes[j].opcode == OpCodes.Ldloca_S)
                        positionLoad = codes[j];
                    bool isStore = codes[j].opcode == OpCodes.Stind_R4 ||
                        (codes[j].opcode == OpCodes.Stfld && codes[j].operand is FieldInfo &&
                         ((FieldInfo)codes[j].operand).Name == "y");
                    if (isStore)
                    {
                        if (positionLoad != null)
                            storeIndex = j;
                        break;
                    }
                }
                break;
            }

            if (seedIndex < 0 || storeIndex < 0)
            {
                Debug.LogWarning("MainMenuRows: DrawMainMenu anchors not found, falling back to half-items");
                transpilerApplied = false;
                return instructions;
            }

            foreach (var code in codes)
                if (code.opcode == OpCodes.Ldc_R4 && code.operand is float && (float)code.operand == 62f)
                {
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.PropertyGetter(typeof(MainMenuRows), nameof(MainMenuRows.Spacing));
                }
            codes.InsertRange(storeIndex + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(positionLoad.opcode, positionLoad.operand),
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(MainMenuRows), nameof(MainMenuRows.Draw)))
            });
            transpilerApplied = true;
            return codes;
        }

        // Nothing to do once the rows are in the stack; this is the half-item fallback
        // for a DrawMainMenu the transpiler could not read.
        public static void DrawFallback(UIElement ui)
        {
            if (transpilerApplied)
                return;
            Vector2 position = new Vector2(500f, 217f);
            foreach (Row row in rows)
            {
                ui.SelectAndDrawHalfItem(Loc.LS(row.Label), position, row.Selection, false);
                position.y += 40f;
            }
        }
    }
}
