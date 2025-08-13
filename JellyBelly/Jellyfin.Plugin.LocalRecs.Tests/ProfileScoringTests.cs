using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Abstractions;
using Jellyfin.Plugin.LocalRecs.Recs;
using Jellyfin.Plugin.LocalRecs.Vectorization;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests;

public class ProfileScoringTests
{
    [Fact]
    public void Profile_Prefers_Shared_Tokens()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var idC = Guid.NewGuid();
        var vec = new TfIdfVectorizer();
        var items = new[]
        {
            (idA, new []{ "genre:drama", "tag:space" }.AsEnumerable()),
            (idB, new []{ "genre:drama", "tag:space" }.AsEnumerable()),
            (idC, new []{ "genre:comedy", "tag:romance" }.AsEnumerable()),
        };
        var ivs = vec.FitTransform(items);
        var byId = ivs.ToDictionary(v => v.ItemId);
        var interactions = new List<Interaction>{ new Interaction{ ItemId = idA, When = DateTimeOffset.UtcNow, Finished = true } };
        var profile = UserProfileBuilder.BuildProfile(interactions, byId, 30, 1.0, 0.5, 0.25, 0.1);
        var ranked = UserRecommender.Rank(profile, ivs, new HashSet<Guid>{ idA }, 0.0, 10);
        Assert.Equal(idB, ranked.First().ItemId);
    }
}


