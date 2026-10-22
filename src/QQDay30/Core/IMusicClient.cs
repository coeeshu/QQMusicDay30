namespace QQDay30.Core;

/// <summary>What the app needs from a music service: today's list, its history, and writing the user's playlists.</summary>
public interface IMusicClient
{
    /// <summary>When the login key was issued; DateTime.MaxValue when it never needs renewing.</summary>
    DateTime KeyCreatedAt { get; }

    /// <summary>Renews the login and returns the updated cookies.</summary>
    Task<Dictionary<string, string>> RefreshLogin();

    /// <summary>Today's recommended list, dated by the day it was generated for.</summary>
    Task<DayList> GetToday();

    /// <summary>Past days the server still keeps (newest first).</summary>
    Task<List<DateTime>> GetHistoryDates();

    Task<List<Song>> GetHistoryDay(DateTime day);

    /// <summary>Finds one of the user's own playlists by name, creating it when missing.</summary>
    Task<UserPlaylist> EnsurePlaylist(string name);

    /// <summary>Clears the playlist and fills it with songs, keeping their order.</summary>
    Task ReplaceSongs(UserPlaylist pl, IReadOnlyList<Song> songs);
}
