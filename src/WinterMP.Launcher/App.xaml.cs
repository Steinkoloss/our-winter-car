using System.Windows;
using WinterMP.Launcher.Services;

namespace WinterMP.Launcher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (CliInstallRunner.TryRun(e.Args, out int exitCode))
            {
                Environment.Exit(exitCode);
                return;
            }

            base.OnStartup(e);
            new MainWindow().Show();
        }
    }
}
