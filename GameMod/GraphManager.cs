using HarmonyLib;
using Overload;
using System.Collections.Generic;
using UnityEngine;
using UnityStandardAssets.ImageEffects;
using static GameMod.Graph.RingBuffer;
using static UnityStandardAssets.ImageEffects.BloomOptimized;

/*
Graph.Commands
	// general commands for handling the graph
    graph 		creates a default graph instance
    gshow 		toggles visibility
    gsetorigin 	sets the graph origin to the mouse cursor position
    gsetwidth 	sets the width of the graph
    gsetheight 	sets the height of the graph
    gsetname 	sets the name of the x axis of the graph
    glistds 	lists all currently available decaystructures

	// commands for changing the properties of a single decaystructure
    gdselect 	selects a decaystructure to work on
    gdsetx 		sets which value should be displayed on the x axis
    gdsety 		sets which value should be displayed on the y axis
    gdshow 		toggles the visibility of the selected
    gdcolor		sets the color for the current decaystructure
    gdname 		sets the name for the current decaystructure
*/



namespace GameMod
{

    internal class GraphManager
    {
        public static bool show_graphs_input = false;

        public static Dictionary<string, Graph> graphs = new Dictionary<string, Graph>();

        private static void CreateGraphs()
        {
            // Pitch

            // Yaw 

            // Roll
            if(!graphs.ContainsKey("Framerate"))
            {
                uConsole.Log("111111");

                graphs.Add("Framerate", new Graph(new Vector2(-200, -200), 250, 100, "Framerate"));
                uConsole.Log("222222");
                graphs["Framerate"].AddDataPointToDecayStructure("Framerate", 0);
                uConsole.Log("33333333");

                graphs["Framerate"].draw_relations.Add(new Graph.DrawRelation(42069, "", "Framerate"));
                uConsole.Log("4444444");

            }
            // Framerate

        }

        [HarmonyPatch(typeof(GameplayManager), "LoadLevel")]
        internal class MPClientPredictionDebugReset
        {
            static void Postfix(UIElement __instance)
            {
                GameManager.m_display_fps = true;

                // kill bloom to make the graph readable
                BloomOptimized component2 = GameManager.m_viewer.c_camera.GetComponent<BloomOptimized>();
                if (component2)
                {
                    component2.enabled = false;
                }
                SENaturalBloomAndDirtyLens component3 = GameManager.m_viewer.c_camera.GetComponent<SENaturalBloomAndDirtyLens>();
                if (component3)
                {
                    component3.enabled = false;
                }
            }
        }

        static int counter = 0;
        [HarmonyPatch(typeof(UIElement), "DrawHUD")]
        internal class GraphManager_GameManager_Starttss
        {
            private static void Postfix(UIElement __instance)
            {
                if (counter == 1500)
                    counter = 0;

                CreateGraphs();
                if (GraphManager.graphs.ContainsKey("Framerate"))
                    GraphManager.graphs["Framerate"].AddDataPointToDecayStructure("Framerate", Time.deltaTime);

                if (graphs != null && graphs.ContainsKey("Framerate"))
                {
                    graphs["Framerate"].Draw(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(GameManager), "Start")]
        internal class GraphManager_GameManager_Start
        {
            /*
            private static void Postfix(GameManager __instance)
            {
                // Graph preset
                uConsole.RegisterCommand("toggle_input_graphs", "displays input and output for pitch/yaw/roll", new uConsole.DebugCommand(CmdToggleInputGraphs));

                // general commands for handling the graph
                uConsole.RegisterCommand("graph", "creates a default graph instance", new uConsole.DebugCommand(CmdCreateGraphInstance));
                uConsole.RegisterCommand("gshow", "toggles visibility", new uConsole.DebugCommand(CmdToggleVisibility));
                uConsole.RegisterCommand("gsetorigin", "sets the graph origin to the mouse cursor position", new uConsole.DebugCommand(CmdOriginToMousePos));
                uConsole.RegisterCommand("gsetwidth", "sets the width of the graph", new uConsole.DebugCommand(CmdSetGraphWidth));
                uConsole.RegisterCommand("gsetheight", "sets the height of the graph", new uConsole.DebugCommand(CmdSetGraphHeight));
                uConsole.RegisterCommand("gsetname", "sets the name of the x axis of the graph", new uConsole.DebugCommand(CmdSetGraphName));
                uConsole.RegisterCommand("glistds", "lists all currently available decaystructures", new uConsole.DebugCommand(CmdListDecayStructures));


                // commands for changing the properties of a single decaystructure
                uConsole.RegisterCommand("gdselect", "selects a decaystructure to work on", new uConsole.DebugCommand(CmdSelectDecayStructure));
                uConsole.RegisterCommand("gdsetx", "sets which value should be displayed on the x axis", new uConsole.DebugCommand(CmdSetXAxis));
                uConsole.RegisterCommand("gdsety", "sets which value should be displayed on the y axis", new uConsole.DebugCommand(CmdSetYAxis));
                uConsole.RegisterCommand("gdshow", "toggles the visibility of the selected ", new uConsole.DebugCommand(CmdToggleShowSelectedDecayStructure));
                uConsole.RegisterCommand("gdcolor", "sets the color for the current decaystructure", new uConsole.DebugCommand(CmdSetColor));
                uConsole.RegisterCommand("gdname", "sets the name for the current decaystructure", new uConsole.DebugCommand(CmdSetName));

                // debug commands
                // uConsole.RegisterCommand("genline", "", new uConsole.DebugCommand(CmdLine));
                // uConsole.RegisterCommand("gencurve", "", new uConsole.DebugCommand(CmdCurve));
            }

            private static void CmdToggleInputGraphs()
            {
                
            }

            private static void CmdCreateGraphInstance()
            {
                g = new Graph(new Vector2(532f, -175), 150, 75, "default");
            }
            private static void CmdToggleVisibility()
            {
                g.visible = !g.visible;
            }
            private static void CmdOriginToMousePos()
            {
                g.origin = UIManager.m_mouse_pos;
            }
            private static void CmdSetGraphWidth()
            {
                int range = uConsole.GetInt();
                if (range > 0)
                {
                    g.xrange = range;
                    g.qxrange = range / 4;
                }
            }
            private static void CmdSetGraphHeight()
            {
                int range = uConsole.GetInt();
                if (range > 0)
                {
                    g.yrange = range;
                    g.qyrange = range / 4;
                }
            }
            private static void CmdSetGraphName()
            {
                g.name = uConsole.GetString();
            }
            private static void CmdListDecayStructures()
            {
                int index = 0;
                foreach (Graph.DecayStructure curr in Graph.data_graphs)
                {
                    uConsole.Log(index++ + ": " + curr.name + "  (" + curr.size + ")");
                }
            }


            private static void CmdSelectDecayStructure()
            {
                int index = uConsole.GetInt();
                if (index >= 0 && index < Graph.data_graphs.Count)
                {
                    selected_ds = index;
                }
            }
            private static void CmdSetXAxis()
            {
                Graph.data_graphs[selected_ds].draw_x = uConsole.GetInt();
            }
            private static void CmdSetYAxis()
            {
                Graph.data_graphs[selected_ds].draw_y = uConsole.GetInt();
            }
            private static void CmdToggleShowSelectedDecayStructure()
            {
                Graph.data_graphs[selected_ds].show = !Graph.data_graphs[selected_ds].show;
            }
            private static void CmdSetColor()
            {
                string s = uConsole.GetString();
                int n = 0;
                if (s != null)
                {
                    if (s.StartsWith("#"))
                        s = s.Substring(1);
                    if ((s.Length != 3 && s.Length != 6) ||
                        !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out n))
                    {
                        uConsole.Log("Invalid color: " + s);
                        return;
                    }
                    if (s.Length == 3)
                        n = ((((n >> 8) & 0xf) * 0x11) << 16) | ((((n >> 4) & 0xf) * 0x11) << 8) | ((n & 0xf) * 0x11);
                }
                Graph.data_graphs[selected_ds].color = n;
            }
            private static void CmdSetName()
            {
                Graph.data_graphs[selected_ds].name = uConsole.GetString();
            }*/
        }

        private static int selected_ds = 0;
        public static Graph g;
    }



    class Graph
    {
        public string description;
        public Vector2 origin_position;
        public int graph_width;
        public int graph_height;
        public float ui_inner_line_width = 0.7f;
        public bool visible = true;
        public Dictionary<string, RingBuffer> data_buffers = new Dictionary<string, RingBuffer>();
        public List<DrawRelation> draw_relations = new List<DrawRelation>();

        public Graph(Vector2 origin_, int x_, int y_, string description_)
        {
            origin_position = origin_;
            graph_width = x_;
            graph_height = y_;
            description = description_;
        }

        public void AddDataPointToDecayStructure(string ring_buffer_key, float data_point, int decay_structure_element_count=1000)
        {
            if (string.IsNullOrEmpty(ring_buffer_key))
                return;

            if(!data_buffers.ContainsKey(ring_buffer_key) ){
                data_buffers.Add(ring_buffer_key, new Graph.RingBuffer(decay_structure_element_count));
                data_buffers[ring_buffer_key].name = ring_buffer_key;
            }
            data_buffers[ring_buffer_key].Add(data_point);
        }

        // Defines what should be drawn and which datastructures should be used for the x and/or y axis.
        // Will fallback to using the element count for the other axis if only one datastructure is provided
        // to correlate two datastructures they have to have the same element count
        internal class DrawRelation
        {
            public bool visible = true;
            public int color = 42069;
            public string x_axis_datastructure_key = "";
            public string y_axis_datastructure_key = "";

            public DrawRelation(int color, string x_axis_datastructure_key, string y_axis_datastructure_key, bool visible = true)
            {
                this.visible = visible;
                this.color = color;
                this.x_axis_datastructure_key = x_axis_datastructure_key;
                this.y_axis_datastructure_key = y_axis_datastructure_key;
            }
        }

        internal class RingBuffer
        {
            public string name;
            public int end, start; // end points to the newest element, start to the oldest
            public float[] data_array;

            public RingBuffer(int limit)
            {
                data_array = new float[limit];
                end = -1;
                start = 0;
            }

            public void Add(float val)
            {
                end++;
                if (end >= data_array.Length)
                    end = 0;
                if (end == start)
                    start++;
                if (start >= data_array.Length)
                    start = 0;
                data_array[end] = val;
            }

            public float GetMaximumValue()
            {
                int loop_end = data_array.Length-1;
                float maximum = data_array[0];
                if (start == 0)
                    loop_end = end;
                for( int i = 0; i <= loop_end; i++)
                    if (data_array[i] > maximum)
                        maximum = data_array[i];
                return maximum;
            }
        }

        // Draws all visible data for this graph
        public void Draw(UIElement instance)
        {
            if (!visible)
                return;

            // obtain the max values for the x and y axis
            float maximum_y_axis = float.MinValue;
            float maximum_x_axis = float.MinValue;
            foreach (DrawRelation dr in draw_relations)
                if (dr.visible)
                {
                    if(!string.IsNullOrEmpty(dr.y_axis_datastructure_key))
                    {
                        float local_max = data_buffers[dr.y_axis_datastructure_key].GetMaximumValue();
                        if ( local_max > maximum_y_axis)
                            maximum_y_axis = local_max;
                    }
                    if (!string.IsNullOrEmpty(dr.x_axis_datastructure_key))
                    {
                        float local_max = data_buffers[dr.x_axis_datastructure_key].GetMaximumValue();
                        if (local_max > maximum_x_axis)
                            maximum_x_axis = local_max;
                    }
                }

            DrawStatsAxes(instance, origin_position, graph_width, graph_height);


            // draw all graphs that are marked to be visible
            foreach (DrawRelation dr in draw_relations)
                if (dr.visible)
                {
                    DrawDecayStructureToGraph(dr, origin_position, instance, maximum_y_axis, maximum_x_axis);
                }
        }

        public void DrawStatsAxes(UIElement __instance, Vector2 initial_pos, int xrange, int yrange)
        {
            Vector2 zero = initial_pos;
            Color c = UIManager.m_col_ub2;
            c.a = 1f * 0.75f;
            zero.y -= graph_height / 4;
            UIManager.DrawQuadBarHorizontal(zero, ui_inner_line_width, ui_inner_line_width, xrange, c, 4);
            zero.y += graph_height / 4;
            UIManager.DrawQuadBarHorizontal(zero, ui_inner_line_width, ui_inner_line_width, xrange, c, 4);
            zero.y += graph_height / 4;
            UIManager.DrawQuadBarHorizontal(zero, ui_inner_line_width, ui_inner_line_width, xrange, c, 4);
            zero.y = initial_pos.y;

            zero.x -= graph_width / 4;
            UIManager.DrawQuadBarVertical(zero, ui_inner_line_width, ui_inner_line_width, yrange, c, 4);
            zero.x += graph_width / 4;
            UIManager.DrawQuadBarVertical(zero, ui_inner_line_width, ui_inner_line_width, yrange, c, 4);
            zero.x += graph_width / 4;
            UIManager.DrawQuadBarVertical(zero, ui_inner_line_width, ui_inner_line_width, yrange, c, 4);

            zero.x = initial_pos.x;
            UIManager.DrawFrameEmptyCenter(zero, 4f, 4f, xrange - (5 + ((-500 + xrange) / 50)), yrange - (5 + ((-200 + yrange) / 20)), c, 8);
            c = UIManager.m_col_ui0;
            c.a = 0.8f;

            zero = initial_pos;
            __instance.DrawStringSmall("[0:00]", zero, 0.3f, StringOffset.LEFT, UIManager.m_col_ui0, 1f, -1f);
            //zero.y += yrange * 0.7f;
            //zero.x += xrange * 0.55f;
            //__instance.DrawStringSmall("[" + RUtility.ConvertFloatToSeconds(GameplayManager.m_game_time, false) + "]", zero, 0.3f, StringOffset.RIGHT, UIManager.m_col_ui0, 1f, -1f);
            //zero.x -= xrange * 1.1f;
            //__instance.DrawStringSmall("[0:00]", zero, 0.3f, StringOffset.LEFT, UIManager.m_col_ui0, 1f, -1f);
            zero.x = initial_pos.x;
            __instance.DrawStringSmall(description, zero, 0.3f, StringOffset.CENTER, UIManager.m_col_ub1, 1f, -1f);
        }

        public void DrawDecayStructureToGraph(DrawRelation dr, Vector2 initial_pos, UIElement instance, float max_y, float max_x)
        {
            if (dr == null
                | (string.IsNullOrEmpty(dr.x_axis_datastructure_key) & string.IsNullOrEmpty(dr.y_axis_datastructure_key))
                | (max_x == float.MinValue & max_y == float.MinValue)
                | initial_pos == null
                | instance == null
                ) { return; }

            Color color = new Color((dr.color >> 16) / 255f, ((dr.color >> 8) & 0xff) / 255f, (dr.color & 0xff) / 255f);
            Vector2 start = Vector2.zero;
            Vector2 end = Vector2.zero;

            if (string.IsNullOrEmpty(dr.x_axis_datastructure_key))
            {
                RingBuffer data = data_buffers[dr.y_axis_datastructure_key];

                // If all displayed Relations associated with this graph do not have an X component fall back to the Element count to get a resolution 
                if (max_x == float.MinValue)
                    max_x = data.data_array.Length;

                // Define the start position
                start.y = (initial_pos.y + graph_height / 2) - (data.data_array[data.start] / max_y) * graph_height;
                start.x = (initial_pos.x - graph_width / 2);

                // Draw all points
                int point_counter = 1;
                int i = data.start + 1;
                if (i >= data.data_array.Length)
                    i = 0;
                while ( i != data.end )
                {
                    end.y = (initial_pos.y + graph_height / 2) - (data.data_array[i] / max_y) * graph_height;
                    end.x = (initial_pos.x - graph_width / 2) + (point_counter / max_x) * graph_width;

                    UIManager.DrawQuadCenterLine(start, end, 0.4f, 0f, color, 4);
                    start = end; // Swap the Points to connect the next line

                    // Iterate over the ring buffer
                    i++;
                    if (i >= data.data_array.Length)
                        i = 0;
                    point_counter++;
                    if(point_counter >= data.data_array.Length)
                    {
                        Debug.Log("GraphManager: Exceeded Exit Condition of while loop in DrawDecayStructureToGraph()");
                        break;
                    }
                }

            }
            else if (string.IsNullOrEmpty(dr.y_axis_datastructure_key))
            {

            }
            else // 2 data buffers
            {
                
            }

            
        }
    }
}
