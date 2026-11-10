using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Paranoidal
{
    /// <summary>
    /// The animated PARANOIDAL logo (1.5.0, owner 2026-11-10): one GIF embedded in the exe (assets/paranoidal-logo.gif,
    /// 900 px wide, frames hold only the changed pixels), shared by the header and the cards of the start window. It
    /// plays all the time; it pauses while the window is minimised or the game runs (MainForm), so it never takes CPU
    /// from the game. The Space Age build adds the "SPACE AGE" plate of the game's logo under it.
    /// </summary>
    static class LogoArt
    {
        static Image gif;
        static Stream gifStream;
        static FrameDimension dim;
        static int frames, frame;
        static int[] delays;
        static bool failed, paused;
        static readonly List<Action> users = new List<Action>();
        static readonly Timer timer = new Timer();

        static LogoArt()
        {
            timer.Tick += (s, e) => Next();
        }

        /// <summary>The GIF's own size (the logo at 150 % DPI).</summary>
        public static Size Natural => gif != null ? gif.Size : new Size(900, 123);

        /// <summary>No animation (the window is minimised, the game runs): the current frame stays.</summary>
        public static bool Paused { get { return paused; } set { paused = value; Sync(); } }

        /// <summary>A control that shows the logo: `changed` repaints it on every frame.</summary>
        public static void Attach(Action changed) { users.Add(changed); Sync(); }
        public static void Detach(Action changed) { users.Remove(changed); Sync(); }

        static void Ensure()
        {
            if (gif != null || failed) return;
            try
            {
                gifStream = Res.LogoGif();
                if (gifStream == null) { failed = true; return; }
                gif = Image.FromStream(gifStream);   // GDI+ reads the frames from the stream later: keep it open
                dim = new FrameDimension(gif.FrameDimensionsList[0]);
                frames = gif.GetFrameCount(dim);
                delays = new int[frames];
                try
                {
                    var raw = gif.GetPropertyItem(0x5100).Value;   // PropertyTagFrameDelay: 1/100 s per frame
                    for (int i = 0; i < frames && i * 4 + 3 < raw.Length; i++) delays[i] = BitConverter.ToInt32(raw, i * 4) * 10;
                }
                catch { }
                for (int i = 0; i < frames; i++) if (delays[i] < 20) delays[i] = 100;
            }
            catch { failed = true; gif = null; }
        }

        static void Sync()
        {
            Ensure();
            bool on = gif != null && frames > 1 && users.Count > 0 && !paused;
            if (on && !timer.Enabled) { timer.Interval = delays[frame]; timer.Start(); }
            else if (!on && timer.Enabled) timer.Stop();
        }

        static void Next()
        {
            if (gif == null) return;
            frame = (frame + 1) % frames;
            gif.SelectActiveFrame(dim, frame);
            timer.Interval = delays[frame];
            foreach (var u in users.ToArray()) u();
        }

        /// <summary>
        /// Draws the logo of a build into the box (centred): the animated PARANOIDAL, for Space Age with the plate under it.
        /// `width`: the wanted width of the PARANOIDAL logo (smaller when the box is narrower or lower). Returns the
        /// rectangle drawn (to repaint only it on the next frame).
        /// </summary>
        public static Rectangle Draw(Graphics g, Rectangle box, bool spaceAge, int width)
        {
            Ensure();
            var nat = Natural;
            var plate = spaceAge ? Res.SpaceAgePlate : null;
            // the plate is a little narrower than the PARANOIDAL logo (400 to 420 in the approved layout)
            float pw = plate != null ? 0.952f : 0f;
            float w = Math.Min(width, box.Width / Math.Max(1f, pw));
            Func<float, float> height = x => x * nat.Height / nat.Width + (plate != null ? x * pw * plate.Height / plate.Width : 0);
            if (height(w) > box.Height) w = w * box.Height / height(w);
            int gw = (int)w, gh = (int)(w * nat.Height / nat.Width);
            int ph = plate != null ? (int)(w * pw * plate.Height / plate.Width) : 0, ppw = (int)(w * pw);
            int x = box.X + (box.Width - gw) / 2, y = box.Y + (box.Height - gh - ph) / 2;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var r = new Rectangle(x, y, gw, gh);
            if (gif != null) g.DrawImage(gif, r);
            else
                using (var f = new Font("Segoe UI Black", Math.Max(8f, gh * 0.45f), GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, "PARANOIDAL", f, r, Theme.Orange, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (plate != null) g.DrawImage(plate, new Rectangle(box.X + (box.Width - ppw) / 2, y + gh, ppw, ph));
            g.PixelOffsetMode = PixelOffsetMode.Default;
            return new Rectangle(Math.Min(x, box.X + (box.Width - ppw) / 2), y, Math.Max(gw, ppw), gh + ph);
        }
    }

    /// <summary>
    /// A card of the start window: the logo of the build, its name, a note ("основная сборка" / "в активной
    /// разработке"), the state with a status dot. The whole card is a button: orange outline under the mouse.
    /// </summary>
    sealed class BuildCard : Control
    {
        public readonly bool SpaceAge;
        public string Title, Note, State, StateNote;
        public Color NoteColor = Theme.Muted, StateColor = Theme.Muted;
        bool hover;
        Rectangle logoRect;
        readonly Action repaint;

        public BuildCard(bool spaceAge)
        {
            SpaceAge = spaceAge;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            repaint = () => { if (IsHandleCreated && Visible) Invalidate(logoRect); };
            LogoArt.Attach(repaint);
        }

        protected override void Dispose(bool disposing) { if (disposing) LogoArt.Detach(repaint); base.Dispose(disposing); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        public int ArtHeight => Theme.S(150);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(Theme.Pane)) g.FillRectangle(b, r);
            // the art: the header's dark gradient with an orange glow, the logo of the build
            var art = new Rectangle(1, 1, r.Width - 2, ArtHeight);
            using (var b = new LinearGradientBrush(new Rectangle(art.X, art.Y, art.Width, art.Height + 1), Color.FromArgb(43, 40, 38), Color.FromArgb(20, 19, 19), 90f))
                g.FillRectangle(b, art);
            using (var glow = new GraphicsPath())
            {
                glow.AddEllipse(art.X + art.Width / 8, art.Y - art.Height / 3, art.Width * 3 / 4, art.Height * 4 / 3);
                using (var pb = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(46, 255, 150, 30), SurroundColors = new[] { Color.FromArgb(0, 255, 150, 30) } })
                    g.FillPath(pb, glow);
            }
            logoRect = LogoArt.Draw(g, Rectangle.Inflate(art, -Theme.S(16), -Theme.S(14)), SpaceAge, Theme.S(SpaceAge ? 320 : 330));
            logoRect.Inflate(2, 2);
            using (var p = new Pen(Color.Black)) g.DrawLine(p, 1, art.Bottom, r.Width - 2, art.Bottom);

            int y = art.Bottom + Theme.S(14), w = r.Width - Theme.S(24), x = Theme.S(12);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, Title, Theme.Heading, new Rectangle(x, y, w, Theme.S(28)), Theme.Title, flags);
            y += Theme.S(28);
            TextRenderer.DrawText(g, Note, Theme.Body, new Rectangle(x, y, w, Theme.S(20)), NoteColor, flags);
            y += Theme.S(32);
            if (!string.IsNullOrEmpty(State))
            {
                int tw = TextRenderer.MeasureText(State, Theme.Bold).Width, d = Theme.S(10), gap = Theme.S(8);
                int sx = (r.Width - tw - d - gap) / 2;
                var dot = new Rectangle(sx, y + (Theme.S(22) - d) / 2, d, d);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var c = Theme.DotFor(StateColor);
                using (var b = new LinearGradientBrush(dot, Theme.Light(c, .35f), Theme.Dark(c, .2f), 90f)) g.FillEllipse(b, dot);
                using (var p = new Pen(Theme.Dark(c, .6f))) g.DrawEllipse(p, dot);
                g.SmoothingMode = SmoothingMode.Default;
                TextRenderer.DrawText(g, State, Theme.Bold, new Rectangle(sx + d + gap, y, tw + 2, Theme.S(22)), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            y += Theme.S(22);
            TextRenderer.DrawText(g, StateNote, Theme.Small, new Rectangle(x, y, w, Theme.S(20)), Theme.Muted, flags);

            // the frame: the pane's edges, orange under the mouse (the whole card is the button)
            if (hover)
            {
                using (var p = new Pen(Theme.Orange, Theme.S(2))) g.DrawRectangle(p, Theme.S(1), Theme.S(1), r.Width - Theme.S(2) - 1, r.Height - Theme.S(2) - 1);
            }
            else
            {
                using (var p = new Pen(Theme.PaneHi)) g.DrawLine(p, 1, 0, r.Width - 2, 0);
                using (var p = new Pen(Theme.Edge)) g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
            }
        }
    }

    /// <summary>
    /// A tab of the build under the header (Paranoidal 2.1 / Paranoidal 2.1 Space Age): the selected one has the pane's
    /// colour with an orange top edge, the others are grey buttons that turn orange under the mouse; a small note after
    /// the name ("в активной разработке").
    /// </summary>
    sealed class LineTab : Control
    {
        public string Note;
        bool active, hover;

        public bool Active { get { return active; } set { active = value; Cursor = value || !Enabled ? Cursors.Default : Cursors.Hand; Invalidate(); } }

        public LineTab()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Font = Theme.Bold;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Active = active; base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        /// <summary>The width the name and the note need.</summary>
        public int PreferredWidth => TextRenderer.MeasureText(Text, Font).Width + (string.IsNullOrEmpty(Note) ? 0 : TextRenderer.MeasureText(Note, Theme.Small).Width + Theme.S(8)) + Theme.S(44);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(Theme.Window)) g.FillRectangle(b, r);
            Color fore, noteColor;
            Rectangle face;
            if (active)
            {
                face = r;
                using (var b = new SolidBrush(Theme.Pane)) g.FillRectangle(b, face);
                using (var b = new SolidBrush(Theme.Orange)) g.FillRectangle(b, 1, 0, r.Width - 2, Theme.S(2));
                using (var p = new Pen(Theme.Edge)) { g.DrawLine(p, 0, 0, 0, r.Height - 1); g.DrawLine(p, r.Width - 1, 0, r.Width - 1, r.Height - 1); }
                fore = Theme.Title;
                noteColor = Theme.Orange;
            }
            else
            {
                face = new Rectangle(r.X, r.Y + Theme.S(4), r.Width, r.Height - Theme.S(4));
                if (hover && Enabled) Theme.Bevel(g, face, Color.FromArgb(247, 203, 128), Color.FromArgb(230, 155, 36), Color.FromArgb(129, 85, 16), false);
                else Theme.Bevel(g, face, Color.FromArgb(201, 201, 201), Color.FromArgb(143, 143, 143), Color.FromArgb(79, 79, 79), false);
                fore = Color.Black;
                noteColor = Color.FromArgb(110, 62, 0);
            }
            int tw = TextRenderer.MeasureText(Text, Font).Width;
            int nw = string.IsNullOrEmpty(Note) ? 0 : TextRenderer.MeasureText(Note, Theme.Small).Width;
            int total = tw + (nw > 0 ? nw + Theme.S(8) : 0);
            int x = face.X + Math.Max(Theme.S(8), (face.Width - total) / 2);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, face.Y, tw + 2, face.Height), fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (nw > 0)
                TextRenderer.DrawText(g, Note, Theme.Small, new Rectangle(x + tw + Theme.S(8), face.Y + Theme.S(1), nw + 2, face.Height), noteColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// The start window (1.5.0, owner 2026-11-10), in the main window before its panes: a header with the language
    /// button and a title, then one of three views: the choice of the build (two cards, «Запомнить выбор»), no suitable
    /// Factorio (what was found, how to get 2.1, «Закрыть» / «Выбрать папку игры…» / «Проверить снова»), or a wait
    /// (looking for the game, the launcher moves into its folder).
    /// </summary>
    sealed class StartPanel : Panel
    {
        public enum View { Busy, Selector, Need }

        public readonly HeaderPanel Header = new HeaderPanel();
        public readonly FButton BtnLang = new FButton("", ButtonKind.Normal);
        public readonly BuildCard Card21 = new BuildCard(false), CardSa = new BuildCard(true);
        public readonly CheckBox ChkRemember = new CheckBox();
        public readonly Label LblHint = new Label();
        readonly Card needPane = new Card(null);
        readonly PictureBox needIcon = new PictureBox();
        readonly Label needHead = new Label(), needBody = new Label(), needTail = new Label();
        public readonly FButton BtnClose = new FButton("", ButtonKind.Normal), BtnPick = new FButton("", ButtonKind.Normal), BtnRecheck = new FButton("", ButtonKind.Confirm);
        readonly Label busyText = new Label(), busyDetail = new Label();
        readonly FProgress busyBar = new FProgress();
        View view = View.Busy;

        public View Current => view;

        public StartPanel()
        {
            BackColor = Theme.Window;
            DoubleBuffered = true;
            Controls.Add(Header);
            Header.Controls.Add(BtnLang);
            foreach (var c in new Control[] { Card21, CardSa, ChkRemember, LblHint, needPane, BtnClose, BtnPick, BtnRecheck, busyText, busyBar, busyDetail })
                Controls.Add(c);
            ChkRemember.BackColor = Theme.Window;
            ChkRemember.ForeColor = Theme.Text;
            ChkRemember.FlatStyle = FlatStyle.Flat;
            ChkRemember.UseMnemonic = false;
            ChkRemember.FlatAppearance.BorderColor = Theme.DotGrey;
            ChkRemember.FlatAppearance.CheckedBackColor = Theme.Deep;
            LblHint.ForeColor = Theme.Muted;
            LblHint.BackColor = Theme.Window;
            LblHint.Font = Theme.Small;
            LblHint.TextAlign = ContentAlignment.MiddleRight;
            LblHint.UseMnemonic = false;
            needIcon.Image = Res.Gear;
            needIcon.SizeMode = PictureBoxSizeMode.Zoom;
            needIcon.BackColor = Theme.Pane;
            needPane.Controls.Add(needIcon);
            foreach (var l in new[] { needHead, needBody, needTail })
            {
                l.BackColor = Theme.Pane;
                l.UseMnemonic = false;
                needPane.Controls.Add(l);
            }
            needHead.Font = Theme.Heading;
            needHead.ForeColor = Theme.Title;
            needBody.ForeColor = Theme.Text;
            needTail.ForeColor = Theme.Muted;
            foreach (var l in new[] { busyText, busyDetail })
            {
                l.BackColor = Theme.Window;
                l.TextAlign = ContentAlignment.MiddleCenter;
                l.UseMnemonic = false;
                l.AutoEllipsis = true;
            }
            busyText.Font = Theme.Heading;
            busyText.ForeColor = Theme.Title;
            busyDetail.ForeColor = Theme.Muted;
            busyDetail.Font = Theme.Small;
            busyBar.Indeterminate = true;
            Resize += (s, e) => DoLayout();
            Show(View.Busy);
        }

        /// <summary>Client height of the window for the current view (its width is Width).</summary>
        public int PreferredHeight(int width)
        {
            Func<float, int> S = Theme.S;
            if (view == View.Selector) return S(66) + S(20) + CardHeight + S(14) + S(28) + S(18);
            if (view == View.Need) return S(66) + S(16) + NeedPaneHeight(width) + S(16) + S(34) + S(20);
            return S(66) + S(210);
        }

        int CardHeight => Card21.ArtHeight + Theme.S(130);

        public void Show(View v)
        {
            view = v;
            Card21.Visible = CardSa.Visible = ChkRemember.Visible = LblHint.Visible = v == View.Selector;
            needPane.Visible = BtnClose.Visible = BtnPick.Visible = BtnRecheck.Visible = v == View.Need;
            busyText.Visible = busyBar.Visible = busyDetail.Visible = v == View.Busy;
            busyBar.SetActive(v == View.Busy);
            DoLayout();
            Invalidate(true);
        }

        public void SetBusy(string text, string detail)
        {
            busyText.Text = text;
            busyDetail.Text = detail ?? "";
        }

        public void SetNeed(string head, string body, string tail)
        {
            needHead.Text = head;
            needBody.Text = body ?? "";
            needTail.Text = tail ?? "";
            needTail.Visible = !string.IsNullOrEmpty(tail);
            DoLayout();
        }

        int NeedTextWidth(int width) => width - 2 * Theme.S(20) - 2 * Theme.S(16) - Theme.S(56) - Theme.S(16);

        int Measure(string text, Font f, int w) =>
            string.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(text, f, new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height;

        int NeedPaneHeight(int width)
        {
            Func<float, int> S = Theme.S;
            int tw = NeedTextWidth(width);
            int h = Measure(needHead.Text, Theme.Heading, tw) + S(10) + Measure(needBody.Text, Theme.Body, tw);
            if (!string.IsNullOrEmpty(needTail.Text)) h += S(10) + Measure(needTail.Text, Theme.Body, tw);
            return Math.Max(S(56), h) + 2 * S(16);
        }

        public void DoLayout()
        {
            Func<float, int> S = Theme.S;
            int W = ClientSize.Width;
            if (W < 10) return;
            Header.Bounds = new Rectangle(0, 0, W, S(66));
            BtnLang.Bounds = new Rectangle(S(12), S(14), Math.Max(S(96), TextRenderer.MeasureText(BtnLang.Text, BtnLang.Font).Width + S(24)), S(28));
            int y = S(66);
            if (view == View.Selector)
            {
                int m = S(20), gap = S(20), cw = (W - 2 * m - gap) / 2, ch = CardHeight;
                Card21.Bounds = new Rectangle(m, y + S(20), cw, ch);
                CardSa.Bounds = new Rectangle(m + cw + gap, y + S(20), cw, ch);
                int fy = Card21.Bottom + S(14);
                int chw = TextRenderer.MeasureText(ChkRemember.Text, ChkRemember.Font).Width + S(30);
                ChkRemember.Bounds = new Rectangle(m, fy, chw, S(28));
                LblHint.Bounds = new Rectangle(ChkRemember.Right + S(12), fy, W - m - ChkRemember.Right - S(12), S(28));
            }
            else if (view == View.Need)
            {
                int m = S(20), pad = S(16), icon = S(56);
                int tw = NeedTextWidth(W), tx = pad + icon + pad;
                needPane.Bounds = new Rectangle(m, y + S(16), W - 2 * m, NeedPaneHeight(W));
                needIcon.Bounds = new Rectangle(pad, pad, icon, icon);
                int ty = pad, h = Measure(needHead.Text, Theme.Heading, tw);
                needHead.Bounds = new Rectangle(tx, ty, tw, h + S(2));
                ty += h + S(10);
                h = Measure(needBody.Text, Theme.Body, tw);
                needBody.Bounds = new Rectangle(tx, ty, tw, h + S(2));
                ty += h + S(10);
                needTail.Bounds = new Rectangle(tx, ty, tw, Measure(needTail.Text, Theme.Body, tw) + S(2));
                int by = needPane.Bottom + S(16), bh = S(34);
                Func<FButton, int> bw = b => Math.Max(S(120), TextRenderer.MeasureText(b.Text, b.Font).Width + S(36));
                BtnClose.Bounds = new Rectangle(m, by, bw(BtnClose), bh);
                BtnRecheck.Bounds = new Rectangle(W - m - bw(BtnRecheck), by, bw(BtnRecheck), bh);
                BtnPick.Bounds = new Rectangle(BtnRecheck.Left - S(8) - bw(BtnPick), by, bw(BtnPick), bh);
            }
            else
            {
                int bw = Math.Min(S(480), W - S(80));
                busyText.Bounds = new Rectangle(S(20), y + S(50), W - S(40), S(30));
                busyBar.Bounds = new Rectangle((W - bw) / 2, y + S(92), bw, S(24));
                busyDetail.Bounds = new Rectangle(S(20), y + S(126), W - S(40), S(40));
            }
        }
    }
}
