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
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.8) };
    private DayList? _day;           // the day being shown (kept while a search is active)
    private bool _searching;
    private DateTime _staggerUntil;  // rows created before this moment play the entrance animation
    private int _busy;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += (_, _) => PlayEntrance();
        AutoStartBox.IsChecked = host.Settings.AutoStartWithWindows;
        FollowQQBox.IsChecked = host.Settings.ShowWithQQMusic;
        host.Captured += OnCaptured;
        host.StatusChanged += OnStatus;
        Closed += (_, _) => { host.Captured -= OnCaptured; host.StatusChanged -= OnStatus; };
        _toastTimer.Tick += (_, _) => HideToast();
        Reload(keepSelection: false);
    }

    private void OnCaptured(object? s, EventArgs e) => Dispatcher.Invoke(() => Reload(keepSelection: true));
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
        ToastDot.Fill = new SolidColorBrush(error ? Color.FromRgb(0xE5, 0x48, 0x4D) : Color.FromRgb(0x31, 0xC2, 0x7C));
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
        var dates = _host.Store.Dates();
        DateList.ItemsSource = dates.Select(d => new DateItem(d)).ToList();
        CountText.Text = dates.Count == 0 ? "还没有记录" : $"已记录 {dates.Count} 天";
        var idx = keepSelection && selected != null ? dates.IndexOf(selected) : 0;
        DateList.SelectedIndex = dates.Count == 0 ? -1 : Math.Max(idx, 0);
        if (dates.Count == 0) ShowDay(null);
    }

    private void DateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DateList.SelectedItem is DateItem d) ShowDay(_host.Store.GetDay(d.Date));
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
            TitleText.Text = "还没有记录";
            SubText.Text = "点「立即同步」记录今天的每日30首";
            SongList.ItemsSource = null;
        }
        else
        {
            TitleText.Text = $"{new DateItem(day.Date).Label} 每日30首";
            SubText.Text = day.Title.Contains("历史推荐")
                ? $"{day.Songs.Count} 首 · 来自手机端历史推荐"
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
        var hits = _host.Store.Search(k);
        TitleText.Text = $"搜索「{k}」";
        SubText.Text = $"在历史每日30首中找到 {hits.Count} 首";
        SetRows(hits.Select((h, i) => new SongRow(i + 1, h.Song, h.Date)));
        PlayBtn.IsEnabled = SaveAsBtn.IsEnabled = false;
    }

    private void SearchBox_Focus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var to = SearchBox.IsKeyboardFocused ? Color.FromRgb(0x31, 0xC2, 0x7C) : Colors.Transparent;
        var brush = new SolidColorBrush(((SolidColorBrush)SearchBorder.BorderBrush).Color);
        SearchBorder.BorderBrush = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(200)));
    }

    // ---- actions ----

    private async Task Busy(Func<Task<string>> work)
    {
        SetBusy(true);
        try { ShowToast(await work()); }
        catch (Exception ex)
        {
            StatusText.Text = "出错：" + ex.Message;
            ShowToast(ex.Message, error: true);
            AppPaths.Log(ex.ToString());
            if (ex is NotLoggedInException) _host.RequestLogin();
        }
        finally { SetBusy(false); }
    }

    private async void PlayBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_day is not { } day) return;
        await Busy(async () =>
        {
            await _host.FillWorkspace(day);
            _host.OpenQQMusic();
            return $"已放进「{_host.Settings.WorkspaceName}」，在QQ音乐里点开即可播放";
        });
    }

    private async void SaveAsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_day is not { } day) return;
        var d = DateTime.ParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var name = $"{d.Year}.{d.Month}.{d.Day}每日30";
        await Busy(async () =>
        {
            await _host.SaveAsPlaylist(day, name);
            return $"已另存为歌单「{name}」";
        });
    }

    private async void CaptureBtn_Click(object sender, RoutedEventArgs e) => await Busy(async () =>
    {
        var added = await _host.CaptureNow();
        return added > 0 ? $"已记录今天，并从历史推荐补回 {added} 天" : "已记录今天的每日30首";
    });

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void SettingBox_Click(object sender, RoutedEventArgs e)
    {
        _host.Settings.AutoStartWithWindows = AutoStartBox.IsChecked == true;
        _host.Settings.ShowWithQQMusic = FollowQQBox.IsChecked == true;
        _host.Settings.Save();
    }
}
