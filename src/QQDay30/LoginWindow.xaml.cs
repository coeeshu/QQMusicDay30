using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using QQDay30.Core;

namespace QQDay30;

public partial class LoginWindow : Window
{
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly Account _account;

    public Dictionary<string, string>? Cookies { get; private set; }

    public LoginWindow(Account account)
    {
        _account = account;
        InitializeComponent();
        Title = $"登录{account.Name} - 每日30首";
        HeadText.Text = $"登录{account.Name}";
        Hint.Foreground = new SolidColorBrush(Accents.Of(account.Platform));
        Hint.Text = account.Platform == Platform.QQ
            ? "点击网页右上角「登录」，用QQ或微信扫码，登录成功后窗口会自动关闭"
            : "点击网页右侧的「用户登录」，用网易云音乐 App 扫码，登录成功后窗口会自动关闭";
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += async (_, _) =>
        {
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDir);
            await Web.EnsureCoreWebView2Async(env);
            // music.163.com is laid out for a wider window than this one
            if (account.Platform == Platform.Netease) Web.ZoomFactor = 0.85;
            Web.CoreWebView2.Navigate(account.LoginUrl);
            _poll.Tick += async (_, _) => await CheckCookies();
            _poll.Start();
        };
        Closed += (_, _) => _poll.Stop();
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private async Task CheckCookies()
    {
        if (Web.CoreWebView2 == null) return;
        var list = await Web.CoreWebView2.CookieManager.GetCookiesAsync(_account.LoginUrl);
        var dict = new Dictionary<string, string>();
        foreach (var c in list) dict[c.Name] = c.Value;
        if (!_account.LooksLoggedIn(dict)) return;

        _poll.Stop();
        Cookies = dict;
        _account.SetCookies(dict);
        AppPaths.Log($"{_account.Name} login captured, cookies: " +
            string.Join(",", list.Select(c => c.IsSession ? c.Name : $"{c.Name}(until {c.Expires:yyyy-MM-dd})")));
        DialogResult = true;
    }
}
