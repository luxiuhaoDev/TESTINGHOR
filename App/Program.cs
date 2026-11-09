using System;
using System.Windows.Forms;

namespace McPetOverlay;

internal static class Program
{
	[STAThread]
	private static void Main()
	{
		ApplicationConfiguration.Initialize();
		AppConfig config = AppConfig.Load();
		config.Save();
		if (config.StartupAnimationEnabled)
		{
			using StartupAnimationForm animation = new StartupAnimationForm();
			animation.ShowDialog();
			OverlayForm.RestoreMinecraftForeground();
		}
		Application.Run(new OverlayForm(config));
	}
}