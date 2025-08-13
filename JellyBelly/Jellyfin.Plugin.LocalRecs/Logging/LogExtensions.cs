using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Abstractions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Logging;

public static class LogExtensions
{
    public static void InfoStart(this ILogger logger, string taskName) => logger.LogInformation("{Task} start at {Time}", taskName, DateTimeOffset.UtcNow);
    public static void InfoEnd(this ILogger logger, string taskName) => logger.LogInformation("{Task} end at {Time}", taskName, DateTimeOffset.UtcNow);
    public static void DebugTop(this ILogger logger, string label, IEnumerable<ScoredItem> items)
        => logger.LogDebug("{Label} top: {Items}", label, string.Join(", ", items.Take(10).Select(i => $"{i.ItemId}:{i.Score:F3}")));
}


