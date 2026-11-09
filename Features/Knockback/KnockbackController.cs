using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace McPetOverlay;

internal sealed class KnockbackController : IDisposable
{
	private readonly AppConfig config;

	private readonly Func<bool> canRun;

	private readonly Func<LocalPlayerState> stateProvider;

	private readonly Func<int> hurtTimeProvider;

	private readonly Random rng = new();

	private readonly Stopwatch clock = Stopwatch.StartNew();

	private readonly Timer timer = new()
	{
		Interval = 5
	};

	private int lastHurtTime;

	private bool wasLeftDown;

	private double fwdEndMs = -1;

	private double periodicEndMs = -1;

	private double extraEndMs = -1;

	private double sKeyEndMs = -1;

	private ushort activeKey;

	internal KnockbackController(AppConfig config, Func<bool> canRun, Func<LocalPlayerState> stateProvider, Func<int> hurtTimeProvider)
	{
		this.config = config;
		this.canRun = canRun;
		this.stateProvider = stateProvider;
		this.hurtTimeProvider = hurtTimeProvider;
		timer.Tick += delegate
		{
			Tick();
		};
		timer.Start();
	}

	private void Tick()
	{
		double now = clock.Elapsed.TotalMilliseconds;

		if (!config.KnockbackEnabled)
		{
			ReleaseAllKeys();
			lastHurtTime = 0;
			wasLeftDown = IsKeyDown(1);
			return;
		}

		if (!canRun())
		{
			ReleaseAllKeys();
			CheckLeftState();
			return;
		}

		ProcessKnockback(now);
	}

	private void ProcessKnockback(double now)
	{
		if (periodicEndMs > 0 && now >= periodicEndMs) { ReleaseKey(activeKey); periodicEndMs = -1; }
		if (fwdEndMs > 0 && now >= fwdEndMs) { ReleaseAllKeys(); return; }
		if (extraEndMs > 0 && now >= extraEndMs) { ReleaseAllKeys(); return; }
		if (sKeyEndMs >= 0 && now >= sKeyEndMs) { ReleaseKey(activeKey); sKeyEndMs = -1; }

		bool leftNow = IsKeyDown(1);
		bool leftRisingEdge = leftNow && !wasLeftDown;
		wasLeftDown = leftNow;

		if (leftRisingEdge)
		{
			sKeyEndMs = -1;
			ReleaseKey(activeKey);

			if (NoActiveTimer() && !IsKeyPhysicallyDown(83) && !IsKeyPhysicallyDown(87))
			{
				double dur = config.KnockbackForwardHoldMs;
				ushort key = 87;
				FwdPress(now, dur, key);
			}
			return;
		}

		if (IsKeyPhysicallyDown(87))
		{
			ReleaseKey(activeKey);
			periodicEndMs = -1;
			fwdEndMs = -1;
			return;
		}

		int hurt = hurtTimeProvider();
		bool hurtRisingEdge = lastHurtTime == 0 && hurt > 0;
		lastHurtTime = hurt;

		if (!hurtRisingEdge)
		{
			return;
		}

		if (config.KnockbackRequireSprint && !IsKeyPhysicallyDown(17))
		{
			return;
		}

		if (rng.Next(100) >= config.KnockbackChancePercent)
		{
			return;
		}

		sKeyEndMs = -1;
		ReleaseKey(activeKey);

		if (!IsKeyPhysicallyDown(83))
		{
			periodicEndMs = now + config.KnockbackBackHoldMs;
			PressKey(83);
		}

		double extra = 50.0 + rng.NextDouble() * 40.0;
		extraEndMs = now + extra;
	}

	private void FwdPress(double now, double dur, ushort key)
	{
		if (key == 0) return;
		fwdEndMs = now + dur;
		PressKey(key);
	}

	private bool NoActiveTimer() => fwdEndMs <= 0 && periodicEndMs <= 0;

	private void PressKey(ushort vk)
	{
		if (vk == 0) return;
		ReleaseKey(activeKey);
		activeKey = vk;
		SendKey(vk, keyUp: false);
	}

	private void ReleaseKey(ushort vk)
	{
		if (vk == 0 || activeKey != vk) return;
		SendKey(vk, keyUp: true);
		activeKey = 0;
	}

	private void ReleaseAllKeys()
	{
		ReleaseKey(activeKey);
		fwdEndMs = periodicEndMs = extraEndMs = sKeyEndMs = -1;
	}

	private void CheckLeftState()
	{
		wasLeftDown = IsKeyDown(1);
		lastHurtTime = 0;
	}

	private static bool IsKeyDown(int vk)
	{
		return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
	}

	private static bool IsKeyPhysicallyDown(ushort vk)
	{
		return vk != 0 && (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
	}

	private static void SendKey(int virtualKey, bool keyUp)
	{
		NativeMethods.Input[] inputs = new NativeMethods.Input[1]
		{
			new()
			{
				Type = NativeMethods.InputKeyboard,
				Data = new NativeMethods.InputUnion
				{
					Keyboard = new NativeMethods.KeyboardInput
					{
						VirtualKey = (ushort)virtualKey,
						ScanCode = 0,
						Flags = keyUp ? NativeMethods.KeyEventKeyUp : 0u,
						Time = 0u,
						ExtraInfo = 0u
					}
				}
			}
		};
		NativeMethods.SendInput(1u, inputs, Marshal.SizeOf<NativeMethods.Input>());
	}

	public void Dispose()
	{
		timer.Stop();
		timer.Dispose();
		ReleaseAllKeys();
	}
}
