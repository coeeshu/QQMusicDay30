using System.IO;
using System.Text.Json;

namespace QQDay30.Core;

/// <summary>y.qq.com cookies captured from the login WebView.</summary>
public static class CookieStore
{
    public static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(AppPaths.CookieFile))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(AppPaths.CookieFile)) ?? new();
        }
        catch (Exception e) { AppPaths.Log("cookie load failed: " + e.Message); }
        return new();
    }

    public static void Save(Dictionary<string, string> cookies)
    {
        File.WriteAllText(AppPaths.CookieFile, JsonSerializer.Serialize(cookies, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Login key: qqmusic_key / qm_keyst for QQ login, wxqqmusic_key... for WeChat login.</summary>
    public static string? MusicKey(IReadOnlyDictionary<string, string> c) =>
        new[] { "qqmusic_key", "qm_keyst", "music_key" }.Select(k => c.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrEmpty(v));

    public static bool LooksLoggedIn(IReadOnlyDictionary<string, string> c) =>
        MusicKey(c) != null && (c.ContainsKey("uin") || c.ContainsKey("wxuin"));
}
