using HarmonyLib;
using Overload;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;

namespace GameMod
{
    class FramerateLimiter
    {
        public static int target_framerate = 0;
        public const int maximum_framerate = 999;
        static string typed_framerate;
        static Vector2 number_position;

        // Insert the framelimiter menu option
        [HarmonyPatch(typeof(UIElement), "DrawGraphicsMenu")]
        class FrameLimiter_DrawGraphicsMenu
        {
            public static void DrawFrameLimiterSlider(UIElement uie, ref Vector2 position)
            {
                position.y += 62f;
                number_position = position + Vector2.right * 90f;
                Menus_UIElement_DrawMpOptions.SelectAndDrawSliderItem(uie, "FRAMERATE LIMIT", position, 282, target_framerate, maximum_framerate,
                    "SETS THE MAXIMUM FRAMERATE (0 = NO LIMIT). CLICK THE NUMBER OR TYPE DIGITS TO ENTER IT. THIS WILL BE OVERRIDDEN WHEN VSYNC IS ACTIVE",
                    value_text: typed_framerate == null ? null : typed_framerate.Length < 3 ? typed_framerate + "_" : typed_framerate);

            }


            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
            {
                int state = 0;
                foreach (var code in codes)
                {
                    // 1. adjust the spacing
                    if (state == 0 && code.opcode == OpCodes.Ldc_R4)
                    {
                        state = 1;
                        yield return new CodeInstruction(OpCodes.Ldc_R4, 19f);
                        continue;
                    }
                    if (state == 1 && code.opcode == OpCodes.Call && code.operand == AccessTools.Method(typeof(UIElement), "DrawHeaderMedium"))
                    {
                        state = 2;
                        yield return code;
                        yield return new CodeInstruction(OpCodes.Ldloca_S, 0);
                        yield return new CodeInstruction(OpCodes.Dup);
                        yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(UnityEngine.Vector2), "y"));
                        yield return new CodeInstruction(OpCodes.Ldc_R4, 55f);
                        yield return new CodeInstruction(OpCodes.Sub);
                        yield return new CodeInstruction(OpCodes.Stfld, AccessTools.Field(typeof(UnityEngine.Vector2), "y"));
                        continue;
                    }

                    // 2. start after the description of the vsync option
                    if (state == 2 && code.opcode == OpCodes.Ldstr && (string)code.operand == "SYNC GAME DRAWING TO MONITOR REFRESH RATE")
                        state = 3;

                    // 3. insert the hook for drawing the new option
                    if (state == 3 && code.opcode == OpCodes.Call && code.operand == AccessTools.Method(typeof(UIElement), "SelectAndDrawStringOptionItem"))
                    {
                        state = 4;
                        yield return code;
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Ldloca_S, 0);
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FrameLimiter_DrawGraphicsMenu), "DrawFrameLimiterSlider"));
                        continue;
                    }

                    yield return code;
                }
            }
        }

        
        [HarmonyPatch(typeof(MenuManager), "GraphicsOptionsUpdate")]
        class FrameLimiter_GraphicsOptionsUpdate
        {
            public static void UpdateFrameLimiterSlider(int menu_selection)
            {
                if (menu_selection != 282 && typed_framerate != null)
                    ApplyTypedFramerate();
                switch (menu_selection)
                {
                    case 282:
                        if (typed_framerate != null)
                        {
                            EditTypedFramerate();
                            break;
                        }
                        if (Input.GetMouseButtonDown(0) && IsMouseOnNumber())
                        {
                            typed_framerate = "";
                            break;
                        }
                        string first_digit = FirstDigit(Input.inputString);
                        if (first_digit != null)
                        {
                            typed_framerate = first_digit;
                            break;
                        }
                        if (MenuManager.option_dir && UIManager.PushedDir()){
                            target_framerate += UIManager.m_select_dir;
                            MenuManager.PlayCycleSound(1f, (float)UIManager.m_select_dir);
                            uConsole.Log("Set the target framerate to " + FramerateLimiter.target_framerate);
                        }
                        else{
                            if (Input.GetMouseButtonUp(0)){
                                target_framerate = (int)(UIElement.SliderPos * maximum_framerate);
                                MenuManager.PlaySelectSound(1f);
                                uConsole.Log("Set the target framerate to " + FramerateLimiter.target_framerate);
                            }
                            else if (Input.GetMouseButton(0))
                                target_framerate = (int)(UIElement.SliderPos * maximum_framerate);
                        }
                        break;
                }
            }

            static void EditTypedFramerate()
            {
                foreach (char c in Input.inputString)
                {
                    if (c == '\b' && typed_framerate.Length > 0)
                        typed_framerate = typed_framerate.Substring(0, typed_framerate.Length - 1);
                    else if (c == '\n' || c == '\r')
                    {
                        ApplyTypedFramerate();
                        return;
                    }
                    else if (c >= '0' && c <= '9' && typed_framerate.Length < 3)
                        typed_framerate += c;
                }
                if (Input.GetMouseButtonDown(0) && !IsMouseOnNumber())
                    ApplyTypedFramerate();
            }

            static void ApplyTypedFramerate()
            {
                int typed;
                if (int.TryParse(typed_framerate, out typed))
                {
                    target_framerate = Mathf.Clamp(typed, 0, maximum_framerate);
                    uConsole.Log("Set the target framerate to " + target_framerate);
                }
                typed_framerate = null;
                MenuManager.PlaySelectSound(1f);
            }

            static bool IsMouseOnNumber()
            {
                return UIManager.m_menu_use_mouse && new Rect(number_position.x - 35f, number_position.y - 22f, 70f, 44f).Contains(UIManager.m_mouse_pos);
            }

            static string FirstDigit(string input)
            {
                foreach (char c in input)
                    if (c >= '0' && c <= '9')
                        return c.ToString();
                return null;
            }

            public static void Postfix()
            {
                UpdateFrameLimiterSlider(UIManager.m_menu_selection);
            }
        }
    }

    // Adds the ability to set the max number of frames queued for rendering. Saved in pilot config, but not present in menu for now. May change in future.
    [HarmonyPatch(typeof(GameManager), "Start")]
    public static class FrameQueueControl_GameManager_Start
    {
        public static void Postfix()
        {
            uConsole.RegisterCommand("maxqueue", "sets the QualitySettings.maxQueuedFrames amount (default 2)", new uConsole.DebugCommand(CmdSetMaxQueuedFrames));
        }

        public static void CmdSetMaxQueuedFrames()
        {
            QualitySettings.maxQueuedFrames = uConsole.GetInt();

            Debug.LogFormat("[Perf] maxQueuedFrames set to {0}", QualitySettings.maxQueuedFrames);
        }
    }
}
