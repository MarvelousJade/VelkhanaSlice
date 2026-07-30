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

            [Tooltip("Frames into the beat at which to capture. Empty captures on the last frame.")]
            public int[] captureAt = Array.Empty<int>();
        }

        [Tooltip("Executed in order. Defaults exercise draw, charge, swing, tackle and roll.")]
        public List<Beat> script = new List<Beat>
        {
            new Beat { label = "01_idle",          frames = 50, captureAt = new[] { 45 } },
            new Beat { label = "02_approach",      frames = 90, move = new Vector2(0f, 1f), captureAt = new[] { 85 } },
            new Beat { label = "03_drawslash",     frames = 40, attack = true, captureAt = new[] { 24, 34 } },
            new Beat { label = "04_release",       frames = 30 },
            new Beat { label = "05_charge_hold",   frames = 125, attack = true, captureAt = new[] { 25, 55, 90, 118 } },
            new Beat { label = "06_charged_swing", frames = 45, captureAt = new[] { 8, 26, 40 } },
            new Beat { label = "07_wide_slash",    frames = 40, secondary = true, captureAt = new[] { 18, 24 } },
            new Beat { label = "08_roll",          frames = 40, dodge = true, move = new Vector2(0f, -1f), captureAt = new[] { 8, 20 } },
            new Beat { label = "09_watch_monster", frames = 150, captureAt = new[] { 40, 80, 120, 145 } },
            new Beat { label = "10_close_in",      frames = 90, move = new Vector2(0f, 1f), captureAt = new[] { 85 } },
            new Beat { label = "11_monster_range", frames = 240, captureAt = new[] { 60, 120, 180, 235 } },

            // Holding run with the sword out first sheaths it, then accelerates. Attacking from
            // that sheathed sprint automatically performs the draw slash.
            new Beat { label = "12_run_auto_sheathe", frames = 50, move = new Vector2(0f, -1f), run = true, captureAt = new[] { 5, 25, 49 } },
            new Beat { label = "13_sprint",           frames = 90, move = new Vector2(0f, -1f), run = true, captureAt = new[] { 0, 89 } },
            new Beat { label = "14_attack_auto_draw", frames = 45, attack = true, captureAt = new[] { 0, 20, 40 } },
        };

        [Tooltip("Seconds to wait after the last capture so files are flushed before quitting.")]
        public float quitDelay = 2f;

        string _directory;
        Gamepad _pad;
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
            Send(beat);

            if (ShouldCapture(beat, _frameInBeat))
            {
                string path = Path.Combine(_directory, $"{beat.label}_f{_frameInBeat:000}.png");
                ScreenCapture.CaptureScreenshot(path);
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
