using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LocalRecs.Abstractions;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Jellyfin.Data.Enums;
using System.Reflection;

namespace Jellyfin.Plugin.LocalRecs.Output;

/// <summary>
/// Creates or updates Collections and Playlists per user.
/// </summary>
public sealed class CollectionsWriter
{
    private readonly ICollectionManager _collections;
    private readonly ILibraryManager _library;

        /// <summary>
        /// Initializes a new instance of the <see cref="CollectionsWriter"/> class.
        /// </summary>
        /// <param name="collections">The collection manager used to create and modify collections.</param>
        /// <param name="library">The library manager used to resolve items by identifier.</param>
    public CollectionsWriter(ICollectionManager collections, ILibraryManager library)
    {
        _collections = collections;
        _library = library;
    }

        /// <summary>
        /// Creates or updates a "Top picks" collection for a user with the provided items.
        /// </summary>
        /// <param name="user">The user for whom the collection is created or updated.</param>
        /// <param name="name">The display name of the collection.</param>
        /// <param name="itemIds">The item identifiers to include, in ranked order.</param>
        /// <param name="dryRun">If true, no changes are written to the server.</param>
    public void UpsertTopPicksCollection(UserRef user, string name, IEnumerable<Guid> itemIds, bool dryRun)
    {
        if (dryRun) return;
        var collection = EnsureCollection(user, name);
        var items = itemIds.Select(id => _library.GetItemById(id)).Where(i => i != null).Cast<BaseItem>().ToList();
        AddItemsToCollection(collection, items);
    }

        /// <summary>
        /// Creates or updates a "Because you watched" playlist for a user with the provided items.
        /// </summary>
        /// <param name="user">The user for whom the playlist is created or updated.</param>
        /// <param name="name">The display name of the playlist.</param>
        /// <param name="itemIds">The item identifiers to include, in ranked order.</param>
        /// <param name="dryRun">If true, no changes are written to the server.</param>
    public void UpsertBecausePlaylist(UserRef user, string name, IEnumerable<Guid> itemIds, bool dryRun)
    {
        if (dryRun) return;
        var playlist = EnsureCollection(user, name);
        var items = itemIds.Select(id => _library.GetItemById(id)).Where(i => i != null).Cast<BaseItem>().ToList();
        AddItemsToCollection(playlist, items);
    }

    private BaseItem EnsureCollection(UserRef user, string name)
    {
        var existing = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
            Name = name,
            Limit = 1
        }).FirstOrDefault();
        if (existing != null) return existing;
        return CreateCollection(name, user.Id);
    }

    private BaseItem CreateCollection(string name, Guid parentId)
    {
        // Try common signatures via reflection for Jellyfin 10.10.x
        var type = _collections.GetType();
        // Try CreateCollection(string, Guid)
        var m = type.GetMethod("CreateCollection", new[] { typeof(string), typeof(Guid) });
        if (m != null)
        {
            var result = m.Invoke(_collections, new object[] { name, parentId });
            if (result is BaseItem bi) return bi;
        }
        // Try CreateCollection(CollectionCreationOptions, CancellationToken?)
        var optsType = typeof(CollectionCreationOptions);
        m = type.GetMethod("CreateCollection", new[] { optsType, typeof(System.Threading.CancellationToken) })
            ?? type.GetMethod("CreateCollection", new[] { optsType });
        if (m != null)
        {
            var opts = new CollectionCreationOptions { Name = name, ParentId = parentId };
            var parameters = m.GetParameters().Length == 2
                ? new object[] { opts, System.Threading.CancellationToken.None }
                : new object[] { opts };
            var result = m.Invoke(_collections, parameters);
            if (result is BaseItem bi) return bi;
        }
        throw new MissingMethodException("ICollectionManager.CreateCollection method not found");
    }

    private void AddItemsToCollection(BaseItem collection, List<BaseItem> items)
    {
        var type = _collections.GetType();
        // Try AddOrRemoveFromCollection(collection, items, <enum>) without compile-time enum dependency
        var addOrRemove = type.GetMethods().FirstOrDefault(mi => mi.Name == "AddOrRemoveFromCollection" && mi.GetParameters().Length == 3);
        if (addOrRemove != null)
        {
            var ps = addOrRemove.GetParameters();
            var enumType = ps[2].ParameterType;
            object mode = Enum.GetNames(enumType).Contains("Replace", StringComparer.Ordinal)
                ? Enum.Parse(enumType, "Replace")
                : (Enum.GetNames(enumType).Contains("Add", StringComparer.Ordinal) ? Enum.Parse(enumType, "Add") : Enum.ToObject(enumType, 0));
            addOrRemove.Invoke(_collections, new object[] { collection, items, mode });
            return;
        }
        // Try AddToCollection(collection, items, CancellationToken)
        var m = type.GetMethod("AddToCollection", new[] { typeof(BaseItem), typeof(IEnumerable<BaseItem>), typeof(System.Threading.CancellationToken) })
            ?? type.GetMethod("AddToCollection", new[] { typeof(BaseItem), typeof(IEnumerable<BaseItem>) });
        if (m != null)
        {
            var args = m.GetParameters().Length == 3
                ? new object[] { collection, items, System.Threading.CancellationToken.None }
                : new object[] { collection, items };
            m.Invoke(_collections, args);
            return;
        }
        // Fallback to adding one by one if method takes single item
        m = type.GetMethod("AddToCollection", new[] { typeof(BaseItem), typeof(BaseItem) });
        if (m != null)
        {
            foreach (var i in items) m.Invoke(_collections, new object[] { collection, i });
            return;
        }
        throw new MissingMethodException("ICollectionManager AddToCollection method not found");
    }
}


