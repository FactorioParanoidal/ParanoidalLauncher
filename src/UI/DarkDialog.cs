using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Paranoidal
{
    enum DialogResult { None, Yes, No, Cancel, OK }

    /// <summary>A dialog window of the game's look: a Canvas laid out in code, Esc and Enter handled by the caller.</summary>
    sealed class DialogWindow : Window
    {
        public readonly Canvas Root = new Canvas();
        public DialogResult Result = DialogResult.Cancel;
        public Action<KeyEventArgs> Keys;

        public DialogWindow(string title, double width, double height)
        {
            Title = title;
            Width = width;
            Height = height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Gui.Brush(Gui.Window);
            var icon = Res.AppIcon();
            if (icon != null) Icon = icon;
            Content = Root;
        }

        public void Finish(DialogResult r) { Result = r; Close(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            Keys?.Invoke(e);
            base.OnKeyDown(e);
        }

        /// <summary>Client size: the window fits the content laid out at that size.</summary>
        public void SetClient(double w, double h) { Width = w; Height = h; }
    }

    /// <summary>
    /// A message or question in a game dialog: the text in a pane, the buttons under it, the way back on the left and the
    /// confirmation on the right (green; red for a destructive one). Enter confirms, Esc goes back.
    /// </summary>
    static class DarkDialog
    {
        /// <summary>Tests only (PARANOIDAL_LAUNCHER_TEST_AUTO): no dialog is shown, its text goes here and the answer is "yes".</summary>
        public static Action<string> Headless;
        /// <summary>UI pictures only (UiShot): receives the dialog instead of showing it.</summary>
        public static Action<Window> TestSnapshot;

        public static DialogResult Ask(Window owner, string title, string message, string sub, IList<string> details,
            string yesText, string noText, Color accent, ButtonKind yesKind = ButtonKind.Confirm)
        {
            if (Headless != null)
            {
                Headless(title + ": " + message + (string.IsNullOrEmpty(sub) ? "" : " " + sub.Replace("\n", " ")));
                return DialogResult.Yes;
            }
            double width = details != null ? 640 : 540, m = 12, pad = 12, iconSize = 48;
            var f = new DialogWindow(title, width, 200);
            var pane = new Card(null);
            f.Root.Children.Add(pane);
            double pw = width - 2 * m;
            double textX = pad + iconSize + 12, textW = pw - textX - pad;

            pane.Children.Add(new Pic(Res.Gear).Place(pad, pad, iconSize, iconSize));
            double y = pad;
            var mh = Gui.TextHeight(message, Gui.Heading, textW);
            pane.Children.Add(new Lbl { Text = message, Font = Gui.Heading, Color = accent, Wrap = true }.Place(textX, y, textW, mh + 4));
            y += mh + 8;
            if (!string.IsNullOrEmpty(sub))
            {
                var sh = Gui.TextHeight(sub, Gui.Body, textW);
                pane.Children.Add(new Lbl { Text = sub, Color = Gui.Text, Wrap = true }.Place(textX, y, textW, sh + 4));
                y += sh + 4;
            }
            y = Math.Max(y, pad + iconSize) + pad;
            if (details != null && details.Count > 0)
            {
                var box = new LogBox(Gui.CodeSmall, false);
                box.Append(string.Join("\n", details), Gui.Text);
                pane.Children.Add(box.Place(pad, y, pw - 2 * pad, 180));
                y += 180 + pad;
            }
            pane.Place(m, m, pw, y);
            y = pane.B() + m;

            double bh = 36;
            double yw = Math.Max(150, Gui.TextWidth(yesText, Gui.Bold) + 40);
            var yesBtn = new FButton(yesText, yesKind).Place(width - m - yw, y, yw, bh);
            yesBtn.Click += () => f.Finish(DialogResult.Yes);
            f.Root.Children.Add(yesBtn);
            if (noText != null)
            {
                double w = Math.Max(130, Gui.TextWidth(noText, Gui.Bold) + 40);
                var noBtn = new FButton(noText, ButtonKind.Normal).Place(m, y, w, bh);
                noBtn.Click += () => f.Finish(DialogResult.No);
                f.Root.Children.Add(noBtn);
            }
            y += bh + m;

            f.SetClient(width, y);
            f.Keys = e =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; f.Finish(DialogResult.Yes); }
                else if (e.Key == Key.Escape) { e.Handled = true; f.Finish(noText == null ? DialogResult.Yes : DialogResult.Cancel); }
            };
            if (TestSnapshot != null) { TestSnapshot(f); return DialogResult.Cancel; }
            Modal.Run(f, owner);
            return f.Result;
        }

        public static void Info(Window owner, string title, string message, Color accent)
        {
            Ask(owner, title, message, null, null, "OK", null, accent);
        }
    }
}
