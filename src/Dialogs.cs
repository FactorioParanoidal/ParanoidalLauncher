using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Paranoidal
{
    /// <summary>
    /// Dialogs with input, laid out like the game's dialogs: the content in a pane, the button row under it with the way
    /// back on the left and the confirmation on the right.
    /// </summary>
    static class Dialogs
    {
        /// <summary>UI tests only: receives the dialog instead of showing it (the dialog then returns "cancelled").</summary>
        public static Action<Form> TestSnapshot;

        static void Show(Form f, IWin32Window owner)
        {
            if (TestSnapshot != null) TestSnapshot(f);
            else f.ShowDialog(owner);
        }

        /// <summary>A dialog being built: controls go into the pane P at pane coordinates; Y is the next free line.</summary>
        sealed class Frame
        {
            public Form F;
            public Card P;
            public int Width, Pad, TextX, TextW, Y;
            /// <summary>Full width inside the pane (for lists and text boxes).</summary>
            public int Inner => Width - 2 * Pad;
        }

        static Frame Open(string title, string message, string sub, int width)
        {
            Func<float, int> S = Theme.S;
            var f = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                BackColor = Theme.Window,
                ForeColor = Theme.Text,
                Font = Theme.Body,
                AutoScaleMode = AutoScaleMode.None,
                KeyPreview = true,
                ClientSize = new Size(S(width), S(200)),
            };
            var icon = Res.AppIcon(32);
            if (icon != null) f.Icon = icon;
            int m = S(12);
            var pane = new Card(null) { Location = new Point(m, m), Size = new Size(S(width) - 2 * m, S(100)) };
            f.Controls.Add(pane);
            var fr = new Frame { F = f, P = pane, Width = pane.Width, Pad = S(12) };
            int iconSize = S(48);
            fr.TextX = fr.Pad + iconSize + S(12);
            fr.TextW = fr.Width - fr.TextX - fr.Pad;
            pane.Controls.Add(new PictureBox { Image = Res.Gear, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(fr.Pad, fr.Pad, iconSize, iconSize), BackColor = Theme.Pane });
            fr.Y = fr.Pad;
            var mh = TextRenderer.MeasureText(message, Theme.Heading, new Size(fr.TextW, int.MaxValue), TextFormatFlags.WordBreak).Height;
            pane.Controls.Add(new Label { Text = message, Font = Theme.Heading, ForeColor = Theme.Title, BackColor = Theme.Pane, Bounds = new Rectangle(fr.TextX, fr.Y, fr.TextW, mh + S(4)), UseMnemonic = false });
            fr.Y += mh + S(8);
            if (!string.IsNullOrEmpty(sub)) AddText(fr, sub, Theme.Text);
            fr.Y = Math.Max(fr.Y, fr.Pad + iconSize + S(8));
            f.HandleCreated += (s, e) => Native.DarkTitle(f.Handle);
            return fr;
        }

        static void AddText(Frame fr, string text, Color color, bool wide = false)
        {
            int x = wide ? fr.Pad : fr.TextX, w = wide ? fr.Inner : fr.TextW;
            var h = TextRenderer.MeasureText(text, Theme.Body, new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height;
            fr.P.Controls.Add(new Label { Text = text, ForeColor = color, BackColor = Theme.Pane, Bounds = new Rectangle(x, fr.Y, w, h + Theme.S(4)), UseMnemonic = false });
            fr.Y += h + Theme.S(8);
        }

        static T Styled<T>(T c) where T : ButtonBase
        {
            c.BackColor = Theme.Pane;
            c.ForeColor = Theme.Text;
            c.FlatStyle = FlatStyle.Flat;
            c.UseMnemonic = false;
            c.FlatAppearance.BorderColor = Theme.DotGrey;
            c.FlatAppearance.CheckedBackColor = Theme.Deep;
            return c;
        }

        /// <summary>
        /// Closes the pane and adds the button row: the way back on the left, `extra` (another action) and the confirmation
        /// (green) on the right. Returns the confirmation button; Esc goes back.
        /// </summary>
        static FButton Buttons(Frame fr, string okText, string cancelText = null, FButton extra = null)
        {
            Func<float, int> S = Theme.S;
            int m = S(12), bh = S(36);
            fr.P.Height = fr.Y + fr.Pad - S(8);
            int y = fr.P.Bottom + m, formW = fr.Width + 2 * m;
            cancelText = cancelText ?? Tr.T("Отмена", "Cancel");
            int w = Math.Max(S(120), TextRenderer.MeasureText(cancelText, Theme.Bold).Width + S(40));
            var no = new FButton(cancelText, ButtonKind.Normal) { Bounds = new Rectangle(m, y, w, bh) };
            no.Click += (s, e) => { fr.F.DialogResult = DialogResult.Cancel; fr.F.Close(); };
            fr.F.Controls.Add(no);
            int yw = Math.Max(S(150), TextRenderer.MeasureText(okText, Theme.Bold).Width + S(40));
            var yes = new FButton(okText, ButtonKind.Confirm) { Bounds = new Rectangle(formW - m - yw, y, yw, bh) };
            fr.F.Controls.Add(yes);
            if (extra != null)
            {
                int ew = Math.Max(S(130), TextRenderer.MeasureText(extra.Text, Theme.Bold).Width + S(40));
                extra.Bounds = new Rectangle(yes.Left - S(8) - ew, y, ew, bh);
                fr.F.Controls.Add(extra);
            }
            fr.F.ClientSize = new Size(formW, y + bh + m);
            fr.F.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { fr.F.DialogResult = DialogResult.Cancel; fr.F.Close(); } };
            return yes;
        }

        static string Describe(FileInfo f) => f == null ? Tr.T("нет", "none")
            : f.Name + " (" + Fmt.Size(f.Length) + ", " + Fmt.Date(f.LastWriteTime) + (f.Length + 64 * 1024 > Report.Limit ? Tr.T(", больше 20 МБ", ", over 20 MB") : "") + ")";

        /// <summary>Report options. Null: cancelled.</summary>
        public static Report.Input AskReport(IWin32Window owner, FactorioInfo fac)
        {
            Func<float, int> S = Theme.S;
            var fr = Open(Tr.T("Отчёт о проблеме", "Problem report"), Tr.T("ZIP-архив для Discord сборки", "A ZIP archive for the modpack Discord"),
                Tr.T("Лаунчер положит архив на рабочий стол. В нём будут логи игры (этот и прошлый запуск), строка версии, mod-list.json, mod-settings.dat, " +
                     "журналы лаунчера (sync-log.txt, crash.txt), config.ini игры и диагностика загрузки.\n" +
                     "Токен аккаунта Factorio (player-data.json) в архив не попадает, а имя пользователя Windows в путях заменяется.",
                     "The launcher puts the archive on the desktop. It holds the game logs (this and the previous start), the version line, mod-list.json, mod-settings.dat, " +
                     "the launcher logs (sync-log.txt, crash.txt), the game's config.ini and the load diagnostics.\n" +
                     "The Factorio account token (player-data.json) stays out, and the Windows user name in paths is replaced."), 620);
            var last = Report.LastSave(fac, false);
            var auto = Report.LastSave(fac, true);
            var chk = Styled(new CheckBox { Text = Tr.T("Добавить сейв", "Add a save"), Checked = false });
            chk.Bounds = new Rectangle(fr.TextX, fr.Y, fr.TextW, S(24));
            fr.P.Controls.Add(chk);
            fr.Y += S(28);
            var rbLast = Styled(new RadioButton { Text = Tr.T("Последний сейв: ", "Latest save: ") + Describe(last), Checked = true });
            var rbAuto = Styled(new RadioButton { Text = Tr.T("Последнее автосохранение: ", "Latest autosave: ") + Describe(auto) });
            var rbPick = Styled(new RadioButton { Text = Tr.T("Выбрать файл…", "Choose a file…") });
            foreach (var rb in new[] { rbLast, rbAuto, rbPick })
            {
                rb.Bounds = new Rectangle(fr.TextX + S(24), fr.Y, fr.TextW - S(24), S(24));
                // choosing a variant means "add a save" (disabled radio buttons are unreadable on the dark background)
                rb.Click += (s, e) => chk.Checked = true;
                fr.P.Controls.Add(rb);
                fr.Y += S(28);
            }
            if (last == null && auto != null) { rbAuto.Checked = true; }
            fr.Y += S(4);
            AddText(fr, Tr.T("Архив не больше 20 МБ, это лимит Discord. Если сейв туда не помещается, лаунчер его не добавит и покажет, где он лежит: его можно приложить отдельно.",
                "The archive stays under 20 MB, the Discord limit. A save that does not fit is left out, and the launcher shows where it is so you can attach it separately."), Theme.Muted);
            var ok = Buttons(fr, Tr.T("Создать архив", "Create the archive"));
            Report.Input result = null;
            ok.Click += (s, e) =>
            {
                var inp = new Report.Input { Save = Report.SaveChoice.None };
                if (chk.Checked)
                {
                    if (rbLast.Checked) inp.Save = Report.SaveChoice.LastManual;
                    else if (rbAuto.Checked) inp.Save = Report.SaveChoice.LastAutosave;
                    else
                    {
                        using (var dlg = new OpenFileDialog
                        {
                            Title = Tr.T("Выберите сейв", "Choose a save"),
                            Filter = Tr.T("Сейвы Factorio (*.zip)|*.zip", "Factorio saves (*.zip)|*.zip"),
                        })
                        {
                            var wd = Report.WriteData(fac);
                            if (wd != null && Directory.Exists(Path.Combine(wd, "saves"))) dlg.InitialDirectory = Path.Combine(wd, "saves");
                            if (dlg.ShowDialog(fr.F) != DialogResult.OK) return;
                            inp.Save = Report.SaveChoice.Chosen;
                            inp.SavePath = dlg.FileName;
                        }
                    }
                }
                result = inp;
                fr.F.DialogResult = DialogResult.OK;
                fr.F.Close();
            };
            Show(fr.F, owner);
            fr.F.Dispose();
            return result;
        }

        /// <summary>
        /// A newer launcher is released: every version between the installed and the latest with its changes, a
        /// recommendation to update; asked on every start until updated (no "never" option, by the owner's decision).
        /// True: update now.
        /// </summary>
        public static bool AskSelfUpdate(IWin32Window owner, ReleaseInfo r, bool needsAdmin)
        {
            Func<float, int> S = Theme.S;
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
            var box = new RichTextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Deep,
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 9.25f),
                WordWrap = true,
                DetectUrls = false,
                Bounds = new Rectangle(fr.Pad, fr.Y, fr.Inner, S(260)),
                Tag = Card.DeepTag,
            };
            Native.DarkScrollbars(box);
            foreach (var c in since)
            {
                box.SelectionFont = Theme.Bold;
                box.SelectionColor = Theme.Title;
                box.AppendText(AppInfo.Name + " " + c.Version + (c.Date.Length > 0 ? ", " + c.Date : "") + "\n");
                box.SelectionFont = box.Font;
                box.SelectionColor = Theme.Text;
                box.AppendText(PlainMarkdown(c.Text) + "\n\n");
            }
            if (since.Count == 0) box.AppendText(Tr.T("Список изменений недоступен.", "The list of changes is not available."));
            box.SelectionStart = 0;
            fr.P.Controls.Add(box);
            fr.Y += S(260) + S(12);
            var ok = Buttons(fr, Tr.T("Обновить сейчас", "Update now"), Tr.T("Позже", "Later"));
            bool result = false;
            ok.Click += (s, e) => { result = true; fr.F.DialogResult = DialogResult.OK; fr.F.Close(); };
            Show(fr.F, owner);
            fr.F.Dispose();
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
        public static FactorioInfo ChooseInstall(IWin32Window owner, List<FactorioInfo> list)
        {
            Func<float, int> S = Theme.S;
            var fr = Open(Tr.T("Папка игры", "Game folder"), Tr.T("Найдено несколько подходящих установок ", "Several suitable installations of ") + AppInfo.RequiredGame,
                Tr.T("Лаунчер должен лежать в папке той игры, в которую вы играете со сборкой. " +
                     "Выберите её: лаунчер перенесёт себя туда, оставит ярлык на рабочем столе и запустится оттуда.",
                     "The launcher must live in the folder of the game you play the modpack with. " +
                     "Choose it: the launcher moves itself there, leaves a desktop shortcut and starts from there."), 660);
            var radios = new List<RadioButton>();
            foreach (var f in list)
            {
                var rb = Styled(new RadioButton { Text = f.Dir, Checked = radios.Count == 0 });
                rb.Bounds = new Rectangle(fr.TextX, fr.Y, fr.TextW, S(24));
                fr.P.Controls.Add(rb);
                radios.Add(rb);
                fr.Y += S(28);
            }
            fr.Y += S(8);
            var ok = Buttons(fr, Tr.T("Перенести сюда", "Move here"), Tr.T("Не сейчас", "Not now"));
            FactorioInfo result = null;
            ok.Click += (s, e) =>
            {
                int i = radios.FindIndex(r => r.Checked);
                if (i < 0) return;
                result = list[i];
                fr.F.DialogResult = DialogResult.OK;
                fr.F.Close();
            };
            Show(fr.F, owner);
            fr.F.Dispose();
            return result;
        }

        /// <summary>
        /// The first switch to the Space Age build (1.5.0): what the profile is, what is copied once, the saves as a
        /// choice, the disk space. Null: cancelled; otherwise whether to copy the saves of the main build.
        /// </summary>
        public static bool? ConfirmSpaceAge(IWin32Window owner, GameProfile.SpaceAgePlan p, FactorioInfo fac)
        {
            Func<float, int> S = Theme.S;
            Func<string, string, string> T = Tr.T;
            var fr = Open("Paranoidal 2.1 Space Age", T("Установить Paranoidal 2.1 Space Age?", "Install Paranoidal 2.1 Space Age?"),
                T("Сборка Space Age в активной разработке. Она живёт в отдельном профиле, в папке ", "The Space Age build is in active development. It lives in a profile of its own, in the folder ") +
                p.Root + T(": там свои моды, сейвы и настройки. Основная сборка 2.1 и её сейвы останутся как есть, вернуться можно вкладкой «Paranoidal 2.1».",
                           ": its own mods, saves and settings. The main 2.1 build and its saves stay as they are; the \"Paranoidal 2.1\" tab switches back."), 680);
            AddText(fr, T("Лаунчер один раз скопирует в профиль настройки игры (графика, клавиши), настройки модов, вход в аккаунт и чертежи. ",
                          "The launcher copies the game settings (graphics, controls), the mod settings, the account login and the blueprints into the profile once. ") +
                (p.Seed.Count > 0 ? T("За основу он возьмёт вашу сборку 2.1, поэтому скачается только разница.", "Your 2.1 build is the base, so only the difference is downloaded.")
                                  : T("Сборка Space Age скачается целиком, около 1 ГБ.", "The Space Age build is downloaded in full, about 1 GB.")), Theme.Text);
            var chk = Styled(new CheckBox
            {
                Text = T("Скопировать сейвы основной сборки", "Copy the saves of the main build") + " (" + p.Saves + (p.Saves > 0 ? ", " + Fmt.Size(p.SavesBytes) : "") + ")",
                Checked = false,
                Enabled = p.Saves > 0,
            });
            chk.Bounds = new Rectangle(fr.TextX, fr.Y, fr.TextW, S(24));
            fr.P.Controls.Add(chk);
            fr.Y += S(28);
            AddText(fr, T("Сейвы копируются, оригиналы остаются на месте. Сейвы основной сборки открываются в сборке Space Age, пока не запущена ракета. Сейв Space Age в основной сборке не откроется.",
                          "The saves are copied; the originals stay where they are. Saves of the main build load in the Space Age build until a rocket is launched. A Space Age save does not load in the main build."), Theme.Muted);
            var space = new Label { ForeColor = Theme.Text, BackColor = Theme.Pane, UseMnemonic = false, Bounds = new Rectangle(fr.TextX, fr.Y, fr.TextW, S(40)) };
            fr.P.Controls.Add(space);
            fr.Y += S(44);
            bool spaceAge = fac != null && !fac.MissingDlcFor(Lines.SA).Contains("space-age");
            if (!spaceAge)
                AddText(fr, T("Нужно дополнение Space Age: в этой игре его нет. Без него сборка Space Age не запустится.",
                              "It needs the Space Age expansion, and this game does not have it. The Space Age build does not run without it."), Theme.Red);
            var ok = Buttons(fr, T("Установить Space Age", "Install Space Age"), T("Назад", "Back"));
            Action check = () =>
            {
                long need = p.Need(chk.Checked);
                bool enough = p.Free < 0 || p.Free >= need;
                space.Text = T("Нужно места на диске: около ", "Disk space needed: about ") + Fmt.Size(need) + T(" (сборка и кэш загрузки игры)", " (the build and the game's load cache)") +
                    (p.Free >= 0 ? T(", свободно ", ", free ") + Fmt.Size(p.Free) : "") + ".";
                space.ForeColor = enough ? Theme.Text : Theme.Red;
                ok.Enabled = enough && spaceAge;
            };
            chk.CheckedChanged += (s, e) => check();
            check();
            bool? result = null;
            ok.Click += (s, e) => { result = chk.Checked; fr.F.DialogResult = DialogResult.OK; fr.F.Close(); };
            Show(fr.F, owner);
            fr.F.Dispose();
            return result;
        }

        /// <summary>The move of the Space Age profile to another folder (GameProfile.PlanMove). True: «Перенести».</summary>
        public static bool ConfirmProfileMove(IWin32Window owner, GameProfile.MovePlan p)
        {
            Func<string, string, string> T = Tr.T;
            var sub = T("Из: ", "From: ") + p.From + "\n" + T("В: ", "To: ") + p.To + "\n\n" +
                T("Переедут моды, сейвы, настройки и кэш загрузки (", "The mods, saves, settings and the load cache move (") + Fmt.Size(p.Bytes) + ")." +
                (p.Free >= 0 ? T(" На диске ", " The disk ") + Path.GetPathRoot(p.To) + T(" свободно ", " has free ") + Fmt.Size(p.Free) + "." : "") +
                (p.SameVolume ? "" : "\n" + T("Профиль переезжает на другой диск: он скопируется, а старая папка удалится только после полной копии. Это займёт несколько минут.",
                                                "The profile goes to another disk: it is copied, and the old folder is deleted only after the full copy. It takes a few minutes.")) +
                (p.OutsideGame ? "\n" + T("Профиль будет лежать вне папки игры: если копировать папку игры на другой компьютер, профиль Space Age нужно переносить отдельно.",
                                          "The profile will be outside the game folder: when you copy the game folder to another PC, move the Space Age profile separately.") : "");
            return DarkDialog.Ask(owner, T("Профиль Space Age", "Space Age profile"), T("Перенести профиль Space Age?", "Move the Space Age profile?"), sub, null,
                T("Перенести", "Move"), T("Отмена", "Cancel"), Theme.Title) == DialogResult.Yes;
        }

        /// <summary>The chosen folder already has a Space Age profile. True: use it (the current one stays where it is).</summary>
        public static bool ConfirmUseProfile(IWin32Window owner, GameProfile.MovePlan p)
        {
            Func<string, string, string> T = Tr.T;
            return DarkDialog.Ask(owner, T("Профиль Space Age", "Space Age profile"), T("В этой папке уже есть профиль Space Age", "This folder already has a Space Age profile"),
                p.To + "\n\n" + T("Использовать его? Лаунчер ничего не перенесёт и не перезапишет, а профиль из папки\n", "Use it? The launcher moves and overwrites nothing; the profile in\n") +
                p.From + T("\nостанется на месте.", "\nstays where it is."), null, T("Использовать", "Use it"), T("Отмена", "Cancel"), Theme.Title) == DialogResult.Yes;
        }

        /// <summary>The host's version line. Null: cancelled.</summary>
        public static string AskHostLine(IWin32Window owner)
        {
            Func<float, int> S = Theme.S;
            var fr = Open(Tr.T("Версия хоста", "Host's version"), Tr.T("Перейти на версию сборки хоста", "Switch to the host's build version"),
                Tr.T("Попросите хоста нажать «Скопировать версию» и вставьте его строку сюда. Лаунчер скачает ровно эту версию сборки " +
                     "(нужен интернет, скачается только разница) и включит «Не менять версию», чтобы не предлагать обновление. " +
                     "Когда хост обновится, снимите эту галочку.",
                     "Ask the host to press \"Copy version\" and paste the line here. The launcher downloads exactly that build version " +
                     "(it needs internet and downloads only the difference) and turns on \"Keep this version\", so it stops offering the update. " +
                     "When the host updates, clear that check box."), 620);
            var box = new TextBox
            {
                Multiline = true,
                WordWrap = true,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Deep,
                ForeColor = Theme.Text,
                Font = new Font("Consolas", 9.5f),
                Bounds = new Rectangle(fr.Pad, fr.Y, fr.Inner, S(72)),
                Tag = Card.DeepTag,
            };
            try { if (Clipboard.ContainsText() && VersionLine.ParseHost(Clipboard.GetText()) != null) box.Text = Clipboard.GetText().Trim(); } catch { }
            fr.P.Controls.Add(box);
            fr.Y += S(72) + S(8);
            var hint = new StatusLine(Theme.Body) { BackColor = Theme.Pane, Bounds = new Rectangle(fr.Pad, fr.Y, fr.Inner, S(22)) };
            fr.P.Controls.Add(hint);
            fr.Y += S(30);
            Action check = () =>
            {
                var sha = VersionLine.ParseHost(box.Text);
                hint.Text = box.Text.Trim().Length == 0 ? "" : sha != null ? Tr.T("Версия сборки в строке: ", "Build version in the line: ") + sha
                    : Tr.T("В строке нет версии сборки (номера в скобках).", "The line has no build version (the id in parentheses).");
                hint.Color = sha != null ? Theme.Green : Theme.Orange;
            };
            box.TextChanged += (s, e) => check();
            check();
            var ok = Buttons(fr, Tr.T("Перейти", "Switch"));
            string result = null;
            ok.Click += (s, e) =>
            {
                if (VersionLine.ParseHost(box.Text) == null) { box.Focus(); return; }
                result = box.Text.Trim();
                fr.F.DialogResult = DialogResult.OK;
                fr.F.Close();
            };
            fr.F.Shown += (s, e) => box.Focus();
            Show(fr.F, owner);
            fr.F.Dispose();
            return result;
        }
    }
}
