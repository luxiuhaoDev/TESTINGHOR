using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace McPetOverlay;

internal sealed class SettingsBackdropForm : Form
{
    private readonly Action closeRequested;
    private AppConfig config;
    private double animationProgress;
    private bool materialConfigured;
    internal int LastCompositionResult { get; private set; }

    internal SettingsBackdropForm(AppConfig config, Action closeRequested)
    {
        this.config = config;
        this.closeRequested = closeRequested;
        Text = "TESTING HOR DESKTOP PET FEATURES Background";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Opacity = 0d;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams value = base.CreateParams;
            value.ExStyle |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
            return value;
        }
    }

    internal void Configure(AppConfig value)
    {
        config = value;
        materialConfigured = false;
        BackColor = Color.Black;
        ApplyMaterial(animationProgress);
    }

    internal void SetAnimationProgress(double progress)
    {
        animationProgress = Math.Clamp(progress, 0d, 1d);
        ApplyMaterial(animationProgress);
    }

    private void ApplyMaterial(double progress)
    {
        if (!IsHandleCreated)
            return;
        EnsureMaterialConfigured();
        int transparency = Math.Clamp(config.GuiBackgroundTransparencyPercent, 0, 100);
        double opacity = 1d - transparency / 100d;
        if (!config.GuiBackgroundBlurEnabled)
        {
            Opacity = Math.Clamp(opacity * progress, 0d, 0.96d);
            return;
        }
        Opacity = Math.Clamp((0.18d + opacity * 0.82d) * progress, 0.01d, 1d);
    }

    private void EnsureMaterialConfigured()
    {
        if (materialConfigured)
            return;
        materialConfigured = true;
        int transparency = Math.Clamp(config.GuiBackgroundTransparencyPercent, 0, 100);
        double opacity = 1d - transparency / 100d;
        if (!config.GuiBackgroundBlurEnabled)
        {
            TransparencyKey = Color.Empty;
            BackColor = EpsilonTheme.Background(config);
            SetSystemBackdrop(false);
            DisableAccent();
            return;
        }
        TransparencyKey = Color.Empty;
        BackColor = Color.Black;
        if (!SetSystemBackdrop(true))
        {
            int alpha = Math.Clamp((int)Math.Round(18d + 142d * opacity), 18, 160);
            ApplyAccent(NativeMethods.AccentState.EnableAcrylicBlurBehind, Color.Black, alpha);
        }
    }

    private void DisableAccent()
    {
        ApplyAccent(NativeMethods.AccentState.Disabled, Color.Black, 0);
    }

    private bool SetSystemBackdrop(bool enabled)
    {
        int darkMode = 1;
        NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        int backdropType = enabled ? NativeMethods.DwmsbtTransientWindow : NativeMethods.DwmsbtNone;
        int result = NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DwmwaSystemBackdropType, ref backdropType, sizeof(int));
        NativeMethods.Margins margins = enabled
            ? new NativeMethods.Margins { LeftWidth = -1, RightWidth = -1, TopHeight = -1, BottomHeight = -1 }
            : new NativeMethods.Margins();
        NativeMethods.DwmExtendFrameIntoClientArea(Handle, ref margins);
        LastCompositionResult = result;
        return result == 0;
    }

    private void ApplyAccent(NativeMethods.AccentState state, Color tint, int alpha)
    {
        NativeMethods.AccentPolicy policy = new NativeMethods.AccentPolicy
        {
            AccentState = state,
            AccentFlags = state == NativeMethods.AccentState.EnableAcrylicBlurBehind ? 2 : 0,
            GradientColor = ToAbgr(tint, alpha),
            AnimationId = 0
        };
        int size = Marshal.SizeOf<NativeMethods.AccentPolicy>();
        nint pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            NativeMethods.WindowCompositionAttributeData data = new NativeMethods.WindowCompositionAttributeData
            {
                Attribute = NativeMethods.WindowCompositionAttribute.AccentPolicy,
                Data = pointer,
                SizeOfData = size
            };
            LastCompositionResult = NativeMethods.SetWindowCompositionAttribute(Handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static uint ToAbgr(Color color, int alpha)
    {
        return ((uint)Math.Clamp(alpha, 0, 255) << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
            closeRequested();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            closeRequested();
            return;
        }
        base.OnFormClosing(e);
    }

}
