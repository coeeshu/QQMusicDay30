namespace QQDay30.Core;

/// <summary>Fetches today's list and stores it under the date it was generated for.</summary>
public sealed class Capturer(Account account)
{
    public async Task<DayList> CaptureToday()
    {
        var day = await account.Client.GetToday();
        if (day.Songs.Count == 0) throw new Exception($"{account.ListName}为空，可能是登录失效");
        account.Store.SaveDay(day);
        AppPaths.Log($"{account.Name} captured {day.Date}: {day.Songs.Count} songs");
        return day;
    }

    /// <summary>Pulls the past days the server keeps that are not stored yet. Returns how many days were added.</summary>
    public async Task<int> BackfillHistory()
    {
        var known = account.Store.Dates().ToHashSet();
        var added = 0;
        foreach (var date in await account.Client.GetHistoryDates())
        {
            var key = date.ToString("yyyy-MM-dd");
            if (known.Contains(key)) continue;
            var songs = await account.Client.GetHistoryDay(date);
            if (songs.Count == 0) continue;
            account.Store.SaveDay(new DayList(key, 0, $"{account.ListName}（{account.HistoryName}）", DateTime.Now, songs));
            added++;
        }
        if (added > 0) AppPaths.Log($"{account.Name} history backfill: {added} days");
        return added;
    }
}
