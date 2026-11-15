using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Simple;

namespace Paranoidal
{
    /// <summary>The Avalonia application: the dark simple theme (scroll bars, text boxes, tooltips) under the controls drawn in code.</summary>
    sealed class App : Application
    {
        public override void Initialize()
        {
            RequestedThemeVariant = ThemeVariant.Dark;
            Styles.Add(new SimpleTheme());
            // tooltips of the game: dark box, light border
            Styles.Add(new Style(x => x.OfType<ToolTip>())
            {
                Setters =
                {
                    new Setter(ToolTip.BackgroundProperty, Gui.Brush(Color.FromRgb(36, 35, 36))),
                    new Setter(ToolTip.BorderBrushProperty, Gui.Brush(Color.FromRgb(128, 126, 124))),
                    new Setter(ToolTip.BorderThicknessProperty, new Thickness(1)),
                    new Setter(ToolTip.PaddingProperty, new Thickness(10, 7)),
                    new Setter(ToolTip.ForegroundProperty, Gui.Brush(Gui.Text)),
                },
            });
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // the desktop start (Program.cs); the UI pictures tool runs without a lifetime and makes its own windows
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.MainWindow = new MainWindow();
            base.OnFrameworkInitializationCompleted();
        }
    }
}
