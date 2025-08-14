using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyBelly.Abstractions;
using Jellyfin.Plugin.JellyBelly.Configuration;
using Jellyfin.Plugin.JellyBelly.Logging;
using Jellyfin.Plugin.JellyBelly.Output;
using Jellyfin.Plugin.JellyBelly.Recs;
using Jellyfin.Plugin.JellyBelly.Services;
using Jellyfin.Plugin.JellyBelly.Vectorization;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBelly.Tasks;

/// <summary>
/// Scheduled task to recompute recommendations.
/// </summary>
public sealed class RecsScheduledTask : IScheduledTask
{
    private readonly ILogger<RecsScheduledTask> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ICollectionManager _collectionManager;

    private readonly PluginConfiguration _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecsScheduledTask"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="collectionManager">The collection manager.</param>
    public RecsScheduledTask(
        ILogger<RecsScheduledTask> logger,
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ICollectionManager collectionManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _collectionManager = collectionManager;
        _config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    /// <summary>
    /// Gets the category under which the task is shown.
    /// </summary>
    public string Category => "Recommendations";
    /// <summary>
    /// Gets the unique key of the task.
    /// </summary>
    public string Key => "Jellyfin.Plugin.JellyBelly.RecsScheduledTask";
    /// <summary>
    /// Gets the display name of the task.
    /// </summary>
    public string Name => "Local Recommendations";
    /// <summary>
    /// Gets the description of the task.
    /// </summary>
    public string Description => "Generates all-local Netflix-style recommendations per user";

    /// <summary>
    /// Returns the default schedule on which the task runs.
    /// </summary>
    /// <returns>The default triggers.</returns>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Nightly at 04:00
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerDaily,
            TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
        };
    }

    /// <summary>
    /// Executes the recommendation pipeline and writes results to collections/playlists.
    /// </summary>
    /// <param name="progress">Reports progress per user processed.</param>
    /// <param name="cancellationToken">Token to cancel execution.</param>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.InfoStart(Name);
        progress.Report(0);
        var libraryReader = new LibraryReader(_libraryManager);
        var items = libraryReader.QueryMoviesAndSeries().ToList();
        var tokens = libraryReader.ReadTokens(items).ToList();
        var vectorizer = new TfIdfVectorizer();
        var itemVectors = vectorizer.FitTransform(tokens);
        var itemVectorById = itemVectors.ToDictionary(v => v.ItemId);

        var watchSignals = new WatchSignals(_userManager, _userDataManager);
        var users = watchSignals.GetUsers().ToList();
        int userIndex = 0;
        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            userIndex++;
            progress.Report((double)userIndex / Math.Max(1, users.Count));

            var interactions = watchSignals.GetInteractions(user, items)
                .Take(Math.Max(1, _config.RecentItemsToLearnFrom))
                .ToList();
            var profile = UserProfileBuilder.BuildProfile(
                interactions,
                itemVectorById,
                _config.HalfLifeDays,
                _config.FinishedWeight,
                _config.PartialOver40Weight,
                _config.FavoriteOrLikeWeight,
                _config.RatingWeight);

            var exclude = interactions.Select(i => i.ItemId).ToHashSet();
            var ranked = UserRecommender.Rank(
                profile,
                itemVectors,
                exclude,
                _config.MinimumScoreThreshold,
                _config.MaxItemsPerRow);

            if (_config.DryRun)
            {
                _logger.DebugTop("Top picks", ranked);
            }
            if (_config.CreateTopPicksRow)
            {
                var writer = new CollectionsWriter(_collectionManager, _libraryManager);
                var userRef = new Jellyfin.Plugin.JellyBelly.Abstractions.UserRef { Id = user.Id, Name = user.Username };
                writer.UpsertTopPicksCollection(userRef, Naming.TopPicksFor(userRef), ranked.Select(r => r.ItemId), _config.DryRun);
            }

            if (_config.CreateBecauseRows)
            {
                foreach (var recent in interactions.Where(i => i.Finished).Take(5))
                {
                    if (!itemVectorById.TryGetValue(recent.ItemId, out var iv)) continue;
                    var nns = ItemToItemRecommender.NearestNeighbors(iv, itemVectors, exclude, _config.MaxItemsPerRow, _config.MinimumScoreThreshold);
                    if (_config.DryRun)
                    {
                        _logger.DebugTop($"Because {recent.ItemId}", nns);
                    }
                    var writer = new CollectionsWriter(_collectionManager, _libraryManager);
                    var userRef = new Jellyfin.Plugin.JellyBelly.Abstractions.UserRef { Id = user.Id, Name = user.Username };
                    writer.UpsertBecausePlaylist(userRef, Naming.BecauseYouWatched(_libraryManager.GetItemById(recent.ItemId)?.Name ?? "Title"), nns.Select(n => n.ItemId), _config.DryRun);
                }
            }
        }

        _logger.InfoEnd(Name);
        await Task.CompletedTask;
    }
}


