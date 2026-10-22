using System.IO;

namespace QQDay30.Core;

public static class AppPaths
{
    public static string DataDir { get; } = Init();

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "log.txt");
    public static string WebViewDir => Path.Combine(DataDir, "webview");

    private static string Init()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QQDay30");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void Log(string msg)
    {
        try { File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}"); }
        catch { }
    }
}
