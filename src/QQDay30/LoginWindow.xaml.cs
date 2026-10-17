using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using QQDay30.Core;

namespace QQDay30;

public partial class LoginWindow : Window
{
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1.5) };

    public Dictionary<string, string>? Cookies { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += async (_, _) =>
        {
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDir);
            await Web.EnsureCoreWebView2Async(env);
            Web.CoreWebView2.Navigate("https://y.qq.com/");
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
        var list = await Web.CoreWebView2.CookieManager.GetCookiesAsync("https://y.qq.com");
        var dict = new Dictionary<string, string>();
        foreach (var c in list) dict[c.Name] = c.Value;
        if (!CookieStore.LooksLoggedIn(dict)) return;

        _poll.Stop();
        Cookies = dict;
        CookieStore.Save(dict);
        AppPaths.Log("login captured, cookie names: " + string.Join(",", dict.Keys));
        DialogResult = true;
    }
}
