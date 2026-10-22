namespace QQDay30.Core;

/// <summary>Helpers for the y.qq.com login cookies.</summary>
public static class CookieStore
{
    /// <summary>Login key: qqmusic_key / qm_keyst for QQ login, wxqqmusic_key... for WeChat login.</summary>
    public static string? MusicKey(IReadOnlyDictionary<string, string> c) =>
        new[] { "qqmusic_key", "qm_keyst", "music_key" }.Select(k => c.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
