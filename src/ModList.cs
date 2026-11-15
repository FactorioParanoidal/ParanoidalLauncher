using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Paranoidal
{
    /// <summary>
    /// mods\mod-list.json: the build is played with the Quality and Elevated Rails DLC switched on (2.1: and Recycler,
    /// Quality 2.1 depends on it) and Space Age switched off (decision of the modpack team). A player with another DLC
    /// state gets a mod mismatch with the host.
    /// </summary>
    static class ModList
    {
        /// <summary>The game's JSON files (mod-list.json, info.json) as Factorio accepts them: trailing commas, comments.</summary>
        public static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        static readonly JsonDocumentOptions DocumentOptions = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        // one entry per line as before; non-ASCII text stays as it is (the game writes UTF-8)
        static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>The entries of mod-list.json the launcher reads (other fields are ignored here and kept by EnsureDlc).</summary>
        public sealed class File
        {
            public List<Entry> Mods { get; set; }
        }

        public sealed class Entry
        {
            public string Name { get; set; }
            public bool Enabled { get; set; }
        }

        /// <summary>mods\mod-list.json; throws when it is missing or broken.</summary>
        public static File Read(string modsDir) =>
            JsonSerializer.Deserialize<File>(System.IO.File.ReadAllText(Path.Combine(modsDir, "mod-list.json"), Encoding.UTF8), ReadOptions);

        /// <summary>DLC mod -> the state the build needs (BuildLine.DlcStates). Other entries of mod-list.json are never touched.</summary>
        static KeyValuePair<string, bool>[] Dlc => Lines.Current.DlcStates;

        /// <summary>"quality, elevated-rails включены, space-age выключен" for the journal.</summary>
        static string StatesRu() =>
            string.Join(", ", Dlc.Where(d => d.Value).Select(d => d.Key)) + " включены, " + string.Join(", ", Dlc.Where(d => !d.Value).Select(d => d.Key)) + " выключен";

        /// <summary>Creates mod-list.json if missing; otherwise only sets the enabled flag of the DLC entries.</summary>
        public static void EnsureDlc(string modsDir, Action<string, LogKind> log)
        {
            Directory.CreateDirectory(modsDir);
            var path = Path.Combine(modsDir, "mod-list.json");
            // the entries as JSON objects: fields the launcher does not know are written back unchanged
            List<JsonObject> mods = null;

            if (System.IO.File.Exists(path))
            {
                try
                {
                    var root = JsonNode.Parse(System.IO.File.ReadAllText(path, Encoding.UTF8), null, DocumentOptions) as JsonObject;
                    var arr = root == null ? null : root["mods"] as JsonArray;
                    if (arr != null) mods = arr.OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()).ToList();
                }
                catch (JsonException) { mods = null; }

                if (mods == null)
                {
                    var bak = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    try { System.IO.File.Copy(path, bak, true); } catch { }
                    log("mod-list.json повреждён и сохранён как " + Path.GetFileName(bak) + ", создаю новый.", LogKind.Warn);
                }
                else
                {
                    var changes = new List<string>();
                    foreach (var want in Dlc)
                    {
                        bool found = false, changed = false;
                        foreach (var m in mods)
                        {
                            string name;
                            if (!(m["name"] is JsonValue nv && nv.TryGetValue(out name) && name == want.Key)) continue;
                            found = true;
                            bool en;
                            if (!(m["enabled"] is JsonValue ev && ev.TryGetValue(out en) && en == want.Value)) { m["enabled"] = want.Value; changed = true; }
                        }
                        if (!found) { mods.Add(NewEntry(want.Key, want.Value)); changed = true; }
                        if (changed) changes.Add(want.Key + (want.Value ? " включён" : " выключен"));
                    }
                    if (changes.Count == 0) return;
                    Write(path, mods);
                    log("mod-list.json: " + string.Join(", ", changes) + ".", LogKind.Ok);
                    return;
                }
            }

            mods = new List<JsonObject> { NewEntry("base", true) };
            foreach (var want in Dlc.OrderBy(d => d.Key, StringComparer.Ordinal)) mods.Add(NewEntry(want.Key, want.Value));
            Write(path, mods);
            log("Создан mod-list.json: " + StatesRu() + ".", LogKind.Ok);
        }

        static JsonObject NewEntry(string name, bool enabled) => new JsonObject { ["name"] = name, ["enabled"] = enabled };

        static void Write(string path, List<JsonObject> mods)
        {
            var sb = new StringBuilder("{\n  \"mods\": [\n");
            for (int i = 0; i < mods.Count; i++)
                sb.Append("    ").Append(mods[i].ToJsonString(WriteOptions)).Append(i + 1 < mods.Count ? ",\n" : "\n");
            sb.Append("  ]\n}\n");
            var tmp = path + ".tmp";
            System.IO.File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (System.IO.File.Exists(path)) { System.IO.File.SetAttributes(path, FileAttributes.Normal); System.IO.File.Delete(path); }
            System.IO.File.Move(tmp, path);
        }
    }
}
