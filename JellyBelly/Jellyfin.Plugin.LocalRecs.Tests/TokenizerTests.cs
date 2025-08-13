using System.Linq;
using Jellyfin.Plugin.LocalRecs.Vectorization;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests;

public class TokenizerTests
{
    [Fact]
    public void Tokenize_Includes_All_Types()
    {
        var toks = Tokenizer.Tokenize(
            new[] { "Drama" },
            new[] { "Thriller" },
            new[] { "Jane Doe" },
            new[] { "Studio A" },
            "The Great Adventure",
            "A story of adventure and courage.").ToList();
        Assert.Contains("genre:drama", toks);
        Assert.Contains("tag:thriller", toks);
        Assert.Contains("person:jane doe", toks);
        Assert.Contains("studio:studio a", toks);
        Assert.Contains(toks, t => t.StartsWith("title:"));
        Assert.Contains(toks, t => t.StartsWith("overview:"));
    }
}


