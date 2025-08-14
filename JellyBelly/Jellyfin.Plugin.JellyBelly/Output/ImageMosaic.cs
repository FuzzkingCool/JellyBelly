using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

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
		if (sourceImagePaths == null) throw new ArgumentNullException(nameof(sourceImagePaths));
		Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

		using var bmp = new Bitmap(canvasWidth, canvasHeight, PixelFormat.Format32bppPArgb);
		using (var g = Graphics.FromImage(bmp))
		{
			g.SmoothingMode = SmoothingMode.HighQuality;
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			g.PixelOffsetMode = PixelOffsetMode.HighQuality;
			g.Clear(Color.Black);

			int cols = 2;
			int rows = 3;
			int cellW = canvasWidth / cols;
			int cellH = canvasHeight / rows;

			for (int idx = 0; idx < Math.Min(6, sourceImagePaths.Count); idx++)
			{
				string path = sourceImagePaths[idx];
				if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
				using var src = Image.FromFile(path);
				int r = idx / cols;
				int c = idx % cols;
				var dest = new Rectangle(c * cellW, r * cellH, cellW, cellH);
				// Cover fit
				double scale = Math.Max((double)dest.Width / src.Width, (double)dest.Height / src.Height);
				int w = (int)Math.Round(src.Width * scale);
				int h = (int)Math.Round(src.Height * scale);
				int x = dest.X + (dest.Width - w) / 2;
				int y = dest.Y + (dest.Height - h) / 2;
				g.DrawImage(src, new Rectangle(x, y, w, h));
			}

			if (!string.IsNullOrEmpty(overlayPath) && File.Exists(overlayPath))
			{
				using var overlay = Image.FromFile(overlayPath);
				g.DrawImage(overlay, new Rectangle(0, 0, canvasWidth, canvasHeight));
			}
		}

		bmp.Save(outputPath, ImageFormat.Png);
	}
}


