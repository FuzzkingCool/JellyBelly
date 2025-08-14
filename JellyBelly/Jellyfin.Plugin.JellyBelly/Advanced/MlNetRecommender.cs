using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyBelly.Abstractions;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace Jellyfin.Plugin.JellyBelly.Advanced;

/// <summary>
/// Optional ML.NET CF recommender. If anything fails, caller must fall back to TF-IDF only.
/// </summary>
public sealed class MlNetRecommender
{
    private readonly MLContext _ml = new(seed: 1);

    public Dictionary<(Guid userId, Guid itemId), float> TrainAndScore(
        IEnumerable<(Guid userId, Guid itemId, float strength)> interactions,
        IEnumerable<Guid> allItems,
        IEnumerable<Guid> userIds)
    {
        var userIndex = userIds.Distinct().Select((u, i) => (u, i)).ToDictionary(x => x.u, x => x.i);
        var itemIndex = allItems.Distinct().Select((m, i) => (m, i)).ToDictionary(x => x.m, x => x.i);

        var rows = interactions
            .Where(t => userIndex.ContainsKey(t.userId) && itemIndex.ContainsKey(t.itemId))
            .Select(t => new CfRow
            {
                UserIdx = userIndex[t.userId],
                ItemIdx = itemIndex[t.itemId],
                Label = t.strength
            });
        var data = _ml.Data.LoadFromEnumerable(rows);
        var options = new Microsoft.ML.Trainers.MatrixFactorizationTrainer.Options
        {
            MatrixColumnIndexColumnName = nameof(CfRow.UserIdx),
            MatrixRowIndexColumnName = nameof(CfRow.ItemIdx),
            LabelColumnName = nameof(CfRow.Label),
            NumberOfIterations = 25,
            ApproximationRank = 64,
            Quiet = true
        };
        var model = _ml.Recommendation().Trainers.MatrixFactorization(options).Fit(data);
        var engine = _ml.Model.CreatePredictionEngine<CfRow, CfScore>(_ml, model);
        var result = new Dictionary<(Guid, Guid), float>();
        foreach (var u in userIndex)
        {
            foreach (var it in itemIndex)
            {
                var score = engine.Predict(new CfRow { UserIdx = u.Value, ItemIdx = it.Value, Label = 0 }).Score;
                result[(u.Key, it.Key)] = score;
            }
        }
        return result;
    }

    private sealed class CfRow
    {
        public float UserIdx { get; set; }
        public float ItemIdx { get; set; }
        public float Label { get; set; }
    }
    private sealed class CfScore
    {
        public float Score { get; set; }
    }
}


