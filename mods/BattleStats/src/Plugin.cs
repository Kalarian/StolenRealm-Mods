using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using BattleStats.Patches;

namespace BattleStats
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class BattleStatsPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static BattleStatsConfig Cfg;
        private Harmony _harmony;
        private bool _patched;
        private int _reloadSeen;

        private void Awake()
        {
            Log = Logger;
            MasterConfig.LogSessionStartOnce(Log);
            Cfg = new BattleStatsConfig(Config);
            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            MasterConfig.Load();
            _reloadSeen = MasterConfig.ReloadToken;
            ApplyMasterVerbose();
            if (MasterConfig.Enabled(PluginInfo.PLUGIN_NAME)) ApplyPatches();
            else Log.LogInfo(PluginInfo.PLUGIN_NAME + " is OFF in " + MasterConfig.FileName + " (no patches applied)");
            Config.SettingChanged += (sender, e) =>
            {
                Log.LogInfo("[" + e.ChangedSetting.Definition.Section + "] " + e.ChangedSetting.Definition.Key + " = " + e.ChangedSetting.BoxedValue);
            };
            Cfg.LogSummary(Log);
            Log.LogInfo("Master config: " + MasterConfig.Summary());
        }

        /// <summary>Apply every [HarmonyPatch] nested in the patch containers, one at a time so a broken signature only loses one feature.</summary>
        private void ApplyPatches()
        {
            if (_patched) return;
            Type[] containers = { typeof(RecorderPatches), typeof(StatsWindowPatches) };
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
            RecorderPatches.Reset();
            StatsWindowPatches.Reset();
            int n = _harmony.GetPatchedMethods().Count();
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
            if (_patched) { try { StatsWindowPatches.Tick(); } catch { } }
            bool reloadKey = Cfg != null && Cfg.ReloadKey.Value.IsDown() && !MasterConfig.MenuPresent; // with the mod menu present, F9 opens the window and Apply reloads
            if (reloadKey || MasterConfig.ReloadRequested(ref _reloadSeen))
            {
                Config.Reload();
                MasterConfig.Load();
                ApplyMasterVerbose();
                bool want = MasterConfig.Enabled(PluginInfo.PLUGIN_NAME);
                if (want && !_patched) ApplyPatches();
                else if (!want && _patched) RemovePatches();
                Log.LogInfo("Config reloaded (" + MasterConfig.Summary() + ")");
                Cfg.LogSummary(Log);
            }
        }

        private void OnDestroy()
        {
            RemovePatches();
        }
    }
}
