using System.Windows;
using System.Windows.Threading;

namespace HauntPatrol;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--selftest"))
        {
            Shutdown(SelfCheck.Run() ? 0 : 1);
            return;
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();

        // Demo recording / smoke tests: --demo --all --interval <seconds> --start --trigger skeleton|cat|glass|slime
        string? Arg(string name) => Array.IndexOf(e.Args, name) is var i and >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null;
        if (e.Args.Contains("--demo")) main.DemoBox.IsChecked = true;
        if (e.Args.Contains("--all")) main.AllMonitors.IsChecked = true;
        if (Arg("--interval") is { } seconds) main.Controller.Interval = TimeSpan.FromSeconds(double.Parse(seconds));
        if (e.Args.Contains("--start")) main.Controller.StartHaunting();
        if (Arg("--trigger") is { } what) main.Controller.Trigger(what);
    }

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A stuck topmost overlay is the worst failure mode: tear everything down and keep running.
        (MainWindow as MainWindow)?.Controller.Abort($"unhandled {e.Exception.GetType().Name}: {e.Exception.Message}");
        e.Handled = true;
    }
}
