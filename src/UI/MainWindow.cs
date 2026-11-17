using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Paranoidal
{
    /// <summary>The window's background with the dark edge under the build tabs (the selected tab opens into the panes).</summary>
    sealed class MainCanvas : DrawCanvas
    {
        public Func<bool> MainShown;
        public LineTab Tab21, TabSa;

        public override void Render(DrawingContext g)
        {
            var r = new Rect(Bounds.Size);
            Gui.Fill(g, Gui.Window, r);
            if (MainShown == null || !MainShown()) return;
            Gui.Fill(g, Gui.Edge, 0, Tab21.B() - 2, r.Width, 2);
            // the selected tab covers the edge, so it joins the panes under it
            var t = TabSa.Active ? TabSa : Tab21;
            Gui.Fill(g, Gui.Pane, t.L() + 1, t.B() - 2, t.W() - 2, 2);
        }
    }

    sealed partial class MainWindow : Window, ISyncUi
    {
        /// <summary>The window while it is open (owner of the crash message).</summary>
        public static MainWindow Current;

        readonly MainCanvas root = new MainCanvas();
        readonly HeaderPanel header = new HeaderPanel();
        // panes of a vanilla window (factorio-gui-design): the build, the game, the actions, the tabbed lists
        readonly Card cardBuild = new Card("");
        readonly Card cardGame = new Card("");
        readonly Card cardAction = new Card(null);
        readonly Card cardList = new Card(null);
        readonly Lbl lblInstalled, lblRemote, lblFacPath, lblModsPath, lblModsNote, lblStage, lblDetail;
        readonly StatusLine lblStatus, lblFacVer, lblLoad;
        readonly FButton btnPickFac, btnPickMods, btnOpenMods, btnUpdate, btnPlay, btnCopy, btnVerify, btnCheck, btnCancel;
        readonly FButton btnLang, btnRollback, btnHost, btnReport, btnGuide, btnShortcuts, btnDiscord;
        readonly FCheck chkCache, chkPin, chkAsk;
        // the build tabs under the header (owner, 2026-11-10): Paranoidal 2.1 | Paranoidal 2.1 Space Age
        readonly LineTab tab21 = new LineTab(), tabSa = new LineTab();
        // the start window: the choice of the build, no suitable game, a wait (StartScreen.cs)
        readonly StartPanel start = new StartPanel();
        bool mainShown, opened, closed;
        // The one initialization task is awaited by every scenario that needs the engine.
        Task engineStartup;
        // settings of the line 2.0 of launchers 1.3.0 and 1.4.0 were reset at this start (ForgetLine20)
        static string line20Note;
        readonly FProgress progress = new FProgress();
        readonly FButton[] tabs;
        readonly Control[] pages;
        readonly FList lvCommits, lvMods;
        readonly LogBox rtbLog;
        readonly DispatcherTimer gameWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };

        GitRunner git;
        SyncEngine engine;
        FactorioInfo fac;
        string modsDir, installed, installedDate, remote, remoteDate;
        bool busy, cancelable, offline, adopted, gameRunning, pinSetting;
        int modified = -1;
        BuildFound found;
        string rollbackTarget, rollbackDate;
        bool rollbackNewer;
        List<CommitInfo> shownCommits;
        bool shownCommitsNew, shownCommitsCached;
        List<ModChange> shownMods;
        LoadVerdict lastLoad;
        // releases of the launcher and of the guide (checked together with the build, Releases.CheckAll)
        ReleaseInfo launcherRel, guideRel;
        string guideLocal, guideLocalVersion;
        bool selfUpdatePending, restarting, closeConfirmed;
        // the build on disk (its line and the game it needs) and the game version the latest build of the line needs
        BuildFound disk;
        string remoteReq;
        // tests: close after the update that follows (TEST_INSTALL)
        bool autoCloseAfterUpdate;
        // the first install of the Space Age profile: the folders of the 2.1 build copied as its base (SyncEngine.Seeded)
        HashSet<string> seeding;
        // tests (PARANOIDAL_LAUNCHER_TEST_SWITCH, TEST_INSTALL): the switch and the press of «Установить» were done
        bool testSwitched, testInstalled, testMoved;

        /// <summary>
        /// Tests only: PARANOIDAL_LAUNCHER_TEST_AUTO=&lt;journal file&gt; runs the window far off-screen without activation,
        /// answers "yes" to the self-update and the first installation, picks the build in the start window (TEST_PICK),
        /// closes on «no suitable game», skips the administrator path, appends the journal to that file on close and
        /// closes after the first status check.
        /// </summary>
        static readonly string TestAuto = Environment.GetEnvironmentVariable("PARANOIDAL_LAUNCHER_TEST_AUTO");
        static bool Auto => !string.IsNullOrWhiteSpace(TestAuto);
        /// <summary>Tests only (with TEST_AUTO): the answer of the start window, "21" or "sa" (default: the line in settings.ini).</summary>
        static readonly string TestPick = Environment.GetEnvironmentVariable("PARANOIDAL_LAUNCHER_TEST_PICK");
        /// <summary>Tests only (with TEST_AUTO): after the first status check switch the build with its tab, "sa" or "21".</summary>
        static readonly string TestSwitch = Environment.GetEnvironmentVariable("PARANOIDAL_LAUNCHER_TEST_SWITCH");
        /// <summary>
        /// Tests only (with TEST_AUTO): =1 presses «Установить»/«Обновить» after the status check (after TEST_SWITCH), the
        /// Space Age window is answered with «Установить Space Age» and the copy of the saves; the launcher closes after it.
        /// </summary>
        static readonly bool TestInstall = Environment.GetEnvironmentVariable("PARANOIDAL_LAUNCHER_TEST_INSTALL") == "1";
        /// <summary>Tests only (with TEST_AUTO): on the Space Age tab, after the status check, «Выбрать…» of the mods folder with this folder.</summary>
        static readonly string TestSaFolder = Environment.GetEnvironmentVariable("PARANOIDAL_LAUNCHER_TEST_SA_FOLDER");
        /// <summary>Tests only (with TEST_AUTO): the answer to «several installations», "none" (= «Не сейчас») or n (1-based); default 1.</summary>
        static readonly string TestChoose = Environment.GetEnvironmentVariable("PARANOIDAL_LAUNCHER_TEST_CHOOSE");
        // «Не сейчас» in the choice of installation at this start: the next start asks again (settings.ini pick-install=1)
        bool installPostponed;

        /// <summary>The client size of the window (before it is shown: the size it is created with).</summary>
        double CW => ClientSize.Width > 0 ? ClientSize.Width : Width;
        double CH => ClientSize.Height > 0 ? ClientSize.Height : Height;

        public MainWindow() : this(true) { }

        /// <summary>`live`: false for the UI pictures tool (no start sequence, no single-instance listener).</summary>
        public MainWindow(bool live)
        {
            Current = this;
            ForgetLine20();
            Title = AppInfo.Name + " " + AppInfo.Version;
            var icon = Res.AppIcon();
            if (icon != null) Icon = icon;
            Background = Gui.Brush(Gui.Window);
            Content = root;
            root.MainShown = () => mainShown;
            root.Tab21 = tab21;
            root.TabSa = tabSa;
            ShowActivated = !Auto;
            WindowStartupLocation = Auto ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen;
            if (Auto) Position = new PixelPoint(-32000, -32000);
            Width = 1000;
            Height = 800;
            MinWidth = 900;
            MinHeight = 740;

            root.Children.Add(start);
            root.Children.Add(header);
            root.Children.Add(tab21);
            root.Children.Add(tabSa);
            root.Children.Add(cardBuild);
            root.Children.Add(cardGame);
            root.Children.Add(cardAction);
            root.Children.Add(cardList);
            btnLang = B(header, "", ButtonKind.Normal, () => Tr.Set(!Tr.En));
            btnGuide = B(header, "", ButtonKind.Normal, OpenGuide);
            btnShortcuts = B(header, "", ButtonKind.Normal, MakeShortcuts);
            btnDiscord = B(header, "Discord", ButtonKind.Normal, OpenDiscord);
            btnDiscord.Icon = DiscordIcon.Create(18);
            btnDiscord.IconSize = new Size(18, 18);
            if (Auto) DarkDialog.Headless = t => AppendLog("[тест: диалог без показа, ответ «да»] " + t, LogKind.Muted);

            // the build tabs and «Спрашивать сборку при запуске» at their right
            tab21.Text = Lines.L21.Name;
            tabSa.Text = Lines.SA.Name;
            tab21.Click += async () =>
            {
                try { await RunOperation(() => SwitchLine(Lines.L21)); }
                catch (Exception ex) { Program.Crash(ex, true); }
            };
            tabSa.Click += async () =>
            {
                try { await RunOperation(() => SwitchLine(Lines.SA)); }
                catch (Exception ex) { Program.Crash(ex, true); }
            };
            chkAsk = Check(root, Settings.Get("ask-build") != "0");
            chkAsk.CheckedChanged += () =>
            {
                Settings.Set("ask-build", chkAsk.Checked ? "1" : "0");
                AppendLog(chkAsk.Checked ? "При запуске лаунчер спросит, какую сборку открыть."
                    : "При запуске лаунчер сразу откроет последнюю сборку: " + Lines.Current.Name + ".", LogKind.Info);
            };

            // the start window
            start.BtnLang.Click += () => Tr.Set(!Tr.En);
            start.Card21.Click += async () =>
            {
                try { await RunOperation(() => PickBuild(Lines.L21)); }
                catch (Exception ex) { Program.Crash(ex, true); }
            };
            start.CardSa.Click += async () =>
            {
                try { await RunOperation(() => PickBuild(Lines.SA)); }
                catch (Exception ex) { Program.Crash(ex, true); }
            };
            start.BtnClose.Click += () => { AppendLog("Подходящей Factorio нет, лаунчер закрыт кнопкой «Закрыть».", LogKind.Muted); Close(); };
            start.BtnPick.Click += async () =>
            {
                try { await RunOperation(PickGameForStart); }
                catch (Exception ex) { Program.Crash(ex, true); }
            };
            start.BtnRecheck.Click += async () =>
            {
                try
                {
                    await RunOperation(async () =>
                    {
                        AppendLog("«Проверить снова»: ищу Factorio ещё раз.", LogKind.Info);
                        await DetectGame();
                    });
                }
                catch (Exception ex) { Program.Crash(ex, true); }
            };

            lblInstalled = L(cardBuild, Gui.Text);
            lblRemote = L(cardBuild, Gui.Text);
            lblStatus = Status(cardBuild, Gui.Heading);
            pinSetting = Settings.Get(Lines.Key("pin")) == "1";
            chkPin = Check(cardBuild, pinSetting);
            chkPin.CheckedChanged += () =>
            {
                if (chkPin.Checked == pinSetting) return;
                pinSetting = chkPin.Checked;
                Settings.Set(Lines.Key("pin"), pinSetting ? "1" : "0");
                AppendLog(pinSetting ? "Версия сборки закреплена: лаунчер не обновляет сборку и не предлагает обновление."
                    : "Версия сборки больше не закреплена.", LogKind.Info);
                UpdateUi();
            };
            btnRollback = B(cardBuild, "", ButtonKind.Normal, DoRollback);
            btnHost = B(cardBuild, "", ButtonKind.Normal, DoHost);

            lblFacPath = L(cardGame, Gui.Text);
            lblFacVer = Status(cardGame, Gui.Bold);
            lblModsPath = L(cardGame, Gui.Text);
            lblModsNote = L(cardGame, Gui.Muted);
            lblLoad = Status(cardGame, Gui.Body);
            btnPickFac = B(cardGame, "", ButtonKind.Normal, PickFactorio);
            btnPickMods = B(cardGame, "", ButtonKind.Normal, PickMods);
            btnOpenMods = B(cardGame, "", ButtonKind.Normal, OpenMods);
            chkCache = Check(cardGame, Settings.Get("game-cache") != "0");
            chkCache.CheckedChanged += () =>
            {
                Settings.Set("game-cache", chkCache.Checked ? "1" : "0");
                AppendLog(chkCache.Checked ? "Кэш загрузки игры будет включаться перед каждым запуском."
                    : "Кэш загрузки игры лаунчер больше не включает (config.ini игры не меняется).", LogKind.Info);
            };

            btnUpdate = B(cardAction, "", ButtonKind.Normal, InstallOrUpdate, true);
            btnPlay = B(cardAction, "", ButtonKind.Confirm, Play, true);
            btnCopy = B(cardAction, "", ButtonKind.Normal, CopyCommit);
            btnVerify = B(cardAction, "", ButtonKind.Normal, DoVerify);
            btnCheck = B(cardAction, "", ButtonKind.Normal, CheckStatus);
            btnReport = B(root, "", ButtonKind.Normal, DoReport);
            btnCancel = B(cardAction, "", ButtonKind.Normal, (Action)CancelClicked);
            async void CancelClicked()
            {
                try { await CancelOp(); }
                catch (Exception ex) { Program.Crash(ex, true); }
            }
            btnCancel.IsVisible = false;

            lblStage = L(cardAction, Gui.Title, Gui.Bold);
            lblDetail = L(cardAction, Gui.Muted);
            cardAction.Children.Add(progress);

            lvCommits = MakeList(new double[] { 140, 600 }, 1);
            lvMods = MakeList(new double[] { 360, 220, 100 }, 0);
            rtbLog = new LogBox(Gui.Code);
            cardList.Children.Add(rtbLog);

            pages = new Control[] { lvCommits, lvMods, rtbLog };
            tabs = new[] { new FButton("", ButtonKind.Tab), new FButton("", ButtonKind.Tab), new FButton("", ButtonKind.Tab) };
            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                tabs[i].Click += () => SelectTab(idx);
                root.Children.Add(tabs[i]);
            }
            SelectTab(0);

            gameWatch.Tick += async (s, e) =>
            {
                try { if (!busy) await WatchGame(); }
                catch (Exception ex) { Program.Crash(ex, true); }
            };
            Tr.Changed += () =>
            {
                ApplyTexts();
                UpdateUi();
                DoLayout();
                // the line of the last operation is in the old language: show the state instead
                if (!busy && engine != null && mainShown) { Idle(StatusIdleText()); lblDetail.Text = ""; }
                RefreshStartTexts();
            };
            root.SizeChanged += (s, e) => DoLayout();
            if (live)
            {
                SingleInstance.Listen(() =>
                {
                    // a second start of the launcher (Program.cs) asks this one to show its window
                    AppendLog("Лаунчер запущен ещё раз, показываю это окно.", LogKind.Muted);
                    if (!Auto) { try { BringUp(); } catch { } }
                });
                Opened += async (s, e) =>
                {
                    try { opened = true; await RunOperation(Startup); }
                    catch (Exception ex) { Program.Crash(ex, true); }
                };
            }
            else Opened += (s, e) => opened = true;
            ApplyTexts();
            SetBusy(false);
            Idle(Tr.T("Запуск…", "Starting…"));
            // the remembered build with the game of settings.ini: straight to the main window; otherwise the start window
            // (the game search, then the choice of the build)
            var quick = Remembered();
            var qfac = quick != null ? FactorioInfo.Inspect(Settings.GetPath("factorio")) : null;
            if (quick != null && qfac != null && qfac.Ok && (!quick.SpaceAge || GameProfile.SpaceAgeExists(qfac)))
            {
                Lines.Set(quick);
                SetScreen(null);
            }
            else
            {
                start.SetBusy(Tr.T("Ищу Factorio…", "Looking for Factorio…"), "");
                SetScreen(StartPanel.View.Busy);
            }
            UpdateUi();
        }

        /// <summary>A second start of the launcher: the window is restored if minimised and comes to the front.</summary>
        void BringUp()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Topmost = true;
            Topmost = false;
            Activate();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == WindowStateProperty) LogoArt.Paused = WindowState == WindowState.Minimized || gameRunning;
        }

        /// <summary>
        /// settings.ini of launchers 1.3.0 and 1.4.0 can say line=2.0: the launcher serves only 2.1 and Space Age now, so
        /// what belonged to that line goes (the pin; the rollback goes with the update, SyncEngine crossLine). Before
        /// anything reads the line or the pin.
        /// </summary>
        static void ForgetLine20()
        {
            if (Settings.Get("line") != "2.0" && Settings.Get("migrating") == null) return;
            if (Settings.Get("line") == "2.0")
            {
                Settings.Set("pin", "0");
                Settings.Set("line", Lines.L21.Id);
                line20Note = "В настройках была линия сборки 2.0: лаунчер теперь ставит только Paranoidal 2.1 и Space Age, «Не менять версию» сброшено.";
            }
            Settings.Set("migrating", "");
        }

        /// <summary>The build the player chose to open at the start («Запомнить выбор»); null: the start window asks.</summary>
        static BuildLine Remembered() => Settings.Get("ask-build") == "0" ? Lines.ById(Settings.Get("line")) : null;

        /// <summary>
        /// The start window (a view) or the main window (null): the controls of the other one are hidden, the size changes
        /// (the start window is small and fixed, the main one resizable), the window stays centred.
        /// </summary>
        void SetScreen(StartPanel.View? view)
        {
            bool main = view == null;
            bool sizeChange = main != mainShown || !opened;
            mainShown = main;
            start.IsVisible = !main;
            foreach (Control c in new Control[] { header, tab21, tabSa, chkAsk, cardBuild, cardGame, cardAction, cardList, btnReport })
                c.IsVisible = main;
            foreach (var t in tabs) t.IsVisible = main;
            if (!main) start.Show(view.Value);
            double oldW = CW, oldH = CH;
            var oldPos = Position;
            if (main)
            {
                if (sizeChange)
                {
                    MaxWidth = MaxHeight = double.PositiveInfinity;
                    MinWidth = MinHeight = 0;
                    CanResize = true;
                    Width = 1000;
                    Height = 800;
                    MinWidth = 900;
                    MinHeight = 760;
                }
            }
            else
            {
                double w = 860, h = start.PreferredHeight(860);
                if (w != CW || h != CH || sizeChange)
                {
                    MaxWidth = MaxHeight = double.PositiveInfinity;
                    MinWidth = MinHeight = 0;
                    if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
                    CanResize = false;
                    Width = w;
                    Height = h;
                    MinWidth = MaxWidth = w;
                    MinHeight = MaxHeight = h;
                }
            }
            // keep the window centred where it was (not before it is shown: the startup location centres it then)
            if (opened && IsVisible && !Auto && (Width != oldW || Height != oldH) && WindowState == WindowState.Normal)
            {
                double k = DesktopScaling;
                int x = oldPos.X + (int)Math.Round((oldW - Width) * k / 2), y = oldPos.Y + (int)Math.Round((oldH - Height) * k / 2);
                var screen = Screens.ScreenFromWindow(this);
                if (screen != null)
                {
                    var wa = screen.WorkingArea;
                    int pw = (int)Math.Round(Width * k), ph = (int)Math.Round(Height * k);
                    x = Math.Max(wa.X, Math.Min(x, wa.Right - pw));
                    y = Math.Max(wa.Y, Math.Min(y, wa.Bottom - ph));
                }
                Position = new PixelPoint(x, y);
            }
            DoLayout();
            root.InvalidateVisual();
        }

        // ------------------------------------------------------------------ building blocks

        static Lbl L(DrawCanvas parent, Color fore, FFont font = null)
        {
            var l = new Lbl { Color = fore };
            if (font != null) l.Font = font;
            parent.Children.Add(l);
            return l;
        }

        static FButton B(DrawCanvas parent, string text, ButtonKind kind, Action click, bool big = false)
        {
            var b = new FButton(text, kind, big);
            b.Click += click;
            parent.Children.Add(b);
            return b;
        }

        /// <summary>The event awaits the complete scenario and handles unexpected errors on the UI thread.</summary>
        FButton B(DrawCanvas parent, string text, ButtonKind kind, Func<Task> click, bool big = false)
        {
            return B(parent, text, kind, (Action)Clicked, big);
            async void Clicked()
            {
                try { await RunOperation(click); }
                catch (Exception ex) { Program.Crash(ex, true); }
            }
        }

        static StatusLine Status(DrawCanvas parent, FFont font)
        {
            var s = new StatusLine(font);
            parent.Children.Add(s);
            return s;
        }

        static FCheck Check(DrawCanvas parent, bool value)
        {
            var c = new FCheck();
            c.Checked = value;
            parent.Children.Add(c);
            return c;
        }

        FList MakeList(double[] widths, int fillCol)
        {
            var lv = new FList(widths, fillCol);
            cardList.Children.Add(lv);
            return lv;
        }

        void SelectTab(int idx)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].Checked = i == idx;
                pages[i].IsVisible = i == idx;
            }
            cardList.InvalidateVisual();
        }

        /// <summary>Every fixed text of the window in the interface language (called again when it changes).</summary>
        void ApplyTexts()
        {
            var line = Lines.Current;
            cardBuild.Title = Tr.T("Сборка " + line.Name, line.Name + " build");
            cardGame.Title = Tr.T("Factorio и папка модов", "Factorio and the mods folder");
            cardBuild.InvalidateVisual();
            cardGame.InvalidateVisual();
            btnLang.Text = Tr.T("Язык: RU", "Language: EN");
            Tips.Set(btnLang, Tr.T("Switch to English", "Переключить на русский"));
            btnGuide.Text = Tr.T("Гайд", "Guide");
            btnShortcuts.Text = Tr.T("Ярлыки", "Shortcuts");
            Tips.Set(btnDiscord, Tr.T("Discord сборки Factorio Paranoidal: вопросы, баги, отчёты о проблемах, новости и совместная игра.\n",
                "The Factorio Paranoidal Discord: questions, bugs, problem reports, news and co-op games.\n") + AppInfo.DiscordUrl);
            // the build tabs (the Space Age one is labelled "in active development", never "may break")
            header.SpaceAge = line.SpaceAge;
            tabSa.Note = Tr.T("в активной разработке", "in active development");
            tab21.Active = !line.SpaceAge;
            tabSa.Active = line.SpaceAge;
            Tips.Set(tab21, line.SpaceAge
                ? Tr.T("Перейти на основную сборку Paranoidal 2.1 с её сейвами и настройками. Профиль Space Age останется как есть.\n" +
                       "Сейв Space Age в обычной 2.1 не откроется.",
                       "Switch to the main Paranoidal 2.1 build with its saves and settings. The Space Age profile stays as it is.\n" +
                       "A Space Age save does not load in the plain 2.1 build.")
                : null);
            Tips.Set(tabSa, line.SpaceAge ? null
                : Tr.T("Сборка Paranoidal с дополнением Space Age, она в активной разработке.\n" +
                       "У неё отдельный профиль со своими модами, сейвами и настройками, основная сборка 2.1 не меняется.\n" +
                       "Вернуться можно вкладкой «Paranoidal 2.1».",
                       "The Paranoidal build with the Space Age expansion, in active development.\n" +
                       "It has a profile of its own with its own mods, saves and settings; the main 2.1 build does not change.\n" +
                       "The \"Paranoidal 2.1\" tab switches back."));
            chkAsk.Text = Tr.T("Спрашивать сборку при запуске", "Ask for the build at start");
            Tips.Set(chkAsk, Tr.T("С галочкой лаунчер при запуске покажет окно выбора сборки.\nБез неё сразу откроет сборку, с которой вы работали в прошлый раз.",
                "Checked: the launcher shows the choice of the build at start.\nCleared: it opens the build you used last time right away."));
            header.InvalidateVisual();
            Tips.Set(btnShortcuts, Tr.T(
                "Создать ярлыки лаунчера на рабочем столе и в меню «Пуск» (папка Paranoidal, там же ярлык гайда).\n" +
                "Сам лаунчер остаётся в папке игры.",
                "Create launcher shortcuts on the desktop and in the Start menu (the Paranoidal folder, with a shortcut to the guide).\n" +
                "The launcher itself stays in the game folder."));
            ShowGuideState();
            btnPickFac.Text = Tr.T("Выбрать…", "Choose…");
            btnPickMods.Text = Tr.T("Выбрать…", "Choose…");
            btnOpenMods.Text = Tr.T("Открыть", "Open");
            chkCache.Text = Tr.T("Кэш загрузки игры (быстрый запуск)", "Game load cache (fast start)");
            Tips.Set(chkCache, Tr.T(
                "Перед каждым запуском игры лаунчер ставит в её config.ini\n" +
                "cache-prototype-data, cache-sprite-atlas и compress-sprite-atlas-cache = true.\n" +
                "Первые два запуска после обновления сборки долгие, пока строится кэш.\n" +
                "Потом игра грузится до меню примерно за 25 секунд, а не за полторы-две минуты.\n" +
                "Другие настройки config.ini лаунчер не трогает, копия до первой правки лежит в config.ini.paranoidal-backup.\n" +
                "Если снять галочку, лаунчер перестанет менять config.ini, а уже включённый кэш останется.",
                "Before every game start the launcher sets\n" +
                "cache-prototype-data, cache-sprite-atlas and compress-sprite-atlas-cache = true in the game's config.ini.\n" +
                "The first two starts after a build update are long while the cache is built.\n" +
                "After that the game loads to the menu in about 25 seconds instead of one and a half to two minutes.\n" +
                "The launcher leaves the other settings of config.ini alone; the copy before its first change is config.ini.paranoidal-backup.\n" +
                "With the box cleared the launcher stops changing config.ini, and a cache that is already on stays on."));
            chkPin.Text = Tr.T("Не менять версию", "Keep this version");
            Tips.Set(chkPin, Tr.T(
                "Закрепить установленную версию сборки: лаунчер не будет её обновлять и не предложит обновление перед игрой.\n" +
                "Когда это нужно:\n" +
                "• хост сервера ещё не обновился, а версии сборки у всех игроков должны совпадать байт в байт;\n" +
                "• обновление сломало сейв или прохождение, и вы ждёте исправления;\n" +
                "• идёт совместная игра или серия, и все остаются на одной версии до конца;\n" +
                "• интернет мобильный или лимитный;\n" +
                "• идёт запись или стрим, и изменения посреди сессии не нужны;\n" +
                "• нужно воспроизвести баг на конкретной версии для отчёта.\n" +
                "«Проверить файлы» продолжает работать и сверяет файлы с закреплённой версией.",
                "Keep the installed build version: the launcher stops updating it and does not offer the update before playing.\n" +
                "When it helps:\n" +
                "• the server's host has not updated yet, and every player needs the same build, byte for byte;\n" +
                "• an update broke a save or the playthrough, and you wait for the fix;\n" +
                "• a co-op game or a series where everybody stays on one version to the end;\n" +
                "• mobile or metered internet;\n" +
                "• recording or streaming, when changes in the middle of a session are unwelcome;\n" +
                "• reproducing a bug on a specific version for a report.\n" +
                "\"Check files\" keeps working and checks the files against the kept version."));
            btnHost.Text = Tr.T("Версия хоста…", "Host's version…");
            Tips.Set(btnHost, Tr.T(
                "Перейти ровно на версию сборки хоста по его строке «Скопировать версию».\n" +
                "Нужен интернет, скачается только разница. После перехода включится «Не менять версию».",
                "Switch to exactly the host's build version by the host's \"Copy version\" line.\n" +
                "It needs internet and downloads only the difference. \"Keep this version\" is turned on afterwards."));
            btnPlay.Text = Tr.T("Играть", "Play");
            btnCopy.Text = Tr.T("Скопировать версию", "Copy version");
            btnVerify.Text = Tr.T("Проверить файлы", "Check files");
            btnCheck.Text = Tr.T("Проверить обновления", "Check for updates");
            btnReport.Text = Tr.T("Отчёт о проблеме", "Problem report");
            btnCancel.Text = Tr.T("Отмена", "Cancel");
            Tips.Set(btnCopy, Tr.T(
                "Скопировать строку для сверки с хостом и для сообщений о багах:\n" +
                "версия сборки, отпечаток стартовых настроек модов, включённые моды не из сборки, версия лаунчера.\n" +
                "Если игра пишет «Содержимое мода отличается», строки у хоста и игрока должны совпасть.\n" +
                "Эту же строку игрок вставляет в «Версия хоста…», чтобы перейти на версию хоста.",
                "Copy the line to compare with the host and to put into bug reports:\n" +
                "the build version, a fingerprint of the startup mod settings, enabled mods not from the build, the launcher version.\n" +
                "If the game says \"mod content differs\", the host's and the player's lines must be equal.\n" +
                "A player pastes the same line into \"Host's version…\" to switch to the host's version."));
            Tips.Set(btnVerify, Tr.T(
                "Заново прочитать все файлы установленной сборки, побайтно сравнить их с GitHub\n" +
                "и скачать отличающиеся. Версия сборки не меняется. Помогает при ошибке\n" +
                "«Содержимое мода отличается», если файлы модов копировали или правили вручную.\n" +
                "Без интернета работает, пока все файлы целы.",
                "Read every file of the installed build again, compare it with GitHub byte for byte\n" +
                "and download the ones that differ. The build version stays the same. It helps with\n" +
                "\"mod content differs\" when mod files were copied or edited by hand.\n" +
                "It works offline as long as no file is broken."));
            Tips.Set(btnCheck, Tr.T("Сравнить установленную версию сборки с последней на GitHub. Файлы не меняются.",
                "Compare the installed build version with the latest one on GitHub. The files stay as they are."));
            Tips.Set(btnReport, Tr.T(
                "Собрать на рабочем столе ZIP для Discord сборки: логи игры, строку версии, mod-list.json,\n" +
                "mod-settings.dat, журналы лаунчера, диагностику загрузки и, если нужно, сейв (всего до 20 МБ).",
                "Put a ZIP for the modpack Discord on the desktop: game logs, the version line, mod-list.json,\n" +
                "mod-settings.dat, launcher logs, the load diagnostics and a save if you want (20 MB in total)."));
            var dlcOn = string.Join(", ", line.DlcStates.Where(d => d.Value).Select(d => d.Key));
            var dlcOff = string.Join(", ", line.DlcStates.Where(d => !d.Value).Select(d => d.Key));
            Tips.Set(lblModsNote, Tr.T(
                "Перед обновлением и запуском игры лаунчер ставит в mod-list.json: " + dlcOn + " включены" +
                (dlcOff.Length > 0 ? ", " + dlcOff + " выключен" : "") + ", как на сервере.\n" +
                "Другие строки mod-list.json и настройки модов (mod-settings.dat) он не трогает.",
                "Before an update and a game start the launcher sets in mod-list.json: " + dlcOn + " on" +
                (dlcOff.Length > 0 ? ", " + dlcOff + " off" : "") + ", as on the server.\n" +
                "It leaves the other lines of mod-list.json and the mod settings (mod-settings.dat) alone."));
            lvCommits.SetTitle(0, Tr.T("Дата", "Date"));
            lvCommits.SetTitle(1, Tr.T("Что изменилось", "What changed"));
            lvMods.SetTitle(0, Tr.T("Мод", "Mod"));
            lvMods.SetTitle(1, Tr.T("Что произошло", "What happened"));
            lvMods.SetTitle(2, Tr.T("Файлов", "Files"));
            tabs[2].Text = Tr.T("Журнал", "Journal");
            Tips.Set(tabs[2], Tr.T("Журнал всегда на русском: его читает команда сборки.", "The journal is always in Russian: the modpack team reads it."));
            FillCommits(shownCommits, shownCommitsNew, shownCommitsCached);
            FillMods(shownMods);
            ShowLoad();
        }

        /// <summary>
        /// The window on the 4/8/12 px grid: the header, two panes (build, game), the action pane, the tabs over the list
        /// pane; 12 px between panes and inside them.
        /// </summary>
        void DoLayout()
        {
            double W = root.Bounds.Width > 0 ? root.Bounds.Width : CW, H = root.Bounds.Height > 0 ? root.Bounds.Height : CH, m = 12, pad = 12;
            if (W < 10) return;
            if (!mainShown)
            {
                start.Place(0, 0, W, H);
                start.DoLayout();
                return;
            }
            header.Place(0, 0, W, 132);
            // left column of the header: language, guide, shortcuts (the logo is centred, ~150 px from the edge at the minimal width)
            double hw = Math.Max(96, new[] { btnLang, btnGuide, btnShortcuts }.Max(b => Gui.TextWidth(b.Text, b.Font)) + 24);
            btnLang.Place(m, 12, hw, 28);
            btnGuide.Place(m, 48, hw, 28);
            btnShortcuts.Place(m, 84, hw, 28);
            // right column, between the version line and "for Factorio …": Discord
            double rw = Gui.TextWidth(btnDiscord.Text, btnDiscord.Font) + btnDiscord.IconSize.Width + 32;
            btnDiscord.Place(W - m - rw, 26, rw, 28);
            // the logo stays clear of both columns and of the version lines at the right
            header.LeftReserve = m + hw;
            header.RightReserve = m + Math.Max(rw, Math.Max(Gui.TextWidth(AppInfo.Name + " " + AppInfo.Version, Gui.Small),
                Gui.TextWidth(Tr.T("для ", "for ") + AppInfo.RequiredGame, Gui.Small)));
            header.InvalidateVisual();

            // the build tabs under the stripe, «Спрашивать сборку при запуске» at their right
            double ty = header.B() + 8, tH = 36;
            tab21.Place(m, ty, Math.Max(150, tab21.PreferredWidth), tH);
            tabSa.Place(tab21.R() + 4, ty, tabSa.PreferredWidth, tH);
            double aw0 = chkAsk.PreferredWidth + 6;
            chkAsk.Place(W - m - aw0, ty + (tH - 24) / 2, aw0, 24);

            double y = ty + tH + m;
            double cw = Math.Floor((W - 3 * m) * 45 / 100), ch = 228;
            cardBuild.Place(m, y, cw, ch);
            cardGame.Place(2 * m + cw, y, W - 3 * m - cw, ch);
            double top = cardBuild.Band + pad, iw = cw - 2 * pad;
            lblInstalled.Place(pad, top, iw, 24);
            lblRemote.Place(pad, top + 24, iw, 24);
            lblStatus.Place(pad, top + 64, iw, 32);
            double bh = 28, by = ch - pad - bh;
            double wHost = Gui.TextWidth(btnHost.Text, btnHost.Font) + 28;
            double wRb = Math.Max(96, Gui.TextWidth(btnRollback.Text, btnRollback.Font) + 28);
            // the buttons of the version on the bottom row (right), the pin on its own row above it
            btnHost.Place(cw - pad - wHost, by, wHost, bh);
            btnRollback.Place(btnHost.L() - 8 - wRb, by, wRb, bh);
            chkPin.Place(pad, by - 32, iw, 24);

            double gw = cardGame.W(), bw = 104, lw = gw - 2 * pad - bw - 8;
            btnPickFac.Place(gw - pad - bw, top, bw, 28);
            lblFacPath.Place(pad, top, lw, 28);
            lblFacVer.Place(pad, top + 28, lw, 24);
            btnPickMods.Place(gw - pad - bw, top + 60, bw, 28);
            lblModsPath.Place(pad, top + 60, lw, 28);
            btnOpenMods.Place(gw - pad - bw, top + 92, bw, 28);
            lblModsNote.Place(pad, top + 92, lw, 28);
            chkCache.Place(pad, top + 124, gw - 2 * pad, 24);
            lblLoad.Place(pad, top + 148, gw - 2 * pad, 24);

            // the action pane: the two main buttons, the other actions, then the stage, the progress and its detail
            y = cardBuild.B() + m;
            double aw = W - 2 * m, ah = 40, sh = 32, gap = 8;
            Func<FButton, double, double> wid = (b, min) => Math.Max(min, Gui.TextWidth(b.Text, b.Font) + 32);
            double wCopy = wid(btnCopy, 150), wVerify = wid(btnVerify, 130), wCheck = wid(btnCheck, 160);
            // the two main buttons give up width first, so the row fits the minimal window
            double pw = Math.Max(130, Math.Min(220, Math.Floor((aw - 2 * pad - wCopy - wVerify - wCheck - 5 * gap) / 2)));
            double ay = pad;
            btnUpdate.Place(pad, ay, pw, ah);
            btnPlay.Place(btnUpdate.R() + gap, ay, pw, ah);
            btnCopy.Place(btnPlay.R() + 2 * gap, ay + (ah - sh) / 2, wCopy, sh);
            btnVerify.Place(btnCopy.R() + gap, ay + (ah - sh) / 2, wVerify, sh);
            btnCheck.Place(aw - pad - wCheck, ay + (ah - sh) / 2, wCheck, sh);
            ay += ah + pad;
            lblStage.Place(pad, ay, aw - 2 * pad, 22);
            ay += 24;
            btnCancel.Place(aw - pad - 110, ay, 110, 28);
            progress.Place(pad, ay, (btnCancel.IsVisible ? btnCancel.L() - gap : aw - pad) - pad, 28);
            ay += 32;
            lblDetail.Place(pad, ay, aw - 2 * pad, 22);
            ay += 22 + pad;
            cardAction.Place(m, y, aw, ay);

            // tabs on top of the list pane, the report button at their right
            y = cardAction.B() + m;
            double tw = 190, th = 32;
            for (int i = 0; i < tabs.Length; i++) tabs[i].Place(m + i * (tw + 4), y, tw, th);
            double wRep = Math.Max(170, Gui.TextWidth(btnReport.Text, btnReport.Font) + 32);
            btnReport.Place(W - m - wRep, y, wRep, 28);
            y += th;
            cardList.Place(m, y, aw, Math.Max(80, H - y - m));
            double lp = 8;
            foreach (var p in pages) p.Place(lp + 1, lp + 1, cardList.W() - 2 * lp - 2, cardList.H() - 2 * lp - 2);
            cardList.InvalidateVisual();
            root.InvalidateVisual();
        }

        // ------------------------------------------------------------------ state → UI

        bool Pinned => pinSetting && installed != null;

        /// <summary>The build in mods\ is for another game (a 2.0 build in a game updated to 2.1): it must be updated before playing.</summary>
        bool OtherLineBuild => disk != null && disk.OtherLine;

        /// <summary>A Paranoidal build of this line is in mods\ even if its version is unknown: the game can be started.</summary>
        bool BuildPresent => !OtherLineBuild && (installed != null || (found != null && found.Present && !found.Legacy));

        /// <summary>"Quality, Elevated Rails, Recycler" of the line that are on (off), for the mods pane.</summary>
        static string DlcText(bool on)
        {
            Func<string, string> title = n => n == "elevated-rails" ? "Elevated Rails" : n == "space-age" ? "Space Age" : char.ToUpperInvariant(n[0]) + n.Substring(1);
            return string.Join(", ", Lines.Current.DlcStates.Where(d => d.Value == on).Select(d => title(d.Key)));
        }

        /// <summary>The player should update (install, switch the line, a new version or guide): the update button is the green one.</summary>
        bool UpdateWanted => modsDir != null && !Pinned &&
            (installed == null || OtherLineBuild || modified > 0 || (remote != null && installed != remote) || (installed == remote && GuideUpdate));

        void UpdateUi()
        {
            if (installed != null)
                lblInstalled.Text = Tr.T("Установлена версия:  ", "Installed version:  ") + (installedDate != null ? Tr.T("от ", "of ") + installedDate : Tr.T("есть", "yes"));
            else if (found != null && found.Present && (found.Legacy || found.OtherLine))
                lblInstalled.Text = Tr.T(found.Legacy ? "Установлена версия:  старая, для Factorio " : "Установлена версия:  для Factorio ",
                    found.Legacy ? "Installed version:  old, for Factorio " : "Installed version:  for Factorio ") + found.FactorioVersion +
                    (found.ZzzVersion != null ? " (zzzparanoidal " + found.ZzzVersion + ")" : "");
            else if (found != null && found.Present && found.Near != null)
                lblInstalled.Text = Tr.T("Установлена версия:  похожа на версию от ", "Installed version:  close to the build of ") + found.NearDate +
                    Tr.T(" (отличаются модов: ", " (mods differ: ") + found.NearDiff + Tr.T(" из ", " of ") + found.NearTotal + ")";
            else if (found != null && found.Present)
                lblInstalled.Text = Tr.T("Установлена версия:  не определена", "Installed version:  unknown") +
                    (found.Fingerprint != null ? Tr.T(", отпечаток ", ", fingerprint ") + found.Fingerprint : "");
            else
                lblInstalled.Text = Tr.T("Установлена версия:  нет", "Installed version:  none");

            lblRemote.Text = Tr.T("Последняя версия:  ", "Latest version:  ") + (remote == null
                ? (offline ? Tr.T("нет связи с GitHub", "no connection to GitHub") : Tr.T("не проверено", "not checked"))
                : remoteDate != null ? Tr.T("от ", "of ") + remoteDate : installed == remote && installedDate != null ? Tr.T("от ", "of ") + installedDate : Tr.T("есть", "yes"));

            string statusTip = null;
            if (modsDir == null)
                Status(Tr.T("Укажите папку Factorio", "Choose the Factorio folder"), Gui.Orange);
            else if (OtherLineBuild)
            {
                Status(Tr.T("Нужно обновить сборку", "The build needs an update"), Gui.Orange);
                statusTip = Tr.T("В папке mods сборка Paranoidal для Factorio " + disk.FactorioVersion + ", на этой игре она не запустится.\n«" +
                    (installed == null ? "Установить" : "Обновить") + "» переведёт её на " + Lines.Current.Name + ", скачается только разница.",
                    "The mods folder has the Paranoidal build for Factorio " + disk.FactorioVersion + ", which does not run on this game.\n\"" +
                    (installed == null ? "Install" : "Update") + "\" switches it to " + Lines.Current.Name + " and downloads only the difference.");
            }
            else if (installed == null)
            {
                if (found == null || !found.Present) Status(Tr.T("Сборка ещё не установлена", "The build is not installed yet"), Gui.Orange);
                else if (found.Legacy) Status(Tr.T("В папке mods старая сборка для Factorio 1.1", "The mods folder has the old build for Factorio 1.1"), Gui.Red);
                else if (found.Near != null)
                {
                    Status(Tr.T("Сборка Paranoidal с изменениями", "A Paranoidal build with changes"), Gui.Orange);
                    statusTip = Tr.T("Отличаются моды: ", "Mods that differ: ") + string.Join(", ", found.NearMods.Take(30)) + (found.NearMods.Count > 30 ? " …" : "");
                }
                else Status(Tr.T("Сборка найдена, но версия не определена", "A build is there, its version is unknown"), Gui.Orange);
            }
            else if (modified > 0)
            {
                Status(Tr.T("Изменено файлов сборки: ", "Build files changed: ") + modified, Gui.Orange);
                statusTip = Tr.T("Файлы модов сборки в папке mods отличаются от установленной версии.\n«Проверить файлы» вернёт их, для этого нужен интернет.",
                    "Files of the build's mods differ from the installed version.\n\"Check files\" restores them; that needs internet.");
            }
            else if (remote == null)
                Status(offline ? Tr.T("Нет связи с GitHub", "No connection to GitHub") : Tr.T("Обновления не проверены", "Updates not checked"), Gui.Muted);
            else if (installed == remote && GuideUpdate && !Pinned)
            {
                Status(Tr.T("Вышла новая версия гайда", "A new guide version is out"), Gui.Orange);
                statusTip = GuideUpdateText();
            }
            else if (installed == remote)
                Status(Tr.T("Установлена последняя версия", "The latest version is installed"), Gui.Green);
            else if (Pinned)
                Status(Tr.T("Версия закреплена, есть обновление", "Version kept, an update exists"), Gui.Muted);
            else
                Status(Tr.T("Доступно обновление", "An update is available"), Gui.Orange);
            Tips.Set(lblStatus, statusTip);

            btnUpdate.Text = installed == null ? Tr.T("Установить", "Install") : Tr.T("Обновить", "Update");
            Tips.Set(btnUpdate, Pinned ? Tr.T("Версия закреплена. Чтобы обновиться, снимите «Не менять версию».", "The version is kept. Clear \"Keep this version\" to update.") : null);
            // the one green button is the next thing to do
            bool wanted = UpdateWanted;
            btnUpdate.Kind = wanted ? ButtonKind.Confirm : ButtonKind.Normal;
            btnPlay.Kind = wanted ? ButtonKind.Normal : ButtonKind.Confirm;

            if (fac == null)
            {
                lblFacPath.Text = Tr.T("Factorio не найдена, нажмите «Выбрать…»", "Factorio not found: press \"Choose…\"");
                lblFacPath.Color = Gui.Orange;
                lblFacVer.Text = Tr.T("Нужна ", "Needs ") + AppInfo.RequiredGame;
                lblFacVer.Color = Gui.Muted;
            }
            else
            {
                lblFacPath.Text = fac.Dir;
                lblFacPath.Color = Gui.Text;
                lblFacVer.Text = fac.Ok ? fac.Describe() : fac.Describe() + Tr.T(", нужна ", ", needs ") + AppInfo.RequiredGame;
                lblFacVer.Color = fac.Ok ? Gui.Green : Gui.Red;
            }
            lblModsPath.Text = modsDir == null ? Tr.T("Папка модов не выбрана", "No mods folder selected") : Tr.T("Моды: ", "Mods: ") + modsDir +
                (Lines.Current.SpaceAge && !Directory.Exists(modsDir) ? Tr.T(" (появится при установке)", " (created by the install)") : "");
            Tips.Set(lblModsPath, lblModsPath.Text);
            var off = DlcText(false);
            lblModsNote.Text = !Lines.Current.SpaceAge && modsDir != null && fac != null && !Relocation.SameDir(modsDir, Path.Combine(fac.Dir, "mods"))
                ? Tr.T("Своя папка модов, игра запускается с --mod-directory", "Own mods folder, the game starts with --mod-directory")
                : DlcText(true) + Tr.T(" вкл.", " on") + (off.Length > 0 ? ", " + off + Tr.T(" выкл.", " off") : "");
            Tips.Set(btnPickMods, Lines.Current.SpaceAge
                ? Tr.T("Перенести профиль Space Age в другую папку: вместе с модами переедут сейвы, настройки и кэш загрузки.\nСейчас профиль: ",
                       "Move the Space Age profile to another folder: the saves, settings and load cache go with the mods.\nThe profile now: ") + (GameProfile.SpaceAgeRoot(fac) ?? "?")
                : null);

            btnRollback.Text = rollbackTarget != null && rollbackNewer ? Tr.T("Вернуть", "Return") : Tr.T("Откатить", "Roll back");
            Tips.Set(btnRollback, rollbackTarget == null
                ? Tr.T("Отката пока нет. Лаунчер сохраняет прошлую версию при обновлении сборки\n(только изменённые файлы, а не всю сборку).",
                       "No rollback yet. The launcher keeps the previous version when it updates the build\n(only the changed files, not the whole build).")
                : (rollbackNewer
                    ? Tr.T("Вернуть версию сборки от ", "Return to the build of ") + rollbackDate
                    : Tr.T("Откатить сборку на версию от ", "Roll the build back to the version of ") + rollbackDate) +
                  Tr.T(" без интернета: её файлы сохранены при обновлении.\nЭта же кнопка вернёт обратно.",
                       " without internet: its files were kept at the update.\nThe same button switches back."));

            bool modsOk = modsDir != null && Directory.Exists(modsDir);
            bool playOk = fac != null && fac.Ok && BuildPresent && modsOk;
            btnUpdate.IsEnabled = !busy && modsDir != null && !Pinned;
            btnPlay.IsEnabled = !busy && playOk;
            btnCopy.IsEnabled = !busy && engine != null && (installed != null || (found != null && found.Present && found.Fingerprint != null));
            btnVerify.IsEnabled = !busy && installed != null && engine != null && modsOk && !OtherLineBuild;
            btnCheck.IsEnabled = !busy && git != null;
            btnRollback.IsEnabled = !busy && rollbackTarget != null && engine != null && modsOk;
            btnHost.IsEnabled = !busy && engine != null && modsDir != null;
            btnReport.IsEnabled = !busy;
            btnGuide.IsEnabled = !busy;
            btnShortcuts.IsEnabled = !busy;
            tab21.IsEnabled = tabSa.IsEnabled = !busy && engine != null && fac != null;
            chkAsk.IsEnabled = !busy;
            btnPickFac.IsEnabled = !busy;
            btnPickMods.IsEnabled = !busy && (!Lines.Current.SpaceAge || engine != null);
            btnOpenMods.IsEnabled = modsOk;
            chkCache.IsEnabled = !busy;
            chkPin.IsEnabled = !busy && installed != null;
            start.Card21.IsEnabled = start.CardSa.IsEnabled = !busy;
            start.BtnPick.IsEnabled = start.BtnRecheck.IsEnabled = !busy;
        }

        void Status(string text, Color c)
        {
            lblStatus.Text = text;
            lblStatus.Color = c;
        }

        void Idle(string text)
        {
            lblStage.Text = text;
            progress.SetActive(false);
            progress.Indeterminate = false;
        }

        void SetBusy(bool b)
        {
            busy = b;
            cancelable = b;
            btnCancel.IsVisible = b;
            btnCancel.IsEnabled = b;
            progress.SetActive(b);
            DoLayout();
            UpdateUi();
        }

        void AppendLog(string text, LogKind kind)
        {
            Color c = kind == LogKind.Ok ? Gui.Green : kind == LogKind.Warn ? Gui.Orange : kind == LogKind.Error ? Gui.Red : kind == LogKind.Muted ? Gui.Muted : Gui.Text;
            rtbLog.Append(DateTime.Now.ToString("HH:mm:ss") + "  ", Gui.Muted);
            rtbLog.Append(text + "\n", c);
            rtbLog.ScrollToEnd();
        }

        /// <summary>A bold journal line in the caption colour (the guide was updated).</summary>
        void AppendLogHighlight(string text)
        {
            rtbLog.Append(DateTime.Now.ToString("HH:mm:ss") + "  ", Gui.Muted);
            rtbLog.Append(text + "\n", Gui.Title, true);
            rtbLog.ScrollToEnd();
        }

        void Ui(Action a)
        {
            if (closed) return;
            Dispatcher.UIThread.Post(() => { if (!closed) a(); });
        }

        void CloseLater() { Dispatcher.UIThread.Post(Close); }

        // ------------------------------------------------------------------ ISyncUi (called from worker threads)

        public void Log(string text, LogKind kind = LogKind.Info) => Ui(() => AppendLog(text, kind));

        public void Stage(string ru, string en) => Ui(() => { lblStage.Text = Tr.T(ru, en); AppendLog(ru, LogKind.Muted); });

        public void Progress(double? fraction, string detail) => Ui(() =>
        {
            progress.Indeterminate = fraction == null;
            if (fraction != null) progress.Value = fraction.Value;
            if (detail != null) lblDetail.Text = detail;
        });

        public void CanCancel(bool value) => Ui(() => { cancelable = value; btnCancel.IsEnabled = value && busy; });

        /// <summary>
        /// A question of the engine: the dialog runs on the UI thread, the engine awaits the answer. The answer resumes the
        /// engine on the thread pool, never inline on the UI thread (the engine's file work must not freeze the window).
        /// </summary>
        static Task<bool> AskOnUi(Func<Task<bool>> ask)
        {
            var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try { answer.SetResult(await ask()); }
                catch (Exception ex) { answer.SetException(ex); }
            });
            return answer.Task;
        }

        public Task<bool> ConfirmExtras(List<string> names)
        {
            return AskOnUi(async () =>
            {
                var details = names.Take(400).ToList();
                if (names.Count > 400) details.Add(Tr.T("… и ещё ", "… and ") + Fmt.Num(names.Count - 400) + Tr.T("", " more"));
                AppendLog("В папке mods не из сборки: " + Fmt.Num(names.Count) + (Auto ? " (" + string.Join(", ", names) + ")" : ""), LogKind.Warn);
                if (Auto) { AppendLog("[тест: вопрос о лишних модах без показа, ответ «Оставить»]", LogKind.Muted); return false; }
                var r = await DarkDialog.Ask(this, Tr.T("Лишние моды", "Extra mods"),
                    Tr.T("В папке mods есть моды и файлы, которых нет в сборке (", "The mods folder has mods and files that are not in the build (") + Fmt.Num(names.Count) + ").",
                    Tr.T("Удалить их, чтобы папка точно совпадала со сборкой? Если оставить, лаунчер больше про них не спросит.",
                         "Delete them so the folder matches the build exactly? If you keep them, the launcher does not ask about them again."),
                    details, Tr.T("Удалить", "Delete"), Tr.T("Оставить", "Keep"), Gui.Title, ButtonKind.Danger);
                return r == DialogResult.Yes;
            });
        }

        public Task<bool> ConfirmOverwrite(int files, List<string> sample, BuildFound f)
        {
            return AskOnUi(async () =>
            {
                if (seeding != null)
                {
                    AppendLog("Первая установка Space Age: файлов сборки 2.1, которые отличаются от сборки Space Age, " + files + ". Заменяю их без вопроса.", LogKind.Muted);
                    return true;
                }
                string message;
                if (f != null && f.OtherLine)
                    message = Tr.T("В папке mods сборка Paranoidal для Factorio ", "The mods folder has the Paranoidal build for Factorio ") + f.FactorioVersion +
                        Tr.T(", установка переведёт её на ", "; installing switches it to ") + Lines.Current.Name + ".";
                else if (f != null && f.Present && f.Legacy)
                    message = Tr.T("В папке mods старая сборка Paranoidal для Factorio ", "The mods folder has the old Paranoidal build for Factorio ") + f.FactorioVersion +
                        (f.ZzzVersion != null ? " (zzzparanoidal " + f.ZzzVersion + ")" : "") + ".";
                else if (f != null && f.Present)
                    message = Tr.T("В папке mods уже есть сборка Paranoidal, но её файлы отличаются от устанавливаемой версии.",
                        "The mods folder already has a Paranoidal build, but its files differ from the version being installed.");
                else
                    message = Tr.T("В папке mods уже есть моды сборки, но их файлы отличаются от устанавливаемой версии.",
                        "The mods folder already has mods of the build, but their files differ from the version being installed.");
                AppendLog("Первая синхронизация: отличаются от сборки файлов " + files + ". Спрашиваю подтверждение.", LogKind.Warn);
                var details = sample.ToList();
                if (files > sample.Count) details.Add(Tr.T("… и ещё ", "… and ") + Fmt.Num(files - sample.Count));
                var r = await DarkDialog.Ask(this, Tr.T("Файлы в папке mods", "Files in the mods folder"), message,
                    Tr.T("Отличающихся файлов: ", "Files that differ: ") + Fmt.Num(files) +
                    Tr.T(". Установка заменит их файлами версии с GitHub, скачается только разница. Моды не из сборки, mod-list.json и mod-settings.dat останутся как есть. " +
                         "Если эту сборку вам дал хост и с ним нужно совпадать, нажмите «Отмена» и воспользуйтесь «Версией хоста…».",
                         ". Installing replaces them with the files of the GitHub version and downloads only the difference. Mods not from the build, mod-list.json and mod-settings.dat stay as they are. " +
                         "If the host gave you this build and you must match the host, press Cancel and use \"Host's version…\"."),
                    details, Tr.T("Заменить", "Replace"), Tr.T("Отмена", "Cancel"), Gui.Title);
                return r == DialogResult.Yes;
            });
        }

        // ------------------------------------------------------------------ operations

        /// <summary>The token belongs to the entire scenario, including all its sequential steps.</summary>
        CancellationToken OpToken => opCancel.Token;
        CancellationTokenSource opCancel = new CancellationTokenSource();
        Task gitCancellation = Task.CompletedTask;

        // Worker progress is posted at default priority. Drain it before applying the final UI state.
        static async Task FlushProgress() => await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Default);

        /// <summary>
        /// Only event boundaries acquire ownership. Scenario methods await each other directly, so nested steps never
        /// release another step's busy state or replace its cancellation source. Expensive work explicitly uses Task.Run.
        /// </summary>
        async Task RunOperation(Func<Task> scenario)
        {
            if (busy || closed || restarting || closeQuestionPending) return;
            opCancel.Dispose();
            opCancel = new CancellationTokenSource();
            SetBusy(true);
            try { await scenario(); }
            catch (Exception error)
            {
                await FlushProgress();
                if (closed) return;
                if (error is OperationCanceledException)
                {
                    Idle(Tr.T("Отменено.", "Cancelled."));
                    lblDetail.Text = Tr.T("Файлы модов не изменены. Уже скачанное сохранено, следующее обновление продолжит с этого места.",
                        "The mod files were not changed. What was downloaded is kept, and the next update continues from here.");
                    AppendLog("Операция отменена.", LogKind.Warn);
                }
                else
                {
                    var se = error as SyncException;
                    var shown = se != null ? se.Display : error.Message;
                    Idle(Tr.T("Ошибка", "Error"));
                    lblDetail.Text = shown.Split('\n')[0];
                    AppendLog(se != null ? error.Message : error.ToString(), LogKind.Error);
                    await DarkDialog.Info(this, Tr.T("Ошибка", "Error"), shown, Gui.Red);
                }
                if (Auto && autoCloseAfterUpdate && !restarting) CloseLater();
            }
            finally
            {
                await gitCancellation;
                await FlushProgress();
                if (!closed) SetBusy(false);
            }
        }
    }
}
