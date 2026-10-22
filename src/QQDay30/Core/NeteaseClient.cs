using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QQDay30.Core;

/// <summary>Client over the music.163.com web ("weapi") endpoints, authenticated with the web login cookies.</summary>
public sealed class NeteaseClient : IMusicClient
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, string> _cookies;
    private long _uid;

    public NeteaseClient(Dictionary<string, string> cookies)
    {
        _cookies = cookies;
        _http = NewHttp(cookies);
    }

    private static HttpClient NewHttp(IEnumerable<KeyValuePair<string, string>> cookies)
    {
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        http.DefaultRequestHeaders.Add("Referer", "https://music.163.com/");
        http.DefaultRequestHeaders.Add("Origin", "https://music.163.com");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}")));
        return http;
    }

    private string Csrf => _cookies.GetValueOrDefault("__csrf", "");

    // ---- weapi: AES twice (fixed nonce, then a random key), random key sent RSA-encrypted ----

    private const string Nonce = "0CoJUm6Qyw8W8jud";
    private static readonly BigInteger Modulus = BigInteger.Parse(
        "00e0b509f6259df8642dbc35662901477df22677ec152b5ff68ace615bb7b725152b3ab17a876aea8a5aa76d2e417629ec4ee341f56135fccf695280104e0312ecbda92557c93870114af6c9d05c4f7f0c3685b7a46bee255932575cce10b424d813cfe4875d3e82047b97ddef52741d546b8e289dc6935b3ece0462db0a22b8e7",
        NumberStyles.HexNumber);

    private static string AesBase64(string text, string key)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key);
        return Convert.ToBase64String(aes.EncryptCbc(Encoding.UTF8.GetBytes(text), "0102030405060708"u8, PaddingMode.PKCS7));
    }

    private static (string Params, string EncSecKey) Encrypt(string json)
    {
        var key = RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 16);
        // textbook RSA (no padding) over the reversed key
        var m = new BigInteger(Encoding.UTF8.GetBytes(key).Reverse().ToArray(), isUnsigned: true, isBigEndian: true);
        var rsa = BigInteger.ModPow(m, 65537, Modulus).ToByteArray(isUnsigned: true, isBigEndian: true);
        return (AesBase64(AesBase64(json, Nonce), key), Convert.ToHexStringLower(rsa).PadLeft(256, '0'));
    }

    /// <summary>Calls one weapi endpoint (path without the "/weapi/" prefix) and returns the whole response.</summary>
    public async Task<JsonNode> Weapi(string path, object data)
    {
        var body = JsonSerializer.SerializeToNode(data)!.AsObject();
        body["csrf_token"] = Csrf;
        var (p, k) = Encrypt(body.ToJsonString());
        using var resp = await _http.PostAsync($"https://music.163.com/weapi/{path}?csrf_token={Csrf}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["params"] = p, ["encSecKey"] = k }));
        var text = await resp.Content.ReadAsStringAsync();
        var root = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        var code = root?["code"]?.GetValue<int>() ?? (int)resp.StatusCode;
        if (code is 301 or 302) throw new NotLoggedInException($"{path} 登录已失效 (code {code})");
        if (code != 200) throw new Exception($"{path} 失败 (code {code}) {root?["message"] ?? root?["msg"]}");
        return root!;
    }

    // ---- login ----

    // MUSIC_U (the login) lasts about half a year. Writes also need __csrf, whose cookie says 15 days, but its value
    // stays the same for one MUSIC_U and we send stored values ourselves, so nothing is renewed on a schedule.
    public DateTime KeyCreatedAt => DateTime.MaxValue;

    /// <summary>After a login error: the home page sometimes hands out a fresh __csrf (only to a request without one).</summary>
    public async Task<Dictionary<string, string>> RefreshLogin()
    {
        // the server only hands out __csrf to a request that has none
        using var http = NewHttp(_cookies.Where(kv => kv.Key != "__csrf"));
        using var resp = await http.GetAsync("https://music.163.com/");
        var updated = new Dictionary<string, string>(_cookies);
        var renewed = false;
        foreach (var header in resp.Headers.TryGetValues("Set-Cookie", out var v) ? v : [])
        {
            var pair = header.Split(';')[0].Split('=', 2);
            if (pair.Length != 2 || pair[1].Length == 0) continue;
            var name = pair[0].Trim();
            if (name is not ("__csrf" or "MUSIC_U")) continue;
            updated[name] = pair[1];
            renewed |= name == "__csrf";
        }
        // no fresh __csrf means MUSIC_U itself is no longer accepted
        if (!renewed) throw new NotLoggedInException("网易云音乐登录已失效，需要重新登录");
        return updated;
    }

    private async Task<long> Uid()
    {
        if (_uid != 0) return _uid;
        var root = await Weapi("w/nuser/account/get", new { });
        _uid = root["account"]?["id"]?.GetValue<long>() ?? root["profile"]?["userId"]?.GetValue<long>() ?? 0;
        if (_uid == 0) throw new NotLoggedInException("网易云音乐未登录");
        return _uid;
    }

    // ---- daily recommend ----

    /// <summary>The daily recommendation refreshes at 06:00 (UTC+8); before that it is still yesterday's.</summary>
    private static DateTime RecommendDay()
    {
        var beijing = DateTime.UtcNow.AddHours(8);
        return (beijing.Hour < 6 ? beijing.AddDays(-1) : beijing).Date;
    }

    public async Task<DayList> GetToday()
    {
        var root = await Weapi("v3/discovery/recommend/songs", new { });
        var songs = Songs(root["data"]?["dailySongs"]);
        return new DayList(RecommendDay().ToString("yyyy-MM-dd"), 0, "每日推荐", DateTime.Now, songs);
    }

    /// <summary>「历史日推」 (VIP): the last few days you opened, within 60 days.</summary>
    public async Task<List<DateTime>> GetHistoryDates()
    {
        var root = await Weapi("discovery/recommend/songs/history/recent", new { });
        return (root["data"]?["dates"]?.AsArray() ?? [])
            .Select(d => DateTime.TryParseExact(d?.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : (DateTime?)null)
            .OfType<DateTime>()
            .ToList();
    }

    public async Task<List<Song>> GetHistoryDay(DateTime day)
    {
        var root = await Weapi("discovery/recommend/songs/history/detail", new { date = day.ToString("yyyy-MM-dd") });
        return Songs(root["data"]?["songs"]);
    }

    private static List<Song> Songs(JsonNode? list) =>
        (list?.AsArray() ?? []).Where(s => s != null).Select(s => ParseSong(s!)).ToList();

    private static Song ParseSong(JsonNode s) => new(
        s["id"]!.GetValue<long>(),
        "",
        0,
        s["name"]?.GetValue<string>() ?? "",
        string.Join(" / ", (s["ar"] ?? s["artists"])?.AsArray().Select(x => x?["name"]?.GetValue<string>()) ?? []),
        (s["al"] ?? s["album"])?["name"]?.GetValue<string>() ?? "",
        (s["al"] ?? s["album"])?["picUrl"]?.GetValue<string>() ?? "",
        (int)((s["dt"] ?? s["duration"])?.GetValue<long>() ?? 0) / 1000);

    // ---- playlists ----

    public async Task<UserPlaylist> EnsurePlaylist(string name)
    {
        var uid = await Uid();
        var root = await Weapi("user/playlist", new { uid, limit = 1000, offset = 0, includeVideo = true });
        // own playlists only; specialType 5 is 我喜欢的音乐
        var existing = (root["playlist"]?.AsArray() ?? [])
            .Where(p => p != null && p["userId"]?.GetValue<long>() == uid && p["specialType"]?.GetValue<int>() == 0)
            .FirstOrDefault(p => p!["name"]?.GetValue<string>() == name);
        if (existing != null)
            return new UserPlaylist(0, existing["id"]!.GetValue<long>(), name, existing["trackCount"]?.GetValue<int>() ?? 0);

        var created = await Weapi("playlist/create", new { name, privacy = "0", type = "NORMAL" });
        var id = created["id"]?.GetValue<long>() ?? created["playlist"]!["id"]!.GetValue<long>();
        AppPaths.Log($"created netease playlist {name}");
        return new UserPlaylist(0, id, name, 0);
    }

    public async Task ReplaceSongs(UserPlaylist pl, IReadOnlyList<Song> songs)
    {
        var detail = await Weapi("v6/playlist/detail", new { id = pl.Tid, n = 100000, s = 8 });
        var current = (detail["playlist"]?["trackIds"]?.AsArray() ?? [])
            .Select(t => t?["id"]?.GetValue<long>() ?? 0).Where(id => id != 0).ToList();
        foreach (var chunk in current.Chunk(100))
            await EditTracks("del", pl, chunk);

        // each added song goes on top, so feed the list back to front
        foreach (var chunk in songs.Select(s => s.Id).Reverse().Chunk(100))
            await EditTracks("add", pl, chunk);
    }

    private Task<JsonNode> EditTracks(string op, UserPlaylist pl, long[] ids) =>
        Weapi("playlist/manipulate/tracks", new { op, pid = pl.Tid, trackIds = JsonSerializer.Serialize(ids), imme = "true" });
}
