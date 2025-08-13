using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Abstractions;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.LocalRecs.Services;

/// <summary>
/// Gathers watch history and per-item signals per user.
/// </summary>
public sealed class WatchSignals
{
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchSignals"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="userDataManager">The user data manager for retrieving play state.</param>
    public WatchSignals(IUserManager userManager, IUserDataManager userDataManager)
    {
        _userManager = userManager;
        _userDataManager = userDataManager;
    }

    /// <summary>
    /// Gets all users known to the server.
    /// </summary>
    /// <returns>The users.</returns>
    public IEnumerable<User> GetUsers() => _userManager.Users;

    /// <summary>
    /// Computes interaction signals for the specified user over the provided items.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="items">The items to compute interactions for.</param>
    /// <returns>An ordered list of interactions, newest first.</returns>
    public IReadOnlyList<Interaction> GetInteractions(User user, IEnumerable<BaseItem> items)
    {
        var list = new List<Interaction>();
        foreach (var item in items)
        {
            var data = _userDataManager.GetUserData(user, item);
            var dateCreated = item.DateCreated;
            var when = data?.LastPlayedDate ?? (dateCreated == default ? DateTime.UtcNow : dateCreated);
            var finished = data?.Played ?? false;
            var pct = data?.PlaybackPositionTicks > 0 && item.RunTimeTicks.HasValue && item.RunTimeTicks.Value > 0
                ? Math.Clamp((double)data.PlaybackPositionTicks / item.RunTimeTicks.Value, 0.0, 1.0)
                : 0.0;
            var fav = (data?.IsFavorite ?? false);
            double? rating01 = null;
            if (data?.Rating.HasValue == true)
            {
                rating01 = Math.Clamp(data.Rating.Value / 10.0, 0.0, 1.0);
            }
            list.Add(new Interaction
            {
                ItemId = item.Id,
                When = new DateTimeOffset(when.ToUniversalTime()),
                Finished = finished,
                FavoriteOrLike = fav,
                PlayedPercentage = pct,
                UserRating01 = rating01
            });
        }
        return list
            .OrderByDescending(i => i.When)
            .ToList();
    }
}


