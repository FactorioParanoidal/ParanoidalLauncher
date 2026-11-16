using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Paranoidal
{
    // The actions of the window (MainForm.cs of 1.5.0 from «the launcher's place» on): relocation, update, check, rollback,
    // the host's version, the game start and its load diagnostics, the version line, the report, the folders, closing.
    sealed partial class MainWindow
    {
        // ------------------------------------------------------------------ the launcher's place: the game folder

        /// <summary>
        /// The launcher works properly only from the game folder. When it is elsewhere (Downloads, the desktop, the folder of
        /// Factorio 2.0.77) and a suitable game is known, it moves itself there silently; if that fails it asks for
        /// administrator rights. True: the copy in the game folder was started and this process closes.
        /// </summary>
        async Task<bool> MaybeRelocate(List<FactorioInfo> all, bool fromSettings)
        {
            var why = Relocation.DisabledReason();
            if (why != null) { AppendLog("Перенос лаунчера в папку игры выключен: " + why + ".", LogKind.Muted); return false; }
            var target = fac;
            if (!fromSettings && all != null)
            {
                var ok = all.Where(x => x.Ok).ToList();
                if (ok.Any(x => Relocation.SameDir(x.Dir, AppInfo.ExeDir))) return false;
                if (ok.Count > 1)
                {
                    AppendLog("Найдено несколько подходящих установок игры: " + string.Join("; ", ok.Select(x => x.Dir)), LogKind.Info);
                    var chosen = Auto ? TestChosen(ok) : await Dialogs.ChooseInstall(this, ok);
                    if (chosen == null)
                    {
                        installPostponed = true;
                        AppendLog("Перенос лаунчера отложен (установка не выбрана). Пока лаунчер работает с " + fac.Dir +
                            " и спросит снова при следующем запуске.", LogKind.Warn);
                        return false;
                    }
                    target = fac = chosen;
                }
            }
            if (target == null || !target.Ok)
            {
                if (!Relocation.SameDir(AppInfo.ExeDir, target != null ? target.Dir : ""))
                    AppendLog("Лаунчер лежит не в папке игры (" + AppInfo.ExeDir + "): он перенесёт себя туда, когда будет выбрана " + AppInfo.RequiredGame + ".", LogKind.Warn);
                return false;
            }
            if (Relocation.SameDir(target.Dir, AppInfo.ExeDir)) return false;
            SetBusy(true);
            cancelable = false;
            btnCancel.IsEnabled = false;
            try { return await RelocateTo(target); }
            finally { if (!restarting) SetBusy(false); }
        }

        /// <summary>Tests: the answer to «several installations» (PARANOIDAL_LAUNCHER_TEST_CHOOSE).</summary>
        static FactorioInfo TestChosen(List<FactorioInfo> ok)
        {
            int n;
            if (TestChoose == "none") return null;
            return TestChoose != null && int.TryParse(TestChoose, out n) && n >= 1 && n <= ok.Count ? ok[n - 1] : ok[0];
        }

        async Task<bool> RelocateTo(FactorioInfo target)
        {
            var exe = SelfUpdate.CurrentExe;
            var log = new List<string>();
            // settings.ini of another game folder (the launcher lay in a Factorio 2.0.77 folder): the copy finds its own mods
            var saved = Settings.GetPath("factorio");
            bool otherGame = saved != null && !Relocation.SameDir(saved, target.Dir);
            AppendLog("Лаунчер лежит не в папке игры (" + AppInfo.ExeDir + "). Переношу его в " + target.Dir + ".", LogKind.Info);
            if (!mainShown)
            {
                start.SetBusy(Tr.T("Переношу лаунчер в папку игры", "Moving the launcher into the game folder"), AppInfo.ExeDir + "  →  " + target.Dir);
                SetScreen(StartPanel.View.Busy);
            }
            string newExe = null, error = null;
            if (AppInfo.CanWrite(target.Dir))
            {
                // the copy of the exe and the data folder runs off the UI thread; the log is read after it
                try { newExe = await Task.Run(() => Relocation.MoveTo(exe, AppInfo.DataDir, target.Dir, log, otherGame)); }
                catch (Exception ex) { error = ex.Message; }
            }
            else error = "в папку игры нельзя записывать без прав администратора";
            foreach (var l in log) AppendLog("Перенос: " + l + ".", LogKind.Muted);
            if (newExe == null)
            {
                AppendLog("Перенести лаунчер автоматически не удалось: " + error + ".", LogKind.Warn);
                var r = Auto ? DialogResult.No : await DarkDialog.Ask(this, Tr.T("Папка игры", "Game folder"),
                    Tr.T("Лаунчер нужно перенести в папку игры", "The launcher must be moved into the game folder"),
                    Tr.T("Лаунчер должен лежать в папке игры, в которую вы играете:\n",
                         "The launcher must live in the folder of the game you play:\n") + target.Dir + "\n\n" +
                    Tr.T("Сам перенестись туда он не смог (", "It could not move itself there (") + (Tr.En ? EnError(error) : error) +
                    Tr.T("). Если нажать «Перенести», Windows попросит права администратора, а лаунчер скопирует себя в папку игры, оставит ярлык на рабочем столе и запустится оттуда.\n\n" +
                         "Если игра стоит в Program Files, перенесите всю её папку в обычную, например C:\\Games\\Factorio. " +
                         "Тогда права администратора не понадобятся ни для переноса, ни для обновлений, и это сообщение больше не появится.",
                         "). If you press \"Move\", Windows asks for administrator rights, and the launcher copies itself into the game folder, leaves a desktop shortcut and starts from there.\n\n" +
                         "If the game is in Program Files, move its whole folder to a normal one, e.g. C:\\Games\\Factorio. " +
                         "Then neither the move nor updates need administrator rights, and this message does not appear again."),
                    null, Tr.T("Перенести", "Move"), Tr.T("Не сейчас", "Not now"), Gui.Orange);
                if (r != DialogResult.Yes)
                {
                    AppendLog("Перенос лаунчера отложен. Правильная работа гарантируется только из папки игры, лаунчер спросит снова при следующем запуске.", LogKind.Warn);
                    lblDetail.Text = Tr.T("Лаунчер не в папке игры, поэтому правильная работа не гарантируется", "The launcher is not in the game folder, so proper work is not guaranteed");
                    return false;
                }
                string err2 = null;
                bool moved = Relocation.TargetIsSameOrNewer(target.Dir);
                if (!moved) (moved, err2) = await SelfUpdate.InstallElevatedAsync(exe, exe, target.Dir);
                if (!moved)
                {
                    AppendLog(err2 == null ? "Права администратора не выданы, перенос отложен." : "Перенос не удался: " + err2, LogKind.Warn);
                    if (err2 != null) await DarkDialog.Info(this, Tr.T("Папка игры", "Game folder"), Tr.T("Перенос не удался: ", "The move failed: ") + err2, Gui.Red);
                    return false;
                }
                newExe = Path.Combine(target.Dir, AppInfo.ExeName);
                try { Relocation.PrepareLegacyData(AppInfo.DataDir, AppInfo.ExeDir, otherGame); }
                catch (Exception ex) { AppendLog("Данные лаунчера не перенесены: " + ex.Message, LogKind.Warn); }
            }
            try { AppendLog("Ярлык на рабочем столе: " + Shortcuts.Desktop(newExe), LogKind.Ok); }
            catch (Exception ex) { AppendLog("Ярлык на рабочем столе не создан: " + ex.Message, LogKind.Warn); }
            try { SelfUpdate.Restart(newExe, "--moved-from " + FsUtil.QuoteArg(exe)); }
            catch (Exception ex)
            {
                AppendLog("Не удалось запустить лаунчер из папки игры: " + ex.Message, LogKind.Error);
                return false;
            }
            restarting = true;
            CloseLater();
            return true;
        }

        static string EnError(string ru) => ru != null && ru.StartsWith("в папку игры нельзя") ? "the game folder cannot be written without administrator rights" : ru;

        // ------------------------------------------------------------------ update and file check

        /// <summary>«Проверить файлы»: read every file of the installed build again and restore what differs.</summary>
        async Task DoVerify()
        {
            if (engine == null || installed == null) return;
            var err = SyncPaths.Validate(modsDir);
            if (err != null) { await DarkDialog.Info(this, Tr.T("Папка модов", "Mods folder"), err, Gui.Red); return; }
            if (FactorioInfo.IsRunning()) { await DarkDialog.Info(this, Tr.T("Factorio запущена", "Factorio is running"), Tr.T("Закройте игру и запустите проверку снова.", "Close the game and start the check again."), Gui.Orange); return; }
            var mods = modsDir;
            RunOp(() => engine.UpdateAsync(new SyncPaths(mods), true), res =>
            {
                installed = res.New;
                progress.Value = 1;
                int files = res.Mods.Sum(x => x.Files);
                if (res.UpToDate)
                {
                    Idle(Tr.T("Проверка файлов: всё в порядке", "File check: all good"));
                    lblDetail.Text = Tr.T("Все файлы модов побайтно совпадают с установленной версией сборки", "Every mod file matches the installed build version byte for byte");
                    AppendLog("Проверка файлов: расхождений нет.", LogKind.Ok);
                }
                else
                {
                    Idle(Tr.T("Проверка файлов: исправлено", "File check: fixed"));
                    lblDetail.Text = Tr.T("Исправлено или удалено файлов: ", "Files fixed or removed: ") + Fmt.Num(files) + Tr.T(", список на вкладке «Изменённые моды»", ", listed on the \"Changed mods\" tab");
                    AppendLog("Проверка файлов: исправлено " + Fmt.Num(files) + " файл(ов), теперь они совпадают со сборкой.", LogKind.Ok);
                    FillMods(res.Mods);
                    if (res.Mods.Count > 0) SelectTab(1);
                }
                modified = 0;
                RefreshAfterUpdate();
            });
        }

        void ApplyStatus(StatusInfo st, bool keepRemote)
        {
            installed = st.Installed;
            installedDate = st.InstalledDate;
            disk = st.Disk;
            remote = keepRemote ? (st.Remote ?? remote) : st.Remote;
            offline = st.Remote == null ? (keepRemote ? offline && remote == null : st.Offline) : false;
            modified = st.Modified;
            found = st.Found;
            adopted = st.Adopted;
            rollbackTarget = st.RollbackTarget;
            rollbackDate = st.RollbackDate;
            rollbackNewer = st.RollbackIsNewer;
            remoteDate = null;
            if (remote != null && st.Commits != null)
                foreach (var c in st.Commits)
                    if (c.Sha == remote && c.Date != default(DateTime)) { remoteDate = Fmt.Date(c.Date); break; }
            FillCommits(st.Commits, st.CommitsAreNew, st.CommitsCached);
        }

        void FillCommits(List<CommitInfo> commits, bool areNew, bool cached)
        {
            shownCommits = commits;
            shownCommitsNew = areNew;
            shownCommitsCached = cached;
            var items = new List<ListItem>();
            if (commits != null)
                foreach (var c in commits)
                {
                    bool here = !areNew && c.Sha == installed;
                    items.Add(new ListItem(areNew ? Gui.Orange : here ? Gui.Green : Gui.Text,
                        c.Date == default(DateTime) ? "" : Fmt.Date(c.Date),
                        here ? c.Message + Tr.T("   (установлена)", "   (installed)") : c.Message));
                }
            lvCommits.SetItems(items);
            tabs[0].Text = areNew && commits != null ? Tr.T("Что нового (", "What's new (") + commits.Count + ")"
                : cached ? Tr.T("Изменения (сохранённые)", "Changes (saved list)") : Tr.T("Последние изменения", "Latest changes");
            Tips.Set(tabs[0], cached ? Tr.T("Нет связи с GitHub: показан список, сохранённый при последней проверке.", "No connection to GitHub: the list saved at the last check.") : null);
            DoLayout();
        }

        async Task DoUpdate(bool thenPlay, string target)
        {
            if (engine == null) return;
            if (target == null && Pinned) return;
            var err = SyncPaths.Validate(modsDir);
            if (err != null) { await DarkDialog.Info(this, Tr.T("Папка модов", "Mods folder"), err, Gui.Red); return; }
            if (FactorioInfo.IsRunning()) { await DarkDialog.Info(this, Tr.T("Factorio запущена", "Factorio is running"), Tr.T("Закройте игру и запустите обновление снова.", "Close the game and start the update again."), Gui.Orange); return; }
            // the latest build needs a newer game than the player has: the build can be updated, the game must follow
            if (target == null && remoteReq != null && fac != null && !Lines.Current.Exact && Lines.Current.Matches(fac.Version) &&
                Releases.Compare(fac.Version, remoteReq) < 0 && remote != installed && !Auto)
            {
                var a = await DarkDialog.Ask(this, Tr.T("Нужна новая версия игры", "A newer game is needed"),
                    Tr.T("Новая версия сборки требует Factorio ", "The new build version needs Factorio ") + remoteReq + Tr.T(" или новее", " or newer"),
                    Tr.T("У вас Factorio " + fac.Version + ". Сборка обновится, но играть можно будет только после обновления игры. Обновить сборку сейчас?",
                         "You have Factorio " + fac.Version + ". The build will be updated, but you can play only after updating the game. Update the build now?"),
                    null, Tr.T("Обновить сборку", "Update the build"), Tr.T("Отмена", "Cancel"), Gui.Orange);
                if (a != DialogResult.Yes) return;
            }
            var mods = modsDir;
            // the guide goes together with the build update; only the guide when the build is current already
            var guide = GuideUpdate ? guideRel : null;
            bool build = !(guide != null && target == null && installed != null && installed == remote && modified <= 0);
            var seed = target == null ? seeding : null;
            RunOp(async () =>
            {
                var o = new UpdateOutcome();
                if (build)
                {
                    engine.Seeded = seed;
                    try { o.Res = await engine.UpdateAsync(new SyncPaths(mods), false, target); }
                    finally { engine.Seeded = null; }
                }
                if (guide != null && (o.Res == null || !o.Res.Declined))
                {
                    Stage("Скачиваю гайд по сборке " + guide.Version + "…", "Downloading the modpack guide " + guide.Version + "…");
                    try { await InstallGuideAsync(guide); o.Guide = guide.Version; }
                    catch (Exception ex) { o.GuideError = ex.Message; }
                }
                return o;
            }, o =>
            {
                var res = o.Res;
                if (res != null && seed != null) seeding = null;
                if (res != null && res.Declined)
                {
                    Idle(Tr.T("Установка отменена, файлы не тронуты.", "Installation cancelled, files untouched."));
                    lblDetail.Text = "";
                    AppendLog("Обновление отменено пользователем.", LogKind.Warn);
                    return;
                }
                if (res == null)
                {
                    progress.Value = 1;
                    Idle(o.Guide != null ? Tr.T("Гайд по сборке обновлён", "The modpack guide is updated") : Tr.T("Гайд по сборке не обновлён", "The modpack guide was not updated"));
                    lblDetail.Text = o.Guide != null ? guideLocal : o.GuideError;
                    ReportGuide(o);
                    if (thenPlay) Launch();
                    return;
                }
                installed = res.New;
                installedDate = null;
                modified = 0;
                progress.Value = 1;
                if (res.UpToDate)
                {
                    Idle(target != null ? Tr.T("Уже установлена версия хоста", "The host's version is installed already")
                        : Tr.T("Изменений нет, установлена последняя версия", "No changes: the latest version is installed"));
                    lblDetail.Text = target != null ? "" : Tr.T("Моды соответствуют последней версии сборки", "The mods match the latest build version");
                    AppendLog("Изменений нет, всё уже актуально.", LogKind.Ok);
                }
                else
                {
                    if (res.FullCheck && res.Old != null) AppendLog("Служебный индекс не найден, поэтому все файлы модов сверены заново побайтно.", LogKind.Muted);
                    Idle(target != null ? Tr.T("Установлена версия хоста", "The host's version is installed")
                        : res.Old == null ? Tr.T("Сборка установлена", "The build is installed") : Tr.T("Моды обновлены до последней версии", "The mods are updated to the latest version"));
                    lblDetail.Text = Tr.T("Скачано файлов: ", "Files downloaded: ") + Fmt.Num(res.Downloaded) +
                        (res.Old != null ? Tr.T(", прошлая версия сохранена для отката", "; the previous version is kept for a rollback") : "");
                    AppendLog(target != null ? "Готово. Моды соответствуют версии хоста " + AppInfo.Short(res.New) : "Готово. Моды соответствуют последней версии сборки", LogKind.Ok);
                    FillMods(res.Mods);
                    if (res.Mods.Count > 0) SelectTab(1);
                }
                if (target != null && !chkPin.Checked)
                {
                    chkPin.Checked = true;   // also sets the setting and logs it
                    AppendLog("После перехода на версию хоста включено «Не менять версию». Снимите галочку, когда хост обновится.", LogKind.Info);
                }
                ReportGuide(o);
                if (thenPlay) Launch();
                else RefreshAfterUpdate();
            });
        }

        sealed class UpdateOutcome { public UpdateResult Res; public string Guide, GuideError; }

        void ReportGuide(UpdateOutcome o)
        {
            RefreshGuideLocal();
            if (o.Guide != null) AppendLogHighlight("Обновился гайд по сборке Paranoidal: версия " + o.Guide + ", " + guideLocal);
            else if (o.GuideError != null) AppendLog(o.GuideError + " Сборка от этого не зависит; гайд скачается при следующем обновлении.", LogKind.Warn);
        }

        void RefreshAfterUpdate()
        {
            var stage = lblStage.Text;
            var detail = lblDetail.Text;
            var mods = modsDir;
            RunOp(() => engine.CheckStatusAsync(mods), st =>
            {
                ApplyStatus(st, true);
                // the game the build needs now (a newer build may need a newer game)
                if (disk != null && disk.Present && Lines.Current.Matches(disk.FactorioVersion)) Lines.Current.SetRequired(disk.BaseMin);
                header.InvalidateVisual();
                Idle(stage);
                progress.Value = 1;
                lblDetail.Text = detail;
                if (Auto && autoCloseAfterUpdate && !restarting) CloseLater();   // tests: the update after the move
            });
        }

        void FillMods(List<ModChange> mods)
        {
            shownMods = mods;
            var items = new List<ListItem>();
            if (mods != null)
                foreach (var m in mods)
                    items.Add(new ListItem(m.Kind.StartsWith("нов") ? Gui.Green : m.Kind.Contains("удал") ? Gui.Orange : Gui.Text,
                        m.Mod, m.Display, Fmt.Num(m.Files)));
            lvMods.SetItems(items);
            tabs[1].Text = Tr.T("Изменённые моды", "Changed mods") + (mods != null ? " (" + mods.Count + ")" : "");
            DoLayout();
        }

        // ------------------------------------------------------------------ rollback and the host's version

        async Task DoRollback()
        {
            if (engine == null || rollbackTarget == null) return;
            if (FactorioInfo.IsRunning()) { await DarkDialog.Info(this, Tr.T("Factorio запущена", "Factorio is running"), Tr.T("Закройте игру и повторите.", "Close the game and try again."), Gui.Orange); return; }
            var r = await DarkDialog.Ask(this, rollbackNewer ? Tr.T("Вернуть версию", "Return to a version") : Tr.T("Откат сборки", "Build rollback"),
                rollbackNewer ? Tr.T("Вернуть версию сборки от ", "Return to the build of ") + rollbackDate + "?"
                              : Tr.T("Откатить сборку на версию от ", "Roll the build back to the version of ") + rollbackDate + "?",
                Tr.T("Интернет не нужен: файлы этой версии сохранены при обновлении. Сейчас установлена версия от ", "No internet needed: the files of that version were kept at the update. Installed now: the version of ") +
                (installedDate ?? AppInfo.Short(installed)) + ".\n" +
                (rollbackNewer ? "" : Tr.T("Сейв, сделанный на текущей версии, может не загрузиться на старой. ", "A save made on the current version may not load on the old one. ")) +
                Tr.T("mod-settings.dat и моды не из сборки не меняются. Вернуться можно этой же кнопкой.",
                     "mod-settings.dat and mods not from the build are not changed. The same button switches back."),
                null, rollbackNewer ? Tr.T("Вернуть", "Return") : Tr.T("Откатить", "Roll back"), Tr.T("Отмена", "Cancel"), Gui.Title);
            if (r != DialogResult.Yes) return;
            var mods = modsDir;
            bool back = !rollbackNewer;
            RunOp(() => engine.RollbackAsync(new SyncPaths(mods)), async res =>
            {
                installed = res.New;
                installedDate = null;
                progress.Value = 1;
                Idle(back ? Tr.T("Сборка откачена", "The build was rolled back") : Tr.T("Версия возвращена", "The version is back"));
                FillMods(res.Mods);
                if (res.Mods.Count > 0) SelectTab(1);
                if (res.NotRestored > 0)
                {
                    lblDetail.Text = Tr.T("Не совпадают со сборкой файлов: ", "Files that do not match the build: ") + res.NotRestored + Tr.T(", нажмите «Проверить файлы» (нужен интернет)", ": press \"Check files\" (needs internet)");
                    await DarkDialog.Info(this, Tr.T("Откат", "Rollback"), Tr.T("Готово, но файлов, которые были изменены вручную до обновления, нет в сохранённой копии: ", "Done, but files changed by hand before the update are not in the saved copy: ") +
                        res.NotRestored + Tr.T(".\nНажмите «Проверить файлы», когда будет интернет.", ".\nPress \"Check files\" when you have internet."), Gui.Orange);
                }
                else lblDetail.Text = back && !pinSetting
                    ? Tr.T("Чтобы лаунчер не предлагал обновиться перед игрой, включите «Не менять версию»", "To stop the launcher offering the update before playing, turn on \"Keep this version\"")
                    : "";
                RefreshAfterUpdate();
            });
        }

        async Task DoHost()
        {
            if (engine == null || modsDir == null) return;
            if (Lines.Current.SpaceAge && fac != null && !GameProfile.SpaceAgeExists(fac))
            {
                await DarkDialog.Info(this, Tr.T("Версия хоста", "Host's version"), Tr.T("Сначала установите " + Lines.SA.Name + " кнопкой «Установить».",
                    "Install " + Lines.SA.Name + " with the \"Install\" button first."), Gui.Orange);
                return;
            }
            var line = await Dialogs.AskHostLine(this);
            if (line == null) return;
            var sha = VersionLine.ParseHost(line);
            if (sha == null) return;
            var hostLine = VersionLine.ParseHostLine(line);
            var hostGame = VersionLine.HostGame(line);
            if (hostLine != null && hostLine != Lines.Current) { await OtherLineHost(hostLine, null); return; }
            if (hostLine == null && hostGame != null) { await OtherLineHost(null, hostGame); return; }
            if (installed != null && installed.StartsWith(sha))
            {
                await DarkDialog.Info(this, Tr.T("Версия хоста", "Host's version"), Tr.T("У вас уже установлена эта версия сборки.", "You already have this build version."), Gui.Green);
                if (!chkPin.Checked) chkPin.Checked = true;
                return;
            }
            AppendLog("Версия хоста: " + line, LogKind.Info);
            RunOp(async () =>
            {
                Stage("Ищу версию хоста на GitHub…", "Looking the host's version up on GitHub…");
                Progress(null, "");
                CommitInfo ci;
                try { ci = await GitHubApi.CommitAsync(sha); }
                catch (Exception ex) { throw new SyncException("Версия " + sha + " не найдена на GitHub: " + ex.Message, "Version " + sha + " was not found on GitHub: " + ex.Message); }
                // a bare commit id says nothing about the line: its zzzparanoidal tells the game, the branch tells 2.1 from
                // Space Age (the Space Age branch contains the 2.1 history, so 2.1 is asked first)
                var bi = ci == null ? null : await RemoteBuild.InfoAsync(ci.Sha);
                BuildLine bl = bi == null ? null : Lines.Of(bi.FactorioVersion);
                if (bl != null && hostLine != null && bl.Game == hostLine.Game) bl = hostLine;
                else if (bl == Lines.L21)
                    bl = await GitHubApi.InBranchAsync(Lines.L21.Branch, ci.Sha) ? Lines.L21 : await GitHubApi.InBranchAsync(Lines.SA.Branch, ci.Sha) ? Lines.SA : null;
                // a version of a build for another game (2.0): the launcher has no build for it
                var game = bi != null && bl == null && bi.FactorioVersion != null && !Lines.L21.Matches(bi.FactorioVersion) ? Paranoidal.BuildLine.MajorMinor(bi.FactorioVersion) : null;
                return Tuple.Create(ci, bl, game);
            }, async res =>
            {
                Idle("");
                var c = res.Item1;
                if (c == null) { await DarkDialog.Info(this, Tr.T("Версия хоста", "Host's version"), Tr.T("Версия не найдена на GitHub.", "The version was not found on GitHub."), Gui.Red); return; }
                var bl = res.Item2;
                if (res.Item3 != null) { await OtherLineHost(null, res.Item3); return; }
                if (bl != null && bl != Lines.Current) { await OtherLineHost(bl, null); return; }
                var r = await DarkDialog.Ask(this, Tr.T("Версия хоста", "Host's version"),
                    Tr.T("Перейти на версию сборки от ", "Switch to the build of ") + (c.Date == default(DateTime) ? "?" : Fmt.Date(c.Date)) + "?",
                    "«" + c.Message + "» (" + AppInfo.Short(c.Sha) + ")\n" +
                    Tr.T("Сейчас установлена: ", "Installed now: ") + (installed == null ? Tr.T("неизвестная версия", "an unknown version") : installedDate ?? AppInfo.Short(installed)) + ".\n" +
                    Tr.T("Скачается только разница. Текущая версия сохранится для отката.", "Only the difference is downloaded. The current version is kept for a rollback."),
                    null, Tr.T("Перейти", "Switch"), Tr.T("Отмена", "Cancel"), Gui.Title);
                if (r == DialogResult.Yes) await DoUpdate(false, c.Sha);
            });
        }

        // ------------------------------------------------------------------ game start

        async Task Play()
        {
            if (fac == null || !fac.Ok)
            {
                await DarkDialog.Info(this, Tr.T("Версия Factorio", "Factorio version"), fac == null ? Tr.T("Для сборки нужна ", "The build needs ") + AppInfo.RequiredGame + "." : fac.Problem(), Gui.Red);
                return;
            }
            if (FactorioInfo.IsRunning()) { await DarkDialog.Info(this, "Factorio", Tr.T("Factorio уже запущена.", "Factorio is already running."), Gui.Orange); return; }
            if (!Pinned && remote != null && installed != null && remote != installed)
            {
                var r = await DarkDialog.Ask(this, Tr.T("Доступно обновление", "An update is available"), Tr.T("Вышла новая версия сборки. Обновить перед запуском?", "A new build version is out. Update before playing?"),
                    Tr.T("Если вы играете на сервере, где хост ещё не обновился, выберите «Играть без обновления» и включите «Не менять версию».",
                         "If you play on a server whose host has not updated yet, choose \"Play without updating\" and turn on \"Keep this version\"."), null,
                    Tr.T("Обновить и играть", "Update and play"), Tr.T("Играть без обновления", "Play without updating"), Gui.Title);
                if (r == DialogResult.Yes) { await DoUpdate(true, null); return; }
                if (r != DialogResult.No) return;
            }
            Launch();
        }

        void Launch()
        {
            if (fac == null || !fac.Ok || modsDir == null || engine == null) return;
            var mods = modsDir;
            var f = fac;
            var build = installed;
            bool cache = chkCache.Checked;
            var prof = GameProfile.Of(f, Lines.Current);
            RunOp(async () =>
            {
                var p = new SyncPaths(mods);
                await engine.PrepareForPlayAsync(p);
                engine.MarkPlayed(p, build);
                // Factorio writes config.ini when it exits: change it only while the game is closed
                if (FactorioInfo.IsRunning()) return true;
                // the Space Age profile: its config.ini points at the profile folder (also after the game folder moved)
                if (prof != null && prof.Own)
                {
                    try { prof.PrepareConfig(); }
                    catch (Exception ex) { Log("Не удалось подготовить " + prof.ConfigPath + ": " + ex.Message, LogKind.Warn); }
                }
                if (!cache) return true;
                ReportCache(prof != null && prof.Own ? GameConfig.EnableCacheAt(prof.ConfigPath) : GameConfig.EnableCache(f.Dir, f.Exe), GameConfig.TildeMods(mods));
                return true;
            }, _ => StartGame());
        }

        /// <summary>Inside an operation: logs what GameConfig did.</summary>
        void ReportCache(GameConfig.Result r, List<string> tilde)
        {
            if (r.Error != null)
                Log("Не удалось включить кэш загрузки в " + (r.ConfigPath ?? "config.ini") + ": " + r.Error, LogKind.Warn);
            else if (r.Created)
                Log("Создан " + r.ConfigPath + " с включённым кэшем загрузки (остальное игра допишет сама).", LogKind.Ok);
            else if (r.Changed.Count > 0)
                Log("Кэш загрузки включён в " + r.ConfigPath + ": " + string.Join(", ", r.Changed) +
                    ". Первые два запуска будут долгими, потом быстрыми.", LogKind.Ok);
            if (tilde.Count > 0)
                Log("Кэш данных игры работать не будет: у модов " + string.Join(", ", tilde) +
                    " в info.json есть зависимость с «~» (ошибка Factorio, есть в 2.0.77 и 2.1.24: игра каждый раз пересобирает данные). Подробности в FAQ сборки, в вопросе о долгой загрузке.", LogKind.Warn);
        }

        async Task StartGame()
        {
            if (fac == null || !fac.Ok || modsDir == null) return;
            if (FactorioInfo.IsRunning()) { await DarkDialog.Info(this, "Factorio", Tr.T("Factorio уже запущена.", "Factorio is already running."), Gui.Orange); return; }
            try
            {
                // the Space Age profile: its own config.ini (write-data = the profile folder)
                var prof = GameProfile.Of(fac, Lines.Current);
                var args = prof != null ? prof.GameArgs(modsDir) : "--mod-directory " + FsUtil.QuoteArg(modsDir);
                using (Process.Start(new ProcessStartInfo(fac.Exe, args)
                {
                    WorkingDirectory = Path.GetDirectoryName(fac.Exe),
                    UseShellExecute = false,
                })) { }
                // the load diagnostics compare this start with the previous one: did the launcher change the build in between
                Settings.Set(Lines.Key("launch-build-prev"), Settings.Get(Lines.Key("launch-build")));
                Settings.Set(Lines.Key("launch-build"), installed ?? (found != null ? "fp:" + found.Fingerprint : ""));
                gameRunning = true;
                AppendLog("Factorio запущена: " + fac.Exe + " " + args, LogKind.Ok);
                lblDetail.Text = Tr.T("Factorio запускается… После выхода из игры здесь появится диагностика загрузки.", "Factorio is starting… After you quit the game, the load diagnostics appear here.");
            }
            catch (Exception ex)
            {
                await DarkDialog.Info(this, Tr.T("Не удалось запустить Factorio", "Could not start Factorio"), ex.Message, Gui.Red);
            }
        }

        // ------------------------------------------------------------------ load diagnostics

        async Task WatchGame()
        {
            bool running = FactorioInfo.IsRunning();
            if (gameRunning && !running) AnalyzeLoad();
            gameRunning = running;
            LogoArt.Paused = running || WindowState == WindowState.Minimized;
            if (!running && selfUpdatePending && !busy) await OfferSelfUpdate();
        }

        /// <summary>Reads the game's last log (the game must be closed) and shows how the load went and why.</summary>
        async void AnalyzeLoad()
        {
            var f = fac;
            var mods = modsDir;
            if (f == null) return;
            string prevBuild = Settings.Get(Lines.Key("launch-build-prev")), curBuild = Settings.Get(Lines.Key("launch-build"));
            var line = Lines.Current;
            var diagKey = Lines.Key("load-diag-key");
            LoadVerdict v;
            try { v = await Task.Run(() =>
            {
                // the game of the line writes its log into its data folder (the Space Age profile has its own)
                var prof = GameProfile.Of(f, line);
                if (prof == null) return null;
                var wd = prof.WriteData;
                var cur = LoadLog.Read(Path.Combine(wd, "factorio-current.log"));
                if (cur == null) return null;
                var prev = LoadLog.Read(Path.Combine(wd, "factorio-previous.log"));
                var ctx = new LoadContext
                {
                    Tilde = GameConfig.TildeMods(mods),
                    BuildChanged = !string.IsNullOrEmpty(prevBuild) && !string.IsNullOrEmpty(curBuild) && prevBuild != curBuild,
                    ModsDir = mods,
                };
                return LoadDiag.Analyze(cur, prev, ctx);
            }); }
            catch { return; }   // no diagnostics this time; the next game exit tries again
            if (closed || v == null || line != Lines.Current) return;
            {
                lastLoad = v;
                ShowLoad();
                if (v.Key != Settings.Get(diagKey))
                {
                    Settings.Set(diagKey, v.Key);
                    LoadDiag.Remember(v);
                    bool good = v.Data == LoadLog.DataState.Hit && (v.Headless || v.Atlas == LoadLog.AtlasState.Hit);
                    AppendLog(v.Full(false), good ? LogKind.Ok : LogKind.Warn);
                }
            }
        }

        void ShowLoad()
        {
            if (lastLoad == null)
            {
                lblLoad.Text = Tr.T("Последний запуск игры: нет данных", "Last game start: no data");
                lblLoad.Color = Gui.Muted;
                Tips.Set(lblLoad, Tr.T("После выхода из игры здесь появится, сколько шла загрузка и сработал ли кэш.",
                    "After you quit the game, this line shows how long loading took and whether the cache worked."));
                return;
            }
            bool good = lastLoad.Data == LoadLog.DataState.Hit && (lastLoad.Headless || lastLoad.Atlas == LoadLog.AtlasState.Hit);
            lblLoad.Text = lastLoad.Line(Tr.En);
            lblLoad.Color = good ? Gui.Green : Gui.Orange;
            Tips.Set(lblLoad, lastLoad.Full(Tr.En));
        }

        // ------------------------------------------------------------------ version line and report

        /// <summary>The version line in the given language; null when there is nothing to describe.</summary>
        async Task<string> BuildLineAsync(bool en)
        {
            if (engine == null || modsDir == null) return null;
            if (installed == null && (found == null || !found.Present || found.Fingerprint == null)) return null;
            if (busy) return null;
            SetBusy(true);
            cancelable = false;
            btnCancel.IsEnabled = false;
            try
            {
                List<string> foreign = null;
                var mods = modsDir;
                // Keep the build and the shared git runner unchanged until the line is complete.
                try { foreign = installed != null ? await Task.Run(() => engine.ForeignEntriesAsync(new SyncPaths(mods))) : null; } catch { }
                return VersionLine.Build(installed, installedDate, mods, foreign, modified, found != null ? found.Fingerprint : null, en);
            }
            finally { SetBusy(false); }
        }

        async Task CopyCommit()
        {
            string text, ru;
            try { text = await BuildLineAsync(Tr.En); ru = Tr.En ? await BuildLineAsync(false) : text; }
            catch (Exception ex) { lblDetail.Text = Tr.T("Не удалось собрать строку версии: ", "Could not build the version line: ") + ex.Message; return; }
            if (text == null) return;
            AppendLog(ru, LogKind.Info);
            if (Clipboard == null)
            {
                lblDetail.Text = Tr.T("Не удалось скопировать: буфер обмена недоступен. Строка есть в «Журнале»", "Could not copy: the clipboard is not available. The line is in the Journal");
                return;
            }
            try
            {
                await Clipboard.SetTextAsync(text);
                lblDetail.Text = Tr.T("Строка скопирована и есть в «Журнале»: сверьте её с хостом или вставьте в сообщение о баге", "The line is copied and is in the Journal: compare it with the host or paste it into a bug report");
            }
            catch (Exception ex) { lblDetail.Text = Tr.T("Не удалось скопировать: ", "Could not copy: ") + ex.Message; }
        }

        /// <summary>The state of the window, in Russian, for the report.</summary>
        string StateText()
        {
            var sb = new StringBuilder();
            sb.Append("Линия сборки: ").Append(Lines.Current.Name).Append(" (ветка ").Append(Lines.Current.Branch).Append("), нужна ").Append(Lines.Current.RequiredGame)
              .Append(remoteReq != null ? ", последняя сборка требует " + remoteReq : "").Append('\n');
            if (Lines.Current.SpaceAge) sb.Append("Профиль Space Age: ").Append(GameProfile.SpaceAgeRoot(fac) ?? "?").Append('\n');
            if (disk != null && disk.Present)
                sb.Append("Сборка на диске: zzzparanoidal ").Append(disk.ZzzVersion ?? "?").Append(", factorio_version ").Append(disk.FactorioVersion ?? "?")
                  .Append(", base >= ").Append(disk.BaseMin ?? "?").Append(disk.OtherLine ? " (другая линия: нужен переход)" : "").Append('\n');
            sb.Append("Установлена версия: ").Append(installed == null ? "не записана" : AppInfo.Short(installed) + " от " + (installedDate ?? "?")).Append('\n');
            if (found != null && found.Present)
            {
                sb.Append("В папке mods: zzzparanoidal ").Append(found.ZzzVersion ?? "?").Append(", factorio_version ").Append(found.FactorioVersion ?? "?");
                if (found.Fingerprint != null) sb.Append(", отпечаток файлов ").Append(found.Fingerprint);
                if (found.Near != null) sb.Append(", ближе всего версия ").Append(AppInfo.Short(found.Near)).Append(" от ").Append(found.NearDate)
                    .Append(" (отличаются ").Append(found.NearDiff).Append(" из ").Append(found.NearTotal).Append(": ").Append(string.Join(", ", found.NearMods)).Append(')');
                sb.Append(", сравнено с известными версиями: ").Append(found.Known).Append('\n');
            }
            sb.Append("Последняя версия на GitHub: ").Append(remote == null ? (offline ? "нет связи" : "не проверено") : AppInfo.Short(remote) + (remoteDate != null ? " от " + remoteDate : "")).Append('\n');
            sb.Append("Изменено файлов сборки: ").Append(modified < 0 ? "не проверено" : modified.ToString()).Append('\n');
            sb.Append("Не менять версию: ").Append(pinSetting ? "да" : "нет").Append('\n');
            sb.Append("Откат: ").Append(rollbackTarget == null ? "нет" : (rollbackNewer ? "вперёд на " : "назад на ") + AppInfo.Short(rollbackTarget) + " от " + rollbackDate).Append('\n');
            sb.Append("Кэш загрузки (галочка лаунчера): ").Append(chkCache.Checked ? "включается" : "не трогается").Append('\n');
            sb.Append("Лаунчер: ").Append(AppInfo.Version).Append(", папка ").Append(AppInfo.ExeDir).Append(AppInfo.ExeDirWritable ? "" : " (только чтение)")
              .Append(", последняя версия на GitHub: ").Append(launcherRel == null ? "не проверено" : launcherRel.Version).Append('\n');
            sb.Append("Гайд по сборке: ").Append(guideLocal == null ? "не скачан" : (guideLocalVersion ?? "?") + " (" + guideLocal + ")")
              .Append(", последняя версия: ").Append(guideRel == null ? "не проверено" : guideRel.Version).Append('\n');
            return sb.ToString();
        }

        async Task DoReport()
        {
            var inp = await Dialogs.AskReport(this, fac);
            if (inp == null) return;
            inp.Fac = fac;
            inp.ModsDir = modsDir;
            try { inp.VersionLine = await BuildLineAsync(false); } catch (Exception ex) { inp.VersionLine = "не собрана: " + ex.Message; }
            inp.State = StateText();
            inp.Journal = rtbLog.Text;
            inp.LoadDiag = lastLoad != null ? lastLoad.Full(false) : null;
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            inp.OutFile = Path.Combine(!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop) ? desktop : AppInfo.DataDir,
                "Paranoidal-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
            var mods = modsDir;
            bool haveBuild = installed != null;
            RunOp(async () =>
            {
                Stage("Собираю отчёт о проблеме…", "Building the problem report…");
                Progress(null, "");
                if (mods != null && engine != null && haveBuild)
                {
                    var p = new SyncPaths(mods);
                    try { inp.ModifiedFiles = await engine.ChangedBuildFilesAsync(p); } catch { }
                    try { var fe = await engine.ForeignEntriesAsync(p); if (fe != null) inp.ForeignMods = VersionLine.Describe(mods, fe); } catch { }
                }
                return Report.Create(inp);
            }, async res =>
            {
                Idle(Tr.T("Отчёт готов", "The report is ready"));
                lblDetail.Text = res.Zip + " (" + Fmt.Size(res.Size) + ")";
                AppendLog("Отчёт о проблеме: " + res.Zip + " (" + Fmt.SizeRu(res.Size) + ", файлов " + res.Entries.Count + ")" +
                    (res.SaveAdded != null ? ", сейв " + Path.GetFileName(res.SaveAdded) : "") + (res.SaveSkippedRu != null ? "; сейв не добавлен: " + res.SaveSkippedRu : ""), LogKind.Ok);
                if (res.SaveSkippedRu != null)
                    await DarkDialog.Info(this, Tr.T("Отчёт о проблеме", "Problem report"), Tr.T("Архив создан, но сейв не добавлен: ", "The archive is ready, but the save was not added: ") +
                        Tr.T(res.SaveSkippedRu, res.SaveSkippedEn) + ".", Gui.Orange);
                if (!Auto) { try { Shell.Reveal(res.Zip); } catch { } }
            });
        }

        // ------------------------------------------------------------------ folders

        async Task OpenMods()
        {
            if (modsDir == null || !Directory.Exists(modsDir)) return;
            try { Shell.OpenFolder(modsDir); }
            catch (Exception ex) { await DarkDialog.Info(this, Tr.T("Папка модов", "Mods folder"), Tr.T("Не удалось открыть папку: ", "Could not open the folder: ") + ex.Message + "\n" + modsDir, Gui.Red); }
        }

        async Task PickFactorio()
        {
            var dir = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Tr.T("Выберите папку ", "Choose the folder of ") + AppInfo.RequiredGame + Tr.T(" (в ней есть ", " (it has ") + FactorioInfo.ExeRelative + ")",
                SuggestedStartLocation = await StorageProvider.StartAt(fac != null ? fac.Dir : null),
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
                await DarkDialog.Info(this, Tr.T("Версия Factorio", "Factorio version"), f.Problem(Lines.L21) + Tr.T("\n\nЛаунчер остаётся с прежней игрой.", "\n\nThe launcher stays with the previous game."), Gui.Red);
                return;
            }
            if (fac != null && Relocation.SameDir(f.Dir, fac.Dir)) return;
            AppendLog("Выбрана Factorio: " + f.Dir + " (" + f.Describe(false) + ")", LogKind.Ok);
            fac = f;
            if (await MaybeRelocate(null, true)) return;   // the copy in that game's folder takes over
            ForgetOtherGame();
            Settings.SetPath("factorio", f.Dir);
            // the Space Age profile belongs to a game folder: without one there the window goes back to 2.1
            ApplyLine(Lines.Current.SpaceAge && !GameProfile.SpaceAgeExists(f) ? Lines.L21 : Lines.Current);
        }

        async Task PickMods()
        {
            if (Lines.Current.SpaceAge) { await PickSpaceAgeFolder(null); return; }
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Tr.T("Выберите папку mods, которую нужно держать в соответствии со сборкой", "Choose the mods folder to keep in line with the build"),
                SuggestedStartLocation = await StorageProvider.StartAt(modsDir ?? (fac != null ? fac.Dir : null)),
            }).LocalPath();
            if (picked == null) return;
            var sel = picked.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Path.GetFileName(sel).Equals("mods", StringComparison.OrdinalIgnoreCase))
            {
                var r = await DarkDialog.Ask(this, Tr.T("Папка модов", "Mods folder"), Tr.T("Папка модов должна называться mods.", "The mods folder must be named mods."),
                    Tr.T("Использовать папку\n", "Use the folder\n") + Path.Combine(sel, "mods") + Tr.T("\n(она будет создана при обновлении)?", "\n(it is created by the update)?"),
                    null, Tr.T("Да", "Yes"), Tr.T("Нет", "No"), Gui.Title);
                if (r != DialogResult.Yes) return;
                sel = Path.Combine(sel, "mods");
            }
            var err = SyncPaths.Validate(sel);
            if (err != null) { await DarkDialog.Info(this, Tr.T("Папка модов", "Mods folder"), err, Gui.Red); return; }
            modsDir = sel;
            Settings.SetPath("mods", modsDir);
            AppendLog("Папка модов: " + modsDir, LogKind.Info);
            UpdateUi();
            CheckStatus();
        }

        /// <summary>
        /// «Выбрать…» on the Space Age tab: the folder where the profile Paranoidal-SpaceAge lies; the whole profile moves
        /// there (a rename on the same disk, a copy on another one). An existing profile there is used after a question;
        /// without an installed build only the place is remembered. `preset`: tests (PARANOIDAL_LAUNCHER_TEST_SA_FOLDER).
        /// </summary>
        async Task PickSpaceAgeFolder(string preset)
        {
            if (busy || fac == null || engine == null) return;
            if (FactorioInfo.IsRunning())
            {
                await DarkDialog.Info(this, Tr.T("Factorio запущена", "Factorio is running"), Tr.T("Закройте игру и выберите папку снова.", "Close the game and choose the folder again."), Gui.Orange);
                return;
            }
            var from = GameProfile.SpaceAgeRoot(fac);
            var dir = preset ?? await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Tr.T("Выберите папку, в которой будет лежать профиль Space Age (папка " + GameProfile.SpaceAgeFolder + ")",
                    "Choose the folder for the Space Age profile (the " + GameProfile.SpaceAgeFolder + " folder)"),
                SuggestedStartLocation = await StorageProvider.StartAt(from != null ? Path.GetDirectoryName(from) : fac.Dir),
            }).LocalPath();
            if (dir == null || from == null) return;
            var f = fac;
            // tests: the launcher closes when the choice ends without a move (with one it closes after the status check)
            Action end = () => { if (Auto) CloseLater(); };
            RunOpSync(() => GameProfile.PlanMove(from, dir, GameProfile.MainMods(f), f.Dir), async plan =>
            {
                if (plan.Same)
                {
                    AppendLog("Профиль Space Age уже лежит в " + plan.To + ".", LogKind.Muted);
                    await DarkDialog.Info(this, Tr.T("Профиль Space Age", "Space Age profile"), Tr.T("Профиль уже лежит здесь: ", "The profile is here already: ") + plan.To, Gui.Green);
                    end();
                    return;
                }
                if (plan.Error != null)
                {
                    AppendLog("Профиль Space Age не перенесён в " + plan.To + ": " + plan.Error, LogKind.Warn);
                    await DarkDialog.Info(this, Tr.T("Профиль Space Age", "Space Age profile"), Tr.T(plan.Error, plan.ErrorEn), Gui.Red);
                    end();
                    return;
                }
                if (plan.TargetIsProfile)
                {
                    if (!await Dialogs.ConfirmUseProfile(this, plan)) { end(); return; }
                    Settings.SetPath("sa-root", plan.To);
                    AppendLog("Профиль Space Age: теперь используется " + plan.To + ", прежний " + plan.From + " остался на месте.", LogKind.Ok);
                    ApplyLine(Lines.SA);
                    return;
                }
                if (!plan.FromExists)
                {
                    // nothing installed yet: «Установить» puts the profile there
                    Settings.SetPath("sa-root", plan.To);
                    AppendLog("Профиль Space Age будет установлен в " + plan.To + ".", LogKind.Ok);
                    ApplyLine(Lines.SA);
                    return;
                }
                if (!await Dialogs.ConfirmProfileMove(this, plan)) { end(); return; }
                autoCloseAfterUpdate = Auto;   // tests: a failed move closes the launcher too
                AppendLog("Переношу профиль Space Age: " + plan.From + " → " + plan.To + " (" + Fmt.SizeRu(plan.Bytes) + (plan.SameVolume ? ", тот же диск" : ", другой диск") + ").", LogKind.Info);
                RunOpSync(() =>
                {
                    CanCancel(false);
                    Stage("Переношу профиль Space Age…", "Moving the Space Age profile…");
                    Progress(null, Tr.T("Не закрывайте программу.", "Do not close the launcher."));
                    try { GameProfile.Move(plan, Progress, l => Log("Профиль Space Age: " + l + ".", LogKind.Muted)); }
                    catch (Exception ex)
                    {
                        throw new SyncException("Профиль Space Age не перенесён: " + ex.Message + "\nПрофиль остался в " + plan.From + ".",
                            "The Space Age profile was not moved: " + ex.Message + "\nThe profile stays in " + plan.From + ".");
                    }
                    return true;
                }, _ =>
                {
                    AppendLog("Профиль Space Age перенесён: " + plan.To + ".", LogKind.Ok);
                    ApplyLine(Lines.SA);
                    lblDetail.Text = Tr.T("Профиль Space Age перенесён в ", "The Space Age profile moved to ") + plan.To;
                });
            });
        }

        void CancelOp()
        {
            if (!busy || !cancelable) return;
            btnCancel.IsEnabled = false;
            lblStage.Text = Tr.T("Отменяю…", "Cancelling…");
            opCancel.Cancel();
            if (git != null) Task.Run(() => git.Cancel());
        }

        // ------------------------------------------------------------------ closing

        /// <summary>
        /// The window closes; during an operation it asks first. Avalonia asks Closing synchronously and a nested dialog
        /// there is fragile, so the close is cancelled, the question is posted and an accepted close comes again with
        /// closeConfirmed.
        /// </summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (Auto && !closeConfirmed)
                try { File.AppendAllText(TestAuto, "=== " + AppInfo.Version + " pid " + Environment.ProcessId + " " + SelfUpdate.CurrentExe + Environment.NewLine + rtbLog.Text.Replace("\n", Environment.NewLine) + Environment.NewLine); } catch { }
            if (restarting || closeConfirmed || !busy) { base.OnClosing(e); return; }
            e.Cancel = true;
            Dispatcher.UIThread.Post(async () =>
            {
                if (!busy) { closeConfirmed = true; Close(); return; }
                if (!cancelable)
                {
                    await DarkDialog.Info(this, Tr.T("Подождите", "Please wait"), Tr.T("Дождитесь завершения текущей операции, затем закройте программу.",
                        "Wait for the current operation to finish, then close the launcher."), Gui.Orange);
                    return;
                }
                var r = await DarkDialog.Ask(this, Tr.T("Идёт обновление", "An update is running"), Tr.T("Прервать обновление и закрыть программу?", "Stop the update and close the launcher?"),
                    Tr.T("Файлы модов не изменятся, уже скачанное сохранится.", "The mod files do not change; what was downloaded is kept."),
                    null, Tr.T("Прервать и закрыть", "Stop and close"), Tr.T("Продолжить", "Continue"), Gui.Title, ButtonKind.Danger);
                if (r != DialogResult.Yes) return;
                opCancel.Cancel();
                if (git != null) await Task.Run(() => git.Cancel());
                closeConfirmed = true;
                Close();
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            closed = true;
            gameWatch.Stop();
            base.OnClosed(e);
        }
    }
}
