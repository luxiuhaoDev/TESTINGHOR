using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace McPetOverlay;

internal sealed partial class AutoInputController
{
	private sealed class ClickChannel
	{
		internal long NextClickAtMs;

		internal long NextWaveAtMs;

		internal double TargetCps;

		internal void Reset(long now)
		{
			NextClickAtMs = now;
			NextWaveAtMs = now;
			TargetCps = 0.0;
		}
	}

	private void UpdateClicker(ClickChannel channel, bool isLeft, bool enabled, int configuredMinCps, int configuredMaxCps, int jitterPercent, bool holdRequired, long now, bool blocksOnly = false, int placeCooldownTicks = 0)
	{
		bool flag = IsKeyDown(isLeft ? 1 : 2);
		bool? flag2 = (isLeft ? new bool?(false) : holdingBlockProvider());
		if (!enabled || (holdRequired && !flag) || ((!isLeft & blocksOnly) && flag2 != true))
		{
			channel.Reset(now);
			return;
		}
		int num = Math.Clamp(Math.Min(configuredMinCps, configuredMaxCps), 1, 30);
		int num2 = Math.Clamp(Math.Max(configuredMinCps, configuredMaxCps), num, 30);
		if (channel.TargetCps <= 0.0 || now >= channel.NextWaveAtMs)
		{
			double num3 = ((channel.TargetCps <= 0.0) ? ((double)(num + num2) * 0.5) : channel.TargetCps);
			double num4 = (random.NextDouble() * 2.0 - 1.0) * Math.Max(0.35, (double)(num2 - num) * 0.35);
			channel.TargetCps = Math.Clamp(num3 + num4, num, num2);
			channel.NextWaveAtMs = now + random.Next(260, 1100);
		}
		if (now >= channel.NextClickAtMs)
		{
			SendMousePulse(isLeft, flag);
			double num5 = (double)Math.Clamp(jitterPercent, 0, 45) / 100.0;
			double value = channel.TargetCps * (1.0 + (random.NextDouble() * 2.0 - 1.0) * num5);
			value = Math.Clamp(value, (double)num * 0.75, (double)num2 * 1.25);
			int num6 = Math.Max(12, (int)Math.Round(1000.0 / value));
			if (!isLeft && flag2 == true)
			{
				num6 = Math.Max(num6, Math.Clamp(placeCooldownTicks, 1, 20) * 50);
			}
			if (random.NextDouble() < 0.025)
			{
				num6 += random.Next(35, 130);
			}
			channel.NextClickAtMs = now + num6;
		}
	}

	private static void SendMousePulse(bool left, bool physicallyHeld)
	{
		uint num = (left ? 2u : 8u);
		uint num2 = (left ? 4u : 16u);
		NativeMethods.Input[] array = ((!physicallyHeld) ? new uint[2] { num, num2 } : new uint[2] { num2, num }).Select((uint flags) => new NativeMethods.Input
		{
			Type = NativeMethods.InputMouse,
			Data = new NativeMethods.InputUnion
			{
				Mouse = new NativeMethods.MouseInput
				{
					Dx = 0,
					Dy = 0,
					MouseData = 0u,
					Flags = flags,
					Time = 0u,
					ExtraInfo = 0u
				}
			}
		}).ToArray();
		NativeMethods.SendInput((uint)array.Length, array, Marshal.SizeOf<NativeMethods.Input>());
	}
}
