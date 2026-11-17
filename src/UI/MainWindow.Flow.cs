using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Paranoidal
{
    sealed partial class MainWindow
    {
        sealed class StartData { public FactorioInfo Fac, Saved; public bool FromSettings; public List<FactorioInfo> All; }

        /// <summary>The window is shown: the journal's first lines, cleanup after an update or a move, then the game search.</summary>
        async Task Startup()
        {
            AppendLog(AppInfo.Name + " " + AppInfo.Version, LogKind.Muted);
            AppendLog("Данные лаунчера: " + AppInfo.DataDir, LogKind.Muted);
            if (AppInfo.DataDirNote != null) AppendLog(AppInfo.DataDirNote, LogKind.Warn);
            if (line20Note != null) AppendLog(line20Note, LogKind.Info);
            if (AppInfo.TestOffline) AppendLog("Тестовый режим: связь с GitHub отключена (PARANOIDAL_LAUNCHER_TEST_OFFLINE=1).", LogKind.Warn);
            if (Releases.Base != "https://github.com") AppendLog("Тестовый режим: релизы лаунчера и гайда берутся с " + Releases.Base + ".", LogKind.Warn);
            var exePath = SelfUpdate.CurrentExe;
            await Task.Run(() => SelfUpdate.CleanupOldAsync(exePath));
            if (StartArgs.UpdatedFrom != null)
            {
                AppendLog("Лаунчер обновлён: " + StartArgs.UpdatedFrom + " → " + AppInfo.Version + ".", LogKind.Ok);
                lblDetail.Text = Tr.T("Лаунчер обновлён до ", "The launcher is updated to ") + AppInfo.Version;
            }
            if (StartArgs.MovedFrom != null)
            {
                var removed = await Task.Run(() => Relocation.CleanupOldPlaceAsync(StartArgs.MovedFrom, exePath, AppInfo.DataDir));
                AppendLog("Лаунчер перенесён в папку игры: " + AppInfo.ExeDir + ". Со старого места удалено: " + (removed.Count == 0 ? "ничего" : string.Join(", ", removed)) +
                    ". На рабочем столе есть ярлык «" + Path.GetFileNameWithoutExtension(Shortcuts.LauncherLink) + "».", LogKind.Ok);
                lblDetail.Text = Tr.T("Лаунчер перенесён в папку игры, на рабочем столе есть ярлык для запуска", "The launcher moved into the game folder; a shortcut is on the desktop");
            }
            RefreshGuideLocal();
            RefreshStartTexts();
            await DetectGame();
        }

        /// <summary>
        /// First part of the start: the game. The game of settings.ini when it fits; otherwise every installation is
        /// searched (the launcher lies in a Factorio 2.0.77 folder or in Downloads, the game was moved). Then the launcher
        /// moves into the folder of the suitable game, or the start window says that none fits.
        /// </summary>
        async Task DetectGame()
        {
            if (!mainShown)
            {
                start.SetBusy(Tr.T("Ищу Factorio…", "Looking for Factorio…"), "");
                SetScreen(StartPanel.View.Busy);
            }
            var sd = await Task.Run(() =>
            {
                Stage("Ищу Factorio…", "Looking for Factorio…");
                Progress(null, "");
                var detected = new StartData();
                detected.Saved = FactorioInfo.Inspect(Settings.GetPath("factorio"));
                // the player postponed the choice of installation last time: the saved game is only the one used meanwhile
                bool ask = Settings.Get("pick-install") == "1";
                if (detected.Saved != null && detected.Saved.Ok && !ask) { detected.Fac = detected.Saved; detected.FromSettings = true; return detected; }
                detected.All = FactorioInfo.DetectAll(AppDomain.CurrentDomain.BaseDirectory);
                if (detected.Saved != null && !detected.All.Any(x => Relocation.SameDir(x.Dir, detected.Saved.Dir))) detected.All.Insert(0, detected.Saved);
                detected.Fac = ask && detected.Saved != null && detected.Saved.Ok ? detected.Saved : detected.All.FirstOrDefault(x => x.Ok);
                return detected;
            });
            await FlushProgress();
            if (closed) return;
            await AfterDetect(sd);
        }

        async Task AfterDetect(StartData sd)
        {
            if (sd.Saved != null && !sd.Saved.Ok)
                AppendLog("Factorio из настроек не подходит: " + sd.Saved.Dir + " (" + sd.Saved.Describe(false, Lines.L21) + "). Ищу другие установки.", LogKind.Warn);
            if (sd.Fac == null)
            {
                AppendLog("Подходящая Factorio не найдена" + (sd.All != null && sd.All.Count > 0
                    ? ", есть только: " + string.Join("; ", sd.All.Select(x => x.Dir + " (" + x.Describe(false, Lines.L21) + ")")) : ": установок Factorio нет") +
                    ". Нужна " + Lines.L21.RequiredGame + ".", LogKind.Warn);
                ShowNeed(sd.All ?? new List<FactorioInfo>());
                return;
            }
            fac = sd.Fac;
            // the launcher lay in another game's folder (Factorio 2.0.77): its copy in the new folder forgets that game
            if (await MaybeRelocate(sd.All, sd.FromSettings)) return;   // the copy in the game folder takes over
            await AcceptGame();
        }

        /// <summary>
        /// The suitable game is known and the launcher stays where it is: settings of another game folder go (its mods
        /// folder, Space Age profile, pin), then the start window or, with a remembered build, the main window.
        /// </summary>
        async Task AcceptGame()
        {
            ForgetOtherGame();
            Settings.SetPath("factorio", fac.Dir);
            Settings.Set("pick-install", installPostponed ? "1" : "");
            AppendLog("Factorio: " + fac.Dir + " (" + fac.Describe(false) + ")", LogKind.Ok);
            if (mainShown)
            {
                if (Lines.Current.SpaceAge && !GameProfile.SpaceAgeExists(fac)) { await ShowSelector(); return; }
                await EnsureEngine();
                if (!closed) await StartLine();
                return;
            }
            await ShowSelector();
        }

        /// <summary>
        /// settings.ini points at another game folder than `fac`: what belonged to it goes (its mods folder, Space Age
        /// profile, pin, last starts), so the launcher uses the folders of this game.
        /// </summary>
        void ForgetOtherGame()
        {
            var saved = Settings.GetPath("factorio");
            if (saved == null || fac == null || Relocation.SameDir(saved, fac.Dir)) return;
            Settings.Forget(Relocation.IsGameKey);
            pinSetting = Settings.Get(Lines.Key("pin")) == "1";
            chkPin.Checked = pinSetting;
            AppendLog("Папка игры сменилась: " + saved + " → " + fac.Dir + ". Настройки прежней игры (папка модов, профиль Space Age, «Не менять версию») сброшены.", LogKind.Info);
        }

        // ------------------------------------------------------------------ the start window (StartScreen.cs)

        /// <summary>No suitable game was found: the installations that were found, for the texts of that view.</summary>
        List<FactorioInfo> needFound;

        /// <summary>The start window: «no suitable Factorio» (what was found, how to get 2.1). Tests close the launcher.</summary>
        void ShowNeed(List<FactorioInfo> found)
        {
            needFound = found;
            if (Auto)
            {
                AppendLog("[тест: подходящей Factorio нет, лаунчер закрывается]", LogKind.Muted);
                CloseLater();
                return;
            }
            start.Show(StartPanel.View.Need);
            RefreshStartTexts();
            SetScreen(StartPanel.View.Need);
        }

        /// <summary>The cards of the start window: whether each build is in its mods folder.</summary>
        void FillCards()
        {
            var c21 = start.Card21;
            var b21 = fac != null ? BuildFound.Scan(GameProfile.MainMods(fac)) : new BuildFound();
            bool have21 = b21.Present && Lines.L21.Matches(b21.FactorioVersion);
            c21.Title = Lines.L21.Name;
            c21.Note = Tr.T("основная сборка", "the main build");
            c21.NoteColor = Gui.Muted;
            if (have21) { c21.State = Tr.T("Установлена", "Installed"); c21.StateColor = Gui.Green; c21.StateNote = "zzzparanoidal " + (b21.ZzzVersion ?? "?"); }
            else if (b21.Present && !b21.Legacy) { c21.State = Tr.T("Нужно обновить", "Needs an update"); c21.StateColor = Gui.Orange; c21.StateNote = Tr.T("сейчас сборка для Factorio ", "the build is for Factorio ") + b21.FactorioVersion; }
            else { c21.State = Tr.T("Не установлена", "Not installed"); c21.StateColor = Gui.Orange; c21.StateNote = Tr.T("скачается около 1 ГБ", "about 1 GB to download"); }

            var csa = start.CardSa;
            csa.Title = Lines.SA.Name;
            csa.Note = Tr.T("в активной разработке", "in active development");
            csa.NoteColor = Gui.Orange;
            var root = fac != null ? GameProfile.SpaceAgeRoot(fac) : null;
            var bsa = root != null ? BuildFound.Scan(Path.Combine(root, "mods")) : new BuildFound();
            if (bsa.Present) { csa.State = Tr.T("Установлена", "Installed"); csa.StateColor = Gui.Green; csa.StateNote = "zzzparanoidal " + (bsa.ZzzVersion ?? "?"); }
            else
            {
                csa.State = Tr.T("Не установлена", "Not installed");
                csa.StateColor = Gui.Orange;
                csa.StateNote = have21 ? Tr.T("скачается только разница со сборкой 2.1", "only the difference to the 2.1 build is downloaded")
                    : Tr.T("скачается около 1 ГБ", "about 1 GB to download");
            }
            c21.InvalidateVisual();
            csa.InvalidateVisual();
        }

        /// <summary>The texts of «no suitable Factorio» for what was found (the start window).</summary>
        void FillNeed()
        {
            var found = needFound ?? new List<FactorioInfo>();
            var l = Lines.L21;
            var how = string.Join("\n", FactorioInfo.HowToGet21().Select(x => "• " + x));
            var tail = Tr.T("Если нужная игра стоит в другой папке, выберите её. Моды и сейвы в найденных папках лаунчер не трогает.",
                "If the game you need is in another folder, choose it. The launcher leaves the mods and saves of the folders it found alone.");
            // the most useful finding first: a 2.1 game without the expansion, then a 2.1 game that is too old, then any other
            var noDlc = found.FirstOrDefault(x => x.VersionOkFor(l) && !x.SpaceAgeFor(l));
            var old = found.Where(x => !x.VersionOkFor(l)).ToList();
            string title, head, body;
            if (noDlc != null)
            {
                bool none = noDlc.MissingDlcFor(l).Count == l.Dlc.Length;
                title = Tr.T("Нужно дополнение Space Age", "The Space Age expansion is needed");
                head = none ? Tr.T("Найдена Factorio " + noDlc.Version + " без дополнения Space Age", "Found Factorio " + noDlc.Version + " without the Space Age expansion")
                            : Tr.T("Дополнение Space Age установлено не полностью", "The Space Age expansion is not installed completely");
                body = noDlc.Dir + "\n\n" + (none
                    ? Tr.T("Сборке Paranoidal нужно дополнение Space Age: Quality, Elevated Rails и Recycler входят в него. В сборке Paranoidal 2.1 сама часть Space Age с планетами выключена.\n" +
                           "Купите или включите дополнение в Steam, GOG или на factorio.com, затем нажмите «Проверить снова».",
                           "The Paranoidal build needs the Space Age expansion: Quality, Elevated Rails and Recycler come with it. The planets part of Space Age is switched off in the Paranoidal 2.1 build.\n" +
                           "Buy or enable the expansion in Steam, GOG or on factorio.com, then press \"Check again\".")
                    : Tr.T("Не хватает: ", "Missing: ") + string.Join(", ", noDlc.MissingDlcFor(l)) +
                      Tr.T(". Проверьте целостность файлов игры в Steam или GOG, затем нажмите «Проверить снова».", ". Verify the integrity of the game files in Steam or GOG, then press \"Check again\"."));
            }
            else if (old.Count > 0)
            {
                title = Tr.T("Нужна Factorio ", "Factorio ") + l.MinGame + Tr.T(" или новее", " or newer is needed");
                var versions = string.Join(", ", old.Select(x => x.Version ?? "?").Distinct());
                head = Tr.T("Найдена только Factorio ", "Only Factorio ") + versions + Tr.T("", " was found");
                body = string.Join("\n", old.Select(x => x.Dir + "  (Factorio " + (x.Version ?? "?") + ")")) + "\n\n" +
                    Tr.T("Сборка Paranoidal работает на Factorio " + l.VersionWords(false) + " с дополнением Space Age. Как обновить игру:\n",
                         "The Paranoidal build runs on Factorio " + l.VersionWords(true) + " with the Space Age expansion. How to update the game:\n") + how;
            }
            else
            {
                title = Tr.T("Factorio не найдена", "Factorio not found");
                head = Tr.T("Лаунчер не нашёл Factorio", "The launcher did not find Factorio");
                body = Tr.T("Он искал рядом с собой (" + AppInfo.ExeDir + "), в Steam, GOG и в папках вроде C:\\Games\\Factorio.\n\n" +
                            "Нужна Factorio " + l.VersionWords(false) + " с дополнением Space Age. Если игра установлена, выберите её папку. Если нет, установите её:\n",
                            "It looked next to itself (" + AppInfo.ExeDir + "), in Steam, GOG and in folders like C:\\Games\\Factorio.\n\n" +
                            "It needs Factorio " + l.VersionWords(true) + " with the Space Age expansion. If the game is installed, choose its folder. If not, install it:\n") + how;
                tail = null;
            }
            start.Header.Title = title;
            start.SetNeed(head, body, tail);
        }

        /// <summary>Every text of the start window in the interface language (also after a language switch).</summary>
        void RefreshStartTexts()
        {
            start.BtnLang.Text = Tr.T("Язык: RU", "Language: EN");
            Tips.Set(start.BtnLang, Tr.T("Switch to English", "Переключить на русский"));
            start.ChkRemember.Text = Tr.T("Запомнить выбор", "Remember the choice");
            Tips.Set(start.ChkRemember, Tr.T("С галочкой лаунчер при следующем запуске сразу откроет выбранную сборку.\nВернуть это окно можно галочкой «Спрашивать сборку при запуске» в главном окне.",
                "Checked: the next start opens the chosen build right away.\nThe \"Ask for the build at start\" box of the main window brings this window back."));
            start.LblHint.Text = Tr.T("Сборку можно сменить в любой момент вкладками главного окна.", "You can change the build any time with the tabs of the main window.");
            start.BtnClose.Text = Tr.T("Закрыть", "Close");
            start.BtnPick.Text = Tr.T("Выбрать папку игры…", "Choose the game folder…");
            start.BtnRecheck.Text = Tr.T("Проверить снова", "Check again");
            Tips.Set(start.BtnClose, Tr.T("Закрыть лаунчер. На диске ничего не меняется, при следующем запуске будет то же окно.",
                "Close the launcher. Nothing on disk changes; the next start shows the same window."));
            Tips.Set(start.BtnRecheck, Tr.T("Найти Factorio ещё раз, например после обновления игры в Steam.", "Look for Factorio again, e.g. after Steam updated the game."));
            if (start.Current == StartPanel.View.Selector) { start.Header.Title = Tr.T("Выберите сборку", "Choose the build"); FillCards(); }
            else if (start.Current == StartPanel.View.Need) FillNeed();
            else start.Header.Title = AppInfo.Name;
            start.Header.InvalidateVisual();
            start.DoLayout();
            if (!mainShown && opened) SetScreen(start.Current);   // the height follows the texts
        }

        /// <summary>The start window: the choice of the build. Tests pick it (PARANOIDAL_LAUNCHER_TEST_PICK).</summary>
        async Task ShowSelector()
        {
            if (Auto)
            {
                var pick = TestPick == "sa" ? Lines.SA : TestPick == "21" ? Lines.L21 : Lines.Current;
                AppendLog("[тест: окно выбора сборки без показа, выбрана " + pick.Name + "]", LogKind.Muted);
                await ChooseBuild(pick, false);
                return;
            }
            start.Show(StartPanel.View.Selector);
            RefreshStartTexts();
            SetScreen(StartPanel.View.Selector);
            await EnsureEngine();   // selection becomes available after MinGit is ready
        }

        /// <summary>A card of the start window was clicked.</summary>
        async Task PickBuild(BuildLine line)
        {
            if (mainShown || start.Current != StartPanel.View.Selector) return;
            await ChooseBuild(line, start.ChkRemember.Checked);
        }

        async Task ChooseBuild(BuildLine line, bool remember)
        {
            AppendLog("Выбрана сборка " + line.Name + (remember ? ", выбор запомнен: окно выбора больше не покажется" : "") + ".", LogKind.Info);
            if (remember) chkAsk.Checked = false;   // also writes ask-build=0 and logs it
            await OpenLine(line);   // nothing is installed until the player presses «Установить»
        }

        /// <summary>The main window with the chosen build (from the start window): its mods folder, the status check.</summary>
        async Task OpenLine(BuildLine line)
        {
            Lines.Set(line);
            Settings.Set("line", line.Id);
            pinSetting = Settings.Get(Lines.Key("pin")) == "1";
            chkPin.Checked = pinSetting;
            SetScreen(null);
            ApplyTexts();
            UpdateUi();
            await EnsureEngine();
            if (!closed) await StartLine();
        }

        /// <summary>«Выбрать папку игры…» of «no suitable Factorio»: the chosen game, then the same way as a game found.</summary>
        async Task PickGameForStart()
        {
            var dir = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Tr.T("Выберите папку Factorio 2.1 (в ней есть ", "Choose the Factorio 2.1 folder (it has ") + FactorioInfo.ExeRelative + ")",
                SuggestedStartLocation = await StorageProvider.StartAt(needFound != null && needFound.Count > 0 ? needFound[0].Dir : null),
            }).LocalPath();
            if (dir == null) return;
            var f = FactorioInfo.Inspect(dir);
            if (f == null)
            {
                await DarkDialog.Info(this, Tr.T("Factorio не найдена", "Factorio not found"), Tr.T("В папке\n", "The folder\n") + dir + Tr.T("\nнет ", "\nhas no ") + FactorioInfo.ExeRelative + ".", Gui.Red);
                return;
            }
            if (!f.OkFor(Lines.L21))
            {
                AppendLog("Выбрана папка " + f.Dir + ", она не подходит (" + f.Describe(false, Lines.L21) + ").", LogKind.Warn);
                await DarkDialog.Info(this, Tr.T("Версия Factorio", "Factorio version"), f.Problem(Lines.L21), Gui.Red);
                return;
            }
            AppendLog("Выбрана папка игры: " + f.Dir + " (" + f.Describe(false, Lines.L21) + ").", LogKind.Info);
            await AfterDetect(new StartData { Fac = f, FromSettings = true });
        }

        /// <summary>One initialization task, shared by every caller; no callback can replace another caller's continuation.</summary>
        Task EnsureEngine() => engineStartup ??= CreateEngine();

        async Task CreateEngine()
        {
            try
            {
                git = await Task.Run(() =>
                {
                    var exe = GitBootstrap.Ensure(Stage);
                    return new GitRunner(exe, Path.Combine(AppInfo.DataDir, "no-global-config"));
                });
                engine = new SyncEngine(git, this);
                await FlushProgress();
            }
            catch
            {
                engineStartup = null;
                if (!closed && !mainShown) SetScreen(null);
                throw;
            }
        }

        /// <summary>
        /// The mods folder of the current line: the one in settings.ini for 2.1 (null: the game's mods folder), the mods
        /// folder of the profile for Space Age (GameProfile).
        /// </summary>
        static string LineMods(FactorioInfo f)
        {
            if (!Lines.Current.SpaceAge) return Settings.GetPath("mods");
            var root = GameProfile.SpaceAgeRoot(f);
            return root == null ? null : Path.Combine(root, "mods");
        }

        /// <summary>The main window serves the current line: its mods folder, the game it needs, the load diagnostics, the status check.</summary>
        async Task StartLine()
        {
            OpToken.ThrowIfCancellationRequested();
            var f0 = fac;
            var line = Lines.Current;
            var mods = await Task.Run(() =>
            {
                var path = LineMods(f0);
                // a game folder copied elsewhere: the old absolute path of the mods folder points nowhere
                // (the Space Age profile folder is created by the update)
                if (path != null && !line.SpaceAge && !Directory.Exists(Path.GetDirectoryName(path) ?? path)) path = null;
                // the game the installed build needs (zzzparanoidal: base >= x.y.z), before the game is checked
                var b = BuildFound.Scan(path ?? Path.Combine(f0.Dir, "mods"));
                if (b.Present && line.Matches(b.FactorioVersion)) line.SetRequired(b.BaseMin);
                return path;
            });
            await FlushProgress();
            if (closed) return;
            modsDir = mods ?? Path.Combine(fac.Dir, "mods");
            AppendLog("Сборка " + line.Name + ", папка модов: " + modsDir + ".", LogKind.Info);
            if (!line.SpaceAge) Settings.SetPath("mods", modsDir);
            ApplyTexts();
            UpdateUi();
            gameRunning = FactorioInfo.IsRunning();
            LogoArt.Paused = gameRunning || WindowState == WindowState.Minimized;
            gameWatch.Start();
            if (!gameRunning) await AnalyzeLoad();
            await CheckStatus();
        }

        /// <summary>The line under the buttons after a status check, from the current state (also after a language switch).</summary>
        string StatusIdleText()
        {
            if (remote == null && offline && !BuildPresent) return Tr.T("Нет связи с GitHub, поэтому установить сборку сейчас нельзя", "No connection to GitHub, so the build cannot be installed now");
            else if (remote == null && offline) return Tr.T("Нет связи с GitHub. Играть можно, а обновиться нельзя", "No connection to GitHub. You can play, but not update");
            else if (remote == null) return Tr.T("Не удалось проверить обновления", "Could not check for updates");
            else if (OtherLineBuild)
                return Tr.T("Сборка для Factorio " + disk.FactorioVersion + ": «" + (installed == null ? "Установить" : "Обновить") + "» переведёт её на " + Lines.Current.Name + " и скачает только разницу",
                    "The build is for Factorio " + disk.FactorioVersion + ": \"" + (installed == null ? "Install" : "Update") + "\" switches it to " + Lines.Current.Name + " and downloads only the difference");
            else if (installed == null)
            {
                if (found != null && found.Present && found.Legacy)
                    return Tr.T("В папке mods старая сборка для Factorio 1.1, «Установить» заменит её", "The mods folder has the old build for Factorio 1.1: \"Install\" replaces it");
                else if (found != null && found.Present)
                    return Tr.T("Версия сборки не определена, «Установить» скачает отличающиеся файлы", "The build version is unknown: \"Install\" downloads the files that differ");
                else return Tr.T("Сборка не установлена, нажмите «Установить»", "The build is not installed: press \"Install\"");
            }
            else if (modified > 0) return Tr.T("Файлы сборки изменены, «Проверить файлы» вернёт их", "Build files were changed: \"Check files\" restores them");
            else if (installed == remote && GuideUpdate && !Pinned) return GuideUpdateText() + Tr.T(", нажмите «Обновить»", ": press \"Update\"");
            else if (installed == remote) return Tr.T("Установлена последняя версия", "The latest version is installed");
            else if (Pinned) return Tr.T("Версия закреплена, обновление не устанавливается", "The version is kept, so the update is not installed");
            else return Tr.T("Доступно обновление сборки", "A build update is available") + (remoteDate != null ? Tr.T(" от ", " of ") + remoteDate : "");
        }

        async Task CheckStatus()
        {
            OpToken.ThrowIfCancellationRequested();
            if (engine == null) return;
            var mods = modsDir;
            var branch = Lines.Current.Branch;
            var res = await Task.Run(async () =>
            {
                // the launcher's and the guide's releases are checked at the same time as the build
                var releases = Releases.CheckAllAsync();
                // which game the latest build of the line needs (zzzparanoidal of the branch head on GitHub)
                var head = RemoteBuild.InfoAsync(branch);
                StatusInfo s;
                try { s = await engine.CheckStatusAsync(mods); }
                finally { await Task.WhenAll(releases, head); }   // InfoAsync never throws; CheckAllAsync keeps the errors
                return Tuple.Create(s, await releases, await head);
            });
            await FlushProgress();
            if (closed) return;
            var st = res.Item1;
            ApplyStatus(st, false);
            ApplyRequirement(res.Item3);
            ApplyReleases(res.Item2, st.Offline);
            Idle(StatusIdleText());
            progress.Value = installed != null && installed == remote ? 1 : 0;
            if (adopted)
                lblDetail.Text = Tr.T("Сборка в папке mods опознана: версия от ", "The build in the mods folder was recognised: the version of ") + installedDate;
            else
                lblDetail.Text = st.CommitsAreNew && st.Commits != null
                    ? Tr.T("Новых изменений в сборке: ", "New changes in the build: ") + st.Commits.Count + Tr.T(", они на вкладке «Что нового»", ", listed on the \"What's new\" tab")
                    : lblDetail.Text.StartsWith(Tr.T("Лаунчер ", "The launcher ")) ? lblDetail.Text : "";
            // tests: one switch of the build after the first check, then «Установить»/«Обновить» if asked
            if (Auto && TestSwitch != null && !testSwitched && !restarting)
            {
                testSwitched = true;
                var to = TestSwitch == "sa" ? Lines.SA : Lines.L21;
                if (to != Lines.Current) { await SwitchLine(to); return; }
            }
            if (Auto && TestSaFolder != null && !testMoved && !restarting && Lines.Current.SpaceAge)
            {
                testMoved = true;
                AppendLog("[тест: «Выбрать…» папки модов Space Age: " + TestSaFolder + "]", LogKind.Muted);
                await PickSpaceAgeFolder(TestSaFolder);
                return;
            }
            if (Auto && TestInstall && !testInstalled && !restarting && UpdateWanted)
            {
                testInstalled = true;
                autoCloseAfterUpdate = true;
                AppendLog("[тест: нажата «" + btnUpdate.Text + "»]", LogKind.Muted);
                await InstallOrUpdate();
                return;
            }
            await OfferSelfUpdate();
            if (Auto && !restarting && guideLocal == null && guideRel != null) await OpenGuide();   // tests: the download path of «Гайд»
            else if (Auto && !restarting) CloseLater();
        }

        /// <summary>
        /// The game version the build needs: the installed build's own requirement, else the one of the latest build of
        /// the line; a warning when the latest build needs a newer game than the player has.
        /// </summary>
        void ApplyRequirement(BuildFound head)
        {
            var line = Lines.Current;
            remoteReq = head != null && line.Matches(head.FactorioVersion) ? head.BaseMin : null;
            var local = disk != null && disk.Present && line.Matches(disk.FactorioVersion) ? disk.BaseMin : null;
            line.SetRequired(local ?? remoteReq);
            header.InvalidateVisual();
            if (remoteReq != null && fac != null && !line.Exact && line.Matches(fac.Version) && Releases.Compare(fac.Version, remoteReq) < 0 && remote != installed)
                AppendLog("Новая версия сборки требует Factorio " + remoteReq + " или новее, а у вас " + fac.Version +
                    ". Обновите игру, иначе после обновления сборки играть будет нельзя.", LogKind.Warn);
        }

        // ------------------------------------------------------------------ launcher and guide releases

        void ApplyReleases(ReleaseCheck rc, bool offline)
        {
            var kind = offline ? LogKind.Muted : LogKind.Warn;
            if (rc.Launcher != null) launcherRel = rc.Launcher;
            else AppendLog("Не удалось проверить новую версию лаунчера: " + rc.LauncherError, kind);
            if (rc.Guide != null) guideRel = rc.Guide;
            else AppendLog("Не удалось проверить гайд по сборке: " + rc.GuideError, kind);
            RefreshGuideLocal();
            if (launcherRel != null && Releases.Compare(launcherRel.Version, AppInfo.Version) <= 0)
                AppendLog("Лаунчер " + AppInfo.Version + ": это последняя версия.", LogKind.Muted);
            if (guideRel != null)
                AppendLog(GuideUpdate ? "Вышла новая версия гайда по сборке: " + guideRel.Version + (guideLocalVersion != null ? " (у вас " + guideLocalVersion + ")" : " (гайд ещё не скачан)") +
                    ". Он скачается вместе с обновлением сборки." : "Гайд по сборке " + guideLocalVersion + ": это последняя версия.",
                    GuideUpdate ? LogKind.Info : LogKind.Muted);
        }

        /// <summary>A newer guide is released (or none is downloaded yet): it is downloaded with the next build update.</summary>
        bool GuideUpdate => GuideFile.NeedsUpdate(guideRel, guideLocalVersion);

        string GuideUpdateText() => guideLocalVersion == null
            ? Tr.T("Гайд по сборке ещё не скачан", "The modpack guide is not downloaded yet")
            : Tr.T("Вышла новая версия гайда по сборке: ", "A new version of the modpack guide: ") + guideRel.Version;

        void RefreshGuideLocal()
        {
            guideLocal = GuideFile.Local();
            guideLocalVersion = guideLocal == null ? null : GuideFile.Version(guideLocal);
            ShowGuideState();
        }

        void ShowGuideState()
        {
            if (btnGuide == null) return;
            Tips.Set(btnGuide, Tr.T(
                "Иллюстрированный гайд по сборке для игроков ванильной Factorio. Это один файл " + AppInfo.GuideFile + "\n" +
                "рядом с лаунчером. Он открывается в браузере и работает без интернета, а новая версия скачивается вместе с обновлением сборки.\n",
                "An illustrated modpack guide for vanilla Factorio players. It is one file, " + AppInfo.GuideFile + ",\n" +
                "next to the launcher. It opens in the browser and works offline; a new version comes with the build update.\n") +
                (guideLocal == null ? Tr.T("Гайд ещё не скачан, кнопка скачает его (нужен интернет).", "Not downloaded yet: the button downloads it (needs internet).")
                    : Tr.T("Версия ", "Version ") + (guideLocalVersion ?? "?") + ": " + guideLocal));
        }

        /// <summary>«Гайд»: opens the local copy; downloads it first only when there is none.</summary>
        async Task OpenGuide()
        {
            RefreshGuideLocal();
            if (guideLocal != null)
            {
                try { GuideFile.Open(guideLocal); AppendLog("Открыт гайд по сборке " + (guideLocalVersion ?? "?") + ": " + guideLocal, LogKind.Muted); }
                catch (Exception ex) { await DarkDialog.Info(this, Tr.T("Гайд по сборке", "Modpack guide"), Tr.T("Не удалось открыть ", "Could not open ") + guideLocal + ":\n" + ex.Message, Gui.Red); }
                return;
            }
            var known = guideRel;
            var r = await Task.Run(async () =>
            {
                Stage("Скачиваю гайд по сборке…", "Downloading the modpack guide…");
                Progress(null, "");
                var release = known ?? await Releases.LatestAsync(AppInfo.GuideRepo, "guide", AppInfo.GuideFile, ct: OpToken);
                return await InstallGuideAsync(release);
            });
            await FlushProgress();
            if (closed) return;
            RefreshGuideLocal();
            guideRel = r;
            Idle(Tr.T("Гайд по сборке скачан", "The modpack guide is downloaded"));
            lblDetail.Text = guideLocal;
            AppendLogHighlight("Скачан гайд по сборке Paranoidal " + r.Version + ": " + guideLocal);
            if (Auto) { CloseLater(); return; }
            try { GuideFile.Open(guideLocal); } catch { }
        }

        /// <summary>Inside an operation: downloads the guide of the release with progress; returns it.</summary>
        async Task<ReleaseInfo> InstallGuideAsync(ReleaseInfo r)
        {
            CanCancel(true);
            try
            {
                await GuideFile.InstallAsync(r, (d, t) => Progress(t > 0 ? d / (double)t : (double?)null, Fmt.Size(d) + " / " + Fmt.Size(t)), OpToken);
            }
            catch (OperationCanceledException) { throw new SyncException("Скачивание гайда отменено.", "The guide download was cancelled."); }
            catch (SyncException) { throw; }
            catch (Exception ex) { throw new SyncException("Гайд не скачан: " + ex.Message, "The guide was not downloaded: " + ex.Message); }
            Ui(RefreshGuideLocal);
            return r;
        }

        async Task MakeShortcuts()
        {
            try
            {
                RefreshGuideLocal();
                var made = Shortcuts.All(SelfUpdate.CurrentExe, guideLocal);
                AppendLog("Созданы ярлыки: " + string.Join(", ", made), LogKind.Ok);
                await DarkDialog.Info(this, Tr.T("Ярлыки", "Shortcuts"), Tr.T("Ярлыки созданы: на рабочем столе и в меню «Пуск» (папка Paranoidal).", "Shortcuts created: on the desktop and in the Start menu (Paranoidal folder).") +
                    (AppInfo.ExeDir == (fac != null ? fac.Dir : null) || fac == null ? "" : Tr.T("\n\nЛаунчер лежит не в папке игры: ярлыки ведут сюда, пока он не перенесён.", "\n\nThe launcher is not in the game folder: the shortcuts point here until it is moved.")),
                    Gui.Green);
            }
            catch (Exception ex)
            {
                AppendLog("Ярлыки не созданы: " + ex.Message, LogKind.Error);
                await DarkDialog.Info(this, Tr.T("Ярлыки", "Shortcuts"), Tr.T("Не удалось создать ярлыки: ", "Could not create the shortcuts: ") + ex.Message, Gui.Red);
            }
        }

        // ------------------------------------------------------------------ self-update

        /// <summary>A newer launcher: asked on every start (and every manual check) until updated; after the game exits if it runs.</summary>
        async Task OfferSelfUpdate()
        {
            var r = launcherRel;
            if (r == null || restarting || Releases.Compare(r.Version, AppInfo.Version) <= 0) return;
            if (gameRunning)
            {
                if (!selfUpdatePending) AppendLog("Вышла новая версия лаунчера " + r.Version + ": предложу обновиться после выхода из игры.", LogKind.Info);
                selfUpdatePending = true;
                return;
            }
            selfUpdatePending = false;
            AppendLog("Вышла новая версия лаунчера: " + r.Version + " (установлена " + AppInfo.Version + ").", LogKind.Warn);
            if (!Auto && !await Dialogs.AskSelfUpdate(this, r, !AppInfo.ExeDirWritable))
            {
                AppendLog("Обновление лаунчера отложено: лаунчер спросит снова при следующем запуске.", LogKind.Muted);
                return;
            }
            await DoSelfUpdate(r);
        }

        async Task DoSelfUpdate(ReleaseInfo r)
        {
            var exe = SelfUpdate.CurrentExe;
            bool direct = AppInfo.ExeDirWritable;
            var file = direct ? exe + SelfUpdate.NewSuffix : Path.Combine(Path.GetTempPath(), "ParanoidalLauncher-" + r.Version + ".exe");
            await Task.Run(async () =>
            {
                Stage("Скачиваю лаунчер " + r.Version + "…", "Downloading launcher " + r.Version + "…");
                CanCancel(true);
                try { await Releases.DownloadAsync(r, file, (d, t) => Progress(t > 0 ? d / (double)t : (double?)null, Fmt.Size(d) + " / " + Fmt.Size(t)), OpToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { throw new SyncException("Новая версия лаунчера не скачана: " + ex.Message, "The new launcher version was not downloaded: " + ex.Message); }
                var v = SelfUpdate.ExeVersion(file);
                if (v != r.Version)
                {
                    try { File.Delete(file); } catch { }
                    throw new SyncException("Скачанный лаунчер имеет версию " + (v ?? "?") + ", ожидалась " + r.Version + ".",
                        "The downloaded launcher has version " + (v ?? "?") + ", expected " + r.Version + ".");
                }
            });
            await FlushProgress();
            if (closed) return;
            CanCancel(false);
                try
                {
                    if (direct) SelfUpdate.Swap(file, exe);
                    else
                    {
                        var (ok, err) = await SelfUpdate.InstallElevatedAsync(exe, file, AppInfo.ExeDir);
                        if (!ok)
                        {
                            AppendLog(err == null ? "Права администратора не выданы, лаунчер не обновлён." : "Лаунчер не обновлён: " + err, LogKind.Warn);
                            Idle(Tr.T("Лаунчер не обновлён", "The launcher was not updated"));
                            try { File.Delete(file); } catch { }
                            return;
                        }
                        try { File.Delete(file); } catch { }
                    }
                    AppendLog("Лаунчер " + r.Version + " установлен, перезапускаюсь.", LogKind.Ok);
                    SelfUpdate.Restart(exe, "--updated-from " + AppInfo.Version);
                    restarting = true;
                    CloseLater();
                }
                catch (Exception ex)
                {
                    AppendLog("Лаунчер не обновлён: " + ex, LogKind.Error);
                    try { File.Delete(file); } catch { }
                    await DarkDialog.Info(this, Tr.T("Обновление лаунчера", "Launcher update"), Tr.T("Не удалось заменить лаунчер: ", "Could not replace the launcher: ") + ex.Message +
                        Tr.T("\nСкачайте новую версию вручную: ", "\nDownload the new version by hand: ") + Releases.PageUrl(AppInfo.LauncherRepo), Gui.Red);
                }
        }

        // ------------------------------------------------------------------ Paranoidal 2.1 and Space Age (GameProfile)

        /// <summary>
        /// A build tab: the line, its mods folder and its pin change; nothing is installed or updated until the player
        /// presses «Установить»/«Обновить» (owner 2026-11-10). The line is remembered for the start.
        /// </summary>
        async Task SwitchLine(BuildLine to)
        {
            if (engine == null || fac == null || to == Lines.Current) return;
            if (FactorioInfo.IsRunning())
            {
                await DarkDialog.Info(this, Tr.T("Factorio запущена", "Factorio is running"), Tr.T("Закройте игру и переключите сборку снова.", "Close the game and switch the build again."), Gui.Orange);
                return;
            }
            await ApplyLine(to);   // nothing is installed or updated until the player presses «Установить»/«Обновить»
        }

        /// <summary>
        /// «Установить» / «Обновить». Space Age without its profile: the profile window first (what is copied, the saves,
        /// the disk space), then the profile and the build; everything else is the update of the build.
        /// </summary>
        async Task InstallOrUpdate()
        {
            if (engine == null) return;
            if (Lines.Current.SpaceAge && fac != null && !GameProfile.SpaceAgeExists(fac)) { await PrepareSpaceAge(); return; }
            await DoUpdate(false, null);
        }

        /// <summary>
        /// «Установить» on the Space Age tab without a profile: what the profile takes from 2.1, the saves as a choice, the
        /// disk space; after «Установить Space Age» the profile is created and the Space Age build is installed. «Назад»
        /// changes nothing.
        /// </summary>
        async Task PrepareSpaceAge()
        {
            OpToken.ThrowIfCancellationRequested();
            var f = fac;
            var mainMods = GameProfile.MainMods(f);
            var plan = await Task.Run(async () =>
            {
                Stage("Готовлю профиль Space Age…", "Preparing the Space Age profile…");
                Progress(null, "");
                HashSet<string> entries = null;
                try { if (mainMods != null && Directory.Exists(mainMods)) entries = await engine.BuildEntriesAsync(new SyncPaths(mainMods)); } catch { }
                return GameProfile.PlanSpaceAge(f, GameProfile.Of(f, Lines.L21), mainMods, entries);
            });
            await FlushProgress();
            if (closed) return;
                Idle(mainShown ? StatusIdleText() : "");
                if (plan.Error != null)
                {
                    AppendLog("Профиль Space Age не создан: " + plan.Error, LogKind.Warn);
                    await DarkDialog.Info(this, "Space Age", Tr.T(plan.Error, plan.ErrorEn), Gui.Red);
                    return;
                }
                AppendLog("Профиль Space Age: " + plan.Root + ", основа из сборки 2.1: модов " + plan.Seed.Count + " (" + Fmt.SizeRu(plan.BuildBytes) + "), сейвов 2.1: " + plan.Saves +
                    ", свободно на диске: " + (plan.Free >= 0 ? Fmt.SizeRu(plan.Free) : "?") + ".", LogKind.Info);
                bool? saves = Auto ? (bool?)true : await Dialogs.ConfirmSpaceAge(this, plan, f);
                if (saves == null)
                {
                    AppendLog("Установка Paranoidal 2.1 Space Age отменена.", LogKind.Muted);
                    return;
                }
                OpToken.ThrowIfCancellationRequested();
                cancelable = false;
                btnCancel.IsEnabled = false;
                var seeded = await Task.Run(() =>
                {
                    Stage("Создаю профиль Space Age…", "Creating the Space Age profile…");
                    Progress(0, "");
                    return GameProfile.CreateSpaceAge(plan, saves.Value, (fr, d) => Progress(fr, d), l => Log("Профиль Space Age: " + l + ".", LogKind.Muted));
                });
                await FlushProgress();
                if (closed) return;
                    // the profile is there: the Space Age build is installed into it (the copied 2.1 build is its base)
                    seeding = seeded != null && seeded.Count > 0 ? seeded : null;
                    modsDir = LineMods(fac) ?? modsDir;
                    UpdateUi();
                    AppendLog("Устанавливаю сборку " + Lines.SA.Name + ", скачается только разница с её основой.", LogKind.Info);
                    await DoUpdate(false, null);
        }

        /// <summary>The window serves the other line now: its mods folder, its pin, its texts; the status check of its build.</summary>
        async Task ApplyLine(BuildLine to)
        {
            var from = Lines.Current;
            Lines.Set(to);
            Settings.Set("line", to.Id);
            modsDir = LineMods(fac) ?? Path.Combine(fac.Dir, "mods");
            seeding = null;
            pinSetting = Settings.Get(Lines.Key("pin")) == "1";
            chkPin.Checked = pinSetting;
            installed = installedDate = remote = remoteDate = null;
            found = disk = null;
            modified = -1;
            rollbackTarget = null;
            lastLoad = null;
            ShowLoad();
            if (from != to) AppendLog("Сборка переключена: " + from.Name + " → " + to.Name + ", папка модов " + modsDir + ".", LogKind.Ok);
            else AppendLog("Сборка " + to.Name + ", папка модов: " + modsDir + ".", LogKind.Info);
            lblDetail.Text = to.SpaceAge
                ? Tr.T("Профиль Space Age: свои моды, сейвы и настройки. Вернуться можно вкладкой «Paranoidal 2.1»", "The Space Age profile: its own mods, saves and settings. The \"Paranoidal 2.1\" tab switches back")
                : Tr.T("Основная сборка Paranoidal 2.1 с её сейвами и настройками", "The main Paranoidal 2.1 build with its saves and settings");
            ApplyTexts();
            DoLayout();
            UpdateUi();
            root.InvalidateVisual();
            gameRunning = FactorioInfo.IsRunning();
            gameWatch.Start();
            if (!gameRunning) await AnalyzeLoad();
            await CheckStatus();
        }

        /// <summary>
        /// «Версия хоста…» with a host of another build: between 2.1 and Space Age the player switches with the tab and pastes
        /// the line again; a host on a build for another game (2.0) must update the game and the launcher first.
        /// </summary>
        async Task OtherLineHost(BuildLine host, string hostGame)
        {
            string hostName = host != null ? host.Name : Tr.T("сборке для Factorio ", "the build for Factorio ") + hostGame;
            AppendLog("Хост играет на " + (host != null ? host.Name : "сборке для Factorio " + hostGame) + ", а у вас " + Lines.Current.Name + ": перейти на его версию здесь нельзя.", LogKind.Warn);
            string what = host == null
                ? Tr.T("Лаунчер ставит только Paranoidal 2.1 и Paranoidal 2.1 Space Age. Хосту нужно обновить игру до Factorio 2.1 и лаунчер, тогда версии можно будет сверить.",
                       "The launcher installs only Paranoidal 2.1 and Paranoidal 2.1 Space Age. The host needs to update the game to Factorio 2.1 and the launcher; then the versions can be compared.")
                : Tr.T("Переключитесь на " + host.Name + " вкладкой вверху окна и вставьте строку хоста ещё раз.",
                       "Switch to " + host.Name + " with the tab at the top of the window and paste the host's line again.");
            await DarkDialog.Info(this, Tr.T("Версия хоста", "Host's version"),
                Tr.T("Хост играет на " + hostName + ", а у вас " + Lines.Current.Name + ". ", "The host plays " + hostName + ", and you have " + Lines.Current.Name + ". ") + what,
                Gui.Orange);
        }

        async Task OpenDiscord()
        {
            try
            {
                Shell.Open(AppInfo.DiscordUrl);
                AppendLog("Открыт Discord сборки: " + AppInfo.DiscordUrl, LogKind.Muted);
            }
            catch (Exception ex)
            {
                await DarkDialog.Info(this, "Discord", Tr.T("Не удалось открыть браузер: ", "Could not open the browser: ") + ex.Message + "\n" + AppInfo.DiscordUrl, Gui.Red);
            }
        }
    }
}
