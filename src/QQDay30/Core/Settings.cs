using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace QQDay30.Core;

public sealed class Settings
{
    public bool AutoStartWithWindows { get; set; } = true;
    /// <summary>Pop the window up when a music client (QQMusic.exe / cloudmusic.exe) starts.</summary>
    public bool ShowWithQQMusic { get; set; } = true;
    public string WorkspaceName { get; set; } = "每日30·回放";
    public string NeteaseWorkspaceName { get; set; } = "每日推荐·回放";
    /// <summary>The service shown in the window last time.</summary>
    public Platform Current { get; set; } = Platform.QQ;
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    /// <summary>Leave empty to locate QQMusic.exe from the registry.</summary>
    public string QQMusicExe { get; set; } = "";

    public string ResolveQQMusicExe()
    {
        if (File.Exists(QQMusicExe)) return QQMusicExe;
        foreach (var path in new[] { @"SOFTWARE\WOW6432Node\Tencent\QQMusic", @"SOFTWARE\Tencent\QQMusic" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key?.GetValue("Install") is string dir && File.Exists(Path.Combine(dir, "QQMusic.exe")))
                return Path.Combine(dir, "QQMusic.exe");
        }
        throw new FileNotFoundException("没找到QQ音乐，请在 settings.json 里填写 QQMusicExe");
    }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new();
        }
        catch (Exception e) { AppPaths.Log("settings load failed: " + e.Message); }
        return new();
    }

    public void Save()
    {
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        ApplyAutoStart();
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public void ApplyAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;
        if (AutoStartWithWindows) key.SetValue("QQDay30", $"\"{Environment.ProcessPath}\" --tray");
        else key.DeleteValue("QQDay30", false);
    }
}
