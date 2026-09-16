using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Installer
{
    /// <summary>
    /// One-file installer for the Stolen Realm mod pack (the pack zip is an embedded resource).
    /// Double-click: a small window shows the detected game folder, a checkbox for resetting settings, and
    /// Install / Remove buttons. Command line (for testing and scripts): "<gameFolder> install [overwrite]" or
    /// "<gameFolder> uninstall" runs without a window and writes "install-log.txt" into the game folder.
    /// Config policy: DLLs and loader files are always replaced; our plugin folders that are no longer in the pack
    /// (stolenrealm.*.dll, not TestDriver) are removed with their cfg and switchboard line. Config files are MERGED unless "overwrite" is chosen:
    /// every value the player already has is kept, every setting the pack has that the player's file lacks is added
    /// with the pack's value (with its comment), and settings the pack no longer has are left in place (BepInEx
    /// ignores them). The master switchboard is merged the same way, so on/off choices survive.
    /// </summary>
    internal static class Program
    {
        internal const string GameExe = "Stolen Realm.exe";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && Directory.Exists(args[0]))
            {
                // headless mode
                string dir = args[0];
                bool overwrite = args.Any(a => a.Equals("overwrite", StringComparison.OrdinalIgnoreCase));
                bool offline = args.Any(a => a.Equals("offline", StringComparison.OrdinalIgnoreCase));
                var log = new StringBuilder();
                int code;
                try
                {
                    if (!File.Exists(Path.Combine(dir, GameExe))) { log.AppendLine("No " + GameExe + " in " + dir); code = 1; }
                    else if (args[1].Equals("uninstall", StringComparison.OrdinalIgnoreCase)) code = Pack.Uninstall(dir, log);
                    else
                    {
                        string zip = offline ? null : Updater.FetchNewerZip(dir, log);
                        code = Pack.Install(dir, overwrite, log, zip);
                    }
                }
                catch (Exception e) { log.AppendLine("ERROR: " + e); code = 2; }
                try { File.WriteAllText(Path.Combine(dir, "install-log.txt"), log.ToString(), new UTF8Encoding(false)); } catch { }
                return code;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }

    // ================================================================== the work

    internal static partial class Pack
    {
        private const string MasterCfg = "BepInEx/config/stolenrealm.mods.cfg";

        public const string VersionFile = "stolenrealm-mods.version"; // written into BepInEx/ on install

        /// <summary>The version of the pack embedded in this exe (version.txt inside the zip), or "0".</summary>
        public static string EmbeddedVersion()
        {
            try
            {
                using (Stream zs = Assembly.GetExecutingAssembly().GetManifestResourceStream("mods.zip"))
                using (var zip = new ZipArchive(zs, ZipArchiveMode.Read))
                    return ZipVersion(zip);
            }
            catch { return "0"; }
        }

        public static string ZipVersion(ZipArchive zip)
        {
            ZipArchiveEntry e = zip.GetEntry("version.txt");
            if (e == null) return "0";
            using (var r = new StreamReader(e.Open(), Encoding.UTF8)) return r.ReadToEnd().Trim();
        }

        public static string InstalledVersion(string gameDir)
        {
            try { string p = Path.Combine(gameDir, "BepInEx", VersionFile); return File.Exists(p) ? File.ReadAllText(p).Trim() : "0"; } catch { return "0"; }
        }

        /// <summary>Install from the embedded pack, or from a downloaded zip file when zipPath is given.</summary>
        public static int Install(string gameDir, bool overwriteConfigs, StringBuilder log, string zipPath = null)
        {
            using (Stream zipStream = zipPath != null ? (Stream)File.OpenRead(zipPath) : Assembly.GetExecutingAssembly().GetManifestResourceStream("mods.zip"))
            {
                if (zipStream == null) { log.AppendLine("The mod pack is missing from this installer (build error)."); return 2; }
                using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    string version = ZipVersion(zip);
                    log.AppendLine("Installing mod pack v" + version + (zipPath != null ? " (downloaded)" : " (built into this installer)") + " over v" + InstalledVersion(gameDir) + ".");
                    int replaced = 0, added = 0, merged = 0, mergedKeys = 0, resetCfg = 0;
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        if (entry.FullName.EndsWith("/")) continue;
                        if (entry.FullName.Equals("version.txt", StringComparison.OrdinalIgnoreCase)) continue;
                        string rel = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                        string dest = Path.Combine(gameDir, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        // BepInEx.cfg is the loader's own file (entry point, log settings), not a mod setting: always replaced,
                        // so a loader fix (e.g. the MonoBehaviour entry point the 2026-09 game build needs) reaches everyone.
                        bool isCfg = entry.FullName.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase)
                                     && entry.FullName.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)
                                     && !entry.FullName.Equals("BepInEx/config/BepInEx.cfg", StringComparison.OrdinalIgnoreCase);
                        bool existed = File.Exists(dest);
                        if (isCfg && existed && !overwriteConfigs)
                        {
                            string shipped;
                            using (var r = new StreamReader(entry.Open(), Encoding.UTF8)) shipped = r.ReadToEnd();
                            int n = MergeConfig(dest, shipped);
                            merged++; mergedKeys += n;
                            if (n > 0) log.AppendLine("  " + Path.GetFileName(dest) + ": added " + n + " new setting(s), kept yours");
                            continue;
                        }
                        if (isCfg && existed) resetCfg++;
                        using (Stream src = entry.Open())
                        using (FileStream dst = new FileStream(dest, FileMode.Create, FileAccess.Write))
                            src.CopyTo(dst);
                        if (existed) replaced++; else added++;
                    }
                    // Retired mods: our plugin folders (stolenrealm.*.dll) that are no longer in the pack go away, with their cfg
                    // and their switchboard line. Other people's mods and our own dev tools are never touched.
                    var packPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        string[] parts = e.FullName.Split('/');
                        if (parts.Length >= 3 && parts[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("plugins", StringComparison.OrdinalIgnoreCase)) packPlugins.Add(parts[2]);
                    }
                    int retired = packPlugins.Count > 0 ? RemoveRetired(gameDir, packPlugins, log) : 0;
                    try { File.WriteAllText(Path.Combine(gameDir, "BepInEx", VersionFile), version + Environment.NewLine, new UTF8Encoding(false)); } catch { }
                    log.AppendLine("Installed: " + added + " new file(s), " + replaced + " updated" + (retired > 0 ? ", " + retired + " retired mod(s) removed" : "")
                        + (merged > 0 ? ", " + merged + " config file(s) merged (" + mergedKeys + " new setting(s) added, your values kept)" : "")
                        + (resetCfg > 0 ? ", " + resetCfg + " config file(s) reset to the pack's settings" : "") + ".");
                }
            }
            log.AppendLine("Done. Launch the game from Steam as usual.");
            log.AppendLine(@"Turn a mod off: press F9 in game for the mod window, untick it, Apply (or edit BepInEx\config\stolenrealm.mods.cfg by hand).");
            return 0;
        }

        private static readonly string[] NeverRetire = { "TestDriver" };
    }

    // ================================================================== GitHub Releases

    /// <summary>Looks for a newer pack on GitHub (public repo, no login): the latest release's tag is the version, its
    /// assets carry the zip and a SHA256SUMS.txt. The zip is downloaded to a temp file and verified before use.</summary>
    internal static class Updater
    {
        public const string Repo = "Kalarian/StolenRealm-Mods";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
        private const string Api = "https://api.github.com/repos/" + Repo + "/releases/latest";
        private const string ZipName = "StolenRealm-Mods-install.zip";

        public sealed class Latest { public string Version; public string ZipUrl; public string SumsUrl; public string Page; }

        public static Latest Check(StringBuilder log)
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                string json;
                using (var wc = new System.Net.WebClient())
                {
                    // never answer from the WinINet cache: GitHub allows 60 s of caching and "latest" must be current
                    wc.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                    wc.Headers.Add("User-Agent", "StolenRealmModsInstaller");
                    wc.Headers.Add("Accept", "application/vnd.github+json");
                    wc.Headers.Add("Cache-Control", "no-cache");
                    json = wc.DownloadString(Api + "?t=" + DateTime.UtcNow.Ticks);
                }
                var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9][0-9.]*)\"");
                if (!m.Success) { log.AppendLine("Online check: no release found."); return null; }
                var l = new Latest { Version = m.Groups[1].Value, Page = ReleasesPage };
                foreach (Match a in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
                {
                    string u = a.Groups[1].Value;
                    if (u.EndsWith("/" + ZipName, StringComparison.OrdinalIgnoreCase)) l.ZipUrl = u;
                    else if (u.EndsWith("/SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) l.SumsUrl = u;
                }
                return l;
            }
            catch (Exception e) { log.AppendLine("Online check failed (" + e.Message + "); using the pack built into this installer."); return null; }
        }

        public static bool IsNewer(string a, string b)
        {
            Version va, vb;
            if (!Version.TryParse(Pad(a), out va) || !Version.TryParse(Pad(b), out vb)) return false;
            return va > vb;
        }
        private static string Pad(string v) { v = (v ?? "0").Trim().TrimStart('v', 'V'); return v.Contains(".") ? v : v + ".0"; }

        /// <summary>Returns the path of a downloaded, verified zip when GitHub has a newer pack than this exe carries; null otherwise.</summary>
        public static string FetchNewerZip(string gameDir, StringBuilder log)
        {
            Latest l = Check(log);
            if (l == null) return null;
            string embedded = Pack.EmbeddedVersion();
            log.AppendLine("Latest online: v" + l.Version + " | in this installer: v" + embedded + " | installed: v" + Pack.InstalledVersion(gameDir));
            if (!IsNewer(l.Version, embedded)) return null;
            if (l.ZipUrl == null) { log.AppendLine("The newer release has no zip asset; using the built-in pack."); return null; }
            try
            {
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                string tmp = Path.Combine(Path.GetTempPath(), "StolenRealm-Mods-" + l.Version + ".zip");
                using (var wc = new System.Net.WebClient())
                {
                    wc.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                    wc.Headers.Add("User-Agent", "StolenRealmModsInstaller");
                    wc.DownloadFile(l.ZipUrl, tmp);
                    if (l.SumsUrl != null)
                    {
                        string sums = wc.DownloadString(l.SumsUrl);
                        var sm = Regex.Match(sums, "([0-9a-fA-F]{64})\\s+\\*?" + Regex.Escape(ZipName), RegexOptions.IgnoreCase);
                        if (sm.Success)
                        {
                            string have = Sha256(tmp);
                            if (!have.Equals(sm.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) { log.AppendLine("Downloaded zip failed its checksum; using the built-in pack instead."); try { File.Delete(tmp); } catch { } return null; }
                            log.AppendLine("Downloaded v" + l.Version + " and verified its checksum.");
                        }
                        else log.AppendLine("Downloaded v" + l.Version + " (no checksum listed for the zip).");
                    }
                    else log.AppendLine("Downloaded v" + l.Version + " (release has no checksum file).");
                }
                using (var zip = ZipFile.OpenRead(tmp)) { if (zip.GetEntry("version.txt") == null) { log.AppendLine("Downloaded zip has no version.txt; using the built-in pack."); return null; } }
                return tmp;
            }
            catch (Exception e) { log.AppendLine("Download failed (" + e.Message + "); using the built-in pack."); return null; }
        }

        private static string Sha256(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var f = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }
    }

    internal static partial class Pack
    { // dev-only tools that are deliberately not in the pack

        /// <summary>Delete plugin folders that hold one of OUR dlls (stolenrealm.*.dll) but are not in this pack, plus
        /// their stolenrealm.&lt;name&gt;.cfg and their line in the switchboard. Returns how many were removed.</summary>
        public static int RemoveRetired(string gameDir, HashSet<string> packPlugins, StringBuilder log)
        {
            int n = 0;
            try
            {
                string plugins = Path.Combine(gameDir, "BepInEx", "plugins");
                if (!Directory.Exists(plugins)) return 0;
                var removedNames = new List<string>();
                foreach (string dir in Directory.GetDirectories(plugins))
                {
                    string name = Path.GetFileName(dir);
                    if (packPlugins.Contains(name) || NeverRetire.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    bool ours = Directory.GetFiles(dir, "stolenrealm.*.dll").Length > 0;
                    if (!ours) continue; // somebody else's mod
                    try
                    {
                        Directory.Delete(dir, true);
                        string cfg = Path.Combine(gameDir, "BepInEx", "config", "stolenrealm." + name.ToLowerInvariant() + ".cfg");
                        if (File.Exists(cfg)) File.Delete(cfg);
                        removedNames.Add(name); n++;
                        log.AppendLine("  Retired mod removed: " + name);
                    }
                    catch (Exception e) { log.AppendLine("  Could not remove retired mod " + name + ": " + e.Message); }
                }
                if (removedNames.Count > 0)
                {
                    string master = Path.Combine(gameDir, "BepInEx", "config", "stolenrealm.mods.cfg");
                    if (File.Exists(master))
                    {
                        var lines = File.ReadAllLines(master).Where(l => !removedNames.Any(r => Regex.IsMatch(l, @"^\s*" + Regex.Escape(r) + @"\s*=", RegexOptions.IgnoreCase))).ToArray();
                        File.WriteAllLines(master, lines, new UTF8Encoding(false));
                    }
                }
            }
            catch (Exception e) { log.AppendLine("  Retired-mod check failed: " + e.Message); }
            return n;
        }

        public static int Uninstall(string gameDir, StringBuilder log)
        {
            int n = 0;
            foreach (string f in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "Stolen Realm Mods - Read Me.pdf", "README - Stolen Realm mods.txt", "install-log.txt" })
            {
                string p = Path.Combine(gameDir, f);
                if (File.Exists(p)) { File.Delete(p); n++; }
            }
            string bep = Path.Combine(gameDir, "BepInEx");
            if (Directory.Exists(bep)) { Directory.Delete(bep, true); n++; }
            log.AppendLine("Removed " + n + " item(s). The game is back to vanilla. Saves and screenshots were not touched.");
            return 0;
        }

        // ---------- BepInEx cfg merge ----------

        private sealed class Entry { public string Section; public string Key; public List<string> Block = new List<string>(); }

        /// <summary>Parse a BepInEx cfg into ordered entries; each entry carries its preceding comment lines.</summary>
        private static List<Entry> Parse(string text, out List<string> preamble)
        {
            var entries = new List<Entry>();
            preamble = new List<string>();
            string section = "";
            var pending = new List<string>();
            bool seenSection = false;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string t = raw.Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) { section = t.Substring(1, t.Length - 2); seenSection = true; pending.Clear(); continue; }
                if (t.Length == 0) { if (!seenSection && entries.Count == 0) preamble.Add(raw); else pending.Clear(); continue; }
                if (t.StartsWith("#") || t.StartsWith("//")) { if (!seenSection && entries.Count == 0 && !LooksLikeKeyComment(t)) preamble.Add(raw); else pending.Add(raw); continue; }
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                var e = new Entry { Section = section, Key = t.Substring(0, eq).Trim() };
                e.Block.AddRange(pending); e.Block.Add(raw);
                pending.Clear();
                entries.Add(e);
            }
            return entries;
        }

        private static bool LooksLikeKeyComment(string t) { return t.StartsWith("##") || t.StartsWith("# Setting type") || t.StartsWith("# Default value") || t.StartsWith("# Acceptable"); }

        /// <summary>Add every (section,key) the shipped file has and the user's file lacks, keeping the user's file otherwise. Returns how many were added.</summary>
        public static int MergeConfig(string userPath, string shippedText)
        {
            string userText = File.ReadAllText(userPath, Encoding.UTF8);
            List<string> pre1, pre2;
            var shipped = Parse(shippedText, out pre1);
            var mine = Parse(userText, out pre2);
            var have = new HashSet<string>(mine.Select(e => (e.Section + "|" + e.Key).ToLowerInvariant()));
            var missing = shipped.Where(e => !have.Contains((e.Section + "|" + e.Key).ToLowerInvariant())).ToList();
            if (missing.Count == 0) return 0;

            var lines = userText.Replace("\r\n", "\n").Split('\n').ToList();
            foreach (var group in missing.GroupBy(e => e.Section))
            {
                string header = "[" + group.Key + "]";
                int idx = group.Key.Length == 0 ? -1 : lines.FindIndex(l => l.Trim().Equals(header, StringComparison.OrdinalIgnoreCase));
                List<string> toInsert = new List<string>();
                foreach (Entry e in group) { toInsert.Add(""); toInsert.AddRange(e.Block); }
                if (group.Key.Length == 0)
                {
                    // sectionless file (the master switchboard): insert before VerboseLogging if present, else append
                    int at = lines.FindIndex(l => l.Trim().StartsWith("VerboseLogging", StringComparison.OrdinalIgnoreCase));
                    var flat = group.Select(e => e.Block.Last()).ToList();
                    if (at < 0) lines.AddRange(flat); else lines.InsertRange(at, flat);
                    continue;
                }
                if (idx < 0)
                {
                    while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
                    lines.Add(""); lines.Add(header);
                    lines.AddRange(toInsert);
                    continue;
                }
                // end of that section = the line before the next [section] header (or end of file)
                int end = lines.Count;
                for (int i = idx + 1; i < lines.Count; i++) { string t = lines[i].Trim(); if (t.StartsWith("[") && t.EndsWith("]")) { end = i; break; } }
                while (end > idx + 1 && lines[end - 1].Trim().Length == 0) end--;
                lines.InsertRange(end, toInsert);
            }
            File.WriteAllText(userPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
            return missing.Count;
        }

        // ---------- finding the game ----------

        public static string FindGameFolder()
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(here, Program.GameExe))) return here;
            foreach (string lib in SteamLibraries())
            {
                string cand = Path.Combine(lib, "steamapps", "common", "Stolen Realm");
                if (File.Exists(Path.Combine(cand, Program.GameExe))) return cand;
            }
            foreach (DriveInfo d in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed && x.IsReady))
                foreach (string rel in new[] { @"Program Files (x86)\Steam", @"Program Files\Steam", "Steam", "SteamLibrary", @"Games\Steam", @"Games\SteamLibrary" })
                {
                    string cand = Path.Combine(d.RootDirectory.FullName, rel, "steamapps", "common", "Stolen Realm");
                    if (File.Exists(Path.Combine(cand, Program.GameExe))) return cand;
                }
            return null;
        }

        private static IEnumerable<string> SteamLibraries()
        {
            var libs = new List<string>();
            string steam = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) steam = k != null ? k.GetValue("SteamPath") as string : null;
                if (steam == null) using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")) steam = k != null ? k.GetValue("InstallPath") as string : null;
            }
            catch { }
            if (string.IsNullOrEmpty(steam)) return libs;
            steam = steam.Replace('/', Path.DirectorySeparatorChar);
            libs.Add(steam);
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(p) && !libs.Contains(p, StringComparer.OrdinalIgnoreCase)) libs.Add(p);
                }
            return libs;
        }

        public static bool IsGameRunning()
        {
            try { return Process.GetProcessesByName("Stolen Realm").Length > 0; } catch { return false; }
        }
    }

    // ================================================================== the window

    internal sealed class MainForm : Form
    {
        private readonly TextBox _path = new TextBox();
        private readonly CheckBox _overwrite = new CheckBox();
        private readonly TextBox _log = new TextBox();
        private readonly Button _install = new Button();
        private readonly Button _remove = new Button();
        private Label _version;

        public MainForm()
        {
            Text = "Stolen Realm Mods";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(560, 400);
            Font = new Font("Segoe UI", 9.5f);

            var title = new Label { Text = "Stolen Realm mod pack", Font = new Font("Segoe UI", 14f, FontStyle.Bold), AutoSize = true, Location = new Point(16, 12) };
            var sub = new Label { Text = "Installs or updates the mods. Close the game first.", AutoSize = true, Location = new Point(18, 44), ForeColor = Color.DimGray };
            _version = new Label { Text = "Pack in this installer: v" + Pack.EmbeddedVersion() + "  -  checking GitHub for a newer one...", AutoSize = true, Location = new Point(18, 62), ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f) };
            Controls.Add(_version);
            var checker = new System.Threading.Thread(() =>
            {
                var tmpLog = new StringBuilder();
                Updater.Latest l = Updater.Check(tmpLog);
                string embedded = Pack.EmbeddedVersion();
                string text = l == null ? "Pack in this installer: v" + embedded + "  -  GitHub not reachable, this pack will be used."
                    : Updater.IsNewer(l.Version, embedded) ? "Pack in this installer: v" + embedded + "  -  v" + l.Version + " is on GitHub and will be downloaded on Install."
                    : "Pack in this installer: v" + embedded + "  -  up to date (GitHub: v" + l.Version + ").";
                try { BeginInvoke((Action)(() => _version.Text = text)); } catch { }
            });
            checker.IsBackground = true; checker.Start();
            var pathLabel = new Label { Text = "Game folder", AutoSize = true, Location = new Point(18, 78) };
            _path.SetBounds(18, 98, 440, 26);
            var browse = new Button { Text = "Browse...", Location = new Point(464, 96), Size = new Size(78, 28) };
            browse.Click += (s, e) => Browse();

            _overwrite.Text = "Reset all mod settings to the pack's settings (overwrites my config edits)";
            _overwrite.AutoSize = true; _overwrite.Location = new Point(18, 136);
            var note = new Label { Text = "Unchecked (recommended): your settings are kept and any new settings are added with the pack's values.", AutoSize = true, Location = new Point(36, 158), ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f) };

            _install.Text = "Install / Update"; _install.SetBounds(18, 190, 150, 34); _install.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            _install.Click += (s, e) => Run(false);
            _remove.Text = "Remove mods"; _remove.SetBounds(176, 190, 120, 34);
            _remove.Click += (s, e) => Run(true);
            var close = new Button { Text = "Close", Location = new Point(464, 190), Size = new Size(78, 34) };
            close.Click += (s, e) => Close();

            _log.Multiline = true; _log.ReadOnly = true; _log.ScrollBars = ScrollBars.Vertical;
            _log.SetBounds(18, 236, 524, 148); _log.Font = new Font("Consolas", 9f); _log.BackColor = Color.White;

            Controls.AddRange(new Control[] { title, sub, pathLabel, _path, browse, _overwrite, note, _install, _remove, close, _log });

            string found = null;
            try { found = Pack.FindGameFolder(); } catch { }
            if (found != null) { _path.Text = found; Log("Found the game at " + found); }
            else Log("Could not find the game on its own. Use Browse... to pick the folder that contains 'Stolen Realm.exe'\r\n(Steam: right-click the game -> Manage -> Browse local files).");
        }

        private void Browse()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pick the Stolen Realm game folder (it contains Stolen Realm.exe)", ShowNewFolderButton = false })
            {
                if (Directory.Exists(_path.Text)) dlg.SelectedPath = _path.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) _path.Text = dlg.SelectedPath;
            }
        }

        private void Run(bool uninstall)
        {
            string dir = _path.Text.Trim().Trim('"');
            if (!Directory.Exists(dir) || !File.Exists(Path.Combine(dir, Program.GameExe)))
            {
                MessageBox.Show(this, "That folder does not contain 'Stolen Realm.exe'. Use Browse... to pick the game folder.", "Stolen Realm Mods", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Pack.IsGameRunning())
            {
                MessageBox.Show(this, "Stolen Realm is running. Close the game, then try again.", "Stolen Realm Mods", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (uninstall && MessageBox.Show(this, "Remove the mod loader and every mod from\n" + dir + "?\n\nSaves and screenshots are not touched.", "Remove mods", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (!uninstall && _overwrite.Checked && MessageBox.Show(this, "This replaces every mod config file with the pack's copy. Any settings you changed by hand are lost.\n\nContinue?", "Reset settings", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            _install.Enabled = _remove.Enabled = false;
            var log = new StringBuilder();
            try
            {
                string zip = uninstall ? null : Updater.FetchNewerZip(dir, log);
                int code = uninstall ? Pack.Uninstall(dir, log) : Pack.Install(dir, _overwrite.Checked, log, zip);
                Log(log.ToString().TrimEnd());
                if (code == 0 && !uninstall) Log("You can close this window and start the game.");
            }
            catch (Exception e)
            {
                Log(log.ToString().TrimEnd());
                Log("Something went wrong: " + e.Message);
                MessageBox.Show(this, "Something went wrong:\n" + e.Message + "\n\nSend a screenshot of this window.", "Stolen Realm Mods", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { _install.Enabled = _remove.Enabled = true; }
        }

        private void Log(string s)
        {
            _log.AppendText((_log.TextLength > 0 ? "\r\n" : "") + s.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        }
    }
}
