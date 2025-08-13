using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Abstractions;
using Jellyfin.Plugin.LocalRecs.Vectorization;

namespace Jellyfin.Plugin.LocalRecs.Recs;

/// <summary>
/// Builds per-user profile vector from interactions.
/// </summary>
public sealed class UserProfileBuilder
{
    public static SparseVector BuildProfile(
        IReadOnlyList<Interaction> interactions,
        IReadOnlyDictionary<Guid, ItemVector> itemVectors,
        double halfLifeDays,
        double wFinished,
        double wPartialOver40,
        double wFav,
        double wRating)
    {
        var profile = new Dictionary<int, double>();
        var now = DateTimeOffset.UtcNow;
        foreach (var inter in interactions)
        {
            if (!itemVectors.TryGetValue(inter.ItemId, out var iv)) continue;
            var weight = (inter.Finished ? wFinished : 0.0)
                        + ((inter.PlayedPercentage >= 0.4) ? wPartialOver40 : 0.0)
                        + (inter.FavoriteOrLike ? wFav : 0.0)
                        + (Math.Clamp(inter.UserRating01 ?? 0.0, 0.0, 1.0) * wRating);
            if (weight <= 0) continue;
            var days = (now - inter.When).TotalDays;
            var decay = Math.Exp(-days / Math.Max(1.0, halfLifeDays));
            var w = weight * decay;
            foreach (var (tid, val) in iv.Vector.Weights)
            {
                profile[tid] = profile.TryGetValue(tid, out var cur) ? cur + (val * w) : (val * w);
            }
        }
        // Normalize
        double norm = 0.0;
        foreach (var v in profile.Values) norm += v * v;
        norm = Math.Sqrt(norm);
        if (norm <= 0) norm = 1.0;
        var sparse = new SparseVector();
        foreach (var (k, v) in profile)
        {
            sparse.Weights[k] = (float)(v / norm);
        }
        return sparse;
    }
}


