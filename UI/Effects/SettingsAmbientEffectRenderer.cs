using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace McPetOverlay;

internal sealed class SettingsAmbientEffectRenderer : IDisposable
{
    private const string CodeGlyphs = "01{}[]<>/=+*-;:ifelseforwhilepublicprivatefloatintbool";
    private const string MathGlyphs = "∑∫√∞πθλΔ≈≠≤≥±×÷αβγφψΩ0123456789";

    private readonly Random random;
    private readonly List<Particle> particles = new List<Particle>();
    private readonly Font codeFont = new Font("Consolas", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font mathFont = new Font("Cambria Math", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
    private Size viewport;
    private int effectType = -1;
    private int density = -1;
    private long lastTick;

    internal SettingsAmbientEffectRenderer(int seed)
    {
        random = new Random(seed);
    }

    internal bool Advance(AppConfig config, Size size)
    {
        if (!config.GuiAmbientEffectEnabled || size.Width <= 0 || size.Height <= 0)
        {
            lastTick = 0L;
            return false;
        }

        EnsureParticles(config, size);
        long now = Stopwatch.GetTimestamp();
        if (lastTick == 0L)
        {
            lastTick = now;
            return true;
        }
        float elapsed = Math.Clamp((now - lastTick) / (float)Stopwatch.Frequency, 0f, 0.05f);
        lastTick = now;
        float speed = Math.Clamp(config.GuiAmbientEffectSpeedPercent, 25, 200) / 100f;
        foreach (Particle particle in particles)
        {
            if (effectType == 0)
            {
                particle.X += particle.Drift * elapsed * speed;
                particle.Y -= particle.Speed * elapsed * speed;
                if (particle.Y < -24f || particle.X > viewport.Width + 30f)
                    ResetParticle(particle, spawnAtBottom: true);
            }
            else
            {
                particle.X += particle.Drift * elapsed * speed * 0.28f;
                particle.Y += particle.Speed * elapsed * speed;
                if (particle.Y > viewport.Height + 24f)
                    ResetParticle(particle, spawnAtBottom: false);
            }
        }
        return true;
    }

    internal void Draw(Graphics graphics, AppConfig config, Size size)
    {
        if (!config.GuiAmbientEffectEnabled || particles.Count == 0 || size.Width <= 0 || size.Height <= 0)
            return;

        GraphicsState state = graphics.Save();
        graphics.SmoothingMode = config.AntiAliasingEnabled ? SmoothingMode.AntiAlias : SmoothingMode.None;
        Color accent = EpsilonTheme.Accent(config);
        float sizeScale = Math.Clamp(config.GuiAmbientEffectSizePercent, 50, 250) / 100f;
        if (effectType == 0)
        {
            Color gold = Color.FromArgb(255, 235, 166, 58);
            Color paleGold = Color.FromArgb(255, 255, 222, 128);
            foreach (Particle particle in particles)
            {
                float scaledSize = particle.Size * sizeScale;
                float length = scaledSize * 2.8f;
                Color lineColor = EpsilonTheme.Mix(gold, accent, particle.Tint);
                using Pen trail = new Pen(Color.FromArgb(particle.Alpha, lineColor), Math.Max(0.7f, scaledSize * 0.16f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                graphics.DrawLine(trail, particle.X - length * 0.28f, particle.Y + length, particle.X, particle.Y);
                using SolidBrush spark = new SolidBrush(Color.FromArgb(Math.Min(255, particle.Alpha + 45), paleGold));
                float dot = Math.Max(1.5f, scaledSize * 0.42f);
                graphics.FillEllipse(spark, particle.X - dot / 2f, particle.Y - dot / 2f, dot, dot);
            }
        }
        else
        {
            string glyphs = effectType == 1 ? CodeGlyphs : MathGlyphs;
            Font font = effectType == 1 ? codeFont : mathFont;
            Color baseColor = effectType == 1
                ? EpsilonTheme.Mix(accent, Color.FromArgb(120, 255, 190), 0.42f)
                : EpsilonTheme.Mix(accent, Color.FromArgb(255, 215, 112), 0.48f);
            foreach (Particle particle in particles)
            {
                char glyph = glyphs[particle.GlyphIndex % glyphs.Length];
                using SolidBrush brush = new SolidBrush(Color.FromArgb(particle.Alpha, baseColor));
                GraphicsState glyphState = graphics.Save();
                graphics.TranslateTransform(particle.X, particle.Y);
                graphics.ScaleTransform(sizeScale, sizeScale);
                graphics.DrawString(glyph.ToString(), font, brush, 0f, 0f, StringFormat.GenericTypographic);
                graphics.Restore(glyphState);
            }
        }
        graphics.Restore(state);
    }

    private void EnsureParticles(AppConfig config, Size size)
    {
        int newType = Math.Clamp(config.GuiAmbientEffectType, 0, 2);
        int newDensity = Math.Clamp(config.GuiAmbientEffectDensityPercent, 10, 100);
        int targetCount = Math.Clamp((int)Math.Round(14d + newDensity * 0.56d), 20, 70);
        if (viewport != size || effectType != newType || density != newDensity)
        {
            viewport = size;
            effectType = newType;
            density = newDensity;
            particles.Clear();
            for (int i = 0; i < targetCount; i++)
            {
                Particle particle = new Particle();
                ResetParticle(particle, spawnAtBottom: null);
                particles.Add(particle);
            }
            lastTick = 0L;
            return;
        }
        while (particles.Count < targetCount)
        {
            Particle particle = new Particle();
            ResetParticle(particle, spawnAtBottom: null);
            particles.Add(particle);
        }
        if (particles.Count > targetCount)
            particles.RemoveRange(targetCount, particles.Count - targetCount);
    }

    private void ResetParticle(Particle particle, bool? spawnAtBottom)
    {
        particle.X = (float)(random.NextDouble() * Math.Max(1, viewport.Width));
        if (spawnAtBottom == true)
            particle.Y = viewport.Height + (float)(random.NextDouble() * 32d);
        else if (spawnAtBottom == false)
            particle.Y = -18f - (float)(random.NextDouble() * 40d);
        else
            particle.Y = (float)(random.NextDouble() * Math.Max(1, viewport.Height));
        particle.Speed = effectType == 0 ? 12f + (float)random.NextDouble() * 18f : 8f + (float)random.NextDouble() * 16f;
        particle.Drift = effectType == 0 ? 4f + (float)random.NextDouble() * 9f : -4f + (float)random.NextDouble() * 8f;
        particle.Size = effectType == 0 ? 2.2f + (float)random.NextDouble() * 3.8f : 10f + (float)random.NextDouble() * 6f;
        particle.Alpha = effectType == 0 ? random.Next(92, 170) : random.Next(82, 158);
        particle.Tint = (float)random.NextDouble() * 0.30f;
        particle.GlyphIndex = random.Next(0, effectType == 1 ? CodeGlyphs.Length : MathGlyphs.Length);
    }

    public void Dispose()
    {
        codeFont.Dispose();
        mathFont.Dispose();
    }

    private sealed class Particle
    {
        internal float X;
        internal float Y;
        internal float Speed;
        internal float Drift;
        internal float Size;
        internal int Alpha;
        internal float Tint;
        internal int GlyphIndex;
    }
}
