using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Abstractions;
using Jellyfin.Plugin.LocalRecs.Vectorization;

namespace Jellyfin.Plugin.LocalRecs.Recs;

/// <summary>
/// Item-to-item nearest neighbors by cosine.
/// </summary>
public static class ItemToItemRecommender
{
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


