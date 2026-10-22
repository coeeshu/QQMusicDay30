using System.IO;
using System.Text.Json;

namespace QQDay30.Core;

public enum Platform { QQ, Netease }

/// <summary>One music service the app follows: how to log in to it, its client, and its own local history.</summary>
public sealed class Account
{
    public Platform Platform { get; }
    /// <summary>Full name, e.g. 网易云音乐.</summary>
    public string Name { get; }
    /// <summary>Name of the daily list, e.g. 每日推荐.</summary>
    public string ListName { get; }
    /// <summary>Where past days come from, e.g. 历史日推.</summary>
    public string HistoryName { get; }
    /// <summary>Process name of the desktop client.</summary>
    public string ProcessName { get; }
    public string LoginUrl { get; }
    public Store Store { get; }
    public IMusicClient Client { get; private set; }
    public bool LoggedIn { get; private set; }

    private readonly string _cookieFile;

    private Account(Platform platform, string name, string listName, string historyName, string processName,
        string loginUrl, string cookieFile, string database)
    {
        Platform = platform;
        Name = name;
        ListName = listName;
        HistoryName = historyName;
        ProcessName = processName;
        LoginUrl = loginUrl;
        _cookieFile = Path.Combine(AppPaths.DataDir, cookieFile);
        Store = new Store(Path.Combine(AppPaths.DataDir, database));
        var cookies = LoadCookies();
        Client = CreateClient(cookies);
        LoggedIn = LooksLoggedIn(cookies);
    }

    public static Account QQ() => new(Platform.QQ, "QQ音乐", "每日30首", "手机端历史推荐", "QQMusic",
        "https://y.qq.com/", "cookie.json", "day30.db");

    public static Account Netease() => new(Platform.Netease, "网易云音乐", "每日推荐", "历史日推", "cloudmusic",
        "https://music.163.com/", "ncm_cookie.json", "ncm_day30.db");

    public bool LooksLoggedIn(IReadOnlyDictionary<string, string> c) => Platform switch
    {
        Platform.QQ => CookieStore.MusicKey(c) != null && (c.ContainsKey("uin") || c.ContainsKey("wxuin")),
        _ => !string.IsNullOrEmpty(c.GetValueOrDefault("MUSIC_U")),
    };

    /// <summary>Stores new login cookies and switches the client over to them.</summary>
    public void SetCookies(Dictionary<string, string> cookies)
    {
        File.WriteAllText(_cookieFile, JsonSerializer.Serialize(cookies, new JsonSerializerOptions { WriteIndented = true }));
        Client = CreateClient(cookies);
        LoggedIn = LooksLoggedIn(cookies);
    }

    private Dictionary<string, string> LoadCookies()
    {
        try
        {
            if (File.Exists(_cookieFile))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_cookieFile)) ?? new();
        }
        catch (Exception e) { AppPaths.Log($"{Name} cookie load failed: " + e.Message); }
        return new();
    }

    private IMusicClient CreateClient(Dictionary<string, string> cookies) =>
        Platform == Platform.QQ ? new QQMusicClient(cookies) : new NeteaseClient(cookies);
}
