using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Paranoidal
{
    /// <summary>
    /// The look of a vanilla Factorio window (1.5.0; workspace skill factorio-gui-design). Colours are sampled from the
    /// GUI atlas of Factorio 2.1.24 (data\core\graphics\gui-new.png at the positions of the vanilla styles in
    /// style.lua): the window frame, inside_shallow_frame panes, inside_deep_frame lists, the subheader band, grey
    /// buttons with black text that turn orange under the mouse, the green confirm button, the red one. Text colours
    /// are gui_color of style.lua (caption, orange, red). Spacing follows the 4/8/12 px grid; three text sizes.
    /// </summary>
    static class Theme
    {
        // frames
        public static readonly Color Window = Color.FromArgb(45, 44, 45);       // frame
        public static readonly Color Pane = Color.FromArgb(62, 61, 62);         // inside_shallow_frame
        public static readonly Color PaneHi = Color.FromArgb(106, 104, 103);    // its top light edge
        public static readonly Color Edge = Color.FromArgb(17, 17, 17);         // dark outer edge of panes and buttons
        public static readonly Color Deep = Color.FromArgb(31, 30, 31);         // inside_deep_frame, subheader band
        public static readonly Color DeepEdge = Color.FromArgb(12, 12, 12);
        public static readonly Color Header = Color.FromArgb(49, 48, 49);       // list column headers
        // aliases of the older names
        public static Color Card => Pane;
        public static Color CardBorder => Edge;
        public static Color CardHi => PaneHi;
        public static Color Inset => Deep;
        // text (gui_color: caption, orange, red)
        public static readonly Color Text = Color.FromArgb(255, 255, 255);
        public static readonly Color Muted = Color.FromArgb(179, 179, 179);
        public static readonly Color Title = Color.FromArgb(255, 230, 192);
        public static readonly Color Orange = Color.FromArgb(250, 168, 56);
        public static readonly Color Green = Color.FromArgb(139, 215, 144);
        public static readonly Color Red = Color.FromArgb(255, 142, 142);
        public static readonly Color Yellow = Color.FromArgb(255, 212, 0);
        // status dots (entity status icons: working / warning / not working)
        public static readonly Color DotGreen = Color.FromArgb(94, 183, 99);
        public static readonly Color DotOrange = Color.FromArgb(228, 153, 34);
        public static readonly Color DotRed = Color.FromArgb(254, 90, 90);
        public static readonly Color DotGrey = Color.FromArgb(143, 143, 143);

        // three text sizes: headings, body, small (and the journal's monospace data)
        public static readonly Font Heading = new Font("Segoe UI Semibold", 12f);
        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 9f);
        /// <summary>The title of the start window's header.</summary>
        public static readonly Font Big = new Font("Segoe UI Semibold", 14f);

        public static float Scale = 1f;
        public static int S(float v) => (int)Math.Round(v * Scale);

        public static Color Mix(Color a, Color b, float t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        public static Color Light(Color c, float t) => Mix(c, Color.White, t);
        public static Color Dark(Color c, float t) => Mix(c, Color.Black, t);

        /// <summary>The colour of a status dot that goes with a text colour.</summary>
        public static Color DotFor(Color text) =>
            text == Green ? DotGreen : text == Orange ? DotOrange : text == Red ? DotRed : DotGrey;

        /// <summary>Yellow/black hazard stripes, like the PARANOIDAL logo (only the header has them).</summary>
        public static void Hazard(Graphics g, Rectangle r, int offset, Color a, Color b)
        {
            using (var bb = new SolidBrush(b)) g.FillRectangle(bb, r);
            int w = Math.Max(4, r.Height), step = w * 2;
            var old = g.Clip;
            g.SetClip(r);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var ab = new SolidBrush(a))
                for (int x = r.Left - r.Height - step + (offset % step); x < r.Right + step; x += step)
                    g.FillPolygon(ab, new[] { new Point(x, r.Bottom), new Point(x + r.Height, r.Top), new Point(x + r.Height + w, r.Top), new Point(x + w, r.Bottom) });
            g.SmoothingMode = SmoothingMode.Default;
            g.Clip = old;
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>
        /// A Factorio button face: a dark outline, a light top edge fading into the body, a dark bottom edge
        /// (inset: the pressed look, dark at the top).
        /// </summary>
        public static void Bevel(Graphics g, Rectangle r, Color top, Color body, Color bottom, bool inset)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var outer = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (var path = Round(outer, S(3)))
            {
                using (var b = new SolidBrush(Color.FromArgb(9, 9, 9))) g.FillPath(b, path);
                var inner = Rectangle.Inflate(outer, -1, -1);
                using (var ip = Round(inner, S(2)))
                {
                    var old = g.Clip;
                    g.SetClip(ip, CombineMode.Intersect);
                    using (var b = new SolidBrush(body)) g.FillRectangle(b, inner);
                    int edge = Math.Max(3, inner.Height * 3 / 10);
                    var tr = new Rectangle(inner.X, inner.Y, inner.Width, edge);
                    using (var b = new LinearGradientBrush(new Rectangle(tr.X, tr.Y - 1, tr.Width, tr.Height + 2), inset ? Dark(body, .55f) : top, body, 90f))
                        g.FillRectangle(b, tr);
                    using (var b = new SolidBrush(inset ? Light(body, .1f) : bottom))
                        g.FillRectangle(b, inner.X, inner.Bottom - S(2), inner.Width, S(2));
                    g.Clip = old;
                }
            }
            g.SmoothingMode = SmoothingMode.Default;
        }

        /// <summary>A pane frame (inside_shallow_frame) or a deep one around a list (inside_deep_frame): an inset edge.</summary>
        public static void Frame(Graphics g, Rectangle r, bool deep)
        {
            using (var p = new Pen(deep ? DeepEdge : Edge)) { g.DrawLine(p, r.Left, r.Top, r.Right - 1, r.Top); g.DrawLine(p, r.Left, r.Top, r.Left, r.Bottom - 1); }
            using (var p = new Pen(deep ? Mix(Pane, PaneHi, .5f) : Dark(Pane, .5f))) { g.DrawLine(p, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1); g.DrawLine(p, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1); }
        }

        /// <summary>Tooltips drawn like the game's: dark box, light border, white text, wrapped at a readable width.</summary>
        public static void DarkTips(ToolTip t)
        {
            t.OwnerDraw = true;
            t.Popup += (s, e) =>
            {
                var text = t.GetToolTip(e.AssociatedControl);
                if (string.IsNullOrEmpty(text)) { e.Cancel = true; return; }
                var size = TextRenderer.MeasureText(text, Body, new Size(S(620), int.MaxValue), TextFormatFlags.WordBreak);
                e.ToolTipSize = new Size(size.Width + S(20), size.Height + S(14));
            };
            t.Draw += (s, e) =>
            {
                using (var b = new SolidBrush(Color.FromArgb(36, 35, 36))) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Color.FromArgb(128, 126, 124))) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, Body, Rectangle.Inflate(e.Bounds, -S(10), -S(7)), Text, TextFormatFlags.WordBreak | TextFormatFlags.Left);
            };
        }
    }

    /// <summary>Normal: grey (button); Confirm: green (confirm_button); Danger: red (red_button); Tab: tab of a tabbed pane.</summary>
    enum ButtonKind { Normal, Confirm, Danger, Tab }

    /// <summary>A Factorio button: black text on grey (green, red), orange under the mouse; tabs as in a tabbed pane.</summary>
    sealed class FButton : Control
    {
        ButtonKind kind;
        /// <summary>A picture drawn left of the text (the Discord button).</summary>
        public Image Icon;
        bool hover, down, check;

        public bool Checked { get { return check; } set { check = value; Invalidate(); } }
        public ButtonKind Kind { get { return kind; } set { if (kind != value) { kind = value; Invalidate(); } } }

        public FButton(string text, ButtonKind kind, bool big = false)
        {
            Text = text;
            this.kind = kind;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = big ? Theme.Heading : Theme.Bold;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            if (kind == ButtonKind.Tab) { PaintTab(g, r); return; }
            Color top, body, bottom;
            bool inset = false;
            Color fore = Color.Black;
            if (!Enabled) { top = Color.FromArgb(45, 45, 45); body = Color.FromArgb(58, 58, 58); bottom = Color.FromArgb(78, 76, 75); fore = Theme.Muted; inset = true; }
            else if (down)
            {
                inset = true;
                body = kind == ButtonKind.Confirm ? Color.FromArgb(208, 224, 148) : kind == ButtonKind.Danger ? Color.FromArgb(252, 169, 169) : Color.FromArgb(241, 191, 100);
                top = bottom = body;
            }
            else if (hover)
            {
                if (kind == ButtonKind.Confirm) { top = Color.FromArgb(161, 252, 167); body = Color.FromArgb(147, 233, 152); bottom = Color.FromArgb(36, 91, 40); }
                else if (kind == ButtonKind.Danger) { top = Color.FromArgb(254, 162, 162); body = Color.FromArgb(255, 156, 156); bottom = Color.FromArgb(95, 48, 48); }
                else { top = Color.FromArgb(247, 203, 128); body = Color.FromArgb(228, 153, 34); bottom = Color.FromArgb(127, 80, 13); }
            }
            else if (kind == ButtonKind.Confirm) { top = Color.FromArgb(139, 215, 144); body = Color.FromArgb(94, 183, 99); bottom = Color.FromArgb(20, 63, 24); }
            else if (kind == ButtonKind.Danger) { top = Color.FromArgb(251, 131, 131); body = Color.FromArgb(254, 90, 90); bottom = Color.FromArgb(92, 25, 25); }
            else { top = Color.FromArgb(201, 201, 201); body = Color.FromArgb(143, 143, 143); bottom = Color.FromArgb(50, 50, 50); }
            Theme.Bevel(g, r, top, body, bottom, inset);

            var tr = r;
            if (down) tr.Offset(0, 1);
            if (Icon != null)
            {
                int gap = Theme.S(7), tw = TextRenderer.MeasureText(Text, Font).Width;
                int x = Math.Max(Theme.S(6), (r.Width - Icon.Width - gap - tw) / 2);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (Enabled) g.DrawImage(Icon, x, tr.Top + (r.Height - Icon.Height) / 2, Icon.Width, Icon.Height);
                else ControlPaint.DrawImageDisabled(g, Icon, x, tr.Top + (r.Height - Icon.Height) / 2, body);
                tr = new Rectangle(x + Icon.Width + gap, tr.Top, Math.Max(1, r.Right - x - Icon.Width - gap), tr.Height);
                TextRenderer.DrawText(g, Text, Font, tr, fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }
            TextRenderer.DrawText(g, Text, Font, tr, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        /// <summary>The selected tab has the colour of the pane under it and the caption colour; the others are grey buttons.</summary>
        void PaintTab(Graphics g, Rectangle r)
        {
            if (check)
            {
                using (var b = new SolidBrush(Theme.Pane)) g.FillRectangle(b, r);
                using (var p = new Pen(Theme.PaneHi)) g.DrawLine(p, 1, 0, r.Width - 2, 0);
                using (var p = new Pen(Theme.Edge)) { g.DrawLine(p, 0, 0, 0, r.Height - 1); g.DrawLine(p, r.Width - 1, 0, r.Width - 1, r.Height - 1); }
                TextRenderer.DrawText(g, Text, Font, r, Theme.Title, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }
            var face = new Rectangle(r.X, r.Y + Theme.S(3), r.Width, r.Height - Theme.S(3));
            if (hover) Theme.Bevel(g, face, Color.FromArgb(247, 203, 128), Color.FromArgb(230, 155, 36), Color.FromArgb(129, 85, 16), false);
            else Theme.Bevel(g, face, Color.FromArgb(201, 201, 201), Color.FromArgb(143, 143, 143), Color.FromArgb(79, 79, 79), false);
            TextRenderer.DrawText(g, Text, Font, face, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>
    /// A pane (inside_shallow_frame) with an optional subheader band: its caption in the caption colour and a short note
    /// on the right. Child controls marked with Tag = Card.DeepTag get a deep frame drawn around them.
    /// </summary>
    sealed class Card : Panel
    {
        public const string DeepTag = "deep";
        public string Title;
        public string Note;
        public Color NoteColor = Theme.Muted;

        public Card(string title)
        {
            Title = title;
            BackColor = Theme.Pane;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        /// <summary>Height of the subheader band (0 without a caption).</summary>
        public int Band => string.IsNullOrEmpty(Title) ? 0 : Theme.S(32);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var p = new Pen(Theme.PaneHi)) g.DrawLine(p, 1, 0, r.Width - 2, 0);
            using (var p = new Pen(Theme.Edge))
            {
                g.DrawLine(p, 0, 0, 0, r.Height - 1);
                g.DrawLine(p, r.Width - 1, 0, r.Width - 1, r.Height - 1);
                g.DrawLine(p, 0, r.Height - 1, r.Width - 1, r.Height - 1);
            }
            if (Band > 0)
            {
                var band = new Rectangle(1, 1, r.Width - 2, Band - 1);
                using (var b = new SolidBrush(Theme.Deep)) g.FillRectangle(b, band);
                using (var p = new Pen(Color.Black)) g.DrawLine(p, 1, Band, r.Width - 2, Band);
                var noteW = string.IsNullOrEmpty(Note) ? 0 : TextRenderer.MeasureText(Note, Theme.Small).Width + Theme.S(12);
                TextRenderer.DrawText(g, Title, Theme.Bold, new Rectangle(Theme.S(12), 1, Math.Max(1, r.Width - Theme.S(24) - noteW), Band - 1), Theme.Title,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (noteW > 0)
                    TextRenderer.DrawText(g, Note, Theme.Small, new Rectangle(r.Width - Theme.S(12) - noteW, 1, noteW, Band - 1), NoteColor,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
            foreach (Control c in Controls)
                if (c.Visible && Equals(c.Tag, DeepTag)) Theme.Frame(g, Rectangle.Inflate(c.Bounds, 1, 1), true);
        }
    }

    /// <summary>A status line: a coloured dot (like the game's entity status) and the text; the colour never stands alone.</summary>
    sealed class StatusLine : Control
    {
        Color color = Theme.Muted;
        public Color Color { get { return color; } set { color = value; Invalidate(); } }
        /// <summary>No dot: a plain line of secondary text.</summary>
        public bool NoDot;

        public StatusLine(Font font)
        {
            Font = font;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, r);
            int x = 0;
            if (!NoDot && !string.IsNullOrEmpty(Text))
            {
                int d = Math.Max(Theme.S(8), (int)(Font.Size * Theme.Scale * 0.95f));
                var dot = new Rectangle(0, (r.Height - d) / 2, d, d);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var c = Theme.DotFor(color);
                using (var b = new LinearGradientBrush(dot, Theme.Light(c, .35f), Theme.Dark(c, .2f), 90f)) g.FillEllipse(b, dot);
                using (var p = new Pen(Theme.Dark(c, .6f))) g.DrawEllipse(p, dot);
                g.SmoothingMode = SmoothingMode.Default;
                x = d + Theme.S(8);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Math.Max(1, r.Width - x), r.Height), NoDot ? color : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// Header with the animated PARANOIDAL logo (LogoArt; for Space Age with the plate of the game's logo) and hazard
    /// stripes: the one distinctive element of the window. With a Title (the start window) it is a low strip with the
    /// title instead of the logo.
    /// </summary>
    sealed class HeaderPanel : Control
    {
        /// <summary>The logo of the Space Age build (PARANOIDAL with the SPACE AGE plate).</summary>
        public bool SpaceAge;
        /// <summary>The start window: the title in the middle, no logo and no version lines.</summary>
        public string Title;
        /// <summary>Widths taken by the buttons at the left and at the right (set by the window layout): the logo stays between them.</summary>
        public int LeftReserve, RightReserve;
        Rectangle logoRect;
        readonly Action repaint;

        public HeaderPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            repaint = () => { if (IsHandleCreated && Visible && Title == null && !logoRect.IsEmpty) Invalidate(logoRect); };
            LogoArt.Attach(repaint);
        }

        protected override void Dispose(bool disposing) { if (disposing) LogoArt.Detach(repaint); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            int stripe = Theme.S(9);
            var top = new Rectangle(0, 0, r.Width, r.Height - stripe);
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, r.Width, Math.Max(1, top.Height)), Color.FromArgb(52, 48, 44), Color.FromArgb(22, 21, 21), 90f))
                g.FillRectangle(b, top);
            using (var glow = new GraphicsPath())
            {
                glow.AddEllipse(r.Width / 2 - r.Width / 3, -top.Height / 2, r.Width * 2 / 3, top.Height * 2);
                using (var pb = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(70, 255, 150, 30), SurroundColors = new[] { Color.FromArgb(0, 255, 150, 30) } })
                    g.FillPath(pb, glow);
            }
            if (Title != null)
            {
                TextRenderer.DrawText(g, Title, Theme.Big, new Rectangle(Theme.S(130), 0, Math.Max(1, r.Width - Theme.S(260)), top.Height), Theme.Title,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                logoRect = Rectangle.Empty;
            }
            else
            {
                // the logo is centred in the window, clear of the button columns at both sides
                int half = Math.Max(Theme.S(60), Math.Min(r.Width / 2 - LeftReserve, r.Width / 2 - RightReserve) - Theme.S(16));
                var box = new Rectangle(r.Width / 2 - half, Theme.S(8), 2 * half, top.Height - Theme.S(16));
                logoRect = LogoArt.Draw(g, box, SpaceAge, Theme.S(SpaceAge ? 420 : 600));
                logoRect.Inflate(2, 2);
                int right = r.Width - Theme.S(12);
                TextRenderer.DrawText(g, AppInfo.Name + " " + AppInfo.Version, Theme.Small, new Rectangle(0, Theme.S(4), right, Theme.S(18)), Theme.Muted, TextFormatFlags.Right);
                // the game the build needs (same wording as in the panes and dialogs), not the game that was found
                TextRenderer.DrawText(g, Tr.T("для ", "for ") + AppInfo.RequiredGame, Theme.Small,
                    new Rectangle(0, top.Height - Theme.S(21), right, Theme.S(18)), Theme.Muted, TextFormatFlags.Right);
            }
            Theme.Hazard(g, new Rectangle(0, top.Bottom, r.Width, stripe), 0, Theme.Yellow, Color.FromArgb(20, 20, 20));
        }
    }

    /// <summary>
    /// The game's progress bar: an inset groove with a green bar, the percentage white over the groove and black over
    /// the bar; while waiting, a short bar runs along the groove.
    /// </summary>
    sealed class FProgress : Control
    {
        double value;
        bool indeterminate;
        int offset;
        readonly Timer timer = new Timer { Interval = 30 };
        public bool Active;
        static readonly Color Bar = Color.FromArgb(70, 196, 64);

        public FProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            timer.Tick += (s, e) => { offset += Theme.S(4); Invalidate(); };
        }

        public double Value { get { return value; } set { this.value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }

        public bool Indeterminate
        {
            get { return indeterminate; }
            set { indeterminate = value; Sync(); Invalidate(); }
        }

        public void SetActive(bool a) { Active = a; Sync(); Invalidate(); }

        void Sync() { timer.Enabled = Active && indeterminate; }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

        void FillBar(Graphics g, Rectangle fill)
        {
            if (fill.Width <= 0) return;
            using (var b = new LinearGradientBrush(new Rectangle(fill.X, fill.Y - 1, fill.Width, fill.Height + 2), Theme.Light(Bar, .45f), Theme.Dark(Bar, .25f), 90f))
                g.FillRectangle(b, fill);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Pane)) g.FillRectangle(b, r);
            using (var b = new SolidBrush(Color.FromArgb(60, 58, 60))) g.FillRectangle(b, r);
            using (var p = new Pen(Color.FromArgb(9, 9, 9))) { g.DrawLine(p, 0, 0, r.Width - 1, 0); g.DrawLine(p, 0, 0, 0, r.Height - 1); }
            using (var p = new Pen(Color.FromArgb(18, 18, 18))) g.DrawLine(p, 1, 1, r.Width - 2, 1);
            using (var p = new Pen(Color.FromArgb(105, 104, 104))) { g.DrawLine(p, 0, r.Height - 1, r.Width - 1, r.Height - 1); g.DrawLine(p, r.Width - 1, 0, r.Width - 1, r.Height - 1); }
            var inner = new Rectangle(2, 3, r.Width - 4, r.Height - 5);
            if (indeterminate && Active)
            {
                int w = Math.Max(Theme.S(40), inner.Width / 6), span = inner.Width + w;
                int x = inner.X - w + (offset % span);
                var fill = Rectangle.Intersect(inner, new Rectangle(x, inner.Y, w, inner.Height));
                FillBar(g, fill);
                return;
            }
            if (value <= 0) return;
            var bar = new Rectangle(inner.X, inner.Y, Math.Max(1, (int)(inner.Width * value)), inner.Height);
            FillBar(g, bar);
            var txt = (value * 100).ToString(value < 0.1 ? "0.0" : "0", Fmt.Ru) + " %";
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, txt, Theme.Bold, r, Theme.Text, flags);
            var old = g.Clip;
            g.SetClip(bar);
            TextRenderer.DrawText(g, txt, Theme.Bold, r, Color.Black, flags);
            g.Clip = old;
        }
    }

    /// <summary>
    /// A message or question in a game dialog: the text in a pane, the buttons under it, the way back on the left and the
    /// confirmation on the right (green; red for a destructive one). Enter confirms, Esc goes back.
    /// </summary>
    sealed class DarkDialog : Form
    {
        DarkDialog() { }

        /// <summary>Tests only (PARANOIDAL_LAUNCHER_TEST_AUTO): no dialog is shown, its text goes here and the answer is "yes".</summary>
        public static Action<string> Headless;
        /// <summary>UI pictures only (UiShot): receives the dialog instead of showing it.</summary>
        public static Action<Form> TestSnapshot;

        public static DialogResult Ask(IWin32Window owner, string title, string message, string sub, IList<string> details,
            string yesText, string noText, Color accent, ButtonKind yesKind = ButtonKind.Confirm)
        {
            if (Headless != null)
            {
                Headless(title + ": " + message + (string.IsNullOrEmpty(sub) ? "" : " " + sub.Replace("\n", " ")));
                return DialogResult.Yes;
            }
            var S = new Func<float, int>(Theme.S);
            var f = new DarkDialog
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                BackColor = Theme.Window,
                ForeColor = Theme.Text,
                Font = Theme.Body,
                AutoScaleMode = AutoScaleMode.None,
                KeyPreview = true,
            };
            var icon = Res.AppIcon(32);
            if (icon != null) f.Icon = icon;
            int width = S(details != null ? 640 : 540), m = S(12), pad = S(12), iconSize = S(48);
            var pane = new Card(null) { Location = new Point(m, m) };
            f.Controls.Add(pane);
            int pw = width - 2 * m;
            int textX = pad + iconSize + S(12), textW = pw - textX - pad;

            pane.Controls.Add(new PictureBox { Image = Res.Gear, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(pad, pad, iconSize, iconSize), BackColor = Theme.Pane });
            int y = pad;
            var mh = TextRenderer.MeasureText(message, Theme.Heading, new Size(textW, int.MaxValue), TextFormatFlags.WordBreak).Height;
            pane.Controls.Add(new Label { Text = message, Font = Theme.Heading, ForeColor = accent, BackColor = Theme.Pane, Bounds = new Rectangle(textX, y, textW, mh + S(4)), UseMnemonic = false });
            y += mh + S(8);
            if (!string.IsNullOrEmpty(sub))
            {
                var sh = TextRenderer.MeasureText(sub, Theme.Body, new Size(textW, int.MaxValue), TextFormatFlags.WordBreak).Height;
                pane.Controls.Add(new Label { Text = sub, ForeColor = Theme.Text, BackColor = Theme.Pane, Bounds = new Rectangle(textX, y, textW, sh + S(4)), UseMnemonic = false });
                y += sh + S(4);
            }
            y = Math.Max(y, pad + iconSize) + pad;
            if (details != null && details.Count > 0)
            {
                var box = new RichTextBox
                {
                    ReadOnly = true,
                    BorderStyle = BorderStyle.None,
                    BackColor = Theme.Deep,
                    ForeColor = Theme.Text,
                    Font = new Font("Consolas", 9f),
                    Bounds = new Rectangle(pad, y, pw - 2 * pad, S(180)),
                    Text = string.Join("\n", details),
                    WordWrap = false,
                    Tag = Card.DeepTag,
                };
                Native.DarkScrollbars(box);
                pane.Controls.Add(box);
                y += S(180) + pad;
            }
            pane.Size = new Size(pw, y);
            y = pane.Bottom + m;

            int bh = S(36);
            var yesFont = Theme.Bold;
            int yw = Math.Max(S(150), TextRenderer.MeasureText(yesText, yesFont).Width + S(40));
            var yesBtn = new FButton(yesText, yesKind) { Bounds = new Rectangle(width - m - yw, y, yw, bh) };
            yesBtn.Click += (s, e) => { f.DialogResult = DialogResult.Yes; f.Close(); };
            f.Controls.Add(yesBtn);
            if (noText != null)
            {
                int w = Math.Max(S(130), TextRenderer.MeasureText(noText, yesFont).Width + S(40));
                var noBtn = new FButton(noText, ButtonKind.Normal) { Bounds = new Rectangle(m, y, w, bh) };
                noBtn.Click += (s, e) => { f.DialogResult = DialogResult.No; f.Close(); };
                f.Controls.Add(noBtn);
            }
            y += bh + m;

            f.ClientSize = new Size(width, y);
            f.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { f.DialogResult = DialogResult.Yes; f.Close(); }
                else if (e.KeyCode == Keys.Escape) { f.DialogResult = noText == null ? DialogResult.Yes : DialogResult.Cancel; f.Close(); }
            };
            f.HandleCreated += (s, e) => Native.DarkTitle(f.Handle);
            if (TestSnapshot != null) { TestSnapshot(f); f.Dispose(); return DialogResult.Cancel; }
            var res = f.ShowDialog(owner);
            f.Dispose();
            return res;
        }

        public static void Info(IWin32Window owner, string title, string message, Color accent)
        {
            Ask(owner, title, message, null, null, "OK", null, accent);
        }
    }
}
