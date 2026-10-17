using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace QQDay30.Core;

public sealed class NotLoggedInException(string msg) : Exception(msg);

/// <summary>Thin client over the y.qq.com web endpoints, authenticated with the captured cookies.</summary>
public sealed class QQMusicClient
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, string> _cookies;

    public string Uin { get; }

    public QQMusicClient(Dictionary<string, string> cookies)
    {
        _cookies = cookies;
        Uin = (cookies.GetValueOrDefault("uin") ?? cookies.GetValueOrDefault("wxuin") ?? "").TrimStart('o');
        _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        _http.DefaultRequestHeaders.Add("Referer", "https://y.qq.com/");
        _http.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}")));
    }

    public static long GTk(string key)
    {
        long h = 5381;
        foreach (var ch in key) h += (h << 5) + ch;
        return h & 0x7fffffff;
    }

    private long Gtk => GTk(CookieStore.MusicKey(_cookies) ?? "");

    /// <summary>Calls one module/method on musicu.fcg and returns the "data" node.</summary>
    public async Task<JsonNode> Musicu(string module, string method, object param)
    {
        var body = new JsonObject
        {
            ["comm"] = new JsonObject
            {
                ["cv"] = 4747474, ["ct"] = 24, ["format"] = "json", ["inCharset"] = "utf-8", ["outCharset"] = "utf-8",
                ["notice"] = 0, ["platform"] = "yqq.json", ["needNewCode"] = 1,
                ["uin"] = long.TryParse(Uin, out var u) ? u : 0, ["g_tk_new_20200303"] = Gtk, ["g_tk"] = Gtk,
            },
            ["req"] = new JsonObject
            {
                ["module"] = module, ["method"] = method, ["param"] = JsonSerializer.SerializeToNode(param),
            },
        };
        using var resp = await _http.PostAsync("https://u.y.qq.com/cgi-bin/musicu.fcg",
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        var root = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
        var req = root["req"];
        var code = req?["code"]?.GetValue<int>() ?? root["code"]?.GetValue<int>() ?? -1;
        if (code is 1000 or 1001 or 2000) throw new NotLoggedInException($"{module}.{method} 登录已失效 (code {code})");
        if (code != 0) throw new Exception($"{module}.{method} 失败 (code {code})");
        var data = req!["data"]!;
        if (data["retCode"]?.GetValue<int>() is int rc && rc != 0) throw new Exception($"{module}.{method} 失败 (retCode {rc})");
        return data;
    }

    /// <summary>Today's daily 30, read from the account's system directory 202.</summary>
    public async Task<(string Title, string PicUrl, DateTime Modified, List<Song> Songs)> GetDaily30()
    {
        var data = await Musicu("music.srfDissInfo.aiDissInfo", "uniform_get_Dissinfo", new
        {
            dirid = 202, optype = 3, tag = 1, userinfo = 0, pic_dpi = 300, song_begin = 0, song_num = 100,
        });
        var result = ParsePlaylist(data);
        if (result.Songs.Count > 0) return result;
        // some accounts may not answer by directory; fall back to the "今日私享" playlist id
        return await GetPlaylist(await FindDaily30Id());
    }

    /// <summary>The "今日私享" playlist id from the mac recommend page (old route to the daily 30).</summary>
    public async Task<long> FindDaily30Id()
    {
        var html = await _http.GetStringAsync("https://c.y.qq.com/node/musicmac/v6/index.html");
        var i = html.IndexOf("今日私享", StringComparison.Ordinal);
        var m = i > 0 ? Regex.Matches(html[..i], "data-rid=\"(\\d+)\"").LastOrDefault() : null;
        return m != null ? long.Parse(m.Groups[1].Value) : throw new Exception("找不到每日30首歌单");
    }

    public async Task<(string Title, string PicUrl, DateTime Modified, List<Song> Songs)> GetPlaylist(long tid)
    {
        var data = await Musicu("music.srfDissInfo.aiDissInfo", "uniform_get_Dissinfo", new
        {
            disstid = tid, userinfo = 1, tag = 1, orderlist = 1, song_begin = 0, song_num = 1000, onlysonglist = 0, enc_host_uin = "",
        });
        return ParsePlaylist(data);
    }

    private static (string Title, string PicUrl, DateTime Modified, List<Song> Songs) ParsePlaylist(JsonNode data)
    {
        var dir = data["dirinfo"];
        var mtime = dir?["mtime"]?.GetValue<long>() ?? 0;
        var songs = (data["songlist"]?.AsArray() ?? []).Where(s => s != null).Select(s => ParseSong(s!)).ToList();
        return (dir?["title"]?.GetValue<string>() ?? "", dir?["picurl"]?.GetValue<string>() ?? "", DateTimeOffset.FromUnixTimeSeconds(mtime).LocalDateTime, songs);
    }

    private static Song ParseSong(JsonNode s) => new(
        s["id"]!.GetValue<long>(),
        s["mid"]?.GetValue<string>() ?? "",
        s["type"]?.GetValue<int>() ?? 0,
        s["name"]?.GetValue<string>() ?? "",
        string.Join(" / ", s["singer"]?.AsArray().Select(x => x?["name"]?.GetValue<string>()) ?? []),
        s["album"]?["name"]?.GetValue<string>() ?? "",
        s["album"]?["mid"]?.GetValue<string>() ?? "",
        s["interval"]?.GetValue<int>() ?? 0);

    /// <summary>
    /// The mobile app's 「历史推荐」: past daily-30 lists the server still keeps (about the last 7 days you opened).
    /// Without a date it returns the date list plus the newest day's songs; with one it returns that day.
    /// </summary>
    public async Task<(List<DateTime> Dates, List<Song> Songs)> GetDaily30History(DateTime? day = null)
    {
        object param = day is { } d
            ? new { dirid = 202, tid = 0, spec = new { bSet = true, year = d.Year, month = d.Month, day = d.Day } }
            : new { dirid = 202, tid = 0 };
        var data = await Musicu("music.srfDissInfo.aiHistory", "get_aiHistoryinfo", param);
        var dates = (data["items"]?.AsArray() ?? [])
            .Where(i => i != null)
            .Select(i => new DateTime(i!["year"]!.GetValue<int>(), i["month"]!.GetValue<int>(), i["day"]!.GetValue<int>()))
            .ToList();
        var songs = (data["songlist"]?.AsArray() ?? [])
            .Select(s => s?["track"])
            .Where(t => t != null)
            .Select(t => ParseSong(t!))
            .ToList();
        return (dates, songs);
    }

    public async Task<List<UserPlaylist>> GetMyPlaylists()
    {
        var data = await Musicu("music.musicasset.PlaylistBaseRead", "GetPlaylistByUin", new { uin = Uin, bFmtUtf8 = true });
        return (data["v_playlist"]?.AsArray() ?? [])
            .Where(p => p != null)
            .Select(p => new UserPlaylist(p!["dirId"]!.GetValue<int>(), p["tid"]!.GetValue<long>(),
                p["dirName"]?.GetValue<string>() ?? "", p["songNum"]?.GetValue<int>() ?? 0))
            .ToList();
    }

    /// <summary>Finds one of the user's own playlists by name, creating it when missing.</summary>
    public async Task<UserPlaylist> EnsurePlaylist(string name)
    {
        // dirId >= 200 are system directories (201 = 我喜欢)
        var existing = (await GetMyPlaylists()).FirstOrDefault(p => p.DirId < 200 && p.Name == name);
        if (existing != null) return existing;

        var data = await Musicu("music.musicasset.PlaylistBaseWrite", "AddPlaylist", new { dirName = name });
        var r = data["result"]!;
        AppPaths.Log($"created playlist {name}");
        return new UserPlaylist(r["dirId"]!.GetValue<int>(), r["tid"]!.GetValue<long>(), name, 0);
    }

    /// <summary>Clears the playlist and fills it with songs, keeping their order.</summary>
    public async Task ReplaceSongs(UserPlaylist pl, IReadOnlyList<Song> songs)
    {
        var (_, _, _, current) = await GetPlaylist(pl.Tid);
        foreach (var chunk in current.Chunk(100))
            await EditSongs("DelSonglist", pl, chunk);

        // the server shows each added batch newest-first, so feed it back to front
        foreach (var chunk in songs.Reverse().Chunk(100))
            await EditSongs("AddSonglist", pl, chunk);
    }

    private Task<JsonNode> EditSongs(string method, UserPlaylist pl, IEnumerable<Song> songs) =>
        Musicu("music.musicasset.PlaylistDetailWrite", method, new
        {
            dirId = pl.DirId, tid = pl.Tid, bFmtUtf8 = true,
            v_songInfo = songs.Select(s => new { songId = s.Id, songType = s.Type }).ToArray(),
        });
}
