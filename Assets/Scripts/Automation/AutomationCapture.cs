using System;
using System.IO;
using UnityEngine;

namespace VelkhanaSlice.Automation
{
    public static class AutomationCapture
    {
        /// <summary>Renders the main camera directly, including while paused or hidden.</summary>
        public static string CaptureMainCamera(string requestedPath)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
                throw new ArgumentException("Capture path is required", nameof(requestedPath));

            Camera camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("No main camera is available");

            string path = Path.GetFullPath(requestedPath);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

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
                return path;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(image);
            }
        }
    }
}
