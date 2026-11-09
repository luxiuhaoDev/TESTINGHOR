using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace McPetOverlay;

internal sealed class EpsilonToggle : Control
{
	private readonly Timer timer = new Timer
	{
		Interval = 15
	};

	private float progress;

	private float target;

	private bool isChecked;

	internal Color AccentColor { get; set; } = Color.FromArgb(208, 188, 255);

	internal bool Checked
	{
		get
		{
			return isChecked;
		}
		set
		{
			if (isChecked == value)
				return;
			isChecked = value;
			target = value ? 1f : 0f;
			timer.Start();
		}
	}

	internal event EventHandler? CheckedChanged;

	internal EpsilonToggle()
	{
		base.Size = new Size(42, 22);
		Cursor = Cursors.Hand;
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
		timer.Tick += delegate
		{
			progress += (target - progress) * 0.24f;
			if (Math.Abs(target - progress) < 0.004f)
			{
				progress = target;
				timer.Stop();
			}
			Invalidate();
		};
	}

	protected override void OnClick(EventArgs e)
	{
		isChecked = !isChecked;
		target = (isChecked ? 1f : 0f);
		timer.Start();
		CheckedChanged?.Invoke(this, EventArgs.Empty);
		base.OnClick(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float eased = progress * progress * (3f - 2f * progress);
		Color color = Blend(Color.FromArgb(74, 68, 82), AccentColor, eased);
		using GraphicsPath path = Rounded(new RectangleF(0f, 1f, base.Width - 1, base.Height - 2), (float)base.Height / 2f);
		using SolidBrush brush = new SolidBrush(color);
		e.Graphics.FillPath(brush, path);
		float baseSize = base.Height - 6;
		float size = baseSize + (float)Math.Sin(Math.PI * progress) * 1.4f;
		float centerX = 3f + baseSize / 2f + ((float)base.Width - baseSize - 6f) * eased;
		float centerY = base.Height / 2f;
		using SolidBrush brush2 = new SolidBrush(Color.White);
		e.Graphics.FillEllipse(brush2, centerX - size / 2f, centerY - size / 2f, size, size);
	}

	private static Color Blend(Color a, Color b, float t)
	{
		return Color.FromArgb((int)((float)(int)a.A + (float)(b.A - a.A) * t), (int)((float)(int)a.R + (float)(b.R - a.R) * t), (int)((float)(int)a.G + (float)(b.G - a.G) * t), (int)((float)(int)a.B + (float)(b.B - a.B) * t));
	}

	private static GraphicsPath Rounded(RectangleF rectangle, float radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		float num = radius * 2f;
		graphicsPath.AddArc(rectangle.X, rectangle.Y, num, num, 90f, 180f);
		graphicsPath.AddArc(rectangle.Right - num, rectangle.Y, num, num, 270f, 180f);
		graphicsPath.AddArc(rectangle.Right - num, rectangle.Bottom - num, num, num, 0f, 180f);
		graphicsPath.AddArc(rectangle.X, rectangle.Bottom - num, num, num, 90f, 180f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	private static GraphicsPath CreateRoundedPath(RectangleF rectangle, float radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		radius = Math.Clamp(radius, 1f, Math.Min(rectangle.Width, rectangle.Height) / 2f);
		float num = radius * 2f;
		graphicsPath.AddArc(rectangle.X, rectangle.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(rectangle.Right - num, rectangle.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(rectangle.Right - num, rectangle.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(rectangle.X, rectangle.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			timer.Dispose();
		}
		base.Dispose(disposing);
	}
}
