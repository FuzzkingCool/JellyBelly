using Jellyfin.Plugin.LocalRecs.Abstractions;

namespace Jellyfin.Plugin.LocalRecs.Output;

/// <summary>
/// Helpers for generating user-facing names for plugin-created rows.
/// </summary>
public static class Naming
{
    /// <summary>
    /// Returns a localized-style name for the "Top picks" row for the specified user.
    /// </summary>
    /// <param name="user">The user reference.</param>
    /// <returns>The display name for the row.</returns>
    public static string TopPicksFor(UserRef user) => $"Top picks for {user.Name}";
    /// <summary>
    /// Returns a name for a "Because you watched" row derived from a title.
    /// </summary>
    /// <param name="title">The title that inspired the recommendations.</param>
    /// <returns>The display name for the row.</returns>
    public static string BecauseYouWatched(string title) => $"Because you watched {title}";
}


