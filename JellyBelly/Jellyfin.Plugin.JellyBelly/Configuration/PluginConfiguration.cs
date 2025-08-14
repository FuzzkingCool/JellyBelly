using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyBelly.Configuration;

/// <summary>
/// Configuration model for JellyBelly plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the maximum number of items per generated row.
    /// </summary>
    public int MaxItemsPerRow { get; set; } = 30;
    /// <summary>
    /// Gets or sets the number of most-recent items to learn from per user.
    /// </summary>
    public int RecentItemsToLearnFrom { get; set; } = 50;
    /// <summary>
    /// Gets or sets the half-life, in days, for time decay of interactions.
    /// </summary>
    public int HalfLifeDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets the weight contribution when an item was finished.
    /// </summary>
    public double FinishedWeight { get; set; } = 1.0;
    /// <summary>
    /// Gets or sets the weight contribution when playback exceeded 40%.
    /// </summary>
    public double PartialOver40Weight { get; set; } = 0.5;
    /// <summary>
    /// Gets or sets the weight contribution for favorites/likes.
    /// </summary>
    public double FavoriteOrLikeWeight { get; set; } = 0.25;
    /// <summary>
    /// Gets or sets the weight multiplier applied to normalized user rating.
    /// </summary>
    public double RatingWeight { get; set; } = 0.1;

    /// <summary>
    /// Gets or sets a value indicating whether to create "Because you watched" rows.
    /// </summary>
    public bool CreateBecauseRows { get; set; } = true;
    /// <summary>
    /// Gets or sets a value indicating whether to create a "Top picks" row.
    /// </summary>
    public bool CreateTopPicksRow { get; set; } = true;
    /// <summary>
    /// Gets or sets a value indicating whether to create topic-based collections.
    /// </summary>
    public bool CreateTopicCollections { get; set; } = false;
    /// <summary>
    /// Gets or sets a value indicating whether to use playback reporting (if available).
    /// </summary>
    public bool UsePlaybackReporting { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether collaborative filtering is enabled.
    /// </summary>
    public bool EnableCollaborativeFiltering { get; set; } = false;
    /// <summary>
    /// Gets or sets the blend weight between CF and content-based scores.
    /// </summary>
    public double CfBlendWeight { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets a value indicating whether to enrich metadata from TMDB.
    /// </summary>
    public bool EnableTmdbEnrichment { get; set; } = false;
    /// <summary>
    /// Gets or sets a value indicating whether to enrich metadata from Wikidata.
    /// </summary>
    public bool EnableWikidataEnrichment { get; set; } = false;

    /// <summary>
    /// Gets or sets the minimum score threshold items must meet to be included.
    /// </summary>
    public double MinimumScoreThreshold { get; set; } = 0.05;
    /// <summary>
    /// Gets or sets a value indicating whether to run in dry-run mode (no writes).
    /// </summary>
    public bool DryRun { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether to run the recommendations task at server startup (for debugging).
        /// </summary>
        public bool DebugRunAtStartup { get; set; } = true;
}


