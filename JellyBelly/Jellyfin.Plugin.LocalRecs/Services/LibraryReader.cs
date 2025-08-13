using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Vectorization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Querying;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.LocalRecs.Services;

/// <summary>
/// Reads library items and produces token streams per item.
/// </summary>
public sealed class LibraryReader
{
    private readonly ILibraryManager _library;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryReader"/> class.
    /// </summary>
    /// <param name="library">The library manager used to query items.</param>
    public LibraryReader(ILibraryManager library)
    {
        _library = library;
    }

    /// <summary>
    /// Queries all movie and series items in the library recursively.
    /// </summary>
    /// <returns>An enumerable of items.</returns>
    public IEnumerable<BaseItem> QueryMoviesAndSeries()
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = true
        };
        return _library.GetItemList(query);
    }

    /// <summary>
    /// Reads token streams for the supplied items to support vectorization.
    /// </summary>
    /// <param name="items">The items to tokenize.</param>
    /// <returns>A sequence of item identifiers with associated token streams.</returns>
    public IEnumerable<(Guid itemId, IEnumerable<string> tokens)> ReadTokens(IEnumerable<BaseItem> items)
    {
        foreach (var item in items)
        {
            var genres = item.Genres ?? Array.Empty<string>();
            var tags = item.Tags ?? Array.Empty<string>();
            var people = Array.Empty<string>();
            var studios = Array.Empty<string>();
            var title = item.Name;
            var overview = item.Overview;
            var toks = Tokenizer.Tokenize(genres, tags, people, studios, title, overview);
            yield return (item.Id, toks);
        }
    }
}


