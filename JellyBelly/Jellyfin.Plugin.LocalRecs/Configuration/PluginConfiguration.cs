using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LocalRecs.Configuration;

/// <summary>
/// Configuration model for LocalRecs plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public int MaxItemsPerRow { get; set; } = 30;
    public int RecentItemsToLearnFrom { get; set; } = 50;
    public int HalfLifeDays { get; set; } = 30;

    public double FinishedWeight { get; set; } = 1.0;
    public double PartialOver40Weight { get; set; } = 0.5;
    public double FavoriteOrLikeWeight { get; set; } = 0.25;
    public double RatingWeight { get; set; } = 0.1;

    public bool CreateBecauseRows { get; set; } = true;
    public bool CreateTopPicksRow { get; set; } = true;
    public bool CreateTopicCollections { get; set; } = false;
    public bool UsePlaybackReporting { get; set; } = true;

    public bool EnableCollaborativeFiltering { get; set; } = false;
    public double CfBlendWeight { get; set; } = 0.5;

    public bool EnableTmdbEnrichment { get; set; } = false;
    public bool EnableWikidataEnrichment { get; set; } = false;

    public double MinimumScoreThreshold { get; set; } = 0.05;
    public bool DryRun { get; set; } = false;
}


