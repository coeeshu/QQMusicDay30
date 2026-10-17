using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
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
            img.DecodePixelWidth = 80;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
        }
        catch { img = null; }
        Cache[url] = img;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cover)));
    }
}

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private DayList? _current;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        AutoStartBox.IsChecked = host.Settings.AutoStartWithWindows;
        FollowQQBox.IsChecked = host.Settings.ShowWithQQMusic;
        host.Captured += (_, _) => Dispatcher.Invoke(() => Reload(keepSelection: true));
        host.StatusChanged += (_, msg) => Dispatcher.Invoke(() => StatusText.Text = msg);
        Reload(keepSelection: false);
    }

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
        _current = day;
        if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = "";
        PlayBtn.IsEnabled = SaveAsBtn.IsEnabled = day != null;
        if (day == null)
        {
            TitleText.Text = "还没有记录";
            SubText.Text = "点「立即捕获今天」记录今天的每日30首";
            SongList.ItemsSource = null;
            return;
        }
        TitleText.Text = $"{new DateItem(day.Date).Label} 每日30首";
        SubText.Text = $"{day.Songs.Count} 首 · 捕获于 {day.CapturedAt:yyyy-MM-dd HH:mm}";
        SongList.ItemsSource = day.Songs.Select((s, i) => new SongRow(i + 1, s, null)).ToList();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var k = SearchBox.Text.Trim();
        SearchHint.Visibility = k.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (k.Length == 0)
        {
            if (_current != null) SongList.ItemsSource = _current.Songs.Select((s, i) => new SongRow(i + 1, s, null)).ToList();
            return;
        }
        var hits = _host.Store.Search(k);
        TitleText.Text = $"搜索「{k}」";
        SubText.Text = $"在历史每日30首中找到 {hits.Count} 首";
        SongList.ItemsSource = hits.Select((h, i) => new SongRow(i + 1, h.Song, h.Date)).ToList();
        _current = null;
        PlayBtn.IsEnabled = SaveAsBtn.IsEnabled = false;
    }

    private async Task Busy(Func<Task> work)
    {
        IsEnabled = false;
        try { await work(); }
        catch (Exception ex)
        {
            StatusText.Text = "出错：" + ex.Message;
            AppPaths.Log(ex.ToString());
            if (ex is NotLoggedInException) _host.RequestLogin();
        }
        finally { IsEnabled = true; }
    }

    private async void PlayBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { } day) return;
        await Busy(async () =>
        {
            await _host.FillWorkspace(day);
            _host.OpenQQMusic();
        });
    }

    private async void SaveAsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { } day) return;
        var d = DateTime.ParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        await Busy(() => _host.SaveAsPlaylist(day, $"{d.Year}.{d.Month}.{d.Day}每日30"));
    }

    private async void CaptureBtn_Click(object sender, RoutedEventArgs e) => await Busy(_host.CaptureNow);

    private void SettingBox_Click(object sender, RoutedEventArgs e)
    {
        _host.Settings.AutoStartWithWindows = AutoStartBox.IsChecked == true;
        _host.Settings.ShowWithQQMusic = FollowQQBox.IsChecked == true;
        _host.Settings.Save();
    }
}
