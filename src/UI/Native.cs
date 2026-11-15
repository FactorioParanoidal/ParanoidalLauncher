using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Paranoidal
{
    /// <summary>
    /// The system folder and file dialogs (the Explorer-style window on Windows, the portal or GTK dialog elsewhere) are
    /// Avalonia's StorageProvider pickers, called in place; these two helpers give them a start folder from a path and
    /// turn the answer into a local path.
    /// </summary>
    static class PickerPaths
    {
        /// <summary>The folder a picker opens in, or null (no such folder: the system default).</summary>
        public static async Task<IStorageFolder> StartAt(this IStorageProvider sp, string initial)
        {
            try { return string.IsNullOrEmpty(initial) || !Directory.Exists(initial) ? null : await sp.TryGetFolderFromPathAsync(initial); }
            catch { return null; }
        }

        /// <summary>The local path of the chosen folder or file; null when nothing was chosen or the picker failed.</summary>
        public static async Task<string> LocalPath<T>(this Task<IReadOnlyList<T>> pick) where T : IStorageItem
        {
            try
            {
                var item = (await pick).FirstOrDefault();
                var path = item == null ? null : item.TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) return null;
                return item is IStorageFolder ? Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;
            }
            catch { return null; }
        }
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
        static bool listening;

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
            if (listening) return;
            listening = true;
            _ = Serve(show);
        }

        // one connection at a time, awaited on the thread pool (no thread of its own); never ends
        static async Task Serve(Action show)
        {
            while (true)
            {
                try
                {
                    using (var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous))
                    {
                        await s.WaitForConnectionAsync().ConfigureAwait(false);
                        try { await s.ReadAsync(new byte[1], 0, 1).ConfigureAwait(false); } catch { }
                    }
                    Dispatcher.UIThread.Post(show);
                }
                catch { await Task.Delay(1000).ConfigureAwait(false); }
            }
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
