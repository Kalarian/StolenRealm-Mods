import os, re

ROOT = r"C:/Claude/General/stolen-realm/mods"
PLUGINS = ["DropRates", "DifficultyXP", "QoL", "TargetTooltip", "SpecialTooltips", "ScalingTooltips", "SharedFortunes", "FortunePreview", "FortuneUpgrade", "LevelSync", "AutoSalvage", "SharedGold", "BattleStats", "ThreatOverlay", "ModMenu", "SharedProgress", "NumberFormat", "ItemSkillTooltips", "SellValue", "RoguelikeQoL"]

MASTER = '''using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;

namespace {ns}
{
    /// <summary>
    /// One switchboard for every Stolen Realm mod: BepInEx\\config\\stolenrealm.mods.cfg.
    /// One line per plugin (true/false) plus a global VerboseLogging line. A mod that is off has none of its
    /// Harmony patches applied; F9 re-reads the file and applies/removes patches live. Missing key = true.
    /// Each plugin carries a copy of this class so no shared DLL is needed.
    /// </summary>
    internal static class MasterConfig
    {
        public const string FileName = "stolenrealm.mods.cfg";
        public static readonly string[] AllPlugins = { "DropRates", "DifficultyXP", "QoL", "TargetTooltip", "SpecialTooltips", "ScalingTooltips", "SharedFortunes", "FortunePreview", "FortuneUpgrade", "LevelSync", "AutoSalvage", "SharedGold", "BattleStats", "ThreatOverlay", "ModMenu", "SharedProgress", "NumberFormat", "ItemSkillTooltips", "SellValue", "RoguelikeQoL" };
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
'''

PLUGIN = '''using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using {ns}.Patches;

namespace {ns}
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class {ns}Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static {ns}Config Cfg;
{extraFields}        private Harmony _harmony;
        private bool _patched;
        private int _reloadSeen;

        private void Awake()
        {
            Log = Logger;
            MasterConfig.LogSessionStartOnce(Log);
            Cfg = new {ns}Config(Config);
{awakeExtra}            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            MasterConfig.Load();
            _reloadSeen = MasterConfig.ReloadToken;
            ApplyMasterVerbose();
            if (MasterConfig.Enabled(PluginInfo.PLUGIN_NAME)) ApplyPatches();
            else Log.LogInfo(PluginInfo.PLUGIN_NAME + " is OFF in " + MasterConfig.FileName + " (no patches applied)");
            Config.SettingChanged += (sender, e) =>
            {
                Log.LogInfo("[" + e.ChangedSetting.Definition.Section + "] " + e.ChangedSetting.Definition.Key + " = " + e.ChangedSetting.BoxedValue);
{settingChangedExtra}            };
{postAwake}            Cfg.LogSummary({summaryArgs});
            Log.LogInfo("Master config: " + MasterConfig.Summary());
{awakeTail}        }

        /// <summary>Apply every [HarmonyPatch] nested in the patch containers, one at a time so a broken signature only loses one feature.</summary>
        private void ApplyPatches()
        {
            if (_patched) return;
            Type[] containers = {containers};
            foreach (Type container in containers)
            {
                foreach (Type nested in container.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (nested.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                    try
                    {
                        _harmony.CreateClassProcessor(nested).Patch();
                    }
                    catch (Exception e)
                    {
                        Log.LogError("Failed to apply " + container.Name + "." + nested.Name + ": " + e);
                    }
                }
            }
            _patched = true;
            Log.LogInfo("Patched " + _harmony.GetPatchedMethods().Count() + " methods");
        }

        private void RemovePatches()
        {
            if (!_patched) return;
{cleanup}            int n = _harmony.GetPatchedMethods().Count();
            _harmony.UnpatchSelf();
            _patched = false;
            Log.LogInfo(PluginInfo.PLUGIN_NAME + " patches removed (" + n + " methods) - mod is OFF");
        }

        /// <summary>The master file's VerboseLogging line, when present, wins over this mod's own setting.</summary>
        private void ApplyMasterVerbose()
        {
            if (Cfg == null || !MasterConfig.Verbose.HasValue) return;
            if (Cfg.Verbose.Value != MasterConfig.Verbose.Value) Cfg.Verbose.Value = MasterConfig.Verbose.Value;
        }

        private void Update()
        {
{updatePre}            bool reloadKey = Cfg != null && Cfg.ReloadKey.Value.IsDown() && !MasterConfig.MenuPresent; // with the mod menu present, F9 opens the window and Apply reloads
            if (reloadKey || MasterConfig.ReloadRequested(ref _reloadSeen))
            {
                Config.Reload();
                MasterConfig.Load();
                ApplyMasterVerbose();
                bool want = MasterConfig.Enabled(PluginInfo.PLUGIN_NAME);
                if (want && !_patched) ApplyPatches();
                else if (!want && _patched) RemovePatches();
{reloadExtra}                Log.LogInfo("Config reloaded (" + MasterConfig.Summary() + ")");
                Cfg.LogSummary({summaryArgs});
            }
        }

        private void OnDestroy()
        {
            RemovePatches();
        }
    }
}
'''

SPEC = {
    "DropRates": dict(containers="{ typeof(EnemyContextPatches), typeof(WorldLootPatches), typeof(PersonalLootPatches), typeof(MerchantPatches), typeof(TownShopPatches), typeof(GamblingPatches) }"),
    "DifficultyXP": dict(containers="{ typeof(XpPatches) }"),
    "QoL": dict(containers="{ typeof(UpgradeCostPatches) }",
                awakeTail="            LogOwner.LogMachine(Log);\n",
                updatePre="            LogOwner.TryLogSteam(Log);\n"),
    "TargetTooltip": dict(containers="{ typeof(HoverTooltipPatches) }",
                updatePre='''            if (_patched && Cfg != null && Cfg.StyleToggleKey.Value.IsDown())
            {
                Cfg.Style.Value = Cfg.Style.Value == TooltipStyle.Full ? TooltipStyle.Compact : TooltipStyle.Full;
                Config.Save();
                Log.LogInfo("Style toggled to " + Cfg.Style.Value + " (saved)");
            }
''',
                cleanup="            HoverTooltipPatches.RestoreTooltipVisuals();\n"),
    "SpecialTooltips": dict(containers="{ typeof(ExaminePatches) }",
                extraFields="        internal static Descriptions Desc;\n",
                awakeExtra='            Desc = new Descriptions(System.IO.Path.Combine(Paths.ConfigPath, "stolenrealm.specialtooltips.descriptions.txt"));\n            Desc.Load();\n',
                summaryArgs="Log, Desc",
                reloadExtra="                Desc.Load();\n"),
    "ScalingTooltips": dict(containers="{ typeof(ScalingPatches) }"),
    "SharedFortunes": dict(containers="{ typeof(FortunePatches) }",
                extraFields="        internal static FortunePool Pool;\n",
                awakeExtra="            Pool = new FortunePool();\n",
                postAwake="            Pool.LoadAndSeed();\n",
                summaryArgs="Log, Pool",
                updatePre="            if (_patched) { try { FortunePatches.Tick(); } catch { } }\n",
                cleanup="            FortunePatches.Reset();\n",
                reloadExtra="                Pool.LoadAndSeed();\n"),
    "FortunePreview": dict(containers="{ typeof(QuestTooltipPatches), typeof(EventOptionPatches), typeof(FortuneTooltipPatches), typeof(FortuneWindowPatches) }",
                settingChangedExtra="                FortuneResolver.ClearCache();\n                FortuneSources.ClearCache();\n",
                updatePre="            if (_patched) { try { QuestTooltipPatches.Tick(); } catch { } }\n",
                cleanup="            QuestTooltipPatches.Reset();\n            FortuneWindowPatches.Reset();\n",
                reloadExtra="                FortuneResolver.ClearCache();\n                FortuneSources.ClearCache();\n"),
    "LevelSync": dict(containers="{ typeof(LevelPatches) }",
                extraFields="        internal static LevelPool Pool;\n",
                awakeExtra="            Pool = new LevelPool();\n",
                postAwake="            Pool.LoadAndSeed();\n",
                summaryArgs="Log, Pool",
                updatePre="            if (_patched) { try { LevelPatches.Tick(); } catch { } }\n",
                cleanup="            LevelPatches.Reset();\n",
                reloadExtra="                Pool.LoadAndSeed();\n"),
    "AutoSalvage": dict(containers="{ typeof(SalvagePatches) }",
                updatePre="            if (_patched) { try { SalvagePatches.Tick(); } catch { } }\n",
                cleanup="            SalvagePatches.Reset();\n"),
    "SharedGold": dict(containers="{ typeof(GoldPatches) }",
                updatePre="            if (_patched) { try { GoldPatches.Tick(); } catch { } }\n",
                cleanup="            GoldPatches.Reset();\n",
                reloadExtra="                if (_patched) GoldPatches.Consolidate(\"reload\");\n"),
    "BattleStats": dict(containers="{ typeof(RecorderPatches), typeof(StatsWindowPatches), typeof(RunStatsPatches) }",
                updatePre="            if (_patched) { try { StatsWindowPatches.Tick(); } catch { } }\n            if (_patched) { try { RunStatsPatches.Tick(); } catch (Exception e) { if (Cfg != null && Cfg.Verbose.Value) Log.LogWarning(\"Run stats tick: \" + e); } }\n            if (_patched) { try { RunHistoryWindow.Tick(); } catch (Exception e) { if (Cfg != null && Cfg.Verbose.Value) Log.LogWarning(\"Run history tick: \" + e); } }\n",
                cleanup="            RecorderPatches.Reset();\n            StatsWindowPatches.Reset();\n            RunStatsPatches.Reset();\n"),
    "ThreatOverlay": dict(containers="{ typeof(OverlayPatches) }",
                updatePre="            if (_patched) { try { OverlayPatches.Tick(); } catch { } }" + chr(10),
                cleanup="            OverlayPatches.Reset();" + chr(10)),
    "ModMenu": dict(containers="{ typeof(MenuPatches) }",
                updatePre='            if (_patched) { try { MenuWindow.Tick(); } catch (Exception e) { if (Cfg != null && Cfg.Verbose.Value) Log.LogWarning("Tick: " + e); } }\n',
                cleanup="            MenuPatches.Reset();\n"),
    "SharedProgress": dict(containers="{ typeof(ProgressPatches) }",
                extraFields="        internal static ProgressPool Pool;\n",
                awakeExtra="            Pool = new ProgressPool();\n",
                postAwake="            Pool.LoadAndSeed();\n",
                summaryArgs="Log, Pool",
                updatePre="            if (_patched) { try { ProgressPatches.Tick(); } catch { } }\n",
                cleanup="            ProgressPatches.Reset();\n",
                reloadExtra="                Pool.LoadAndSeed();\n"),
    "NumberFormat": dict(containers="{ typeof(NumberPatches) }",
                cleanup="            NumberPatches.Reset();\n"),
    "ItemSkillTooltips": dict(containers="{ typeof(ItemSkillPatches) }",
                cleanup="            ItemSkillPatches.Reset();\n"),
    "SellValue": dict(containers="{ typeof(SellPatches) }",
                updatePre='            if (_patched) { try { SellPatches.Tick(); } catch (Exception e) { if (Cfg != null && Cfg.Verbose.Value) Log.LogWarning("Tick: " + e); } }\n',
                cleanup="            SellPatches.Reset();\n"),
    "RoguelikeQoL": dict(containers="{ typeof(EventPatches), typeof(CurrencyPatches), typeof(RarityPatches) }",
                cleanup="            EventPatches.Reset();\n            CurrencyPatches.Reset();\n            RarityPatches.Reset();\n",
                reloadExtra="                EventPatches.Reset();\n"),
    "FortuneUpgrade": dict(containers="{ typeof(UpgradePatches) }",
                updatePre='            if (_patched) { try { UpgradePatches.Tick(); } catch (Exception e) { if (Cfg != null && Cfg.Verbose.Value) Log.LogWarning("Tick: " + e); } }\n',
                cleanup="            UpgradePatches.Reset();\n"),
}
# Rule (all Tick-style hooks): anything a mod does from Update must be gated on _patched, so the master switch really
# turns the mod off; and RemovePatches clears the mod's static state so nothing stale acts when it is switched back on.

for p in PLUGINS:
    spec = SPEC[p]
    d = dict(ns=p, extraFields="", awakeExtra="", settingChangedExtra="", postAwake="", summaryArgs="Log", awakeTail="", cleanup="", updatePre="", reloadExtra="")
    d.update(spec)
    src = PLUGIN
    for k, v in d.items():
        src = src.replace("{" + k + "}", v)
    assert "{" + "ns" + "}" not in src
    leftover = re.findall(r"\{[a-zA-Z]+\}", src)
    assert not leftover, (p, leftover)
    with open(os.path.join(ROOT, p, "src", "Plugin.cs"), "w", encoding="utf-8") as f:
        f.write(src)
    with open(os.path.join(ROOT, p, "src", "MasterConfig.cs"), "w", encoding="utf-8") as f:
        f.write(MASTER.replace("{ns}", p))
    print("wrote", p)
