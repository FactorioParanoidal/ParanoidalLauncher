using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Paranoidal
{
    /// <summary>
    /// One line of the build: Paranoidal 2.1 and, since 1.5.0, Paranoidal 2.1 Space Age on the same game. The launcher
    /// serves one line at a time (settings.ini line=2.1|2.1-sa, Lines.Current; the player picks it in the start window or
    /// with the tabs). The Space Age line lives in a profile of its own (GameProfile). Since 1.5.0 the launcher knows no
    /// 2.0 line: a build for another game in mods\ is updated to 2.1 like any other old build (BuildFound.OtherLine).
    /// README «Сборки».
    /// </summary>
    sealed class BuildLine
    {
        /// <summary>"2.1" / "2.1-sa": the line in settings.ini and in setting keys.</summary>
        public readonly string Id;
        /// <summary>"2.1": major.minor of the game and the factorio_version of the build's mods.</summary>
        public readonly string Game;
        public readonly string Branch;
        /// <summary>The game version the build needs when its own requirement is not known (offline, nothing installed).</summary>
        public readonly string DefaultMin;
        /// <summary>Exactly DefaultMin (no line uses it since 1.5.0); otherwise the version zzzparanoidal requires (base >= x.y.z) or newer within the game.</summary>
        public readonly bool Exact;
        /// <summary>The Space Age line: the expansion is switched on, own profile (mods, saves, settings).</summary>
        public readonly bool SpaceAge;
        /// <summary>Expansion mods in data\ that must have the game's version.</summary>
        public readonly string[] Dlc;
        /// <summary>DLC entries of mod-list.json and the state the build needs (other entries are never touched).</summary>
        public readonly KeyValuePair<string, bool>[] DlcStates;
        string required;

        public BuildLine(string id, string game, string branch, string defaultMin, bool exact, bool spaceAge, string[] dlc, KeyValuePair<string, bool>[] states)
        {
            Id = id;
            Game = game;
            Branch = branch;
            DefaultMin = defaultMin;
            Exact = exact;
            SpaceAge = spaceAge;
            Dlc = dlc;
            DlcStates = states;
        }

        /// <summary>The oldest game version the build needs.</summary>
        public string MinGame => Exact || required == null || Releases.Compare(required, DefaultMin) <= 0 ? DefaultMin : required;

        /// <summary>The requirement of the build (zzzparanoidal: base >= x.y.z) installed or about to be installed.</summary>
        public void SetRequired(string version)
        {
            if (Releases.IsVersion(version) && Matches(version)) required = version;
        }

        /// <summary>A game version ("2.1.24") or a factorio_version ("2.1") of this line's game.</summary>
        public bool Matches(string version) => MajorMinor(version) == Game;

        /// <summary>The build of this line runs on this game version.</summary>
        public bool Fits(string gameVersion) =>
            gameVersion != null && (Exact ? gameVersion == DefaultMin : Matches(gameVersion) && Releases.Compare(gameVersion, MinGame) >= 0);

        /// <summary>The game the build needs, worded the same everywhere it is shown: "Factorio 2.1.24+ Space Age".</summary>
        public string RequiredGame => "Factorio " + MinGame + (Exact ? "" : "+") + " Space Age";

        /// <summary>"exactly 2.0.77" / "2.1.24 or newer (2.1.x)" in the interface language (en = null) or the given one.</summary>
        public string VersionWords(bool? en = null)
        {
            bool e = en ?? Tr.En;
            if (Exact) return (e ? "exactly " : "строго ") + MinGame;
            return MinGame + (e ? " or newer (" : " или новее (") + Game + ".x)";
        }

        /// <summary>"Paranoidal 2.1" / "Paranoidal 2.1 Space Age": the same in both languages, also in the version line.</summary>
        public string Name => "Paranoidal " + Game + (SpaceAge ? " Space Age" : "");

        public static string MajorMinor(string version)
        {
            var m = Regex.Match(version ?? "", @"^\s*(\d+)\.(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) + "." + int.Parse(m.Groups[2].Value) : null;
        }
    }

    static class Lines
    {
        static readonly string[] Dlc21 = { "space-age", "quality", "elevated-rails", "recycler" };

        /// <summary>Quality 2.1 depends on the recycler mod, so it is switched on too (ПР-069 of the modpack).</summary>
        public static readonly BuildLine L21 = new BuildLine("2.1", "2.1", "pre_release_2.1", "2.1.24", false, false, Dlc21,
            new[]
            {
                new KeyValuePair<string, bool>("space-age", false),
                new KeyValuePair<string, bool>("quality", true),
                new KeyValuePair<string, bool>("elevated-rails", true),
                new KeyValuePair<string, bool>("recycler", true),
            });

        /// <summary>
        /// 1.5.0 (owner, 2026-11-10): the Space Age build of the modpack (branch pre_release_2.1_space_age, in active
        /// development), every expansion mod on. Before the rocket it is the main 2.1 build, so 2.1 saves load in it; a
        /// Space Age save does not load in the main build. Played in a profile of its own (GameProfile).
        /// </summary>
        public static readonly BuildLine SA = new BuildLine("2.1-sa", "2.1", "pre_release_2.1_space_age", "2.1.24", false, true, Dlc21,
            new[]
            {
                new KeyValuePair<string, bool>("space-age", true),
                new KeyValuePair<string, bool>("quality", true),
                new KeyValuePair<string, bool>("elevated-rails", true),
                new KeyValuePair<string, bool>("recycler", true),
            });

        /// <summary>The main line of each game version (the Space Age line shares the game of 2.1).</summary>
        static readonly BuildLine[] Main = { L21 };

        static BuildLine current;

        /// <summary>
        /// The line of this launcher: settings.ini line=…; 2.1 without it and for "2.0" of launchers 1.3.0 and 1.4.0
        /// (MainForm.ForgetLine20 resets what belonged to that line before anything reads it).
        /// </summary>
        public static BuildLine Current
        {
            get { return current ?? (current = ById(Settings.Get("line")) ?? L21); }
        }

        public static void Set(BuildLine line) { current = line; }

        public static BuildLine ById(string id) => id == L21.Id ? L21 : id == SA.Id ? SA : null;

        /// <summary>The main line of a factorio_version / game version ("2.1", "2.1.24"); null for other versions (2.0, 1.1).</summary>
        public static BuildLine Of(string version)
        {
            var mm = BuildLine.MajorMinor(version);
            foreach (var l in Main) if (l.Game == mm) return l;
            return null;
        }

        /// <summary>
        /// A setting that belongs to the build of a line (mods folder, pin, last start): the main lines keep the plain key
        /// of the older launchers, the Space Age line gets "key-2.1-sa".
        /// </summary>
        public static string Key(string key, BuildLine line = null) => (line ?? Current).SpaceAge ? key + "-" + SA.Id : key;
    }
}
