using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace McPetOverlay;

internal sealed class MyauBlockHitController : IDisposable
{
    private readonly AppConfig config;
    private readonly Func<bool> canRun;
    private readonly Func<bool?> holdingSwordProvider;
    private readonly Timer timer = new Timer { Interval = 5 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private long nextBlockHitAtMs;
    private long repressAtMs;
    private bool waitingToRepress;

    internal MyauBlockHitController(AppConfig config, Func<bool> canRun, Func<bool?> holdingSwordProvider)
    {
        this.config = config;
        this.canRun = canRun;
        this.holdingSwordProvider = holdingSwordProvider;
        timer.Tick += Tick;
        timer.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        long now = clock.ElapsedMilliseconds;
        bool rightHeld = IsKeyDown(2);
        if (waitingToRepress)
        {
            if (now < repressAtMs)
                return;
            waitingToRepress = false;
            if (rightHeld)
                SendRightButton(keyUp: false);
        }

        bool enabled = config.LeftClickerEnabled
            && config.LeftClickerAutoBlockEnabled
            && canRun()
            && IsKeyDown(1)
            && rightHeld
            && holdingSwordProvider() == true;
        if (!enabled)
        {
            nextBlockHitAtMs = now;
            return;
        }
        if (now < nextBlockHitAtMs)
            return;

        SendRightButton(keyUp: true);
        waitingToRepress = true;
        repressAtMs = now + 50L;
        double ticks = Math.Clamp(config.LeftClickerAutoBlockTicks, 1f, 20f);
        nextBlockHitAtMs = now + Math.Max(50L, (long)Math.Round(ticks * 50d));
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static void SendRightButton(bool keyUp)
    {
        NativeMethods.Input[] inputs =
        {
            new NativeMethods.Input
            {
                Type = NativeMethods.InputMouse,
                Data = new NativeMethods.InputUnion
                {
                    Mouse = new NativeMethods.MouseInput
                    {
                        Flags = keyUp ? 16u : 8u
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
        if (waitingToRepress && IsKeyDown(2))
            SendRightButton(keyUp: false);
        waitingToRepress = false;
    }
}
