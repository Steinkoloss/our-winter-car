using Avalonia;
using WinterMP.Launcher.Services;

namespace WinterMP.Launcher
{
    internal static class Program
    {
        // Avalonia needs an explicit STA entry point (WPF generated this from App.xaml).
        [STAThread]
        public static int Main(string[] args)
        {
            // Headless install path (WinterMP-Setup.exe / automation / Linux scripts) must
            // run console-only and exit before any UI/windowing is initialized.
            if (CliInstallRunner.TryRun(args, out int exitCode))
                return exitCode;

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
