using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyBelly.Abstractions;

/// <summary>
/// Sparse vector stored as tokenId -> weight.
/// </summary>
public sealed class SparseVector
{
    /// <summary>
    /// Gets the sparse weights mapping token identifier to normalized weight.
    /// </summary>
    public Dictionary<int, float> Weights { get; } = new();
}

/// <summary>
/// Tokenized item and its vector representation.
/// </summary>
public sealed class ItemVector
{
    /// <summary>
    /// Gets the Jellyfin item identifier.
    /// </summary>
    public Guid ItemId { get; init; }
    /// <summary>
    /// Gets the TF-IDF sparse vector representation for the item.
    /// </summary>
    public SparseVector Vector { get; } = new();
}

/// <summary>
/// User interaction signal for an item.
/// </summary>
public sealed class Interaction
{
    /// <summary>
    /// Gets the Jellyfin item identifier the interaction refers to.
    /// </summary>
    public Guid ItemId { get; init; }
    /// <summary>
    /// Gets when the interaction occurred (UTC).
    /// </summary>
    public DateTimeOffset When { get; init; }
    /// <summary>
    /// Gets a value indicating whether the item was completed.
    /// </summary>
    public bool Finished { get; init; }
    /// <summary>
    /// Gets a value indicating whether the user favorited/liked the item.
    /// </summary>
    public bool FavoriteOrLike { get; init; }
    /// <summary>
    /// Gets the fraction of the runtime that was played in range [0,1].
    /// </summary>
    public double PlayedPercentage { get; init; }
    /// <summary>
    /// Gets the optional user rating mapped to range [0,1], or null if no rating.
    /// </summary>
    public double? UserRating01 { get; init; }
}

/// <summary>
/// Scored item recommendation.
/// </summary>
public sealed class ScoredItem
{
    /// <summary>
    /// Gets the recommended item identifier.
    /// </summary>
    public Guid ItemId { get; init; }
    /// <summary>
    /// Gets the recommendation score (higher is better).
    /// </summary>
    public double Score { get; init; }
}

/// <summary>
/// Lightweight user reference.
/// </summary>
public sealed class UserRef
{
    /// <summary>
    /// Gets the user identifier.
    /// </summary>
    public Guid Id { get; init; }
    /// <summary>
    /// Gets the user display name.
    /// </summary>
    public string Name { get; init; } = string.Empty;
}


