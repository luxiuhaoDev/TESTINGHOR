using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace McPetOverlay;

internal static class BackgroundRemover
{
	internal static Bitmap LoadOrCreate(string sourcePath, string outputPath)
	{
		if (File.Exists(outputPath) && File.GetLastWriteTimeUtc(outputPath) >= File.GetLastWriteTimeUtc(sourcePath))
		{
			return new Bitmap(outputPath);
		}
		using Bitmap bitmap = new Bitmap(sourcePath);
		Bitmap bitmap2 = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap2))
		{
			graphics.DrawImageUnscaled(bitmap, 0, 0);
		}
		RemoveConnectedWhite(bitmap2);
		bitmap2.Save(outputPath, ImageFormat.Png);
		return new Bitmap(bitmap2);
	}

	private static void RemoveConnectedWhite(Bitmap image)
	{
		int width = image.Width;
		int height = image.Height;
		bool[] background = new bool[width * height];
		Queue<int> queue = new Queue<int>(width * 2 + height * 2);
		for (int i = 0; i < width; i++)
		{
			Seed(i, 0);
			Seed(i, height - 1);
		}
		for (int j = 0; j < height; j++)
		{
			Seed(0, j);
			Seed(width - 1, j);
		}
		int[] array = new int[4] { -1, 1, 0, 0 };
		int[] array2 = new int[4] { 0, 0, -1, 1 };
		while (queue.Count > 0)
		{
			int num = queue.Dequeue();
			int num2 = num % width;
			int num3 = num / width;
			for (int k = 0; k < 4; k++)
			{
				int num4 = num2 + array[k];
				int num5 = num3 + array2[k];
				if ((uint)num4 < width && (uint)num5 < height)
				{
					int num6 = num5 * width + num4;
					if (!background[num6] && IsBackgroundLike(image.GetPixel(num4, num5)))
					{
						background[num6] = true;
						queue.Enqueue(num6);
					}
				}
			}
		}
		RemoveEnclosedLegGap(image, background);
		for (int l = 0; l < height; l++)
		{
			for (int m = 0; m < width; m++)
			{
				int num7 = l * width + m;
				if (background[num7])
				{
					Color pixel = image.GetPixel(m, l);
					image.SetPixel(m, l, Color.FromArgb(0, pixel.R, pixel.G, pixel.B));
				}
				else
				{
					Color pixel2 = image.GetPixel(m, l);
					image.SetPixel(m, l, Color.FromArgb(255, pixel2.R, pixel2.G, pixel2.B));
				}
			}
		}
		void Seed(int x, int y)
		{
			int num8 = y * width + x;
			if (!background[num8] && IsBackgroundLike(image.GetPixel(x, y)))
			{
				background[num8] = true;
				queue.Enqueue(num8);
			}
		}
	}

	private static void RemoveEnclosedLegGap(Bitmap image, bool[] background)
	{
		int width = image.Width;
		int height = image.Height;
		int num = width * 45 / 100;
		int num2 = width * 55 / 100;
		int num3 = height * 54 / 100;
		int num4 = height * 92 / 100;
		Queue<int> queue = new Queue<int>();
		int num5 = width / 2;
		for (int i = num3; i < num4; i++)
		{
			if (IsStrictWhite(image.GetPixel(num5, i)))
			{
				int num6 = i * width + num5;
				if (!background[num6])
				{
					background[num6] = true;
					queue.Enqueue(num6);
				}
			}
		}
		int[] array = new int[4] { -1, 1, 0, 0 };
		int[] array2 = new int[4] { 0, 0, -1, 1 };
		while (queue.Count > 0)
		{
			int num7 = queue.Dequeue();
			int num8 = num7 % width;
			int num9 = num7 / width;
			for (int j = 0; j < 4; j++)
			{
				int num10 = num8 + array[j];
				int num11 = num9 + array2[j];
				if (num10 >= num && num10 < num2 && num11 >= num3 && num11 < num4)
				{
					int num12 = num11 * width + num10;
					if (!background[num12] && IsStrictWhite(image.GetPixel(num10, num11)))
					{
						background[num12] = true;
						queue.Enqueue(num12);
					}
				}
			}
		}
	}

	private static bool IsBackgroundLike(Color color)
	{
		int num = Math.Min(color.R, Math.Min(color.G, color.B));
		int num2 = Math.Max(color.R, Math.Max(color.G, color.B));
		if (num >= 232)
		{
			return num2 - num <= 18;
		}
		return false;
	}

	private static bool IsStrictWhite(Color color)
	{
		int num = Math.Min(color.R, Math.Min(color.G, color.B));
		int num2 = Math.Max(color.R, Math.Max(color.G, color.B));
		if (num >= 246)
		{
			return num2 - num <= 8;
		}
		return false;
	}
}
