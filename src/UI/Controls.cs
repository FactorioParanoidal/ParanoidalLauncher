using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Paranoidal
{
    /// <summary>Absolute layout of the window (the 1.5.0 layout code is kept): controls sit on a Canvas at computed bounds.</summary>
    static class Lay
    {
        public static T Place<T>(this T c, double x, double y, double w, double h) where T : Control
        {
            Canvas.SetLeft(c, x);
            Canvas.SetTop(c, y);
            c.Width = Math.Max(0, w);
            c.Height = Math.Max(0, h);
            return c;
        }

        public static T Place<T>(this T c, Rect r) where T : Control => c.Place(r.X, r.Y, r.Width, r.Height);

        public static double L(this Control c) => Canvas.GetLeft(c);
        public static double T(this Control c) => Canvas.GetTop(c);
        public static double R(this Control c) => Canvas.GetLeft(c) + (double.IsNaN(c.Width) ? 0 : c.Width);
        public static double B(this Control c) => Canvas.GetTop(c) + (double.IsNaN(c.Height) ? 0 : c.Height);
        public static double W(this Control c) => double.IsNaN(c.Width) ? 0 : c.Width;
        public static double H(this Control c) => double.IsNaN(c.Height) ? 0 : c.Height;
        public static Rect Box(this Control c) => new Rect(c.L(), c.T(), c.W(), c.H());
    }

    /// <summary>
    /// A Canvas that paints itself: Avalonia 12 seals Panel.Render, so panes that draw their frame lay out their children
    /// here (Canvas.Left/Top and the size set by Lay.Place) and override Render like a Control.
    /// </summary>
    class DrawCanvas : Control
    {
        public Avalonia.Controls.Controls Children { get; } = new Avalonia.Controls.Controls();

        static DrawCanvas()
        {
            // what Canvas gets from AffectsParentArrange (internal in Avalonia 12): a moved child is arranged again
            Canvas.LeftProperty.Changed.AddClassHandler<Control>((c, e) => (c.Parent as DrawCanvas)?.InvalidateArrange());
            Canvas.TopProperty.Changed.AddClassHandler<Control>((c, e) => (c.Parent as DrawCanvas)?.InvalidateArrange());
        }

        public DrawCanvas() { Children.CollectionChanged += ChildrenChanged; }

        void ChildrenChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (Control c in e.OldItems) { LogicalChildren.Remove(c); VisualChildren.Remove(c); }
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                LogicalChildren.Clear();
                VisualChildren.Clear();
                foreach (var c in Children) { LogicalChildren.Add(c); VisualChildren.Add(c); }
            }
            else if (e.NewItems != null)
                foreach (Control c in e.NewItems) { LogicalChildren.Add(c); VisualChildren.Add(c); }
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            foreach (var c in Children) c.Measure(inf);
            return new Size();
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (var c in Children)
            {
                double x = Canvas.GetLeft(c), y = Canvas.GetTop(c);
                c.Arrange(new Rect(new Point(double.IsNaN(x) ? 0 : x, double.IsNaN(y) ? 0 : y), c.DesiredSize));
            }
            return finalSize;
        }
    }

    /// <summary>Tooltips drawn like the game's: dark box, light border, white text, wrapped at a readable width.</summary>
    static class Tips
    {
        public static void Set(Control c, string text)
        {
            if (string.IsNullOrEmpty(text)) { ToolTip.SetTip(c, null); return; }
            ToolTip.SetTip(c, new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 620,
                Foreground = Gui.Brush(Gui.Text),
                FontFamily = Gui.Body.Family,
                FontSize = Gui.Body.Size,
            });
            ToolTip.SetShowDelay(c, 400);
        }
    }

    /// <summary>Images embedded in the exe.</summary>
    static partial class Res
    {
        static Bitmap gear, plate;
        public static Bitmap Gear => gear ?? (gear = Load("gear.png"));
        /// <summary>The "SPACE AGE" plate of the game's Factorio Space Age logo (under the animated logo of Space Age).</summary>
        public static Bitmap SpaceAgePlate => plate ?? (plate = Load("space-age-plate.png"));

        static Bitmap Load(string name)
        {
            try
            {
                using (var s = Open(name)) return s == null ? null : new Bitmap(s);
            }
            catch { return null; }
        }

        public static WindowIcon AppIcon()
        {
            try
            {
                using (var s = Open("app.ico")) return s == null ? null : new WindowIcon(s);
            }
            catch { return null; }
        }
    }

    /// <summary>A control drawn in code that tracks the mouse and raises Click (the left button released over it).</summary>
    abstract class Painted : Control
    {
        protected bool hover, down;
        public event Action Click;
        string text = "";

        public string Text
        {
            get { return text; }
            set { if (text != (value ?? "")) { text = value ?? ""; InvalidateVisual(); } }
        }

        protected Painted() { ClipToBounds = true; }

        /// <summary>The hand cursor while it can be clicked.</summary>
        protected void Clickable(bool on) { Cursor = on ? new Cursor(StandardCursorType.Hand) : Cursor.Default; }

        protected override void OnPointerEntered(PointerEventArgs e) { hover = true; InvalidateVisual(); base.OnPointerEntered(e); }
        protected override void OnPointerExited(PointerEventArgs e) { hover = false; down = false; InvalidateVisual(); base.OnPointerExited(e); }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { down = true; e.Pointer.Capture(this); InvalidateVisual(); }
            base.OnPointerPressed(e);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            bool was = down;
            down = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            base.OnPointerReleased(e);
            var p = e.GetPosition(this);
            if (was && IsEffectivelyEnabled && p.X >= 0 && p.Y >= 0 && p.X < Bounds.Width && p.Y < Bounds.Height) OnClick();
        }

        protected virtual void OnClick() { Click?.Invoke(); }

        /// <summary>Raises Click as if the control was clicked (keyboard, tests).</summary>
        public void PerformClick() { if (IsEffectivelyEnabled) OnClick(); }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsEnabledProperty || change.Property == IsEffectivelyEnabledProperty) { EnabledChanged(); InvalidateVisual(); }
        }

        protected virtual void EnabledChanged() { }
    }

    /// <summary>Normal: grey (button); Confirm: green (confirm_button); Danger: red (red_button); Tab: tab of a tabbed pane.</summary>
    enum ButtonKind { Normal, Confirm, Danger, Tab }

    /// <summary>A Factorio button: black text on grey (green, red), orange under the mouse; tabs as in a tabbed pane.</summary>
    sealed class FButton : Painted
    {
        ButtonKind kind;
        /// <summary>A picture drawn left of the text (the Discord button).</summary>
        public IImage Icon;
        public Size IconSize;
        bool check;
        public FFont Font;

        public bool Checked { get { return check; } set { check = value; InvalidateVisual(); } }
        public ButtonKind Kind { get { return kind; } set { if (kind != value) { kind = value; InvalidateVisual(); } } }

        public FButton(string text, ButtonKind kind, bool big = false)
        {
            Text = text;
            this.kind = kind;
            Font = big ? Gui.Heading : Gui.Bold;
            Clickable(true);
        }

        protected override void EnabledChanged() { Clickable(IsEffectivelyEnabled); }

        public override void Render(DrawingContext g)
        {
            var r = new Rect(Bounds.Size);
            if (kind == ButtonKind.Tab) { PaintTab(g, r); return; }
            Color top, body, bottom;
            bool inset = false;
            Color fore = Gui.Black;
            bool enabled = IsEffectivelyEnabled;
            if (!enabled) { top = Color.FromRgb(45, 45, 45); body = Color.FromRgb(58, 58, 58); bottom = Color.FromRgb(78, 76, 75); fore = Gui.Muted; inset = true; }
            else if (down)
            {
                inset = true;
                body = kind == ButtonKind.Confirm ? Color.FromRgb(208, 224, 148) : kind == ButtonKind.Danger ? Color.FromRgb(252, 169, 169) : Color.FromRgb(241, 191, 100);
                top = bottom = body;
            }
            else if (hover)
            {
                if (kind == ButtonKind.Confirm) { top = Color.FromRgb(161, 252, 167); body = Color.FromRgb(147, 233, 152); bottom = Color.FromRgb(36, 91, 40); }
                else if (kind == ButtonKind.Danger) { top = Color.FromRgb(254, 162, 162); body = Color.FromRgb(255, 156, 156); bottom = Color.FromRgb(95, 48, 48); }
                else { top = Color.FromRgb(247, 203, 128); body = Color.FromRgb(228, 153, 34); bottom = Color.FromRgb(127, 80, 13); }
            }
            else if (kind == ButtonKind.Confirm) { top = Color.FromRgb(139, 215, 144); body = Color.FromRgb(94, 183, 99); bottom = Color.FromRgb(20, 63, 24); }
            else if (kind == ButtonKind.Danger) { top = Color.FromRgb(251, 131, 131); body = Color.FromRgb(254, 90, 90); bottom = Color.FromRgb(92, 25, 25); }
            else { top = Color.FromRgb(201, 201, 201); body = Color.FromRgb(143, 143, 143); bottom = Color.FromRgb(50, 50, 50); }
            Gui.Bevel(g, r, top, body, bottom, inset);

            var tr = down ? r.Translate(new Vector(0, 1)) : r;
            if (Icon != null)
            {
                double gap = 7, tw = Gui.TextWidth(Text, Font), iw = IconSize.Width, ih = IconSize.Height;
                double x = Math.Max(6, Math.Round((r.Width - iw - gap - tw) / 2));
                var ir = new Rect(x, tr.Top + Math.Round((r.Height - ih) / 2), iw, ih);
                using (g.PushOpacity(enabled ? 1 : 0.4)) g.DrawImage(Icon, ir);
                Gui.DrawText(g, Text, Font, new Rect(x + iw + gap, tr.Top, Math.Max(1, r.Right - x - iw - gap), tr.Height), fore);
                return;
            }
            Gui.DrawText(g, Text, Font, tr, fore, HAlign.Center);
        }

        /// <summary>The selected tab has the colour of the pane under it and the caption colour; the others are grey buttons.</summary>
        void PaintTab(DrawingContext g, Rect r)
        {
            if (check)
            {
                Gui.Fill(g, Gui.Pane, r);
                Gui.Line(g, Gui.PaneHi, 1, 0, r.Width - 2, 0);
                Gui.Line(g, Gui.Edge, 0, 0, 0, r.Height - 1);
                Gui.Line(g, Gui.Edge, r.Width - 1, 0, r.Width - 1, r.Height - 1);
                Gui.DrawText(g, Text, Font, r, Gui.Title, HAlign.Center);
                return;
            }
            var face = new Rect(r.X, r.Y + 3, r.Width, r.Height - 3);
            if (hover) Gui.Bevel(g, face, Color.FromRgb(247, 203, 128), Color.FromRgb(230, 155, 36), Color.FromRgb(129, 85, 16), false);
            else Gui.Bevel(g, face, Color.FromRgb(201, 201, 201), Color.FromRgb(143, 143, 143), Color.FromRgb(79, 79, 79), false);
            Gui.DrawText(g, Text, Font, face, Gui.Black, HAlign.Center);
        }
    }

    /// <summary>
    /// A pane (inside_shallow_frame) with an optional subheader band: its caption in the caption colour and a short note
    /// on the right. Child controls with Tag = Card.DeepTag get a deep frame drawn around them.
    /// </summary>
    class Card : DrawCanvas
    {
        public const string DeepTag = "deep";
        public string Title;
        public string Note;
        public Color NoteColor = Gui.Muted;
        public Color Back = Gui.Pane;

        public Card(string title)
        {
            Title = title;
            ClipToBounds = true;
        }

        /// <summary>Height of the subheader band (0 without a caption).</summary>
        public double Band => string.IsNullOrEmpty(Title) ? 0 : 32;

        public override void Render(DrawingContext g)
        {
            var r = new Rect(Bounds.Size);
            Gui.Fill(g, Back, r);
            Gui.Line(g, Gui.PaneHi, 1, 0, r.Width - 2, 0);
            Gui.Line(g, Gui.Edge, 0, 0, 0, r.Height - 1);
            Gui.Line(g, Gui.Edge, r.Width - 1, 0, r.Width - 1, r.Height - 1);
            Gui.Line(g, Gui.Edge, 0, r.Height - 1, r.Width - 1, r.Height - 1);
            if (Band > 0)
            {
                Gui.Fill(g, Gui.Deep, 1, 1, r.Width - 2, Band - 1);
                Gui.Line(g, Gui.Black, 1, Band, r.Width - 2, Band);
                double noteW = string.IsNullOrEmpty(Note) ? 0 : Gui.TextWidth(Note, Gui.Small) + 12;
                Gui.DrawText(g, Title, Gui.Bold, new Rect(12, 1, Math.Max(1, r.Width - 24 - noteW), Band - 1), Gui.Title);
                if (noteW > 0) Gui.DrawText(g, Note, Gui.Small, new Rect(r.Width - 12 - noteW, 1, noteW, Band - 1), NoteColor, HAlign.Right);
            }
            foreach (var c in Children)
                if (c.IsVisible && Equals(c.Tag, DeepTag)) Gui.Frame(g, c.Box().Inflate(1), true);
        }
    }

    /// <summary>A line of text (the Label of 1.5.0): one line with an ellipsis, vertically centred, or wrapped from the top.</summary>
    sealed class Lbl : Control
    {
        string text = "";
        Color color = Gui.Text;
        public FFont Font = Gui.Body;
        public bool Wrap;
        public HAlign Align = HAlign.Left;
        public Color Back = Color.FromArgb(0, 0, 0, 0);

        public Lbl() { ClipToBounds = true; }

        public string Text { get { return text; } set { if (text != (value ?? "")) { text = value ?? ""; InvalidateVisual(); } } }
        public Color Color { get { return color; } set { if (color != value) { color = value; InvalidateVisual(); } } }

        public override void Render(DrawingContext g)
        {
            var r = new Rect(Bounds.Size);
            if (Back.A > 0) Gui.Fill(g, Back, r);
            Gui.DrawText(g, text, Font, r, color, Align, !Wrap, Wrap);
        }
    }

    /// <summary>A status line: a coloured dot (like the game's entity status) and the text; the colour never stands alone.</summary>
    sealed class StatusLine : Control
    {
        Color color = Gui.Muted;
        string text = "";
        public readonly FFont Font;
        /// <summary>No dot: a plain line of secondary text.</summary>
        public bool NoDot;

        public StatusLine(FFont font) { Font = font; ClipToBounds = true; }

        public Color Color { get { return color; } set { color = value; InvalidateVisual(); } }
        public string Text { get { return text; } set { if (text != (value ?? "")) { text = value ?? ""; InvalidateVisual(); } } }

        public override void Render(DrawingContext g)
        {
            var r = new Rect(Bounds.Size);
            double x = 0;
            if (!NoDot && !string.IsNullOrEmpty(text))
            {
                double d = Math.Max(8, Math.Round(Font.Size * 0.95 * 0.75));
                Gui.Dot(g, new Rect(0, Math.Round((r.Height - d) / 2), d, d), color);
                x = d + 8;
            }
            Gui.DrawText(g, text, Font, new Rect(x, 0, Math.Max(1, r.Width - x), r.Height), NoDot ? color : Gui.Text);
        }
    }

    /// <summary>A check box of the game: a dark square with a light check mark, the text on its right.</summary>
    class FCheck : Painted
    {
        protected bool value;
        public FFont Font = Gui.Body;
        public event Action CheckedChanged;

        public FCheck() { Clickable(true); }

        public bool Checked
        {
            get { return value; }
            set { if (this.value != value) { this.value = value; InvalidateVisual(); CheckedChanged?.Invoke(); } }
        }

        protected override void EnabledChanged() { Clickable(IsEffectivelyEnabled); }
        protected override void OnClick() { Checked = !Checked; base.OnClick(); }

        /// <summary>Click without the toggle (radio buttons).</summary>
        protected void RaiseClicked() { base.OnClick(); }

        /// <summary>Width the box and the text need.</summary>
        public double PreferredWidth => Gui.TextWidth(Text, Font) + 24;

        public override void Render(DrawingContext g)
        {
            var r = new Rect(Bounds.Size);
            bool en = IsEffectivelyEnabled;
            double s = 14, y = Math.Round((r.Height - s) / 2);
            var box = new Rect(1, y, s, s);
            DrawMark(g, box, en);
            Gui.DrawText(g, Text, Font, new Rect(s + 7, 0, Math.Max(1, r.Width - s - 7), r.Height), en ? Gui.Text : Gui.Muted);
        }

        protected virtual void DrawMark(DrawingContext g, Rect box, bool en)
        {
            Gui.Fill(g, value ? Gui.Deep : Gui.Argb(255, 40, 39, 40), box);
            Gui.Outline(g, hover && en ? Gui.Orange : Gui.DotGrey, box);
            if (value)
            {
                var geo = new StreamGeometry();
                using (var c = geo.Open())
                {
                    c.BeginFigure(new Point(box.X + 3, box.Y + 7.5), false);
                    c.LineTo(new Point(box.X + 6, box.Y + 10.5));
                    c.LineTo(new Point(box.X + 11.5, box.Y + 3.5));
                    c.EndFigure(false);
                }
                g.DrawGeometry(null, new Pen(Gui.Brush(en ? Gui.Text : Gui.Muted), 2), geo);
            }
        }
    }

    /// <summary>A radio button: one of a group (the same Group object) is chosen.</summary>
    sealed class FRadio : FCheck
    {
        public List<FRadio> Group;

        protected override void OnClick()
        {
            if (Group != null) foreach (var o in Group) if (o != this) o.Checked = false;
            if (!value) Checked = true;
            RaiseClicked();
        }

        protected override void DrawMark(DrawingContext g, Rect box, bool en)
        {
            g.DrawEllipse(Gui.Brush(Gui.Argb(255, 40, 39, 40)), Gui.Pen(hover && en ? Gui.Orange : Gui.DotGrey), box.Deflate(.5));
            if (value) g.DrawEllipse(Gui.Brush(en ? Gui.Text : Gui.Muted), null, box.Deflate(4));
        }
    }

    /// <summary>
    /// Header with the animated PARANOIDAL logo (LogoArt; for Space Age with the plate of the game's logo) and hazard
    /// stripes: the one distinctive element of the window. With a Title (the start window) it is a low strip with the
    /// title instead of the logo.
    /// </summary>
    sealed class HeaderPanel : DrawCanvas
    {
        /// <summary>The logo of the Space Age build (PARANOIDAL with the SPACE AGE plate).</summary>
        public bool SpaceAge;
        /// <summary>The start window: the title in the middle, no logo and no version lines.</summary>
        public string Title;
        /// <summary>Widths taken by the buttons at the left and at the right (set by the window layout): the logo stays between them.</summary>
        public double LeftReserve, RightReserve;
        readonly Action repaint;

        public HeaderPanel()
        {
            ClipToBounds = true;
            repaint = () => { if (IsVisible && Title == null) InvalidateVisual(); };
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); LogoArt.Attach(repaint); }
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { LogoArt.Detach(repaint); base.OnDetachedFromVisualTree(e); }

        public override void Render(DrawingContext g)
        {
            var r = new Rect(Bounds.Size);
            double stripe = 9;
            var top = new Rect(0, 0, r.Width, r.Height - stripe);
            g.FillRectangle(Gui.VGradient(Color.FromRgb(52, 48, 44), Color.FromRgb(22, 21, 21)), top);
            using (g.PushClip(top))
                Gui.Glow(g, new Rect(r.Width / 2 - r.Width / 3, -top.Height / 2, r.Width * 2 / 3, top.Height * 2), Color.FromArgb(70, 255, 150, 30));
            if (Title != null)
                Gui.DrawText(g, Title, Gui.Big, new Rect(130, 0, Math.Max(1, r.Width - 260), top.Height), Gui.Title, HAlign.Center);
            else
            {
                // the logo is centred in the window, clear of the button columns at both sides
                double half = Math.Max(60, Math.Min(r.Width / 2 - LeftReserve, r.Width / 2 - RightReserve) - 16);
                var box = new Rect(r.Width / 2 - half, 8, 2 * half, top.Height - 16);
                LogoArt.Draw(g, box, SpaceAge, SpaceAge ? 420 : 600);
                double right = r.Width - 12;
                Gui.DrawText(g, AppInfo.Name + " " + AppInfo.Version, Gui.Small, new Rect(0, 4, right, 18), Gui.Muted, HAlign.Right);
                // the game the build needs (same wording as in the panes and dialogs), not the game that was found
                Gui.DrawText(g, Tr.T("для ", "for ") + AppInfo.RequiredGame, Gui.Small, new Rect(0, top.Height - 21, right, 18), Gui.Muted, HAlign.Right);
            }
            Gui.Hazard(g, new Rect(0, top.Bottom, r.Width, stripe), 0, Gui.Yellow, Color.FromRgb(20, 20, 20));
            base.Render(g);
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
        double offset;
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        public bool Active;
        static readonly Color Bar = Color.FromRgb(70, 196, 64);

        public FProgress()
        {
            ClipToBounds = true;
            timer.Tick += (s, e) => { offset += 4; InvalidateVisual(); };
        }

        public double Value { get { return value; } set { this.value = Math.Max(0, Math.Min(1, value)); InvalidateVisual(); } }

        public bool Indeterminate
        {
            get { return indeterminate; }
            set { indeterminate = value; Sync(); InvalidateVisual(); }
        }

        public void SetActive(bool a) { Active = a; Sync(); InvalidateVisual(); }

        void Sync() { timer.IsEnabled = Active && indeterminate; }

        static void FillBar(DrawingContext g, Rect fill)
        {
            if (fill.Width <= 0) return;
            g.FillRectangle(Gui.VGradient(Gui.Light(Bar, .45), Gui.Dark(Bar, .25)), fill);
        }

        public override void Render(DrawingContext g)
        {
            var r = new Rect(Bounds.Size);
            Gui.Fill(g, Color.FromRgb(60, 58, 60), r);
            Gui.Line(g, Color.FromRgb(9, 9, 9), 0, 0, r.Width - 1, 0);
            Gui.Line(g, Color.FromRgb(9, 9, 9), 0, 0, 0, r.Height - 1);
            Gui.Line(g, Color.FromRgb(18, 18, 18), 1, 1, r.Width - 2, 1);
            Gui.Line(g, Color.FromRgb(105, 104, 104), 0, r.Height - 1, r.Width - 1, r.Height - 1);
            Gui.Line(g, Color.FromRgb(105, 104, 104), r.Width - 1, 0, r.Width - 1, r.Height - 1);
            var inner = new Rect(2, 3, Math.Max(0, r.Width - 4), Math.Max(0, r.Height - 5));
            if (indeterminate && Active)
            {
                double w = Math.Max(40, inner.Width / 6), span = inner.Width + w;
                double x = inner.X - w + (offset % span);
                FillBar(g, inner.Intersect(new Rect(x, inner.Y, w, inner.Height)));
                return;
            }
            if (value <= 0) return;
            var bar = new Rect(inner.X, inner.Y, Math.Max(1, Math.Floor(inner.Width * value)), inner.Height);
            FillBar(g, bar);
            var txt = (value * 100).ToString(value < 0.1 ? "0.0" : "0", Fmt.Ru) + " %";
            Gui.DrawText(g, txt, Gui.Bold, r, Gui.Text, HAlign.Center);
            using (g.PushClip(bar)) Gui.DrawText(g, txt, Gui.Bold, r, Gui.Black, HAlign.Center);
        }
    }

    /// <summary>
    /// Read-only text with coloured lines, selectable and copyable (the RichTextBox of 1.5.0): the journal, the lists in
    /// dialogs, the changes of a launcher update.
    /// </summary>
    sealed class LogBox : ScrollViewer
    {
        readonly SelectableTextBlock block;
        readonly StringBuilder plain = new StringBuilder();
        public FFont Font;

        public LogBox(FFont font, bool wrap = true)
        {
            Font = font;
            block = new SelectableTextBlock
            {
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                FontFamily = font.Family,
                FontSize = font.Size,
                Foreground = Gui.Brush(Gui.Text),
                Padding = new Thickness(4, 2, 4, 2),
                SelectionBrush = Gui.Brush(Color.FromRgb(90, 80, 60)),
            };
            block.Inlines = new InlineCollection();
            Content = block;
            Background = Gui.Brush(Gui.Deep);
            HorizontalScrollBarVisibility = wrap ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            Tag = Card.DeepTag;
        }

        /// <summary>The whole text (the report's journal, the test journal).</summary>
        public string Text => plain.ToString();

        public void Append(string text, Color color, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            var run = new Run(text) { Foreground = Gui.Brush(color) };
            if (bold) run.FontWeight = FontWeight.Bold;
            block.Inlines.Add(run);
            plain.Append(text);
        }

        public void Clear()
        {
            block.Inlines.Clear();
            plain.Clear();
        }

        public new void ScrollToEnd() { Dispatcher.UIThread.Post(() => Offset = new Vector(Offset.X, double.MaxValue), DispatcherPriority.Background); }
    }
}

namespace Paranoidal
{
    /// <summary>A row of an FList: its cells and the colour of its text.</summary>
    sealed class ListItem
    {
        public string[] Cells;
        public Avalonia.Media.Color Color = Gui.Text;
        public ListItem(Avalonia.Media.Color color, params string[] cells) { Color = color; Cells = cells; }
    }

    /// <summary>
    /// A list with column headers (the ListView of 1.5.0): the header row in the list header colour with captions in the
    /// caption colour, the rows in their own text colours, one row can be selected; one column takes the free width.
    /// </summary>
    sealed class FList : Avalonia.Controls.Grid
    {
        public const double RowH = 20, HeadH = 24;
        readonly double[] widths;
        readonly int fill;
        public readonly string[] Titles;
        public readonly System.Collections.Generic.List<ListItem> Items = new System.Collections.Generic.List<ListItem>();
        public int Selected = -1;
        readonly Head head;
        readonly Rows rows;
        readonly Avalonia.Controls.ScrollViewer sv;

        public FList(double[] widths, int fill)
        {
            this.widths = widths;
            this.fill = fill;
            Titles = new string[widths.Length];
            RowDefinitions = new Avalonia.Controls.RowDefinitions("Auto,*");
            head = new Head(this) { Height = HeadH };
            rows = new Rows(this);
            sv = new Avalonia.Controls.ScrollViewer
            {
                Content = rows,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            };
            SetRow(head, 0);
            SetRow(sv, 1);
            Children.Add(head);
            Children.Add(sv);
            Background = Gui.Brush(Gui.Deep);
            Tag = Card.DeepTag;
            ClipToBounds = true;
        }

        public void SetTitle(int i, string t) { Titles[i] = t; head.InvalidateVisual(); }

        public void SetItems(System.Collections.Generic.IEnumerable<ListItem> items)
        {
            Items.Clear();
            if (items != null) Items.AddRange(items);
            Selected = -1;
            rows.InvalidateMeasure();
            rows.InvalidateVisual();
        }

        /// <summary>Column widths for the total width: the fill column takes what the others leave.</summary>
        double[] Columns(double total)
        {
            var w = (double[])widths.Clone();
            double other = 0;
            for (int i = 0; i < w.Length; i++) if (i != fill) other += w[i];
            w[fill] = System.Math.Max(120, total - other - 2);
            return w;
        }

        sealed class Head : Avalonia.Controls.Control
        {
            readonly FList list;
            public Head(FList l) { list = l; }

            public override void Render(Avalonia.Media.DrawingContext g)
            {
                var r = new Avalonia.Rect(Bounds.Size);
                Gui.Fill(g, Gui.Header, r);
                var cols = list.Columns(list.Bounds.Width);
                double x = 0;
                for (int i = 0; i < cols.Length; i++)
                {
                    var c = new Avalonia.Rect(x, 0, cols[i], r.Height);
                    Gui.Line(g, Gui.Edge, c.Right - 1, 0, c.Right - 1, r.Height - 1);
                    Gui.DrawText(g, list.Titles[i], Gui.Bold, c.Deflate(new Avalonia.Thickness(6, 0)), Gui.Title);
                    x += cols[i];
                }
                Gui.Line(g, Gui.Edge, 0, r.Height - 1, r.Width - 1, r.Height - 1);
            }
        }

        sealed class Rows : Avalonia.Controls.Control
        {
            readonly FList list;
            public Rows(FList l) { list = l; ClipToBounds = true; }

            protected override Avalonia.Size MeasureOverride(Avalonia.Size available) => new Avalonia.Size(0, list.Items.Count * RowH + 2);

            protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
            {
                int i = (int)(e.GetPosition(this).Y / RowH);
                list.Selected = i >= 0 && i < list.Items.Count ? i : -1;
                InvalidateVisual();
                base.OnPointerPressed(e);
            }

            public override void Render(Avalonia.Media.DrawingContext g)
            {
                var cols = list.Columns(list.Bounds.Width);
                for (int i = 0; i < list.Items.Count; i++)
                {
                    var it = list.Items[i];
                    double y = i * RowH;
                    if (i == list.Selected) Gui.Fill(g, Avalonia.Media.Color.FromRgb(70, 62, 50), 0, y, Bounds.Width, RowH);
                    double x = 0;
                    for (int c = 0; c < cols.Length && c < it.Cells.Length; c++)
                    {
                        Gui.DrawText(g, it.Cells[c], Gui.Body, new Avalonia.Rect(x + 6, y, System.Math.Max(1, cols[c] - 10), RowH), it.Color);
                        x += cols[c];
                    }
                }
            }
        }
    }
}
