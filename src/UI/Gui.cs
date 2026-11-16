using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Paranoidal
{
    /// <summary>A font of the window: family, size in device independent pixels, weight.</summary>
    sealed class FFont
    {
        public readonly FontFamily Family;
        public readonly double Size;
        public readonly FontWeight Weight;
        public readonly Typeface Face;

        public FFont(FontFamily family, double size, FontWeight weight = FontWeight.Normal)
        {
            Family = family;
            Size = size;
            Weight = weight;
            Face = new Typeface(family, FontStyle.Normal, weight);
        }

        public FFont WithWeight(FontWeight w) => new FFont(Family, Size, w);
    }

    enum HAlign { Left, Center, Right }

    /// <summary>
    /// The look of a vanilla Factorio window (1.5.0; workspace skill factorio-gui-design). Colours are sampled from the
    /// GUI atlas of Factorio 2.1.24 (data\core\graphics\gui-new.png at the positions of the vanilla styles in
    /// style.lua): the window frame, inside_shallow_frame panes, inside_deep_frame lists, the subheader band, grey
    /// buttons with black text that turn orange under the mouse, the green confirm button, the red one. Text colours
    /// are gui_color of style.lua (caption, orange, red). Spacing follows the 4/8/12 px grid; three text sizes.
    /// Sizes are device independent pixels: Avalonia scales them to the screen.
    /// </summary>
    static class Gui
    {
        // frames
        public static readonly Color Window = Color.FromRgb(45, 44, 45);       // frame
        public static readonly Color Pane = Color.FromRgb(62, 61, 62);         // inside_shallow_frame
        public static readonly Color PaneHi = Color.FromRgb(106, 104, 103);    // its top light edge
        public static readonly Color Edge = Color.FromRgb(17, 17, 17);         // dark outer edge of panes and buttons
        public static readonly Color Deep = Color.FromRgb(31, 30, 31);         // inside_deep_frame, subheader band
        public static readonly Color DeepEdge = Color.FromRgb(12, 12, 12);
        public static readonly Color Header = Color.FromRgb(49, 48, 49);       // list column headers
        public static Color Inset => Deep;
        // text (gui_color: caption, orange, red)
        public static readonly Color Text = Color.FromRgb(255, 255, 255);
        public static readonly Color Muted = Color.FromRgb(179, 179, 179);
        public static readonly Color Title = Color.FromRgb(255, 230, 192);
        public static readonly Color Orange = Color.FromRgb(250, 168, 56);
        public static readonly Color Green = Color.FromRgb(139, 215, 144);
        public static readonly Color Red = Color.FromRgb(255, 142, 142);
        public static readonly Color Yellow = Color.FromRgb(255, 212, 0);
        public static readonly Color Black = Color.FromRgb(0, 0, 0);
        // status dots (entity status icons: working / warning / not working)
        public static readonly Color DotGreen = Color.FromRgb(94, 183, 99);
        public static readonly Color DotOrange = Color.FromRgb(228, 153, 34);
        public static readonly Color DotRed = Color.FromRgb(254, 90, 90);
        public static readonly Color DotGrey = Color.FromRgb(143, 143, 143);

        // the fonts of 1.5.0 (Segoe UI on Windows; the same sizes with a fallback elsewhere). WinForms sizes were points:
        // 9.75 pt = 13 px, 12 pt = 16 px, 9 pt = 12 px, 14 pt = 18.67 px at 96 DPI.
        public static readonly FontFamily Sans = new FontFamily("Segoe UI, Noto Sans, DejaVu Sans, Liberation Sans, sans-serif");
        public static readonly FontFamily Mono = new FontFamily("Consolas, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace");
        public static readonly FontFamily Heavy = new FontFamily("Segoe UI Black, Noto Sans, DejaVu Sans, sans-serif");
        // three text sizes: headings, body, small (and the journal's monospace data)
        public static readonly FFont Heading = new FFont(Sans, 16, FontWeight.SemiBold);
        public static readonly FFont Body = new FFont(Sans, 13);
        public static readonly FFont Bold = new FFont(Sans, 13, FontWeight.SemiBold);
        public static readonly FFont Small = new FFont(Sans, 12);
        /// <summary>The title of the start window's header.</summary>
        public static readonly FFont Big = new FFont(Sans, 18.67, FontWeight.SemiBold);
        public static readonly FFont Code = new FFont(Mono, 12.67);
        public static readonly FFont CodeSmall = new FFont(Mono, 12);

        /// <summary>A size of the 96 DPI layout (Avalonia scales device independent pixels itself).</summary>
        public static double S(double v) => Math.Round(v);

        public static Color Mix(Color a, Color b, double t) =>
            Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        public static Color Light(Color c, double t) => Mix(c, Colors.White, t);
        public static Color Dark(Color c, double t) => Mix(c, Colors.Black, t);
        public static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

        public static IBrush Brush(Color c) => new ImmutableSolidColorBrush(c);
        public static IPen Pen(Color c, double w = 1) => new ImmutablePen(c.ToUInt32(), w);

        /// <summary>The colour of a status dot that goes with a text colour.</summary>
        public static Color DotFor(Color text) =>
            text == Green ? DotGreen : text == Orange ? DotOrange : text == Red ? DotRed : DotGrey;

        /// <summary>Vertical gradient (top → bottom) over a rectangle.</summary>
        public static IBrush VGradient(Color top, Color bottom) => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
        };

        /// <summary>An elliptic glow: the colour in the middle fading out to the edge of the ellipse.</summary>
        public static void Glow(DrawingContext g, Rect ellipse, Color center)
        {
            var b = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientStops = { new GradientStop(center, 0), new GradientStop(Color.FromArgb(0, center.R, center.G, center.B), 1) },
            };
            g.DrawEllipse(b, null, ellipse);
        }

        // ---- lines and rectangles on the pixel grid (1 px pens at half pixels, like GDI's DrawLine)

        public static void Fill(DrawingContext g, Color c, double x, double y, double w, double h)
        {
            if (w > 0 && h > 0) g.FillRectangle(Brush(c), new Rect(x, y, w, h));
        }

        public static void Fill(DrawingContext g, Color c, Rect r) => Fill(g, c, r.X, r.Y, r.Width, r.Height);

        /// <summary>A 1 px line from pixel (x1, y1) to pixel (x2, y2), both included (GDI DrawLine semantics).</summary>
        public static void Line(DrawingContext g, Color c, double x1, double y1, double x2, double y2)
        {
            if (x1 == x2) Fill(g, c, x1, Math.Min(y1, y2), 1, Math.Abs(y2 - y1) + 1);
            else if (y1 == y2) Fill(g, c, Math.Min(x1, x2), y1, Math.Abs(x2 - x1) + 1, 1);
            else g.DrawLine(Pen(c), new Point(x1 + .5, y1 + .5), new Point(x2 + .5, y2 + .5));
        }

        /// <summary>A 1 px outline inside the rectangle.</summary>
        public static void Outline(DrawingContext g, Color c, Rect r)
        {
            Line(g, c, r.X, r.Y, r.Right - 1, r.Y);
            Line(g, c, r.X, r.Bottom - 1, r.Right - 1, r.Bottom - 1);
            Line(g, c, r.X, r.Y, r.X, r.Bottom - 1);
            Line(g, c, r.Right - 1, r.Y, r.Right - 1, r.Bottom - 1);
        }

        /// <summary>Yellow/black hazard stripes, like the PARANOIDAL logo (only the header has them).</summary>
        public static void Hazard(DrawingContext g, Rect r, double offset, Color a, Color b)
        {
            Fill(g, b, r);
            double w = Math.Max(4, r.Height), step = w * 2;
            using (g.PushClip(r))
            {
                var geo = new StreamGeometry();
                using (var c = geo.Open())
                {
                    for (double x = r.Left - r.Height - step + (offset % step); x < r.Right + step; x += step)
                    {
                        c.BeginFigure(new Point(x, r.Bottom), true);
                        c.LineTo(new Point(x + r.Height, r.Top));
                        c.LineTo(new Point(x + r.Height + w, r.Top));
                        c.LineTo(new Point(x + w, r.Bottom));
                        c.EndFigure(true);
                    }
                }
                g.DrawGeometry(Brush(a), null, geo);
            }
        }

        /// <summary>
        /// A Factorio button face: a dark outline, a light top edge fading into the body, a dark bottom edge
        /// (inset: the pressed look, dark at the top).
        /// </summary>
        public static void Bevel(DrawingContext g, Rect r, Color top, Color body, Color bottom, bool inset)
        {
            var outer = new Rect(r.X, r.Y, r.Width, r.Height);
            g.DrawRectangle(Brush(Color.FromRgb(9, 9, 9)), null, outer, 3, 3);
            var inner = outer.Deflate(1);
            if (inner.Width <= 0 || inner.Height <= 0) return;
            using (g.PushClip(new RoundedRect(inner, 2)))
            {
                Fill(g, body, inner);
                double edge = Math.Max(3, Math.Floor(inner.Height * 3 / 10));
                var tr = new Rect(inner.X, inner.Y, inner.Width, edge);
                g.FillRectangle(VGradient(inset ? Dark(body, .55) : top, body), tr);
                Fill(g, inset ? Light(body, .1) : bottom, inner.X, inner.Bottom - 2, inner.Width, 2);
            }
        }

        /// <summary>A pane frame (inside_shallow_frame) or a deep one around a list (inside_deep_frame): an inset edge.</summary>
        public static void Frame(DrawingContext g, Rect r, bool deep)
        {
            var dark = deep ? DeepEdge : Edge;
            var light = deep ? Mix(Pane, PaneHi, .5) : Dark(Pane, .5);
            Line(g, dark, r.Left, r.Top, r.Right - 1, r.Top);
            Line(g, dark, r.Left, r.Top, r.Left, r.Bottom - 1);
            Line(g, light, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1);
            Line(g, light, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1);
        }

        /// <summary>A status dot (the game's entity status icons): a small sphere in the colour that goes with a text colour.</summary>
        public static void Dot(DrawingContext g, Rect dot, Color text)
        {
            var c = DotFor(text);
            g.DrawEllipse(VGradient(Light(c, .35), Dark(c, .2)), Pen(Dark(c, .6)), dot.Deflate(.5));
        }

        // ---- text

        public static FormattedText Format(string text, FFont f, Color color, double maxWidth = double.PositiveInfinity, bool wrap = false, bool ellipsis = true)
        {
            var ft = new FormattedText(text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, f.Face, f.Size, Brush(color));
            if (!double.IsInfinity(maxWidth) && maxWidth > 0)
            {
                ft.MaxTextWidth = maxWidth;
                if (!wrap) { ft.MaxLineCount = 1; if (ellipsis) ft.Trimming = TextTrimming.CharacterEllipsis; }
            }
            return ft;
        }

        /// <summary>Width of a single line of text (TextRenderer.MeasureText of WinForms adds a few pixels of padding).</summary>
        public static double TextWidth(string text, FFont f) =>
            string.IsNullOrEmpty(text) ? 0 : Math.Ceiling(Format(text, f, Text).WidthIncludingTrailingWhitespace) + 6;

        /// <summary>Height of text wrapped at a width.</summary>
        public static double TextHeight(string text, FFont f, double width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return Math.Ceiling(Format(text, f, Text, Math.Max(1, width), true).Height);
        }

        /// <summary>Text in a rectangle: one line with an ellipsis (vertically centred) or wrapped from the top.</summary>
        public static void DrawText(DrawingContext g, string text, FFont f, Rect r, Color color, HAlign h = HAlign.Left, bool vcenter = true, bool wrap = false)
        {
            if (string.IsNullOrEmpty(text) || r.Width <= 0) return;
            var ft = Format(text, f, color, r.Width, wrap);
            ft.TextAlignment = h == HAlign.Center ? TextAlignment.Center : h == HAlign.Right ? TextAlignment.Right : TextAlignment.Left;
            double y = vcenter && !wrap ? r.Y + (r.Height - ft.Height) / 2 : r.Y;
            using (g.PushClip(r)) g.DrawText(ft, new Point(r.X, Math.Round(y)));
        }
    }
}
