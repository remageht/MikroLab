using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MikroLab.Presentation.Services;

namespace MikroLab.Studio.Services
{
    /// <summary>
    /// Renders the whole studio window into a PNG. Avalonia's
    /// <see cref="RenderTargetBitmap"/> needs a visible, attached window, which
    /// the capture harness guarantees before every shot.
    /// </summary>
    internal sealed class AvaloniaScreenshotService : IScreenshotService
    {
        private readonly Func<Window> _window;

        public AvaloniaScreenshotService(Func<Window> window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        public void Capture(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            Window window = _window();
            if (window == null) throw new InvalidOperationException("Screenshot owner is not available.");

            double scaling = window.RenderScaling <= 0 ? 1 : window.RenderScaling;
            int width = Math.Max(1, (int)Math.Round(window.Bounds.Width * scaling));
            int height = Math.Max(1, (int)Math.Round(window.Bounds.Height * scaling));

            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            using (var bitmap = new RenderTargetBitmap(
                new PixelSize(width, height),
                new Vector(96 * scaling, 96 * scaling)))
            {
                bitmap.Render(window);
                bitmap.Save(path, PngBitmapEncoderOptions.Default);
            }
        }
    }
}
