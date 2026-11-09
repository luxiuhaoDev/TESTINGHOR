using System;
using System.Runtime.InteropServices;

namespace McPetOverlay;

internal sealed partial class AutoInputController
{
	private void UpdateAutoSneak(bool enabled)
	{
		if (IsSprintKeyDown())
		{
			ReleaseInjectedSneak();
			return;
		}
		if (!enabled)
		{
			ReleaseInjectedSneak();
			return;
		}
		if (!IsKeyDown(83))
		{
			ReleaseInjectedSneak();
			return;
		}
		if (config.AutoSneakPitchCheck && playerState.Pitch < config.AutoSneakMinPitch)
		{
			ReleaseInjectedSneak();
			return;
		}
		if (config.AutoSneakGroundOnly && !playerState.OnGround)
		{
			ReleaseInjectedSneak();
			return;
		}
		if (!playerState.OnGround)
		{
			ReleaseInjectedSneak();
			return;
		}
		bool? atEdgeFlag = supportProvider(playerState.X, playerState.Y, playerState.Z);
		bool atEdge = atEdgeFlag.HasValue && !atEdgeFlag.Value;
		if (atEdge)
		{
			if (!injectedShiftDown)
			{
				SendKey(160, keyUp: false);
				injectedShiftDown = true;
				Console.WriteLine("[边缘潜行] Shift 按下（边缘）");
			}
		}
		else if (injectedShiftDown)
		{
			SendKey(160, keyUp: true);
			injectedShiftDown = false;
			Console.WriteLine("[边缘潜行] Shift 释放");
		}
	}

	private void ReleaseInjectedSneak()
	{
		if (injectedShiftDown)
		{
			SendKey(160, keyUp: true);
			injectedShiftDown = false;
			Console.WriteLine("[边缘潜行] Shift 释放");
		}
	}

	private static bool IsSprintKeyDown()
	{
		if (!IsKeyDown(NativeMethods.VkControl) && !IsKeyDown(NativeMethods.VkLControl))
		{
			return IsKeyDown(NativeMethods.VkRControl);
		}
		return true;
	}

	private static void SendKey(int virtualKey, bool keyUp)
	{
		NativeMethods.Input[] inputs = new NativeMethods.Input[1]
		{
			new NativeMethods.Input
			{
				Type = NativeMethods.InputKeyboard,
				Data = new NativeMethods.InputUnion
				{
					Keyboard = new NativeMethods.KeyboardInput
					{
						VirtualKey = (ushort)virtualKey,
						ScanCode = 0,
						Flags = (keyUp ? NativeMethods.KeyEventKeyUp : 0u),
						Time = 0u,
						ExtraInfo = 0u
					}
				}
			}
		};
		NativeMethods.SendInput(1u, inputs, Marshal.SizeOf<NativeMethods.Input>());
	}
}
