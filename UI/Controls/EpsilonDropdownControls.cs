using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace McPetOverlay;

/// <summary>
/// 卡片式 ClickGUI（瀑布模式）。
/// 视觉参考高星开源卡片 UI（LVGL / Nuklear 的卡片层级与强调色 chip 语言），
/// 全部由 GDI+ 自绘，无第三方依赖，动画按 60fps 以上节流。
/// </summary>
internal sealed class EpsilonDropdownControl : Control
{
    private Color CardBody => EpsilonTheme.Mix(EpsilonTheme.Panel(config), EpsilonTheme.Background(config), 0.16f);
    private Color CardRim => EpsilonTheme.Mix(EpsilonTheme.Panel(config), EpsilonTheme.Background(config), 0.62f);
    private Color CardHighlight => EpsilonTheme.Mix(CardBody, Color.White, 0.09f);
    private Color RowNormal => EpsilonTheme.Mix(CardBody, EpsilonTheme.Text(config), 0.02f);
    private Color RowHover => EpsilonTheme.Mix(CardBody, EpsilonTheme.Text(config), 0.09f);
    private Color RowEnabled => EpsilonTheme.Mix(CardBody, EpsilonTheme.Accent(config), 0.30f);
    private Color WellColor => EpsilonTheme.Mix(CardBody, EpsilonTheme.Background(config), 0.34f);
    private Color WellRow => EpsilonTheme.Mix(WellColor, EpsilonTheme.Text(config), 0.05f);
    private Color Divider => EpsilonTheme.Mix(CardBody, EpsilonTheme.Background(config), 0.48f);
    private Color Primary => EpsilonTheme.Accent(config);
    private Color TextPrimary => EpsilonTheme.Text(config);
    private Color TextSecondary => EpsilonTheme.TextSecondary(config);
    private Color TextMuted => EpsilonTheme.TextMuted(config);
    private Color Outline => EpsilonTheme.Outline(config);

    private const float PanelWidth = 158f;
    private const float HeaderHeight = 38f;
    private const float PanelRadius = 12f;
    private const float PanelGap = 12f;
    private const float MarginX = 16f;
    private const float MarginY = 16f;
    private const float ModuleHeight = 26f;
    private const float SettingHeight = 23f;
    private const float SettingGap = 3f;
    private const float SettingsInset = 6f;
    private const float BottomPadding = 8f;
    private const float ScrollSpeed = 30f;
    private const float ExpandSpeed = 0.30f;

    private static readonly StringFormat Typographic = StringFormat.GenericTypographic;

    private readonly List<EpsilonModuleModel> modules;
    private readonly AppConfig config;
    private readonly Action commit;
    private readonly Action closeRequested;
    private readonly List<DropdownPanel> panels = new List<DropdownPanel>();
    private readonly Dictionary<EpsilonCategory, List<EpsilonModuleModel>> modulesByCategory = new Dictionary<EpsilonCategory, List<EpsilonModuleModel>>();
    private readonly Dictionary<string, float> expandProgress = new Dictionary<string, float>(StringComparer.Ordinal);
    private readonly Dictionary<object, float> toggleProgress = new Dictionary<object, float>();
    private readonly Dictionary<(int Pixel, FontStyle Style), Font> fontCache = new Dictionary<(int, FontStyle), Font>();
    private readonly Timer animationTimer = new Timer { Interval = 15 };
    private DropdownPanel? draggingPanel;
    private PointF dragOffset;
    private EpsilonSliderSettingModel? draggingSlider;
    private RectangleF draggingSliderBounds;
    private float animatedUiScale;
    private Point lastMouseLocation;
    private bool hasMouse;

    internal EpsilonDropdownControl(List<EpsilonModuleModel> modules, AppConfig config, Action commit, Action closeRequested)
    {
        this.modules = modules;
        this.config = config;
        this.commit = commit;
        this.closeRequested = closeRequested;
        config.DropdownPanels ??= new Dictionary<string, DropdownPanelLayoutState>();
        config.DropdownModuleExpanded ??= new Dictionary<string, bool>();
        foreach (EpsilonModuleModel module in modules)
        {
            if (!modulesByCategory.TryGetValue(module.Category, out List<EpsilonModuleModel>? list))
            {
                list = new List<EpsilonModuleModel>();
                modulesByCategory[module.Category] = list;
            }
            list.Add(module);
            expandProgress[GetModuleKey(module)] = IsModuleExpanded(module) ? 1f : 0f;
            toggleProgress[module] = module.EnabledGetter() ? 1f : 0f;
            foreach (EpsilonBoolSettingModel setting in module.Settings.OfType<EpsilonBoolSettingModel>())
                toggleProgress[setting] = setting.Getter() ? 1f : 0f;
        }
        animatedUiScale = EpsilonTheme.Scale(config);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        TabStop = true;
        BackColor = Color.Transparent;
        BuildPanels();
        animationTimer.Tick += Animate;
        animationTimer.Start();
    }

    internal bool HandleKeyDown(Keys keyCode)
    {
        return false;
    }

    internal bool HandleKeyPress(char keyChar)
    {
        return false;
    }

    internal void SaveLayout()
    {
        foreach (DropdownPanel panel in panels)
        {
            DropdownPanelLayoutState state = GetOrCreateState(panel);
            state.X = panel.X;
            state.Y = panel.Y;
            state.Opened = panel.Opened;
            state.Visible = panel.Visible;
        }
    }

    private void BuildPanels()
    {
        AddPanel("main", "TESTING HOR", "T", null, 0);
        AddPanel("combat", "战斗", "战", EpsilonCategory.Combat, 1);
        AddPanel("player", "玩家", "玩", EpsilonCategory.Player, 2);
        AddPanel("movement", "移动", "移", EpsilonCategory.Movement, 3);
        AddPanel("render", "渲染", "渲", EpsilonCategory.Render, 4);
        EnsureLayoutCompatible();
    }

    private void AddPanel(string id, string title, string icon, EpsilonCategory? category, int index)
    {
        float defaultX = MarginX + index * (PanelWidth + PanelGap);
        DropdownPanel panel = new DropdownPanel(id, title, icon, category, defaultX, MarginY);
        bool hasSavedPosition = config.DropdownPanels.ContainsKey(id);
        DropdownPanelLayoutState state = GetOrCreateState(panel);
        panel.X = hasSavedPosition ? state.X : defaultX;
        panel.Y = hasSavedPosition ? state.Y : MarginY;
        panel.Opened = state.Opened;
        panel.Visible = id == "main" || state.Visible;
        panels.Add(panel);
    }

    /// <summary>旧版卡片更窄；检测到横向重叠时一次性重排，避免升级后卡片叠在一起。</summary>
    private void EnsureLayoutCompatible()
    {
        List<DropdownPanel> ordered = panels.OrderBy(panel => panel.X).ToList();
        bool overlap = false;
        for (int i = 1; i < ordered.Count; i++)
        {
            DropdownPanel previous = ordered[i - 1];
            DropdownPanel current = ordered[i];
            bool sameRow = Math.Abs(current.Y - previous.Y) < HeaderHeight;
            if (sameRow && current.X < previous.X + PanelWidth + 2f)
            {
                overlap = true;
                break;
            }
        }
        if (!overlap)
            return;
        for (int i = 0; i < panels.Count; i++)
        {
            panels[i].X = MarginX + i * (PanelWidth + PanelGap);
            panels[i].Y = MarginY;
        }
    }

    private DropdownPanelLayoutState GetOrCreateState(DropdownPanel panel)
    {
        if (!config.DropdownPanels.TryGetValue(panel.Id, out DropdownPanelLayoutState? state) || state == null)
        {
            state = new DropdownPanelLayoutState
            {
                X = panel.X,
                Y = panel.Y,
                Opened = true,
                Visible = true
            };
            config.DropdownPanels[panel.Id] = state;
        }
        return state;
    }

    private void Animate(object? sender, EventArgs e)
    {
        bool changed = false;
        foreach (DropdownPanel panel in panels)
        {
            float nextScroll = panel.Scroll + (panel.TargetScroll - panel.Scroll) * 0.24f;
            if (Math.Abs(nextScroll - panel.Scroll) > 0.02f)
            {
                panel.Scroll = nextScroll;
                changed = true;
            }
            else if (panel.Scroll != panel.TargetScroll)
            {
                panel.Scroll = panel.TargetScroll;
                changed = true;
            }
        }
        foreach (EpsilonModuleModel module in modules)
        {
            string key = GetModuleKey(module);
            float target = IsModuleExpanded(module) ? 1f : 0f;
            float current = expandProgress.TryGetValue(key, out float value) ? value : target;
            float next = current + (target - current) * ExpandSpeed;
            if (Math.Abs(next - current) > 0.004f)
            {
                expandProgress[key] = next;
                changed = true;
            }
            else if (current != target)
            {
                expandProgress[key] = target;
                changed = true;
            }
            changed |= AnimateToggle(module, module.EnabledGetter());
            foreach (EpsilonBoolSettingModel setting in module.Settings.OfType<EpsilonBoolSettingModel>())
                changed |= AnimateToggle(setting, setting.Getter());
        }
        float scaleTarget = EpsilonTheme.Scale(config);
        float nextScale = animatedUiScale + (scaleTarget - animatedUiScale) * 0.18f;
        if (Math.Abs(nextScale - animatedUiScale) > 0.002f)
        {
            animatedUiScale = nextScale;
            changed = true;
        }
        else if (animatedUiScale != scaleTarget)
        {
            animatedUiScale = scaleTarget;
            changed = true;
        }
        if (changed)
            Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = config.AntiAliasingEnabled ? SmoothingMode.AntiAlias : SmoothingMode.None;
        e.Graphics.PixelOffsetMode = config.AntiAliasingEnabled ? PixelOffsetMode.HighQuality : PixelOffsetMode.None;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        e.Graphics.ScaleTransform(animatedUiScale, animatedUiScale);
        foreach (DropdownPanel panel in panels)
        {
            if (panel.Visible)
                DrawPanel(e.Graphics, panel);
        }
    }

    private void DrawPanel(Graphics graphics, DropdownPanel panel)
    {
        float contentHeight = GetContentHeight(panel);
        float visibleHeight = GetVisibleContentHeight(panel, contentHeight);
        panel.MaxScroll = Math.Max(0f, contentHeight - visibleHeight);
        panel.TargetScroll = Math.Clamp(panel.TargetScroll, 0f, panel.MaxScroll);
        panel.Scroll = Math.Clamp(panel.Scroll, 0f, panel.MaxScroll);
        float totalHeight = HeaderHeight + (panel.Opened ? visibleHeight + BottomPadding : 0f);
        RectangleF bounds = new RectangleF(panel.X, panel.Y, PanelWidth, totalHeight);

        // 外沿：用不透明混合色代替半透明阴影，避免 TransparencyKey 窗口产生黑边。
        using (GraphicsPath rim = RoundedRect(new RectangleF(bounds.X - 1f, bounds.Y - 1f, bounds.Width + 2f, bounds.Height + 2f), PanelRadius + 1f))
        using (SolidBrush rimBrush = new SolidBrush(CardRim))
            graphics.FillPath(rimBrush, rim);
        using (GraphicsPath body = RoundedRect(bounds, PanelRadius))
        using (SolidBrush bodyBrush = new SolidBrush(CardBody))
            graphics.FillPath(bodyBrush, body);
        using (Pen highlight = new Pen(CardHighlight, 1f))
            graphics.DrawLine(highlight, bounds.X + PanelRadius, bounds.Y + 1f, bounds.Right - PanelRadius, bounds.Y + 1f);

        using (GraphicsPath headerPath = RoundedRect(new RectangleF(bounds.X, bounds.Y, bounds.Width, HeaderHeight), PanelRadius))
        using (SolidBrush headerBrush = new SolidBrush(EpsilonTheme.Mix(CardBody, EpsilonTheme.Background(config), 0.10f)))
            graphics.FillPath(headerBrush, headerPath);

        DrawHeader(graphics, panel, bounds);
        using (Pen divider = new Pen(Divider, 1f))
            graphics.DrawLine(divider, bounds.X + 1f, bounds.Y + HeaderHeight - 0.5f, bounds.Right - 1f, bounds.Y + HeaderHeight - 0.5f);

        if (!panel.Opened || visibleHeight <= 0f)
            return;

        RectangleF clip = new RectangleF(panel.X, panel.Y + HeaderHeight, PanelWidth, visibleHeight);
        GraphicsState state = graphics.Save();
        graphics.SetClip(clip);
        if (panel.Category == null)
            DrawMainContent(graphics, panel);
        else
            DrawCategoryContent(graphics, panel);
        graphics.Restore(state);
        DrawScrollbar(graphics, panel, contentHeight, visibleHeight);
    }

    private void DrawHeader(Graphics graphics, DropdownPanel panel, RectangleF bounds)
    {
        RectangleF chip = new RectangleF(bounds.X + 10f, bounds.Y + 9f, 20f, 20f);
        using (GraphicsPath chipPath = RoundedRect(chip, 6f))
        using (SolidBrush chipBrush = new SolidBrush(EpsilonTheme.Mix(CardBody, Primary, 0.26f)))
            graphics.FillPath(chipBrush, chipPath);
        DrawText(graphics, panel.Icon, F(11, FontStyle.Bold), Primary, chip.X + 6f, chip.Y + 4f);

        if (panel.Category == null)
        {
            DrawText(graphics, panel.Title, F(11, FontStyle.Bold), TextPrimary, bounds.X + 37f, bounds.Y + 8f);
            DrawText(graphics, "DESKTOP PET FEATURES", F(7, FontStyle.Regular), TextMuted, bounds.X + 37f, bounds.Y + 22f);
        }
        else
        {
            DrawText(graphics, panel.Title, F(11, FontStyle.Bold), TextPrimary, bounds.X + 37f, bounds.Y + 8f);
            int count = GetPanelModules(panel).Count;
            int enabled = GetPanelModules(panel).Count(module => module.EnabledGetter());
            DrawText(graphics, $"{enabled}/{count} 已启用", F(7, FontStyle.Regular), TextMuted, bounds.X + 37f, bounds.Y + 22f);
        }
        DrawChevron(graphics, bounds.Right - 15f, bounds.Y + HeaderHeight / 2f, panel.Opened);
    }

    private void DrawMainContent(Graphics graphics, DropdownPanel panel)
    {
        float y = panel.Y + HeaderHeight - panel.Scroll + 4f;
        foreach (DropdownPanel categoryPanel in GetCategoryPanels())
        {
            DrawCategoryRow(graphics, panel, categoryPanel, y);
            y += ModuleHeight;
        }
        DrawText(graphics, "左键显示/隐藏 · 右键折叠卡片 · 拖动标题移动", F(7, FontStyle.Regular), TextMuted, panel.X + 10f, y + 6f);
    }

    private void DrawCategoryRow(Graphics graphics, DropdownPanel panel, DropdownPanel categoryPanel, float y)
    {
        RectangleF row = new RectangleF(panel.X + 6f, y, PanelWidth - 12f, ModuleHeight - 2f);
        bool hovered = IsHovered(row);
        using GraphicsPath path = RoundedRect(row, 7f);
        using SolidBrush background = new SolidBrush(categoryPanel.Visible ? RowEnabled : hovered ? RowHover : RowNormal);
        graphics.FillPath(background, path);
        if (categoryPanel.Visible)
        {
            using SolidBrush accent = new SolidBrush(Primary);
            graphics.FillRectangle(accent, row.X + 1f, row.Y + 5f, 2.5f, row.Height - 10f);
        }
        RectangleF chip = new RectangleF(row.X + 7f, row.Y + 3f, 16f, 16f);
        using (GraphicsPath chipPath = RoundedRect(chip, 5f))
        using (SolidBrush chipBrush = new SolidBrush(EpsilonTheme.Mix(categoryPanel.Visible ? RowEnabled : RowNormal, Primary, 0.22f)))
            graphics.FillPath(chipBrush, chipPath);
        DrawText(graphics, categoryPanel.Icon, F(8, FontStyle.Bold), categoryPanel.Visible ? Primary : TextMuted, chip.X + 4f, chip.Y + 3f);
        DrawText(graphics, categoryPanel.Title, F(8, FontStyle.Regular), categoryPanel.Visible ? TextPrimary : TextSecondary, row.X + 29f, row.Y + 5f);
        int count = GetPanelModules(categoryPanel).Count;
        string countText = count + " 项";
        SizeF size = Measure(graphics, countText, F(7));
        DrawText(graphics, countText, F(7), TextMuted, row.Right - size.Width - 18f, row.Y + 6f);
        DrawVisibilityDot(graphics, row.Right - 10f, row.Y + row.Height / 2f, categoryPanel.Visible);
    }

    private void DrawCategoryContent(Graphics graphics, DropdownPanel panel)
    {
        float y = panel.Y + HeaderHeight - panel.Scroll + 3f;
        foreach (EpsilonModuleModel module in GetPanelModules(panel))
        {
            DrawModuleRow(graphics, panel, module, y);
            y += ModuleHeight;
            if (module.Settings.Count == 0)
                continue;
            float expand = GetExpand(module);
            float settingsHeight = expand * (2f + module.Settings.Count * (SettingHeight + SettingGap));
            if (settingsHeight > 0.6f)
                DrawSettingsWell(graphics, panel, module, y, settingsHeight);
            y += settingsHeight;
        }
    }

    private void DrawModuleRow(Graphics graphics, DropdownPanel panel, EpsilonModuleModel module, float y)
    {
        RectangleF row = new RectangleF(panel.X + 6f, y, PanelWidth - 12f, ModuleHeight - 2f);
        bool enabled = module.EnabledGetter();
        float progress = GetToggleProgress(module, enabled);
        float eased = SmoothStep(progress);
        bool hovered = IsHovered(row);
        using GraphicsPath path = RoundedRect(row, 7f);
        Color offBackground = hovered ? RowHover : RowNormal;
        using SolidBrush background = new SolidBrush(EpsilonTheme.Mix(offBackground, RowEnabled, eased));
        graphics.FillPath(background, path);
        if (progress > 0.01f)
        {
            using SolidBrush accent = new SolidBrush(Color.FromArgb((int)Math.Round(255f * progress), Primary));
            float accentHeight = (row.Height - 10f) * (0.35f + 0.65f * eased);
            graphics.FillRectangle(accent, row.X + 1f, row.Y + (row.Height - accentHeight) / 2f, 2.5f, accentHeight);
        }
        DrawText(graphics, module.Name, F(8, enabled ? FontStyle.Bold : FontStyle.Regular), EpsilonTheme.Mix(TextSecondary, TextPrimary, eased), row.X + 10f, row.Y + 6f);
        if (module.Settings.Count > 0)
            DrawSmallChevron(graphics, row.Right - 30f, row.Y + row.Height / 2f, IsModuleExpanded(module));
        DrawToggle(graphics, new RectangleF(row.Right - 24f, row.Y + (row.Height - 12f) / 2f, 20f, 12f), progress);
    }

    private void DrawSettingsWell(Graphics graphics, DropdownPanel panel, EpsilonModuleModel module, float y, float height)
    {
        RectangleF well = new RectangleF(panel.X + 10f, y, PanelWidth - 20f, height);
        using (GraphicsPath wellPath = RoundedRect(well, 8f))
        using (SolidBrush wellBrush = new SolidBrush(WellColor))
            graphics.FillPath(wellBrush, wellPath);

        GraphicsState state = graphics.Save();
        graphics.SetClip(well);
        float rowY = y + 2f;
        foreach (EpsilonSettingModel setting in module.Settings)
        {
            RectangleF row = new RectangleF(well.X + 4f, rowY, well.Width - 8f, SettingHeight);
            DrawSetting(graphics, setting, row);
            rowY += SettingHeight + SettingGap;
        }
        graphics.Restore(state);
    }

    private void DrawSetting(Graphics graphics, EpsilonSettingModel setting, RectangleF row)
    {
        using GraphicsPath path = RoundedRect(row, 6f);
        using SolidBrush surface = new SolidBrush(WellRow);
        graphics.FillPath(surface, path);

        if (setting is EpsilonBoolSettingModel boolSetting)
        {
            DrawText(graphics, setting.Name, F(7, FontStyle.Regular), TextPrimary, row.X + 6f, row.Y + 7f);
            DrawToggle(graphics, new RectangleF(row.Right - 22f, row.Y + (row.Height - 11f) / 2f, 18f, 11f), GetToggleProgress(boolSetting, boolSetting.Getter()));
            return;
        }
        if (setting is EpsilonSliderSettingModel slider)
        {
            double value = slider.Getter();
            DrawText(graphics, setting.Name, F(7, FontStyle.Regular), TextPrimary, row.X + 6f, row.Y + 3f);
            string valueText = FormatValue(value, slider.Step);
            SizeF size = Measure(graphics, valueText, F(7, FontStyle.Bold));
            float pillWidth = size.Width + 8f;
            RectangleF pill = new RectangleF(row.Right - pillWidth - 4f, row.Y + 3f, pillWidth, 10f);
            using (GraphicsPath pillPath = RoundedRect(pill, 5f))
            using (SolidBrush pillBrush = new SolidBrush(EpsilonTheme.Mix(WellRow, Primary, 0.30f)))
                graphics.FillPath(pillBrush, pillPath);
            DrawText(graphics, valueText, F(7, FontStyle.Bold), TextPrimary, pill.X + 4f, pill.Y + 1f);
            RectangleF bar = GetSliderBar(row);
            double t = (value - slider.Minimum) / Math.Max(0.0001d, slider.Maximum - slider.Minimum);
            using (Pen track = new Pen(EpsilonTheme.Mix(WellRow, TextMuted, 0.55f), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                graphics.DrawLine(track, bar.Left, bar.Top, bar.Right, bar.Top);
            float knobX = bar.Left + bar.Width * (float)Math.Clamp(t, 0d, 1d);
            using (Pen active = new Pen(Primary, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                graphics.DrawLine(active, bar.Left, bar.Top, knobX, bar.Top);
            using (SolidBrush knob = new SolidBrush(Primary))
                graphics.FillEllipse(knob, knobX - 3.5f, bar.Top - 3.5f, 7f, 7f);
            return;
        }
        if (setting is EpsilonActionSettingModel action)
        {
            DrawText(graphics, setting.Name, F(7, FontStyle.Regular), TextPrimary, row.X + 6f, row.Y + 7f);
            string value = action.ValueGetter();
            Font valueFont = F(7, FontStyle.Bold);
            SizeF size = Measure(graphics, value, valueFont);
            float width = Math.Min(60f, size.Width + 12f);
            RectangleF button = new RectangleF(row.Right - width - 4f, row.Y + 5f, width, 13f);
            using (GraphicsPath buttonPath = RoundedRect(button, 6f))
            using (SolidBrush buttonBrush = new SolidBrush(EpsilonTheme.Mix(WellRow, Primary, 0.34f)))
                graphics.FillPath(buttonBrush, buttonPath);
            if (action.ColorGetter != null)
            {
                using SolidBrush colorBrush = new SolidBrush(action.ColorGetter());
                graphics.FillEllipse(colorBrush, button.X + 3f, button.Y + 3f, 7f, 7f);
            }
            float textX = action.ColorGetter == null ? button.X + (button.Width - size.Width) / 2f : button.X + 12f;
            DrawText(graphics, value, valueFont, TextPrimary, textX, button.Y + 2f);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        float scale = animatedUiScale;
        PointF point = new PointF(e.X / scale, e.Y / scale);
        for (int i = panels.Count - 1; i >= 0; i--)
        {
            DropdownPanel panel = panels[i];
            if (!panel.Visible || !GetPanelBounds(panel).Contains(point))
                continue;
            if (i != panels.Count - 1)
            {
                panels.RemoveAt(i);
                panels.Add(panel);
            }
            if (GetHeaderBounds(panel).Contains(point))
            {
                if (e.Button == MouseButtons.Left)
                {
                    draggingPanel = panel;
                    dragOffset = new PointF(panel.X - point.X, panel.Y - point.Y);
                    Capture = true;
                }
                else if (e.Button == MouseButtons.Right)
                {
                    panel.Opened = !panel.Opened;
                    if (!panel.Opened)
                        panel.Scroll = panel.TargetScroll = 0f;
                    SavePanelState(panel);
                }
                Invalidate();
                return;
            }
            if (panel.Opened && GetContentBounds(panel).Contains(point))
            {
                if (panel.Category == null)
                    HandleMainClick(panel, point, e.Button);
                else
                    HandleCategoryClick(panel, point, e.Button);
                return;
            }
            return;
        }
        if (e.Button == MouseButtons.Left)
            closeRequested();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        lastMouseLocation = e.Location;
        hasMouse = true;
        float scale = animatedUiScale;
        PointF point = new PointF(e.X / scale, e.Y / scale);
        if (draggingPanel != null && e.Button == MouseButtons.Left)
        {
            draggingPanel.X = point.X + dragOffset.X;
            draggingPanel.Y = point.Y + dragOffset.Y;
            Invalidate();
            return;
        }
        if (draggingSlider != null && e.Button == MouseButtons.Left)
        {
            UpdateSlider(draggingSlider, draggingSliderBounds, point.X);
            return;
        }
        Invalidate();
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hasMouse = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    private bool IsHovered(RectangleF bounds)
    {
        if (!hasMouse)
            return false;
        float scale = animatedUiScale;
        PointF point = new PointF(lastMouseLocation.X / scale, lastMouseLocation.Y / scale);
        return bounds.Contains(point);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && draggingPanel != null)
        {
            ClampPanel(draggingPanel);
            SavePanelState(draggingPanel);
            draggingPanel = null;
            Capture = false;
        }
        if (draggingSlider != null)
        {
            draggingSlider = null;
            Capture = false;
        }
        base.OnMouseUp(e);
    }

    private void ClampPanel(DropdownPanel panel)
    {
        float logicalWidth = Width / Math.Max(0.1f, animatedUiScale);
        float logicalHeight = Height / Math.Max(0.1f, animatedUiScale);
        panel.X = Math.Clamp(panel.X, -PanelWidth + 48f, Math.Max(0f, logicalWidth - 48f));
        panel.Y = Math.Clamp(panel.Y, 0f, Math.Max(0f, logicalHeight - HeaderHeight));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        float scale = animatedUiScale;
        PointF point = new PointF(e.X / scale, e.Y / scale);
        foreach (DropdownPanel panel in panels.AsEnumerable().Reverse())
        {
            if (!panel.Visible || !panel.Opened || !GetPanelBounds(panel).Contains(point) || panel.MaxScroll <= 0f)
                continue;
            panel.TargetScroll = Math.Clamp(panel.TargetScroll - Math.Sign(e.Delta) * ScrollSpeed, 0f, panel.MaxScroll);
            Invalidate();
            return;
        }
        base.OnMouseWheel(e);
    }

    private void HandleMainClick(DropdownPanel panel, PointF point, MouseButtons button)
    {
        float y = panel.Y + HeaderHeight - panel.Scroll + 4f;
        foreach (DropdownPanel categoryPanel in GetCategoryPanels())
        {
            RectangleF row = new RectangleF(panel.X + 6f, y, PanelWidth - 12f, ModuleHeight - 2f);
            if (row.Contains(point) && button == MouseButtons.Left)
            {
                categoryPanel.Visible = !categoryPanel.Visible;
                SavePanelState(categoryPanel);
                Invalidate();
                return;
            }
            y += ModuleHeight;
        }
    }

    private void HandleCategoryClick(DropdownPanel panel, PointF point, MouseButtons button)
    {
        float y = panel.Y + HeaderHeight - panel.Scroll + 3f;
        foreach (EpsilonModuleModel module in GetPanelModules(panel))
        {
            RectangleF header = new RectangleF(panel.X + 6f, y, PanelWidth - 12f, ModuleHeight - 2f);
            if (header.Contains(point))
            {
                if (button == MouseButtons.Left)
                {
                    module.EnabledSetter(!module.EnabledGetter());
                    commit();
                }
                else if (button == MouseButtons.Right && module.Settings.Count > 0)
                {
                    bool expanded = !IsModuleExpanded(module);
                    config.DropdownModuleExpanded[GetModuleKey(module)] = expanded;
                    config.Save();
                }
                Invalidate();
                return;
            }
            y += ModuleHeight;
            if (module.Settings.Count == 0)
                continue;
            float settingsHeight = GetExpand(module) * (2f + module.Settings.Count * (SettingHeight + SettingGap));
            if (settingsHeight <= 0.6f)
            {
                y += settingsHeight;
                continue;
            }
            float rowY = y + 2f;
            foreach (EpsilonSettingModel setting in module.Settings)
            {
                RectangleF row = new RectangleF(panel.X + 14f, rowY, PanelWidth - 28f, SettingHeight);
                if (row.Contains(point) && point.Y <= y + settingsHeight
                    && (button == MouseButtons.Left || button == MouseButtons.Right))
                {
                    if (button == MouseButtons.Left && setting is EpsilonBoolSettingModel boolSetting)
                    {
                        boolSetting.Setter(!boolSetting.Getter());
                        commit();
                    }
                    else if (button == MouseButtons.Left && setting is EpsilonSliderSettingModel slider)
                    {
                        draggingSlider = slider;
                        draggingSliderBounds = GetSliderBar(row);
                        Capture = true;
                        UpdateSlider(slider, draggingSliderBounds, point.X);
                    }
                    else if (setting is EpsilonActionSettingModel action)
                    {
                        if (button == MouseButtons.Right)
                            action.SecondaryAction?.Invoke();
                        else
                            action.Action();
                    }
                    Invalidate();
                    return;
                }
                rowY += SettingHeight + SettingGap;
            }
            y += settingsHeight;
        }
    }

    private void UpdateSlider(EpsilonSliderSettingModel slider, RectangleF bar, float x)
    {
        double t = Math.Clamp((x - bar.Left) / Math.Max(1f, bar.Width), 0d, 1d);
        double value = slider.Minimum + (slider.Maximum - slider.Minimum) * t;
        if (slider.Step > 0d)
            value = Math.Round(value / slider.Step) * slider.Step;
        slider.Setter(Math.Clamp(value, slider.Minimum, slider.Maximum));
        commit();
        Invalidate();
    }

    private float GetContentHeight(DropdownPanel panel)
    {
        if (panel.Category == null)
            return GetCategoryPanels().Count * ModuleHeight + 22f;
        float height = 0f;
        foreach (EpsilonModuleModel module in GetPanelModules(panel))
        {
            height += ModuleHeight;
            if (module.Settings.Count > 0)
                height += GetExpand(module) * (2f + module.Settings.Count * (SettingHeight + SettingGap));
        }
        return height;
    }

    private float GetVisibleContentHeight(DropdownPanel panel, float contentHeight)
    {
        float logicalHeight = Height / animatedUiScale;
        float maxHeight = Math.Max(60f, logicalHeight - panel.Y - HeaderHeight - MarginY - BottomPadding);
        return Math.Min(contentHeight, maxHeight);
    }

    private RectangleF GetPanelBounds(DropdownPanel panel)
    {
        float contentHeight = GetContentHeight(panel);
        float visibleHeight = GetVisibleContentHeight(panel, contentHeight);
        return new RectangleF(panel.X, panel.Y, PanelWidth, HeaderHeight + (panel.Opened ? visibleHeight + BottomPadding : 0f));
    }

    private static RectangleF GetHeaderBounds(DropdownPanel panel)
    {
        return new RectangleF(panel.X, panel.Y, PanelWidth, HeaderHeight);
    }

    private RectangleF GetContentBounds(DropdownPanel panel)
    {
        float visibleHeight = GetVisibleContentHeight(panel, GetContentHeight(panel));
        return new RectangleF(panel.X, panel.Y + HeaderHeight, PanelWidth, visibleHeight);
    }

    private List<EpsilonModuleModel> GetPanelModules(DropdownPanel panel)
    {
        if (panel.Category is EpsilonCategory category && modulesByCategory.TryGetValue(category, out List<EpsilonModuleModel>? list))
            return list;
        return EmptyModules;
    }

    private static readonly List<EpsilonModuleModel> EmptyModules = new List<EpsilonModuleModel>();

    private List<DropdownPanel> GetCategoryPanels()
    {
        return panels.Where(panel => panel.Category != null).OrderBy(panel => panel.Category).ToList();
    }

    private bool IsModuleExpanded(EpsilonModuleModel module)
    {
        return config.DropdownModuleExpanded.TryGetValue(GetModuleKey(module), out bool expanded) && expanded;
    }

    private float GetExpand(EpsilonModuleModel module)
    {
        return expandProgress.TryGetValue(GetModuleKey(module), out float value) ? Math.Clamp(value, 0f, 1f) : 0f;
    }

    private static string GetModuleKey(EpsilonModuleModel module)
    {
        return module.Category.ToString().ToLowerInvariant() + ":" + module.Name.ToLowerInvariant().Replace(" ", "_");
    }

    private void SavePanelState(DropdownPanel panel)
    {
        DropdownPanelLayoutState state = GetOrCreateState(panel);
        state.X = panel.X;
        state.Y = panel.Y;
        state.Opened = panel.Opened;
        state.Visible = panel.Visible;
        config.Save();
    }

    private static RectangleF GetSliderBar(RectangleF row)
    {
        return new RectangleF(row.X + 6f, row.Bottom - 5f, row.Width - 12f, 1f);
    }

    private static string FormatValue(double value, double step)
    {
        if (step >= 1d)
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("0.0#", CultureInfo.InvariantCulture);
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

    private static float SmoothStep(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value * value * (3f - 2f * value);
    }

    private void DrawToggle(Graphics graphics, RectangleF bounds, float progress)
    {
        float eased = SmoothStep(progress);
        using (GraphicsPath path = RoundedRect(bounds, bounds.Height / 2f))
        using (SolidBrush surface = new SolidBrush(EpsilonTheme.Mix(EpsilonTheme.Mix(CardBody, TextMuted, 0.45f), Primary, eased)))
            graphics.FillPath(surface, path);
        float baseDiameter = bounds.Height - 4f;
        float diameter = baseDiameter + (float)Math.Sin(Math.PI * progress) * 1f;
        float centerX = bounds.Left + 2f + baseDiameter / 2f + (bounds.Width - baseDiameter - 4f) * eased;
        float centerY = bounds.Top + bounds.Height / 2f;
        using (SolidBrush knob = new SolidBrush(EpsilonTheme.Mix(TextMuted, EpsilonTheme.OnAccent(config), eased)))
            graphics.FillEllipse(knob, centerX - diameter / 2f, centerY - diameter / 2f, diameter, diameter);
    }

    private void DrawVisibilityDot(Graphics graphics, float x, float y, bool visible)
    {
        if (visible)
        {
            using SolidBrush brush = new SolidBrush(Primary);
            graphics.FillEllipse(brush, x - 3f, y - 3f, 6f, 6f);
            return;
        }
        using Pen ring = new Pen(EpsilonTheme.Mix(CardBody, TextMuted, 0.65f), 1f);
        graphics.DrawEllipse(ring, x - 3f, y - 3f, 6f, 6f);
    }

    private void DrawChevron(Graphics graphics, float x, float y, bool opened)
    {
        using Pen pen = new Pen(TextMuted, 1.3f);
        if (opened)
        {
            graphics.DrawLine(pen, x - 3f, y - 2f, x, y + 1.5f);
            graphics.DrawLine(pen, x, y + 1.5f, x + 3f, y - 2f);
        }
        else
        {
            graphics.DrawLine(pen, x - 1.5f, y - 3f, x + 1.5f, y);
            graphics.DrawLine(pen, x + 1.5f, y, x - 1.5f, y + 3f);
        }
    }

    private void DrawSmallChevron(Graphics graphics, float x, float y, bool opened)
    {
        using Pen pen = new Pen(opened ? Primary : TextMuted, 1.2f);
        if (opened)
        {
            graphics.DrawLine(pen, x - 3f, y - 1f, x, y + 2f);
            graphics.DrawLine(pen, x, y + 2f, x + 3f, y - 1f);
        }
        else
        {
            graphics.DrawLine(pen, x - 1f, y - 3f, x + 2f, y);
            graphics.DrawLine(pen, x + 2f, y, x - 1f, y + 3f);
        }
    }

    private void DrawScrollbar(Graphics graphics, DropdownPanel panel, float contentHeight, float visibleHeight)
    {
        if (contentHeight <= visibleHeight || panel.MaxScroll <= 0f)
            return;
        float trackHeight = visibleHeight - 6f;
        float thumbHeight = Math.Max(18f, trackHeight * visibleHeight / contentHeight);
        float thumbY = panel.Y + HeaderHeight + 3f + (trackHeight - thumbHeight) * panel.Scroll / panel.MaxScroll;
        using SolidBrush brush = new SolidBrush(EpsilonTheme.Mix(CardBody, TextMuted, 0.55f));
        using GraphicsPath path = RoundedRect(new RectangleF(panel.X + PanelWidth - 4f, thumbY, 2f, thumbHeight), 1f);
        graphics.FillPath(brush, path);
    }

    private Font F(int pixel, FontStyle style = FontStyle.Regular)
    {
        (int, FontStyle) key = (pixel, style);
        if (!fontCache.TryGetValue(key, out Font? font))
        {
            font = new Font("Microsoft YaHei UI", pixel, style, GraphicsUnit.Pixel);
            fontCache[key] = font;
        }
        return font;
    }

    private static void DrawText(Graphics graphics, string text, Font font, Color color, float x, float y)
    {
        using SolidBrush brush = new SolidBrush(color);
        graphics.DrawString(text, font, brush, x, y, Typographic);
    }

    private static SizeF Measure(Graphics graphics, string text, Font font)
    {
        return graphics.MeasureString(text, font, int.MaxValue, Typographic);
    }

    private static GraphicsPath RoundedRect(RectangleF rectangle, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float diameter = Math.Max(0f, Math.Min(radius * 2f, Math.Min(rectangle.Width, rectangle.Height)));
        if (diameter <= 0f)
        {
            path.AddRectangle(rectangle);
            return path;
        }
        RectangleF arc = new RectangleF(rectangle.X, rectangle.Y, diameter, diameter);
        path.AddArc(arc, 180f, 90f);
        arc.X = rectangle.Right - diameter;
        path.AddArc(arc, 270f, 90f);
        arc.Y = rectangle.Bottom - diameter;
        path.AddArc(arc, 0f, 90f);
        arc.X = rectangle.Left;
        path.AddArc(arc, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animationTimer.Dispose();
            foreach (Font font in fontCache.Values)
                font.Dispose();
            fontCache.Clear();
        }
        base.Dispose(disposing);
    }

    private sealed class DropdownPanel
    {
        internal string Id { get; }
        internal string Title { get; }
        internal string Icon { get; }
        internal EpsilonCategory? Category { get; }
        internal float X { get; set; }
        internal float Y { get; set; }
        internal bool Opened { get; set; } = true;
        internal bool Visible { get; set; } = true;
        internal float Scroll { get; set; }
        internal float TargetScroll { get; set; }
        internal float MaxScroll { get; set; }

        internal DropdownPanel(string id, string title, string icon, EpsilonCategory? category, float x, float y)
        {
            Id = id;
            Title = title;
            Icon = icon;
            Category = category;
            X = x;
            Y = y;
        }
    }
}
