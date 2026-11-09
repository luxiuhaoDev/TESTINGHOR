using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace McPetOverlay;

internal sealed class StartupAnimationForm : Form
{
	private const double DurationSeconds = 6.4;
	private const float DesignWidth = 1280f;
	private const float DesignHeight = 720f;

	private readonly System.Windows.Forms.Timer animationTimer = new System.Windows.Forms.Timer { Interval = 16 };
	private readonly Stopwatch clock = new Stopwatch();
	private readonly Particle[] particles;
	private readonly Font titleFont = new Font("Segoe Print", 104f, FontStyle.Bold, GraphicsUnit.Pixel);
	private readonly Font subtitleFont = new Font("Segoe Print", 19f, FontStyle.Regular, GraphicsUnit.Pixel);
	private readonly Font microFont = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
	private bool cursorHidden;

	internal StartupAnimationForm()
	{
		FormBorderStyle = FormBorderStyle.None;
		StartPosition = FormStartPosition.Manual;
		BackColor = Color.Black;
		ShowInTaskbar = false;
		TopMost = true;
		KeyPreview = true;
		DoubleBuffered = true;
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

		Random random = new Random(404);
		particles = Enumerable.Range(0, 55).Select(_ => new Particle(
			100f + (float)random.NextDouble() * 1080f,
			180f + (float)random.NextDouble() * 350f,
			0.5f + (float)random.NextDouble() * 1.4f,
			4f + (float)random.NextDouble() * 11f,
			(float)random.NextDouble() * MathF.PI * 2f,
			-5f + (float)random.NextDouble() * 10f)).ToArray();

		Shown += OnAnimationShown;
		FormClosed += OnAnimationClosed;
		KeyDown += OnAnimationKeyDown;
		animationTimer.Tick += Animate;
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if (keyData == Keys.Escape || keyData == Keys.Space || keyData == Keys.Enter)
		{
			Close();
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		Graphics graphics = e.Graphics;
		graphics.Clear(Color.Black);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

		float scale = Math.Min(ClientSize.Width / DesignWidth, ClientSize.Height / DesignHeight);
		float offsetX = (ClientSize.Width - DesignWidth * scale) / 2f;
		float offsetY = (ClientSize.Height - DesignHeight * scale) / 2f;
		GraphicsState state = graphics.Save();
		graphics.TranslateTransform(offsetX, offsetY);
		graphics.ScaleTransform(scale, scale);
		DrawAnimation(graphics, clock.Elapsed.TotalSeconds);
		graphics.Restore(state);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			animationTimer.Dispose();
			titleFont.Dispose();
			subtitleFont.Dispose();
			microFont.Dispose();
		}
		base.Dispose(disposing);
	}

	private void OnAnimationShown(object? sender, EventArgs e)
	{
		Screen screen = Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position);
		Bounds = screen.Bounds;
		BringToFront();
		Activate();
		Cursor.Hide();
		cursorHidden = true;
		clock.Restart();
		animationTimer.Start();
	}

	private void OnAnimationClosed(object? sender, FormClosedEventArgs e)
	{
		animationTimer.Stop();
		if (cursorHidden)
		{
			Cursor.Show();
			cursorHidden = false;
		}
	}

	private void OnAnimationKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
		{
			Close();
		}
	}

	private void Animate(object? sender, EventArgs e)
	{
		if (clock.Elapsed.TotalSeconds >= DurationSeconds)
		{
			Close();
			return;
		}
		Invalidate();
	}

	private void DrawAnimation(Graphics graphics, double time)
	{
		float globalIn = Smooth((float)((time - 0.35) / 0.75));
		float globalOut = 1f - Smooth((float)((time - 5.65) / 0.65));
		float global = globalIn * globalOut;
		if (global <= 0.001f)
		{
			return;
		}

		DrawGlow(graphics, new PointF(DesignWidth / 2f, 335f), 430f, 125f, Color.FromArgb((int)(30 * global), 0, 68, 88));
		DrawGlow(graphics, new PointF(DesignWidth / 2f, 335f), 245f, 80f, Color.FromArgb((int)(27 * global), 0, 142, 172));

		foreach (Particle particle in particles)
		{
			float phase = particle.Phase + (float)time * 0.38f;
			float x = particle.X + MathF.Sin(phase) * particle.Drift;
			float y = particle.Y - ((float)time * particle.Speed % 380f);
			if (y < 145f)
			{
				y += 380f;
			}
			float twinkle = 0.25f + 0.75f * (0.5f + 0.5f * MathF.Sin(phase * 2.1f));
			using SolidBrush particleBrush = new SolidBrush(Color.FromArgb((int)(38f * twinkle * global), 85, 217, 238));
			graphics.FillEllipse(particleBrush, x, y, particle.Radius, particle.Radius);
		}

		DrawHandwrittenTitle(graphics, "TESTING HOR", DesignWidth / 2f, 270f, 5f, time, global);

		float subtitleProgress = EaseOutCubic(Clamp((float)((time - 3.0) / 0.85)));
		DrawSubtitle(graphics, "DESKTOP PET FEATURES", DesignWidth / 2f, 414f, 3.8f, subtitleProgress, global);

		float titleProgress = Clamp((float)((time - 1.05) / 2.05));
		if (titleProgress > 0f && titleProgress < 1f)
		{
			float nibX = 270f + EaseInOutCubic(titleProgress) * 740f;
			float nibY = 330f + MathF.Sin(titleProgress * 11f) * 3f;
			DrawGlow(graphics, new PointF(nibX, nibY), 44f, 13f, Color.FromArgb((int)(85 * global), 104, 240, 255));
			using Pen nibPen = new Pen(Color.FromArgb((int)(155 * global), 210, 255, 255), 1.2f);
			graphics.DrawEllipse(nibPen, nibX - 3f, nibY - 3f, 6f, 6f);
		}

		float lineProgress = EaseOutCubic(Clamp((float)((time - 1.25) / 1.15)));
		using (Pen linePen = new Pen(Color.FromArgb((int)(65 * global), 55, 224, 240), 1f))
		{
			float lineWidth = 390f * lineProgress;
			graphics.DrawLine(linePen, DesignWidth / 2f - lineWidth, 445f, DesignWidth / 2f - 70f, 445f);
			graphics.DrawLine(linePen, DesignWidth / 2f + 70f, 445f, DesignWidth / 2f + lineWidth, 445f);
		}

		float microProgress = Smooth((float)((time - 3.75) / 0.8));
		using SolidBrush microBrush = new SolidBrush(Color.FromArgb((int)(75 * microProgress * global), 146, 190, 203));
		const string microText = "HANDWRITING EDITION";
		SizeF microSize = graphics.MeasureString(microText, microFont);
		graphics.DrawString(microText, microFont, microBrush, DesignWidth / 2f - microSize.Width / 2f, 477f);
	}

	private void DrawHandwrittenTitle(Graphics graphics, string text, float centerX, float y, float tracking, double time, float global)
	{
		float[] widths = text.Select(character => graphics.MeasureString(character.ToString(), titleFont, PointF.Empty, StringFormat.GenericTypographic).Width).ToArray();
		float total = widths.Sum() + tracking * (text.Length - 1);
		float x = centerX - total / 2f;

		for (int index = 0; index < text.Length; index++)
		{
			char character = text[index];
			float revealStart = (float)(1.05 + index * 0.08);
			float reveal = EaseInOutCubic(Clamp((float)((time - revealStart) / 0.38)));
			if (character != ' ' && reveal > 0f)
			{
				GraphicsState state = graphics.Save();
				graphics.SetClip(new RectangleF(x, y - 4f, widths[index] * reveal, 140f));
				using SolidBrush bleedBrush = new SolidBrush(Color.FromArgb((int)(45 * reveal * global), 0, 198, 230));
				using SolidBrush inkBrush = new SolidBrush(Color.FromArgb((int)(250 * reveal * global), 225, 252, 255));
				graphics.DrawString(character.ToString(), titleFont, bleedBrush, x + 2f, y + 1f, StringFormat.GenericTypographic);
				graphics.DrawString(character.ToString(), titleFont, inkBrush, x, y, StringFormat.GenericTypographic);
				graphics.Restore(state);
			}
			x += widths[index] + tracking;
		}
	}

	private void DrawSubtitle(Graphics graphics, string text, float centerX, float y, float tracking, float progress, float global)
	{
		float[] widths = text.Select(character => graphics.MeasureString(character.ToString(), subtitleFont, PointF.Empty, StringFormat.GenericTypographic).Width).ToArray();
		float total = widths.Sum() + tracking * (text.Length - 1);
		float x = centerX - total / 2f;
		for (int index = 0; index < text.Length; index++)
		{
			float characterProgress = Clamp(progress * text.Length - index);
			if (characterProgress > 0f && text[index] != ' ')
			{
				using SolidBrush subtitleBrush = new SolidBrush(Color.FromArgb((int)(215 * characterProgress * global), 177, 237, 244));
				graphics.DrawString(text[index].ToString(), subtitleFont, subtitleBrush, x, y + (1f - characterProgress) * 3f, StringFormat.GenericTypographic);
			}
			x += widths[index] + tracking;
		}
	}

	private static void DrawGlow(Graphics graphics, PointF center, float radius, float coreRadius, Color color)
	{
		for (int index = 9; index >= 1; index--)
		{
			float progress = index / 9f;
			float currentRadius = coreRadius + (radius - coreRadius) * progress;
			int alpha = (int)(color.A * (1f - progress) * 0.4f);
			using SolidBrush glowBrush = new SolidBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
			graphics.FillEllipse(glowBrush, center.X - currentRadius, center.Y - currentRadius * 0.46f, currentRadius * 2f, currentRadius * 0.92f);
		}
	}

	private static float Clamp(float value) => Math.Clamp(value, 0f, 1f);

	private static float Smooth(float value)
	{
		value = Clamp(value);
		return value * value * (3f - 2f * value);
	}

	private static float EaseOutCubic(float value)
	{
		value = Clamp(value);
		float inverse = 1f - value;
		return 1f - inverse * inverse * inverse;
	}

	private static float EaseInOutCubic(float value)
	{
		value = Clamp(value);
		return value < 0.5f ? 4f * value * value * value : 1f - (float)Math.Pow(-2f * value + 2f, 3d) / 2f;
	}

	private readonly record struct Particle(float X, float Y, float Radius, float Speed, float Phase, float Drift);
}