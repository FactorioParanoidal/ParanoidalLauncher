using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Paranoidal
{
    /// <summary>
    /// Dialogs with input, laid out like the game's dialogs: the content in a pane, the button row under it with the way
    /// back on the left and the confirmation on the right.
    /// </summary>
    static class Dialogs
    {
        /// <summary>UI tests only: receives the dialog instead of showing it (the dialog then returns "cancelled").</summary>
        public static Action<Window> TestSnapshot;

        static Task Show(DialogWindow f, Window owner)
        {
            if (TestSnapshot == null) return f.ShowFor(owner);
            TestSnapshot(f);
            return Task.CompletedTask;
        }

        /// <summary>A dialog being built: controls go into the pane P at pane coordinates; Y is the next free line.</summary>
        sealed class Frame
        {
            public DialogWindow F;
            public Card P;
            public double Width, Pad, TextX, TextW, Y;
            /// <summary>Full width inside the pane (for lists and text boxes).</summary>
            public double Inner => Width - 2 * Pad;
        }

        static Frame Open(string title, string message, string sub, double width)
        {
            var f = new DialogWindow(title, width, 200);
            double m = 12;
            var pane = new Card(null);
            f.Root.Children.Add(pane);
            var fr = new Frame { F = f, P = pane, Width = width - 2 * m, Pad = 12 };
            double iconSize = 48;
            fr.TextX = fr.Pad + iconSize + 12;
            fr.TextW = fr.Width - fr.TextX - fr.Pad;
            pane.Children.Add(new Pic(Res.Gear).Place(fr.Pad, fr.Pad, iconSize, iconSize));
            fr.Y = fr.Pad;
            var mh = Gui.TextHeight(message, Gui.Heading, fr.TextW);
            pane.Children.Add(new Lbl { Text = message, Font = Gui.Heading, Color = Gui.Title, Wrap = true }.Place(fr.TextX, fr.Y, fr.TextW, mh + 4));
            fr.Y += mh + 8;
            if (!string.IsNullOrEmpty(sub)) AddText(fr, sub, Gui.Text);
            fr.Y = Math.Max(fr.Y, fr.Pad + iconSize + 8);
            return fr;
        }

        static Lbl AddText(Frame fr, string text, Color color, bool wide = false)
        {
            double x = wide ? fr.Pad : fr.TextX, w = wide ? fr.Inner : fr.TextW;
            var h = Gui.TextHeight(text, Gui.Body, w);
            var l = new Lbl { Text = text, Color = color, Wrap = true }.Place(x, fr.Y, w, h + 4);
            fr.P.Children.Add(l);
            fr.Y += h + 8;
            return l;
        }

        /// <summary>
        /// Closes the pane and adds the button row: the way back on the left, `extra` (another action) and the confirmation
        /// (green) on the right. Returns the confirmation button; Esc goes back.
        /// </summary>
        static FButton Buttons(Frame fr, string okText, string cancelText = null, FButton extra = null)
        {
            double m = 12, bh = 36;
            fr.P.Place(m, m, fr.Width, fr.Y + fr.Pad - 8);
            double y = fr.P.B() + m, formW = fr.Width + 2 * m;
            cancelText = cancelText ?? Tr.T("Отмена", "Cancel");
            double w = Math.Max(120, Gui.TextWidth(cancelText, Gui.Bold) + 40);
            var no = new FButton(cancelText, ButtonKind.Normal).Place(m, y, w, bh);
            no.Click += () => fr.F.Finish(DialogResult.Cancel);
            fr.F.Root.Children.Add(no);
            double yw = Math.Max(150, Gui.TextWidth(okText, Gui.Bold) + 40);
            var yes = new FButton(okText, ButtonKind.Confirm).Place(formW - m - yw, y, yw, bh);
            fr.F.Root.Children.Add(yes);
            if (extra != null)
            {
                double ew = Math.Max(130, Gui.TextWidth(extra.Text, Gui.Bold) + 40);
                extra.Place(yes.L() - 8 - ew, y, ew, bh);
                fr.F.Root.Children.Add(extra);
            }
            fr.F.SetClient(formW, y + bh + m);
            fr.F.Keys = e => { if (e.Key == Key.Escape) { e.Handled = true; fr.F.Finish(DialogResult.Cancel); } };
            return yes;
        }

        static string Describe(FileInfo f) => f == null ? Tr.T("нет", "none")
            : f.Name + " (" + Fmt.Size(f.Length) + ", " + Fmt.Date(f.LastWriteTime) + (f.Length + 64 * 1024 > Report.Limit ? Tr.T(", больше 20 МБ", ", over 20 MB") : "") + ")";

        static FCheck Check(Frame fr, string text, bool value, double x, double w)
        {
            var c = new FCheck { Text = text };
            c.Checked = value;
            fr.P.Children.Add(c.Place(x, fr.Y, w, 24));
            return c;
        }

        static FRadio Radio(Frame fr, List<FRadio> group, string text, bool value, double x, double w)
        {
            var r = new FRadio { Text = text, Group = group };
            r.Checked = value;
            group.Add(r);
            fr.P.Children.Add(r.Place(x, fr.Y, w, 24));
            return r;
        }

        /// <summary>Report options. Null: cancelled.</summary>
        public static async Task<Report.Input> AskReport(Window owner, FactorioInfo fac)
        {
            var fr = Open(Tr.T("Отчёт о проблеме", "Problem report"), Tr.T("ZIP-архив для Discord сборки", "A ZIP archive for the modpack Discord"),
                Tr.T("Лаунчер положит архив на рабочий стол. В нём будут логи игры (этот и прошлый запуск), строка версии, mod-list.json, mod-settings.dat, " +
                     "журналы лаунчера (sync-log.txt, crash.txt), config.ini игры и диагностика загрузки.\n" +
                     "Токен аккаунта Factorio (player-data.json) в архив не попадает, а имя пользователя Windows в путях заменяется.",
                     "The launcher puts the archive on the desktop. It holds the game logs (this and the previous start), the version line, mod-list.json, mod-settings.dat, " +
                     "the launcher logs (sync-log.txt, crash.txt), the game's config.ini and the load diagnostics.\n" +
                     "The Factorio account token (player-data.json) stays out, and the Windows user name in paths is replaced."), 620);
            var last = Report.LastSave(fac, false);
            var auto = Report.LastSave(fac, true);
            var chk = Check(fr, Tr.T("Добавить сейв", "Add a save"), false, fr.TextX, fr.TextW);
            fr.Y += 28;
            var group = new List<FRadio>();
            var rbLast = Radio(fr, group, Tr.T("Последний сейв: ", "Latest save: ") + Describe(last), true, fr.TextX + 24, fr.TextW - 24);
            fr.Y += 28;
            var rbAuto = Radio(fr, group, Tr.T("Последнее автосохранение: ", "Latest autosave: ") + Describe(auto), false, fr.TextX + 24, fr.TextW - 24);
            fr.Y += 28;
            var rbPick = Radio(fr, group, Tr.T("Выбрать файл…", "Choose a file…"), false, fr.TextX + 24, fr.TextW - 24);
            fr.Y += 28;
            // choosing a variant means "add a save" (disabled radio buttons are unreadable on the dark background)
            foreach (var rb in group) rb.Click += () => chk.Checked = true;
            if (last == null && auto != null) { rbLast.Checked = false; rbAuto.Checked = true; }
            fr.Y += 4;
            AddText(fr, Tr.T("Архив не больше 20 МБ, это лимит Discord. Если сейв туда не помещается, лаунчер его не добавит и покажет, где он лежит: его можно приложить отдельно.",
                "The archive stays under 20 MB, the Discord limit. A save that does not fit is left out, and the launcher shows where it is so you can attach it separately."), Gui.Muted);
            var ok = Buttons(fr, Tr.T("Создать архив", "Create the archive"));
            Report.Input result = null;
            ok.Click += async () =>
            {
                try { await CreateReport(); }
                catch (Exception ex) { Program.Crash(ex, true); }
            };
            async Task CreateReport()
            {
                var inp = new Report.Input { Save = Report.SaveChoice.None };
                if (chk.Checked)
                {
                    if (rbLast.Checked) inp.Save = Report.SaveChoice.LastManual;
                    else if (rbAuto.Checked) inp.Save = Report.SaveChoice.LastAutosave;
                    else
                    {
                        var wd = Report.WriteData(fac);
                        var saves = wd != null && Directory.Exists(Path.Combine(wd, "saves")) ? Path.Combine(wd, "saves") : null;
                        var sp = fr.F.StorageProvider;
                        // the button waits for the system dialog: a second click does not open another one
                        ok.IsEnabled = false;
                        string file;
                        try
                        {
                            file = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
                            {
                                Title = Tr.T("Выберите сейв", "Choose a save"),
                                SuggestedStartLocation = await sp.StartAt(saves),
                                FileTypeFilter = new[] { new FilePickerFileType(Tr.T("Сейвы Factorio (*.zip)", "Factorio saves (*.zip)")) { Patterns = new[] { "*.zip" } } },
                            }).LocalPath();
                        }
                        finally { ok.IsEnabled = true; }
                        if (file == null) return;
                        inp.Save = Report.SaveChoice.Chosen;
                        inp.SavePath = file;
                    }
                }
                result = inp;
                fr.F.Finish(DialogResult.OK);
            }
            await Show(fr.F, owner);
            return result;
        }

        /// <summary>
        /// A newer launcher is released: every version between the installed and the latest with its changes, a
        /// recommendation to update; asked on every start until updated (no "never" option, by the owner's decision).
        /// True: update now.
        /// </summary>
        public static async Task<bool> AskSelfUpdate(Window owner, ReleaseInfo r, bool needsAdmin)
        {
            var since = r.Since(AppInfo.Version);
            var versions = string.Join(", ", since.Select(c => c.Version).Reverse());
            var fr = Open(Tr.T("Обновление лаунчера", "Launcher update"),
                Tr.T("Вышла новая версия лаунчера: ", "A new launcher version is out: ") + r.Version,
                Tr.T("У вас версия ", "You have version ") + AppInfo.Version + ". " +
                (since.Count > 1 ? Tr.T("С тех пор вышли ", "Released since then: ") + versions + ". " : "") +
                Tr.T("Советуем обновиться: лаунчер скачает новую версию (", "We recommend updating: the launcher downloads the new version (") + Fmt.Size(r.Size) +
                Tr.T("), проверит её и перезапустится. Настройки, моды и сейвы останутся как есть.",
                     "), checks it and restarts. Settings, mods and saves stay as they are.") +
                (needsAdmin ? Tr.T("\n\nWindows защищает папку лаунчера (например, это Program Files) и попросит права администратора. " +
                                   "Чтобы вопрос больше не появлялся, перенесите папку игры вместе с лаунчером в обычную папку, например C:\\Games.",
                                   "\n\nWindows protects the launcher's folder (e.g. Program Files) and asks for administrator rights. " +
                                   "To stop this question, move the game folder with the launcher to a normal folder, e.g. C:\\Games.") : ""), 700);
            var box = new LogBox(new FFont(Gui.Sans, 12.33));
            foreach (var c in since)
            {
                box.Append(AppInfo.Name + " " + c.Version + (c.Date.Length > 0 ? ", " + c.Date : "") + "\n", Gui.Title, true);
                box.Append(PlainMarkdown(c.Text) + "\n\n", Gui.Text);
            }
            if (since.Count == 0) box.Append(Tr.T("Список изменений недоступен.", "The list of changes is not available."), Gui.Text);
            fr.P.Children.Add(box.Place(fr.Pad, fr.Y, fr.Inner, 260));
            fr.Y += 260 + 12;
            var ok = Buttons(fr, Tr.T("Обновить сейчас", "Update now"), Tr.T("Позже", "Later"));
            bool result = false;
            ok.Click += () => { result = true; fr.F.Finish(DialogResult.OK); };
            await Show(fr.F, owner);
            return result;
        }

        /// <summary>CHANGELOG markdown as plain text: bullets, no ** and backticks.</summary>
        public static string PlainMarkdown(string md)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var raw in (md ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Replace("**", "").Replace("`", "");
                var t = line.TrimStart();
                if (t.StartsWith("- ")) { if (sb.Length > 0) sb.Append('\n'); sb.Append("• ").Append(t.Substring(2)); }
                else if (t.Length > 0) sb.Append(sb.Length > 0 ? " " : "").Append(t);   // wrapped continuation of a bullet
            }
            return sb.ToString();
        }

        /// <summary>
        /// Several suitable game installations: which one is the player's (the launcher moves itself there and uses it).
        /// Null: not now.
        /// </summary>
        public static async Task<FactorioInfo> ChooseInstall(Window owner, List<FactorioInfo> list)
        {
            var fr = Open(Tr.T("Папка игры", "Game folder"), Tr.T("Найдено несколько подходящих установок ", "Several suitable installations of ") + AppInfo.RequiredGame,
                Tr.T("Лаунчер должен лежать в папке той игры, в которую вы играете со сборкой. " +
                     "Выберите её: лаунчер перенесёт себя туда, оставит ярлык на рабочем столе и запустится оттуда.",
                     "The launcher must live in the folder of the game you play the modpack with. " +
                     "Choose it: the launcher moves itself there, leaves a desktop shortcut and starts from there."), 660);
            var radios = new List<FRadio>();
            foreach (var f in list)
            {
                Radio(fr, radios, f.Dir, radios.Count == 0, fr.TextX, fr.TextW);
                fr.Y += 28;
            }
            fr.Y += 8;
            var ok = Buttons(fr, Tr.T("Перенести сюда", "Move here"), Tr.T("Не сейчас", "Not now"));
            FactorioInfo result = null;
            ok.Click += () =>
            {
                int i = radios.FindIndex(r => r.Checked);
                if (i < 0) return;
                result = list[i];
                fr.F.Finish(DialogResult.OK);
            };
            await Show(fr.F, owner);
            return result;
        }

        /// <summary>
        /// The first switch to the Space Age build (1.5.0): what the profile is, what is copied once, the saves as a
        /// choice, the disk space. Null: cancelled; otherwise whether to copy the saves of the main build.
        /// </summary>
        public static async Task<bool?> ConfirmSpaceAge(Window owner, GameProfile.SpaceAgePlan p, FactorioInfo fac)
        {
            Func<string, string, string> T = Tr.T;
            var fr = Open("Paranoidal 2.1 Space Age", T("Установить Paranoidal 2.1 Space Age?", "Install Paranoidal 2.1 Space Age?"),
                T("Сборка Space Age в активной разработке. Она живёт в отдельном профиле, в папке ", "The Space Age build is in active development. It lives in a profile of its own, in the folder ") +
                p.Root + T(": там свои моды, сейвы и настройки. Основная сборка 2.1 и её сейвы останутся как есть, вернуться можно вкладкой «Paranoidal 2.1».",
                           ": its own mods, saves and settings. The main 2.1 build and its saves stay as they are; the \"Paranoidal 2.1\" tab switches back."), 680);
            AddText(fr, T("Лаунчер один раз скопирует в профиль настройки игры (графика, клавиши), настройки модов, вход в аккаунт и чертежи. ",
                          "The launcher copies the game settings (graphics, controls), the mod settings, the account login and the blueprints into the profile once. ") +
                (p.Seed.Count > 0 ? T("За основу он возьмёт вашу сборку 2.1, поэтому скачается только разница.", "Your 2.1 build is the base, so only the difference is downloaded.")
                                  : T("Сборка Space Age скачается целиком, около 1 ГБ.", "The Space Age build is downloaded in full, about 1 GB.")), Gui.Text);
            var chk = Check(fr, T("Скопировать сейвы основной сборки", "Copy the saves of the main build") + " (" + p.Saves + (p.Saves > 0 ? ", " + Fmt.Size(p.SavesBytes) : "") + ")",
                false, fr.TextX, fr.TextW);
            chk.IsEnabled = p.Saves > 0;
            fr.Y += 28;
            AddText(fr, T("Сейвы копируются, оригиналы остаются на месте. Сейвы основной сборки открываются в сборке Space Age, пока не запущена ракета. Сейв Space Age в основной сборке не откроется.",
                          "The saves are copied; the originals stay where they are. Saves of the main build load in the Space Age build until a rocket is launched. A Space Age save does not load in the main build."), Gui.Muted);
            var space = new Lbl { Color = Gui.Text, Wrap = true }.Place(fr.TextX, fr.Y, fr.TextW, 40);
            fr.P.Children.Add(space);
            fr.Y += 44;
            bool spaceAge = fac != null && !fac.MissingDlcFor(Lines.SA).Contains("space-age");
            if (!spaceAge)
                AddText(fr, T("Нужно дополнение Space Age: в этой игре его нет. Без него сборка Space Age не запустится.",
                              "It needs the Space Age expansion, and this game does not have it. The Space Age build does not run without it."), Gui.Red);
            var ok = Buttons(fr, T("Установить Space Age", "Install Space Age"), T("Назад", "Back"));
            Action check = () =>
            {
                long need = p.Need(chk.Checked);
                bool enough = p.Free < 0 || p.Free >= need;
                space.Text = T("Нужно места на диске: около ", "Disk space needed: about ") + Fmt.Size(need) + T(" (сборка и кэш загрузки игры)", " (the build and the game's load cache)") +
                    (p.Free >= 0 ? T(", свободно ", ", free ") + Fmt.Size(p.Free) : "") + ".";
                space.Color = enough ? Gui.Text : Gui.Red;
                ok.IsEnabled = enough && spaceAge;
            };
            chk.CheckedChanged += check;
            check();
            bool? result = null;
            ok.Click += () => { result = chk.Checked; fr.F.Finish(DialogResult.OK); };
            await Show(fr.F, owner);
            return result;
        }

        /// <summary>The move of the Space Age profile to another folder (GameProfile.PlanMove). True: «Перенести».</summary>
        public static async Task<bool> ConfirmProfileMove(Window owner, GameProfile.MovePlan p)
        {
            Func<string, string, string> T = Tr.T;
            var sub = T("Из: ", "From: ") + p.From + "\n" + T("В: ", "To: ") + p.To + "\n\n" +
                T("Переедут моды, сейвы, настройки и кэш загрузки (", "The mods, saves, settings and the load cache move (") + Fmt.Size(p.Bytes) + ")." +
                (p.Free >= 0 ? T(" На диске ", " The disk ") + Path.GetPathRoot(p.To) + T(" свободно ", " has free ") + Fmt.Size(p.Free) + "." : "") +
                (p.SameVolume ? "" : "\n" + T("Профиль переезжает на другой диск: он скопируется, а старая папка удалится только после полной копии. Это займёт несколько минут.",
                                                "The profile goes to another disk: it is copied, and the old folder is deleted only after the full copy. It takes a few minutes.")) +
                (p.OutsideGame ? "\n" + T("Профиль будет лежать вне папки игры: если копировать папку игры на другой компьютер, профиль Space Age нужно переносить отдельно.",
                                          "The profile will be outside the game folder: when you copy the game folder to another PC, move the Space Age profile separately.") : "");
            return await DarkDialog.Ask(owner, T("Профиль Space Age", "Space Age profile"), T("Перенести профиль Space Age?", "Move the Space Age profile?"), sub, null,
                T("Перенести", "Move"), T("Отмена", "Cancel"), Gui.Title) == DialogResult.Yes;
        }

        /// <summary>The chosen folder already has a Space Age profile. True: use it (the current one stays where it is).</summary>
        public static async Task<bool> ConfirmUseProfile(Window owner, GameProfile.MovePlan p)
        {
            Func<string, string, string> T = Tr.T;
            return await DarkDialog.Ask(owner, T("Профиль Space Age", "Space Age profile"), T("В этой папке уже есть профиль Space Age", "This folder already has a Space Age profile"),
                p.To + "\n\n" + T("Использовать его? Лаунчер ничего не перенесёт и не перезапишет, а профиль из папки\n", "Use it? The launcher moves and overwrites nothing; the profile in\n") +
                p.From + T("\nостанется на месте.", "\nstays where it is."), null, T("Использовать", "Use it"), T("Отмена", "Cancel"), Gui.Title) == DialogResult.Yes;
        }

        /// <summary>The host's version line. Null: cancelled.</summary>
        public static async Task<string> AskHostLine(Window owner)
        {
            var fr = Open(Tr.T("Версия хоста", "Host's version"), Tr.T("Перейти на версию сборки хоста", "Switch to the host's build version"),
                Tr.T("Попросите хоста нажать «Скопировать версию» и вставьте его строку сюда. Лаунчер скачает ровно эту версию сборки " +
                     "(нужен интернет, скачается только разница) и включит «Не менять версию», чтобы не предлагать обновление. " +
                     "Когда хост обновится, снимите эту галочку.",
                     "Ask the host to press \"Copy version\" and paste the line here. The launcher downloads exactly that build version " +
                     "(it needs internet and downloads only the difference) and turns on \"Keep this version\", so it stops offering the update. " +
                     "When the host updates, clear that check box."), 620);
            var box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0),
                Background = Gui.Brush(Gui.Deep),
                Foreground = Gui.Brush(Gui.Text),
                FontFamily = Gui.Code.Family,
                FontSize = 12.67,
                Padding = new Thickness(4, 2),
                Tag = Card.DeepTag,
            };
            // a host's line on the clipboard is pasted already; no clipboard (or no text on it) leaves the box empty
            try
            {
                var clip = owner != null && owner.Clipboard != null ? await owner.Clipboard.TryGetTextAsync() : null;
                if (clip != null && VersionLine.ParseHost(clip) != null) box.Text = clip.Trim();
            }
            catch { }
            fr.P.Children.Add(box.Place(fr.Pad, fr.Y, fr.Inner, 72));
            fr.Y += 72 + 8;
            var hint = new StatusLine(Gui.Body).Place(fr.Pad, fr.Y, fr.Inner, 22);
            fr.P.Children.Add(hint);
            fr.Y += 30;
            Action check = () =>
            {
                var t = box.Text ?? "";
                var sha = VersionLine.ParseHost(t);
                hint.Text = t.Trim().Length == 0 ? "" : sha != null ? Tr.T("Версия сборки в строке: ", "Build version in the line: ") + sha
                    : Tr.T("В строке нет версии сборки (номера в скобках).", "The line has no build version (the id in parentheses).");
                hint.Color = sha != null ? Gui.Green : Gui.Orange;
            };
            box.TextChanged += (s, e) => check();
            check();
            var ok = Buttons(fr, Tr.T("Перейти", "Switch"));
            string result = null;
            ok.Click += () =>
            {
                if (VersionLine.ParseHost(box.Text ?? "") == null) { box.Focus(); return; }
                result = box.Text.Trim();
                fr.F.Finish(DialogResult.OK);
            };
            fr.F.Opened += (s, e) => box.Focus();
            await Show(fr.F, owner);
            return result;
        }
    }
}
