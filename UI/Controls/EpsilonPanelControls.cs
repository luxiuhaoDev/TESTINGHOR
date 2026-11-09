using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace McPetOverlay;

internal enum EpsilonCategory
{
    Combat,
    Player,
    Movement,
    Render
}

internal abstract class EpsilonSettingModel
{
    internal string Name { get; }

    protected EpsilonSettingModel(string name)
    {
        Name = name;
    }
}

internal sealed class EpsilonBoolSettingModel : EpsilonSettingModel
{
    internal Func<bool> Getter { get; }
    internal Action<bool> Setter { get; }

    internal EpsilonBoolSettingModel(string name, Func<bool> getter, Action<bool> setter) : base(name)
    {
        Getter = getter;
        Setter = setter;
    }
}

internal sealed class EpsilonSliderSettingModel : EpsilonSettingModel
{
    internal Func<double> Getter { get; }
    internal Action<double> Setter { get; }
    internal double Minimum { get; }
    internal double Maximum { get; }
    internal double Step { get; }

    internal EpsilonSliderSettingModel(string name, Func<double> getter, Action<double> setter, double minimum, double maximum, double step) : base(name)
    {
        Getter = getter;
        Setter = setter;
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
    }
}

internal sealed class EpsilonActionSettingModel : EpsilonSettingModel
{
    internal Func<string> ValueGetter { get; }
    internal Action Action { get; }
    internal Action? SecondaryAction { get; }
    internal Func<Color>? ColorGetter { get; }

    internal EpsilonActionSettingModel(string name, Func<string> valueGetter, Action action, Func<Color>? colorGetter = null, Action? secondaryAction = null) : base(name)
    {
        ValueGetter = valueGetter;
        Action = action;
        ColorGetter = colorGetter;
        SecondaryAction = secondaryAction;
    }
}

internal sealed class EpsilonModuleModel
{
    internal string? HotkeyId { get; }
    internal string Name { get; }
    internal EpsilonCategory Category { get; }
    internal Func<bool> EnabledGetter { get; }
    internal Action<bool> EnabledSetter { get; }
    internal List<EpsilonSettingModel> Settings { get; } = new List<EpsilonSettingModel>();

    internal EpsilonModuleModel(string? hotkeyId, string name, EpsilonCategory category, Func<bool> enabledGetter, Action<bool> enabledSetter)
    {
        HotkeyId = hotkeyId;
        Name = name;
        Category = category;
        EnabledGetter = enabledGetter;
        EnabledSetter = enabledSetter;
    }
}

internal sealed class EpsilonPanelControl : Control
{
    private Color Surface => EpsilonTheme.Background(config);
    private Color SurfaceDim => EpsilonTheme.Mix(EpsilonTheme.Background(config), Color.Black, 0.18f);
    private Color SurfaceContainer => EpsilonTheme.Panel(config);
    private Color SurfaceContainerHigh => EpsilonTheme.PanelHigh(config);
    private Color SurfaceContainerHighest => EpsilonTheme.PanelHighest(config);
    private Color Outline => EpsilonTheme.Outline(config);
    private Color Primary => EpsilonTheme.Accent(config);
    private Color OnPrimary => EpsilonTheme.OnAccent(config);
    private Color SecondaryContainer => EpsilonTheme.Secondary(config);
    private Color OnSecondaryContainer => EpsilonTheme.Text(config);
    private Color TextPrimary => EpsilonTheme.Text(config);
    private Color TextSecondary => EpsilonTheme.TextSecondary(config);
    private Color TextMuted => EpsilonTheme.TextMuted(config);

    private const float BasePanelWidth = 584f;
    private const float BasePanelHeight = 324f;
    private const float RailCollapsedWidth = 42f;
    private const float RailExpandedWidth = 120f;
    private const float OuterPadding = 5f;
    private const float SectionGap = 3f;
    private const float ModuleWidth = 164f;
    private const float RowGap = 3f;
    private const float ModuleRowHeight = 34f;
    private const float SettingRowHeight = 28f;

    private readonly List<EpsilonModuleModel> modules;
    private readonly AppConfig config;
    private readonly Action commit;
    private readonly Action closeRequested;
    private readonly Timer animationTimer = new Timer { Interval = 15 };
    private readonly Dictionary<object, float> toggleProgress = new Dictionary<object, float>();
    private EpsilonCategory selectedCategory = EpsilonCategory.Combat;
    private EpsilonModuleModel? selectedModule;
    private bool detailVisible;
    private bool sidebarExpanded;
    private float animatedRailWidth;
    private float animatedUiScale;
    private float moduleScroll;
    private float detailScroll;
    private EpsilonSliderSettingModel? draggingSlider;
    private RectangleF draggingSliderBounds;
    private string searchQuery = string.Empty;
    private bool searchFocused;

    internal bool SearchFocused => searchFocused;

    internal EpsilonPanelControl(List<EpsilonModuleModel> modules, AppConfig config, bool sidebarExpanded, Action commit, Action closeRequested)
    {
        this.modules = modules;
        this.config = config;
        this.sidebarExpanded = sidebarExpanded;
        this.commit = commit;
        this.closeRequested = closeRequested;
        animatedRailWidth = sidebarExpanded ? RailExpandedWidth : RailCollapsedWidth;
        animatedUiScale = EpsilonTheme.Scale(config);
        foreach (EpsilonModuleModel module in modules)
        {
            toggleProgress[module] = module.EnabledGetter() ? 1f : 0f;
            foreach (EpsilonBoolSettingModel setting in module.Settings.OfType<EpsilonBoolSettingModel>())
                toggleProgress[setting] = setting.Getter() ? 1f : 0f;
        }
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        TabStop = true;
        Cursor = Cursors.Default;
        animationTimer.Tick += delegate
        {
            bool changed = false;
            float target = this.sidebarExpanded ? RailExpandedWidth : RailCollapsedWidth;
            if (Math.Abs(target - animatedRailWidth) >= 0.15f)
            {
                animatedRailWidth += (target - animatedRailWidth) * 0.24f;
                changed = true;
            }
            else if (animatedRailWidth != target)
            {
                animatedRailWidth = target;
                changed = true;
            }
            float scaleTarget = EpsilonTheme.Scale(this.config);
            if (Math.Abs(scaleTarget - animatedUiScale) >= 0.002f)
            {
                animatedUiScale += (scaleTarget - animatedUiScale) * 0.18f;
                changed = true;
            }
            else if (animatedUiScale != scaleTarget)
            {
                animatedUiScale = scaleTarget;
                changed = true;
            }
            foreach (EpsilonModuleModel module in this.modules)
            {
                changed |= AnimateToggle(module, module.EnabledGetter());
                foreach (EpsilonBoolSettingModel setting in module.Settings.OfType<EpsilonBoolSettingModel>())
                    changed |= AnimateToggle(setting, setting.Getter());
            }
            // 动画收敛后不再持续整窗重绘，避免空转占满 UI 线程。
            if (changed)
                Invalidate();
        };
        animationTimer.Start();
    }

    internal bool SidebarExpanded => sidebarExpanded;

    internal bool HandleKeyDown(Keys keyCode)
    {
        if (!searchFocused)
            return false;
        if (keyCode == Keys.Escape)
        {
            searchFocused = false;
            Invalidate();
            return true;
        }
        if (keyCode == Keys.Back)
        {
            if (searchQuery.Length > 0)
                searchQuery = searchQuery[..^1];
            moduleScroll = 0;
            Invalidate();
            return true;
        }
        if (keyCode == Keys.Delete)
        {
            searchQuery = string.Empty;
            moduleScroll = 0;
            Invalidate();
            return true;
        }
        return false;
    }

    internal bool HandleKeyPress(char keyChar)
    {
        if (!searchFocused || char.IsControl(keyChar))
            return false;
        searchQuery += keyChar;
        moduleScroll = 0;
        Invalidate();
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = config.AntiAliasingEnabled ? SmoothingMode.AntiAlias : SmoothingMode.None;
        e.Graphics.PixelOffsetMode = config.AntiAliasingEnabled ? PixelOffsetMode.HighQuality : PixelOffsetMode.None;
        e.Graphics.TextRenderingHint = config.AntiAliasingEnabled ? System.Drawing.Text.TextRenderingHint.ClearTypeGridFit : System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        LayoutRects layout = ComputeLayout();
        DrawPanelChrome(e.Graphics, layout);
        DrawCategoryRail(e.Graphics, layout);
        DrawModuleList(e.Graphics, layout);
        DrawDetailPanel(e.Graphics, layout);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        LayoutRects layout = ComputeLayout();
        PointF point = ToLogical(e.Location, layout.Scale);
        if (!layout.Panel.Contains(point))
        {
            if (e.Button == MouseButtons.Left)
                closeRequested();
            return;
        }

        if (HandleRailClick(point, e.Button, layout))
            return;
        if (HandleModuleClick(point, e.Button, layout))
            return;
        if (HandleDetailClick(point, e.Button, layout))
            return;
        searchFocused = false;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (draggingSlider != null && e.Button == MouseButtons.Left)
        {
            LayoutRects layout = ComputeLayout();
            PointF point = ToLogical(e.Location, layout.Scale);
            UpdateSlider(draggingSlider, draggingSliderBounds, point.X);
        }
        Invalidate();
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        draggingSlider = null;
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        LayoutRects layout = ComputeLayout();
        PointF point = ToLogical(e.Location, layout.Scale);
        if (layout.Modules.Contains(point))
        {
            int visible = GetVisibleModules().Count;
            float viewportHeight = layout.Modules.Height - 40f;
            float max = Math.Max(0f, visible * (ModuleRowHeight + RowGap) - viewportHeight);
            moduleScroll = Math.Clamp(moduleScroll - e.Delta / 120f * 24f, 0f, max);
            Invalidate();
        }
        else if (layout.Detail.Contains(point) && detailVisible && selectedModule != null)
        {
            float viewportHeight = layout.Detail.Height - 78f;
            float max = Math.Max(0f, selectedModule.Settings.Count * (SettingRowHeight + RowGap) - viewportHeight);
            detailScroll = Math.Clamp(detailScroll - e.Delta / 120f * 24f, 0f, max);
            Invalidate();
        }
        base.OnMouseWheel(e);
    }

    private void DrawPanelChrome(Graphics g, LayoutRects layout)
    {
        GraphicsState state = g.Save();
        g.TranslateTransform(layout.OffsetX, layout.OffsetY);
        g.ScaleTransform(layout.Scale, layout.Scale);
        for (int i = 6; i >= 1; i--)
        {
            using SolidBrush shadow = new SolidBrush(Color.FromArgb(8 + i * 2, 0, 0, 0));
            FillRoundedRectangle(g, shadow, new RectangleF(-i * 1.2f, -i * 0.4f, BasePanelWidth + i * 2.4f, BasePanelHeight + i * 2.1f), 17f + i);
        }
        using SolidBrush panel = new SolidBrush(Surface);
        FillRoundedRectangle(g, panel, new RectangleF(0, 0, BasePanelWidth, BasePanelHeight), 17f);
        using SolidBrush section = new SolidBrush(SurfaceDim);
        FillRoundedRectangle(g, section, layout.Rail, 13f);
        FillRoundedRectangle(g, section, layout.Modules, 13f);
        FillRoundedRectangle(g, section, layout.Detail, 13f);
        g.Restore(state);
    }

    private void DrawCategoryRail(Graphics g, LayoutRects layout)
    {
        GraphicsState state = BeginLogical(g, layout);
        RectangleF menu = new RectangleF(layout.Rail.X + 6f, layout.Rail.Y + 4f, 28f, 28f);
        if (IsHovered(menu, layout))
        {
            using SolidBrush hover = new SolidBrush(SurfaceContainerHigh);
            FillRoundedRectangle(g, hover, menu, 12f);
        }
        using Pen menuPen = new Pen(TextPrimary, 1.6f);
        float mx = menu.X + 8f;
        float my = menu.Y + 9f;
        g.DrawLine(menuPen, mx, my, mx + 12f, my);
        g.DrawLine(menuPen, mx, my + 4f, mx + 12f, my + 4f);
        g.DrawLine(menuPen, mx, my + 8f, mx + 12f, my + 8f);

        if (animatedRailWidth > 70f)
        {
            DrawText(g, "TESTING HOR", 0.57f, TextPrimary, layout.Rail.X + 38f, layout.Rail.Y + 5f, FontStyle.Bold);
            DrawText(g, "DESKTOP PET FEATURES", 0.40f, TextSecondary, layout.Rail.X + 38f, layout.Rail.Y + 18f, FontStyle.Bold);
        }

        float y = layout.Rail.Y + 40f;
        foreach (EpsilonCategory category in Enum.GetValues<EpsilonCategory>())
        {
            RectangleF row = new RectangleF(layout.Rail.X + 5f, y, layout.Rail.Width - 10f, 34f);
            bool selected = selectedCategory == category;
            bool hovered = IsHovered(row, layout);
            if (selected || hovered)
            {
                using SolidBrush fill = new SolidBrush(selected ? SecondaryContainer : SurfaceContainerHigh);
                FillRoundedRectangle(g, fill, row, 9f);
            }
            Color foreground = selected ? OnSecondaryContainer : hovered ? TextPrimary : TextSecondary;
            DrawTextCentered(g, CategoryIcon(category), 0.80f, foreground, new RectangleF(row.X + 1f, row.Y, 28f, row.Height), FontStyle.Bold);
            if (animatedRailWidth > 70f)
            {
                DrawText(g, CategoryName(category), 0.62f, selected ? OnSecondaryContainer : TextPrimary, row.X + 30f, row.Y + 11f);
                string count = modules.Count(m => m.Category == category).ToString(CultureInfo.InvariantCulture);
                DrawTextRight(g, count, 0.58f, selected ? OnSecondaryContainer : TextSecondary, row.Right - 10f, row.Y + 11f);
            }
            y += 38f;
        }

        RectangleF settings = new RectangleF(layout.Rail.X + 5f, layout.Rail.Bottom - 39f, layout.Rail.Width - 10f, 34f);
        if (IsHovered(settings, layout))
        {
            using SolidBrush hover = new SolidBrush(SurfaceContainerHigh);
            FillRoundedRectangle(g, hover, settings, 9f);
        }
        DrawTextCentered(g, "⚙", 0.82f, TextSecondary, new RectangleF(settings.X + 1f, settings.Y, 28f, settings.Height));
        if (animatedRailWidth > 70f)
            DrawText(g, "界面设置", 0.60f, TextPrimary, settings.X + 30f, settings.Y + 11f);
        g.Restore(state);
    }

    private void DrawModuleList(Graphics g, LayoutRects layout)
    {
        GraphicsState state = BeginLogical(g, layout);
        DrawText(g, CategoryName(selectedCategory), 0.78f, TextPrimary, layout.Modules.X + 6f, layout.Modules.Y + 9f, FontStyle.Bold);
        DrawText(g, "功能模块", 0.56f, TextSecondary, layout.Modules.X + 6f, layout.Modules.Y + 21f);
        RectangleF search = GetSearchBounds(layout);
        using SolidBrush searchFill = new SolidBrush(searchFocused ? SurfaceContainerHighest : SurfaceContainer);
        FillRoundedRectangle(g, searchFill, search, 8f);
        string searchText = searchQuery.Length == 0 && !searchFocused ? "搜索" : searchQuery;
        DrawText(g, searchText, 0.52f, searchQuery.Length == 0 ? TextMuted : TextPrimary, search.X + 7f, search.Y + 5f);
        if (searchFocused)
        {
            using Pen caret = new Pen(Primary, 1f);
            float caretX = search.X + 7f + MeasureText(g, searchText, 0.52f).Width + 1f;
            g.DrawLine(caret, caretX, search.Y + 4f, caretX, search.Bottom - 4f);
        }

        RectangleF viewport = new RectangleF(layout.Modules.X + 3f, layout.Modules.Y + 34f, layout.Modules.Width - 6f, layout.Modules.Height - 40f);
        Region oldClip = g.Clip;
        g.SetClip(viewport);
        float y = viewport.Y - moduleScroll;
        foreach (EpsilonModuleModel module in GetVisibleModules())
        {
            RectangleF row = new RectangleF(viewport.X, y, viewport.Width, ModuleRowHeight);
            bool hovered = IsHovered(row, layout);
            bool selected = detailVisible && selectedModule == module;
            bool moduleEnabled = module.EnabledGetter();
            float moduleProgress = GetToggleProgress(module, moduleEnabled);
            using SolidBrush fill = new SolidBrush(selected ? Color.FromArgb(72, Primary) : hovered ? SurfaceContainerHigh : SurfaceContainer);
            FillRoundedRectangle(g, fill, row, 9f);
            if (moduleProgress > 0.01f)
            {
                using SolidBrush accent = new SolidBrush(Color.FromArgb((int)Math.Round(255f * moduleProgress), Primary));
                float accentHeight = (row.Height - 16f) * (0.35f + 0.65f * moduleProgress);
                FillRoundedRectangle(g, accent, new RectangleF(row.X + 2.5f, row.Y + (row.Height - accentHeight) / 2f, 3f, accentHeight), 1.5f);
            }
            if (config.PanelModuleOutlineEnabled)
            {
                Color outlineColor = Color.FromArgb(config.PanelModuleOutlineColorArgb);
                using Pen cardOutline = new Pen(Color.FromArgb(selected ? 255 : hovered ? 225 : 175, outlineColor), selected ? 1.5f : 1f);
                DrawRoundedRectangle(g, cardOutline, new RectangleF(row.X + 0.5f, row.Y + 0.5f, row.Width - 1f, row.Height - 1f), 9f);
            }
            float textX = row.X + 8f;
            DrawText(g, module.Name, 0.70f, selected ? OnSecondaryContainer : TextPrimary, textX, row.Y + 6f, FontStyle.Bold);
            DrawText(g, CategoryName(module.Category), 0.56f, selected ? Color.FromArgb(220, OnSecondaryContainer) : TextSecondary, textX, row.Y + 19f);
            RectangleF toggle = GetModuleToggleBounds(row);
            DrawSwitch(g, toggle, moduleProgress, IsHovered(toggle, layout));
            y += ModuleRowHeight + RowGap;
        }
        g.Clip = oldClip;
        g.Restore(state);
    }

    private void DrawDetailPanel(Graphics g, LayoutRects layout)
    {
        GraphicsState state = BeginLogical(g, layout);
        if (!detailVisible || selectedModule == null)
        {
            DrawText(g, "功能设置", 0.78f, TextPrimary, layout.Detail.X + 6f, layout.Detail.Y + 10f, FontStyle.Bold);
            DrawTextCentered(g, "右键功能打开详细设置", 0.60f, TextMuted, new RectangleF(layout.Detail.X + 12f, layout.Detail.Y + 62f, layout.Detail.Width - 24f, 42f));
            g.Restore(state);
            return;
        }

        DrawText(g, selectedModule.Name, 0.78f, TextPrimary, layout.Detail.X + 6f, layout.Detail.Y + 10f, FontStyle.Bold);
        RectangleF header = new RectangleF(layout.Detail.X + 3f, layout.Detail.Y + 34f, layout.Detail.Width - 6f, 36f);
        using SolidBrush headerFill = new SolidBrush(SurfaceContainer);
        FillRoundedRectangle(g, headerFill, header, 9f);
        float headerProgress = GetToggleProgress(selectedModule, selectedModule.EnabledGetter());
        using SolidBrush headerAccent = new SolidBrush(EpsilonTheme.Mix(Outline, Primary, headerProgress));
        FillRoundedRectangle(g, headerAccent, new RectangleF(header.X + 3f, header.Y + 9f, 2.5f, header.Height - 18f), 1.25f);
        DrawText(g, "启用", 0.62f, TextPrimary, header.X + 8f, header.Y + 12f);
        RectangleF headerToggle = new RectangleF(header.Right - 33f, header.Y + 10f, 26f, 16f);
        DrawSwitch(g, headerToggle, headerProgress, IsHovered(headerToggle, layout));

        RectangleF viewport = new RectangleF(layout.Detail.X + 3f, header.Bottom + 6f, layout.Detail.Width - 6f, layout.Detail.Bottom - header.Bottom - 10f);
        Region oldClip = g.Clip;
        g.SetClip(viewport);
        float y = viewport.Y - detailScroll;
        foreach (EpsilonSettingModel setting in selectedModule.Settings)
        {
            RectangleF row = new RectangleF(viewport.X, y, viewport.Width, SettingRowHeight);
            using SolidBrush fill = new SolidBrush(IsHovered(row, layout) ? SurfaceContainerHigh : SurfaceContainer);
            FillRoundedRectangle(g, fill, row, 9f);
            DrawSetting(g, setting, row, layout);
            y += SettingRowHeight + RowGap;
        }
        g.Clip = oldClip;
        g.Restore(state);
    }

    private void DrawSetting(Graphics g, EpsilonSettingModel setting, RectangleF row, LayoutRects layout)
    {
        if (setting is EpsilonBoolSettingModel boolSetting)
        {
            DrawText(g, boolSetting.Name, 0.62f, TextPrimary, row.X + 5f, row.Y + 9f);
            RectangleF toggle = new RectangleF(row.Right - 31f, row.Y + 6f, 26f, 16f);
            DrawSwitch(g, toggle, GetToggleProgress(boolSetting, boolSetting.Getter()), IsHovered(toggle, layout));
            return;
        }
        if (setting is EpsilonSliderSettingModel slider)
        {
            double value = Math.Clamp(slider.Getter(), slider.Minimum, slider.Maximum);
            DrawText(g, slider.Name, 0.58f, TextPrimary, row.X + 5f, row.Y + 4f);
            string valueText = Math.Abs(value - Math.Round(value)) < 0.001 ? Math.Round(value).ToString(CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture);
            DrawTextRight(g, valueText, 0.56f, TextSecondary, row.Right - 6f, row.Y + 4f);
            RectangleF bar = new RectangleF(row.X + 6f, row.Bottom - 8f, row.Width - 12f, 4f);
            using SolidBrush track = new SolidBrush(SurfaceContainerHighest);
            FillRoundedRectangle(g, track, bar, 2f);
            float progress = (float)((value - slider.Minimum) / Math.Max(0.0001, slider.Maximum - slider.Minimum));
            float activeWidth = Math.Max(4f, bar.Width * progress);
            using SolidBrush active = new SolidBrush(Primary);
            FillRoundedRectangle(g, active, new RectangleF(bar.X, bar.Y, activeWidth, bar.Height), 2f);
            float knobDiameter = IsHovered(row, layout) ? 7.5f : 6f;
            float knobCenterX = bar.X + bar.Width * progress;
            using SolidBrush knob = new SolidBrush(Primary);
            g.FillEllipse(knob, knobCenterX - knobDiameter / 2f, bar.Y + bar.Height / 2f - knobDiameter / 2f, knobDiameter, knobDiameter);
            return;
        }
        if (setting is EpsilonActionSettingModel action)
        {
            DrawText(g, action.Name, 0.62f, TextPrimary, row.X + 5f, row.Y + 9f);
            string value = action.ValueGetter();
            float right = row.Right - 7f;
            if (action.ColorGetter != null)
            {
                using SolidBrush swatch = new SolidBrush(action.ColorGetter());
                FillRoundedRectangle(g, swatch, new RectangleF(row.Right - 25f, row.Y + 7f, 18f, 14f), 5f);
                right -= 24f;
            }
            DrawTextRight(g, value, 0.56f, TextSecondary, right, row.Y + 9f);
        }
    }

    private bool HandleRailClick(PointF point, MouseButtons button, LayoutRects layout)
    {
        if (!layout.Rail.Contains(point) || button != MouseButtons.Left)
            return false;
        RectangleF menu = new RectangleF(layout.Rail.X + 6f, layout.Rail.Y + 4f, 28f, 28f);
        if (menu.Contains(point))
        {
            sidebarExpanded = !sidebarExpanded;
            Invalidate();
            return true;
        }
        float y = layout.Rail.Y + 40f;
        foreach (EpsilonCategory category in Enum.GetValues<EpsilonCategory>())
        {
            RectangleF row = new RectangleF(layout.Rail.X + 5f, y, layout.Rail.Width - 10f, 34f);
            if (row.Contains(point))
            {
                selectedCategory = category;
                selectedModule = null;
                detailVisible = false;
                moduleScroll = 0;
                detailScroll = 0;
                searchFocused = false;
                Invalidate();
                return true;
            }
            y += 38f;
        }
        RectangleF settings = new RectangleF(layout.Rail.X + 5f, layout.Rail.Bottom - 39f, layout.Rail.Width - 10f, 34f);
        if (settings.Contains(point))
        {
            selectedModule = modules.FirstOrDefault(m => m.Name == "界面设置");
            detailVisible = selectedModule != null;
            detailScroll = 0;
            Invalidate();
            return true;
        }
        return true;
    }

    private bool HandleModuleClick(PointF point, MouseButtons button, LayoutRects layout)
    {
        if (!layout.Modules.Contains(point))
            return false;
        RectangleF search = GetSearchBounds(layout);
        if (button == MouseButtons.Left && search.Contains(point))
        {
            searchFocused = true;
            Invalidate();
            return true;
        }
        searchFocused = false;
        RectangleF viewport = new RectangleF(layout.Modules.X + 3f, layout.Modules.Y + 34f, layout.Modules.Width - 6f, layout.Modules.Height - 40f);
        float y = viewport.Y - moduleScroll;
        foreach (EpsilonModuleModel module in GetVisibleModules())
        {
            RectangleF row = new RectangleF(viewport.X, y, viewport.Width, ModuleRowHeight);
            if (row.Contains(point) && viewport.Contains(point))
            {
                if (button == MouseButtons.Left)
                {
                    module.EnabledSetter(!module.EnabledGetter());
                    commit();
                }
                else if (button == MouseButtons.Right && module.Settings.Count > 0)
                {
                    selectedModule = module;
                    detailVisible = true;
                    detailScroll = 0;
                }
                Invalidate();
                return true;
            }
            y += ModuleRowHeight + RowGap;
        }
        return true;
    }

    private bool HandleDetailClick(PointF point, MouseButtons button, LayoutRects layout)
    {
        if (!layout.Detail.Contains(point)
            || (button != MouseButtons.Left && button != MouseButtons.Right)
            || !detailVisible || selectedModule == null)
            return false;
        RectangleF header = new RectangleF(layout.Detail.X + 3f, layout.Detail.Y + 34f, layout.Detail.Width - 6f, 36f);
        RectangleF headerToggle = new RectangleF(header.Right - 33f, header.Y + 10f, 26f, 16f);
        if (button == MouseButtons.Left && headerToggle.Contains(point))
        {
            selectedModule.EnabledSetter(!selectedModule.EnabledGetter());
            commit();
            Invalidate();
            return true;
        }
        RectangleF viewport = new RectangleF(layout.Detail.X + 3f, header.Bottom + 6f, layout.Detail.Width - 6f, layout.Detail.Bottom - header.Bottom - 10f);
        float y = viewport.Y - detailScroll;
        foreach (EpsilonSettingModel setting in selectedModule.Settings)
        {
            RectangleF row = new RectangleF(viewport.X, y, viewport.Width, SettingRowHeight);
            if (row.Contains(point) && viewport.Contains(point))
            {
                if (button == MouseButtons.Left && setting is EpsilonBoolSettingModel boolSetting)
                {
                    boolSetting.Setter(!boolSetting.Getter());
                    commit();
                }
                else if (button == MouseButtons.Left && setting is EpsilonSliderSettingModel slider)
                {
                    RectangleF bar = new RectangleF(row.X + 6f, row.Bottom - 12f, row.Width - 12f, 12f);
                    draggingSlider = slider;
                    draggingSliderBounds = bar;
                    UpdateSlider(slider, bar, point.X);
                }
                else if (setting is EpsilonActionSettingModel action)
                {
                    if (button == MouseButtons.Right)
                        action.SecondaryAction?.Invoke();
                    else
                        action.Action();
                }
                Invalidate();
                return true;
            }
            y += SettingRowHeight + RowGap;
        }
        return true;
    }

    private void UpdateSlider(EpsilonSliderSettingModel slider, RectangleF bar, float x)
    {
        double t = Math.Clamp((x - bar.X) / Math.Max(1f, bar.Width), 0d, 1d);
        double value = slider.Minimum + (slider.Maximum - slider.Minimum) * t;
        if (slider.Step > 0)
            value = Math.Round(value / slider.Step) * slider.Step;
        slider.Setter(Math.Clamp(value, slider.Minimum, slider.Maximum));
        commit();
        Invalidate();
    }

    private List<EpsilonModuleModel> GetVisibleModules()
    {
        IEnumerable<EpsilonModuleModel> query = modules.Where(m => m.Category == selectedCategory && m.Name != "界面设置");
        if (!string.IsNullOrWhiteSpace(searchQuery))
            query = query.Where(m => m.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
        return query.ToList();
    }

    private RectangleF GetSearchBounds(LayoutRects layout)
    {
        return new RectangleF(layout.Modules.Right - 82f, layout.Modules.Y + 8f, 76f, 18f);
    }

    private static RectangleF GetModuleToggleBounds(RectangleF row)
    {
        return new RectangleF(row.Right - 31f, row.Y + 9f, 26f, 16f);
    }

    private LayoutRects ComputeLayout()
    {
        float fitScale = Math.Min((ClientSize.Width - 28f) / BasePanelWidth, (ClientSize.Height - 28f) / BasePanelHeight);
        float baseScale = Math.Min(1.25f, fitScale);
        float scale = Math.Clamp(baseScale * animatedUiScale, 0.42f, 2.25f);
        float offsetX = (ClientSize.Width - BasePanelWidth * scale) / 2f;
        float offsetY = (ClientSize.Height - BasePanelHeight * scale) / 2f;
        float railWidth = animatedRailWidth;
        RectangleF panel = new RectangleF(0, 0, BasePanelWidth, BasePanelHeight);
        RectangleF rail = new RectangleF(OuterPadding, OuterPadding, railWidth, BasePanelHeight - OuterPadding * 2);
        RectangleF moduleList = new RectangleF(rail.Right + SectionGap, OuterPadding, ModuleWidth, rail.Height);
        RectangleF detail = new RectangleF(moduleList.Right + SectionGap, OuterPadding, BasePanelWidth - OuterPadding - moduleList.Right - SectionGap, rail.Height);
        return new LayoutRects(scale, offsetX, offsetY, panel, rail, moduleList, detail);
    }

    private PointF ToLogical(Point point, float scale)
    {
        LayoutRects layout = ComputeLayout();
        return new PointF((point.X - layout.OffsetX) / scale, (point.Y - layout.OffsetY) / scale);
    }

    private bool IsHovered(RectangleF rect, LayoutRects layout)
    {
        Point client = PointToClient(Cursor.Position);
        PointF logical = new PointF((client.X - layout.OffsetX) / layout.Scale, (client.Y - layout.OffsetY) / layout.Scale);
        return rect.Contains(logical);
    }

    private static GraphicsState BeginLogical(Graphics g, LayoutRects layout)
    {
        GraphicsState state = g.Save();
        g.TranslateTransform(layout.OffsetX, layout.OffsetY);
        g.ScaleTransform(layout.Scale, layout.Scale);
        return state;
    }

    private bool AnimateToggle(object key, bool enabled)
    {
        float target = enabled ? 1f : 0f;
        float current = GetToggleProgress(key, enabled);
        float next = current + (target - current) * 0.24f;
        if (Math.Abs(target - next) < 0.004f)
            next = target;
        if (Math.Abs(next - current) < 0.0001f)
            return false;
        toggleProgress[key] = next;
        return true;
    }

    private float GetToggleProgress(object key, bool enabled)
    {
        if (!toggleProgress.TryGetValue(key, out float value))
        {
            value = enabled ? 1f : 0f;
            toggleProgress[key] = value;
        }
        return Math.Clamp(value, 0f, 1f);
    }

    private void DrawSwitch(Graphics g, RectangleF bounds, float progress, bool hovered)
    {
        float eased = progress * progress * (3f - 2f * progress);
        Color offColor = EpsilonTheme.Mix(SurfaceContainerHighest, hovered ? Primary : Outline, hovered ? 0.18f : 0.06f);
        Color trackColor = EpsilonTheme.Mix(offColor, Primary, eased);
        using SolidBrush track = new SolidBrush(trackColor);
        FillRoundedRectangle(g, track, bounds, 8f);
        if (eased < 0.99f)
        {
            using Pen outline = new Pen(Color.FromArgb((int)Math.Round(168f * (1f - eased)), Outline), 1f);
            DrawRoundedRectangle(g, outline, bounds, 8f);
        }
        float baseKnobSize = bounds.Height - 4f;
        float pulse = (float)Math.Sin(Math.PI * progress);
        float knobSize = baseKnobSize + pulse * 1.2f;
        float knobCenterX = bounds.X + 2f + baseKnobSize / 2f + (bounds.Width - baseKnobSize - 4f) * eased;
        float knobCenterY = bounds.Y + bounds.Height / 2f;
        using SolidBrush knob = new SolidBrush(EpsilonTheme.Mix(TextSecondary, OnPrimary, eased));
        g.FillEllipse(knob, knobCenterX - knobSize / 2f, knobCenterY - knobSize / 2f, knobSize, knobSize);
    }

    private static string CategoryName(EpsilonCategory category)
    {
        return category switch
        {
            EpsilonCategory.Combat => "战斗",
            EpsilonCategory.Player => "玩家",
            EpsilonCategory.Movement => "移动",
            EpsilonCategory.Render => "渲染",
            _ => category.ToString()
        };
    }

    private static string CategoryIcon(EpsilonCategory category)
    {
        return category switch
        {
            EpsilonCategory.Combat => "⚔",
            EpsilonCategory.Player => "●",
            EpsilonCategory.Movement => "➜",
            EpsilonCategory.Render => "✦",
            _ => "•"
        };
    }

    private static readonly Dictionary<(int Pixel, FontStyle Style), Font> UiFontCache = new Dictionary<(int, FontStyle), Font>();

    private static Font UiFont(float scale, FontStyle style = FontStyle.Regular)
    {
        int pixel = Math.Max(6, (int)Math.Round(10f * scale));
        (int, FontStyle) key = (pixel, style);
        if (!UiFontCache.TryGetValue(key, out Font? font))
        {
            font = new Font("Microsoft YaHei UI", pixel, style, GraphicsUnit.Pixel);
            UiFontCache[key] = font;
        }
        return font;
    }

    private static void DrawText(Graphics g, string text, float scale, Color color, float x, float y, FontStyle style = FontStyle.Regular)
    {
        Font font = UiFont(scale, style);
        using SolidBrush brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, y, StringFormat.GenericTypographic);
    }

    private static void DrawTextRight(Graphics g, string text, float scale, Color color, float right, float y)
    {
        SizeF size = MeasureText(g, text, scale);
        DrawText(g, text, scale, color, right - size.Width, y);
    }

    private static void DrawTextCentered(Graphics g, string text, float scale, Color color, RectangleF bounds, FontStyle style = FontStyle.Regular)
    {
        Font font = UiFont(scale, style);
        using SolidBrush brush = new SolidBrush(color);
        using StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        g.DrawString(text, font, brush, bounds, format);
    }

    private static SizeF MeasureText(Graphics g, string text, float scale)
    {
        Font font = UiFont(scale);
        return g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
    }

    private static void FillRoundedRectangle(Graphics g, Brush brush, RectangleF rect, float radius)
    {
        using GraphicsPath path = RoundedPath(rect, radius);
        g.FillPath(brush, path);
    }

    private static void DrawRoundedRectangle(Graphics g, Pen pen, RectangleF rect, float radius)
    {
        using GraphicsPath path = RoundedPath(rect, radius);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedPath(RectangleF rect, float radius)
    {
        float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        GraphicsPath path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            animationTimer.Dispose();
        base.Dispose(disposing);
    }

    private readonly record struct LayoutRects(
        float Scale, float OffsetX, float OffsetY, RectangleF Panel, RectangleF Rail, RectangleF Modules, RectangleF Detail);
}
