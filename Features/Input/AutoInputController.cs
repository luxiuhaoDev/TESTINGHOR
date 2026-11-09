using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace McPetOverlay;

internal sealed partial class AutoInputController : IDisposable
{
	private readonly AppConfig config;

	private readonly Func<bool> canRun;

	private readonly Func<LocalPlayerState> stateProvider;

	private readonly Func<double, double, double, bool?> supportProvider;

	private readonly Func<bool?> holdingBlockProvider;

	private readonly Timer timer = new Timer
	{
		Interval = 5
	};

	private readonly Stopwatch clock = Stopwatch.StartNew();

	private readonly Random random = new Random();

	private readonly ClickChannel left = new ClickChannel();

	private readonly ClickChannel right = new ClickChannel();

	private long nextPlayerReadMs;

	private LocalPlayerState playerState = LocalPlayerState.Empty;

	private bool injectedShiftDown;

	internal AutoInputController(AppConfig config, Func<bool> canRun, Func<LocalPlayerState> stateProvider, Func<double, double, double, bool?> supportProvider, Func<bool?> holdingBlockProvider)
	{
		this.config = config;
		this.canRun = canRun;
		this.stateProvider = stateProvider;
		this.supportProvider = supportProvider;
		this.holdingBlockProvider = holdingBlockProvider;
		timer.Tick += delegate
		{
			Tick();
		};
		timer.Start();
	}

	private void Tick()
	{
		long elapsedMilliseconds = clock.ElapsedMilliseconds;
		if (!config.LeftClickerEnabled && !config.RightClickerEnabled && !config.AutoSneakEnabled)
		{
			UpdateAutoSneak(enabled: false);
			left.Reset(elapsedMilliseconds);
			right.Reset(elapsedMilliseconds);
			return;
		}
		if (elapsedMilliseconds >= nextPlayerReadMs)
		{
			nextPlayerReadMs = elapsedMilliseconds + 35;
			playerState = stateProvider();
		}
		bool flag = canRun() && playerState.Valid && playerState.InWorld && !playerState.GuiOpen;
		UpdateClicker(left, isLeft: true, flag && config.LeftClickerEnabled, config.LeftClickerMinCps, config.LeftClickerMaxCps, config.LeftClickerJitterPercent, config.LeftClickerHoldRequired, elapsedMilliseconds);
		UpdateClicker(right, isLeft: false, flag && config.RightClickerEnabled, config.RightClickerMinCps, config.RightClickerMaxCps, config.RightClickerJitterPercent, config.RightClickerHoldRequired, elapsedMilliseconds, config.RightClickerBlocksOnly, config.RightClickerPlaceCooldownTicks);
		UpdateAutoSneak(flag && config.AutoSneakEnabled && config.RightClickerEnabled && IsKeyDown(2));
	}

	private static bool IsKeyDown(int virtualKey)
	{
		return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
	}

	public void Dispose()
	{
		timer.Stop();
		timer.Dispose();
		if (injectedShiftDown)
		{
			SendKey(160, keyUp: true);
		}
		injectedShiftDown = false;
	}
}
