using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyBelly.Abstractions;
using Jellyfin.Plugin.JellyBelly.Vectorization;

namespace Jellyfin.Plugin.JellyBelly.Recs;

/// <summary>
/// Item-to-item nearest neighbors by cosine.
/// </summary>
public static class ItemToItemRecommender
{
    /// <summary>
    /// Finds nearest neighbors to an anchor item using cosine similarity.
    /// </summary>
    /// <param name="anchor">The anchor item vector.</param>
    /// <param name="all">All candidate item vectors.</param>
    /// <param name="exclude">Set of item ids to exclude.</param>
    /// <param name="maxItems">Maximum items to return.</param>
    /// <param name="minScore">Minimum similarity threshold.</param>
    /// <returns>Top scored neighbors ordered descending by score.</returns>
    public static List<ScoredItem> NearestNeighbors(
        ItemVector anchor,
        IEnumerable<ItemVector> all,
        HashSet<Guid> exclude,
        int maxItems,
        double minScore)
    {
        var results = new List<ScoredItem>();
        foreach (var other in all)
        {
            if (other.ItemId == anchor.ItemId) continue;
            if (exclude.Contains(other.ItemId)) continue;
            var s = Cosine.Similarity(anchor.Vector.Weights, other.Vector.Weights);
            if (s >= minScore) results.Add(new ScoredItem { ItemId = other.ItemId, Score = s });
        }
        return results.OrderByDescending(x => x.Score).Take(Math.Max(1, maxItems)).ToList();
    }
}


