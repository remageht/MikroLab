using System;
using System.IO;

namespace MikroLab.Studio.Services
{
    internal static class CrashReporter
    {
        public static void Report(Exception exception)
        {
            if (exception == null) return;

            try
            {
                string text = DateTime.Now.ToString("u") + Environment.NewLine + exception + Environment.NewLine + Environment.NewLine;

                if (!TryAppend(Path.Combine(AppContext.BaseDirectory, "crash.log"), text))
                {
                    string folder = FallbackFolder();
                    Directory.CreateDirectory(folder);
                    File.AppendAllText(Path.Combine(folder, "crash.log"), text);
                }
            }
            catch
            {
                // Logging must never mask the original failure.
            }
        }

        private static string FallbackFolder()
        {
            if (OperatingSystem.IsMacOS())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Application Support",
                    "MikroLab");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MikroLab");
        }

        private static bool TryAppend(string path, string text)
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
