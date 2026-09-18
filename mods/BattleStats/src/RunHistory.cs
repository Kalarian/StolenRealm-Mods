using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace BattleStats
{
    /// <summary>
    /// Every run written to disk and read back. One file per run under BepInEx\BattleStats\runs\run-&lt;date-time&gt;.json,
    /// rewritten after each battle of that run, so a crash or an alt-F4 never loses more than the fight in progress.
    /// The file carries both a readable summary (for opening it in a text editor) and the raw key dictionary the Stats
    /// window needs to redraw the run exactly as it would have looked in game.
    /// </summary>
    internal static class RunHistory
    {
        public const int FileVersion = 1;

        public sealed class Fight
        {
            [JsonProperty("n")] public int Index;
            [JsonProperty("victory")] public bool Victory;
            [JsonProperty("turns")] public int Turns;
        }

        public sealed class Char
        {
            [JsonProperty("name")] public string Name;
            [JsonProperty("owner")] public string Owner;
            [JsonProperty("level")] public int Level;
            [JsonProperty("summary")] public Dictionary<string, object> Summary = new Dictionary<string, object>();
            [JsonProperty("keys")] public Dictionary<string, float> Keys = new Dictionary<string, float>();

            [JsonIgnore] private Dictionary<int, float> _live;
            /// <summary>The key map as the window reads it.</summary>
            [JsonIgnore]
            public Dictionary<int, float> Values
            {
                get
                {
                    if (_live == null)
                    {
                        _live = new Dictionary<int, float>();
                        if (Keys != null) foreach (var kv in Keys) { int k; if (int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out k)) _live[k] = kv.Value; }
                    }
                    return _live;
                }
            }
        }

        public sealed class RunFile
        {
            [JsonProperty("version")] public int Version = FileVersion;
            [JsonProperty("started")] public string Started;
            [JsonProperty("ended")] public string Ended;
            [JsonProperty("mode")] public string Mode;             // Campaign / Roguelike
            [JsonProperty("difficulty")] public string Difficulty;
            [JsonProperty("quest")] public string Quest;
            [JsonProperty("act")] public int Act;
            [JsonProperty("area")] public int Area;               // Roguelike: how deep the run got
            [JsonProperty("battles")] public int Battles;
            [JsonProperty("wins")] public int Wins;
            [JsonProperty("quest_id")] public string QuestId;    // which quest this run is being played on, so it can be picked up again after a restart
            [JsonProperty("closed")] public bool Closed;         // the run ended (town, retry, a new quest): never resumed, only read
            [JsonProperty("fights")] public List<Fight> Fights = new List<Fight>();
            [JsonProperty("characters")] public List<Char> Characters = new List<Char>();

            [JsonIgnore] public string Path;          // where it was read from
            [JsonIgnore] public bool IsCurrent;       // the run still being played

            [JsonIgnore]
            public DateTime StartedAt
            {
                get { DateTime d; return DateTime.TryParse(Started, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : DateTime.MinValue; }
            }

            public float Total(string statName)
            {
                float sum = 0f;
                foreach (Char c in Characters) { object v; if (c.Summary != null && c.Summary.TryGetValue(statName, out v)) sum += Convert.ToSingle(v, CultureInfo.InvariantCulture); }
                return sum;
            }

            /// <summary>One line for the history list: when, how big, where.</summary>
            public string Headline()
            {
                DateTime d = StartedAt;
                string when = d == DateTime.MinValue ? "?" : d.ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
                string what = Battles + (Battles == 1 ? " battle" : " battles");
                if (Battles > 0 && Wins < Battles) what += " (" + Wins + " won)";
                var bits = new List<string> { when, what };
                if (!string.IsNullOrEmpty(Difficulty)) bits.Add(Difficulty);
                if (!string.IsNullOrEmpty(Mode) && Mode != "Campaign") bits.Add(Mode);
                if (Area > 0) bits.Add("area " + Area);
                if (!string.IsNullOrEmpty(Quest)) bits.Add(Quest);
                return string.Join("  ·  ", bits.ToArray()) + (IsCurrent ? "   (current run)" : "");
            }

            public string SubLine()
            {
                string who = Characters.Count == 0 ? "no characters" : string.Join(", ", Characters.Select(c => c.Name).ToArray());
                float dmg = Total("DamageDealt"), heal = Total("HealingAdministered");
                string nums = dmg.ToString("N0", CultureInfo.InvariantCulture) + " damage";
                if (heal > 0f) nums += ", " + heal.ToString("N0", CultureInfo.InvariantCulture) + " healing";
                return who + "  ·  " + nums;
            }
        }

        public static string Folder
        {
            get { return System.IO.Path.Combine(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "BattleStats"), "runs"); }
        }

        public static string Save(RunFile run, string path)
        {
            Directory.CreateDirectory(Folder);
            if (string.IsNullOrEmpty(path)) path = System.IO.Path.Combine(Folder, "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            var s = new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore };
            File.WriteAllText(path, JsonConvert.SerializeObject(run, s), new System.Text.UTF8Encoding(false));
            return path;
        }

        /// <summary>Every saved run, newest first. Bad files are skipped with a warning, never thrown.</summary>
        public static List<RunFile> LoadAll()
        {
            var list = new List<RunFile>();
            try
            {
                if (!Directory.Exists(Folder)) return list;
                foreach (string f in Directory.GetFiles(Folder, "run-*.json"))
                {
                    try
                    {
                        RunFile r = JsonConvert.DeserializeObject<RunFile>(File.ReadAllText(f));
                        if (r == null) continue;
                        r.Path = f;
                        if (r.Characters == null) r.Characters = new List<Char>();
                        // a run with nobody in it has nothing to show: skip it rather than list a dead row
                        if (r.Characters.Count == 0) { if (BattleStatsPlugin.Cfg != null && BattleStatsPlugin.Cfg.Verbose.Value) BattleStatsPlugin.Log.LogInfo("Run history: skipping " + System.IO.Path.GetFileName(f) + ", it has no characters"); continue; }
                        list.Add(r);
                    }
                    catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Run history: could not read " + System.IO.Path.GetFileName(f) + " (" + e.Message + ")"); }
                }
            }
            catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Run history: could not list " + Folder + " (" + e.Message + ")"); }
            list.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
            return list;
        }

        /// <summary>Keep the newest `keep` files (0 or less = keep everything).</summary>
        public static void Prune(int keep, string protectPath)
        {
            if (keep <= 0) return;
            try
            {
                if (!Directory.Exists(Folder)) return;
                var files = new List<string>(Directory.GetFiles(Folder, "run-*.json"));
                if (files.Count <= keep) return;
                files.Sort(StringComparer.OrdinalIgnoreCase);   // the names carry the timestamp, so this is chronological
                int remove = files.Count - keep;
                foreach (string f in files)
                {
                    if (remove <= 0) break;
                    if (!string.IsNullOrEmpty(protectPath) && string.Equals(f, protectPath, StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(f); remove--; if (BattleStatsPlugin.Cfg != null && BattleStatsPlugin.Cfg.Verbose.Value) BattleStatsPlugin.Log.LogInfo("Run history: deleted old run " + System.IO.Path.GetFileName(f)); }
                    catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Run history: could not delete " + System.IO.Path.GetFileName(f) + " (" + e.Message + ")"); }
                }
            }
            catch { }
        }
    }
}
