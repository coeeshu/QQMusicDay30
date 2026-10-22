using Microsoft.Data.Sqlite;

namespace QQDay30.Core;

/// <summary>Local history of captured daily-30 lists.</summary>
public sealed class Store
{
    private readonly string _cs;

    public Store(string path)
    {
        _cs = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var c = Open();
        Exec(c, """
            CREATE TABLE IF NOT EXISTS days(
                date TEXT PRIMARY KEY, diss_id INTEGER, title TEXT, captured_at TEXT);
            CREATE TABLE IF NOT EXISTS songs(
                date TEXT, pos INTEGER, song_id INTEGER, song_mid TEXT, song_type INTEGER,
                name TEXT, singers TEXT, album TEXT, album_mid TEXT, duration INTEGER,
                PRIMARY KEY(date, pos));
            """);
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql, params (string, object)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        cmd.ExecuteNonQuery();
    }

    public void SaveDay(DayList day)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Exec(c, "DELETE FROM songs WHERE date=$d", ("$d", day.Date));
        Exec(c, "INSERT OR REPLACE INTO days VALUES($d,$id,$t,$at)",
            ("$d", day.Date), ("$id", day.DissId), ("$t", day.Title), ("$at", day.CapturedAt.ToString("O")));
        for (var i = 0; i < day.Songs.Count; i++)
        {
            var s = day.Songs[i];
            Exec(c, "INSERT INTO songs VALUES($d,$p,$id,$mid,$ty,$n,$si,$al,$am,$du)",
                ("$d", day.Date), ("$p", i), ("$id", s.Id), ("$mid", s.Mid), ("$ty", s.Type), ("$n", s.Name),
                ("$si", s.Singers), ("$al", s.Album), ("$am", s.AlbumMid), ("$du", s.Duration));
        }
        tx.Commit();
    }

    public List<string> Dates()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT date FROM days ORDER BY date DESC";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public DayList? GetDay(string date)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT diss_id, title, captured_at FROM days WHERE date=$d";
        cmd.Parameters.AddWithValue("$d", date);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var (id, title, at) = (r.GetInt64(0), r.GetString(1), DateTime.Parse(r.GetString(2)));
        return new DayList(date, id, title, at, Songs(c, "WHERE date=$d ORDER BY pos", ("$d", date)).Select(x => x.Song).ToList());
    }

    /// <summary>Search all captured songs by name / singer / album.</summary>
    public List<(string Date, Song Song)> Search(string keyword)
    {
        using var c = Open();
        return Songs(c, "WHERE name LIKE $k OR singers LIKE $k OR album LIKE $k ORDER BY date DESC, pos", ("$k", $"%{keyword}%"));
    }

    private static List<(string Date, Song Song)> Songs(SqliteConnection c, string where, params (string, object)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT date, song_id, song_mid, song_type, name, singers, album, album_mid, duration FROM songs " + where;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        using var r = cmd.ExecuteReader();
        var list = new List<(string, Song)>();
        while (r.Read())
            list.Add((r.GetString(0), new Song(r.GetInt64(1), r.GetString(2), r.GetInt32(3), r.GetString(4),
                r.GetString(5), r.GetString(6), r.GetString(7), r.GetInt32(8))));
        return list;
    }
}
