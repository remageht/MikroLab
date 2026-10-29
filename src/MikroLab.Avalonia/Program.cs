using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using MikroLab.Studio.Services;

namespace MikroLab.Studio
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
                CrashReporter.Report(eventArgs.ExceptionObject as Exception);

            TaskScheduler.UnobservedTaskException += (sender, eventArgs) =>
            {
                CrashReporter.Report(eventArgs.Exception);
                eventArgs.SetObserved();
            };

            Dispatcher.UIThread.UnhandledException += (sender, eventArgs) =>
            {
                CrashReporter.Report(eventArgs.Exception);
            };

            App.Options = StartupOptions.Parse(args);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
        }
    }
}
