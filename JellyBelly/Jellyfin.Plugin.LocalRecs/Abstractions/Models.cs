using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Abstractions;

/// <summary>
/// Sparse vector stored as tokenId -> weight.
/// </summary>
public sealed class SparseVector
{
    public Dictionary<int, float> Weights { get; } = new();
}

/// <summary>
/// Tokenized item and its vector representation.
/// </summary>
public sealed class ItemVector
{
    public Guid ItemId { get; init; }
    public SparseVector Vector { get; } = new();
}

/// <summary>
/// User interaction signal for an item.
/// </summary>
public sealed class Interaction
{
    public Guid ItemId { get; init; }
    public DateTimeOffset When { get; init; }
    public bool Finished { get; init; }
    public bool FavoriteOrLike { get; init; }
    public double PlayedPercentage { get; init; }
    public double? UserRating01 { get; init; }
}

/// <summary>
/// Scored item recommendation.
/// </summary>
public sealed class ScoredItem
{
    public Guid ItemId { get; init; }
    public double Score { get; init; }
}

/// <summary>
/// Lightweight user reference.
/// </summary>
public sealed class UserRef
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}


