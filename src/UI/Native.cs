using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Paranoidal
{
    /// <summary>
    /// Modal windows and waits on the UI thread. The 1.5.0 code asks questions synchronously (WinForms ShowDialog):
    /// Avalonia's dialogs are asynchronous, so the UI thread runs a nested dispatcher frame until the answer is there.
    /// </summary>
    static class Modal
    {
        /// <summary>Shows the window as a dialog of `owner` (or alone) and returns when it is closed.</summary>
        public static void Run(Window w, Window owner)
        {
            var frame = new DispatcherFrame();
            w.Closed += (s, e) => frame.Continue = false;
            if (owner != null && owner.IsVisible) w.ShowDialog(owner);
            else w.Show();
            Dispatcher.UIThread.PushFrame(frame);
        }

        /// <summary>The result of a task, the UI thread keeps working meanwhile.</summary>
        public static T Wait<T>(Task<T> t)
        {
            if (!t.IsCompleted)
            {
                var frame = new DispatcherFrame();
                t.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
                Dispatcher.UIThread.PushFrame(frame);
            }
            return t.GetAwaiter().GetResult();
        }

        public static void Wait(Task t) => Wait(t.ContinueWith(x => { x.GetAwaiter().GetResult(); return true; }, TaskScheduler.Default));
    }

    /// <summary>Folder and file choice (the Explorer-style window on Windows, the portal or GTK dialog elsewhere).</summary>
    static class Pickers
    {
        static async Task<IStorageFolder> Start(IStorageProvider sp, string initial)
        {
            try { return string.IsNullOrEmpty(initial) || !Directory.Exists(initial) ? null : await sp.TryGetFolderFromPathAsync(initial); }
            catch { return null; }
        }

        /// <summary>The chosen folder or null.</summary>
        public static string Folder(Window owner, string title, string initial)
        {
            try
            {
                var sp = owner.StorageProvider;
                var list = Modal.Wait(PickFolder(sp, title, initial));
                var f = list == null ? null : list.FirstOrDefault();
                var path = f == null ? null : f.TryGetLocalPath();
                return string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch { return null; }
        }

        static async Task<IReadOnlyList<IStorageFolder>> PickFolder(IStorageProvider sp, string title, string initial) =>
            await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = await Start(sp, initial) });

        /// <summary>A chosen file (filter: a name and its patterns like *.zip) or null.</summary>
        public static string File(Window owner, string title, string initial, string filterName, params string[] patterns)
        {
            try
            {
                var sp = owner.StorageProvider;
                var list = Modal.Wait(PickFile(sp, title, initial, filterName, patterns));
                var f = list == null ? null : list.FirstOrDefault();
                var path = f == null ? null : f.TryGetLocalPath();
                return string.IsNullOrEmpty(path) ? null : path;
            }
            catch { return null; }
        }

        static async Task<IReadOnlyList<IStorageFile>> PickFile(IStorageProvider sp, string title, string initial, string filterName, string[] patterns) =>
            await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = await Start(sp, initial),
                FileTypeFilter = new[] { new FilePickerFileType(filterName) { Patterns = patterns } },
            });
    }

    /// <summary>Opening links, folders and files with the programs of the system.</summary>
    static class Shell
    {
        public static void Open(string target)
        {
            if (OperatingSystem.IsWindows()) { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); return; }
            var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            var psi = new ProcessStartInfo(opener) { UseShellExecute = false };
            psi.ArgumentList.Add(target);
            using (Process.Start(psi)) { }
        }

        /// <summary>A folder in the file manager.</summary>
        public static void OpenFolder(string dir)
        {
            if (OperatingSystem.IsWindows()) { Process.Start("explorer.exe", FsUtil.QuoteArg(dir)); return; }
            Open(dir);
        }

        /// <summary>The file selected in its folder (Windows); elsewhere its folder.</summary>
        public static void Reveal(string file)
        {
            if (OperatingSystem.IsWindows()) { Process.Start("explorer.exe", "/select," + FsUtil.QuoteArg(file)); return; }
            Open(Path.GetDirectoryName(file));
        }
    }

    /// <summary>The clipboard of a window, synchronously.</summary>
    static class ClipText
    {
        static IClipboard Of(Window w) => TopLevel.GetTopLevel(w)?.Clipboard;

        public static void SetText(Window w, string text)
        {
            var c = Of(w);
            if (c == null) throw new InvalidOperationException("clipboard is not available");
            Modal.Wait(c.SetTextAsync(text));
        }

        public static string GetText(Window w)
        {
            var c = Of(w);
            return c == null ? null : Modal.Wait(c.TryGetTextAsync());
        }
    }

    /// <summary>
    /// The Discord mark drawn in code (no image file): the white logo on the Discord blurple rounded square. The outline
    /// follows the 24×24 mark of Discord's brand kit, its tiny corner arcs replaced by lines.
    /// </summary>
    static class DiscordIcon
    {
        public static readonly Color Blurple = Color.FromRgb(88, 101, 242);

        public static IImage Create(double size)
        {
            var group = new DrawingGroup();
            double r = size * 0.24;
            group.Children.Add(new GeometryDrawing { Brush = Gui.Brush(Blurple), Geometry = new RectangleGeometry(new Rect(0, 0, size, size), r, r) });
            // the mark is 24 units wide, 18.3 high; scaled into ~70 % of the square and centred
            double k = size * 0.70 / 24, ox = (size - 24 * k) / 2, oy = (size - 18.3 * k) / 2 - 2.85 * k;
            Func<double, double, Point> P = (x, y) => new Point(ox + x * k, oy + y * k);
            var path = new StreamGeometry();
            using (var c = path.Open())
            {
                c.BeginFigure(P(20.317, 4.370), true);
                c.LineTo(P(15.432, 2.855));
                c.LineTo(P(15.353, 2.892));
                c.CubicBezierTo(P(15.142, 3.267), P(14.909, 3.757), P(14.745, 4.141));
                c.CubicBezierTo(P(12.900, 3.865), P(11.065, 3.865), P(9.258, 4.141));
                c.CubicBezierTo(P(9.095, 3.748), P(8.853, 3.267), P(8.641, 2.892));
                c.LineTo(P(8.562, 2.855));
                c.LineTo(P(3.677, 4.370));
                c.LineTo(P(3.645, 4.397));
                c.CubicBezierTo(P(0.533, 9.046), P(-0.319, 13.580), P(0.099, 18.058));
                c.LineTo(P(0.130, 18.114));
                c.CubicBezierTo(P(2.183, 19.622), P(4.172, 20.537), P(6.123, 21.143));
                c.LineTo(P(6.208, 21.116));
                c.CubicBezierTo(P(6.669, 20.485), P(7.081, 19.820), P(7.434, 19.122));
                c.LineTo(P(7.392, 19.016));
                c.CubicBezierTo(P(6.739, 18.768), P(6.118, 18.466), P(5.520, 18.124));
                c.LineTo(P(5.512, 17.996));
                c.CubicBezierTo(P(5.638, 17.902), P(5.764, 17.804), P(5.884, 17.705));
                c.LineTo(P(5.962, 17.694));
                c.CubicBezierTo(P(9.889, 19.487), P(14.142, 19.487), P(18.023, 17.694));
                c.LineTo(P(18.101, 17.703));
                c.CubicBezierTo(P(18.222, 17.802), P(18.347, 17.901), P(18.474, 17.996));
                c.LineTo(P(18.468, 18.123));
                c.LineTo(P(16.595, 19.015));
                c.LineTo(P(16.554, 19.122));
                c.CubicBezierTo(P(16.914, 19.820), P(17.326, 20.485), P(17.779, 21.115));
                c.LineTo(P(17.863, 21.143));
                c.CubicBezierTo(P(19.824, 20.537), P(21.813, 19.622), P(23.866, 18.114));
                c.LineTo(P(23.897, 18.059));
                c.CubicBezierTo(P(24.397, 12.882), P(23.059, 8.385), P(20.348, 4.398));
                c.EndFigure(true);
            }
            group.Children.Add(new GeometryDrawing { Brush = Brushes.White, Geometry = path });
            foreach (var cx in new[] { 8.020, 15.995 })
                group.Children.Add(new GeometryDrawing
                {
                    Brush = Gui.Brush(Blurple),
                    Geometry = new EllipseGeometry(new Rect(ox + (cx - 2.157) * k, oy + (12.912 - 2.419) * k, 2 * 2.157 * k, 2 * 2.419 * k)),
                });
            return new DrawingImage(group);
        }
    }

    /// <summary>
    /// The launcher started a second time: the running one shows its window (Program.cs, MainWindow). One per user
    /// session: the named mutex of 1.5.0 (Local\ParanoidalLauncher) and a named pipe for "show your window".
    /// </summary>
    static class SingleInstance
    {
        static readonly string PipeName = "ParanoidalLauncher.ShowWindow." + Environment.UserName;
        static Thread server;

        /// <summary>Second start: asks the running launcher to show its window (it may take the foreground).</summary>
        public static void Signal()
        {
            if (OperatingSystem.IsWindows()) { try { AllowSetForegroundWindow(-1); } catch { } }   // ASFW_ANY
            try
            {
                using (var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    c.Connect(1500);
                    c.WriteByte(1);
                    c.Flush();
                }
            }
            catch { }
        }

        /// <summary>The running launcher: `show` runs on the UI thread at every second start.</summary>
        public static void Listen(Action show)
        {
            if (server != null) return;
            server = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly))
                        {
                            s.WaitForConnection();
                            try { s.ReadByte(); } catch { }
                        }
                        Dispatcher.UIThread.Post(show);
                    }
                    catch { Thread.Sleep(1000); }
                }
            }) { IsBackground = true, Name = "single-instance" };
            server.Start();
        }

        /// <summary>Restores a minimised window and brings it to the front.</summary>
        public static void BringUp(Window w)
        {
            if (!w.IsVisible) w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Topmost = true;
            w.Topmost = false;
            w.Activate();
        }

        /// <summary>A message above every window, without an owner in this process (the second start).</summary>
        public static void Info(string text, string caption)
        {
            if (OperatingSystem.IsWindows())
            {
                MessageBoxW(IntPtr.Zero, text, caption, 0x40 | 0x10000 | 0x40000);   // MB_ICONINFORMATION | MB_SETFOREGROUND | MB_TOPMOST
                return;
            }
            Console.Error.WriteLine(caption + ": " + text);
        }

        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
    }
}
