using HarmonyLib;
using Overload;
using Rewired;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameMod
{
    class Controllers
    {
        public static string m_serialized_data = String.Empty;
        public static float m_menu_sensitivity;
        public static float m_menu_deadzone;
        public static List<Controller> controllers = new List<Controller>();

        public class Controller
        {
            public bool populated = false;
            public string m_device_name = "";
            public int m_joystick_id = -1;
            public List<Axis> axes = new List<Axis>();

            public class Axis
            {
                public float sensitivity = 1;
                public float deadzone = 1;
            }
        }

        public static void SetAxisSensitivity(int controller, int axis, float sensitivity)
        {
            Overload.Controller controller1 = Controls.m_controllers[controller];
            if (axis == -1)
            {
                for (axis = 0; axis < controller1.m_joystick.axisCount; axis++)
                {
                    SetAxisSensitivity(controller, axis, sensitivity);
                }
                return;
            }
            if (controller1.isConnected)
            {
                float sens = (float)sensitivity;
                sens *= 0.0145f;
                sens += 0.75f;
                controller1.m_joystick.calibrationMap.GetAxis(axis).sensitivity = sens;
                controller1.m_joystick.calibrationMap.GetAxis(axis).sensitivityType = Rewired.AxisSensitivityType.Multiplier;
                Controllers.controllers[controller].axes[axis].sensitivity = sensitivity;
            }
        }

        public static void SetAxisDeadzone(int controller, int axis, float dz_index)
        {
            Overload.Controller controller1 = Controls.m_controllers[controller];
            if (axis == -1)
            {
                for (axis = 0; axis < controller1.m_joystick.axisCount; axis++)
                {
                    SetAxisDeadzone(controller, axis, dz_index);
                }
                return;
            }
            if (controller1.isConnected)
            {
                controller1.m_joystick.calibrationMap.GetAxis(axis).deadZone = (float)dz_index / 200f;
                Controllers.controllers[controller].axes[axis].deadzone = dz_index;
            }
        }
    }

    [HarmonyPatch(typeof(UIElement), "DrawControlsMenu")]
    class Controllers_UIElement_DrawControlsMenu
    {
        static void DrawItem(UIElement uie, ref Vector2 position)
        {
            Controllers.m_menu_sensitivity = Controllers.controllers[MenuManager.m_calibration_current_controller].axes[MenuManager.m_calibration_current_axis].sensitivity;
            Controllers.m_menu_deadzone = Controllers.controllers[MenuManager.m_calibration_current_controller].axes[MenuManager.m_calibration_current_axis].deadzone;
            uie.SelectAndDrawSliderItem(Loc.LS("AXIS SENSITIVITY"), position, 7, (float)Controllers.m_menu_sensitivity / 100f);
            position.y += 62f;
            uie.SelectAndDrawSliderItem(Loc.LS("AXIS DEADZONE"), position, 8, (float)Controllers.m_menu_deadzone / 100f);
            position.y += 62f;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
        {

            int state = 0;
            foreach (var code in codes)
            {
                if (code.opcode == OpCodes.Ldstr && (string)code.operand == "AXIS SENSITIVITY")
                    state = 1; // Start code block skip at Axis Sensitivity

                if (state == 1 && code.opcode == OpCodes.Ldstr && (string)code.operand == "APPLY TO ALL AXES")
                {
                    state = 2; // End code block skip
                    yield return new CodeInstruction(OpCodes.Ldloca, 0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Controllers_UIElement_DrawControlsMenu), "DrawItem"));
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                }
                else if (state == 1)
                {
                    continue;
                }

                yield return code;
            }
        }
    }

    [HarmonyPatch(typeof(MenuManager), "ControlsOptionsUpdate")]
    class Controllers_MenuManager_ControlsOptionsUpdate
    {

        static void PatchMenu()
        {
            Controllers.m_menu_sensitivity = Controllers.controllers[MenuManager.m_calibration_current_controller].axes[MenuManager.m_calibration_current_axis].sensitivity;
            Controllers.m_menu_deadzone = Controllers.controllers[MenuManager.m_calibration_current_controller].axes[MenuManager.m_calibration_current_axis].deadzone;
            switch (UIManager.m_menu_selection)
            {
                case 7:
                    if (UIManager.m_menu_use_mouse)
                    {
                        if (UIElement.SliderValid)
                        {
                            Controllers.m_menu_sensitivity = (int)((double)UIElement.SliderPos * 100.0);
                            Controllers.controllers[MenuManager.m_calibration_current_controller].axes[MenuManager.m_calibration_current_axis].sensitivity = Controllers.m_menu_sensitivity;
                            Controllers.SetAxisSensitivity(MenuManager.m_calibration_current_controller, MenuManager.m_calibration_current_axis, Controllers.m_menu_sensitivity);
                            if (Input.GetMouseButtonDown(0))
                            {
                                MenuManager.PlayCycleSound(1f, (float)((double)UIElement.SliderPos * 5.0 - 3.0));
                                break;
                            }
                            break;
                        }
                        break;
                    }
                    Controllers.m_menu_sensitivity = MenuManager.AdjustSensitivity(UIManager.m_select_dir, (int)Controllers.m_menu_sensitivity);
                    Controllers.controllers[MenuManager.m_calibration_current_controller].axes[MenuManager.m_calibration_current_axis].sensitivity = Controllers.m_menu_sensitivity;
                    Controllers.SetAxisSensitivity(MenuManager.m_calibration_current_controller, MenuManager.m_calibration_current_axis, Controllers.m_menu_sensitivity);
                    MenuManager.PlayCycleSound(1f, (float)UIManager.m_select_dir);
                    break;
                case 8:
                    if (UIManager.m_menu_use_mouse)
                    {
                        if (UIElement.SliderValid)
                        {
                            Controllers.m_menu_deadzone = (int)((double)UIElement.SliderPos * 100.0);
                            Controllers.controllers[MenuManager.m_calibration_current_controller].axes[MenuManager.m_calibration_current_axis].deadzone = Controllers.m_menu_deadzone;
                            Controllers.SetAxisDeadzone(MenuManager.m_calibration_current_controller, MenuManager.m_calibration_current_axis, Controllers.m_menu_deadzone);
                            if (Input.GetMouseButtonDown(0))
                            {
                                MenuManager.PlayCycleSound(1f, (float)((double)UIElement.SliderPos * 5.0 - 3.0));
                                break;
                            }
                            break;
                        }
                        break;
                    }
                    Controllers.m_menu_deadzone = MenuManager.AdjustSensitivity(UIManager.m_select_dir, (int)Controllers.m_menu_deadzone);
                    Controllers.controllers[MenuManager.m_calibration_current_controller].axes[MenuManager.m_calibration_current_axis].deadzone = Controllers.m_menu_deadzone;
                    Controllers.SetAxisDeadzone(MenuManager.m_calibration_current_controller, MenuManager.m_calibration_current_axis, Controllers.m_menu_deadzone);
                    MenuManager.PlayCycleSound(1f, (float)UIManager.m_select_dir);
                    break;
                case 6:
                    Controllers.SetAxisSensitivity(MenuManager.m_calibration_current_controller, -1, Controllers.m_menu_sensitivity);
                    Controllers.SetAxisDeadzone(MenuManager.m_calibration_current_controller, -1, Controllers.m_menu_deadzone);
                    break;
            }
        }

        // SET DEFAULT CALIBRATION (vanilla ResetAxisCalibration) makes Rewired reload the hardware
        // default calibration, which also overwrites the axis deadzone and sensitivity with the
        // hardware defaults. olmod owns those two values, so re-apply them afterwards - otherwise
        // the axis silently runs on a deadzone/sensitivity that neither the sliders nor the config
        // files show until the next pilot load.
        static void ReapplyAxisSettings()
        {
            Controllers.Controller.Axis axis = Controllers.controllers[MenuManager.m_calibration_current_controller].axes[MenuManager.m_calibration_current_axis];
            Controllers.SetAxisSensitivity(MenuManager.m_calibration_current_controller, MenuManager.m_calibration_current_axis, axis.sensitivity);
            Controllers.SetAxisDeadzone(MenuManager.m_calibration_current_controller, MenuManager.m_calibration_current_axis, axis.deadzone);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
        {
            int state = 0;
            foreach (var code in codes)
            {
                if (code.opcode == OpCodes.Callvirt && code.operand == AccessTools.Method(typeof(Overload.Controller), "SetAxisSensitivity"))
                {
                    state++;
                    if (state == 2)
                    {
                        yield return code;
                        yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(MenuManager), "m_calibration_current_controller")); // Current controller
                        yield return new CodeInstruction(OpCodes.Ldc_I4, -1); // All axes
                        yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(Controllers), "m_menu_sensitivity"));
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Controllers), "SetAxisSensitivity"));
                        continue;
                    }
                }

                if (code.opcode == OpCodes.Callvirt && code.operand == AccessTools.Method(typeof(Overload.Controller), "SetAxisDeadzone"))
                {
                    state++;
                    if (state == 4)
                    {
                        yield return code;
                        yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(MenuManager), "m_calibration_current_controller")); // Current controller
                        yield return new CodeInstruction(OpCodes.Ldc_I4, -1); // All axes
                        yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(Controllers), "m_menu_deadzone"));
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Controllers), "SetAxisDeadzone"));
                        continue;
                    }
                }

                if (code.opcode == OpCodes.Callvirt && code.operand == AccessTools.Method(typeof(Overload.Controller), "ResetAxisCalibration"))
                {
                    yield return code;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Controllers_MenuManager_ControlsOptionsUpdate), "ReapplyAxisSettings"));
                    continue;
                }

                if (code.opcode == OpCodes.Stloc_S && ((LocalBuilder)code.operand).LocalType == typeof(Overload.Controller))
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Controllers_MenuManager_ControlsOptionsUpdate), "PatchMenu"));
                }
                yield return code;
            }
        }
    }

    [HarmonyPatch(typeof(PilotManager), "Select", new Type[] { typeof(string) })]
    class Controllers_PilotManager_Select
    {

        public static void ReadControlData()
        {
            Controllers.controllers = new List<Controllers.Controller>();
            string fn = PilotManager.FileName(PilotFileType.CONFIG) + "mod";
            for (int i = 0; i < Controls.m_controllers.Count; i++)
            {
                Controllers.controllers.Add(new Controllers.Controller
                {
                    m_device_name = Controls.m_controllers[i].name,
                    m_joystick_id = Controls.m_controllers[i].joystickID,
                    axes = new List<Controllers.Controller.Axis>()
                });
                for (int j = 0; j < Controls.m_controllers[i].m_axis_count; j++)
                {
                    int dz_index = Controls.m_controllers[i].GetAxisDeadzone(j);
                    int sens_index = Controls.m_controllers[i].GetAxisSensitivity(j);
                    float sens = ((RWInput.sens_multiplier[sens_index] - 0.75f) / 1.45f) * 100f;
                    float deadzone = (Controls.DEADZONE_ADDITIONAL[dz_index] / 0.5f) * 100f;

                    Controllers.controllers[i].axes.Add(new Controllers.Controller.Axis()
                    {
                        sensitivity = sens,
                        deadzone = deadzone
                    });
                }
            }
            Controllers.m_serialized_data = Platform.ReadTextUserData(fn);
            if (Controllers.m_serialized_data == null)
            {
                Controllers.m_serialized_data = String.Empty;
                Debug.Log("[Controls] no existing .xconfigmod file");
                return;
            }
            using (MemoryStream memoryStream = new MemoryStream(Encoding.ASCII.GetBytes(Controllers.m_serialized_data)))
            {
                using (StreamReader streamReader = new StreamReader(memoryStream))
                {
                    ReadControlDataFromStream(streamReader);
                }
            }
        }

        // returns the index of a controller in Overload.Controls.m_controllers
        static int FindControllerIndex(string controller_name, int device_id, int skip_first_n_occurences = 0)
        {
            int index = -1;
            for(int i = 0; i < Overload.Controls.m_controllers.Count; i++){
                if(Overload.Controls.m_controllers[i].name.Equals(controller_name) && Overload.Controls.m_controllers[i].joystickID == device_id)
                {
                    if (skip_first_n_occurences <= 0)
                    {
                        index = i;
                        break;
                    }
                    else
                        skip_first_n_occurences--;
                }
            }
            return index;
        }

        private static List<Controllers.Controller> ReadInDevicesAndReturnThemInAList(StreamReader sr, int numControllers)
        {
            Debug.LogFormat("[Controls] loading {0} devices from .xconfigmod", numControllers);
            List<Controllers.Controller> all_controllers = new List<Controllers.Controller>();
            for (int i = 0; i < numControllers; i++)
            {
                // create a blank device
                Controllers.Controller device = new Controllers.Controller();
                
                // read in the device name and id
                string[] device_informations = sr.ReadLine().Split(';');
                device.m_device_name = device_informations[0];
                device.m_joystick_id = -1;
                if (device_informations.Length == 2)
                {
                    int.TryParse(device_informations[1], out device.m_joystick_id);
                }
                /*
                Debug.Log(string.Format("[{0,-35}] id: {1,2} controller at this position in Overload.Controls.m_controllers: {2, -35} id: {3,9}",
                    device.m_device_name,
                    device.m_joystick_id,
                    i < Controls.m_controllers.Count ? Controls.m_controllers[i].name : "NO DEVICE",
                    i < Controls.m_controllers.Count ? Controls.m_controllers[i].joystickID.ToString() : "NO DEVICE"
                    ));*/


                // read in the amount of axes that this saved device has
                int numAxes = int.Parse(sr.ReadLine());
                // populate all the axes with default values
                for (int j = 0; j < numAxes; j++)
                {
                    // create a default axis if the axes count doesnt match
                    if (j >= device.axes.Count)
                    {
                        // default values, deadzone used to be 40 but that is a very bad default so this should help newcomers
                        float sens = 17.24138f;
                        float deadzone = 5f;

                        // if there is a corresponding controller and axis then convert and use its sens/deadzone
                        int device_pos = FindControllerIndex(device.m_device_name, device.m_joystick_id);
                        if (device_pos != -1 && j < Controls.m_controllers[device_pos].m_axis_count)
                        {
                            int dz_index = Controls.m_controllers[device_pos].GetAxisDeadzone(j);
                            int sens_index = Controls.m_controllers[device_pos].GetAxisSensitivity(j);
                            sens = ((RWInput.sens_multiplier[sens_index] - 0.75f) / 1.45f) * 100f;
                            deadzone = (Controls.DEADZONE_ADDITIONAL[dz_index] / 0.5f) * 100f;
                        }

                        device.axes.Add(new Controllers.Controller.Axis()
                        {
                            sensitivity = sens,
                            deadzone = deadzone
                        });
                    }


                    device.axes[j].deadzone = float.Parse(sr.ReadLine(), CultureInfo.InvariantCulture);
                    device.axes[j].sensitivity = float.Parse(sr.ReadLine(), CultureInfo.InvariantCulture);
                    //Debug.Log("  created axis: " + j + "   sensitivity: " + device.axes[j].sensitivity + "   deadzone: " + device.axes[j].deadzone);
                }

                all_controllers.Add(device);
            }
            return all_controllers;
        }

     
        static void ReadControlDataFromStream(StreamReader sr)
        {
            //Debug.Log("\n--------- SENSITIVITIES & DEADZONES ---------");
            string text = sr.ReadLine(); // 1
            if (!RUtility.StringStartsWith(text, Controls.CONFIG_KEY))
            {
                Debug.LogErrorFormat("[Controls] .xconfigmod has an incorrect key: {0}", text);
                return;
            }

            int num; // config key
            bool flag = int.TryParse(text.Substring(Controls.CONFIG_KEY.Length), out num);
            int numControllers = int.Parse(sr.ReadLine()); // amount of saved controllers in file
            int[] controllers = new int[numControllers];

            // this holds all the saved devices till they get ordered and matched to Overload.Controls.m_controllers
            List<Controllers.Controller> all_controllers = ReadInDevicesAndReturnThemInAList(sr, numControllers);

            // this holds the different types of connected devices to differentiate between for example a Sidewinder 3D pro and a T.16000
            Dictionary<string, int> controller_types = new Dictionary<string, int>();


            List<Controllers.Controller> unassigned_controllers = new List<Controllers.Controller>();

            // match the devices
            foreach (var device in all_controllers)
            {
                // add this device name to controller_types once
                if (!controller_types.ContainsKey(device.m_device_name))
                    controller_types.Add(device.m_device_name, 0);


                if (device.m_joystick_id != -1)
                {
                    int index = Controls.m_controllers.FindIndex((Overload.Controller c) => c.name.Equals(device.m_device_name) && c.joystickID == device.m_joystick_id);
                    //Debug.Log("[1S] Matching devices: (" + device.m_device_name + ":" + device.m_joystick_id + ") to " + (index == -1 ? "UNCONNECTED" : index.ToString()));
                    
                    if (index == -1)
                    {
                        unassigned_controllers.Add(device);
                    } 
                    else
                    {
                        Controllers.controllers[index] = device;
                        Controllers.controllers[index].populated = true;
                    }
                        
                }
                else
                {
                    // ensure that there is sth that this device can be matched to
                    int index = Controls.m_controllers.FindIndex((Overload.Controller c) => c.name.Equals(device.m_device_name));
                    if (index != -1)
                    {
                        // device has no id and there are no other devices with ids either 
                        if (all_controllers.Find((Controllers.Controller c) => c.m_device_name == Controls.m_controllers[index].name && c.m_joystick_id == Controls.m_controllers[index].joystickID && c.m_joystick_id != -1) == null)
                        {
                            int[] matching_device_indexes = FindControllerIndexes(device.m_device_name, device.m_joystick_id, false);
                            if (matching_device_indexes.Length > 0 && matching_device_indexes.Length > controller_types[device.m_device_name])
                            {
                                device.populated = true;
                                Controllers.controllers[matching_device_indexes[controller_types[device.m_device_name]]] = device;
                                //Debug.Log("[2S] Matching devices: (" + device.m_device_name + ":" + device.m_joystick_id + ") to " + (index == -1 ? "UNCONNECTED" : matching_device_indexes[controller_types[device.m_device_name]].ToString()));
                            }
                            controller_types[device.m_device_name]++;
                        }
                    }
                }
            }
            
            foreach(Controllers.Controller c in unassigned_controllers)
            {
                for(int i = 0; i < Controllers.controllers.Count; i++)
                {
                    if(!Controllers.controllers[i].populated && Controllers.controllers[i].m_device_name.Equals(c.m_device_name))
                    {
                        //Debug.Log("  readded controller: " + c.m_device_name+ " at "+i);
                        Controllers.controllers[i] = c;
                        Controllers.controllers[i].populated = true;
                    }
                }

            }

            //Debug.Log(" ");
            for (int i = 0; i < Controls.m_controllers.Count; i++)
            {
                //Debug.Log("Device");
                for(int j = 0; j < Controllers.controllers[i].axes.Count; j++)
                {
                    if (j < Controllers.controllers[i].axes.Count)
                    {
                        //Debug.Log("   axis:"+j+" sens:"+ Controllers.controllers[i].axes[j].sensitivity+" dead:"+ Controllers.controllers[i].axes[j].deadzone);
                        Controllers.SetAxisDeadzone(i, j, Controllers.controllers[i].axes[j].deadzone);
                        Controllers.SetAxisSensitivity(i, j, Controllers.controllers[i].axes[j].sensitivity);
                    }

                }
            }


            int enums_read = 0;
            // Read any new bindings that are past the original CCInput bounds in our pilot .xconfigmod 
            while (!sr.EndOfStream)
            {
                enums_read++;
                text = sr.ReadLine();
                if (sr.EndOfStream)
                {
                    break;
                }
                try
                {
                    CCInputExt ccinput = (CCInputExt)Enum.Parse(typeof(CCInputExt), text);
                    if (ccinput != CCInputExt.NUM)
                    {
                        Controls.m_input_joy[0, (int)ccinput].Read(sr, num);
                        Controls.m_input_joy[1, (int)ccinput].Read(sr, num);
                        Controls.m_input_kc[0, (int)ccinput] = (KeyCode)int.Parse(sr.ReadLine());
                        Controls.m_input_kc[1, (int)ccinput] = (KeyCode)int.Parse(sr.ReadLine());
                        if (Controls.m_input_joy[0, (int)ccinput].m_controller_num >= numControllers)
                        {
                            Controls.m_input_joy[0, (int)ccinput].m_controller_num = numControllers - 1;
                        }
                        if (Controls.m_input_joy[1, (int)ccinput].m_controller_num >= numControllers)
                        {
                            Controls.m_input_joy[1, (int)ccinput].m_controller_num = numControllers - 1;
                        }
                    }
                }
                catch
                {
                    for (int l = 0; l < 10; l++)
                    {
                        sr.ReadLine();
                    }
                }
            }

            // This indicates that this config file doesnt contain information about the bound taunt keys so we need to initialise them
            if(enums_read < 7)
            {
                for(int i = (int)CCInputExt.TAUNT_1; i <= (int)CCInputExt.TAUNT_6; i++)
                {
                    Controls.m_input_joy[0, i].m_controller_num = -1;
                    Controls.m_input_joy[0, i].m_type = 0;
                    Controls.m_input_joy[0, i].m_control_num = -1;
                    Controls.m_input_joy[0, i].m_axis_pos = false;
                    Controls.m_input_joy[1, i].m_controller_num = -1;
                    Controls.m_input_joy[1, i].m_type = 0;
                    Controls.m_input_joy[1, i].m_control_num = -1;
                    Controls.m_input_joy[1, i].m_axis_pos = false;
                    Controls.m_input_kc[0, i] = 0;
                    Controls.m_input_kc[1, i] = 0;
                }
            }

            for (int m = (int)CCInputExt.TOGGLE_LOADOUT_PRIMARY; m < ControlsExt.MAX_ARRAY_SIZE; m++)
            {
                for (int n = 0; n < 2; n++)
                {
                    if (Controls.m_input_joy[n, m].m_controller_num != -1)
                        Controls.m_input_joy[n, m].m_controller_num = controllers[Controls.m_input_joy[n, m].m_controller_num];
                }
            }
        }

        static int[] FindControllerIndexes(string controller_name, int device_id, bool requires_matching_device_id = true)
        {
            int[] indexes = new int[FindAmountOfControllerIndexes(controller_name, device_id, requires_matching_device_id)];
            int occurence = 0;
            for (int i = 0; i < Overload.Controls.m_controllers.Count; i++)
            {
                if (Overload.Controls.m_controllers[i].name.Equals(controller_name) && (Overload.Controls.m_controllers[i].joystickID == device_id | !requires_matching_device_id))
                {
                    indexes[occurence] = i;
                    occurence++;
                }
            }
            return indexes;
        }


        static int FindAmountOfControllerIndexes(string controller_name, int device_id, bool requires_matching_device_id = true)
        {
            int amount = 0;
            for (int i = 0; i < Overload.Controls.m_controllers.Count; i++)
            {
                if (Overload.Controls.m_controllers[i].name.Equals(controller_name) && (Overload.Controls.m_controllers[i].joystickID == device_id  | !requires_matching_device_id))
                {
                    amount++;
                }
            }
            return amount;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
        {
            foreach (var code in codes)
            {
                if (code.opcode == OpCodes.Call && code.operand == AccessTools.Method(typeof(Platform), "CloseMountPoint"))
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Controllers_PilotManager_Select), "ReadControlData"));
                }

                yield return code;
            }
        }
    }

    [HarmonyPatch(typeof(Controls), "SaveControlData")]
    class Controllers_Controls_SaveControlData
    {

        static void Postfix(string filename)
        {
            string fn = filename + "mod";
            Debug.LogFormat("[Controls] saving {0} devices to {1}", Controls.m_controllers.Count, fn);
            try
            {
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    using (StreamWriter streamWriter = new StreamWriter(memoryStream))
                    {
                        WriteControlDataToStream(streamWriter);
                        streamWriter.Flush();
                        memoryStream.Position = 0L;
                        using (StreamReader streamReader = new StreamReader(memoryStream))
                        {
                            string text = streamReader.ReadToEnd();
                            //if (Controllers.m_serialized_data != text)
                            //{
                            Controllers.m_serialized_data = text;
                            Platform.WriteTextUserData(fn, Controllers.m_serialized_data);
                            //}
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException)
                {
                    Debug.LogError("[Controls] SaveControlData: cannot write the pilot file, insufficient permissions");
                    return;
                }
                Debug.LogException(ex);
                GameManager.DebugOut("Error in ControlsMod.SaveControlData: " + ex.Message, 15f);
            }
        }

        static void WriteControlDataToStream(StreamWriter w)
        {
            //Debug.Log("\n------------- Saving the controller sens/deadzone ------------");
            w.WriteLine(Controls.CONFIG_KEY + Controls.VERSION);
            bool[] array = new bool[Controls.m_controllers.Count];
            array.Populate(true);
            w.WriteLine(Controls.m_controllers.Count);
            for (int i = 0; i < Controls.m_controllers.Count; i++)
            {
                Overload.Controller controller = Controls.m_controllers[i];
                w.WriteLine((!array[i]) ? string.Empty : controller.name + ";" + Controls.m_controllers[i].joystickID);
                if (array[i])
                {
                    w.WriteLine(controller.m_axis_count);
                    for (int j = 0; j < controller.m_axis_count; j++)
                    {
                        //Debug.Log("  axis: "+j+"  sens: "+ Controllers.controllers[i].axes[j].sensitivity+"  deadzone: "+ Controllers.controllers[i].axes[j].deadzone);
                        w.WriteLine(Controllers.controllers[i].axes[j].deadzone.ToStringInvariantCulture());
                        w.WriteLine(Controllers.controllers[i].axes[j].sensitivity.ToStringInvariantCulture());
                    }
                }
            }
            for (int k = (int)CCInputExt.TOGGLE_LOADOUT_PRIMARY; k < ControlsExt.MAX_ARRAY_SIZE; k++)
            {
                CCInputExt ccinput = (CCInputExt)k;
                w.WriteLine(ccinput.ToString());
                Controls.m_input_joy[0, k].Write(w);
                Controls.m_input_joy[1, k].Write(w);
                int num = (int)Controls.m_input_kc[0, k];
                w.WriteLine(num.ToString());
                int num2 = (int)Controls.m_input_kc[1, k];
                w.WriteLine(num2.ToString());
            }
        }
    }

    [HarmonyPatch(typeof(Controls), "OnControllerConnected")]
    class Controllers_Controls_OnControllerConnected
    {

        static void DeadzoneHelper(int controller, int axis)
        {

            //Controllers.SetAxisDeadzone(controller, axis, Controllers.controllers[controller].axes[axis].deadzone);
        }

        static void SensitivityHelper(int controller, int axis)
        {

            //Controllers.SetAxisSensitivity(controller, axis, Controllers.controllers[controller].axes[axis].sensitivity);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
        {
            foreach (var code in codes)
            {
                if (code.opcode == OpCodes.Callvirt && code.operand == AccessTools.Method(typeof(Overload.Controller), "SetAxisDeadzone"))
                {
                    yield return code;
                    yield return new CodeInstruction(OpCodes.Ldloc_3); // Current controller
                    yield return new CodeInstruction(OpCodes.Ldloc_S, 4); // Current axis
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Controllers_Controls_OnControllerConnected), "DeadzoneHelper"));
                    continue;
                }

                if (code.opcode == OpCodes.Callvirt && code.operand == AccessTools.Method(typeof(Overload.Controller), "SetAxisSensitivity"))
                {
                    yield return code;
                    yield return new CodeInstruction(OpCodes.Ldloc_3); // Current controller
                    yield return new CodeInstruction(OpCodes.Ldloc_S, 4); // Current axis
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Controllers_Controls_OnControllerConnected), "SensitivityHelper"));
                    continue;
                }

                yield return code;
            }
        }
    }

    // Generic utility class for controller name remapping
    public class ControllerNameRemapper {
        private static bool configLoaded = false;
        private static Dictionary<string, string> remapValues = new Dictionary<string,string>();


        private static void AddRemap(string origName, string newName)
        {
            if (String.IsNullOrEmpty(origName)) {
                    Debug.LogWarning("[Controls] remap: invalid request with an empty original name ignored");
                    return;
            }
            if (String.IsNullOrEmpty(newName)) {
                // Delete the existing mapping
                if (remapValues.ContainsKey(origName)) {
                    Debug.LogFormat("[Controls] remap: removing {0} -> {1}", origName, remapValues[origName]);
                    remapValues.Remove(origName);
                }
            } else {
                // Add the mapping
                remapValues[origName] = newName;
                Debug.LogFormat("[Controls] remap: will remap '{0}' -> '{1}'", origName, newName);
            }
        }

        private static void AddRemap(string line)
        {
            if (!String.IsNullOrEmpty(line)) {
                if (line[0] == '#') {
                    // ignore comments
                    return;
                }
                int idx = line.IndexOf("->");
                if (idx < 1 || idx > line.Length-3 ) {
                    Debug.LogWarningFormat("[Controls] remap: config option '{0}' is invalid", line);
                    return;
                }
                string origName = line.Substring(0, idx);
                string newName = line.Substring(idx +2, line.Length - idx - 2);
                AddRemap(origName, newName);
            }
        }

        private static void LoadConfig()
        {
            if (configLoaded) {
                return;
            }

            configLoaded = true;
            try {
                string fn = Path.Combine(Application.persistentDataPath, "controllers.remapmod");
                //Debug.LogFormat("ControllerNameRemapper: looking for config file '{0}'", fn);
                if (File.Exists(fn)) {
                    Debug.LogFormat("[Controls] remap: found config file '{0}'", fn);
                    StreamReader sr = new StreamReader(fn, new System.Text.UTF8Encoding());
                    string line;
                    while( (line = sr.ReadLine()) != null) {
                        AddRemap(line);
                    }
                }
            } catch (Exception ex) {
                Debug.LogErrorFormat("[Controls] remap: loading the config failed: {0}", ex.Message);
            }
        }

        public static bool RenameController(string origName, ref string newName)
        {
            LoadConfig();
            //Debug.LogFormat("ControllerNameRemapper: got '{0}'", origName);
            if (remapValues.ContainsKey(origName)) {
                newName = remapValues[origName];
                return true;
            }
            return false;
        }
    }

    /// Patch to apply controller name remapping
    [HarmonyPatch(typeof(Overload.Controller), MethodType.Constructor, new [] { typeof(Joystick) })]
    class ControllerNameRemapper_ControllerCtorRenamePatch {
        private static FieldInfo field_m_name = AccessTools.Field(typeof(Overload.Controller), "m_name");
        private static void Postfix(Overload.Controller __instance) {
            string origName = (string)field_m_name.GetValue(__instance);
            string newName = origName;
            if (ControllerNameRemapper.RenameController(origName, ref newName)) {
                Debug.LogFormat("[Controls] remap: mapping '{0}' -> '{1}'", origName, newName);
                field_m_name.SetValue(__instance, newName);
            }
        }
    }

    /// This replaces RWInput.GetName() to include an additional check before accessing Controls.m_controllers[]
    [HarmonyPatch(typeof(RWInput), "GetName")]
    class Controllers_Overwrite_RWInput_GetName
    {
        private static bool Prefix(RWInput __instance, ref string __result)
        {
            if (__instance.m_control_num < 0 || __instance.m_controller_num < 0)
            {
                __result = string.Empty;
                return false;
            }
            string text = "";
            __result = text;
            if (Controls.m_controllers != null && __instance.m_controller_num < Controls.m_controllers.Count && Controls.m_controllers[__instance.m_controller_num] != null)
            {
                text = Controls.m_controllers[__instance.m_controller_num].GetControlName(__instance.m_type, __instance.m_control_num);
            }
            if (text != null)
            {
                if (text == "touch pad button")
                {
                    text = Loc.LS("Touch Pad Button");
                }
            }
            text = text.Replace("Button", "Btn").Replace("Center", "Cntr").Replace("Throttle Wheel", "Throt Wheel");
            if (__instance.m_type == ControlType.Axis)
            {
                text += ((!__instance.m_axis_pos) ? "-" : "+");
            }
            AtlasIndex0 controlImageIndex = GetControlImageIndex(__instance, text, Controls.m_controllers[__instance.m_controller_num].name);
            if (controlImageIndex != AtlasIndex0.NONE)
            {
                string str = "%%";
                int num = (int)controlImageIndex;
                text = str + num.ToString("D3");
            }
            if (Controls.m_controllers.Count > 1 && __instance.m_controller_num < Controls.m_controllers.Count && Controls.m_controllers[__instance.m_controller_num] != null)
            {
                text = string.Concat(new object[]
                {
                    (!Controls.m_controllers[__instance.m_controller_num].isConnected) ? "X" : "J",
                    __instance.m_controller_num + 1,
                    ": ",
                    text
                });
            }
            __result = text;
            return false;
        }

        private static AtlasIndex0 GetControlImageIndex(RWInput __instance, string control_name, string controller_name)
        {
            bool flag = false;
            string name = Controls.m_controllers[__instance.m_controller_num].name;
            if (name.IndexOf("Xbox") != -1 || name.StartsWith("XInput Gamepad"))
            {
                flag = true;
            }
            else if (name.IndexOf("DualShock") == -1)
            {
                return AtlasIndex0.NONE;
            }
            switch (control_name)
            {
                case "Circle":
                    return AtlasIndex0.PS4_BUTTON01;
                case "Square":
                    return AtlasIndex0.PS4_BUTTON03;
                case "Cross":
                    return AtlasIndex0.PS4_BUTTON00;
                case "Triangle":
                    return AtlasIndex0.PS4_BUTTON02;
                case "X":
                    return AtlasIndex0.XB1_BUTTON03;
                case "Y":
                    return AtlasIndex0.XB1_BUTTON02;
                case "A":
                    return AtlasIndex0.XB1_BUTTON00;
                case "B":
                    return AtlasIndex0.XB1_BUTTON01;
                case "Right Stick Y-":
                    return (!flag) ? AtlasIndex0.PS4_RS00 : AtlasIndex0.XB1_RS00;
                case "Right Stick X+":
                    return ((!flag) ? AtlasIndex0.PS4_RS00 : AtlasIndex0.XB1_RS00) + 1;
                case "Right Stick Y+":
                    return ((!flag) ? AtlasIndex0.PS4_RS00 : AtlasIndex0.XB1_RS00) + 2;
                case "Right Stick X-":
                    return ((!flag) ? AtlasIndex0.PS4_RS00 : AtlasIndex0.XB1_RS00) + 3;
                case "Left Stick Y-":
                    return (!flag) ? AtlasIndex0.PS4_LS00 : AtlasIndex0.XB1_LS00;
                case "Left Stick X+":
                    return ((!flag) ? AtlasIndex0.PS4_LS00 : AtlasIndex0.XB1_LS00) + 1;
                case "Left Stick Y+":
                    return ((!flag) ? AtlasIndex0.PS4_LS00 : AtlasIndex0.XB1_LS00) + 2;
                case "Left Stick X-":
                    return ((!flag) ? AtlasIndex0.PS4_LS00 : AtlasIndex0.XB1_LS00) + 3;
                case "L1":
                    return AtlasIndex0.PS4_L1;
                case "L2+":
                case "L2-":
                case "L2":
                    return AtlasIndex0.PS4_L2;
                case "R1":
                    return AtlasIndex0.PS4_R1;
                case "R2+":
                case "R2-":
                case "R2":
                    return AtlasIndex0.PS4_R2;
                case "Left Shoulder":
                    return AtlasIndex0.XB1_L1;
                case "Left Trigger":
                case "Left Trigger+":
                case "Left Trigger-":
                    return AtlasIndex0.XB1_L2;
                case "Right Shoulder":
                    return AtlasIndex0.XB1_R1;
                case "Right Trigger":
                case "Right Trigger+":
                case "Right Trigger-":
                    return AtlasIndex0.XB1_R2;
                case "Right Stick Btn":
                    return (!flag) ? AtlasIndex0.PS4_RIGHTCLICK : AtlasIndex0.XB1_RIGHTCLICK;
                case "Left Stick Btn":
                    return (!flag) ? AtlasIndex0.PS4_LEFTCLICK : AtlasIndex0.XB1_LEFTCLICK;
                case "D-Pad Down":
                    return (!flag) ? AtlasIndex0.PS4_DPAD00 : AtlasIndex0.XB1_DPAD00;
                case "D-Pad Right":
                    return ((!flag) ? AtlasIndex0.PS4_DPAD00 : AtlasIndex0.XB1_DPAD00) + 1;
                case "D-Pad Up":
                    return ((!flag) ? AtlasIndex0.PS4_DPAD00 : AtlasIndex0.XB1_DPAD00) + 2;
                case "D-Pad Left":
                    return ((!flag) ? AtlasIndex0.PS4_DPAD00 : AtlasIndex0.XB1_DPAD00) + 3;
                case "Options":
                    return AtlasIndex0.PS4_OPTIONS;
                case "Share":
                    return AtlasIndex0.PS4_SHARE;
                case "Start":
                    return (!flag) ? AtlasIndex0.NONE : AtlasIndex0.XB360_START;
                case "Back":
                    return (!flag) ? AtlasIndex0.NONE : AtlasIndex0.XB360_BACK;
                case "View":
                    return AtlasIndex0.XB1_VIEW;
                case "Menu":
                    return AtlasIndex0.XB1_MENU;
            }
            return AtlasIndex0.NONE;
        }
    }
}
