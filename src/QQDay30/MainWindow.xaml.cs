using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QQDay30.Core;

namespace QQDay30;

public sealed record DateItem(string Date)
{
    private DateTime D => DateTime.ParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string Label => D.Year == DateTime.Today.Year ? $"{D.Month}月{D.Day}日" : $"{D.Year}年{D.Month}月{D.Day}日";
    public string Week => D == DateTime.Today ? "今天" : D.ToString("ddd", new CultureInfo("zh-CN"));
}

public sealed class SongRow(int index, Song song, string? date) : INotifyPropertyChanged
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Dictionary<string, BitmapImage?> Cache = new();

    private bool _loading;
    public int Index { get; } = index;
    public Song Song { get; } = song;
    public string Date { get; } = date ?? "";
    public string DateLabel => string.IsNullOrEmpty(Date) ? "" : new DateItem(Date).Label;
    public bool HasCover => Cover != null;

    public event PropertyChangedEventHandler? PropertyChanged;

    public BitmapImage? Cover
    {
        get
        {
            var url = Song.CoverUrl;
            if (string.IsNullOrEmpty(url)) return null;
            if (Cache.TryGetValue(url, out var img)) return img;
            if (!_loading) { _loading = true; _ = Load(url); }
            return null;
        }
    }

    private async Task Load(string url)
    {
        BitmapImage? img = null;
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = new MemoryStream(bytes);
            img.DecodePixelWidth = 84;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
        }
        catch { img = null; }
        Cache[url] = img;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cover)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCover)));
    }
}

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private Color _accent;               // accent of the service shown (target of any running blend)
    private EventHandler? _accentBlend;
    private Account _account;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.8) };
    private DayList? _day;           // the day being shown (kept while a search is active)
    private bool _searching;
    private DateTime _staggerUntil;  // rows created before this moment play the entrance animation
    private int _busy;

    public MainWindow(AppHost host)
    {
        _host = host;
        _account = host.Get(host.Settings.Current);
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += (_, _) =>
        {
            MoveThumb(animate: false);
            PlayEntrance();
        };
        AutoStartBox.IsChecked = host.Settings.AutoStartWithWindows;
        FollowQQBox.IsChecked = host.Settings.ShowWithQQMusic;
        host.Captured += OnCaptured;
        host.StatusChanged += OnStatus;
        Closed += (_, _) => { host.Captured -= OnCaptured; host.StatusChanged -= OnStatus; };
        _toastTimer.Tick += (_, _) => HideToast();

        ThemeSwitcher.SetMode(ThemeManager.Mode, animate: false);
        ThemeSwitcher.ModeSelected += (mode, origin) =>
        {
            ThemeManager.SetMode(mode, origin);
            _host.Settings.Theme = mode;
            _host.Settings.Save();
        };
        ThemeManager.Transition = RevealSwap;
        Closed += (_, _) => { if (ThemeManager.Transition == RevealSwap) ThemeManager.Transition = null; };

        SwitchTo(_account.Platform, animate: false);
    }

    // ---- service switch ----

    private void SourceTab_Click(object sender, RoutedEventArgs e) =>
        SwitchTo(sender == QQTab ? Platform.QQ : Platform.Netease);

    /// <summary>Shows another service: the thumb slides over, the accent color blends, the lists slide in.</summary>
    public void SwitchTo(Platform platform, bool animate = true)
    {
        if (animate && platform == _account.Platform) return;
        _account = _host.Get(platform);
        if (_host.Settings.Current != platform)
        {
            _host.Settings.Current = platform;
            _host.Settings.Save();
        }

        var color = Accents.Of(platform);
        SetAccent(color, animate);
        var clear = Color.FromArgb(0, color.R, color.G, color.B);
        LoadingDash.Fill = new LinearGradientBrush(new GradientStopCollection { new(clear, 0), new(color, 0.5), new(clear, 1) },
            new Point(0, 0), new Point(1, 0));
        MoveThumb(animate);

        BrandText.Text = _account.ListName;
        PlayBtn.Content = "▶  放到工作区歌单并打开" + (platform == Platform.QQ ? "QQ音乐" : "网易云");
        CaptureBtn.ToolTip = $"捕获今天的{_account.ListName}，并从{_account.HistoryName}补回最近几天";
        _searching = false;
        SearchBox.Text = "";
        Reload(keepSelection: false);
        if (animate)
        {
            Anim.FadeSlideIn(BrandText, 0, 8, 0, 360);
            Anim.FadeSlideIn(CountText, -12, 0, 0, 380);
            Anim.FadeSlideIn(DateList, -20, 0, 40, 460);
        }
    }

    /// <summary>
    /// The accent brush lives in the window resources, which freeze it, so a blend swaps in a new brush every frame.
    /// </summary>
    private void SetAccent(Color to, bool animate)
    {
        if (_accentBlend != null) CompositionTarget.Rendering -= _accentBlend;
        _accentBlend = null;
        var from = Resources["Accent"] is SolidColorBrush b ? b.Color : to;
        _accent = to;
        if (!animate || from == to)
        {
            Resources["Accent"] = new SolidColorBrush(to);
            return;
        }
        var start = DateTime.Now;
        _accentBlend = (_, _) =>
        {
            var t = Math.Min((DateTime.Now - start).TotalMilliseconds / 450, 1);
            Resources["Accent"] = new SolidColorBrush(Blend(from, to, Anim.EaseInOut.Ease(t)));
            if (t >= 1 && _accentBlend != null)
            {
                CompositionTarget.Rendering -= _accentBlend;
                _accentBlend = null;
            }
        };
        CompositionTarget.Rendering += _accentBlend;
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private void MoveThumb(bool animate)
    {
        var selected = _account.Platform == Platform.QQ ? QQTab : NeteaseTab;
        foreach (var tab in new[] { QQTab, NeteaseTab })
        {
            if (tab == selected) tab.Foreground = Brushes.White;
            else tab.SetResourceReference(ForegroundProperty, "Sub");
        }
        var move = (TranslateTransform)SourceThumb.RenderTransform;
        var x = _account.Platform == Platform.QQ ? 0 : QQTab.ActualWidth;
        if (animate)
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 },
            });
        else
        {
            move.BeginAnimation(TranslateTransform.XProperty, null);
            move.X = x;
        }
    }

    /// <summary>
    /// Theme transition: freeze the current look into an overlay, swap colors underneath,
    /// then grow a circular hole from <paramref name="origin"/> until the new theme fills the window.
    /// </summary>
    private void RevealSwap(Action swap, Point? origin, Action done)
    {
        if (Content is not FrameworkElement root || !IsLoaded || WindowState == WindowState.Minimized)
        {
            swap();
            done();
            return;
        }
        double w = root.ActualWidth, h = root.ActualHeight;
        var dpi = VisualTreeHelper.GetDpi(this);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // areas without their own background show the window's; paint it first or they'd be see-through
            dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(0, 0, w, h));
        }
        var shot = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        shot.Render(dv);
        shot.Freeze();

        ThemeOverlay.Source = shot;
        ThemeOverlay.Visibility = Visibility.Visible;
        var fromBorder = DarkTitleBar.BorderFor(ThemeManager.IsDark);
        swap();
        var toBorder = DarkTitleBar.BorderFor(ThemeManager.IsDark);

        var c = origin ?? new Point(w / 2, h / 2);
        var radius = new[] { new Point(0, 0), new Point(w, 0), new Point(0, h), new Point(w, h) }
            .Max(p => (p - c).Length);
        var hole = new EllipseGeometry(c, 0, 0);
        ThemeOverlay.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, w, h)), hole);

        var grow = new DoubleAnimation(0, radius, TimeSpan.FromMilliseconds(680))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        // the DWM border is outside the snapshot: blend its color with the reveal's progress
        void Border(object? s, EventArgs e)
        {
            var t = Math.Clamp(hole.RadiusX / radius, 0, 1);
            DarkTitleBar.SetBorder(this, Color.FromRgb(
                (byte)(fromBorder.R + (toBorder.R - fromBorder.R) * t),
                (byte)(fromBorder.G + (toBorder.G - fromBorder.G) * t),
                (byte)(fromBorder.B + (toBorder.B - fromBorder.B) * t)));
        }
        CompositionTarget.Rendering += Border;
        grow.Completed += (_, _) =>
        {
            CompositionTarget.Rendering -= Border;
            ThemeOverlay.Visibility = Visibility.Collapsed;
            ThemeOverlay.Source = null;
            ThemeOverlay.Clip = null;
            done();
        };
        hole.BeginAnimation(EllipseGeometry.RadiusXProperty, grow);
        hole.BeginAnimation(EllipseGeometry.RadiusYProperty, grow);
    }

    private void OnCaptured(object? s, Platform p) => Dispatcher.Invoke(() =>
    {
        if (p == _account.Platform) Reload(keepSelection: true);
    });
    private void OnStatus(object? s, string msg) => Dispatcher.Invoke(() => StatusText.Text = msg);

    // ---- animations ----

    private void PlayEntrance()
    {
        Anim.FadeSlideIn(Brand, -12, 0, 0, 420);
        Anim.FadeSlideIn(Sidebar, -28, 0, 40, 520);
        Anim.FadeSlideIn(Header, 0, 14, 90, 480);
    }

    private void AnimateHeader()
    {
        Anim.FadeSlideIn(TitleText, 18, 0, 0, 380);
        Anim.FadeSlideIn(SubText, 18, 0, 40, 380);
    }

    private void SetRows(IEnumerable<SongRow> rows)
    {
        _staggerUntil = DateTime.Now.AddMilliseconds(700);
        SongList.ItemsSource = rows.ToList();
        if (SongList.Items.Count > 0) SongList.ScrollIntoView(SongList.Items[0]);
    }

    private void SongItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListViewItem item || DateTime.Now > _staggerUntil) return;
        var index = SongList.ItemContainerGenerator.IndexFromContainer(item);
        Anim.FadeSlideIn(item, 0, 16, Math.Min(Math.Max(index, 0), 14) * 28, 420);
    }

    private void SetBusy(bool on)
    {
        _busy += on ? 1 : -1;
        var busy = _busy > 0;
        PlayBtn.IsEnabled = SaveAsBtn.IsEnabled = !busy && _day != null && !_searching;
        CaptureBtn.IsEnabled = !busy;
        if (busy)
        {
            var t = (TranslateTransform)LoadingDash.RenderTransform;
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-220, Math.Max(ActualWidth, 400), TimeSpan.FromSeconds(1.1))
            {
                RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Anim.EaseInOut,
            });
            Anim.Fade(LoadingBar, 1, 150);
        }
        else Anim.Fade(LoadingBar, 0, 300, () =>
        {
            if (_busy == 0) LoadingDash.RenderTransform.BeginAnimation(TranslateTransform.XProperty, null);
        });
    }

    private void ShowToast(string msg, bool error = false)
    {
        ToastText.Text = msg;
        ToastDot.Fill = new SolidColorBrush(error ? Color.FromRgb(0xE5, 0x48, 0x4D) : _accent);
        Anim.FadeSlideIn(Toast, 0, 24, 0, 420);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        Anim.Fade(Toast, 0, 260);
        Anim.SlideY(Toast, 12, 260);
    }

    // ---- data ----

    private void Reload(bool keepSelection)
    {
        var selected = (DateList.SelectedItem as DateItem)?.Date;
        var dates = _account.Store.Dates();
        DateList.ItemsSource = dates.Select(d => new DateItem(d)).ToList();
        CountText.Text = !_account.LoggedIn ? "未登录" : dates.Count == 0 ? "还没有记录" : $"已记录 {dates.Count} 天";
        CaptureBtn.Content = _account.LoggedIn ? "立即同步" : "登录";
        if (_account.LoggedIn) CaptureBtn.ClearValue(StyleProperty); // back to the implicit pill style
        else CaptureBtn.Style = (Style)FindResource("Primary");
        PlayBtn.Visibility = SaveAsBtn.Visibility = _account.LoggedIn ? Visibility.Visible : Visibility.Collapsed;
        var idx = keepSelection && selected != null ? dates.IndexOf(selected) : 0;
        DateList.SelectedIndex = dates.Count == 0 ? -1 : Math.Max(idx, 0);
        if (dates.Count == 0) ShowDay(null);
    }

    private void DateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DateList.SelectedItem is DateItem d) ShowDay(_account.Store.GetDay(d.Date));
    }

    private void ShowDay(DayList? day)
    {
        _day = day;
        if (_searching)
        {
            _searching = false;
            SearchBox.Text = "";
        }
        PlayBtn.IsEnabled = SaveAsBtn.IsEnabled = day != null && _busy == 0;
        if (day == null)
        {
            TitleText.Text = _account.LoggedIn ? "还没有记录" : $"还没登录{_account.Name}";
            SubText.Text = _account.LoggedIn
                ? $"点「立即同步」记录今天的{_account.ListName}"
                : $"点「登录」扫码，之后每天自动记录{_account.ListName}";
            SongList.ItemsSource = null;
        }
        else
        {
            TitleText.Text = $"{new DateItem(day.Date).Label} {_account.ListName}";
            SubText.Text = day.Title.Contains("历史")
                ? $"{day.Songs.Count} 首 · 来自{_account.HistoryName}"
                : $"{day.Songs.Count} 首 · 捕获于 {day.CapturedAt:yyyy-MM-dd HH:mm}";
            SetRows(day.Songs.Select((s, i) => new SongRow(i + 1, s, null)));
        }
        AnimateHeader();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var k = SearchBox.Text.Trim();
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (k.Length == 0)
        {
            if (!_searching) return;
            _searching = false;
            ShowDay(_day);
            return;
        }
        _searching = true;
        var hits = _account.Store.Search(k);
        TitleText.Text = $"搜索「{k}」";
        SubText.Text = $"在历史{_account.ListName}中找到 {hits.Count} 首";
        SetRows(hits.Select((h, i) => new SongRow(i + 1, h.Song, h.Date)));
        PlayBtn.IsEnabled = SaveAsBtn.IsEnabled = false;
    }

    private void SearchBox_Focus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var to = SearchBox.IsKeyboardFocused ? _accent : Colors.Transparent;
        var brush = new SolidColorBrush(((SolidColorBrush)SearchBorder.BorderBrush).Color);
        SearchBorder.BorderBrush = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(200)));
    }

    // ---- actions ----

    private async Task Busy(Account a, Func<Task<string>> work)
    {
        SetBusy(true);
        try
        {
            try { ShowToast(await work()); }
            catch (NotLoggedInException)
            {
                // the key expired: renew it and run the action once more, else fall back to QR login
                if (!await _host.TryRefreshLogin(a, force: true)) throw;
                ShowToast(await work());
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "出错：" + ex.Message;
            ShowToast(ex.Message, error: true);
            AppPaths.Log(ex.ToString());
            if (ex is NotLoggedInException) _host.RequestLogin(a);
        }
        finally { SetBusy(false); }
    }

    private async void PlayBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_day is not { } day) return;
        var a = _account;
        await Busy(a, async () =>
        {
            var ws = await _host.FillWorkspace(a, day);
            _host.OpenClient(a, ws);
            return a.Platform == Platform.QQ
                ? $"已放进「{ws.Name}」，在QQ音乐里点开即可播放"
                : $"已放进「{ws.Name}」，网易云已开始播放";
        });
    }

    private async void SaveAsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_day is not { } day) return;
        var a = _account;
        var d = DateTime.ParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var name = $"{d.Year}.{d.Month}.{d.Day}" + (a.Platform == Platform.QQ ? "每日30" : "每日推荐");
        await Busy(a, async () =>
        {
            await _host.SaveAsPlaylist(a, day, name);
            return $"已另存为歌单「{name}」";
        });
    }

    private async void CaptureBtn_Click(object sender, RoutedEventArgs e)
    {
        var a = _account;
        if (!a.LoggedIn)
        {
            _host.RequestLogin(a);
            return;
        }
        await Busy(a, async () =>
        {
            var added = await _host.CaptureNow(a);
            return added > 0 ? $"已记录今天，并从{a.HistoryName}补回 {added} 天" : $"已记录今天的{a.ListName}";
        });
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void SettingBox_Click(object sender, RoutedEventArgs e)
    {
        _host.Settings.AutoStartWithWindows = AutoStartBox.IsChecked == true;
        _host.Settings.ShowWithQQMusic = FollowQQBox.IsChecked == true;
        _host.Settings.Save();
    }
}
