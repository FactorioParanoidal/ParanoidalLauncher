# Paranoidal Launcher: instructions for agents

Read this file in full before you change anything, then the sections of `README.md` that cover the code you touch.
`README.md` (Russian, kept short) is the contract of every feature; this file adds the working rules, the decisions behind the
design and the plans. Reply to the user in the language they write in (the team speaks Russian). Code, comments and
these documents are in English; texts players see are RU/EN; `README.md` and `CHANGELOG.md` are Russian.

## 1. What it is

- One launcher for players of the Factorio modpack **Paranoidal** (repository `FactorioParanoidal/ParanoidalModpack`;
  `AppInfo.Owner/RepoName`), built from one code base for Windows (a single exe) and Linux (in progress, §8).
  Since 1.5.0 it serves two **builds** of one game
  (`src/Lines.cs`, `settings.ini` `line=`): Paranoidal 2.1 (branch `pre_release_2.1`, Factorio 2.1.x not older than the
  `base >= …` of zzzparanoidal, DLC incl. recycler) and Paranoidal 2.1 Space Age (`2.1-sa`, branch
  `pre_release_2.1_space_age`) in a profile of its own (`src/Profile.cs`, rule 19). The player picks one in the start
  window or with the build tabs (rule 16). Builds for Factorio 2.0 are not served (1.3.0 had the 2.0 line, 1.4.0 moved
  its players; 1.4.0 was never released).
- It installs and updates the game's `mods\` from the branch of the build, checks the game and starts it with
  `--mod-directory`.
- Players have no git, no tokens and no technical skills. "Put the exe next to the game and press one button" is
  the whole user story; every feature must keep it that way.
- Stack (since 1.6.0, issue #3): C# on **.NET 10** with **Avalonia 12** for the window, built with the `dotnet` SDK from
  plain `.csproj` files. The release is a self-contained single-file exe: players install nothing. NuGet packages:
  only Avalonia (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Simple`; `Avalonia.Headless` in `tools/UiShot`).
  Git: on Windows MinGit is embedded as a resource and unpacked on first start; on Linux the system git is used
  (a clear error when it is missing). Up to 1.5.0 the launcher was .NET Framework 4.8 WinForms compiled by `csc.exe`.
- Licence: MIT (`LICENSE`); MinGit is GPLv2 (`vendor/`).

## 2. File map

| Path | Role |
|---|---|
| `src/ParanoidalLauncher.csproj` | the launcher: every `.cs` under `src/` (engine `src/*.cs`, window `src/UI/*.cs`; no Compile lists), Avalonia packages, embedded assets (MinGit only for `win-*`), single-file publish settings |
| `src/AppInfo.cs` | version (single source), repo, Discord link, data folder, `Settings` (`settings.ini`), `MaskUser` |
| `src/Lines.cs` | builds 2.1 / 2.1-sa: branch, game (`Game`, `MinGame`, `Fits`), DLC and their `mod-list.json` states; `Lines.Current`, `Lines.Key` (per-build setting keys) |
| `src/Profile.cs` | `GameProfile`: config.ini and write-data of the line's game (Space Age: own folder, `--config`), the first switch to Space Age (plan, copies, base from the 2.1 build) |
| `src/Sync.cs` | `SyncEngine`: status, update, «Проверить файлы», parallel blob download, extra mods, start preparation |
| `src/Git.cs` | runs git asynchronously (`RunAsync`; bundled MinGit on Windows, system git elsewhere) with an isolated environment, kill handle for the download watchdog, the operation's cancel token |
| `src/Http.cs` | the one `HttpClient` (anonymous GET, gzip, system proxy) with per-request time limits; `HttpFailure` |
| `src/GitHubApi.cs` | anonymous read-only REST: change list, file sizes for batching, short commit id → full; offline cache; `RemoteBuild` (zzzparanoidal of a branch/commit from raw.githubusercontent.com) |
| `src/Recognize.cs` | recognising a build already in `mods\` by content (no network needed) |
| `src/Rollback.cs` | one step back/forward without internet (difference only) |
| `src/VersionLine.cs` | «Скопировать версию» line, parsing the host's line, `mod-settings.dat` reader/fingerprint |
| `src/Factorio.cs` | finding installations (`PARANOIDAL_LAUNCHER_TEST_GAMES` for tests), `ExeRelative` (`bin/x64/factorio[.exe]`), game version and DLC check per build, where to get 2.1 |
| `src/ModList.cs` | DLC entries of `mod-list.json` (`JsonNode`, other fields kept), `ModList.Read` and the JSON options for the game's files |
| `src/GameConfig.cs` | load cache keys in the `config.ini` the game reads; warning about `~` dependencies |
| `src/LoadDiag.cs` | load diagnostics from `factorio-current/previous.log` |
| `src/Report.cs` | «Отчёт о проблеме» ZIP |
| `src/Releases.cs` | release.json of the launcher's and the guide's GitHub Releases, verified downloads (size + SHA-256) |
| `src/SelfUpdate.cs` | self-update file swap (exe -> .old, new -> exe, restart), elevated helper `--install-exe`, `StartArgs` |
| `src/Relocation.cs` | moving the launcher into the game folder (`ForOtherGame`: the copy forgets another game's settings), cleanup of the old place, `Shortcuts` (IShellLink, Windows) |
| `src/Guide.cs` | the one-file guide next to the exe: local version, install, open |
| `src/Tr.cs` | RU/EN: `Tr.T("русский", "English")` at the place of use |
| `src/UI/Program.cs` | entry point: helper mode, single instance (mutex), crash log, Avalonia start; assembly attributes from `AppInfo`, `InternalsVisibleTo` Harness and UiShot |
| `src/UI/App.cs` | the Avalonia application: dark Simple theme under the controls drawn in code, game-style tooltips |
| `src/UI/MainWindow.cs` | the window: construction, the build tabs, layout on the 4/8/12 px grid, texts, journal, `ISyncUi`, `RunOp` |
| `src/UI/MainWindow.Flow.cs` | the start (game search, relocation, `StartPanel` views), status check, releases and guide, self-update, Space Age install |
| `src/UI/MainWindow.Actions.cs` | update, «Проверить файлы», rollback, host's version, game start, load diagnostics, version line, report, folders, closing |
| `src/UI/StartScreen.cs` | `LogoArt` (the animated logo, one GIF for every view), `BuildCard`, `LineTab`, `StartPanel` (choice of the build / no suitable Factorio / wait) |
| `src/UI/Gui.cs` | the vanilla Factorio look (rule 20): colours of the 2.1.24 GUI atlas, fonts, drawing helpers (text, frames, bevels, status dots) |
| `src/UI/Controls.cs` | `Lay.Place` (absolute layout), `DrawCanvas` (a Canvas that paints itself: `Panel.Render` is sealed in Avalonia 12), `FButton`, `Card` (pane), `Lbl`, `StatusLine`, `FCheck`/`FRadio`, `HeaderPanel`, `FProgress`, `LogBox`, `FList`, `Tips` |
| `src/UI/DarkDialog.cs`, `src/UI/Dialogs.cs` | dialog windows of the game's look, awaited (`await DarkDialog.Ask(...)`, the owner is modal meanwhile); dialogs with input (report, host's version, choice of installation, Space Age, profile move) |
| `src/UI/Native.cs` | `PickerPaths` (start folder and local path for the StorageProvider pickers called in place), `Shell` (open links, folders), Discord mark, `SingleInstance` (named pipe, async listener) |
| `tools/Harness/` | console driver of the engine (`Harness.cs`, usage at its top); references the launcher project |
| `tools/UiShot/` | dialogs, start views and the main window rendered to PNG by Avalonia.Headless + Skia (no window on the desktop); references the launcher project |
| `tools/testdata/`, `tools/release-manifest.ps1` | reference logs; release.json writer (PowerShell) |
| `.github/workflows/build-windows.yml` | CI: every push and pull request publishes the win-x64 exe on a Windows runner, runs the `build.ps1` checks (version, CHANGELOG, MinGit SHA-256, version resource) and the LoadDiag self-test, uploads the exe + `build-info.txt` as a workflow artifact (30 days); never releases |
| `build.ps1`, `publish.ps1`, `tools/snap.ps1` | the 1.5.0 build and release scripts (csc, .NET Framework 4.8): obsolete for the new stack, replaced in the port (§3, §8) |
| `docs/factorio-load-cache.md` | measured behaviour of the 2.0.77 load cache: the basis of `LoadDiag` and `GameConfig` |
| `assets/` | `paranoidal-logo.gif` (animated logo), `space-age-plate.png` (from the game's Space Age logo), `gear.png`, `app.ico`; embedded by the csproj |
| `vendor/` | MinGit zip (embedded only into Windows builds) and its licence |
| `app.manifest` | Windows manifest of the exe (embedded into the apphost by `dotnet publish -r win-x64`) |

## 3. Versions, build, release

- The rule is in `README.md` → «Версии»: **every change that produces a different exe raises `AppInfo.Version`** (its
  only place) and adds a Russian section at the top of `CHANGELOG.md` (`## x.y.z — YYYY-MM-DD`). Patch for fixes and
  small features, minor for larger ones. Comments and documents do not change the exe and need no version. The
  assembly attributes (`src/UI/Program.cs`) and the version resource of the exe (ProductVersion, read by the
  self-update) come from `AppInfo`.
- Build (Windows or Linux, .NET 10 SDK; set `AVALONIA_TELEMETRY_OPTOUT=1` and `DOTNET_CLI_TELEMETRY_OPTOUT=1`):
  - development: `dotnet build src/ParanoidalLauncher.csproj -c Release` → `src/bin/Release/net10.0/`;
  - Windows release exe: `dotnet publish src/ParanoidalLauncher.csproj -c Release -r win-x64 -o dist/win-x64` → one
    self-contained `ParanoidalLauncher.exe` with MinGit, icon, manifest and version resource (also when built on
    Linux). Do not ship the native `.pdb` files that land next to it;
  - Linux: `-r linux-x64` (packaging is §8).
- The checks `build.ps1` made for 1.5.0 (version format, CHANGELOG starts with the version, MinGit SHA-256, one
  version = one exe via `dist/build-info.txt`) move into MSBuild targets of the csproj (§8). Until then check them by
  hand before giving an exe to anyone; the CI workflow (`.github/workflows/build-windows.yml`) runs them on every push. Whether `dotnet publish` single-file output is byte-for-byte reproducible is not
  verified yet.
- `dist/`, `obj/`, `bin/` are not in Git. A release = a GitHub Release `vX.Y.Z` with `ParanoidalLauncher.exe` and
  `release.json` (version, size, SHA-256, every CHANGELOG section; format in README «Где лаунчер ищет обновления»),
  marked latest, notes = the CHANGELOG section. Releases of this repository are made only on the owner's command:
  launchers 1.2.0+ update themselves from `releases/latest/download/release.json`, so a release reaches every player.
  `publish.ps1` still drives the 1.5.0 build; until it is rewritten (§8) no release of this repository is made.
  Test builds of the port go out as pre-releases of a contributor's fork, never here.
- The guide (`FactorioParanoidal/guide-to-the-paranoidal`) is released by its own CI on every new `guide_version`;
  the launcher only reads it. Formats and URLs: README «Где лаунчер ищет обновления».

## 4. Decisions you must not undo without the team

Each one was chosen after a real problem; the reasons are what you need when a "simpler" idea comes up.

1. **Git, not per-file HTTP.** The first version downloaded files one by one from `raw.githubusercontent.com`. With
   thousands of files it hit GitHub limits (429), and a broken run left `mods\` half old, half new. Git transfers a
   compressed pack and only switches files at the end. The launcher keeps a partial shallow clone with sparse checkout
   of `/mods/` (no second 1.2 GB copy in `.git`, only the difference is downloaded). Details: README «Как хранится
   папка модов».
2. **Download in parallel batches with a watchdog** (`Sync.cs`, ~24 MB per request): an early version shrank the batch
   after a failure and never grew it back, so the speed decayed over time. A stalled or sharply slowed request is
   killed and asked again; finished requests stay on disk, so a cancelled update continues.
3. **Byte-for-byte files** (`info/attributes`: `* -text`). Factorio compares mods byte for byte in multiplayer; a CRLF
   copy made by a git client with `autocrlf` caused «mod content differs» between players. Never let git normalise
   line endings in `mods\`.
4. **The launcher never writes into a mod** and the modpack is not changed for the launcher (recognition works by
   content fingerprint, not by a marker file in `zzzparanoidal`).
5. **Player files are not touched**: saves, `mod-settings.dat`, `player-data.json`, zip mods, mods the player chose to
   keep. Only the DLC entries of `mod-list.json` are set (quality and elevated-rails on, 2.1 also recycler, space-age
   off: the main build is played without Space Age content; the Space Age profile switches all four on) and only the
   three cache keys of the game `config.ini` (with one backup). The exception the player confirms: the first switch
   to Space Age (item 19) copies some of them into the profile, never overwrites. Saves of a 2.0 game are not moved
   (owner, 2026-11-10).
6. **One question, no jargon.** No branch names or git words in the window; an update asks once and shows what changes.
7. **Journal, `sync-log.txt`, `crash.txt` and the report stay Russian** in both interface languages: the team reads
   them. Default interface language: Russian for the 15 post-Soviet countries (UI language or region), else English.
8. **Report ≤ 20 MB** (the Discord upload limit), never `player-data.json` (Factorio account token), user name in paths
   masked. Save options: latest, latest autosave, chosen file; a save that does not fit is reported, not cut.
9. **Rollback keeps only the difference** of one step and goes back to the version the game was last *played* with;
   it works offline and «Вернуть» goes forward again. «Не менять версию» and «Версия хоста…» cover hosts that did not
   update yet, saves broken by an update and series on one version.
10. **Offline is a normal state**: «Играть», rollback, report, diagnostics and recognition must work without GitHub;
    network waits are short (20 s for the head check).
11. **Data next to the exe** (`ParanoidalLauncher-data`), relative paths inside the exe folder: a whole game folder can
    be copied to another computer or over the LAN and keeps working.
12. **Self-update asks every time** (owner's decision, 2026-10-28): all versions newer than the installed one with
    their changes and a recommendation; «Обновить сейчас» / «Позже», no "never ask" option. Downloads are checked
    by size, SHA-256 and the exe's version before the swap; the swap renames, never overwrites, a running exe.
13. **The launcher lives in the game folder** (owner's decision, 2026-10-28): it moves itself there silently (exe,
    data without MinGit, guide; desktop shortcut; the copy removes the old place) and asks for administrator rights
    only when that fails, recommending to move the game out of Program Files. Only the files the launcher owns are
    removed from the old place. Off for tests and the developer's `dist`/`obj` (`Relocation.DisabledReason`). Since
   1.5.0 the game of `settings.ini` is used only when it fits; otherwise every installation is searched, so a launcher
   that lies in a Factorio 2.0.77 folder moves into the 2.1 game, and its copy forgets the settings of the old game
   (`Relocation.ForOtherGame`, `MainWindow.ForgetOtherGame`). «Не сейчас» in the choice of installation keeps the
   launcher where it is and the next start asks again (`pick-install=1`; before 1.5.0 the next start moved silently
   into the game used meanwhile). The move, the shortcuts and the administrator path are Windows behaviour; what the
   launcher does on Linux (where games live in Steam, Flatpak or a tarball folder) is decided in the port (§8).
14. **The guide is a local file** next to the exe, updated together with the build (owner's decision): the button
    opens only the local copy; a guide update is offered with the build update, and logged as a highlighted line.
15. **The modpack's load cache.** The launcher turns the game's cache on before every start; any change of
    `mod-settings.dat` (also per-user settings changed in game) and any enabled mod with a `~` dependency make
    Factorio 2.0.77 rebuild the data stage (the `~` bug is in 2.1.24 too). Facts and log markers: `docs/factorio-load-cache.md`.
16. **Only Factorio 2.1; the start window** (owner, 2026-11-10, 1.5.0; README «Сборки и окно выбора»; replaces the
    2.0 line of 1.3.0 and the forced move of 1.4.0, which was never released). No word about 2.0 in the window.
    `line=2.0` of older launchers becomes 2.1 and its pin goes (`MainWindow.ForgetLine20`); a 2.0 build in the mods
    folder of a game updated to 2.1 is updated like any old build; saves of a 2.0 game are not moved. The start: the
    game (search, relocation), then the start window: no suitable game → what was found (2.1 without the expansion,
    an older game, nothing), where to get 2.1, «Закрыть» (closes the launcher) / «Выбрать папку игры…» / «Проверить
    снова»; otherwise two cards (2.1, Space Age «в активной разработке») and «Запомнить выбор» (off by default,
    `ask-build=0`). The main window has the build tabs under the header and «Спрашивать сборку при запуске» at
    their right; `line` remembers the last build. The approved mockup:
    https://expertlove.github.io/paranoidal-space-age/launcher.html (workspace `test-runs/launcher150-20261110/mockup/`).
    The branch `pre_release_2.0` stays only for launchers 1.2.0 and 1.3.0 whose players declined the self-update.
17. **Questions only about the player's files** (owner, 2026-11-04): folders the build itself removed or renamed are
    removed by the update without a question (and kept for the rollback: do not delete them before
    `SaveForRollback`); folders of any known build version and earlier «Оставить» answers about them go without a
    question; «Лишние моды» lists only what never was in the build.
18. **A second start shows the running launcher** (owner, 2026-11-04): the message «уже запущен» stays, the window of
    the first one is restored and brought to the front (since 1.6.0: a named mutex and a per-user named pipe,
    `SingleInstance` in `src/UI/Native.cs`; 1.5.0 used a registered window message).
19. **Space Age is a profile of its own** (owner, 2026-11-10, 1.5.0; README «Space Age»): chosen in the start window
    or with the tab «Paranoidal 2.1 Space Age», labelled "в активной разработке" (never "may break"). The profile is
    the folder `Paranoidal-SpaceAge`
    next to the main mods folder (rule 11), the game is started with `--config <profile>\config\config.ini` whose
    `[path]` write-data is rewritten to the profile before every start (an absolute path, as in the team's own SA
    setup). **Nothing is installed or updated without the player's «Установить»/«Обновить»** (owner, 2026-11-10):
    choosing a build in the start window or with a tab only opens it and checks its status. «Установить» on the
    Space Age tab without a profile opens one window with what is copied once (game settings with the profile's
    `[path]`, `mod-settings.dat`, `mod-list.json`, `player-data.json`, blueprints), «Скопировать сейвы основной сборки»
    as a check box with their number and size (off by default), the disk space; the installed 2.1 build is copied as
    the base (`SyncEngine.Seeded`) so only the difference downloads. «Выбрать…» of the mods folder on the Space Age
    tab moves the whole profile to `<chosen folder>\Paranoidal-SpaceAge` (owner, 2026-11-10; the default stays in the
    game folder): a rename on the same disk, a copy into `.partial` then the deletion of the old folder on another one;
    an existing profile there is used after a question (nothing overwritten); without an installed build only `sa-root`
    changes (`GameProfile.PlanMove`/`Move`, `Harness --sa-move`). Space Age is simply
    switched on in the profile's `mod-list.json` (both builds need the expansion: the launcher accepts no game without it).
    «Скопировать версию»/«Версия хоста» tell "2.1" from "2.1 Space Age" (bare ids: by the branch on GitHub).
20. **The window looks like a vanilla Factorio window** (owner, 2026-11-10, 1.5.0): made with the workspace skill
    `.pi/skills/factorio-gui-design` (colours sampled from the 2.1.24 GUI atlas, panes with a subheader band, 12 px
    spacing, grey buttons with black text, one green button for the next action, red for destructive answers, back on
    the left and confirm on the right in dialogs, status dots, sentence case, three text sizes; the logo header is the
    one distinctive element). The logo is the animated PARANOIDAL GIF made by the team (owner, 2026-11-10; no author
    credit), for Space Age with the SPACE AGE plate cut from the game's logo; no "2.1" in the logo. The controls are
    drawn in code (`src/UI/Gui.cs`, `Controls.cs`), the same on every OS. Folder choice is the system dialog
    (Avalonia's `StorageProvider`: the Explorer-style window on Windows, the desktop portal or GTK dialog on Linux), never the old
    "Browse for folder" tree. Every text the player sees follows the humanizer rules
    (https://github.com/blader/humanizer): no em/en dashes as connectors, no glyphs as decoration, no ALL CAPS, no
    "not X but Y", plain verbs. Check new texts with `test-runs/launcher150-20261110/tools/literals.py` of the workspace
    (string literals with dashes or glyphs) and new windows with UiShot pictures.

## 5. Testing

- **Harness first.** `dotnet build tools/Harness -c Release` → `tools/Harness/bin/Release/net10.0/Harness` drives the
  real engine without the window (usage at the top of `tools/Harness/Harness.cs`). Always set `PARANOIDAL_LAUNCHER_DATA_DIR` to a test folder and use a test mods
  folder; `PARANOIDAL_LAUNCHER_TEST_OFFLINE=1` simulates no connection. Exact copies of build versions for a stand:
  `git archive <commit> mods | tar -x` from a clone of the modpack.
- `Harness --loaddiag-selftest tools/testdata/load-logs-2.0.77.zip` must print `SELFTEST OK` after any change
  of `LoadDiag.cs`/`GameConfig.cs`.
- The start: end to end with `TEST_AUTO` + `PARANOIDAL_LAUNCHER_TEST_PICK=21|sa` (the start window) +
  `PARANOIDAL_LAUNCHER_TEST_GAMES=<fake games>|-` (the search sees only them: without it a test launcher that searches
  finds the real games and may move into them) + `PARANOIDAL_LAUNCHER_TEST_SHORTCUTS`; relocation on only with
  `TEST_GAMES`. Stands and script: `test-runs/launcher150-20261110/tools/e2e150b.py` of the workspace (pick 2.1, pick
  Space Age, remembered choice, the first Space Age install from the start window, a launcher in a 2.0.77 folder with
  a 2.1 game elsewhere, no suitable game). `Harness --other-game <settings.ini>` shows what a moved copy keeps.
- Space Age: `Harness --profile <game> [--prepare] --line=2.1-sa` (paths, `--config`, the `[path]` written) on a fake
  game folder; end to end `TEST_AUTO` + `PARANOIDAL_LAUNCHER_TEST_SWITCH=sa|21` (a tab) + `PARANOIDAL_LAUNCHER_TEST_INSTALL=1`
  (press «Установить»/«Обновить») + `NO_RELOCATE=1` on a copy of a 2.1 player's folder (`e2e150b.py`: browse,
  picksa0, switch21 check that a choice or a tab installs nothing; freshsa installs Space Age with «Установить»).
- Releases, self-update and relocation: a local fake GitHub (`python -m http.server` over
  `FactorioParanoidal/<repo>/releases/latest/download/release.json` + `releases/download/vX.Y.Z/<file>`, the manifest
  from `tools/release-manifest.ps1`, a second launcher built from a copy of the sources with a higher version) and
  `PARANOIDAL_LAUNCHER_TEST_GITHUB`. Engine parts through Harness (`--releases`, `--guide`, `--download-launcher`,
  `--swap`, `--relocate`, `--cleanup-old`, `--shortcuts`); restarts end to end with `PARANOIDAL_LAUNCHER_TEST_AUTO`
  (off-screen, no activation, journal to a file) and `PARANOIDAL_LAUNCHER_TEST_SHORTCUTS`, in a fake game folder
  under `test-runs/` with its own `settings.ini`. Never let a test launcher see the real installations without
  `settings.ini` pointing at the fake game: relocation would move it into the player's game folder, and with a `mods`
  folder there the status check would create `.paranoidal-git` in it. This holds for every test that starts the exe,
  also a short single-instance one (1.3.0 tests: one such start read the real 2.1.24 install, nothing was written
  because it has no `mods`). The UAC path cannot be automated: say so in the handoff. A build checked only on Linux was not run on Windows: say so too.
- Dialogs, the start window views and the main window of both builds: `dotnet build tools/UiShot -c Release`, then
  `UiShot <outDir> <ru|en> [factorio dir] [launcher release.json]` with `PARANOIDAL_LAUNCHER_DATA_DIR` set renders them
  to PNG with Avalonia.Headless + Skia, without a window on the desktop (`Dialogs.TestSnapshot`,
  `DarkDialog.TestSnapshot`). Review the pictures at 100 % against rule 20. Do not click the window with posted
  messages and never take the focus from a running game.
- `PARANOIDAL_LAUNCHER_TEST_AUTO` places the window off-screen without activation on Windows; on Linux (X11/XWayland,
  Wayland compositors) the window manager decides where a window goes, so a live run shows a window on the desktop.
  Run live tests on Linux only in a separate display (Xvfb) or when the person at the desktop agrees.
- Linux: the same isolation rules hold. Steam keeps Factorio in `~/.local/share/Steam/steamapps/common/Factorio`
  (Flatpak Steam under `~/.var/app/com.valvesoftware.Steam/`), write-data in `~/.factorio`: a test launcher must see
  only fake games (`PARANOIDAL_LAUNCHER_TEST_GAMES`). A fake game is `bin/x64/factorio` (a script that only logs its
  start) plus `data/{base,space-age,quality,elevated-rails,recycler}/info.json` with the game version.
- **Never** use the player's real game folder, `%APPDATA%\Factorio`, saves, `mod-list.json` or `mod-settings.dat` for
  tests, and never kill a game you did not start. Real game launches (e.g. new reference logs after a game update)
  only with the owner's permission, in an isolated copy with its own write-data (`--config`), closed with WM_CLOSE.
- What only a person can accept: real clicks and dialogs, the diagnostics line after a real game exit, Steam installs,
  copying between two computers, default language on a non-Russian Windows. Say so in the handoff; do not claim them.
- When the game version changes: re-measure the cache markers (`docs/factorio-load-cache.md`), update
  `BuildLine.DefaultMin` in `src/Lines.cs` (the 2.1 line also follows `base >= …` of zzzparanoidal) and the reference logs.

## 6. Ideas not done yet

Proposed by agents and discussed with the team; none is approved as a plan. Ask before starting one.

- **Code signing** of the exe: unsigned exes that replace themselves and create shortcuts are what antivirus
  heuristics dislike most; no certificate yet.
- **Backup of saves** before an update of the build (mod migrations are one-way), with size and age limits.
- **Connect to a server** (`--mp-connect`) with a comparison of the host's version line before connecting.
- **Disk space check** before an update and before the first cached start (build ~1 GB, atlas cache ~1 GB).
- **Video memory hint** from `Dedicated video memory size` in the game log (the modpack needs ~3.2 GB at the highest
  settings) with suggested `video-memory-usage`/graphics settings.
- **Cache warm-up** after an update (`--create` into a temp file builds the data cache without a window); measure first.
- **Startup settings that differ from the build defaults**, with a reset (players change them and cannot join the host).
- After-start behaviour (tray / close / nothing), a link to the modpack FAQ (Discord is a button since 1.3.0), starting
  Steam installs through Steam (`-applaunch 427520`) if direct start proves unreliable there.
- The owner declined a backup of saves for the move to 2.1 (2026-11-04); the idea above is about build updates only.
- A "2.1" drawn in the style of the animated logo, if its author makes one (owner, 2026-11-10: no "2.1" in the logo for now).

## 7. Working rules

- Read the code you change and its README section; keep README in sync with behaviour (it is the contract).
- Every message that a player sees goes through `Tr.T`; journal lines and exceptions follow §4.7.
- No dependencies beyond the Avalonia packages of the csproj (and Avalonia.Headless in UiShot), no installers, no
  network services; GitHub anonymous access only (no tokens on players' PCs). Avalonia's build tasks send anonymous
  build telemetry unless `AVALONIA_TELEMETRY_OPTOUT=1` is set; whether the csproj turns it off for everyone is a
  question for the team (§8).
- Do not publish, tag, push or upload a Release without the owner's explicit command. Commit titles in Russian,
  third-person verb, no trailing period ("Исправляет …"); list only checks that were really done.

## 8. Port to .NET 10 + Avalonia (in progress)

Issue #3; work branch `Dev-Avalonia-10NET` of a contributor's fork, merged here as one PR. Agreed in the team's
Discord (2026-11-15): first the whole launcher on modern .NET + Avalonia with Windows parity, then Linux features;
build with plain `.csproj` files only; Windows keeps the embedded MinGit, Linux uses the system git (no LibGit2Sharp:
it has no partial clone and sparse checkout, decision 1). Test builds only as pre-releases of the fork; the owner
decides when it replaces the current launcher.

Done:
- engine and Harness on net10.0, JSON with System.Text.Json (typed models, `JsonNode` where `mod-list.json` is
  rewritten); the whole window in `src/UI` with the look and behaviour of 1.5.0 (the WinForms files are deleted);
  Harness and UiShot in their project folders, referencing the launcher project; UiShot on Avalonia.Headless;
  version 1.6.0 with its CHANGELOG section;
- checked on Linux: builds, `SELFTEST OK`, UiShot RU/EN, a live run on a fake game stand (start window, tabs, GitHub
  status check, closing); the win-x64 exe is cross-built with its version resource, manifest and MinGit.
- async through the whole stack (branch `async-engine`, 1.6.2): git, HTTP, the engine (`SyncEngine.*Async`) and its
  questions (`ISyncUi.Confirm*` return `Task<bool>`) are awaited, no thread waits for another; `RunOp` runs async work
  on the thread pool, `RunOpSync` work without awaits (file copies, zip, game search). Only `Program.Main` before the
  window (waiting for the previous process) still blocks.

Before the merge:
- [ ] Windows run by a tester: first start (MinGit unpack), install and update of both builds, game start, rollback,
      report, folder dialogs, single instance, self-update 1.5.0 → 1.6.0 (the Avalonia exe as the update target),
      relocation and shortcuts, the UAC path by hand.
- [ ] MSBuild targets in the csproj for the checks of `build.ps1` (§3); then delete `build.ps1` and
      `tools/snap.ps1`; rewrite `publish.ps1` (or replace it) around `dotnet publish` and the manifest.
- [ ] README: «Версии», «Публикация», «Сборка и тесты», «Как выглядит окно» for the new stack.
- [ ] Team decision on Avalonia build telemetry (`AVALONIA_TELEMETRY_OPTOUT`).

Linux (after Windows parity):
- [ ] Game search: Steam native and Flatpak (`libraryfolders.vdf`), the factorio.com tarball, typical folders.
- [ ] Relocation, desktop shortcuts and the administrator path off or redesigned on Linux (`Relocation.DisabledReason`
      does not check the OS yet).
- [ ] Default language from `LANG`/`LC_*` like the Windows UI language/region rule (§4.7); the window position on
      Wayland compositors.
- [ ] Packages: self-contained linux-x64 first as AppImage, then Flatpak / AUR / deb / rpm (git as a dependency, or
      bundled inside AppImage/Flatpak).

