using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Paranoidal
{
    static class Native
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public static void DarkTitle(IntPtr hwnd)
        {
            int on = 1;
            try { if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4); } catch { }
        }

        public static void DarkScrollbars(Control c)
        {
            c.HandleCreated += (s, e) => { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
        }
    }

    /// <summary>
    /// The Discord mark drawn in code (no image file): the white logo on the Discord blurple rounded square. The outline
    /// follows the 24×24 mark of Discord's brand kit, its tiny corner arcs replaced by lines.
    /// </summary>
    static class DiscordIcon
    {
        public static readonly Color Blurple = Color.FromArgb(88, 101, 242);

        public static Bitmap Create(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float r = size * 0.24f;
                using (var bg = new GraphicsPath())
                {
                    bg.AddArc(0, 0, 2 * r, 2 * r, 180, 90);
                    bg.AddArc(size - 2 * r - 1, 0, 2 * r, 2 * r, 270, 90);
                    bg.AddArc(size - 2 * r - 1, size - 2 * r - 1, 2 * r, 2 * r, 0, 90);
                    bg.AddArc(0, size - 2 * r - 1, 2 * r, 2 * r, 90, 90);
                    bg.CloseFigure();
                    using (var b = new SolidBrush(Blurple)) g.FillPath(b, bg);
                }
                // the mark is 24 units wide, 18.3 high; scaled into ~70 % of the square and centred
                float k = size * 0.70f / 24f, ox = (size - 24 * k) / 2f, oy = (size - 18.3f * k) / 2f - 2.85f * k;
                Func<float, float, PointF> P = (x, y) => new PointF(ox + x * k, oy + y * k);
                using (var path = new GraphicsPath())
                {
                    path.StartFigure();
                    path.AddLine(P(20.317f, 4.370f), P(15.432f, 2.855f));
                    path.AddLine(P(15.432f, 2.855f), P(15.353f, 2.892f));
                    path.AddBezier(P(15.353f, 2.892f), P(15.142f, 3.267f), P(14.909f, 3.757f), P(14.745f, 4.141f));
                    path.AddBezier(P(14.745f, 4.141f), P(12.900f, 3.865f), P(11.065f, 3.865f), P(9.258f, 4.141f));
                    path.AddBezier(P(9.258f, 4.141f), P(9.095f, 3.748f), P(8.853f, 3.267f), P(8.641f, 2.892f));
                    path.AddLine(P(8.641f, 2.892f), P(8.562f, 2.855f));
                    path.AddLine(P(8.562f, 2.855f), P(3.677f, 4.370f));
                    path.AddLine(P(3.677f, 4.370f), P(3.645f, 4.397f));
                    path.AddBezier(P(3.645f, 4.397f), P(0.533f, 9.046f), P(-0.319f, 13.580f), P(0.099f, 18.058f));
                    path.AddLine(P(0.099f, 18.058f), P(0.130f, 18.114f));
                    path.AddBezier(P(0.130f, 18.114f), P(2.183f, 19.622f), P(4.172f, 20.537f), P(6.123f, 21.143f));
                    path.AddLine(P(6.123f, 21.143f), P(6.208f, 21.116f));
                    path.AddBezier(P(6.208f, 21.116f), P(6.669f, 20.485f), P(7.081f, 19.820f), P(7.434f, 19.122f));
                    path.AddLine(P(7.434f, 19.122f), P(7.392f, 19.016f));
                    path.AddBezier(P(7.392f, 19.016f), P(6.739f, 18.768f), P(6.118f, 18.466f), P(5.520f, 18.124f));
                    path.AddLine(P(5.520f, 18.124f), P(5.512f, 17.996f));
                    path.AddBezier(P(5.512f, 17.996f), P(5.638f, 17.902f), P(5.764f, 17.804f), P(5.884f, 17.705f));
                    path.AddLine(P(5.884f, 17.705f), P(5.962f, 17.694f));
                    path.AddBezier(P(5.962f, 17.694f), P(9.889f, 19.487f), P(14.142f, 19.487f), P(18.023f, 17.694f));
                    path.AddLine(P(18.023f, 17.694f), P(18.101f, 17.703f));
                    path.AddBezier(P(18.101f, 17.703f), P(18.222f, 17.802f), P(18.347f, 17.901f), P(18.474f, 17.996f));
                    path.AddLine(P(18.474f, 17.996f), P(18.468f, 18.123f));
                    path.AddLine(P(18.468f, 18.123f), P(16.595f, 19.015f));
                    path.AddLine(P(16.595f, 19.015f), P(16.554f, 19.122f));
                    path.AddBezier(P(16.554f, 19.122f), P(16.914f, 19.820f), P(17.326f, 20.485f), P(17.779f, 21.115f));
                    path.AddLine(P(17.779f, 21.115f), P(17.863f, 21.143f));
                    path.AddBezier(P(17.863f, 21.143f), P(19.824f, 20.537f), P(21.813f, 19.622f), P(23.866f, 18.114f));
                    path.AddLine(P(23.866f, 18.114f), P(23.897f, 18.059f));
                    path.AddBezier(P(23.897f, 18.059f), P(24.397f, 12.882f), P(23.059f, 8.385f), P(20.348f, 4.398f));
                    path.CloseFigure();
                    using (var w = new SolidBrush(Color.White)) g.FillPath(w, path);
                }
                using (var eye = new SolidBrush(Blurple))
                {
                    g.FillEllipse(eye, ox + (8.020f - 2.157f) * k, oy + (12.912f - 2.419f) * k, 2 * 2.157f * k, 2 * 2.419f * k);
                    g.FillEllipse(eye, ox + (15.995f - 2.157f) * k, oy + (12.912f - 2.419f) * k, 2 * 2.157f * k, 2 * 2.419f * k);
                }
            }
            return bmp;
        }
    }

    /// <summary>The launcher started a second time: the running one shows its window (Program.cs, MainForm.WndProc).</summary>
    static class SingleInstance
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint msg, uint action, IntPtr info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool FlashWindow(IntPtr hwnd, bool invert);

        /// <summary>Message "show your window", one for the whole session.</summary>
        public static readonly uint ShowMessage = RegisterWindowMessage("ParanoidalLauncher.ShowWindow.7c2f1d0e");

        /// <summary>Second start: lets the running launcher take the foreground and asks it to show its window.</summary>
        public static void Signal()
        {
            try { AllowSetForegroundWindow(-1); } catch { }   // ASFW_ANY: this process was started by the player, it may give the foreground away
            try { if (ShowMessage != 0) PostMessage((IntPtr)0xFFFF, ShowMessage, IntPtr.Zero, IntPtr.Zero); } catch { }   // HWND_BROADCAST
        }

        /// <summary>The message also reaches a launcher that runs with administrator rights (UIPI filter).</summary>
        public static void Accept(IntPtr hwnd)
        {
            try { if (ShowMessage != 0) ChangeWindowMessageFilterEx(hwnd, ShowMessage, 1, IntPtr.Zero); } catch { }   // MSGFLT_ALLOW
        }

        /// <summary>Restores a minimised window and brings it to the front; the taskbar button flashes if Windows refuses.</summary>
        public static void BringUp(Form f)
        {
            var h = f.Handle;
            if (IsIconic(h)) ShowWindow(h, 9);   // SW_RESTORE: back to normal or maximised
            else if (!f.Visible) ShowWindow(h, 5);   // SW_SHOW
            f.TopMost = true;
            f.TopMost = false;
            f.Activate();
            if (!SetForegroundWindow(h)) FlashWindow(h, true);
        }

        /// <summary>A message box above every window (MB_TOPMOST | MB_SETFOREGROUND), without an owner in this process.</summary>
        public static void Info(string text, string caption)
        {
            MessageBoxW(IntPtr.Zero, text, caption, 0x40 | 0x10000 | 0x40000);   // MB_ICONINFORMATION | MB_SETFOREGROUND | MB_TOPMOST
        }
    }
}
