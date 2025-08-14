using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyBelly.Abstractions;
using Jellyfin.Plugin.JellyBelly.Vectorization;

namespace Jellyfin.Plugin.JellyBelly.Recs;

/// <summary>
/// Scores candidate items for a user.
/// </summary>
public sealed class UserRecommender
{
    /// <summary>
    /// Ranks candidate items for a user profile using cosine similarity.
    /// </summary>
    /// <param name="userProfile">The user profile sparse vector.</param>
    /// <param name="candidates">Candidate item vectors.</param>
    /// <param name="exclude">Set of item ids to exclude.</param>
    /// <param name="minScore">Minimum similarity threshold.</param>
    /// <param name="maxItems">Maximum number of results.</param>
    /// <returns>Top scored items ordered by score descending.</returns>
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


