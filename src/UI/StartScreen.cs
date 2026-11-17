using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace Paranoidal
{
    /// <summary>
    /// The animated PARANOIDAL logo (1.5.0, owner 2026-11-10): one GIF embedded in the exe (assets/paranoidal-logo.gif,
    /// 900 px wide, frames hold only the changed pixels), shared by the header and the cards of the start window. It
    /// plays all the time; it pauses while the window is minimised or the game runs (MainWindow), so it never takes CPU
    /// from the game. The Space Age build adds the "SPACE AGE" plate of the game's logo under it. Avalonia does not play
    /// GIFs: the frames are decoded one after another by SkiaSharp into one canvas, copied into a WriteableBitmap.
    /// </summary>
    static class LogoArt
    {
        static SKCodec codec;
        static SKBitmap canvas;
        static WriteableBitmap image;
        static int frames, frame = -1;
        static int[] delays;
        static bool failed, paused;
        static readonly List<Action> users = new List<Action>();
        static readonly DispatcherTimer timer = new DispatcherTimer();

        static LogoArt()
        {
            timer.Tick += (s, e) => Next();
        }

        /// <summary>The GIF's own size (the logo at 150 % DPI).</summary>
        public static Size Natural => canvas != null ? new Size(canvas.Width, canvas.Height) : new Size(900, 123);

        /// <summary>No animation (the window is minimised, the game runs): the current frame stays.</summary>
        public static bool Paused { get { return paused; } set { paused = value; Sync(); } }

        /// <summary>A control that shows the logo: `changed` repaints it on every frame.</summary>
        public static void Attach(Action changed) { if (!users.Contains(changed)) users.Add(changed); Sync(); }
        public static void Detach(Action changed) { users.Remove(changed); Sync(); }

        static void Ensure()
        {
            if (image != null || failed) return;
            try
            {
                byte[] data;
                using (var s = Res.LogoGif())
                {
                    if (s == null) { failed = true; return; }
                    var ms = new MemoryStream();
                    s.CopyTo(ms);
                    data = ms.ToArray();
                }
                codec = SKCodec.Create(new SKMemoryStream(data));
                if (codec == null) { failed = true; return; }
                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                canvas = new SKBitmap(info);
                var fi = codec.FrameInfo;
                frames = Math.Max(1, fi.Length);
                delays = new int[frames];
                for (int i = 0; i < frames; i++) delays[i] = i < fi.Length && fi[i].Duration >= 20 ? fi[i].Duration : 100;
                image = new WriteableBitmap(new PixelSize(info.Width, info.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                Decode(0);
            }
            catch { failed = true; image = null; }
        }

        /// <summary>Frame i into the canvas (the GIF's frames build on the previous one) and into the bitmap the window draws.</summary>
        static void Decode(int i)
        {
            var info = canvas.Info;
            var opts = new SKCodecOptions(i, i == 0 || i != frame + 1 ? -1 : frame);
            if (i == 0) canvas.Erase(SKColors.Transparent);
            else if (i != frame + 1)
            {
                // not the next frame (should not happen): decode from the start
                canvas.Erase(SKColors.Transparent);
                for (int k = 0; k < i; k++) codec.GetPixels(info, canvas.GetPixels(), new SKCodecOptions(k, k == 0 ? -1 : k - 1));
                opts = new SKCodecOptions(i, i - 1);
            }
            codec.GetPixels(info, canvas.GetPixels(), opts);
            frame = i;
            using (var fb = image.Lock())
            {
                int rows = Math.Min(info.Height, fb.Size.Height), bytes = Math.Min(info.RowBytes, fb.RowBytes);
                var src = canvas.GetPixels();
                var row = new byte[bytes];
                for (int y = 0; y < rows; y++)
                {
                    Marshal.Copy(src + y * info.RowBytes, row, 0, bytes);
                    Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, bytes);
                }
            }
        }

        static void Sync()
        {
            Ensure();
            bool on = image != null && frames > 1 && users.Count > 0 && !paused;
            if (on && !timer.IsEnabled) { timer.Interval = TimeSpan.FromMilliseconds(delays[Math.Max(0, frame)]); timer.Start(); }
            else if (!on && timer.IsEnabled) timer.Stop();
        }

        static void Next()
        {
            if (image == null) return;
            try { Decode((frame + 1) % frames); } catch { }
            timer.Interval = TimeSpan.FromMilliseconds(delays[frame]);
            foreach (var u in users.ToArray()) u();
        }

        /// <summary>
        /// Draws the logo of a build into the box (centred): the animated PARANOIDAL, for Space Age with the plate under it.
        /// `width`: the wanted width of the PARANOIDAL logo (smaller when the box is narrower or lower). Returns the
        /// rectangle drawn.
        /// </summary>
        public static Rect Draw(DrawingContext g, Rect box, bool spaceAge, double width)
        {
            Ensure();
            var nat = Natural;
            var plate = spaceAge ? Res.SpaceAgePlate : null;
            // the plate is a little narrower than the PARANOIDAL logo (400 to 420 in the approved layout)
            double pw = plate != null ? 0.952 : 0;
            double w = Math.Min(width, box.Width / Math.Max(1, pw));
            Func<double, double> height = x => x * nat.Height / nat.Width + (plate != null ? x * pw * plate.PixelSize.Height / plate.PixelSize.Width : 0);
            if (height(w) > box.Height) w = w * box.Height / height(w);
            double gw = Math.Floor(w), gh = Math.Floor(w * nat.Height / nat.Width);
            double ph = plate != null ? Math.Floor(w * pw * plate.PixelSize.Height / plate.PixelSize.Width) : 0, ppw = Math.Floor(w * pw);
            double x0 = box.X + Math.Floor((box.Width - gw) / 2), y0 = box.Y + Math.Floor((box.Height - gh - ph) / 2);
            var r = new Rect(x0, y0, gw, gh);
            using (g.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            {
                if (image != null) g.DrawImage(image, new Rect(0, 0, nat.Width, nat.Height), r);
                else Gui.DrawText(g, "PARANOIDAL", new FFont(Gui.Heavy, Math.Max(8, gh * 0.45), FontWeight.Black), r, Gui.Orange, HAlign.Center);
                if (plate != null) g.DrawImage(plate, new Rect(box.X + Math.Floor((box.Width - ppw) / 2), y0 + gh, ppw, ph));
            }
            return new Rect(Math.Min(x0, box.X + (box.Width - ppw) / 2), y0, Math.Max(gw, ppw), gh + ph);
        }
    }

    /// <summary>
    /// A card of the start window: the logo of the build, its name, a note ("основная сборка" / "в активной
    /// разработке"), the state with a status dot. The whole card is a button: orange outline under the mouse.
    /// </summary>
    sealed class BuildCard : Painted
    {
        public readonly bool SpaceAge;
        public string Title, Note, State, StateNote;
        public Color NoteColor = Gui.Muted, StateColor = Gui.Muted;
        readonly Action repaint;

        public BuildCard(bool spaceAge)
        {
            SpaceAge = spaceAge;
            Clickable(true);
            repaint = () => { if (IsVisible) InvalidateVisual(); };
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); LogoArt.Attach(repaint); }
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { LogoArt.Detach(repaint); base.OnDetachedFromVisualTree(e); }

        public double ArtHeight => 150;

        public override void Render(DrawingContext g)
        {
            var r = new Rect(Bounds.Size);
            Gui.Fill(g, Gui.Pane, r);
            // the art: the header's dark gradient with an orange glow, the logo of the build
            var art = new Rect(1, 1, r.Width - 2, ArtHeight);
            g.FillRectangle(Gui.VGradient(Color.FromRgb(43, 40, 38), Color.FromRgb(20, 19, 19)), art);
            using (g.PushClip(art))
                Gui.Glow(g, new Rect(art.X + art.Width / 8, art.Y - art.Height / 3, art.Width * 3 / 4, art.Height * 4 / 3), Color.FromArgb(46, 255, 150, 30));
            LogoArt.Draw(g, art.Deflate(new Thickness(16, 14)), SpaceAge, SpaceAge ? 320 : 330);
            Gui.Line(g, Gui.Black, 1, art.Bottom, r.Width - 2, art.Bottom);

            double y = art.Bottom + 14, w = r.Width - 24, x = 12;
            Gui.DrawText(g, Title, Gui.Heading, new Rect(x, y, w, 28), Gui.Title, HAlign.Center);
            y += 28;
            Gui.DrawText(g, Note, Gui.Body, new Rect(x, y, w, 20), NoteColor, HAlign.Center);
            y += 32;
            if (!string.IsNullOrEmpty(State))
            {
                double tw = Gui.TextWidth(State, Gui.Bold), d = 10, gap = 8;
                double sx = Math.Floor((r.Width - tw - d - gap) / 2);
                Gui.Dot(g, new Rect(sx, y + Math.Floor((22 - d) / 2), d, d), StateColor);
                Gui.DrawText(g, State, Gui.Bold, new Rect(sx + d + gap, y, tw + 2, 22), Gui.Text);
            }
            y += 22;
            Gui.DrawText(g, StateNote, Gui.Small, new Rect(x, y, w, 20), Gui.Muted, HAlign.Center);

            // the frame: the pane's edges, orange under the mouse (the whole card is the button)
            if (hover) g.DrawRectangle(null, Gui.Pen(Gui.Orange, 2), new Rect(1, 1, r.Width - 2, r.Height - 2));
            else
            {
                Gui.Line(g, Gui.PaneHi, 1, 0, r.Width - 2, 0);
                Gui.Outline(g, Gui.Edge, r);
            }
        }
    }

    /// <summary>
    /// A tab of the build under the header (Paranoidal 2.1 / Paranoidal 2.1 Space Age): the selected one has the pane's
    /// colour with an orange top edge, the others are grey buttons that turn orange under the mouse; a small note after
    /// the name ("в активной разработке").
    /// </summary>
    sealed class LineTab : Painted
    {
        public string Note;
        bool active;
        public readonly FFont Font = Gui.Bold;

        public bool Active { get { return active; } set { active = value; Clickable(!value && IsEffectivelyEnabled); InvalidateVisual(); } }

        public LineTab() { Clickable(true); }

        protected override void EnabledChanged() { Clickable(!active && IsEffectivelyEnabled); }

        /// <summary>The width the name and the note need.</summary>
        public double PreferredWidth => Gui.TextWidth(Text, Font) + (string.IsNullOrEmpty(Note) ? 0 : Gui.TextWidth(Note, Gui.Small) + 8) + 44;

        public override void Render(DrawingContext g)
        {
            var r = new Rect(Bounds.Size);
            Gui.Fill(g, Gui.Window, r);
            Color fore, noteColor;
            Rect face;
            if (active)
            {
                face = r;
                Gui.Fill(g, Gui.Pane, face);
                Gui.Fill(g, Gui.Orange, 1, 0, r.Width - 2, 2);
                Gui.Line(g, Gui.Edge, 0, 0, 0, r.Height - 1);
                Gui.Line(g, Gui.Edge, r.Width - 1, 0, r.Width - 1, r.Height - 1);
                fore = Gui.Title;
                noteColor = Gui.Orange;
            }
            else
            {
                face = new Rect(r.X, r.Y + 4, r.Width, r.Height - 4);
                if (hover && IsEffectivelyEnabled) Gui.Bevel(g, face, Color.FromRgb(247, 203, 128), Color.FromRgb(230, 155, 36), Color.FromRgb(129, 85, 16), false);
                else Gui.Bevel(g, face, Color.FromRgb(201, 201, 201), Color.FromRgb(143, 143, 143), Color.FromRgb(79, 79, 79), false);
                fore = Gui.Black;
                noteColor = Color.FromRgb(110, 62, 0);
            }
            double tw = Gui.TextWidth(Text, Font);
            double nw = string.IsNullOrEmpty(Note) ? 0 : Gui.TextWidth(Note, Gui.Small);
            double total = tw + (nw > 0 ? nw + 8 : 0);
            double x = face.X + Math.Max(8, Math.Floor((face.Width - total) / 2));
            Gui.DrawText(g, Text, Font, new Rect(x, face.Y, tw + 2, face.Height), fore);
            if (nw > 0) Gui.DrawText(g, Note, Gui.Small, new Rect(x + tw + 8, face.Y + 1, nw + 2, face.Height), noteColor);
        }
    }

    /// <summary>A picture scaled into its box (the gear of the dialogs).</summary>
    sealed class Pic : Control
    {
        public IImage Image;
        public Pic(IImage image) { Image = image; }

        public override void Render(DrawingContext g)
        {
            if (Image == null) return;
            var s = Image.Size;
            double k = Math.Min(Bounds.Width / s.Width, Bounds.Height / s.Height);
            var r = new Rect((Bounds.Width - s.Width * k) / 2, (Bounds.Height - s.Height * k) / 2, s.Width * k, s.Height * k);
            using (g.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                g.DrawImage(Image, r);
        }
    }

    /// <summary>
    /// The start window (1.5.0, owner 2026-11-10), in the main window before its panes: a header with the language
    /// button and a title, then one of three views: the choice of the build (two cards, «Запомнить выбор»), no suitable
    /// Factorio (what was found, how to get 2.1, «Закрыть» / «Выбрать папку игры…» / «Проверить снова»), or a wait
    /// (looking for the game, the launcher moves into its folder).
    /// </summary>
    sealed class StartPanel : Canvas
    {
        public enum View { Busy, Selector, Need }

        public readonly HeaderPanel Header = new HeaderPanel();
        public readonly FButton BtnLang = new FButton("", ButtonKind.Normal);
        public readonly BuildCard Card21 = new BuildCard(false), CardSa = new BuildCard(true);
        public readonly FCheck ChkRemember = new FCheck();
        public readonly Lbl LblHint = new Lbl { Color = Gui.Muted, Font = Gui.Small, Align = HAlign.Right };
        readonly Card needPane = new Card(null);
        readonly Pic needIcon = new Pic(Res.Gear);
        readonly Lbl needHead = new Lbl { Font = Gui.Heading, Color = Gui.Title, Wrap = true },
            needBody = new Lbl { Color = Gui.Text, Wrap = true }, needTail = new Lbl { Color = Gui.Muted, Wrap = true };
        public readonly FButton BtnClose = new FButton("", ButtonKind.Normal), BtnPick = new FButton("", ButtonKind.Normal), BtnRecheck = new FButton("", ButtonKind.Confirm);
        readonly Lbl busyText = new Lbl { Font = Gui.Heading, Color = Gui.Title, Align = HAlign.Center },
            busyDetail = new Lbl { Font = Gui.Small, Color = Gui.Muted, Align = HAlign.Center };
        readonly FProgress busyBar = new FProgress();
        View view = View.Busy;

        public View Current => view;

        public StartPanel()
        {
            Background = Gui.Brush(Gui.Window);
            ClipToBounds = true;
            Children.Add(Header);
            Header.Children.Add(BtnLang);
            foreach (var c in new Control[] { Card21, CardSa, ChkRemember, LblHint, needPane, BtnClose, BtnPick, BtnRecheck, busyText, busyBar, busyDetail })
                Children.Add(c);
            needPane.Children.Add(needIcon);
            foreach (var l in new[] { needHead, needBody, needTail }) needPane.Children.Add(l);
            busyBar.Indeterminate = true;
            SizeChanged += (s, e) => DoLayout();
            Show(View.Busy);
        }

        /// <summary>Client height of the window for the current view (its width is given).</summary>
        public double PreferredHeight(double width)
        {
            if (view == View.Selector) return 66 + 20 + CardHeight + 14 + 28 + 18;
            if (view == View.Need) return 66 + 16 + NeedPaneHeight(width) + 16 + 34 + 20;
            return 66 + 210;
        }

        double CardHeight => Card21.ArtHeight + 130;

        public void Show(View v)
        {
            view = v;
            Card21.IsVisible = CardSa.IsVisible = ChkRemember.IsVisible = LblHint.IsVisible = v == View.Selector;
            needPane.IsVisible = BtnClose.IsVisible = BtnPick.IsVisible = BtnRecheck.IsVisible = v == View.Need;
            busyText.IsVisible = busyBar.IsVisible = busyDetail.IsVisible = v == View.Busy;
            busyBar.SetActive(v == View.Busy);
            DoLayout();
            InvalidateVisual();
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
            needTail.IsVisible = !string.IsNullOrEmpty(tail);
            DoLayout();
        }

        static double NeedTextWidth(double width) => width - 2 * 20 - 2 * 16 - 56 - 16;

        double NeedPaneHeight(double width)
        {
            double tw = NeedTextWidth(width);
            double h = Gui.TextHeight(needHead.Text, Gui.Heading, tw) + 10 + Gui.TextHeight(needBody.Text, Gui.Body, tw);
            if (!string.IsNullOrEmpty(needTail.Text)) h += 10 + Gui.TextHeight(needTail.Text, Gui.Body, tw);
            return Math.Max(56, h) + 2 * 16;
        }

        public void DoLayout()
        {
            double W = Bounds.Width;
            if (W < 10) W = Width;
            if (double.IsNaN(W) || W < 10) return;
            Header.Place(0, 0, W, 66);
            BtnLang.Place(12, 14, Math.Max(96, Gui.TextWidth(BtnLang.Text, BtnLang.Font) + 24), 28);
            double y = 66;
            if (view == View.Selector)
            {
                double m = 20, gap = 20, cw = Math.Floor((W - 2 * m - gap) / 2), ch = CardHeight;
                Card21.Place(m, y + 20, cw, ch);
                CardSa.Place(m + cw + gap, y + 20, cw, ch);
                double fy = Card21.B() + 14;
                ChkRemember.Place(m, fy, ChkRemember.PreferredWidth, 28);
                LblHint.Place(ChkRemember.R() + 12, fy, W - m - ChkRemember.R() - 12, 28);
            }
            else if (view == View.Need)
            {
                double m = 20, pad = 16, icon = 56;
                double tw = NeedTextWidth(W), tx = pad + icon + pad;
                needPane.Place(m, y + 16, W - 2 * m, NeedPaneHeight(W));
                needIcon.Place(pad, pad, icon, icon);
                double ty = pad, h = Gui.TextHeight(needHead.Text, Gui.Heading, tw);
                needHead.Place(tx, ty, tw, h + 2);
                ty += h + 10;
                h = Gui.TextHeight(needBody.Text, Gui.Body, tw);
                needBody.Place(tx, ty, tw, h + 2);
                ty += h + 10;
                needTail.Place(tx, ty, tw, Gui.TextHeight(needTail.Text, Gui.Body, tw) + 2);
                double by = needPane.B() + 16, bh = 34;
                Func<FButton, double> bw = b => Math.Max(120, Gui.TextWidth(b.Text, b.Font) + 36);
                BtnClose.Place(m, by, bw(BtnClose), bh);
                BtnRecheck.Place(W - m - bw(BtnRecheck), by, bw(BtnRecheck), bh);
                BtnPick.Place(BtnRecheck.L() - 8 - bw(BtnPick), by, bw(BtnPick), bh);
            }
            else
            {
                double bw = Math.Min(480, W - 80);
                busyText.Place(20, y + 50, W - 40, 30);
                busyBar.Place(Math.Floor((W - bw) / 2), y + 92, bw, 24);
                busyDetail.Place(20, y + 126, W - 40, 40);
            }
            Header.InvalidateVisual();
        }
    }
}
