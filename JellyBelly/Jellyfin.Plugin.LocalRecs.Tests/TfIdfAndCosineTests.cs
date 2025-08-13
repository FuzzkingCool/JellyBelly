using System;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Vectorization;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests;

public class TfIdfAndCosineTests
{
    [Fact]
    public void TfIdf_Produces_Normalized_Vectors()
    {
        var vec = new TfIdfVectorizer();
        var items = new[]
        {
            (Guid.NewGuid(), new []{ "genre:drama", "tag:thriller" }.AsEnumerable()),
            (Guid.NewGuid(), new []{ "genre:drama", "tag:comedy" }.AsEnumerable())
        };
        var ivs = vec.FitTransform(items);
        Assert.Equal(2, ivs.Count);
        foreach (var v in ivs)
        {
            var norm = System.Math.Sqrt(v.Vector.Weights.Values.Select(x => x * x).Sum());
            Assert.InRange(norm, 0.99, 1.01);
        }
    }

    [Fact]
    public void Cosine_Symmetry_And_Range()
    {
        var a = new System.Collections.Generic.Dictionary<int, float> { [0] = 1, [1] = 0.5f };
        var b = new System.Collections.Generic.Dictionary<int, float> { [0] = 1, [1] = 0.25f };
        var ab = Cosine.Similarity(a, b);
        var ba = Cosine.Similarity(b, a);
        Assert.InRange(ab, 0, 1);
        Assert.InRange(ba, 0, 1);
        Assert.True(System.Math.Abs(ab - ba) < 1e-6);
    }
}


