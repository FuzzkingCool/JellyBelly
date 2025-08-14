using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Jellyfin.Plugin.JellyBelly.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyBelly;

/// <summary>
/// JellyBelly: Local recommendations plugin for Jellyfin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Gets the singleton plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }
    private readonly Assembly _assembly = typeof(Plugin).Assembly;

    /// <inheritdoc />
    public override string Name => "JellyBelly";

    /// <inheritdoc />
    public override string Description => "Generates all-local Netflix-style recommendations per user using TF-IDF and optional ML.NET";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("f8fa0c88-7b8f-4c21-9da3-62d0fb1b9a54");

    /// <inheritdoc />
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "JellyBelly",
            EmbeddedResourcePath = _assembly.GetName().Name + ".Configuration.PluginConfigurationPage.html"
        };
        yield return new PluginPageInfo
        {
            // Serve overlay for consumers that want a URL instead of embedding file path
            Name = "jellybelly-overlay.png",
            EmbeddedResourcePath = _assembly.GetName().Name + ".wwwroot.jellybelly-overlay.png"
        };
        yield return new PluginPageInfo
        {
            // Serve thumbnail image for plugin display
            Name = "thumb.png",
            EmbeddedResourcePath = _assembly.GetName().Name + ".wwwroot.jellybelly-card.png"
        };
    }

    /// <summary>
    /// Gets the plugin's thumbnail image stream for display in the Jellyfin interface.
    /// </summary>
    /// <returns>A stream containing the plugin's thumbnail image.</returns>
    public Stream GetThumbImage()
    {
        // Prefer embedded resource; fallback to copied wwwroot file.
        var resName = _assembly.GetName().Name + ".wwwroot.jellybelly-card.png";
        var stream = _assembly.GetManifestResourceStream(resName);
        if (stream != null) return stream;

        var onDisk = Path.Combine(base.ApplicationPaths?.PluginsPath ?? AppContext.BaseDirectory ?? ".", "Jellyfin.Plugin.JellyBelly", "wwwroot", "jellybelly-card.png");
        if (File.Exists(onDisk)) return File.OpenRead(onDisk);
        // As a last resort, return an empty stream to avoid 404
        return new MemoryStream();
    }

    /// <summary>
    /// Gets the format of the thumbnail image.
    /// </summary>
    public string ThumbImageFormat => "png";
}


