using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyBelly.Abstractions;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Jellyfin.Data.Enums;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyBelly.Output;

/// <summary>
/// Creates or updates Collections and Playlists per user.
/// </summary>
public sealed class CollectionsWriter
{
    private readonly ICollectionManager _collections;
    private readonly ILibraryManager _library;

    /// <summary>
    /// Unwraps Task/ValueTask results returned by reflection into their underlying value or null.
    /// </summary>
    private static object? UnwrapTaskResult(object? result)
    {
        if (result == null) return null;
        var rt = result.GetType();
        if (typeof(Task).IsAssignableFrom(rt))
        {
            var isGeneric = rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>);
            if (isGeneric)
            {
                var prop = rt.GetProperty("Result");
                (result as Task)!.GetAwaiter().GetResult();
                return prop?.GetValue(result);
            }
            (result as Task)!.GetAwaiter().GetResult();
            return null;
        }
        if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var prop = rt.GetProperty("Result");
            return prop?.GetValue(result);
        }
        if (rt == typeof(ValueTask))
        {
            return null;
        }
        return result;
    }

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
        try
        {
            return CreateCollection(name, user.Id);
        }
        catch
        {
            // Fallback: create a user folder parent if required and retry without parent
            return CreateCollection(name, Guid.Empty);
        }
    }

    private BaseItem CreateCollection(string name, Guid parentId)
    {
        var type = _collections.GetType();

        // Try any CreateCollection overloads, preferring options-based
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => string.Equals(m.Name, "CreateCollection", StringComparison.Ordinal)
                     || string.Equals(m.Name, "CreateCollectionAsync", StringComparison.Ordinal))
            .ToList();

        // Resolve an options type by name to avoid assembly tie
        Type? optsType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => string.Equals(t.Name, "CollectionCreationOptions", StringComparison.Ordinal)
                               || string.Equals(t.Name, "CollectionCreationRequest", StringComparison.Ordinal));

        // 1) Options-based overloads
        if (optsType != null)
        {
            foreach (var m in methods.Where(m => m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == optsType))
            {
                var opts = Activator.CreateInstance(optsType);
                optsType.GetProperty("Name")?.SetValue(opts, name);
                var parentProp = optsType.GetProperty("ParentId") ?? optsType.GetProperty("ParentIdGuid") ?? optsType.GetProperty("ParentGuid");
                if (parentProp != null)
                {
                    if (parentProp.PropertyType == typeof(Guid)) parentProp.SetValue(opts, parentId);
                    else if (Nullable.GetUnderlyingType(parentProp.PropertyType) == typeof(Guid)) parentProp.SetValue(opts, parentId == Guid.Empty ? null : (Guid?)parentId);
                }

                var ps = m.GetParameters();
                var args = ps.Length == 2 && ps[1].ParameterType == typeof(CancellationToken)
                    ? new object[] { opts!, CancellationToken.None }
                    : new object[] { opts! };

                var result = m.Invoke(_collections, args);
                var unwrapped = UnwrapTaskResult(result);
                if (unwrapped is BaseItem bi) return bi;
                // Try to extract an Id/Item from complex results
                if (unwrapped != null)
                {
                    var utype = unwrapped.GetType();
                    var itemProp = utype.GetProperty("Item") ?? utype.GetProperty("Collection") ?? utype.GetProperty("BoxSet");
                    var idProp = utype.GetProperty("Id") ?? utype.GetProperty("ItemId") ?? utype.GetProperty("CollectionId");
                    if (itemProp != null)
                    {
                        var inner = itemProp.GetValue(unwrapped);
                        if (inner is BaseItem bi2) return bi2;
                        var innerId = inner?.GetType().GetProperty("Id")?.GetValue(inner);
                        if (innerId is Guid gid1) { var fromId = _library.GetItemById(gid1); if (fromId is BaseItem bi3) return bi3; }
                    }
                    if (idProp != null)
                    {
                        var idVal = idProp.GetValue(unwrapped);
                        if (idVal is Guid gid) { var fromId = _library.GetItemById(gid); if (fromId is BaseItem bi4) return bi4; }
                        if (idVal is string s && Guid.TryParse(s, out var g2)) { var fromId = _library.GetItemById(g2); if (fromId is BaseItem bi5) return bi5; }
                    }
                }
            }
        }

        // 2) String/Guid overloads (optionally with CancellationToken / Nullable<Guid>)
        foreach (var m in methods)
        {
            var ps = m.GetParameters();
            bool IsNullableGuid(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>) && t.GenericTypeArguments[0] == typeof(Guid);
            var hasStringGuid = ps.Any(p => p.ParameterType == typeof(string)) && ps.Any(p => p.ParameterType == typeof(Guid) || IsNullableGuid(p.ParameterType));
            if (!hasStringGuid) continue;
            var argsList = new List<object?>();
            foreach (var p in ps)
            {
                if (p.ParameterType == typeof(string)) argsList.Add(name);
                else if (p.ParameterType == typeof(Guid)) argsList.Add(parentId);
                else if (IsNullableGuid(p.ParameterType)) argsList.Add(parentId == Guid.Empty ? (Guid?)null : (Guid?)parentId);
                else if (p.ParameterType == typeof(CancellationToken)) argsList.Add(CancellationToken.None);
                else if (p.IsOptional) argsList.Add(Type.Missing);
                else goto NextMethod;
            }
            {
                var result = m.Invoke(_collections, argsList.ToArray()!);
                var unwrapped = UnwrapTaskResult(result);
                if (unwrapped is BaseItem bi) return bi;
                if (unwrapped is Guid gid) { var fromId = _library.GetItemById(gid); if (fromId is BaseItem bi6) return bi6; }
                if (unwrapped is string s && Guid.TryParse(s, out var g3)) { var fromId = _library.GetItemById(g3); if (fromId is BaseItem bi7) return bi7; }
            }
        NextMethod: ;
        }

        // Build diagnostic message including available methods
        var sigs = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name.StartsWith("CreateCollection", StringComparison.Ordinal))
            .Select(m => m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)) + ")")
            .ToArray();
        throw new MissingMethodException("ICollectionManager.CreateCollection method not found. Available overloads: " + string.Join("; ", sigs));
    }

    private void AddItemsToCollection(BaseItem collection, List<BaseItem> items)
    {
        var type = _collections.GetType();

        // Try AddOrRemoveFromCollection(collection, items, <enum>) without compile-time enum dependency
        var addOrRemove = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(mi => mi.Name == "AddOrRemoveFromCollection" && mi.GetParameters().Length == 3);
        if (addOrRemove != null)
        {
            var ps = addOrRemove.GetParameters();
            var enumType = ps[2].ParameterType;
            object mode = Enum.GetNames(enumType).Contains("Replace", StringComparer.Ordinal)
                ? Enum.Parse(enumType, "Replace")
                : (Enum.GetNames(enumType).Contains("Add", StringComparer.Ordinal) ? Enum.Parse(enumType, "Add") : Enum.ToObject(enumType, 0));
            var result = addOrRemove.Invoke(_collections, new object[] { collection, items, mode });
            var _ = UnwrapTaskResult(result);
            return;
        }

        // Prepare flexible argument shapes
        var collectionId = collection.Id;
        var itemIds = items.Select(i => i.Id).ToList();
        var itemIdStrings = itemIds.Select(g => g.ToString()).ToList();

        // Try any AddToCollection/AddToCollectionAsync overload with mappable parameters
        var candidates = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(mi => string.Equals(mi.Name, "AddToCollection", StringComparison.Ordinal)
                      || string.Equals(mi.Name, "AddToCollectionAsync", StringComparison.Ordinal))
            .ToList();

        bool TryInvokeBatch(MethodInfo mi)
        {
            var ps = mi.GetParameters();
            if (ps.Length < 2 || ps.Length > 3) return false;

            var args = new List<object?>();
            foreach (var p in ps)
            {
                if (p.ParameterType == typeof(BaseItem)) { args.Add(collection); continue; }
                if (p.ParameterType == typeof(Guid)) { args.Add(collectionId); continue; }
                if (p.ParameterType == typeof(string)) { args.Add(collectionId.ToString()); continue; }
                if (p.ParameterType == typeof(CancellationToken)) { args.Add(CancellationToken.None); continue; }
                if (p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    var elem = p.ParameterType.GenericTypeArguments[0];
                    if (elem == typeof(BaseItem)) { args.Add(items); continue; }
                    if (elem == typeof(Guid)) { args.Add(itemIds); continue; }
                    if (elem == typeof(string)) { args.Add(itemIdStrings); continue; }
                }
                if (p.IsOptional) { args.Add(Type.Missing); continue; }
                return false;
            }
            var result = mi.Invoke(_collections, args.ToArray());
            var _ = UnwrapTaskResult(result);
            return true;
        }

        foreach (var mi in candidates)
        {
            if (TryInvokeBatch(mi)) return;
        }

        // Fallback to adding one by one for any two-arg AddToCollection variant
        bool TryInvokeSingle(MethodInfo mi)
        {
            var ps = mi.GetParameters();
            if (ps.Length != 2) return false;
            foreach (var item in items)
            {
                var args = new object?[2];
                for (int idx = 0; idx < 2; idx++)
                {
                    var p = ps[idx];
                    if (p.ParameterType == typeof(BaseItem)) args[idx] = idx == 0 ? collection : item;
                    else if (p.ParameterType == typeof(Guid)) args[idx] = idx == 0 ? collectionId : item.Id;
                    else if (p.ParameterType == typeof(string)) args[idx] = idx == 0 ? collectionId.ToString() : item.Id.ToString();
                    else return false;
                }
                var result = mi.Invoke(_collections, args);
                var _ = UnwrapTaskResult(result);
            }
            return true;
        }

        var singleCandidates = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(mi => (mi.Name == "AddToCollection" || mi.Name == "AddToCollectionAsync") && mi.GetParameters().Length == 2)
            .ToList();
        foreach (var mi in singleCandidates)
        {
            if (TryInvokeSingle(mi)) return;
        }

        // Build diagnostics for available add methods
        var sigs = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(mi => mi.Name.StartsWith("Add", StringComparison.OrdinalIgnoreCase))
            .Select(mi => mi.Name + "(" + string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name)) + ")")
            .ToArray();
        throw new MissingMethodException("ICollectionManager AddToCollection method not found. Available overloads: " + string.Join("; ", sigs));
    }
}


