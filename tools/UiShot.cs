// UI snapshot tool (not shipped): draws the launcher's dialogs and windows into PNG files without showing them, so a test
// never takes the focus from the user's game. Usage: UiShot.exe <outDir> <ru|en> [factorio dir] [launcher release.json]
// Set PARANOIDAL_LAUNCHER_DATA_DIR to a test folder: the language choice is written to its settings.ini.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Paranoidal
{
    static class UiShot
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>
        /// Renders the window with PrintWindow (RichTextBox and nested controls included, unlike DrawToBitmap). The window is
        /// shown far off-screen without activation (SW_SHOWNOACTIVATE), so it never takes the focus, then hidden again.
        /// </summary>
        static void Snap(Form f, string file)
        {
            var h = f.Handle;
            SetWindowPos(h, IntPtr.Zero, -32000, -32000, 0, 0, 0x0001 | 0x0004 | 0x0010);   // NOSIZE | NOZORDER | NOACTIVATE
            ShowWindow(h, 4);   // SW_SHOWNOACTIVATE
            for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(15); }
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    var hdc = g.GetHdc();
                    PrintWindow(h, hdc, 0);   // WM_PRINT path: works off-screen (PW_RENDERFULLCONTENT needs a DWM surface)
                    g.ReleaseHdc(hdc);
                }
                bmp.Save(file);
            }
            ShowWindow(h, 0);   // SW_HIDE
            Console.WriteLine("snap " + file + " " + f.Width + "x" + f.Height);
        }

        static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
        static void Set(MainForm m, string name, object value) => typeof(MainForm).GetField(name, Flags).SetValue(m, value);
        static object Get(MainForm m, string name) => typeof(MainForm).GetField(name, Flags).GetValue(m);
        static void Call(MainForm m, string name, params object[] args) => typeof(MainForm).GetMethod(name, Flags).Invoke(m, args);

        [STAThread]
        static int Main(string[] a)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = g.DpiX / 96f;
            Tr.Init();
            Tr.Set(a[1] == "en");
            Directory.CreateDirectory(a[0]);
            int n = 0;
            Dialogs.TestSnapshot = f => { Snap(f, Path.Combine(a[0], "dialog-" + a[1] + "-" + (++n) + ".png")); f.Close(); };
            var fac = FactorioInfo.Inspect(a.Length > 2 ? a[2] : null);
            Dialogs.AskReport(null, fac);
            Dialogs.AskHostLine(null);
            // 1.2.0: self-update (a[3] = a launcher release.json, e.g. of a test stand), choice of installation
            ReleaseInfo rel = null;
            if (a.Length > 3 && File.Exists(a[3]))
                rel = Releases.ParseManifest(File.ReadAllText(a[3]), AppInfo.LauncherRepo, "launcher", AppInfo.ExeName);
            if (rel != null)
            {
                Dialogs.AskSelfUpdate(null, rel, false);
                Dialogs.AskSelfUpdate(null, rel, true);
            }
            var g21 = new List<FactorioInfo>
            {
                new FactorioInfo { Dir = @"C:\Games\Factorio 2.1.24", Version = "2.1.24" }.WithAllDlc(),
                new FactorioInfo { Dir = @"D:\SteamLibrary\steamapps\common\Factorio", Version = "2.1.25" }.WithAllDlc(),
            };
            Dialogs.ChooseInstall(null, g21);
            // 1.5.0: the first switch to Space Age, a question with a destructive answer, a message
            Dialogs.ConfirmSpaceAge(null, SampleSpaceAge(), g21[0]);
            DarkDialog.TestSnapshot = f => { Snap(f, Path.Combine(a[0], "dialog-" + a[1] + "-" + (++n) + ".png")); f.Close(); };
            DarkDialog.Ask(null, Tr.T("Лишние моды", "Extra mods"), Tr.T("В папке mods есть моды и файлы, которых нет в сборке (3).", "The mods folder has mods and files that are not in the build (3)."),
                Tr.T("Удалить их, чтобы папка точно совпадала со сборкой? Если оставить, лаунчер больше про них не спросит.",
                     "Delete them so the folder matches the build exactly? If you keep them, the launcher does not ask about them again."),
                new[] { "even-distribution_2.0.2.zip", "my-own-mod\\", "squeak-through-2_0.1.4.zip" }, Tr.T("Удалить", "Delete"), Tr.T("Оставить", "Keep"), Theme.Title, ButtonKind.Danger);
            // 1.5.0: the move of the Space Age profile to another disk, an existing profile in the chosen folder
            var mp = new GameProfile.MovePlan { From = @"C:\Games\Factorio 2.1.24\Paranoidal-SpaceAge", To = @"D:\Games\Paranoidal-SpaceAge", FromExists = true,
                SameVolume = false, OutsideGame = true, Bytes = 1930L * 1024 * 1024, Free = 120L * 1024 * 1024 * 1024 };
            Dialogs.ConfirmProfileMove(null, mp);
            Dialogs.ConfirmUseProfile(null, mp);
            DarkDialog.Info(null, Tr.T("Ярлыки", "Shortcuts"), Tr.T("Ярлыки созданы: на рабочем столе и в меню «Пуск» (папка Paranoidal).", "Shortcuts created: on the desktop and in the Start menu (Paranoidal folder)."), Theme.Green);
            DarkDialog.TestSnapshot = null;

            // 1.5.0: the start window: the choice of the build, no suitable Factorio (three findings), the wait
            {
                var main = new MainForm();
                var start = (StartPanel)Get(main, "start");
                Set(main, "fac", g21[0]);
                var h = main.Handle;   // the window sizes need a handle
                start.Show(StartPanel.View.Selector);
                Call(main, "RefreshStartTexts");
                Call(main, "SetScreen", (StartPanel.View?)StartPanel.View.Selector);
                Snap(main, Path.Combine(a[0], "start-select-" + a[1] + ".png"));
                var finds = new[]
                {
                    new List<FactorioInfo> { new FactorioInfo { Dir = @"C:\Games\Factorio 2.077 - QUILITY", Version = "2.0.77" }.WithAllDlc() },
                    new List<FactorioInfo> { new FactorioInfo { Dir = @"C:\Program Files (x86)\Steam\steamapps\common\Factorio", Version = "2.1.24" } },
                    new List<FactorioInfo>(),
                };
                string[] names = { "old", "nodlc", "none" };
                for (int i = 0; i < finds.Length; i++)
                {
                    Set(main, "needFound", finds[i]);
                    start.Show(StartPanel.View.Need);
                    Call(main, "RefreshStartTexts");
                    Call(main, "SetScreen", (StartPanel.View?)StartPanel.View.Need);
                    Snap(main, Path.Combine(a[0], "start-need-" + names[i] + "-" + a[1] + ".png"));
                }
                start.Show(StartPanel.View.Busy);
                Call(main, "RefreshStartTexts");
                start.SetBusy(Tr.T("Переношу лаунчер в папку игры", "Moving the launcher into the game folder"), @"C:\Users\Player\Downloads  →  C:\Games\Factorio 2.1.24");
                Call(main, "SetScreen", (StartPanel.View?)StartPanel.View.Busy);
                Snap(main, Path.Combine(a[0], "start-busy-" + a[1] + ".png"));
                main.Dispose();
            }

            foreach (var shot in new[] { Tuple.Create(Lines.L21, true), Tuple.Create(Lines.SA, true), Tuple.Create(Lines.SA, false) })
            {
                var line = shot.Item1;
                bool have = shot.Item2;
                Lines.Set(line);
                var main = new MainForm();
                var h = main.Handle;
                Call(main, "SetScreen", new object[] { null });
                // a player's state after the status check (only for the picture: no engine work is done)
                var game = g21[0];
                Set(main, "fac", game);
                // a Space Age build that is not installed: a folder that does not exist on any PC
                Set(main, "modsDir", !have ? @"Z:\no-such-game\Paranoidal-SpaceAge\mods" : line.SpaceAge ? game.Dir + @"\Paranoidal-SpaceAge\mods" : game.Dir + @"\mods");
                Set(main, "installed", have ? "209bf491bf64b1893617623dc9805f2a3963ad58" : null);
                Set(main, "installedDate", have ? "10.11.2026 00:32" : null);
                Set(main, "remote", "209bf491bf64b1893617623dc9805f2a3963ad58");
                Set(main, "remoteDate", "10.11.2026 00:32");
                Set(main, "modified", 0);
                Set(main, "engine", new SyncEngine(null, main));
                Set(main, "git", new GitRunner("git.exe", "none"));
                var commits = new List<CommitInfo>
                {
                    new CommitInfo { Sha = "209bf491bf64b1893617623dc9805f2a3963ad58", Message = "Исправляет рецепт ускорителя частиц", Date = new DateTime(2026, 11, 10, 0, 32, 0) },
                    new CommitInfo { Sha = "5ce560d97867a9e0aec7a4b35104433c7fd0b85d", Message = "Заканчивает вспышку Paranoidal-Protomolecule-Beast сразу, когда от неё ничего не осталось", Date = new DateTime(2026, 11, 4, 15, 18, 0) },
                };
                Call(main, "FillCommits", commits, false, false);
                Set(main, "lastLoad", new LoadVerdict { Seconds = 23, Data = LoadLog.DataState.Hit, Atlas = LoadLog.AtlasState.Hit });
                Call(main, "ApplyTexts");
                Call(main, "UpdateUi");
                Call(main, "DoLayout");
                Call(main, "Idle", have ? Tr.T("Установлена последняя версия", "The latest version is installed") : (string)typeof(MainForm).GetMethod("StatusIdleText", Flags).Invoke(main, null));
                ((FProgress)Get(main, "progress")).Value = have ? 1 : 0;
                Call(main, "AppendLog", "Готово. Моды соответствуют последней версии сборки", LogKind.Ok);
                Call(main, "AppendLogHighlight", "Обновился гайд по сборке Paranoidal: версия 1.3.4, C:\\Games\\Factorio\\Guide_to_the_Paranoidal.html");
                Call(main, "AppendLog", "Следующая обычная строка журнала.", LogKind.Info);
                Call(main, "SelectTab", 0);
                var id = line.Id.Replace(".", "") + (have ? "" : "-new");
                Snap(main, Path.Combine(a[0], "main-" + id + "-" + a[1] + ".png"));
                main.MinimumSize = Size.Empty;
                main.ClientSize = new Size(Theme.S(900), Theme.S(760));
                Call(main, "SelectTab", 2);
                Snap(main, Path.Combine(a[0], "main-min-" + id + "-" + a[1] + ".png"));
                main.Dispose();
            }
            return 0;
        }

        /// <summary>The first switch to Space Age as a player with a typical game folder would see it (only for the picture).</summary>
        static GameProfile.SpaceAgePlan SampleSpaceAge()
        {
            var p = new GameProfile.SpaceAgePlan { Root = @"C:\Games\Factorio 2.1.24\Paranoidal-SpaceAge", Saves = 12, SavesBytes = 840L * 1024 * 1024,
                BuildBytes = 925L * 1024 * 1024, Free = 48L * 1024 * 1024 * 1024 };
            p.Seed.Add("zzzparanoidal");
            return p;
        }
    }
}
