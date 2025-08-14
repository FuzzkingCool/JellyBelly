using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Drawing.Processing;

namespace Jellyfin.Plugin.JellyBelly.Output;

/// <summary>
/// Builds simple image mosaics and applies an overlay.
/// </summary>
internal static class ImageMosaic
{
	/// <summary>
	/// Creates a 2x3 poster-style mosaic image from up to six source images and applies an overlay image.
	/// </summary>
	/// <param name="sourceImagePaths">Image file paths in display order.</param>
	/// <param name="overlayPath">Optional PNG overlay path applied on top (scaled to canvas).</param>
	/// <param name="outputPath">Destination image path (PNG).</param>
	/// <param name="canvasWidth">Output width in pixels.</param>
	/// <param name="canvasHeight">Output height in pixels.</param>
    public static void Build2x3PosterWithOverlay(IReadOnlyList<string> sourceImagePaths, string? overlayPath, string outputPath, int canvasWidth = 720, int canvasHeight = 1080)
	{
		try
		{
			Build2x3PosterWithOverlayInternal(sourceImagePaths, overlayPath, outputPath, canvasWidth, canvasHeight);
		}
		catch (Exception ex)
		{
			// Log the error but don't throw - we don't want to break collection creation
			// The error will be logged by the calling code
			throw new InvalidOperationException($"Failed to create mosaic image: {ex.Message}", ex);
		}
	}

	private static void Build2x3PosterWithOverlayInternal(IReadOnlyList<string> sourceImagePaths, string? overlayPath, string outputPath, int canvasWidth, int canvasHeight)
	{
		if (sourceImagePaths == null) throw new ArgumentNullException(nameof(sourceImagePaths));
		Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var canvas = new Image<Rgba32>(canvasWidth, canvasHeight);
        canvas.Mutate(ctx => ctx.BackgroundColor(Color.Black));

        int cols = 2;
        int rows = 3;
        int cellW = canvasWidth / cols;
        int cellH = canvasHeight / rows;

        for (int idx = 0; idx < Math.Min(6, sourceImagePaths.Count); idx++)
        {
            string path = sourceImagePaths[idx];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            using var src = Image.Load<Rgba32>(path);
            var options = new ResizeOptions
            {
                Size = new Size(cellW, cellH),
                Mode = ResizeMode.Crop,
                Sampler = KnownResamplers.Bicubic,
                Position = AnchorPositionMode.Center
            };
            using var resized = src.Clone(op => op.Resize(options));
            int r = idx / cols;
            int c = idx % cols;
            int x = c * cellW;
            int y = r * cellH;
            canvas.Mutate(op => op.DrawImage(resized, new Point(x, y), 1f));
        }

        if (!string.IsNullOrEmpty(overlayPath) && File.Exists(overlayPath))
        {
            using var overlay = Image.Load<Rgba32>(overlayPath);
            using var overlayScaled = overlay.Clone(op => op.Resize(new ResizeOptions
            {
                Size = new Size(canvasWidth, canvasHeight),
                Mode = ResizeMode.Stretch
            }));
            canvas.Mutate(op => op.DrawImage(overlayScaled, new Point(0, 0), 1f));
        }

        var encoder = new PngEncoder { ColorType = PngColorType.RgbWithAlpha, CompressionLevel = PngCompressionLevel.Level6 };
        canvas.Save(outputPath, encoder);
	}
}


