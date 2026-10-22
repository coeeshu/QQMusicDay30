namespace QQDay30.Core;

/// <summary>A song. QQ Music stores the album mid in AlbumMid; NetEase has no mid, so it keeps the cover URL there.</summary>
public sealed record Song(long Id, string Mid, int Type, string Name, string Singers, string Album, string AlbumMid, int Duration)
{
    public string CoverUrl => AlbumMid switch
    {
        "" => "",
        _ when AlbumMid.StartsWith("http") => AlbumMid + "?param=150y150",
        _ => $"https://y.gtimg.cn/music/photo_new/T002R150x150M000{AlbumMid}.jpg",
    };
    public string DurationText => $"{Duration / 60:00}:{Duration % 60:00}";
}

public sealed record DayList(string Date, long DissId, string Title, DateTime CapturedAt, IReadOnlyList<Song> Songs);

public sealed record UserPlaylist(int DirId, long Tid, string Name, int SongNum);
