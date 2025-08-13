using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Vectorization;

/// <summary>
/// Efficient cosine similarity for sparse vectors.
/// </summary>
public static class Cosine
{
    public static double Similarity(Dictionary<int, float> a, Dictionary<int, float> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0.0;
        double dot = 0.0;
        double aNorm = 0.0;
        double bNorm = 0.0;
        var smaller = a.Count <= b.Count ? a : b;
        var larger = a.Count <= b.Count ? b : a;
        foreach (var kv in smaller)
        {
            if (larger.TryGetValue(kv.Key, out var bv))
            {
                dot += kv.Value * bv;
            }
        }
        foreach (var v in a.Values) aNorm += v * v;
        foreach (var v in b.Values) bNorm += v * v;
        if (aNorm == 0 || bNorm == 0) return 0.0;
        return dot / (System.Math.Sqrt(aNorm) * System.Math.Sqrt(bNorm));
    }
}


