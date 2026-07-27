using System;
using System.IO;
using UnityEngine;

namespace VelkhanaSlice.DebugTools
{
    /// <summary>
    /// Captures frames from inside the running player and then exits, so a build can be checked
    /// visually without capturing the desktop. Inert unless the player is started with
    /// <c>-autoshots &lt;directory&gt;</c>.
    /// </summary>
    public class AutoScreenshot : MonoBehaviour
    {
        [Tooltip("Frames on which to capture, in order.")]
        public int[] captureFrames = { 45, 150, 260, 370, 480 };

        [Tooltip("Seconds to wait after the last capture so the file is flushed before quitting.")]
        public float quitDelay = 2f;

        string _directory;
        int _frame;
        int _next;

        void Awake()
        {
            _directory = CommandLineValue("-autoshots");
            if (string.IsNullOrEmpty(_directory))
            {
                enabled = false;
                return;
            }

            Directory.CreateDirectory(_directory);
            Debug.Log($"AutoScreenshot writing to {_directory}");
        }

        void Update()
        {
            _frame++;
            if (_next >= captureFrames.Length || _frame < captureFrames[_next]) return;

            string path = Path.Combine(_directory, $"frame_{captureFrames[_next]:0000}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"AutoScreenshot captured {path}");
            _next++;

            if (_next >= captureFrames.Length) Invoke(nameof(Quit), quitDelay);
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
