using System;
using System.IO;

namespace McPetOverlay;

internal static class AppLog
{
	private static readonly object Sync = new object();
	private const long MaxBytes = 1000000;

	internal static string FilePath => Path.Combine(AppConfig.ConfigDirectory, "overlay.log");

	internal static void Write(string message)
	{
		try
		{
			lock (Sync)
			{
				Directory.CreateDirectory(AppConfig.ConfigDirectory);
				FileInfo info = new FileInfo(FilePath);
				if (info.Exists && info.Length > MaxBytes)
					File.Delete(FilePath);
				File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
			}
		}
		catch
		{
		}
	}
}
