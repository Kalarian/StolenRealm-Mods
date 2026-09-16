using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;

namespace FortunePreview
{
    /// <summary>
    /// One switchboard for every Stolen Realm mod: BepInEx\config\stolenrealm.mods.cfg.
    /// One line per plugin (true/false) plus a global VerboseLogging line. A mod that is off has none of its
    /// Harmony patches applied; F9 re-reads the file and applies/removes patches live. Missing key = true.
    /// Each plugin carries a copy of this class so no shared DLL is needed.
    /// </summary>
    internal static class MasterConfig
    {
        public const string FileName = "stolenrealm.mods.cfg";
        public static readonly string[] AllPlugins = { "DropRates", "DifficultyXP", "QoL", "TargetTooltip", "SpecialTooltips", "ScalingTooltips", "SharedFortunes", "FortunePreview", "FortuneUpgrade", "LevelSync", "AutoSalvage", "SharedGold", "BattleStats", "ThreatOverlay", "ModMenu", "SharedProgress", "NumberFormat", "BardPreview" };
        private static readonly Dictionary<string, bool> _on = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static bool? _verbose;

        public static string Path => System.IO.Path.Combine(Paths.ConfigPath, FileName);
        /// <summary>Global VerboseLogging line, or null if the line is absent (per-mod setting stands).</summary>
        public static bool? Verbose => _verbose;

        public static void Load()
        {
            _on.Clear();
            _verbose = null;
            try
            {
                if (!File.Exists(Path)) WriteDefault();
                foreach (string raw in File.ReadAllLines(Path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//") || line.StartsWith("[")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    bool b;
                    if (!bool.TryParse(val, out b)) continue;
                    if (key.Equals("VerboseLogging", StringComparison.OrdinalIgnoreCase)) _verbose = b;
                    else _on[key] = b;
                }
            }
            catch (Exception)
            {
                // unreadable file: treat everything as enabled
            }
        }

        public static bool Enabled(string plugin)
        {
            bool v;
            return !_on.TryGetValue(plugin, out v) || v;
        }

        /// <summary>The log file is appended across launches (BepInEx.cfg AppendLog = true). Whichever of our plugins
        /// loads first prints one SESSION START line; the flag lives in the AppDomain so every plugin copy shares it.</summary>
        public static void LogSessionStartOnce(BepInEx.Logging.ManualLogSource log)
        {
            try
            {
                const string key = "stolenrealm.mods.sessionmarker";
                if (AppDomain.CurrentDomain.GetData(key) != null) return;
                AppDomain.CurrentDomain.SetData(key, true);
                log.LogMessage("==================== SESSION START " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====================");
            }
            catch { }
        }

        // ---------- the in-game switchboard window (ModMenu plugin) ----------

        private const string MenuKey = "stolenrealm.mods.menu";
        private const string ReloadKey = "stolenrealm.mods.reloadtoken";

        /// <summary>True while the ModMenu plugin is active: then F9 opens the window and Apply triggers the reload.</summary>
        public static bool MenuPresent { get { try { return AppDomain.CurrentDomain.GetData(MenuKey) != null; } catch { return false; } } }

        public static int ReloadToken { get { try { object o = AppDomain.CurrentDomain.GetData(ReloadKey); return o is int ? (int)o : 0; } catch { return 0; } } }

        /// <summary>Ask every plugin to re-read its configs and this file (they poll ReloadRequested from Update).</summary>
        public static void RequestReload() { try { AppDomain.CurrentDomain.SetData(ReloadKey, ReloadToken + 1); } catch { } }

        public static bool ReloadRequested(ref int seen)
        {
            int t = ReloadToken;
            if (t == seen) return false;
            seen = t;
            return true;
        }

        /// <summary>Rewrite the switchboard with the given states (missing names count as true).</summary>
        public static void Write(IDictionary<string, bool> states, bool verbose)
        {
            var sb = new StringBuilder();
            AppendHeader(sb);
            foreach (string p in AllPlugins) { bool on; sb.Append(p).Append(" = ").AppendLine(states != null && states.TryGetValue(p, out on) ? (on ? "true" : "false") : "true"); }
            sb.AppendLine();
            sb.Append("VerboseLogging = ").AppendLine(verbose ? "true" : "false");
            File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void AppendHeader(StringBuilder sb)
        {
            sb.AppendLine("# Stolen Realm mods - master switchboard. Press F9 in game for the checkbox window (or edit with Notepad; then F9 / Apply applies it without restarting).");
            sb.AppendLine("# true = mod active, false = mod completely off (none of its game patches are applied).");
            sb.AppendLine("# VerboseLogging here overrides the VerboseLogging line in every stolenrealm.<mod>.cfg; delete the line to set them individually.");
            sb.AppendLine();
        }

        public static string Summary()
        {
            int on = 0;
            foreach (string p in AllPlugins) if (Enabled(p)) on++;
            return on + "/" + AllPlugins.Length + " mods enabled" + (_verbose.HasValue ? ", verbose " + (_verbose.Value ? "on" : "off") + " for all" : "");
        }

        private static void WriteDefault()
        {
            var sb = new StringBuilder();
            AppendHeader(sb);
            foreach (string p in AllPlugins) sb.Append(p).AppendLine(" = true");
            sb.AppendLine();
            sb.AppendLine("VerboseLogging = true");
            File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
