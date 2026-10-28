using System;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace MikroLab.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (sender, eventArgs) => ReportFatal(eventArgs.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) => ReportFatal(eventArgs.ExceptionObject as Exception);

            string captureDirectory = null;
            int sample = 0;
            string workspace = "studio";

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--capture":
                        captureDirectory = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                            ? args[++i]
                            : Path.Combine(AppContext.BaseDirectory, "screens");
                        captureDirectory = Path.GetFullPath(captureDirectory);
                        break;
                    case "--sample":
                        if (i + 1 < args.Length)
                        {
                            int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out sample);
                        }
                        break;
                    case "--workspace":
                        if (i + 1 < args.Length)
                        {
                            workspace = args[++i];
                        }
                        break;
                }
            }

            var form = new MainForm();
            if (captureDirectory != null)
            {
                form.Shown += (sender, eventArgs) => form.BeginCapture(captureDirectory, sample, workspace);
            }

            Application.Run(form);
        }

        private static void ReportFatal(Exception exception)
        {
            if (exception == null) return;

            try
            {
                string text = DateTime.Now.ToString("u") + Environment.NewLine + exception + Environment.NewLine + Environment.NewLine;

                // Portable builds keep the log beside the executable. If that folder is
                // read-only (for example Program Files), fall back to the user profile.
                if (!TryAppendCrashLog(Path.Combine(AppContext.BaseDirectory, "crash.log"), text))
                {
                    string folder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MikroLab");
                    Directory.CreateDirectory(folder);
                    File.AppendAllText(Path.Combine(folder, "crash.log"), text);
                }
            }
            catch
            {
                // Logging must never mask the original failure.
            }

            MessageBox.Show(
                exception.Message,
                "MikroLab — непредвиденная ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private static bool TryAppendCrashLog(string path, string text)
        {
            try
            {
                File.AppendAllText(path, text);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
