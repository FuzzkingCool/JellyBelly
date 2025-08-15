using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

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

		using var surface = SKSurface.Create(new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
		var canvas = surface.Canvas;
		canvas.Clear(SKColors.Black);

		int cols = 2;
		int rows = 3;
		int cellW = canvasWidth / cols;
		int cellH = canvasHeight / rows;

		for (int idx = 0; idx < Math.Min(6, sourceImagePaths.Count); idx++)
		{
			string path = sourceImagePaths[idx];
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
			using var bitmap = SKBitmap.Decode(path);
			if (bitmap == null) continue;

			float srcAspect = (float)bitmap.Width / bitmap.Height;
			float dstAspect = (float)cellW / cellH;
			SKRect srcRect;
			if (srcAspect > dstAspect)
			{
				int newWidth = (int)(bitmap.Height * dstAspect);
				int xOff = (bitmap.Width - newWidth) / 2;
				srcRect = new SKRect(xOff, 0, xOff + newWidth, bitmap.Height);
			}
			else
			{
				int newHeight = (int)(bitmap.Width / dstAspect);
				int yOff = (bitmap.Height - newHeight) / 2;
				srcRect = new SKRect(0, yOff, bitmap.Width, yOff + newHeight);
			}

			int r = idx / cols;
			int c = idx % cols;
			int x = c * cellW;
			int y = r * cellH;
			var dstRect = new SKRect(x, y, x + cellW, y + cellH);
			canvas.DrawBitmap(bitmap, srcRect, dstRect, new SKPaint { FilterQuality = SKFilterQuality.High });
		}

		if (!string.IsNullOrEmpty(overlayPath) && File.Exists(overlayPath))
		{
			using var overlayBmp = SKBitmap.Decode(overlayPath);
			if (overlayBmp != null)
			{
				var dst = new SKRect(0, 0, canvasWidth, canvasHeight);
				canvas.DrawBitmap(overlayBmp, dst, new SKPaint { FilterQuality = SKFilterQuality.High });
			}
		}

		canvas.Flush();
		using var image = surface.Snapshot();
		using var png = image.Encode(SKEncodedImageFormat.Png, 90);
		using var fs = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
		png.SaveTo(fs);
	}
}


