using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using QQDay30.Core;
using Forms = System.Windows.Forms;

namespace QQDay30;

/// <summary>Long-lived background part: tray icon, scheduled capture, QQ Music watcher.</summary>
public sealed class AppHost : IDisposable
{
    public Settings Settings { get; } = Settings.Load();
    public Store Store { get; } = new();
    public QQMusicClient Client { get; private set; }

    public event EventHandler? Captured;
    public event EventHandler<string>? StatusChanged;

    private readonly Forms.NotifyIcon _tray;
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromMinutes(30) };
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private MainWindow? _window;
    private bool _qqWasRunning;
    private bool _loginOpen;

    public AppHost()
    {
        Client = new QQMusicClient(CookieStore.Load());
        Settings.ApplyAutoStart();

        _tray = new Forms.NotifyIcon { Icon = LoadIcon(), Text = "每日30首捕捉器", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => ShowWindow());
        menu.Items.Add("立即捕获今天", null, async (_, _) => await SafeCapture(manual: true));
        menu.Items.Add("重新登录", null, (_, _) => RequestLogin());
        menu.Items.Add("-");
        menu.Items.Add("退出", null, (_, _) => Application.Current.Shutdown());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowWindow();

        _captureTimer.Tick += async (_, _) => await SafeCapture(manual: false);
        _captureTimer.Start();
        _qqWasRunning = QQMusicRunning();
        _watchTimer.Tick += OnWatchTick;
        _watchTimer.Start();
        Dispatcher.CurrentDispatcher.InvokeAsync(async () => await SafeCapture(manual: false), DispatcherPriority.Background);
    }

    public void ShowWindow()
    {
        if (_window == null)
        {
            _window = new MainWindow(this);
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void SetStatus(string msg)
    {
        AppPaths.Log(msg);
        StatusChanged?.Invoke(this, msg);
    }

    private static bool QQMusicRunning() => Process.GetProcessesByName("QQMusic").Length > 0;

    private async void OnWatchTick(object? sender, EventArgs e)
    {
        var running = QQMusicRunning();
        if (running && !_qqWasRunning)
        {
            if (Settings.ShowWithQQMusic) ShowWindow();
            await SafeCapture(manual: false);
        }
        _qqWasRunning = running;
    }

    // ---- capture ----

    public async Task CaptureNow()
    {
        SetStatus("正在捕获今天的每日30首…");
        var day = await new Capturer(Client, Store).CaptureToday();
        SetStatus($"已记录 {new DateItem(day.Date).Label} 的 {day.Songs.Count} 首");
        Captured?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Automatic runs skip when today is already stored; failures only go to the status bar.</summary>
    private async Task SafeCapture(bool manual)
    {
        if (!manual && Store.Dates().FirstOrDefault() == DateTime.Today.ToString("yyyy-MM-dd")) return;
        try { await CaptureNow(); }
        catch (NotLoggedInException) { SetStatus("登录已失效，请重新登录"); RequestLogin(); }
        catch (Exception e) { SetStatus("捕获失败：" + e.Message); }
    }

    // ---- playlists ----

    public async Task FillWorkspace(DayList day)
    {
        SetStatus("正在写入工作区歌单…");
        var ws = await Client.EnsurePlaylist(Settings.WorkspaceName);
        await Client.ReplaceSongs(ws, day.Songs);
        SetStatus($"已把 {new DateItem(day.Date).Label} 的 {day.Songs.Count} 首放进「{Settings.WorkspaceName}」");
    }

    public async Task SaveAsPlaylist(DayList day, string name)
    {
        SetStatus($"正在创建歌单「{name}」…");
        var pl = await Client.EnsurePlaylist(name);
        await Client.ReplaceSongs(pl, day.Songs);
        SetStatus($"已另存为歌单「{name}」");
    }

    public void OpenQQMusic()
    {
        try { Process.Start(new ProcessStartInfo(Settings.ResolveQQMusicExe()) { UseShellExecute = true }); }
        catch (Exception e) { SetStatus("打开QQ音乐失败：" + e.Message); }
    }

    public void RequestLogin()
    {
        if (_loginOpen) return;
        _loginOpen = true;
        try
        {
            var w = new LoginWindow();
            if (w.ShowDialog() == true && w.Cookies != null)
            {
                Client = new QQMusicClient(w.Cookies);
                SetStatus("登录成功");
                _ = SafeCapture(manual: false);
            }
        }
        finally { _loginOpen = false; }
    }

    private static Icon LoadIcon()
    {
        using var s = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
        return new Icon(s, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose();
    }
}
