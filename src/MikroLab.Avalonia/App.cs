using System;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using MikroLab.Studio.Services;

namespace MikroLab.Studio
{
    internal sealed class StartupOptions
    {
        public string CaptureDirectory { get; private set; }
        public int Sample { get; private set; }
        public string Workspace { get; private set; } = "studio";

        public static StartupOptions Parse(string[] args)
        {
            var options = new StartupOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--capture":
                        options.CaptureDirectory = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                            ? args[++i]
                            : Path.Combine(AppContext.BaseDirectory, "screens");
                        options.CaptureDirectory = Path.GetFullPath(options.CaptureDirectory);
                        break;
                    case "--sample":
                        if (i + 1 < args.Length)
                        {
                            int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sample);
                            options.Sample = sample;
                        }
                        break;
                    case "--workspace":
                        if (i + 1 < args.Length) options.Workspace = args[++i];
                        break;
                }
            }
            return options;
        }
    }

    internal sealed class App : Application
    {
        public static StartupOptions Options { get; set; } = new StartupOptions();

        public override void Initialize()
        {
            RequestedThemeVariant = ThemeVariant.Dark;
            Styles.Add(new FluentTheme());
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = new MainWindow();
                desktop.MainWindow = window;
                if (Options.CaptureDirectory != null)
                {
                    window.Opened += (sender, args) => window.BeginCapture(
                        Options.CaptureDirectory, Options.Sample, Options.Workspace);
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
