using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace McPetOverlay;

internal sealed class EpsilonWorkbenchCoordinator : IDisposable
{
    private const int SnapDistance = 36;
    private readonly AppConfig config;
    private readonly Action commit;
    private readonly Action closeRequested;
    private readonly WorkbenchState state;
    private readonly List<WorkbenchToolForm> windows;
    private readonly Dictionary<WorkbenchToolForm, Rectangle> animationTargets = new Dictionary<WorkbenchToolForm, Rectangle>();
    private readonly Dictionary<WorkbenchToolForm, WindowMotion> windowMotions = new Dictionary<WorkbenchToolForm, WindowMotion>();
    private readonly Timer animationTimer = new Timer { Interval = 15 };
    private readonly WorkbenchToolForm logWindow;
    private Rectangle gameBounds;
    private bool arranging;
    private bool disposed;

    internal EpsilonWorkbenchCoordinator(
        List<EpsilonModuleModel> modules,
        AppConfig config,
        Action commit,
        Action closeRequested,
        Func<Keys, bool> keyDown,
        Func<char, bool> keyPress)
    {
        this.config = config;
        this.commit = commit;
        this.closeRequested = closeRequested;
        state = new WorkbenchState(modules);
        windows = new List<WorkbenchToolForm>();
        logWindow = CreateWindow("logs", "LIVE LOG", new WorkbenchLogSurface(state, config), keyDown, keyPress);
        windows.Add(CreateWindow("command", "ARC WORKSPACE", new WorkbenchCommandSurface(state, config, ToggleLogWindow, () => logWindow.Visible, ResetLayout, closeRequested), keyDown, keyPress));
        windows.Add(CreateWindow("categories", "ACTIVITY", new WorkbenchCategorySurface(state, config), keyDown, keyPress));
        windows.Add(CreateWindow("modules", "MODULE EXPLORER", new WorkbenchModuleSurface(state, config, commit), keyDown, keyPress));
        windows.Add(CreateWindow("inspector", "INSPECTOR", new WorkbenchInspectorSurface(state, config, commit), keyDown, keyPress));
        windows.Add(logWindow);
        state.Changed += delegate
        {
            animationTimer.Start();
            Invalidate();
        };
        animationTimer.Tick += delegate
        {
            bool active = false;
            foreach (WorkbenchToolForm window in windows)
            {
                active |= window.AdvanceAnimations();
                active |= AdvanceWindowMotion(window);
            }
            if (active)
                Invalidate();
            else
                animationTimer.Stop();
        };
    }

    internal bool Visible => windows.Any(window => window.Visible);

    internal IWin32Window? DialogOwner => windows.LastOrDefault(window => window.Visible && window.ContainsFocus)
        ?? windows.LastOrDefault(window => window.Visible);

    private WorkbenchToolForm CreateWindow(
        string id,
        string title,
        WorkbenchSurface surface,
        Func<Keys, bool> keyDown,
        Func<char, bool> keyPress)
    {
        WorkbenchToolForm window = new WorkbenchToolForm(id, title, surface, closeRequested, keyDown, keyPress);
        window.LocationChanged += OnWindowGeometryChanged;
        window.SizeChanged += OnWindowGeometryChanged;
        window.UserDragStarted += OnWindowDragStarted;
        window.UserDragCompleted += OnWindowDragCompleted;
        windowMotions[window] = new WindowMotion();
        return window;
    }

    internal void SetGameBounds(Rectangle bounds)
    {
        gameBounds = bounds;
        if (!Visible)
            return;
        arranging = true;
        try
        {
            foreach (WorkbenchToolForm window in windows)
                window.Bounds = ClampToGame(window.Bounds, window.MinimumSize);
        }
        finally
        {
            arranging = false;
        }
        CaptureAnimationTargets();
    }

    internal void ShowWorkspace(Rectangle bounds)
    {
        gameBounds = bounds;
        ApplySavedOrDefaultLayout();
        CaptureAnimationTargets();
        foreach (WorkbenchToolForm window in windows)
        {
            if (ReferenceEquals(window, logWindow) && !config.WorkbenchLogVisible)
            {
                window.Hide();
                continue;
            }
            if (!window.Visible)
                window.Show();
            window.Opacity = 0d;
        }
        animationTimer.Start();
        FocusPrimary();
    }

    internal void SetAnimationProgress(double progress)
    {
        progress = Math.Clamp(progress, 0d, 1d);
        arranging = true;
        try
        {
            for (int i = 0; i < windows.Count; i++)
            {
                WorkbenchToolForm window = windows[i];
                if (!window.Visible)
                    continue;
                if (!animationTargets.TryGetValue(window, out Rectangle target))
                    continue;
                double delay = i * 0.075d;
                double local = Math.Clamp((progress - delay) / Math.Max(0.01d, 1d - delay), 0d, 1d);
                double eased = EaseOutBack(local);
                Point travel = window.PanelId switch
                {
                    "command" => new Point(0, -48),
                    "categories" => new Point(-62, 18),
                    "modules" => new Point(-28, 54),
                    _ => new Point(64, 38)
                };
                double scale = 0.925d + 0.075d * eased;
                int width = Math.Max(window.MinimumSize.Width, (int)Math.Round(target.Width * scale));
                int height = Math.Max(window.MinimumSize.Height, (int)Math.Round(target.Height * scale));
                int x = target.X + (target.Width - width) / 2 + (int)Math.Round(travel.X * (1d - eased));
                int y = target.Y + (target.Height - height) / 2 + (int)Math.Round(travel.Y * (1d - eased));
                window.Bounds = new Rectangle(x, y, width, height);
                window.Opacity = Math.Clamp((1d - Math.Pow(1d - local, 2.4d)) * 0.985d, 0d, 0.985d);
            }
        }
        finally
        {
            arranging = false;
        }
    }

    internal void HideImmediate()
    {
        animationTimer.Stop();
        foreach (WorkbenchToolForm window in windows)
        {
            window.Opacity = 0d;
            window.Hide();
        }
    }

    internal void FocusPrimary()
    {
        WorkbenchToolForm command = windows[0];
        if (command.Visible)
        {
            command.Activate();
            command.FocusSurface();
        }
    }

    internal bool HandleKeyDown(Keys keyCode)
    {
        if (keyCode == Keys.F && Control.ModifierKeys.HasFlag(Keys.Control))
        {
            state.SearchFocused = true;
            FocusPrimary();
            state.NotifyChanged();
            return true;
        }
        if (keyCode == Keys.OemQuestion || keyCode == Keys.Divide)
        {
            state.SearchFocused = true;
            FocusPrimary();
            state.NotifyChanged();
            return true;
        }
        if (state.SearchFocused)
        {
            if (keyCode == Keys.Escape)
            {
                state.SearchFocused = false;
                state.NotifyChanged();
                return true;
            }
            if (keyCode == Keys.Back)
            {
                if (state.SearchQuery.Length > 0)
                    state.SetSearch(state.SearchQuery[..^1]);
                return true;
            }
            if (keyCode == Keys.Delete)
            {
                state.SetSearch(string.Empty);
                return true;
            }
        }
        if (keyCode == Keys.Up)
        {
            state.MoveSelection(-1);
            return true;
        }
        if (keyCode == Keys.Down)
        {
            state.MoveSelection(1);
            return true;
        }
        if ((keyCode == Keys.Space || keyCode == Keys.Enter) && state.SelectedModule != null)
        {
            EpsilonModuleModel module = state.SelectedModule;
            module.EnabledSetter(!module.EnabledGetter());
            commit();
            state.NotifyChanged();
            return true;
        }
        return false;
    }

    internal bool HandleKeyPress(char keyChar)
    {
        if (!state.SearchFocused || char.IsControl(keyChar))
            return false;
        state.SetSearch(state.SearchQuery + keyChar);
        return true;
    }

    internal void SaveLayout()
    {
        if (gameBounds.Width <= 0 || gameBounds.Height <= 0)
            return;
        foreach (WorkbenchToolForm window in windows)
        {
            Rectangle bounds = animationTargets.TryGetValue(window, out Rectangle target) ? target : window.Bounds;
            config.WorkbenchPanels[window.PanelId] = new WorkbenchPanelLayoutState
            {
                X = (bounds.X - gameBounds.X) / (float)gameBounds.Width,
                Y = (bounds.Y - gameBounds.Y) / (float)gameBounds.Height,
                Width = bounds.Width / (float)gameBounds.Width,
                Height = bounds.Height / (float)gameBounds.Height
            };
        }
    }

    internal void Invalidate()
    {
        foreach (WorkbenchToolForm window in windows)
            window.InvalidateSurface();
    }

    private void ApplySavedOrDefaultLayout()
    {
        Dictionary<string, Rectangle> defaults = GetDefaultLayout();
        arranging = true;
        try
        {
            foreach (WorkbenchToolForm window in windows)
            {
                Rectangle bounds = defaults[window.PanelId];
                if (config.WorkbenchPanels.TryGetValue(window.PanelId, out WorkbenchPanelLayoutState? saved)
                    && saved.Width > 0.05f && saved.Height > 0.05f)
                {
                    bounds = new Rectangle(
                        gameBounds.X + (int)Math.Round(saved.X * gameBounds.Width),
                        gameBounds.Y + (int)Math.Round(saved.Y * gameBounds.Height),
                        (int)Math.Round(saved.Width * gameBounds.Width),
                        (int)Math.Round(saved.Height * gameBounds.Height));
                }
                window.Bounds = ClampToGame(bounds, window.MinimumSize);
            }
        }
        finally
        {
            arranging = false;
        }
    }

    private Dictionary<string, Rectangle> GetDefaultLayout()
    {
        float scale = Math.Clamp(config.UiScalePercent / 100f, 0.70f, 1.38f);
        int gap = Math.Max(6, (int)Math.Round(8 * scale));
        int railWidth = Math.Max(82, (int)Math.Round(94 * scale));
        int moduleWidth = Math.Max(280, (int)Math.Round(330 * scale));
        int inspectorWidth = Math.Max(360, (int)Math.Round(430 * scale));
        int commandHeight = Math.Clamp((int)Math.Round(92 * scale), 82, 116);
        int maximumWorkHeight = Math.Max(360, gameBounds.Height - commandHeight - gap * 5);
        int workHeight = Math.Min(maximumWorkHeight, Math.Max(440, (int)Math.Round(610 * scale)));
        int totalWidth = railWidth + moduleWidth + inspectorWidth + gap * 2;
        int maximumWidth = Math.Max(720, gameBounds.Width - gap * 4);
        if (totalWidth > maximumWidth)
        {
            double ratio = maximumWidth / (double)totalWidth;
            railWidth = Math.Max(74, (int)Math.Round(railWidth * ratio));
            moduleWidth = Math.Max(240, (int)Math.Round(moduleWidth * ratio));
            inspectorWidth = Math.Max(310, maximumWidth - railWidth - moduleWidth - gap * 2);
            totalWidth = railWidth + moduleWidth + inspectorWidth + gap * 2;
        }
        int x = gameBounds.X + Math.Max(gap * 2, (gameBounds.Width - totalWidth) / 2);
        int workspaceHeight = commandHeight + gap + workHeight;
        int y = gameBounds.Y + Math.Max(gap * 2, (gameBounds.Height - workspaceHeight) / 2);
        int bodyY = y + commandHeight + gap;
        return new Dictionary<string, Rectangle>(StringComparer.Ordinal)
        {
            ["command"] = new Rectangle(x + railWidth + gap, y, moduleWidth + inspectorWidth + gap, commandHeight),
            ["categories"] = new Rectangle(x, y, railWidth, workspaceHeight),
            ["modules"] = new Rectangle(x + railWidth + gap, bodyY, moduleWidth, workHeight),
            ["inspector"] = new Rectangle(x + railWidth + moduleWidth + gap * 2, bodyY, inspectorWidth, workHeight)
            , ["logs"] = new Rectangle(
                Math.Max(gameBounds.Left + gap, x + totalWidth - Math.Max(520, moduleWidth + 120)),
                Math.Max(gameBounds.Top + gap, bodyY + workHeight - Math.Max(210, (int)Math.Round(230 * scale))),
                Math.Min(Math.Max(520, moduleWidth + 120), gameBounds.Width - gap * 2),
                Math.Min(Math.Max(210, (int)Math.Round(230 * scale)), gameBounds.Height - gap * 2))
        };
    }

    private void ToggleLogWindow()
    {
        if (logWindow.Visible)
        {
            animationTargets[logWindow] = logWindow.Bounds;
            config.WorkbenchLogVisible = false;
            logWindow.Hide();
        }
        else
        {
            config.WorkbenchLogVisible = true;
            if (animationTargets.TryGetValue(logWindow, out Rectangle target))
                logWindow.Bounds = ClampToGame(target, logWindow.MinimumSize);
            logWindow.Opacity = 0.985d;
            logWindow.Show();
            logWindow.BringToFront();
            logWindow.FocusSurface();
        }
        commit();
        Invalidate();
    }

    private void ResetLayout()
    {
        config.WorkbenchPanels.Clear();
        ApplySavedOrDefaultLayout();
        CaptureAnimationTargets();
        SaveLayout();
        commit();
        Invalidate();
    }

    private void CaptureAnimationTargets()
    {
        animationTargets.Clear();
        foreach (WorkbenchToolForm window in windows)
            animationTargets[window] = window.Bounds;
    }

    private void OnWindowGeometryChanged(object? sender, EventArgs e)
    {
        if (arranging || sender is not WorkbenchToolForm window || !window.Visible)
            return;
        if (window.IsUserDragging)
        {
            Rectangle current = ClampToGame(window.Bounds, window.MinimumSize);
            Rectangle target = SnapBounds(window, current);
            window.SetDockPreview(current != target);
            return;
        }
        arranging = true;
        try
        {
            Rectangle clamped = ClampToGame(window.Bounds, window.MinimumSize);
            window.Bounds = clamped;
            animationTargets[window] = clamped;
        }
        finally
        {
            arranging = false;
        }
    }

    private void OnWindowDragStarted(object? sender, EventArgs e)
    {
        if (sender is not WorkbenchToolForm window)
            return;
        if (windowMotions.TryGetValue(window, out WindowMotion? motion))
            motion.Stop();
        window.BringToFront();
        window.SetDragVisual(true);
        animationTimer.Start();
    }

    private void OnWindowDragCompleted(object? sender, EventArgs e)
    {
        if (sender is not WorkbenchToolForm window)
            return;
        window.SetDragVisual(false);
        window.SetDockPreview(false);
        Rectangle current = ClampToGame(window.Bounds, window.MinimumSize);
        Rectangle target = SnapBounds(window, current);
        animationTargets[window] = target;
        if (!windowMotions.TryGetValue(window, out WindowMotion? motion))
        {
            motion = new WindowMotion();
            windowMotions[window] = motion;
        }
        motion.Start(current, target);
        animationTimer.Start();
    }

    private bool AdvanceWindowMotion(WorkbenchToolForm window)
    {
        if (!windowMotions.TryGetValue(window, out WindowMotion? motion) || !motion.Active || window.IsUserDragging)
            return false;
        Rectangle bounds = motion.Step();
        arranging = true;
        try
        {
            window.Bounds = bounds;
        }
        finally
        {
            arranging = false;
        }
        return motion.Active;
    }

    private static double EaseOutBack(double value)
    {
        value = Math.Clamp(value, 0d, 1d);
        const double overshoot = 1.52d;
        double shifted = value - 1d;
        return 1d + (overshoot + 1d) * shifted * shifted * shifted + overshoot * shifted * shifted;
    }

    private Rectangle SnapBounds(WorkbenchToolForm moving, Rectangle bounds)
    {
        int x = bounds.X;
        int y = bounds.Y;
        if (Math.Abs(bounds.Left - gameBounds.Left) <= SnapDistance) x = gameBounds.Left;
        if (Math.Abs(bounds.Right - gameBounds.Right) <= SnapDistance) x = gameBounds.Right - bounds.Width;
        if (Math.Abs(bounds.Top - gameBounds.Top) <= SnapDistance) y = gameBounds.Top;
        if (Math.Abs(bounds.Bottom - gameBounds.Bottom) <= SnapDistance) y = gameBounds.Bottom - bounds.Height;
        Rectangle candidate = new Rectangle(x, y, bounds.Width, bounds.Height);
        foreach (WorkbenchToolForm other in windows)
        {
            if (ReferenceEquals(other, moving) || !other.Visible)
                continue;
            Rectangle anchor = animationTargets.TryGetValue(other, out Rectangle target) ? target : other.Bounds;
            bool verticalOverlap = candidate.Bottom >= anchor.Top && candidate.Top <= anchor.Bottom;
            bool horizontalOverlap = candidate.Right >= anchor.Left && candidate.Left <= anchor.Right;
            if (verticalOverlap && Math.Abs(candidate.Left - anchor.Right) <= SnapDistance) candidate.X = anchor.Right;
            if (verticalOverlap && Math.Abs(candidate.Right - anchor.Left) <= SnapDistance) candidate.X = anchor.Left - candidate.Width;
            if (horizontalOverlap && Math.Abs(candidate.Top - anchor.Bottom) <= SnapDistance) candidate.Y = anchor.Bottom;
            if (horizontalOverlap && Math.Abs(candidate.Bottom - anchor.Top) <= SnapDistance) candidate.Y = anchor.Top - candidate.Height;
            if (Math.Abs(candidate.Top - anchor.Top) <= SnapDistance) candidate.Y = anchor.Top;
            if (Math.Abs(candidate.Bottom - anchor.Bottom) <= SnapDistance) candidate.Y = anchor.Bottom - candidate.Height;
            if (Math.Abs(candidate.Left - anchor.Left) <= SnapDistance) candidate.X = anchor.Left;
            if (Math.Abs(candidate.Right - anchor.Right) <= SnapDistance) candidate.X = anchor.Right - candidate.Width;
        }
        return ClampToGame(candidate, moving.MinimumSize);
    }

    private Rectangle ClampToGame(Rectangle bounds, Size minimum)
    {
        if (gameBounds.Width <= 0 || gameBounds.Height <= 0)
            return bounds;
        int width = Math.Clamp(bounds.Width, Math.Min(minimum.Width, gameBounds.Width), gameBounds.Width);
        int height = Math.Clamp(bounds.Height, Math.Min(minimum.Height, gameBounds.Height), gameBounds.Height);
        int x = Math.Clamp(bounds.X, gameBounds.Left, Math.Max(gameBounds.Left, gameBounds.Right - width));
        int y = Math.Clamp(bounds.Y, gameBounds.Top, Math.Max(gameBounds.Top, gameBounds.Bottom - height));
        return new Rectangle(x, y, width, height);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        animationTimer.Dispose();
        foreach (WorkbenchToolForm window in windows)
            window.Dispose();
        windows.Clear();
    }
}

internal sealed class WindowMotion
{
    private double x;
    private double y;
    private double width;
    private double height;
    private double velocityX;
    private double velocityY;
    private double velocityWidth;
    private double velocityHeight;
    private Rectangle target;

    internal bool Active { get; private set; }

    internal void Start(Rectangle current, Rectangle targetBounds)
    {
        x = current.X;
        y = current.Y;
        width = current.Width;
        height = current.Height;
        target = targetBounds;
        velocityX = velocityY = velocityWidth = velocityHeight = 0d;
        Active = current != targetBounds;
    }

    internal Rectangle Step()
    {
        if (!Active)
            return target;
        StepAxis(ref x, ref velocityX, target.X);
        StepAxis(ref y, ref velocityY, target.Y);
        StepAxis(ref width, ref velocityWidth, target.Width);
        StepAxis(ref height, ref velocityHeight, target.Height);
        if (Math.Abs(x - target.X) < 0.35d && Math.Abs(y - target.Y) < 0.35d
            && Math.Abs(width - target.Width) < 0.35d && Math.Abs(height - target.Height) < 0.35d
            && Math.Abs(velocityX) < 0.20d && Math.Abs(velocityY) < 0.20d
            && Math.Abs(velocityWidth) < 0.20d && Math.Abs(velocityHeight) < 0.20d)
        {
            x = target.X;
            y = target.Y;
            width = target.Width;
            height = target.Height;
            Active = false;
        }
        return new Rectangle((int)Math.Round(x), (int)Math.Round(y), Math.Max(1, (int)Math.Round(width)), Math.Max(1, (int)Math.Round(height)));
    }

    internal void Stop()
    {
        Active = false;
        velocityX = velocityY = velocityWidth = velocityHeight = 0d;
    }

    private static void StepAxis(ref double value, ref double velocity, double target)
    {
        velocity = (velocity + (target - value) * 0.19d) * 0.73d;
        value += velocity;
    }
}

internal sealed class WorkbenchState
{
    private readonly List<EpsilonModuleModel> modules;
    internal event Action? Changed;
    internal EpsilonCategory SelectedCategory { get; private set; } = EpsilonCategory.Combat;
    internal EpsilonModuleModel? SelectedModule { get; private set; }
    internal string SearchQuery { get; private set; } = string.Empty;
    internal bool SearchFocused { get; set; }
    internal bool UiSettingsSelected => SelectedModule?.Name == "界面设置";

    internal WorkbenchState(List<EpsilonModuleModel> modules)
    {
        this.modules = modules;
        SelectedModule = modules.FirstOrDefault(module => module.Category == SelectedCategory) ?? modules.FirstOrDefault();
    }

    internal IReadOnlyList<EpsilonModuleModel> VisibleModules
    {
        get
        {
            IEnumerable<EpsilonModuleModel> query = modules;
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                query = query.Where(module => module.Category == SelectedCategory);
            }
            else
            {
                query = query.Where(module => module.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                    || CategoryName(module.Category).Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                    || module.Settings.Any(setting => setting.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)));
            }
            return query.ToList();
        }
    }

    internal void SelectCategory(EpsilonCategory category)
    {
        SelectedCategory = category;
        if (string.IsNullOrWhiteSpace(SearchQuery))
            SelectedModule = modules.FirstOrDefault(module => module.Category == category);
        NotifyChanged();
    }

    internal void SelectModule(EpsilonModuleModel module)
    {
        SelectedModule = module;
        SelectedCategory = module.Category;
        NotifyChanged();
    }

    internal void SelectUiSettings()
    {
        EpsilonModuleModel? settings = modules.FirstOrDefault(module => module.Name == "界面设置");
        if (settings == null)
            return;
        SearchQuery = string.Empty;
        SearchFocused = false;
        SelectedModule = settings;
        SelectedCategory = settings.Category;
        NotifyChanged();
    }

    internal void SetSearch(string value)
    {
        SearchQuery = value;
        IReadOnlyList<EpsilonModuleModel> visible = VisibleModules;
        if (SelectedModule == null || !visible.Contains(SelectedModule))
            SelectedModule = visible.FirstOrDefault();
        NotifyChanged();
    }

    internal void MoveSelection(int delta)
    {
        IReadOnlyList<EpsilonModuleModel> visible = VisibleModules;
        if (visible.Count == 0)
            return;
        int index = SelectedModule == null ? 0 : visible.ToList().IndexOf(SelectedModule);
        if (index < 0) index = 0;
        index = Math.Clamp(index + delta, 0, visible.Count - 1);
        SelectModule(visible[index]);
    }

    internal void NotifyChanged() => Changed?.Invoke();

    internal static string CategoryName(EpsilonCategory category) => category switch
    {
        EpsilonCategory.Combat => "战斗",
        EpsilonCategory.Player => "玩家",
        EpsilonCategory.Movement => "移动",
        EpsilonCategory.Render => "渲染",
        _ => category.ToString()
    };
}

[Flags]
internal enum WorkbenchWindowTransform
{
    None = 0,
    Move = 1,
    Left = 2,
    Right = 4,
    Top = 8,
    Bottom = 16
}

internal sealed class WorkbenchToolForm : Form
{
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private readonly WorkbenchSurface surface;
    private readonly Action closeRequested;
    private readonly Func<Keys, bool> keyDown;
    private readonly Func<char, bool> keyPress;
    private WorkbenchWindowTransform transformMode;
    private Rectangle transformStartBounds;
    private Point transformStartCursor;

    internal event EventHandler? UserDragStarted;
    internal event EventHandler? UserDragCompleted;
    internal bool IsUserDragging { get; private set; }

    internal string PanelId { get; }

    internal WorkbenchToolForm(
        string id,
        string title,
        WorkbenchSurface surface,
        Action closeRequested,
        Func<Keys, bool> keyDown,
        Func<char, bool> keyPress)
    {
        PanelId = id;
        this.surface = surface;
        this.closeRequested = closeRequested;
        this.keyDown = keyDown;
        this.keyPress = keyPress;
        Text = title;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = id switch
        {
            "command" => new Size(520, 80),
            "categories" => new Size(72, 360),
            "modules" => new Size(240, 360),
            _ => new Size(310, 360)
        };
        BackColor = Color.FromArgb(13, 14, 17);
        Controls.Add(surface);
        surface.Dock = DockStyle.Fill;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams value = base.CreateParams;
            value.ExStyle |= NativeMethods.WsExToolWindow;
            return value;
        }
    }

    internal void FocusSurface() => surface.Focus();
    internal void InvalidateSurface() => surface.Invalidate();
    internal bool AdvanceAnimations() => surface.AdvanceAnimations();
    internal void SetDragVisual(bool dragging) => surface.SetDragVisual(dragging);
    internal void SetDockPreview(bool preview) => surface.SetDockPreview(preview);

    internal void BeginSurfaceTransform(WorkbenchWindowTransform mode, Point cursorScreen)
    {
        if (mode == WorkbenchWindowTransform.None || IsUserDragging)
            return;
        transformMode = mode;
        transformStartBounds = Bounds;
        transformStartCursor = cursorScreen;
        IsUserDragging = true;
        UserDragStarted?.Invoke(this, EventArgs.Empty);
        BringToFront();
        Activate();
        surface.SetDragVisual(mode == WorkbenchWindowTransform.Move);
    }

    internal void UpdateSurfaceTransform(Point cursorScreen)
    {
        if (!IsUserDragging || transformMode == WorkbenchWindowTransform.None)
            return;
        int dx = cursorScreen.X - transformStartCursor.X;
        int dy = cursorScreen.Y - transformStartCursor.Y;
        Rectangle next = transformStartBounds;
        if (transformMode.HasFlag(WorkbenchWindowTransform.Move))
        {
            next.X += dx;
            next.Y += dy;
        }
        else
        {
            if (transformMode.HasFlag(WorkbenchWindowTransform.Left))
            {
                next.X += dx;
                next.Width -= dx;
            }
            if (transformMode.HasFlag(WorkbenchWindowTransform.Right))
                next.Width += dx;
            if (transformMode.HasFlag(WorkbenchWindowTransform.Top))
            {
                next.Y += dy;
                next.Height -= dy;
            }
            if (transformMode.HasFlag(WorkbenchWindowTransform.Bottom))
                next.Height += dy;
            if (next.Width < MinimumSize.Width)
            {
                if (transformMode.HasFlag(WorkbenchWindowTransform.Left))
                    next.X -= MinimumSize.Width - next.Width;
                next.Width = MinimumSize.Width;
            }
            if (next.Height < MinimumSize.Height)
            {
                if (transformMode.HasFlag(WorkbenchWindowTransform.Top))
                    next.Y -= MinimumSize.Height - next.Height;
                next.Height = MinimumSize.Height;
            }
        }
        Bounds = next;
    }

    internal void EndSurfaceTransform()
    {
        if (!IsUserDragging)
            return;
        transformMode = WorkbenchWindowTransform.None;
        IsUserDragging = false;
        surface.SetDragVisual(false);
        UserDragCompleted?.Invoke(this, EventArgs.Empty);
    }

    internal void BeginUserDrag()
    {
        if (IsUserDragging)
            return;
        IsUserDragging = true;
        UserDragStarted?.Invoke(this, EventArgs.Empty);
        BringToFront();
        Activate();
        ReleaseCapture();
        SendMessageW(Handle, WmNcLButtonDown, HtCaption, 0);
        IsUserDragging = false;
        UserDragCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (keyDown(e.KeyCode))
            e.SuppressKeyPress = true;
        base.OnKeyDown(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (keyPress(e.KeyChar))
            e.Handled = true;
        base.OnKeyPress(e);
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

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WmNcHitTest || (int)m.Result != HtClient)
            return;
        Point cursor = PointToClient(Cursor.Position);
        const int grip = 6;
        bool left = cursor.X <= grip;
        bool right = cursor.X >= ClientSize.Width - grip;
        bool top = cursor.Y <= grip;
        bool bottom = cursor.Y >= ClientSize.Height - grip;
        if (left && top) m.Result = HtTopLeft;
        else if (right && top) m.Result = HtTopRight;
        else if (left && bottom) m.Result = HtBottomLeft;
        else if (right && bottom) m.Result = HtBottomRight;
        else if (left) m.Result = HtLeft;
        else if (right) m.Result = HtRight;
        else if (top) m.Result = HtTop;
        else if (bottom) m.Result = HtBottom;
        else if (cursor.Y <= WorkbenchSurface.TitleHeight) m.Result = HtCaption;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(nint hWnd, int message, nint wParam, nint lParam);
}

internal abstract class WorkbenchSurface : Control
{
    internal const int TitleHeight = 32;
    protected readonly WorkbenchState State;
    protected readonly AppConfig Config;
    private readonly Dictionary<(int Size, FontStyle Style), Font> fonts = new Dictionary<(int, FontStyle), Font>();
    private readonly string title;
    private bool dragVisual;
    private bool dockPreview;
    private WorkbenchWindowTransform activeTransform;

    protected bool WindowTransformActive => activeTransform != WorkbenchWindowTransform.None;

    protected Color Background => EpsilonTheme.Mix(EpsilonTheme.Background(Config), Color.FromArgb(12, 13, 16), 0.72f);
    protected Color Surface => EpsilonTheme.Mix(EpsilonTheme.Panel(Config), Color.FromArgb(22, 23, 27), 0.65f);
    protected Color SurfaceHigh => EpsilonTheme.Mix(Surface, Text, 0.07f);
    protected Color SurfaceHighest => EpsilonTheme.Mix(Surface, Text, 0.12f);
    protected Color Accent => EpsilonTheme.Accent(Config);
    protected new Color Text => EpsilonTheme.Text(Config);
    protected Color TextSecondary => EpsilonTheme.Mix(Text, Background, 0.34f);
    protected Color TextMuted => EpsilonTheme.Mix(Text, Background, 0.58f);
    protected Color Outline => EpsilonTheme.Mix(Text, Background, 0.76f);

    protected WorkbenchSurface(string title, WorkbenchState state, AppConfig config)
    {
        this.title = title;
        State = state;
        Config = config;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        TabStop = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = Config.AntiAliasingEnabled ? SmoothingMode.AntiAlias : SmoothingMode.None;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using SolidBrush background = new SolidBrush(Background);
        e.Graphics.FillRectangle(background, ClientRectangle);
        using SolidBrush titleFill = new SolidBrush(EpsilonTheme.Mix(Background, Color.Black, 0.18f));
        e.Graphics.FillRectangle(titleFill, 0, 0, Width, TitleHeight);
        if (dragVisual)
        {
            using LinearGradientBrush dragGlow = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(1, Width), TitleHeight),
                Color.FromArgb(110, Accent),
                Color.FromArgb(8, Accent),
                LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(dragGlow, 0, 0, Width, TitleHeight);
        }
        using Pen divider = new Pen(Color.FromArgb(105, Outline));
        e.Graphics.DrawLine(divider, 0, TitleHeight - 1, Width, TitleHeight - 1);
        using SolidBrush marker = new SolidBrush(Accent);
        e.Graphics.FillRectangle(marker, 10, 10, 4, 12);
        DrawText(e.Graphics, title, 7.5f, FontStyle.Bold, TextSecondary, 22, 8);
        DrawContent(e.Graphics, new Rectangle(0, TitleHeight, Width, Math.Max(0, Height - TitleHeight)));
        if (dockPreview)
        {
            using Pen magnetBorder = new Pen(Color.FromArgb(235, Accent), 2f);
            e.Graphics.DrawRectangle(magnetBorder, 1, 1, Math.Max(0, Width - 3), Math.Max(0, Height - 3));
            RectangleF badge = new RectangleF(Math.Max(12f, Width - 116f), 6f, 102f, 21f);
            using SolidBrush badgeFill = new SolidBrush(Color.FromArgb(225, EpsilonTheme.Mix(SurfaceHighest, Accent, 0.42f)));
            FillRounded(e.Graphics, badgeFill, badge, 6f);
            DrawTextCentered(e.Graphics, "释放以吸附", 6.2f, FontStyle.Bold, Text, badge);
        }
        using Pen border = new Pen(Color.FromArgb(dockPreview ? 225 : 160, dockPreview ? Accent : Outline));
        e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    }

    internal void SetDragVisual(bool dragging)
    {
        dragVisual = dragging;
        Cursor = dragging ? Cursors.SizeAll : Cursors.Default;
        Invalidate(new Rectangle(0, 0, Width, TitleHeight + 1));
    }

    internal void SetDockPreview(bool preview)
    {
        if (dockPreview == preview)
            return;
        dockPreview = preview;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || FindForm() is not WorkbenchToolForm window)
            return;
        activeTransform = HitTestTransform(e.Location);
        if (activeTransform == WorkbenchWindowTransform.None)
            return;
        Capture = true;
        window.BeginSurfaceTransform(activeTransform, PointToScreen(e.Location));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (activeTransform != WorkbenchWindowTransform.None && FindForm() is WorkbenchToolForm moving)
        {
            moving.UpdateSurfaceTransform(PointToScreen(e.Location));
            return;
        }
        Cursor = CursorFor(HitTestTransform(e.Location));
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (activeTransform != WorkbenchWindowTransform.None && FindForm() is WorkbenchToolForm window)
        {
            activeTransform = WorkbenchWindowTransform.None;
            Capture = false;
            window.EndSurfaceTransform();
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (activeTransform == WorkbenchWindowTransform.None)
            Cursor = Cursors.Default;
        base.OnMouseLeave(e);
    }

    private WorkbenchWindowTransform HitTestTransform(Point point)
    {
        const int grip = 8;
        bool left = point.X <= grip;
        bool right = point.X >= Width - grip;
        bool top = point.Y <= grip;
        bool bottom = point.Y >= Height - grip;
        WorkbenchWindowTransform mode = WorkbenchWindowTransform.None;
        if (left) mode |= WorkbenchWindowTransform.Left;
        if (right) mode |= WorkbenchWindowTransform.Right;
        if (top) mode |= WorkbenchWindowTransform.Top;
        if (bottom) mode |= WorkbenchWindowTransform.Bottom;
        if (mode == WorkbenchWindowTransform.None && point.Y < TitleHeight)
            mode = WorkbenchWindowTransform.Move;
        return mode;
    }

    private static Cursor CursorFor(WorkbenchWindowTransform mode)
    {
        if (mode.HasFlag(WorkbenchWindowTransform.Move)) return Cursors.SizeAll;
        bool horizontal = mode.HasFlag(WorkbenchWindowTransform.Left) || mode.HasFlag(WorkbenchWindowTransform.Right);
        bool vertical = mode.HasFlag(WorkbenchWindowTransform.Top) || mode.HasFlag(WorkbenchWindowTransform.Bottom);
        if (horizontal && vertical)
        {
            bool backslash = (mode.HasFlag(WorkbenchWindowTransform.Left) && mode.HasFlag(WorkbenchWindowTransform.Top))
                || (mode.HasFlag(WorkbenchWindowTransform.Right) && mode.HasFlag(WorkbenchWindowTransform.Bottom));
            return backslash ? Cursors.SizeNWSE : Cursors.SizeNESW;
        }
        if (horizontal) return Cursors.SizeWE;
        if (vertical) return Cursors.SizeNS;
        return Cursors.Default;
    }

    protected abstract void DrawContent(Graphics graphics, Rectangle bounds);
    internal virtual bool AdvanceAnimations() => false;

    protected Font UiFont(float pixels, FontStyle style)
    {
        int size = Math.Max(9, (int)Math.Round(pixels * 1.42f));
        (int, FontStyle) key = (size, style);
        if (!fonts.TryGetValue(key, out Font? font))
        {
            font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Pixel);
            fonts[key] = font;
        }
        return font;
    }

    protected void DrawText(Graphics g, string text, float size, FontStyle style, Color color, float x, float y)
    {
        using SolidBrush brush = new SolidBrush(color);
        g.DrawString(text, UiFont(size, style), brush, x, y, StringFormat.GenericTypographic);
    }

    protected void DrawTextRight(Graphics g, string text, float size, FontStyle style, Color color, float right, float y)
    {
        SizeF measured = g.MeasureString(text, UiFont(size, style), int.MaxValue, StringFormat.GenericTypographic);
        DrawText(g, text, size, style, color, right - measured.Width, y);
    }

    protected void DrawTextCentered(Graphics g, string text, float size, FontStyle style, Color color, RectangleF bounds)
    {
        using SolidBrush brush = new SolidBrush(color);
        using StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, UiFont(size, style), brush, bounds, format);
    }

    protected static void FillRounded(Graphics g, Brush brush, RectangleF bounds, float radius)
    {
        using GraphicsPath path = Rounded(bounds, radius);
        g.FillPath(brush, path);
    }

    protected static void DrawRounded(Graphics g, Pen pen, RectangleF bounds, float radius)
    {
        using GraphicsPath path = Rounded(bounds, radius);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        GraphicsPath path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (Font font in fonts.Values)
                font.Dispose();
            fonts.Clear();
        }
        base.Dispose(disposing);
    }
}

internal sealed class WorkbenchCommandSurface : WorkbenchSurface
{
    private readonly Action toggleLogs;
    private readonly Func<bool> logsVisible;
    private readonly Action resetLayout;
    private readonly Action closeRequested;
    private RectangleF searchBounds;
    private RectangleF logsBounds;
    private RectangleF resetBounds;
    private RectangleF closeBounds;

    internal WorkbenchCommandSurface(WorkbenchState state, AppConfig config, Action toggleLogs, Func<bool> logsVisible, Action resetLayout, Action closeRequested)
        : base("COMMAND CENTER", state, config)
    {
        this.toggleLogs = toggleLogs;
        this.logsVisible = logsVisible;
        this.resetLayout = resetLayout;
        this.closeRequested = closeRequested;
    }

    protected override void DrawContent(Graphics g, Rectangle bounds)
    {
        float y = bounds.Y + 10f;
        searchBounds = new RectangleF(14f, y, Math.Max(160f, Width - 338f), 36f);
        logsBounds = new RectangleF(Width - 310f, y, 82f, 36f);
        resetBounds = new RectangleF(Width - 218f, y, 128f, 36f);
        closeBounds = new RectangleF(Width - 78f, y, 64f, 36f);
        using SolidBrush searchFill = new SolidBrush(State.SearchFocused ? SurfaceHighest : SurfaceHigh);
        FillRounded(g, searchFill, searchBounds, 8f);
        using Pen searchPen = new Pen(Color.FromArgb(State.SearchFocused ? 210 : 95, State.SearchFocused ? Accent : Outline));
        DrawRounded(g, searchPen, searchBounds, 8f);
        using Pen glyph = new Pen(State.SearchFocused ? Accent : TextMuted, 1.5f);
        g.DrawEllipse(glyph, searchBounds.X + 14f, searchBounds.Y + 11f, 10f, 10f);
        g.DrawLine(glyph, searchBounds.X + 22f, searchBounds.Y + 20f, searchBounds.X + 27f, searchBounds.Y + 25f);
        string query = State.SearchQuery.Length == 0 ? "搜索全部模块与参数  Ctrl+F" : State.SearchQuery;
        DrawText(g, query, 7.5f, FontStyle.Regular, State.SearchQuery.Length == 0 ? TextMuted : Text, searchBounds.X + 35f, searchBounds.Y + 9f);
        DrawButton(g, logsBounds, logsVisible() ? "隐藏日志" : "实时日志", logsVisible());
        DrawButton(g, resetBounds, "重置停靠", false);
        DrawButton(g, closeBounds, "关闭", true);
    }

    private void DrawButton(Graphics g, RectangleF bounds, string text, bool accent)
    {
        using SolidBrush fill = new SolidBrush(accent ? EpsilonTheme.Mix(SurfaceHigh, Accent, 0.34f) : SurfaceHigh);
        FillRounded(g, fill, bounds, 8f);
        using Pen border = new Pen(Color.FromArgb(accent ? 165 : 85, accent ? Accent : Outline));
        DrawRounded(g, border, bounds, 8f);
        DrawTextCentered(g, text, 7f, FontStyle.Bold, accent ? Text : TextSecondary, bounds);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (WindowTransformActive)
            return;
        Focus();
        if (searchBounds.Contains(e.Location))
        {
            State.SearchFocused = true;
            State.NotifyChanged();
        }
        else if (resetBounds.Contains(e.Location))
        {
            resetLayout();
        }
        else if (logsBounds.Contains(e.Location))
        {
            toggleLogs();
        }
        else if (closeBounds.Contains(e.Location))
        {
            closeRequested();
        }
        else
        {
            State.SearchFocused = false;
            State.NotifyChanged();
        }
    }
}

internal sealed class WorkbenchCategorySurface : WorkbenchSurface
{
    private float highlightY = 48f;
    private float highlightVelocity;

    internal WorkbenchCategorySurface(WorkbenchState state, AppConfig config) : base("ACTIVITY BAR", state, config) { }

    protected override void DrawContent(Graphics g, Rectangle bounds)
    {
        float target = bounds.Y + 18f + (int)State.SelectedCategory * 68f;
        using SolidBrush active = new SolidBrush(EpsilonTheme.Mix(SurfaceHigh, Accent, 0.28f));
        FillRounded(g, active, new RectangleF(10f, highlightY, Width - 20f, 52f), 10f);
        using SolidBrush marker = new SolidBrush(Accent);
        FillRounded(g, marker, new RectangleF(0f, highlightY + 13f, 3f, 26f), 1.5f);
        for (int i = 0; i < 4; i++)
        {
            EpsilonCategory category = (EpsilonCategory)i;
            RectangleF row = new RectangleF(8f, bounds.Y + 18f + i * 68f, Width - 16f, 52f);
            bool selected = !State.UiSettingsSelected && category == State.SelectedCategory;
            DrawTextCentered(g, CategoryIcon(category), 11f, FontStyle.Bold, selected ? Accent : TextSecondary, new RectangleF(row.X, row.Y + 2f, row.Width, 25f));
            DrawTextCentered(g, WorkbenchState.CategoryName(category), 6.5f, FontStyle.Regular, selected ? Text : TextMuted, new RectangleF(row.X, row.Y + 28f, row.Width, 18f));
        }
        RectangleF settings = SettingsBounds;
        if (State.UiSettingsSelected)
        {
            using SolidBrush settingsFill = new SolidBrush(EpsilonTheme.Mix(SurfaceHigh, Accent, 0.30f));
            FillRounded(g, settingsFill, settings, 10f);
            using SolidBrush settingsMarker = new SolidBrush(Accent);
            FillRounded(g, settingsMarker, new RectangleF(0f, settings.Y + 13f, 3f, 26f), 1.5f);
        }
        DrawTextCentered(g, "⚙", 11f, FontStyle.Regular, State.UiSettingsSelected ? Accent : TextSecondary, new RectangleF(settings.X, settings.Y + 2f, settings.Width, 25f));
        DrawTextCentered(g, "外观", 6.5f, FontStyle.Regular, State.UiSettingsSelected ? Text : TextMuted, new RectangleF(settings.X, settings.Y + 28f, settings.Width, 18f));
    }

    internal override bool AdvanceAnimations()
    {
        float target = TitleHeight + 18f + (int)State.SelectedCategory * 68f;
        float before = highlightY;
        Spring(ref highlightY, ref highlightVelocity, target, 0.22f, 0.70f);
        return Math.Abs(before - highlightY) > 0.001f;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (WindowTransformActive)
            return;
        Focus();
        if (SettingsBounds.Contains(e.Location))
        {
            State.SelectUiSettings();
            return;
        }
        for (int i = 0; i < 4; i++)
        {
            RectangleF row = new RectangleF(8f, TitleHeight + 18f + i * 68f, Width - 16f, 52f);
            if (row.Contains(e.Location))
            {
                State.SelectCategory((EpsilonCategory)i);
                return;
            }
        }
    }

    private RectangleF SettingsBounds => new RectangleF(8f, Math.Max(TitleHeight + 314f, Height - 70f), Width - 16f, 52f);

    private static string CategoryIcon(EpsilonCategory category) => category switch
    {
        EpsilonCategory.Combat => "⚔",
        EpsilonCategory.Player => "●",
        EpsilonCategory.Movement => "➜",
        EpsilonCategory.Render => "✦",
        _ => "•"
    };

    private static void Spring(ref float value, ref float velocity, float target, float stiffness, float damping)
    {
        velocity = (velocity + (target - value) * stiffness) * damping;
        value += velocity;
        if (Math.Abs(target - value) < 0.02f && Math.Abs(velocity) < 0.02f)
        {
            value = target;
            velocity = 0f;
        }
    }
}

internal sealed class WorkbenchModuleSurface : WorkbenchSurface
{
    private const float RowHeight = 56f;
    private readonly Action commit;
    private readonly Dictionary<EpsilonModuleModel, WorkbenchSpring> toggles = new Dictionary<EpsilonModuleModel, WorkbenchSpring>();
    private readonly Dictionary<EpsilonModuleModel, float> togglePulses = new Dictionary<EpsilonModuleModel, float>();
    private readonly Dictionary<EpsilonModuleModel, long> toggleAnimationStarted = new Dictionary<EpsilonModuleModel, long>();
    private float scroll;

    internal WorkbenchModuleSurface(WorkbenchState state, AppConfig config, Action commit) : base("MODULE EXPLORER", state, config)
    {
        this.commit = commit;
    }

    protected override void DrawContent(Graphics g, Rectangle bounds)
    {
        IReadOnlyList<EpsilonModuleModel> modules = State.VisibleModules;
        DrawText(g, string.IsNullOrWhiteSpace(State.SearchQuery) ? WorkbenchState.CategoryName(State.SelectedCategory) : "全局搜索", 10f, FontStyle.Bold, Text, 16f, bounds.Y + 15f);
        DrawTextRight(g, modules.Count.ToString(CultureInfo.InvariantCulture), 7f, FontStyle.Bold, Accent, Width - 16f, bounds.Y + 19f);
        using Pen line = new Pen(Color.FromArgb(90, Outline));
        g.DrawLine(line, 0, bounds.Y + 50f, Width, bounds.Y + 50f);
        RectangleF viewport = new RectangleF(0f, bounds.Y + 51f, Width, Math.Max(0f, bounds.Height - 51f));
        GraphicsState saved = g.Save();
        g.SetClip(viewport);
        float y = viewport.Y - scroll;
        foreach (EpsilonModuleModel module in modules)
        {
            RectangleF row = new RectangleF(0f, y, Width, RowHeight);
            bool selected = ReferenceEquals(State.SelectedModule, module);
            float pulse = GetPulse(module);
            Color rowBase = selected ? EpsilonTheme.Mix(SurfaceHigh, Accent, 0.20f) : Surface;
            float rowFlash = (float)Math.Sin(Math.PI * Math.Clamp(pulse, 0f, 1f));
            using SolidBrush fill = new SolidBrush(EpsilonTheme.Mix(rowBase, Accent, rowFlash * 0.42f));
            g.FillRectangle(fill, row);
            if (selected)
            {
                using SolidBrush marker = new SolidBrush(Accent);
                g.FillRectangle(marker, row.X, row.Y, 3f, row.Height);
            }
            using SolidBrush glyphFill = new SolidBrush(selected ? EpsilonTheme.Mix(SurfaceHighest, Accent, 0.34f) : SurfaceHigh);
            FillRounded(g, glyphFill, new RectangleF(row.X + 14f, row.Y + 13f, 29f, 29f), 7f);
            string glyph = module.Name.Length == 0 ? "?" : module.Name[..1];
            DrawTextCentered(g, glyph, 7f, FontStyle.Bold, selected ? Accent : TextMuted, new RectangleF(row.X + 14f, row.Y + 13f, 29f, 29f));
            DrawText(g, module.Name, 8f, selected ? FontStyle.Bold : FontStyle.Regular, selected ? Text : TextSecondary, row.X + 54f, row.Y + 9f);
            DrawText(g, WorkbenchState.CategoryName(module.Category) + " · " + module.Settings.Count + " 参数", 6.5f, FontStyle.Regular, TextMuted, row.X + 54f, row.Y + 29f);
            float inflate = rowFlash * 2.8f;
            DrawSwitch(g, RectangleF.Inflate(new RectangleF(row.Right - 48f, row.Y + 18f, 34f, 20f), inflate, inflate * 0.55f), GetToggle(module), pulse, rowFlash);
            g.DrawLine(line, row.X + 14f, row.Bottom - 1f, row.Right - 14f, row.Bottom - 1f);
            y += RowHeight;
        }
        g.Restore(saved);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (WindowTransformActive)
            return;
        Focus();
        IReadOnlyList<EpsilonModuleModel> modules = State.VisibleModules;
        float y = TitleHeight + 51f - scroll;
        foreach (EpsilonModuleModel module in modules)
        {
            RectangleF row = new RectangleF(0f, y, Width, RowHeight);
            if (row.Contains(e.Location))
            {
                if (e.X >= row.Right - 62f)
                {
                    WorkbenchSpring spring = GetSpring(module);
                    bool next = !module.EnabledGetter();
                    module.EnabledSetter(next);
                    spring.Kick(next ? 0.28f : -0.28f);
                    togglePulses[module] = 0f;
                    toggleAnimationStarted[module] = Environment.TickCount64;
                    commit();
                    State.NotifyChanged();
                }
                else
                {
                    State.SelectModule(module);
                }
                return;
            }
            y += RowHeight;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        float max = Math.Max(0f, State.VisibleModules.Count * RowHeight - Math.Max(0, Height - TitleHeight - 51));
        scroll = Math.Clamp(scroll - e.Delta / 120f * 44f, 0f, max);
        Invalidate();
        base.OnMouseWheel(e);
    }

    internal override bool AdvanceAnimations()
    {
        bool changed = false;
        foreach (EpsilonModuleModel module in State.VisibleModules)
        {
            if (!toggles.TryGetValue(module, out WorkbenchSpring? spring))
            {
                spring = new WorkbenchSpring(module.EnabledGetter() ? 1f : 0f);
                toggles[module] = spring;
            }
            changed |= spring.Step(module.EnabledGetter() ? 1f : 0f);
            if (toggleAnimationStarted.TryGetValue(module, out long started))
            {
                float progress = Math.Clamp((Environment.TickCount64 - started) / 460f, 0f, 1f);
                togglePulses[module] = progress;
                if (progress < 1f)
                    changed = true;
                else
                    toggleAnimationStarted.Remove(module);
            }
        }
        return changed;
    }

    private float GetToggle(EpsilonModuleModel module)
    {
        return GetSpring(module).Value;
    }

    private WorkbenchSpring GetSpring(EpsilonModuleModel module)
    {
        if (!toggles.TryGetValue(module, out WorkbenchSpring? spring))
        {
            spring = new WorkbenchSpring(module.EnabledGetter() ? 1f : 0f);
            toggles[module] = spring;
        }
        return spring;
    }

    private float GetPulse(EpsilonModuleModel module) => togglePulses.TryGetValue(module, out float pulse) ? pulse : 0f;

    private void DrawSwitch(Graphics g, RectangleF bounds, float value, float pulse, float rowFlash)
    {
        float progress = Math.Clamp(value, 0f, 1f);
        float eased = progress * progress * (3f - 2f * progress);
        if (pulse > 0.001f && pulse < 1f)
        {
            float ring = (float)(1d - Math.Pow(1d - pulse, 2d));
            int alpha = (int)Math.Round(210f * (1f - pulse));
            using Pen halo = new Pen(Color.FromArgb(Math.Clamp(alpha, 0, 210), Accent), 2.8f - pulse * 1.3f);
            DrawRounded(g, halo, RectangleF.Inflate(bounds, 3f + ring * 13f, 2f + ring * 8f), bounds.Height / 2f + 12f);
        }
        Color trackColor = EpsilonTheme.Mix(SurfaceHighest, Accent, Math.Clamp(eased + rowFlash * 0.18f, 0f, 1f));
        using SolidBrush track = new SolidBrush(trackColor);
        FillRounded(g, track, bounds, bounds.Height / 2f);
        float bounce = (float)Math.Sin(Math.PI * Math.Clamp(pulse, 0f, 1f));
        float size = bounds.Height - 5f + (float)Math.Sin(Math.PI * progress) * 2f + bounce * 5.5f;
        float x = bounds.Left + 2.5f + size / 2f + (bounds.Width - size - 5f) * eased;
        using SolidBrush knob = new SolidBrush(EpsilonTheme.Mix(TextMuted, EpsilonTheme.OnAccent(Config), eased));
        g.FillEllipse(knob, x - size / 2f, bounds.Top + bounds.Height / 2f - size / 2f, size, size);
        if (bounce > 0.05f)
        {
            using SolidBrush core = new SolidBrush(Color.FromArgb((int)(170f * bounce), Accent));
            float coreSize = Math.Max(3f, size * 0.35f);
            g.FillEllipse(core, x - coreSize / 2f, bounds.Top + bounds.Height / 2f - coreSize / 2f, coreSize, coreSize);
        }
    }
}

internal sealed class WorkbenchLogSurface : WorkbenchSurface
{
    private readonly Timer refreshTimer = new Timer { Interval = 350 };
    private readonly List<string> lines = new List<string>();
    private long lastLength = -1;
    private float scroll;
    private bool followTail = true;

    internal WorkbenchLogSurface(WorkbenchState state, AppConfig config) : base("LIVE LOG", state, config)
    {
        refreshTimer.Tick += delegate { RefreshLog(); };
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            RefreshLog(true);
            refreshTimer.Start();
        }
        else
        {
            refreshTimer.Stop();
        }
    }

    private void RefreshLog(bool force = false)
    {
        try
        {
            FileInfo info = new FileInfo(AppLog.FilePath);
            long length = info.Exists ? info.Length : 0L;
            if (!force && length == lastLength)
                return;
            lastLength = length;
            lines.Clear();
            if (info.Exists)
            {
                string[] all = File.ReadAllLines(info.FullName);
                int start = Math.Max(0, all.Length - 500);
                for (int i = start; i < all.Length; i++)
                    lines.Add(all[i]);
            }
            if (followTail)
                scroll = float.MaxValue;
            Invalidate();
        }
        catch
        {
        }
    }

    protected override void DrawContent(Graphics g, Rectangle bounds)
    {
        const float lineHeight = 18f;
        DrawText(g, "overlay.log", 6.8f, FontStyle.Bold, Accent, 14f, bounds.Y + 10f);
        DrawTextRight(g, lines.Count + " 行", 6.5f, FontStyle.Regular, TextMuted, Width - 14f, bounds.Y + 10f);
        using Pen divider = new Pen(Color.FromArgb(90, Outline));
        g.DrawLine(divider, 0, bounds.Y + 34f, Width, bounds.Y + 34f);
        RectangleF viewport = new RectangleF(12f, bounds.Y + 42f, Math.Max(20f, Width - 24f), Math.Max(0f, bounds.Height - 52f));
        float contentHeight = lines.Count * lineHeight;
        float max = Math.Max(0f, contentHeight - viewport.Height);
        if (scroll == float.MaxValue)
            scroll = max;
        scroll = Math.Clamp(scroll, 0f, max);
        GraphicsState saved = g.Save();
        g.SetClip(viewport);
        float y = viewport.Y - scroll;
        for (int i = 0; i < lines.Count; i++)
        {
            if (y + lineHeight >= viewport.Top && y <= viewport.Bottom)
            {
                string line = lines[i];
                Color color = line.Contains("失败", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("error", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(255, 255, 125, 135)
                    : line.Contains("警告", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("warn", StringComparison.OrdinalIgnoreCase)
                        ? Color.FromArgb(255, 255, 198, 104)
                        : TextSecondary;
                DrawText(g, line, 6.4f, FontStyle.Regular, color, viewport.X, y);
            }
            y += lineHeight;
        }
        if (lines.Count == 0)
            DrawTextCentered(g, "等待日志输出…", 7.5f, FontStyle.Regular, TextMuted, viewport);
        g.Restore(saved);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        followTail = false;
        float max = Math.Max(0f, lines.Count * 18f - Math.Max(0, Height - TitleHeight - 52));
        scroll = Math.Clamp(scroll - e.Delta / 120f * 72f, 0f, max);
        if (scroll >= max - 3f)
            followTail = true;
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            refreshTimer.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class WorkbenchInspectorSurface : WorkbenchSurface
{
    private const float RowHeight = 62f;
    private readonly Action commit;
    private readonly Dictionary<object, WorkbenchSpring> toggles = new Dictionary<object, WorkbenchSpring>();
    private EpsilonSliderSettingModel? draggingSlider;
    private RectangleF draggingTrack;
    private float scroll;

    internal WorkbenchInspectorSurface(WorkbenchState state, AppConfig config, Action commit) : base("INSPECTOR", state, config)
    {
        this.commit = commit;
    }

    protected override void DrawContent(Graphics g, Rectangle bounds)
    {
        EpsilonModuleModel? module = State.SelectedModule;
        if (module == null)
        {
            DrawTextCentered(g, "没有匹配的模块", 8f, FontStyle.Regular, TextMuted, bounds);
            return;
        }
        DrawText(g, "MODULE", 6.5f, FontStyle.Bold, Accent, 18f, bounds.Y + 14f);
        DrawText(g, module.Name, 14f, FontStyle.Bold, Text, 18f, bounds.Y + 33f);
        DrawText(g, WorkbenchState.CategoryName(module.Category) + " / " + module.Settings.Count + " 个参数", 7f, FontStyle.Regular, TextMuted, 18f, bounds.Y + 63f);
        DrawSwitch(g, new RectangleF(Width - 62f, bounds.Y + 35f, 42f, 23f), GetToggle(module, module.EnabledGetter()));
        using Pen divider = new Pen(Color.FromArgb(92, Outline));
        g.DrawLine(divider, 18f, bounds.Y + 93f, Width - 18f, bounds.Y + 93f);
        DrawText(g, "PARAMETERS", 6.5f, FontStyle.Bold, TextMuted, 18f, bounds.Y + 105f);
        RectangleF viewport = new RectangleF(18f, bounds.Y + 128f, Math.Max(20f, Width - 36f), Math.Max(0f, bounds.Height - 139f));
        GraphicsState saved = g.Save();
        g.SetClip(viewport);
        float y = viewport.Y - scroll;
        foreach (EpsilonSettingModel setting in module.Settings)
        {
            RectangleF row = new RectangleF(viewport.X, y, viewport.Width, RowHeight - 5f);
            DrawSetting(g, setting, row, divider);
            y += RowHeight;
        }
        g.Restore(saved);
    }

    private void DrawSetting(Graphics g, EpsilonSettingModel setting, RectangleF row, Pen divider)
    {
        bool matched = State.SearchQuery.Length > 0 && setting.Name.Contains(State.SearchQuery, StringComparison.OrdinalIgnoreCase);
        if (matched)
        {
            using SolidBrush match = new SolidBrush(EpsilonTheme.Mix(SurfaceHigh, Accent, 0.24f));
            g.FillRectangle(match, row);
        }
        DrawText(g, setting.Name, 8f, FontStyle.Regular, TextSecondary, row.X + 2f, row.Y + 9f);
        if (setting is EpsilonBoolSettingModel boolean)
        {
            DrawText(g, boolean.Getter() ? "已启用" : "已关闭", 6.5f, FontStyle.Regular, TextMuted, row.X + 2f, row.Y + 31f);
            DrawSwitch(g, new RectangleF(row.Right - 40f, row.Y + 19f, 34f, 20f), GetToggle(boolean, boolean.Getter()));
        }
        else if (setting is EpsilonSliderSettingModel slider)
        {
            string value = slider.Getter().ToString(slider.Step < 1d ? "0.0" : "0", CultureInfo.InvariantCulture);
            RectangleF tag = new RectangleF(row.Right - 62f, row.Y + 7f, 56f, 25f);
            using SolidBrush tagFill = new SolidBrush(SurfaceHighest);
            FillRounded(g, tagFill, tag, 6f);
            DrawTextCentered(g, value, 7f, FontStyle.Bold, Text, tag);
            RectangleF track = new RectangleF(row.X + 2f, row.Bottom - 11f, row.Width - 8f, 3f);
            if (ReferenceEquals(draggingSlider, slider)) draggingTrack = track;
            double ratio = (slider.Getter() - slider.Minimum) / Math.Max(0.0001d, slider.Maximum - slider.Minimum);
            using Pen off = new Pen(Outline, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using Pen on = new Pen(Accent, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(off, track.Left, track.Top, track.Right, track.Top);
            float knobX = track.Left + (float)Math.Clamp(ratio, 0d, 1d) * track.Width;
            g.DrawLine(on, track.Left, track.Top, knobX, track.Top);
            using SolidBrush knob = new SolidBrush(Accent);
            g.FillEllipse(knob, knobX - 5f, track.Top - 5f, 10f, 10f);
        }
        else if (setting is EpsilonActionSettingModel action)
        {
            if (action.ColorGetter != null)
            {
                using SolidBrush swatch = new SolidBrush(action.ColorGetter());
                FillRounded(g, swatch, new RectangleF(row.Right - 34f, row.Y + 15f, 25f, 25f), 6f);
            }
            else
            {
                DrawTextRight(g, action.ValueGetter(), 7f, FontStyle.Bold, Text, row.Right - 6f, row.Y + 20f);
            }
        }
        g.DrawLine(divider, row.X, row.Bottom - 1f, row.Right, row.Bottom - 1f);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (WindowTransformActive)
            return;
        Focus();
        EpsilonModuleModel? module = State.SelectedModule;
        if (module == null)
            return;
        RectangleF moduleToggle = new RectangleF(Width - 62f, TitleHeight + 35f, 42f, 23f);
        if (moduleToggle.Contains(e.Location))
        {
            module.EnabledSetter(!module.EnabledGetter());
            commit();
            State.NotifyChanged();
            return;
        }
        float y = TitleHeight + 128f - scroll;
        foreach (EpsilonSettingModel setting in module.Settings)
        {
            RectangleF row = new RectangleF(18f, y, Math.Max(20f, Width - 36f), RowHeight);
            if (row.Contains(e.Location))
            {
                if (setting is EpsilonBoolSettingModel boolean)
                {
                    if (e.Button != MouseButtons.Left)
                        return;
                    boolean.Setter(!boolean.Getter());
                    commit();
                }
                else if (setting is EpsilonSliderSettingModel slider)
                {
                    if (e.Button != MouseButtons.Left)
                        return;
                    draggingSlider = slider;
                    draggingTrack = new RectangleF(row.X + 2f, row.Bottom - 15f, row.Width - 8f, 12f);
                    UpdateSlider(slider, draggingTrack, e.X);
                }
                else if (setting is EpsilonActionSettingModel action)
                {
                    if (e.Button == MouseButtons.Right)
                        action.SecondaryAction?.Invoke();
                    else if (e.Button == MouseButtons.Left)
                        action.Action();
                }
                State.NotifyChanged();
                return;
            }
            y += RowHeight;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (draggingSlider != null && e.Button == MouseButtons.Left)
            UpdateSlider(draggingSlider, draggingTrack, e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        draggingSlider = null;
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        EpsilonModuleModel? module = State.SelectedModule;
        if (module != null)
        {
            float max = Math.Max(0f, module.Settings.Count * RowHeight - Math.Max(0, Height - TitleHeight - 139));
            scroll = Math.Clamp(scroll - e.Delta / 120f * 46f, 0f, max);
            Invalidate();
        }
        base.OnMouseWheel(e);
    }

    internal override bool AdvanceAnimations()
    {
        bool changed = false;
        EpsilonModuleModel? module = State.SelectedModule;
        if (module == null)
            return false;
        changed |= GetSpring(module, module.EnabledGetter()).Step(module.EnabledGetter() ? 1f : 0f);
        foreach (EpsilonBoolSettingModel boolean in module.Settings.OfType<EpsilonBoolSettingModel>())
            changed |= GetSpring(boolean, boolean.Getter()).Step(boolean.Getter() ? 1f : 0f);
        return changed;
    }

    private void UpdateSlider(EpsilonSliderSettingModel slider, RectangleF bounds, float x)
    {
        double ratio = Math.Clamp((x - bounds.Left) / Math.Max(1f, bounds.Width), 0f, 1f);
        double raw = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
        double value = Math.Round((raw - slider.Minimum) / slider.Step) * slider.Step + slider.Minimum;
        slider.Setter(Math.Clamp(value, slider.Minimum, slider.Maximum));
        commit();
        State.NotifyChanged();
    }

    private WorkbenchSpring GetSpring(object key, bool value)
    {
        if (!toggles.TryGetValue(key, out WorkbenchSpring? spring))
        {
            spring = new WorkbenchSpring(value ? 1f : 0f);
            toggles[key] = spring;
        }
        return spring;
    }

    private float GetToggle(object key, bool value) => GetSpring(key, value).Value;

    private void DrawSwitch(Graphics g, RectangleF bounds, float value)
    {
        float progress = Math.Clamp(value, 0f, 1f);
        float eased = progress * progress * (3f - 2f * progress);
        using SolidBrush track = new SolidBrush(EpsilonTheme.Mix(SurfaceHighest, Accent, eased));
        FillRounded(g, track, bounds, bounds.Height / 2f);
        float size = bounds.Height - 5f + (float)Math.Sin(Math.PI * progress) * 1.3f;
        float x = bounds.Left + 2.5f + size / 2f + (bounds.Width - size - 5f) * eased;
        using SolidBrush knob = new SolidBrush(EpsilonTheme.Mix(TextMuted, EpsilonTheme.OnAccent(Config), eased));
        g.FillEllipse(knob, x - size / 2f, bounds.Top + bounds.Height / 2f - size / 2f, size, size);
    }
}

internal sealed class WorkbenchSpring
{
    internal float Value;
    private float velocity;

    internal WorkbenchSpring(float value)
    {
        Value = value;
    }

    internal bool Step(float target)
    {
        float before = Value;
        velocity = (velocity + (target - Value) * 0.23f) * 0.70f;
        Value += velocity;
        if (Math.Abs(target - Value) < 0.002f && Math.Abs(velocity) < 0.002f)
        {
            Value = target;
            velocity = 0f;
        }
        return Math.Abs(Value - before) > 0.0001f;
    }

    internal void Kick(float amount)
    {
        velocity += amount;
    }
}
