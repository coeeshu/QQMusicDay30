using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using QQDay30.Core;
using Forms = System.Windows.Forms;

namespace QQDay30;

/// <summary>Long-lived background part: tray icon, scheduled capture, music client watcher.</summary>
public sealed class AppHost : IDisposable
{
    public Settings Settings { get; } = Settings.Load();
    public IReadOnlyList<Account> Accounts { get; } = [Account.QQ(), Account.Netease()];

    /// <summary>Raised after a capture or login; the argument is the service it concerns.</summary>
    public event EventHandler<Platform>? Captured;
    public event EventHandler<string>? StatusChanged;

    private readonly Forms.NotifyIcon _tray;
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromMinutes(30) };
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly HashSet<Platform> _clientRunning = [];
    private readonly Dictionary<Platform, DateTime> _backfilledOn = [];
    private MainWindow? _window;
    private bool _loginOpen;

    public AppHost()
    {
        Settings.ApplyAutoStart();

        _tray = new Forms.NotifyIcon { Icon = LoadIcon(), Text = "每日30首捕捉器", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => ShowWindow());
        menu.Items.Add("立即同步", null, async (_, _) => await SafeCaptureAll(manual: true));
        var login = new Forms.ToolStripMenuItem("登录 / 重新登录");
        foreach (var a in Accounts) login.DropDownItems.Add(a.Name, null, (_, _) => RequestLogin(a));
        menu.Items.Add(login);
        menu.Items.Add("-");
        menu.Items.Add("退出", null, (_, _) => Application.Current.Shutdown());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowWindow();

        _captureTimer.Tick += async (_, _) => await SafeCaptureAll(manual: false);
        _captureTimer.Start();
        foreach (var a in Accounts)
            if (ClientRunning(a)) _clientRunning.Add(a.Platform);
        _watchTimer.Tick += OnWatchTick;
        _watchTimer.Start();
        Dispatcher.CurrentDispatcher.InvokeAsync(async () => await SafeCaptureAll(manual: false), DispatcherPriority.Background);
    }

    public Account Get(Platform p) => Accounts.First(a => a.Platform == p);

    public bool AnyLoggedIn => Accounts.Any(a => a.LoggedIn);

    /// <summary>Shows the window, optionally switched to one service.</summary>
    public void ShowWindow(Platform? platform = null)
    {
        if (_window == null)
        {
            if (platform != null) Settings.Current = platform.Value;
            _window = new MainWindow(this);
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else if (platform != null) _window.SwitchTo(platform.Value);
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void SetStatus(string msg)
    {
        AppPaths.Log(msg);
        StatusChanged?.Invoke(this, msg);
    }

    private static bool ClientRunning(Account a) => Process.GetProcessesByName(a.ProcessName).Length > 0;

    private async void OnWatchTick(object? sender, EventArgs e)
    {
        foreach (var a in Accounts)
        {
            var running = ClientRunning(a);
            var started = running && _clientRunning.Add(a.Platform);
            if (!running) _clientRunning.Remove(a.Platform);
            if (!started || !a.LoggedIn) continue;
            if (Settings.ShowWithQQMusic) ShowWindow(a.Platform);
            await SafeCapture(a, manual: false);
        }
    }

    // ---- capture ----

    /// <summary>Captures today and backfills missing days from the service's history. Returns the number of backfilled days.</summary>
    public async Task<int> CaptureNow(Account a)
    {
        SetStatus($"正在捕获{a.Name}今天的{a.ListName}…");
        var cap = new Capturer(a);
        var day = await cap.CaptureToday();
        var added = 0;
        try
        {
            added = await cap.BackfillHistory();
            _backfilledOn[a.Platform] = DateTime.Today;
        }
        catch (Exception e) { AppPaths.Log($"{a.Name} history backfill failed: " + e.Message); }
        SetStatus($"{a.Name}：已记录 {new DateItem(day.Date).Label} 的 {day.Songs.Count} 首" + (added > 0 ? $"，并从{a.HistoryName}补回 {added} 天" : ""));
        Captured?.Invoke(this, a.Platform);
        return added;
    }

    private async Task SafeCaptureAll(bool manual)
    {
        foreach (var a in Accounts.Where(a => a.LoggedIn))
            await SafeCapture(a, manual);
    }

    /// <summary>Automatic runs skip when today is stored and history was checked today; failures only go to the status bar.</summary>
    private async Task SafeCapture(Account a, bool manual)
    {
        await TryRefreshLogin(a, force: false);
        if (!manual && _backfilledOn.GetValueOrDefault(a.Platform) == DateTime.Today
            && a.Store.Dates().Contains(DateTime.Today.ToString("yyyy-MM-dd"))) return;
        try { await CaptureNow(a); }
        catch (NotLoggedInException)
        {
            if (await TryRefreshLogin(a, force: true))
            {
                try { await CaptureNow(a); return; }
                catch (Exception e) when (e is not NotLoggedInException) { SetStatus($"{a.Name}捕获失败：" + e.Message); return; }
                catch (NotLoggedInException) { }
            }
            SetStatus($"{a.Name}登录已失效，请重新登录");
            RequestLogin(a);
        }
        catch (Exception e) { SetStatus($"{a.Name}捕获失败：" + e.Message); }
    }

    /// <summary>
    /// QQ Music's key lasts 3 days; renew it once it is a day old (or when forced after a login error).
    /// Returns false when renewal failed and a fresh QR login is needed.
    /// </summary>
    public async Task<bool> TryRefreshLogin(Account a, bool force)
    {
        if (!force && DateTime.Now - a.Client.KeyCreatedAt < TimeSpan.FromDays(1)) return true;
        try
        {
            a.SetCookies(await a.Client.RefreshLogin());
            AppPaths.Log($"{a.Name} login refreshed");
            return true;
        }
        catch (Exception e)
        {
            AppPaths.Log($"{a.Name} login refresh failed: " + e.Message);
            return false;
        }
    }

    // ---- playlists ----

    public string WorkspaceName(Account a) => a.Platform == Platform.QQ ? Settings.WorkspaceName : Settings.NeteaseWorkspaceName;

    public async Task<UserPlaylist> FillWorkspace(Account a, DayList day)
    {
        var name = WorkspaceName(a);
        SetStatus("正在写入工作区歌单…");
        var ws = await a.Client.EnsurePlaylist(name);
        await a.Client.ReplaceSongs(ws, day.Songs);
        SetStatus($"已把 {new DateItem(day.Date).Label} 的 {day.Songs.Count} 首放进{a.Name}歌单「{name}」");
        return ws;
    }

    public async Task SaveAsPlaylist(Account a, DayList day, string name)
    {
        SetStatus($"正在创建歌单「{name}」…");
        var pl = await a.Client.EnsurePlaylist(name);
        await a.Client.ReplaceSongs(pl, day.Songs);
        SetStatus($"已另存为{a.Name}歌单「{name}」");
    }

    /// <summary>Opens the desktop client; NetEase is asked to open (and play) the playlist directly.</summary>
    public void OpenClient(Account a, UserPlaylist playlist)
    {
        try
        {
            var target = a.Platform == Platform.QQ
                ? Settings.ResolveQQMusicExe()
                : "orpheus://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new { type = "playlist", id = playlist.Tid.ToString(), cmd = "play" })));
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception e) { SetStatus($"打开{a.Name}失败：" + e.Message); }
    }

    public bool RequestLogin(Account a)
    {
        if (_loginOpen) return false;
        _loginOpen = true;
        try
        {
            if (new LoginWindow(a).ShowDialog() != true) return false;
            SetStatus($"{a.Name}登录成功");
            Captured?.Invoke(this, a.Platform);
            _ = SafeCapture(a, manual: true);
            return true;
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
