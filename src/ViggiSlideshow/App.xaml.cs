using System.Windows;
using System.Windows.Interop;
using ViggiSlideshow.Core;

namespace ViggiSlideshow;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Nothing may ever put up an error dialog: a screensaver nobody is watching would sit there
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled error on the UI thread", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled error", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task error", args.Exception);
            args.SetObserved();
        };

        if (e.Args.Length >= 4 && e.Args[0].Equals("/test", StringComparison.OrdinalIgnoreCase))
        {
            new TestRun(e.Args).Start();
            return;
        }

        ScreensaverArgs args = ScreensaverArgs.Parse(e.Args);
        Log.Info($"Started with [{string.Join(' ', e.Args)}] as {args.Mode}");

        switch (args.Mode)
        {
            case ScreensaverMode.Configure:
                var settingsWindow = new ConfigWindow();
                if (args.Window != 0)
                    new WindowInteropHelper(settingsWindow).Owner = args.Window;
                settingsWindow.Closed += (_, _) => Shutdown();
                settingsWindow.Show();
                break;

            case ScreensaverMode.Preview:
                if (args.Window == 0)
                {
                    Shutdown();
                    return;
                }
                new PreviewHost(args.Window).Start();
                break;

            default:
                // Windows occasionally starts a second copy; one is plenty
                _singleInstance = new Mutex(true, @"Local\ViggiSlideshow.Running", out bool first);
                if (!first)
                {
                    Log.Info("Already running");
                    Shutdown();
                    return;
                }
                new ScreensaverSession().Start();
                break;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
