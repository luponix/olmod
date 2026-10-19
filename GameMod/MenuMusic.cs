using HarmonyLib;
using Overload;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;

namespace GameMod
{
    public static class MenuMusic
    {
        public enum PlayMode
        {
            Default,
            Shuffle,
            InOrder,
            ShuffleAll,
            InOrderAll
        }

        public const string DefaultTrack = "MENU_01";

        public const int MainMenuSelection = 2500;

        private const float TrackVolume = 0.35f;

        private const float MinTrackLength = 45f;

        private const float StartTimeout = 5f;

        private const float EndMargin = 0.25f;

        private static readonly string[] Tracks = {
            "ALIEN_13", "ALIEN_14", "ALIEN_15", "ALIEN_16", "ALIEN_BOSS",
            "BRIEFING_01", "BRIEFING_01A", "BRIEFING_01B", "BRIEFING_02", "BRIEFING_03",
            "BRIEFING_04", "CREDITS", "ENDING_A", "ENDING_B", "EXIT_01",
            "EXIT_02", "INNER_11", "INNER_12", "INNER_12_SHIP", "OUTER_01",
            "OUTER_02", "OUTER_03", "OUTER_04", "OUTER_05", "OUTER_BOSS",
            "SECRET_01", "TITAN_06", "TITAN_07", "TITAN_08", "TITAN_09",
            "TITAN_10", "TITAN_BOSS", "TRAINING", "UNUSED_01"
        };

        private static readonly System.Random m_rng = new System.Random((int)(System.DateTime.Now.Ticks & 0x7fffffff));

        private static readonly bool[] m_unusable = new bool[Tracks.Length];

        public static PlayMode Mode { get; private set; } = PlayMode.Shuffle;

        public static bool Enabled
        {
            get { return Mode != PlayMode.Default; }
        }

        public static bool Active
        {
            get { return m_active; }
        }

        public static bool InLevels
        {
            get { return Mode == PlayMode.ShuffleAll || Mode == PlayMode.InOrderAll; }
        }

        private static bool Shuffled
        {
            get { return Mode == PlayMode.Shuffle || Mode == PlayMode.ShuffleAll; }
        }

        private static bool m_active;

        private static bool m_advancing;

        private static int[] m_order;
        private static bool m_order_shuffled;
        private static int m_order_pos;

        private static int m_current = -1;
        private static string m_current_label = "";

        private const string TooltipBase = "REPLACES THE MAIN MENU TRACK WITH THE OTHER MUSIC FROM THE GAME";
        private static string m_tooltip = TooltipBase;

        private static float m_last_time;

        private static float m_track_length;

        private static bool m_awaiting_start;

        private static float m_start_deadline;

        private static int m_failed_advances;

        private static bool InMenus
        {
            get { return GameManager.m_game_state == GameManager.GameState.MENU && !MenuManager.m_game_paused; }
        }

        public static string ModeName()
        {
            switch (Mode)
            {
                case PlayMode.Shuffle: return "SHUFFLE";
                case PlayMode.InOrder: return "IN ORDER";
                case PlayMode.ShuffleAll: return "SHUFFLE (LEVELS TOO)";
                case PlayMode.InOrderAll: return "IN ORDER (LEVELS TOO)";
                default: return "DEFAULT";
            }
        }

        public static string Tooltip()
        {
            return m_tooltip;
        }

        public static string CurrentLabel()
        {
            return m_active ? m_current_label : "";
        }

        public static float Progress()
        {
            if (!m_active || m_track_length <= 0f)
                return 0f;
            return Mathf.Clamp01(m_last_time / m_track_length);
        }

        public static void SetMode(PlayMode mode, bool apply = true)
        {
            if (Mode == mode)
                return;
            Mode = mode;
            UpdateTooltip();
            if (apply)
                ApplyMode();
        }

        public static void CycleMode(int dir)
        {
            const int count = (int)PlayMode.InOrderAll + 1;
            SetMode((PlayMode)((((int)Mode + dir) % count + count) % count));
        }

        public static void SetModeFromPrefs(int value)
        {
            if (value < 0 || value > (int)PlayMode.InOrderAll)
                value = 0;
            SetMode((PlayMode)value, false);
        }

        public static void Skip()
        {
            if (Enabled && (InMenus || InLevels))
                Advance();
        }

        private static void ApplyMode()
        {
            if (GameManager.m_audio == null)
                return;
            if (Mode == PlayMode.Default)
            {
                if (!m_active)
                    return;
                m_active = false;
                if (InMenus)
                    GameManager.m_audio.PlayMusic(DefaultTrack, TrackVolume, 0, true, true);
                return;
            }
            if (m_active)
                return;
            if (InMenus || InLevels)
                Advance();
        }

        private static void Advance()
        {
            m_advancing = true;
            try
            {
                GameManager.m_audio.PlayMusic(DefaultTrack, TrackVolume, 0, true, false);
            }
            finally
            {
                m_advancing = false;
            }
        }

        public static void OnPlayMusic(ref string track, ref float volume, ref bool loop, ref bool force)
        {
            if (!Enabled || GameplayManager.IsDedicatedServer())
                return;

            if (track != DefaultTrack)
            {
                if (!InLevels || !m_active)
                {
                    m_active = false;
                    return;
                }
                KeepCurrentTrack(ref track, ref volume, ref loop, ref force);
                return;
            }

            if (m_active && !m_advancing)
            {
                KeepCurrentTrack(ref track, ref volume, ref loop, ref force);
                return;
            }

            int index = NextUsableTrack();
            if (index < 0)
            {
                m_active = false;
                loop = true;
                return;
            }

            m_current = index;
            m_current_label = Tracks[index].Replace('_', ' ');
            m_last_time = 0f;
            m_active = true;
            track = Tracks[index];
            volume = TrackVolume;
            loop = false;
            WaitForStart();
            UpdateTooltip();
            Debug.Log("[MenuMusic] playing " + track);
        }

        private static void KeepCurrentTrack(ref string track, ref float volume, ref bool loop, ref bool force)
        {
            track = Tracks[m_current];
            volume = TrackVolume;
            loop = false;
            force = false;
        }

        public static void OnUpdateAudio(AudioSource music_source, float music_volume)
        {
            if (!m_active)
                return;
            if (!Enabled || music_source == null)
            {
                m_active = false;
                return;
            }
            if (music_volume <= 0f)
                return;

            if (!InMenus && !InLevels)
            {
                if (!music_source.isPlaying && music_source.clip != null)
                    music_source.Play();
                return;
            }

            if (music_source.loop)
            {
                music_source.loop = false;
                WaitForStart();
                return;
            }

            if (m_awaiting_start)
            {
                if (music_source.isPlaying && music_source.clip != null)
                {
                    m_awaiting_start = false;
                    m_failed_advances = 0;
                    m_last_time = music_source.time;
                    m_track_length = music_source.clip.length;
                }
                else if (!Application.isFocused)
                {
                    m_start_deadline = Time.unscaledTime + StartTimeout;
                }
                else if (Time.unscaledTime >= m_start_deadline)
                {
                    TryAdvance();
                }
                return;
            }

            float position = music_source.time;
            bool ended = music_source.clip != null && position >= music_source.clip.length - EndMargin;
            bool rewound = !music_source.isPlaying && position + 1f < m_last_time;
            m_last_time = position;
            if (ended || rewound)
                TryAdvance();
        }

        private static void WaitForStart()
        {
            m_awaiting_start = true;
            m_start_deadline = Time.unscaledTime + StartTimeout;
        }

        private static void TryAdvance()
        {
            if (m_failed_advances >= Tracks.Length)
            {
                Debug.LogWarning("[MenuMusic] the music source will not play, staying quiet until the next menu");
                m_active = false;
                m_failed_advances = 0;
                return;
            }
            m_failed_advances++;
            Advance();
        }

        private static int NextUsableTrack()
        {
            if (m_order == null || m_order_shuffled != Shuffled)
                BuildOrder();

            for (int tries = 0; tries < m_order.Length; tries++)
            {
                m_order_pos++;
                if (m_order_pos >= m_order.Length)
                {
                    if (Shuffled)
                        BuildOrder();
                    m_order_pos = 0;
                }
                int index = m_order[m_order_pos];
                if (!m_unusable[index] && CanPlay(index))
                    return index;
            }
            return -1;
        }

        private static void BuildOrder()
        {
            if (m_order == null)
                m_order = new int[Tracks.Length];
            for (int i = 0; i < m_order.Length; i++)
                m_order[i] = i;
            if (Shuffled)
                for (int i = m_order.Length - 1; i > 0; i--)
                {
                    int j = m_rng.Next(i + 1);
                    int tmp = m_order[i];
                    m_order[i] = m_order[j];
                    m_order[j] = tmp;
                }
            m_order_shuffled = Shuffled;
            m_order_pos = -1;
        }

        private static bool CanPlay(int index)
        {
            AudioClip clip = (AudioClip)Resources.Load("Music/overload_" + Tracks[index]);
            if (clip == null)
            {
                Debug.Log("[MenuMusic] no clip for " + Tracks[index] + ", dropping it");
                m_unusable[index] = true;
                return false;
            }
            if (clip.length < MinTrackLength)
            {
                m_unusable[index] = true;
                Resources.UnloadAsset(clip);
                return false;
            }
            m_track_length = clip.length;
            return true;
        }

        private static void UpdateTooltip()
        {
            m_tooltip = TooltipBase;
            if (m_active && Enabled)
                m_tooltip += " (NOW PLAYING: " + m_current_label + ")";
        }

    }

    [HarmonyPatch(typeof(UnityAudio), "PlayMusic")]
    class MenuMusic_UnityAudio_PlayMusic
    {
        static void Prefix(ref string track, ref float volume, ref bool loop, ref bool force)
        {
            MenuMusic.OnPlayMusic(ref track, ref volume, ref loop, ref force);
        }
    }

    [HarmonyPatch(typeof(UnityAudio), "FadeMusic")]
    class MenuMusic_UnityAudio_FadeMusic
    {
        static bool Prefix()
        {
            return !(MenuMusic.InLevels && MenuMusic.Active);
        }
    }

    [HarmonyPatch(typeof(UnityAudio), "UpdateAudio")]
    class MenuMusic_UnityAudio_UpdateAudio
    {
        static void Postfix(AudioSource ___m_music_source, float ___m_volume_music)
        {
            MenuMusic.OnUpdateAudio(___m_music_source, ___m_volume_music);
        }
    }

    [HarmonyPatch(typeof(UIElement), "DrawMainMenu")]
    class MenuMusic_UIElement_DrawMainMenu
    {
        private const float ROW_X = 500f;
        private const float BAR_Y = 31f;
        private const float LABEL_Y = 52f;
        private const float BAR_HALF_WIDTH = 110f;
        private const float BAR_HALF_HEIGHT = 1.5f;

        public static void DrawMenuMusicRow(UIElement ui, ref Vector2 pos)
        {
            if (!MenuMusic.Enabled)
                return;
            Vector2 row = new Vector2(ROW_X, pos.y);
            ui.SelectAndDrawHalfItem(Loc.LS("NEXT MUSIC TRACK"), row, MenuMusic.MainMenuSelection, false);
            string label = MenuMusic.CurrentLabel();
            if (label == "")
                return;
            Vector2 bar = row + Vector2.up * BAR_Y;
            UIManager.DrawQuadUI(bar, BAR_HALF_WIDTH, BAR_HALF_HEIGHT, UIManager.m_col_ui1, ui.m_alpha * 0.2f, 11);
            float progress = MenuMusic.Progress();
            if (progress > 0f)
                UIManager.DrawQuadUI(bar - Vector2.right * (BAR_HALF_WIDTH * (1f - progress)), BAR_HALF_WIDTH * progress,
                    BAR_HALF_HEIGHT, UIManager.m_col_ui1, ui.m_alpha * 0.75f, 11);
            ui.DrawStringSmall(label, row + Vector2.up * LABEL_Y, 0.4f, StringOffset.CENTER, UIManager.m_col_ui1, 0.5f, 300f);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
        {
            var halfItem = AccessTools.Method(typeof(UIElement), "SelectAndDrawHalfItem");
            var row = AccessTools.Method(typeof(MenuMusic_UIElement_DrawMainMenu), "DrawMenuMusicRow");
            int state = 0;
            foreach (var code in codes)
            {
                if (state == 0 && code.opcode == OpCodes.Ldstr && (string)code.operand == "OVERLOAD ON DISCORD")
                    state = 1;
                yield return code;
                if (state == 1 && code.opcode == OpCodes.Call && code.operand == halfItem)
                {
                    state = 2;
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldloca_S, 0);
                    yield return new CodeInstruction(OpCodes.Call, row);
                }
            }
            if (state != 2)
                Debug.LogWarning("[MenuMusic] DrawMainMenu anchor not found, the next track item is not drawn");
        }
    }

    [HarmonyPatch(typeof(MenuManager), "MainMenuUpdate")]
    class MenuMusic_MenuManager_MainMenuUpdate
    {
        static void Postfix()
        {
            if (!MenuMusic.Enabled ||
                MenuManager.m_menu_sub_state != MenuSubState.ACTIVE ||
                UIManager.m_menu_selection != MenuMusic.MainMenuSelection ||
                !UIManager.PushedSelect())
                return;
            MenuManager.PlaySelectSound();
            MenuMusic.Skip();
        }
    }
}
