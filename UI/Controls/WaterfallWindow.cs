using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace McPetOverlay;

internal sealed class WaterfallWindow : Panel
{
	private readonly Panel header = new Panel();

	private readonly Timer timer = new Timer
	{
		Interval = 15
	};

	private readonly int expandedBodyHeight;

	private int scalePercent = 100;

	private int cornerRadius = 12;

	private float progress = 1f;

	private float startProgress = 1f;

	private float targetProgress = 1f;

	private long startedAt;

	private Point dragOrigin;

	private Point panelOrigin;

	internal Panel Body { get; } = new Panel();

	internal int CornerRadius
	{
		get
		{
			return cornerRadius;
		}
		set
		{
			cornerRadius = Math.Clamp(value, 0, 48);
			ApplyHeight();
		}
	}

	internal void SetScalePercent(int percent)
	{
		percent = Math.Clamp(percent, 75, 150);
		if (percent != scalePercent)
		{
			float num = (float)percent / (float)scalePercent;
			scalePercent = percent;
			Scale(new SizeF(num, num));
			ApplyHeight();
		}
	}

	internal WaterfallWindow(string title, Color primary, Color text, Color bodyColor, int bodyHeight)
	{
		expandedBodyHeight = bodyHeight;
		BackColor = bodyColor;
		AutoScroll = false;
		DoubleBuffered = true;
		Margin = new Padding(0, 0, 0, 12);
		header.Dock = DockStyle.Top;
		header.Height = 34;
		header.BackColor = primary;
		header.Cursor = Cursors.Hand;
		Label label = new Label
		{
			Text = title,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Padding = new Padding(10, 0, 0, 0),
			ForeColor = text,
			Font = new Font("Segoe UI Semibold", 8.5f),
			Cursor = Cursors.Hand
		};
		Label arrow = new Label
		{
			Text = "▾",
			Dock = DockStyle.Right,
			Width = 34,
			TextAlign = ContentAlignment.MiddleCenter,
			ForeColor = text,
			Font = new Font("Segoe UI", 11f),
			Cursor = Cursors.Hand
		};
		header.Controls.Add(label);
		header.Controls.Add(arrow);
		base.Controls.Add(header);
		Body.Location = new Point(0, 34);
		Body.Width = 244;
		Body.Height = expandedBodyHeight;
		Body.BackColor = bodyColor;
		base.Controls.Add(Body);
		header.MouseDown += BeginMove;
		header.MouseMove += MovePanel;
		label.MouseDown += BeginMove;
		label.MouseMove += MovePanel;
		header.Click += Toggle;
		label.Click += Toggle;
		arrow.Click += Toggle;
		timer.Tick += delegate
		{
			double num = Math.Clamp((double)(Environment.TickCount64 - startedAt) / 280.0, 0.0, 1.0);
			double num2 = 1.0 - Math.Pow(1.0 - num, 3.0);
			progress = startProgress + (targetProgress - startProgress) * (float)num2;
			ApplyHeight();
			arrow.Text = ((progress > 0.5f) ? "▾" : "▸");
			if (num >= 1.0)
			{
				timer.Stop();
			}
		};
		ApplyHeight();
		void BeginMove(object? sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				dragOrigin = System.Windows.Forms.Cursor.Position;
				panelOrigin = base.Location;
				BringToFront();
			}
		}
		void MovePanel(object? sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left || base.Parent == null)
			{
				return;
			}
			if (base.Parent != null)
			{
				Point position = System.Windows.Forms.Cursor.Position;
				int value = panelOrigin.X + position.X - dragOrigin.X;
				int value2 = panelOrigin.Y + position.Y - dragOrigin.Y;
				base.Location = new Point(Math.Clamp(value, 0, Math.Max(0, base.Parent.ClientSize.Width - base.Width)), Math.Clamp(value2, 0, Math.Max(0, base.Parent.ClientSize.Height - 34)));
			}
		}
		void Toggle(object? sender, EventArgs e)
		{
			SetExpanded(targetProgress < 0.5f, animate: true);
		}
	}

	internal void SetExpanded(bool expanded, bool animate)
	{
		startProgress = progress;
		targetProgress = (expanded ? 1f : 0f);
		if (!animate)
		{
			progress = targetProgress;
			ApplyHeight();
		}
		else
		{
			startedAt = Environment.TickCount64;
			timer.Start();
		}
	}

	private void ApplyHeight()
	{
		float num = (float)scalePercent / 100f;
		int num2 = (int)Math.Round((float)expandedBodyHeight * num);
		int num3 = (int)Math.Round((float)num2 * progress);
		Body.Height = num2;
		base.Height = header.Height + num3;
		base.Region?.Dispose();
		if (cornerRadius <= 0)
		{
			base.Region = new Region(new Rectangle(0, 0, base.Width, base.Height));
		}
		else
		{
			using GraphicsPath path = CreateRoundedPath(new RectangleF(0f, 0f, Math.Max(1, base.Width), Math.Max(1, base.Height)), (float)cornerRadius * num);
			base.Region = new Region(path);
		}
		Invalidate();
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

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float radius = Math.Max(1f, cornerRadius * (float)scalePercent / 100f);
		using GraphicsPath path = CreateRoundedPath(new RectangleF(1f, 1f, Math.Max(1f, Width - 3f), Math.Max(1f, Height - 3f)), radius);
		using Pen pen = new Pen(Color.FromArgb(220, 208, 188, 255), 2f);
		e.Graphics.DrawPath(pen, path);
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
