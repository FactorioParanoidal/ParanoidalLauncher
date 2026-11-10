using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Paranoidal
{
    /// <summary>
    /// Where the game of a line reads its config.ini and writes saves, logs and the load cache. Paranoidal 2.1 (and 2.0)
    /// use the game's own folders (config-path.cfg). The Space Age line (1.5.0) has a profile folder of its own,
    /// Paranoidal-SpaceAge next to the main mods folder (normally inside the game folder, so a copied game folder keeps
    /// it): mods, .paranoidal-git, config\config.ini, saves, logs and caches. The game is started with
    /// --config &lt;profile&gt;\config\config.ini, whose [path] write-data is the profile folder (written before every start,
    /// so a moved or copied game folder still points at its own profile). README «Space Age».
    /// </summary>
    sealed class GameProfile
    {
        public const string SpaceAgeFolder = "Paranoidal-SpaceAge";

        /// <summary>Profile folder of the Space Age line; null: the game's own folders.</summary>
        public string Root;
        public string ConfigPath, WriteData;

        public bool Own => Root != null;
        public string Mods => Root == null ? null : Path.Combine(Root, "mods");

        /// <summary>The profile of the line for this game; null without a game.</summary>
        public static GameProfile Of(FactorioInfo fac, BuildLine line)
        {
            if (line != null && line.SpaceAge)
            {
                var root = SpaceAgeRoot(fac);
                if (root == null) return null;
                return new GameProfile { Root = root, ConfigPath = Path.Combine(root, "config", "config.ini"), WriteData = root };
            }
            if (fac == null) return null;
            return new GameProfile { ConfigPath = GameConfig.ResolveConfigPath(fac.Dir, fac.Exe), WriteData = GameConfig.ResolveWriteData(fac.Dir, fac.Exe) };
        }

        /// <summary>The main mods folder of the 2.1 line: the one in settings.ini, else the game's mods folder.</summary>
        public static string MainMods(FactorioInfo fac) =>
            Settings.GetPath(Lines.Key("mods", Lines.L21)) ?? (fac != null ? Path.Combine(fac.Dir, "mods") : null);

        /// <summary>The Space Age profile folder: the one in settings.ini (sa-root), else next to the main mods folder.</summary>
        public static string SpaceAgeRoot(FactorioInfo fac)
        {
            // a game folder copied elsewhere: an old absolute path points nowhere, the profile is next to the mods folder again
            var saved = Settings.GetPath("sa-root");
            if (saved != null && Directory.Exists(Path.GetDirectoryName(saved) ?? saved)) return saved;
            var main = MainMods(fac);
            var parent = main == null ? null : Path.GetDirectoryName(main);
            return parent == null ? null : Path.Combine(parent, SpaceAgeFolder);
        }

        /// <summary>The Space Age build is in the profile already (installed or copied there).</summary>
        public static bool SpaceAgeExists(FactorioInfo fac)
        {
            var root = SpaceAgeRoot(fac);
            return root != null && File.Exists(Path.Combine(root, "mods", "zzzparanoidal", "info.json"));
        }

        /// <summary>The command line of the game: --config only for an own profile.</summary>
        public string GameArgs(string mods) =>
            (Own ? "--config " + FsUtil.QuoteArg(ConfigPath) + " " : "") + "--mod-directory " + FsUtil.QuoteArg(mods);

        /// <summary>The [path] section of the own profile: the game's data, the profile folder for everything it writes.</summary>
        string PathSection(string eol) =>
            "[path]" + eol + "read-data=__PATH__executable__/../../data" + eol + "write-data=" + Root.Replace('\\', '/') + eol + eol;

        /// <summary>
        /// Own profile, before every start: config.ini exists and its [path] points at this profile folder (a game folder
        /// moved or copied with the profile keeps working). The rest of the file stays as the game wrote it.
        /// </summary>
        public void PrepareConfig()
        {
            if (!Own) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            string text = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath, Encoding.UTF8) : "";
            var eol = text.Contains("\r\n") || text.Length == 0 ? "\r\n" : "\n";
            var updated = text.Length == 0 ? PathSection(eol) : GameConfig.MergeForOtherInstall(text, PathSection(eol));
            if (updated != text) File.WriteAllText(ConfigPath, updated, new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------ the first switch to Space Age

        /// <summary>What the first switch to Space Age copies, for its window.</summary>
        public sealed class SpaceAgePlan
        {
            public string Root, MainMods, MainWd, MainCfg;
            public int Saves;
            public long SavesBytes, BuildBytes, Free;
            /// <summary>Top-level entries of the installed main build (copied as the base: only the difference is downloaded).</summary>
            public List<string> Seed = new List<string>();
            /// <summary>Copied once: settings of the game and the mods, the login, the blueprints (names for the window).</summary>
            public List<string> Copies = new List<string>();
            public string Error, ErrorEn;
            /// <summary>Disk space the profile needs: the build, the saves if copied, ~1 GB of load cache, a margin.</summary>
            public long Need(bool saves) => BuildBytes + (saves ? SavesBytes : 0) + 1300L * 1024 * 1024;
        }

        /// <summary>Worker thread: what the first switch copies and how much space it needs.</summary>
        public static SpaceAgePlan PlanSpaceAge(FactorioInfo fac, GameProfile main, string mainMods, HashSet<string> buildEntries)
        {
            var p = new SpaceAgePlan { Root = SpaceAgeRoot(fac), MainMods = mainMods, MainWd = main != null ? main.WriteData : null, MainCfg = main != null ? main.ConfigPath : null };
            if (p.Root == null) { p.Error = "Не выбрана папка модов."; p.ErrorEn = "No mods folder is chosen."; return p; }
            try { Directory.CreateDirectory(Path.GetDirectoryName(p.Root)); } catch { }
            if (!AppInfo.CanWrite(Path.GetDirectoryName(p.Root)))
            {
                p.Error = "В папку " + Path.GetDirectoryName(p.Root) + " нельзя записывать без прав администратора (например, это Program Files). " +
                    "Перенесите папку игры в обычную папку, например C:\\Games\\Factorio.";
                p.ErrorEn = "The folder " + Path.GetDirectoryName(p.Root) + " cannot be written without administrator rights (e.g. Program Files). " +
                    "Move the game folder to a normal folder, e.g. C:\\Games\\Factorio.";
                return p;
            }
            if (mainMods != null && buildEntries != null && Directory.Exists(mainMods))
                foreach (var name in buildEntries.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var full = Path.Combine(mainMods, name);
                    if (!Directory.Exists(full)) continue;
                    p.Seed.Add(name);
                    try { p.BuildBytes += new DirectoryInfo(full).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { }
                }
            if (p.Seed.Count == 0) p.BuildBytes = 1000L * 1024 * 1024;   // a full download of the build
            if (p.MainWd != null)
            {
                try
                {
                    var saves = new DirectoryInfo(Path.Combine(p.MainWd, "saves"));
                    if (saves.Exists)
                        foreach (var f in saves.GetFiles("*.zip")) { p.Saves++; p.SavesBytes += f.Length; }
                }
                catch { }
            }
            if (p.MainCfg != null && File.Exists(p.MainCfg)) p.Copies.Add("config.ini");
            if (mainMods != null && File.Exists(Path.Combine(mainMods, "mod-settings.dat"))) p.Copies.Add("mod-settings.dat");
            if (mainMods != null && File.Exists(Path.Combine(mainMods, "mod-list.json"))) p.Copies.Add("mod-list.json");
            foreach (var f in PlayerFiles)
                if (p.MainWd != null && File.Exists(Path.Combine(p.MainWd, f))) p.Copies.Add(f);
            try { p.Free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(p.Root))).AvailableFreeSpace; } catch { p.Free = -1; }
            return p;
        }

        /// <summary>Copied once from the main game data: the login (multiplayer) and the blueprint library.</summary>
        static readonly string[] PlayerFiles = { "player-data.json", "blueprint-storage-2.dat" };

        /// <summary>
        /// Worker thread: creates the Space Age profile. Copies (never moves) the game settings with the [path] of the
        /// profile, mod-settings.dat, mod-list.json, the login, the blueprints, optionally the saves, and the folders of the
        /// installed main build as the base of the Space Age build (the update then downloads only the difference and
        /// removes what the Space Age build does not have). Files already in the profile are not overwritten.
        /// Returns the copied build folders.
        /// </summary>
        public static HashSet<string> CreateSpaceAge(SpaceAgePlan plan, bool copySaves, Action<double?, string> progress, Action<string> log)
        {
            var root = plan.Root;
            var mods = Path.Combine(root, "mods");
            Directory.CreateDirectory(mods);
            Directory.CreateDirectory(Path.Combine(root, "saves"));
            Settings.SetPath("sa-root", root);
            var prof = new GameProfile { Root = root, ConfigPath = Path.Combine(root, "config", "config.ini"), WriteData = root };

            if (!File.Exists(prof.ConfigPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(prof.ConfigPath));
                if (plan.MainCfg != null && File.Exists(plan.MainCfg))
                {
                    var text = File.ReadAllText(plan.MainCfg, Encoding.UTF8);
                    File.WriteAllText(prof.ConfigPath, GameConfig.MergeForOtherInstall(text, prof.PathSection(text.Contains("\r\n") ? "\r\n" : "\n")), new UTF8Encoding(false));
                    log("настройки игры: " + plan.MainCfg + " → " + prof.ConfigPath + " (пути [path] профиля Space Age)");
                }
            }
            prof.PrepareConfig();
            Action<string, string> copy = (src, dst) =>
            {
                if (src == null || !File.Exists(src) || File.Exists(dst)) return;
                File.Copy(src, dst);
                log(Path.GetFileName(src) + ": " + src + " → " + dst);
            };
            if (plan.MainMods != null)
            {
                copy(Path.Combine(plan.MainMods, "mod-settings.dat"), Path.Combine(mods, "mod-settings.dat"));
                copy(Path.Combine(plan.MainMods, "mod-list.json"), Path.Combine(mods, "mod-list.json"));
            }
            if (plan.MainWd != null)
                foreach (var f in PlayerFiles) copy(Path.Combine(plan.MainWd, f), Path.Combine(root, f));

            long total = plan.BuildBytes + (copySaves ? plan.SavesBytes : 0), done = 0;
            Action<long> step = n =>
            {
                done += n;
                progress(total > 0 ? Math.Min(1, done / (double)total) : (double?)null, Tr.T("Скопировано ", "Copied ") + Fmt.Size(done) + " / " + Fmt.Size(total));
            };
            if (copySaves && plan.MainWd != null)
            {
                var saves = new DirectoryInfo(Path.Combine(plan.MainWd, "saves"));
                int n = 0;
                if (saves.Exists)
                    foreach (var f in saves.GetFiles("*.zip"))
                    {
                        var dst = Path.Combine(root, "saves", f.Name);
                        if (!File.Exists(dst)) { f.CopyTo(dst); n++; }
                        step(f.Length);
                    }
                log("сейвы основной сборки: скопировано " + n + " в " + Path.Combine(root, "saves"));
            }
            var seeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in plan.Seed)
            {
                var dst = Path.Combine(mods, name);
                // left by an install that was interrupted: still the base (no build is installed in the profile yet)
                if (Directory.Exists(dst)) { seeded.Add(name); continue; }
                CopyDir(Path.Combine(plan.MainMods, name), dst + ".partial", step);
                Directory.Move(dst + ".partial", dst);
                seeded.Add(name);
            }
            if (seeded.Count > 0) log("основа сборки Space Age из сборки 2.1: модов " + seeded.Count + " (" + Fmt.SizeRu(plan.BuildBytes) + ")");
            return seeded;
        }

        // ------------------------------------------------------------------ another place of the Space Age profile

        /// <summary>A folder that holds a Space Age profile (a build, its git data or its config.ini).</summary>
        public static bool IsProfile(string dir) =>
            dir != null && (File.Exists(Path.Combine(dir, "mods", "zzzparanoidal", "info.json")) || Directory.Exists(Path.Combine(dir, ".paranoidal-git")) ||
                            File.Exists(Path.Combine(dir, "config", "config.ini")));

        /// <summary>
        /// «Выбрать…» of the mods folder on the Space Age tab (owner, 2026-11-10): the whole profile goes to
        /// &lt;chosen folder&gt;\Paranoidal-SpaceAge (the chosen folder itself when it is a profile named so).
        /// </summary>
        public sealed class MovePlan
        {
            public string From, To, Error, ErrorEn;
            /// <summary>The profile is there already (nothing to do).</summary>
            public bool Same;
            /// <summary>The current profile has files (otherwise only its place is remembered: the install goes there).</summary>
            public bool FromExists;
            /// <summary>The target already is a profile: the player is asked whether to use it; nothing is moved or overwritten.</summary>
            public bool TargetIsProfile;
            /// <summary>Same disk: a rename. Another disk: a copy into &lt;To&gt;.partial, then the old folder is deleted.</summary>
            public bool SameVolume;
            public long Bytes, Free;
            /// <summary>The profile leaves the folder of the game: a copied game folder no longer carries it.</summary>
            public bool OutsideGame;
        }

        static bool Inside(string dir, string parent) =>
            parent != null && (dir.Equals(parent, StringComparison.OrdinalIgnoreCase) || dir.StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

        /// <summary>Worker thread: where the profile goes and whether it can.</summary>
        public static MovePlan PlanMove(string from, string chosen, string mainMods, string gameDir)
        {
            var p = new MovePlan { From = Path.GetFullPath(from).TrimEnd('\\') };
            // a disk root keeps its backslash: "E:\" and not the drive-relative "E:"
            var c = Path.GetFullPath(chosen);
            if (c.Length > Path.GetPathRoot(c).Length) c = c.TrimEnd('\\');
            p.To = Path.GetFileName(c).Equals(SpaceAgeFolder, StringComparison.OrdinalIgnoreCase) && IsProfile(c) ? c : Path.Combine(c, SpaceAgeFolder);
            p.FromExists = Directory.Exists(p.From) && Directory.EnumerateFileSystemEntries(p.From).Any();
            if (p.To.Equals(p.From, StringComparison.OrdinalIgnoreCase)) { p.Same = true; return p; }
            var parent = Path.GetDirectoryName(p.To);
            if (Inside(p.To, p.From))
            { p.Error = "Нельзя перенести профиль внутрь него самого."; p.ErrorEn = "The profile cannot be moved into itself."; return p; }
            // (the game folder holds .paranoidal-git of the main build next to its mods: that is the default place, it is fine)
            if (Path.GetFileName(parent).Equals("mods", StringComparison.OrdinalIgnoreCase) || (mainMods != null && Inside(p.To, mainMods)) ||
                Directory.Exists(Path.Combine(parent, ".git")) || File.Exists(Path.Combine(parent, ".git")))
            {
                p.Error = "Папка " + parent + " принадлежит другой сборке или репозиторию. Выберите обычную папку, например D:\\Games.";
                p.ErrorEn = "The folder " + parent + " belongs to another build or repository. Choose a normal folder, e.g. D:\\Games.";
                return p;
            }
            if (!AppInfo.CanWrite(parent))
            {
                p.Error = "В папку " + parent + " нельзя записывать без прав администратора (например, это Program Files). Выберите обычную папку, например D:\\Games.";
                p.ErrorEn = "The folder " + parent + " cannot be written without administrator rights (e.g. Program Files). Choose a normal folder, e.g. D:\\Games.";
                return p;
            }
            if (IsProfile(p.To)) { p.TargetIsProfile = true; return p; }
            if (Directory.Exists(p.To) && Directory.EnumerateFileSystemEntries(p.To).Any())
            {
                p.Error = "В папке " + parent + " уже есть " + SpaceAgeFolder + ", и это не профиль Space Age. Выберите другую папку.";
                p.ErrorEn = "The folder " + parent + " already has " + SpaceAgeFolder + ", and it is not a Space Age profile. Choose another folder.";
                return p;
            }
            p.OutsideGame = gameDir != null && !Inside(p.To, gameDir);
            p.SameVolume = string.Equals(Path.GetPathRoot(p.From), Path.GetPathRoot(p.To), StringComparison.OrdinalIgnoreCase);
            if (p.FromExists)
                try { p.Bytes = new DirectoryInfo(p.From).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { }
            try { p.Free = new DriveInfo(Path.GetPathRoot(p.To)).AvailableFreeSpace; } catch { p.Free = -1; }
            if (p.FromExists && !p.SameVolume && p.Free >= 0 && p.Free < p.Bytes + 200L * 1024 * 1024)
            {
                p.Error = "На диске " + Path.GetPathRoot(p.To) + " не хватает места: профилю нужно " + Fmt.SizeRu(p.Bytes) + ", свободно " + Fmt.SizeRu(p.Free) + ".";
                p.ErrorEn = "The disk " + Path.GetPathRoot(p.To) + " has not enough space: the profile needs " + Fmt.Size(p.Bytes, true) + ", free " + Fmt.Size(p.Free, true) + ".";
            }
            return p;
        }

        /// <summary>
        /// Worker thread: moves the profile (rename on the same disk; on another disk a copy into &lt;To&gt;.partial, the rename,
        /// then the old folder is deleted, so an interruption leaves the original whole) and remembers its place.
        /// Without a profile only the place is remembered.
        /// </summary>
        public static void Move(MovePlan p, Action<double?, string> progress, Action<string> log)
        {
            if (p.FromExists)
            {
                if (Directory.Exists(p.To)) Directory.Delete(p.To);   // empty (PlanMove checked)
                if (p.SameVolume)
                {
                    progress(null, p.From + "  →  " + p.To);
                    Directory.Move(p.From, p.To);
                    log("перемещён (тот же диск): " + p.From + " → " + p.To);
                }
                else
                {
                    long done = 0;
                    CopyDir(p.From, p.To + ".partial", n =>
                    {
                        done += n;
                        progress(p.Bytes > 0 ? Math.Min(1, done / (double)p.Bytes) : (double?)null, Tr.T("Скопировано ", "Copied ") + Fmt.Size(done) + " / " + Fmt.Size(p.Bytes));
                    });
                    Directory.Move(p.To + ".partial", p.To);
                    log("скопирован на другой диск: " + p.From + " → " + p.To + " (" + Fmt.SizeRu(p.Bytes) + ")");
                }
            }
            Settings.SetPath("sa-root", p.To);
            if (p.FromExists && !p.SameVolume)
            {
                progress(null, Tr.T("Удаляю старую папку профиля", "Deleting the old profile folder"));
                try { FsUtil.DeleteDir(p.From); log("старая папка удалена: " + p.From); }
                catch (Exception ex) { log("старую папку " + p.From + " удалить не удалось (" + ex.Message + "), её можно удалить вручную"); }
            }
        }

        static void CopyDir(string src, string dst, Action<long> step)
        {
            if (Directory.Exists(dst)) FsUtil.DeleteDir(dst);
            Directory.CreateDirectory(dst);
            foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dst + d.Substring(src.Length));
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var to = dst + f.Substring(src.Length);
                File.Copy(f, to, true);
                File.SetAttributes(to, FileAttributes.Normal);
                step(new FileInfo(f).Length);
            }
        }
    }
}
