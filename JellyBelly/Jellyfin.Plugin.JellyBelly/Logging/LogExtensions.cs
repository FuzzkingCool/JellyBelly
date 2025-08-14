using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyBelly.Abstractions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBelly.Logging;

/// <summary>
/// Logging helpers for structured task progress and top-N outputs.
/// </summary>
public static class LogExtensions
{
    /// <summary>
    /// Logs a standardized task start message.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="taskName">The task display name.</param>
    public static void InfoStart(this ILogger logger, string taskName) => logger.LogInformation("{Task} start at {Time}", taskName, DateTimeOffset.UtcNow);
    /// <summary>
    /// Logs a standardized task end message.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="taskName">The task display name.</param>
    public static void InfoEnd(this ILogger logger, string taskName) => logger.LogInformation("{Task} end at {Time}", taskName, DateTimeOffset.UtcNow);
    /// <summary>
    /// Logs the top recommendations for debugging.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="label">Context label.</param>
    /// <param name="items">Scored items to render.</param>
    public static void DebugTop(this ILogger logger, string label, IEnumerable<ScoredItem> items)
        => logger.LogDebug("{Label} top: {Items}", label, string.Join(", ", items.Take(10).Select(i => $"{i.ItemId}:{i.Score:F3}")));
}


