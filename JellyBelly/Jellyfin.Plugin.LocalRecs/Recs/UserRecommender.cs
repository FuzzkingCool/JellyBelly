using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Abstractions;
using Jellyfin.Plugin.LocalRecs.Vectorization;

namespace Jellyfin.Plugin.LocalRecs.Recs;

/// <summary>
/// Scores candidate items for a user.
/// </summary>
public sealed class UserRecommender
{
    public static List<ScoredItem> Rank(
        SparseVector userProfile,
        IEnumerable<ItemVector> candidates,
        HashSet<Guid> exclude,
        double minScore,
        int maxItems)
    {
        var list = new List<ScoredItem>();
        foreach (var c in candidates)
        {
            if (exclude.Contains(c.ItemId)) continue;
            var score = Cosine.Similarity(userProfile.Weights, c.Vector.Weights);
            if (score >= minScore)
            {
                list.Add(new ScoredItem { ItemId = c.ItemId, Score = score });
            }
        }
        return list
            .OrderByDescending(s => s.Score)
            .Take(Math.Max(1, maxItems))
            .ToList();
    }
}


