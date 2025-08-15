using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyBelly.Abstractions;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBelly.Services;

/// <summary>
/// Gathers watch history and per-item signals per user.
/// </summary>
public sealed class WatchSignals
{
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchSignals"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="userDataManager">The user data manager for retrieving play state.</param>
    /// <param name="logger">The logger for diagnostic information.</param>
    public WatchSignals(IUserManager userManager, IUserDataManager userDataManager, ILogger logger)
    {
        _userManager = userManager;
        _userDataManager = userDataManager;
        _logger = logger;
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
        _logger.LogInformation("Getting interactions for user {Username} across {ItemCount} items", user.Username, items.Count());
        
        var list = new List<Interaction>();
        int itemsWithData = 0;
        int finishedItems = 0;
        int favoritedItems = 0;
        
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
            
            if (data != null && (data.Played || data.PlaybackPositionTicks > 0 || data.IsFavorite || data.Rating.HasValue))
            {
                itemsWithData++;
                if (finished) finishedItems++;
                if (fav) favoritedItems++;
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
        
        var result = list
            .OrderByDescending(i => i.When)
            .ToList();
            
        _logger.LogInformation("User {Username} interactions: {TotalItems} total, {ItemsWithData} with user data, {FinishedItems} finished, {FavoritedItems} favorited", 
            user.Username, items.Count(), itemsWithData, finishedItems, favoritedItems);
            
        return result;
    }
}


