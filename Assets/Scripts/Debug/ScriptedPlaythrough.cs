using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace VelkhanaSlice.DebugTools
{
    /// <summary>
    /// Plays the game through a virtual gamepad and screenshots each beat, so a build can be
    /// checked visually without a human at the keyboard. Inert unless the player is started with
    /// <c>-autoshots &lt;directory&gt;</c>.
    ///
    /// The virtual pad goes through the real Input System, so this exercises the same polling path
    /// <see cref="Hunter.HunterController"/> uses in a normal session.
    /// </summary>
    public class ScriptedPlaythrough : MonoBehaviour
    {
        [Serializable]
        public class Beat
        {
            [Tooltip("Used as the screenshot filename, so keep it sortable.")]
            public string label = "beat";

            [Tooltip("How long to hold this input, in render frames.")]
            public int frames = 60;

            public Vector2 move;
            public bool attack;
            public bool secondary;
            public bool dodge;
            public bool sheathe;
            public bool run;
            public bool guard;

            [Tooltip("Frames into the beat at which to capture. Empty captures on the last frame.")]
            public int[] captureAt = Array.Empty<int>();
        }

        [Tooltip("Executed in order. Defaults exercise the retained decoded WP00 ground core.")]
        public List<Beat> script = new List<Beat>
        {
            new Beat { label = "01_idle",                 frames = 30, captureAt = new[] { 25 } },
            new Beat { label = "02_guard",                frames = 25, guard = true, captureAt = new[] { 8, 22 } },
            new Beat { label = "03_guard_release",        frames = 12 },

            // Full Triangle chain. Each buffered Triangle+lever input is held long enough for the
            // prior release animation to complete and for the next charge stance to become visible.
            new Beat { label = "04_basic_charge",         frames = 90, attack = true, captureAt = new[] { 20, 50, 82 } },
            new Beat { label = "05_basic_release",        frames = 20, captureAt = new[] { 4, 16 } },
            new Beat { label = "06_buffer_strong_charge", frames = 125, attack = true, move = new Vector2(1f, 0f), captureAt = new[] { 20, 70, 118 } },
            new Beat { label = "07_strong_release",       frames = 20, captureAt = new[] { 4, 16 } },
            new Beat { label = "08_buffer_true_charge",   frames = 140, attack = true, move = new Vector2(-1f, 0f), captureAt = new[] { 25, 80, 132 } },
            new Beat { label = "09_tcs_open_and_finish",  frames = 75, captureAt = new[] { 5, 28, 52, 70 } },

            // Circle during the basic hold chooses tackle A79. Triangle after it advances to the
            // strong hold, while the strong/true charge route uses tackle A80.
            new Beat { label = "10_basic_charge_for_tackle", frames = 30, attack = true, captureAt = new[] { 24 } },
            new Beat { label = "11_tackle_a79",               frames = 6, attack = true, secondary = true, captureAt = new[] { 2, 5 } },
            new Beat { label = "12_release_tackle_a79",       frames = 12 },
            new Beat { label = "13_tackle_to_strong",         frames = 35, attack = true, captureAt = new[] { 8, 28 } },
            new Beat { label = "14_tackle_a80",               frames = 6, attack = true, secondary = true, captureAt = new[] { 2, 5 } },
            new Beat { label = "15_release_tackle_a80",       frames = 12 },
            new Beat { label = "16_tackle_to_true",           frames = 40, attack = true, captureAt = new[] { 8, 34 } },
            new Beat { label = "17_true_release_again",       frames = 60, captureAt = new[] { 6, 28, 52 } },

            new Beat { label = "18_roll",          frames = 40, dodge = true, move = new Vector2(0f, -1f), captureAt = new[] { 8, 20 } },
            new Beat { label = "19_watch_monster", frames = 180, captureAt = new[] { 40, 90, 140, 175 } },

            // The first run beat begins sheathing; attacking before it completes must cancel into
            // N021 MovingDrawToVerticalSlash (WP_00::WALK_ON), then N031 into N001 VSLASH,
            // rather than skipping directly into the N003 basic-charge state.
            new Beat { label = "20_begin_run_sheathe",   frames = 10, move = new Vector2(0f, -1f), run = true, captureAt = new[] { 4, 9 } },
            new Beat { label = "21_attack_pending_sheathe", frames = 60, move = new Vector2(0f, -1f), attack = true, run = true, captureAt = new[] { 1, 20, 52 } },
            new Beat { label = "22_release_draw",        frames = 20 },
            new Beat { label = "23_run_auto_sheathe",    frames = 50, move = new Vector2(0f, -1f), run = true, captureAt = new[] { 5, 25, 49 } },
            new Beat { label = "24_sprint",              frames = 75, move = new Vector2(0f, -1f), run = true, captureAt = new[] { 0, 70 } },
        };

        [Tooltip("Seconds to wait after the last capture so files are flushed before quitting.")]
        public float quitDelay = 2f;

        string _directory;
        Gamepad _pad;
        Monster.VelkhanaBrain _brain;
        int _beatIndex;
        int _frameInBeat;

        void Awake()
        {
            _directory = CommandLineValue("-autoshots");
            if (string.IsNullOrEmpty(_directory)) { enabled = false; return; }

            Directory.CreateDirectory(_directory);

            // Beats are counted in render frames while combat runs at a fixed 60 Hz. Without this
            // cap an uncapped player advances several render frames per simulation frame and the
            // captures land nowhere near the attack frames they are named for.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            _pad = InputSystem.AddDevice<Gamepad>("ScriptedPad");
            _brain = UnityEngine.Object.FindAnyObjectByType<Monster.VelkhanaBrain>();
            if (_brain != null)
            {
                // Keep the first half as a clean weapon-state showcase. The later watch beat
                // re-enables the real AI so the same run still validates monster behaviour.
                _brain.enabled = false;
            }
            Debug.Log($"ScriptedPlaythrough driving {script.Count} beats into {_directory}");
        }

        void OnDestroy()
        {
            if (_pad != null && _pad.added) InputSystem.RemoveDevice(_pad);
        }

        void Update()
        {
            if (_beatIndex >= script.Count) return;

            Beat beat = script[_beatIndex];
            if (_brain != null &&
                !_brain.enabled &&
                beat.label.StartsWith("19_watch_monster", StringComparison.Ordinal))
            {
                _brain.enabled = true;
            }
            Send(beat);

            if (ShouldCapture(beat, _frameInBeat))
            {
                string path = Path.Combine(_directory, $"{beat.label}_f{_frameInBeat:000}.png");
                CaptureCamera(path);
            }

            if (++_frameInBeat < beat.frames) return;

            _frameInBeat = 0;
            if (++_beatIndex < script.Count) return;

            Send(null);
            Invoke(nameof(Quit), quitDelay);
        }

        static bool ShouldCapture(Beat beat, int frame)
        {
            if (beat.captureAt == null || beat.captureAt.Length == 0) return frame == beat.frames - 1;

            for (int i = 0; i < beat.captureAt.Length; i++)
                if (beat.captureAt[i] == frame) return true;
            return false;
        }

        /// <summary>
        /// Renders the game camera explicitly so automated validation also works when the player
        /// window is hidden. ScreenCapture returns black frames for hidden D3D windows.
        /// </summary>
        static void CaptureCamera(string path)
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            int width = Mathf.Max(640, Screen.width);
            int height = Mathf.Max(360, Screen.height);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);

            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply(false);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                Destroy(image);
            }
        }

        /// <summary>Queues one frame of pad state. A null beat releases everything.</summary>
        void Send(Beat beat)
        {
            var state = new GamepadState();
            if (beat != null)
            {
                state.leftStick = beat.move;
                state = state.WithButton(GamepadButton.West, beat.attack);
                state = state.WithButton(GamepadButton.North, beat.secondary);
                state = state.WithButton(GamepadButton.East, beat.dodge);
                state = state.WithButton(GamepadButton.South, beat.sheathe);
                state = state.WithButton(GamepadButton.LeftStick, beat.run);
                state.rightTrigger = beat.guard ? 1f : 0f;
            }

            InputSystem.QueueStateEvent(_pad, state);
        }

        void Quit() => Application.Quit();

        static string CommandLineValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
