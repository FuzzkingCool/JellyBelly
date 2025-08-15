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
        
        _logger.LogInformation("RecsScheduledTask initialized with configuration: MaxItemsPerRow={MaxItemsPerRow}, RecentItemsToLearnFrom={RecentItemsToLearnFrom}, HalfLifeDays={HalfLifeDays}, DryRun={DryRun}, CreateTopPicksRow={CreateTopPicksRow}, CreateBecauseRows={CreateBecauseRows}, MinimumScoreThreshold={MinimumScoreThreshold}", 
            _config.MaxItemsPerRow, _config.RecentItemsToLearnFrom, _config.HalfLifeDays, _config.DryRun, _config.CreateTopPicksRow, _config.CreateBecauseRows, _config.MinimumScoreThreshold);
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
        // Conditionally run once at server startup (useful during development / debugging)
        if ((_config?.DebugRunAtStartup) == true)
        {
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerStartup
            };
        }
    }

    /// <summary>
    /// Executes the recommendation pipeline and writes results to collections/playlists.
    /// </summary>
    /// <param name="progress">Reports progress per user processed.</param>
    /// <param name="cancellationToken">Token to cancel execution.</param>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.InfoStart(Name);
        _logger.LogInformation("Starting Local Recommendations task execution");
        progress.Report(0);

        try
        {
            // Step 1: Load library items
            _logger.LogInformation("Step 1: Loading library items...");
            var libraryReader = new LibraryReader(_libraryManager);
            var items = libraryReader.QueryMoviesAndSeries().ToList();
            _logger.LogInformation("Found {ItemCount} movies and series in library", items.Count);
            
            if (items.Count == 0)
            {
                _logger.LogWarning("No movies or series found in library. Task will exit early.");
                _logger.InfoEnd(Name);
                return;
            }

            // Step 2: Read tokens and vectorize
            _logger.LogInformation("Step 2: Reading tokens and vectorizing items...");
            var tokens = libraryReader.ReadTokens(items).ToList();
            _logger.LogInformation("Generated {TokenCount} token streams", tokens.Count);
            
            if (tokens.Count == 0)
            {
                _logger.LogWarning("No token streams generated. Task will exit early.");
                _logger.InfoEnd(Name);
                return;
            }

            var vectorizer = new TfIdfVectorizer();
            var itemVectors = vectorizer.FitTransform(tokens);
            var itemVectorById = itemVectors.ToDictionary(v => v.ItemId);
            _logger.LogInformation("Created {VectorCount} item vectors", itemVectors.Count);

            // Step 3: Get users
            _logger.LogInformation("Step 3: Loading users...");
            var watchSignals = new WatchSignals(_userManager, _userDataManager, _logger);
            var users = watchSignals.GetUsers().ToList();
            _logger.LogInformation("Found {UserCount} users", users.Count);
            
            if (users.Count == 0)
            {
                _logger.LogWarning("No users found. Task will exit early.");
                _logger.InfoEnd(Name);
                return;
            }

            // Step 4: Process each user
            _logger.LogInformation("Step 4: Processing {UserCount} users...", users.Count);
            int userIndex = 0;
            int usersWithInteractions = 0;
            int totalCollectionsCreated = 0;
            int totalPlaylistsCreated = 0;

            foreach (var user in users)
            {
                cancellationToken.ThrowIfCancellationRequested();
                userIndex++;
                progress.Report((double)userIndex / Math.Max(1, users.Count));

                _logger.LogInformation("Processing user {UserIndex}/{UserCount}: {Username} (ID: {UserId})", 
                    userIndex, users.Count, user.Username, user.Id);

                // Get user interactions
                var interactions = watchSignals.GetInteractions(user, items)
                    .Take(Math.Max(1, _config.RecentItemsToLearnFrom))
                    .ToList();
                
                _logger.LogInformation("User {Username} has {InteractionCount} interactions (using {RecentCount} most recent)", 
                    user.Username, interactions.Count, Math.Min(interactions.Count, _config.RecentItemsToLearnFrom));

                if (interactions.Count == 0)
                {
                    _logger.LogInformation("User {Username} has no interactions, skipping", user.Username);
                    continue;
                }

                usersWithInteractions++;

                // Build user profile
                _logger.LogInformation("Building profile for user {Username}...", user.Username);
                var profile = UserProfileBuilder.BuildProfile(
                    interactions,
                    itemVectorById,
                    _config.HalfLifeDays,
                    _config.FinishedWeight,
                    _config.PartialOver40Weight,
                    _config.FavoriteOrLikeWeight,
                    _config.RatingWeight);

                var exclude = interactions.Select(i => i.ItemId).ToHashSet();
                _logger.LogInformation("User {Username} profile built, excluding {ExcludeCount} watched items", 
                    user.Username, exclude.Count);

                // Generate recommendations
                _logger.LogInformation("Generating recommendations for user {Username}...", user.Username);
                var ranked = UserRecommender.Rank(
                    profile,
                    itemVectors,
                    exclude,
                    _config.MinimumScoreThreshold,
                    _config.MaxItemsPerRow);

                _logger.LogInformation("Generated {RecommendationCount} recommendations for user {Username} (threshold: {Threshold})", 
                    ranked.Count, user.Username, _config.MinimumScoreThreshold);

                if (_config.DryRun)
                {
                    _logger.DebugTop("Top picks", ranked);
                }

                // Create top picks collection
                if (_config.CreateTopPicksRow && ranked.Count > 0)
                {
                    _logger.LogInformation("Creating top picks collection for user {Username}...", user.Username);
                    var writer = new CollectionsWriter(_collectionManager, _libraryManager, _logger);
                    var userRef = new Jellyfin.Plugin.JellyBelly.Abstractions.UserRef { Id = user.Id, Name = user.Username };
                    writer.UpsertTopPicksCollection(userRef, Naming.TopPicksFor(userRef), ranked.Select(r => r.ItemId), _config.DryRun);
                    totalCollectionsCreated++;
                    _logger.LogInformation("Top picks collection created for user {Username}", user.Username);
                }

                // Create "because you watched" playlists
                if (_config.CreateBecauseRows)
                {
                    var finishedInteractions = interactions.Where(i => i.Finished).Take(5).ToList();
                    _logger.LogInformation("Creating 'because you watched' playlists for {FinishedCount} finished items for user {Username}...", 
                        finishedInteractions.Count, user.Username);

                    foreach (var recent in finishedInteractions)
                    {
                        if (!itemVectorById.TryGetValue(recent.ItemId, out var iv))
                        {
                            _logger.LogWarning("Item vector not found for item {ItemId}, skipping", recent.ItemId);
                            continue;
                        }

                        var nns = ItemToItemRecommender.NearestNeighbors(iv, itemVectors, exclude, _config.MaxItemsPerRow, _config.MinimumScoreThreshold);
                        _logger.LogInformation("Found {NeighborCount} similar items for item {ItemId}", nns.Count, recent.ItemId);

                        if (_config.DryRun)
                        {
                            _logger.DebugTop($"Because {recent.ItemId}", nns);
                        }

                        if (nns.Count > 0)
                        {
                            var writer = new CollectionsWriter(_collectionManager, _libraryManager, _logger);
                            var userRef = new Jellyfin.Plugin.JellyBelly.Abstractions.UserRef { Id = user.Id, Name = user.Username };
                            var itemName = _libraryManager.GetItemById(recent.ItemId)?.Name ?? "Title";
                            writer.UpsertBecausePlaylist(userRef, Naming.BecauseYouWatched(itemName), nns.Select(n => n.ItemId), _config.DryRun);
                            totalPlaylistsCreated++;
                            _logger.LogInformation("Created 'because you watched {ItemName}' playlist for user {Username}", itemName, user.Username);
                        }
                    }
                }
            }

            _logger.LogInformation("Task completed successfully: {UserCount} users processed, {UsersWithInteractions} had interactions, {CollectionsCreated} collections created, {PlaylistsCreated} playlists created", 
                users.Count, usersWithInteractions, totalCollectionsCreated, totalPlaylistsCreated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Local Recommendations task execution");
            throw;
        }

        _logger.InfoEnd(Name);
        await Task.CompletedTask;
    }
}


