using System.Text.RegularExpressions;

namespace QQDay30.Core;

/// <summary>Fetches today's daily 30 and stores it under the date it was generated for.</summary>
public sealed class Capturer(QQMusicClient client, Store store)
{
    public async Task<DayList> CaptureToday()
    {
        var (title, pic, modified, songs) = await client.GetDaily30();
        if (songs.Count == 0) throw new Exception("每日30首为空，可能是登录失效");

        var day = new DayList(ResolveDate(pic, modified).ToString("yyyy-MM-dd"), 202, title, DateTime.Now, songs);
        store.SaveDay(day);
        AppPaths.Log($"captured {day.Date}: {songs.Count} songs");
        return day;
    }

    /// <summary>Pulls the past days from 「历史推荐」 that are not stored yet. Returns how many days were added.</summary>
    public async Task<int> BackfillHistory()
    {
        var known = store.Dates().ToHashSet();
        var (dates, newest) = await client.GetDaily30History();
        var added = 0;
        for (var i = 0; i < dates.Count; i++)
        {
            var key = dates[i].ToString("yyyy-MM-dd");
            if (known.Contains(key)) continue;
            // the first date's songs come with the date list; the rest need one call each
            var songs = i == 0 ? newest : (await client.GetDaily30History(dates[i])).Songs;
            if (songs.Count == 0) continue;
            store.SaveDay(new DayList(key, 202, "每日30首（历史推荐）", DateTime.Now, songs));
            added++;
        }
        if (added > 0) AppPaths.Log($"history backfill: {added} days");
        return added;
    }

    /// <summary>The cover is a calendar image like ".../calendar/1018_300.jpg"; fall back to the playlist mtime.</summary>
    private static DateTime ResolveDate(string picUrl, DateTime modified)
    {
        var m = Regex.Match(picUrl, @"calendar/(\d{2})(\d{2})_");
        if (m.Success)
        {
            var now = DateTime.Now;
            var d = new DateTime(now.Year, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            // a December cover fetched in early January belongs to last year
            if (d > now.AddDays(2)) d = d.AddYears(-1);
            return d;
        }
        return modified.Year > 2000 ? modified.Date : DateTime.Today;
    }
}
