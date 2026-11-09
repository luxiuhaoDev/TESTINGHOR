using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace McPetOverlay;

internal sealed class HudEditorForm : Form
{
	private sealed record EditorItem(string Name, HudElementLayout Layout, Size Size);

	private readonly AppConfig config;
	private readonly Action saved;
	private readonly List<EditorItem> items;
	private EditorItem? dragging;
	private Point dragOffset;
	private readonly Dictionary<string, (PointF Position, bool AnchorRight)> original = new Dictionary<string, (PointF, bool)>();
	private bool accepted;
	private bool closeHandled;

	internal HudEditorForm(AppConfig config, Rectangle gameBounds, Action saved)
	{
		this.config = config;
		this.saved = saved;
		items = new List<EditorItem>
		{
			new EditorItem("功能列表", config.EnabledModulesHud, new Size(190, 190)),
			new EditorItem("目标显示", config.TargetHud, new Size(260, 118)),
			new EditorItem("附近目标", config.TargetListHud, new Size(230, 210))
		};
		foreach (EditorItem item in items)
		{
			original[item.Name] = (new PointF(item.Layout.X, item.Layout.Y), item.Layout.AnchorRight);
		}
		FormBorderStyle = FormBorderStyle.None;
		ShowInTaskbar = false;
		TopMost = true;
		KeyPreview = true;
		DoubleBuffered = true;
		Bounds = gameBounds;
		BackColor = Color.Black;
		Opacity = 0.96;
		Cursor = Cursors.SizeAll;
		MouseDown += OnEditorMouseDown;
		MouseMove += OnEditorMouseMove;
		MouseUp += delegate { dragging = null; };
		KeyDown += OnEditorKeyDown;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using SolidBrush veil = new SolidBrush(Color.FromArgb(150, 8, 9, 12));
		e.Graphics.FillRectangle(veil, ClientRectangle);
		using Font titleFont = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
		using Font itemFont = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
		using SolidBrush titleBrush = new SolidBrush(Color.White);
		e.Graphics.DrawString("HUD 编辑  拖动组件 · Enter 保存 · Esc 取消 · R 恢复默认", titleFont, titleBrush, 18f, 16f);
		foreach (EditorItem item in items)
		{
			Rectangle rect = GetItemBounds(item);
			(bool showBackground, int backgroundArgb, int opacity, int textArgb, int accentArgb, float radius) = GetPreviewStyle(item);
			using GraphicsPath path = RoundedRect(rect, radius);
			if (showBackground)
			{
				using SolidBrush fill = new SolidBrush(WithOpacity(backgroundArgb, opacity));
				e.Graphics.FillPath(fill, path);
				using Pen accent = new Pen(Color.FromArgb(accentArgb), 2f);
				e.Graphics.DrawPath(accent, path);
			}
			using SolidBrush itemText = new SolidBrush(Color.FromArgb(textArgb));
			e.Graphics.DrawString(item.Name, itemFont, itemText, rect.X + 10f, rect.Y + 8f);
			using Pen guide = new Pen(Color.FromArgb(190, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash };
			e.Graphics.DrawPath(guide, path);
			e.Graphics.DrawLine(guide, rect.X + 10, rect.Y + 34, rect.Right - 10, rect.Y + 34);
		}
	}

	private (bool ShowBackground, int BackgroundArgb, int Opacity, int TextArgb, int AccentArgb, float Radius) GetPreviewStyle(EditorItem item)
	{
		return item.Name switch
		{
			"功能列表" => (config.EnabledModulesHudShowBackground, config.EnabledModulesHudBackgroundColorArgb, config.EnabledModulesHudBackgroundOpacityPercent, config.EnabledModulesHudTextColorArgb, config.EnabledModulesHudAccentColorArgb, config.EnabledModulesHudCornerRadius),
			"目标显示" => (config.TargetHudShowBackground, config.TargetHudBackgroundColorArgb, config.TargetHudBackgroundOpacityPercent, config.TargetHudTextColorArgb, config.TargetHudAccentColorArgb, config.TargetHudCornerRadius),
			_ => (config.EspTargetListShowBackground, config.EspTargetListBackgroundColorArgb, config.EspTargetListBackgroundOpacityPercent, config.EspTargetListColorArgb, config.EspTargetListAccentColorArgb, 7f)
		};
	}

	private static Color WithOpacity(int argb, int opacityPercent)
	{
		Color color = Color.FromArgb(argb);
		return Color.FromArgb(Math.Clamp(opacityPercent, 0, 100) * 255 / 100, color.R, color.G, color.B);
	}

	private void OnEditorMouseDown(object? sender, MouseEventArgs e)
	{
		for (int i = items.Count - 1; i >= 0; i--)
		{
			Rectangle bounds = GetItemBounds(items[i]);
			if (bounds.Contains(e.Location))
			{
				dragging = items[i];
				dragOffset = new Point(e.X - bounds.X, e.Y - bounds.Y);
				return;
			}
		}
	}

	private void OnEditorMouseMove(object? sender, MouseEventArgs e)
	{
		if (dragging == null || e.Button != MouseButtons.Left)
		{
			return;
		}
		int x = Math.Clamp(e.X - dragOffset.X, 0, Math.Max(0, ClientSize.Width - dragging.Size.Width));
		int y = Math.Clamp(e.Y - dragOffset.Y, 48, Math.Max(48, ClientSize.Height - dragging.Size.Height));
		if (Math.Abs(x) < 8) x = 0;
		if (Math.Abs(ClientSize.Width - (x + dragging.Size.Width)) < 8) x = ClientSize.Width - dragging.Size.Width;
		if (Math.Abs(y - 48) < 8) y = 48;
		dragging.Layout.AnchorRight = x + dragging.Size.Width / 2 > ClientSize.Width / 2;
		dragging.Layout.X = dragging.Layout.AnchorRight ? (float)(x + dragging.Size.Width) / ClientSize.Width : (float)x / ClientSize.Width;
		dragging.Layout.Y = (float)y / ClientSize.Height;
		Invalidate();
	}

	private void OnEditorKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Enter)
		{
			accepted = true;
			closeHandled = true;
			config.Save();
			Close();
		}
		else if (e.KeyCode == Keys.Escape)
		{
			RestoreOriginalLayout();
			closeHandled = true;
			Close();
		}
		else if (e.KeyCode == Keys.R)
		{
			config.EnabledModulesHud.X = 0.985f;
			config.EnabledModulesHud.Y = 0.28f;
			config.EnabledModulesHud.AnchorRight = true;
			config.TargetHud.X = 0.5f;
			config.TargetHud.Y = 0.72f;
			config.TargetHud.AnchorRight = false;
			config.TargetListHud.X = 0.82f;
			config.TargetListHud.Y = 0.05f;
			config.TargetListHud.AnchorRight = true;
			Invalidate();
		}
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		if (!accepted && !closeHandled)
		{
			RestoreOriginalLayout();
		}
		base.OnFormClosing(e);
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		saved();
		base.OnFormClosed(e);
	}

	private void RestoreOriginalLayout()
	{
		foreach (EditorItem item in items)
		{
			(PointF position, bool anchorRight) = original[item.Name];
			item.Layout.X = position.X;
			item.Layout.Y = position.Y;
			item.Layout.AnchorRight = anchorRight;
		}
	}

	private Rectangle GetItemBounds(EditorItem item)
	{
		int anchorX = (int)Math.Round(item.Layout.X * ClientSize.Width);
		int x = item.Layout.AnchorRight ? anchorX - item.Size.Width : anchorX;
		int y = (int)Math.Round(item.Layout.Y * ClientSize.Height);
		return new Rectangle(Math.Clamp(x, 0, Math.Max(0, ClientSize.Width - item.Size.Width)), Math.Clamp(y, 48, Math.Max(48, ClientSize.Height - item.Size.Height)), item.Size.Width, item.Size.Height);
	}

	private static GraphicsPath RoundedRect(Rectangle rect, float radius)
	{
		GraphicsPath path = new GraphicsPath();
		float clampedRadius = Math.Clamp(radius, 0f, Math.Min(rect.Width, rect.Height) / 2f);
		if (clampedRadius < 0.5f)
		{
			path.AddRectangle(rect);
			return path;
		}
		float diameter = clampedRadius * 2f;
		path.AddArc(rect.X, rect.Y, diameter, diameter, 180f, 90f);
		path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270f, 90f);
		path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
		path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
		path.CloseFigure();
		return path;
	}
}
