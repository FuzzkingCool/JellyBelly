using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Abstractions;

namespace Jellyfin.Plugin.LocalRecs.Vectorization;

/// <summary>
/// Builds a vocabulary and normalized TF-IDF vectors for items.
/// </summary>
public sealed class TfIdfVectorizer
{
    private readonly Dictionary<string, int> _tokenToId = new(StringComparer.Ordinal);
    private readonly List<string> _idToToken = new();
    private readonly Dictionary<int, double> _idf = new();

    public IReadOnlyDictionary<string, int> Vocabulary => _tokenToId;
    public IReadOnlyDictionary<int, double> Idf => _idf;

    public List<ItemVector> FitTransform(IEnumerable<(Guid itemId, IEnumerable<string> tokens)> items)
    {
        var itemList = items.ToList();
        // Build DF counts
        var df = new Dictionary<int, int>();
        var tokenized = new List<(Guid id, List<int> tokenIds)>();
        foreach (var (itemId, tokens) in itemList)
        {
            var ids = new HashSet<int>();
            foreach (var t in tokens)
            {
                var id = GetOrAddTokenId(t);
                ids.Add(id);
            }
            foreach (var tid in ids)
                df[tid] = df.TryGetValue(tid, out var c) ? c + 1 : 1;
            tokenized.Add((itemId, ids.ToList()));
        }

        var n = Math.Max(1, itemList.Count);
        _idf.Clear();
        foreach (var (tid, dfi) in df)
        {
            _idf[tid] = Math.Log((double)n / (1 + dfi));
        }

        var results = new List<ItemVector>(tokenized.Count);
        // Compute TF-IDF normalized
        foreach (var (id, tokenIds) in tokenized)
        {
            var tf = new Dictionary<int, double>();
            foreach (var tid in tokenIds)
            {
                tf[tid] = tf.TryGetValue(tid, out var c) ? c + 1.0 : 1.0;
            }
            double norm = 0.0;
            var vec = new Dictionary<int, float>();
            foreach (var (tid, tfi) in tf)
            {
                var w = tfi * _idf.GetValueOrDefault(tid, 0.0);
                norm += w * w;
            }
            norm = Math.Sqrt(norm);
            if (norm <= 0) norm = 1.0;
            foreach (var (tid, tfi) in tf)
            {
                var w = (float)((tfi * _idf.GetValueOrDefault(tid, 0.0)) / norm);
                if (w != 0) vec[tid] = w;
            }
            results.Add(new ItemVector { ItemId = id, Vector = { } });
            // Copy weights into the ItemVector's sparse vector
            foreach (var (k, v) in vec)
            {
                results[^1].Vector.Weights[k] = v;
            }
        }
        return results;
    }

    private int GetOrAddTokenId(string token)
    {
        if (_tokenToId.TryGetValue(token, out var id)) return id;
        id = _idToToken.Count;
        _tokenToId[token] = id;
        _idToToken.Add(token);
        return id;
    }
}


