using System.Threading;
using System.Windows;
using QQDay30.Core;

namespace QQDay30;

public partial class App : Application
{
    private Mutex? _single;
    private EventWaitHandle? _showSignal;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _single = new Mutex(true, "QQDay30.Single", out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "QQDay30.Show");
        if (!first)
        {
            // already running: ask that instance to show its window
            _showSignal.Set();
            Shutdown();
            return;
        }

        ThemeManager.Init(Settings.Load().Theme);

        if (!CookieStore.LooksLoggedIn(CookieStore.Load()) || e.Args.Contains("--login"))
        {
            if (new LoginWindow().ShowDialog() != true) { Shutdown(); return; }
        }

        _host = new AppHost();
        if (!e.Args.Contains("--tray")) _host.ShowWindow();

        var signal = _showSignal;
        new Thread(() =>
        {
            while (signal.WaitOne()) Dispatcher.Invoke(() => _host?.ShowWindow());
        }) { IsBackground = true }.Start();

        DispatcherUnhandledException += (_, ev) =>
        {
            AppPaths.Log("unhandled: " + ev.Exception);
            ev.Handled = true;
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
