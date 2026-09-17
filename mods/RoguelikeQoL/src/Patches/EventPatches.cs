using System;
using System.Collections.Generic;
using Burst2Flame;
using HarmonyLib;
using MapNodeSystem;

namespace RoguelikeQoL.Patches
{
    /// <summary>
    /// Two changes to how an island's main event is drawn (WorldMapGenerator.GetRandomEvent):
    ///
    /// 1. Fortune odds. Vanilla flips a fair coin (rand.NextDouble() >= 0.5) and then only allows events whose LeadsToFortune
    ///    matches the flip, so exactly half of all event nodes lead to a fortune. The coin is the first use of the
    ///    System.Random the caller creates for that one call, so a prefix swaps in a wrapper whose first NextDouble()
    ///    answers according to FortuneNodeChance and which delegates everything else to the original. Nothing else in the
    ///    draw changes (deck history, ChanceRatio weights, fallbacks).
    /// 2. Junk list. PartyEvent.CanSpawn is the candidate filter for every draw and for the fallback passes; a prefix on
    ///    all three overloads answers false for events whose name is in RemoveEvents.
    ///
    /// Both apply only while Game.Instance.RoguelikeModeActive unless the campaign switches are on. In co-op the host
    /// generates the islands, so the host's settings decide.
    /// </summary>
    internal static class EventPatches
    {
        private static HashSet<string> _junk;
        private static bool _lastForced;
        private static int _lastRoll = -1;
        private static bool _lastBiased;

        private static RoguelikeQoLConfig Cfg => RoguelikeQoLPlugin.Cfg;

        internal static void Reset()
        {
            _junk = null; _lastRoll = -1; _lastBiased = false;
        }

        private static bool Roguelike()
        {
            try { return Game.Instance != null && Game.Instance.RoguelikeModeActive; } catch { return false; }
        }

        // ---------- junk list

        private static HashSet<string> Junk()
        {
            if (_junk != null) return _junk;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Cfg != null && !string.IsNullOrEmpty(Cfg.RemoveEvents.Value))
                foreach (string raw in Cfg.RemoveEvents.Value.Split(','))
                {
                    string s = raw.Trim();
                    if (s.Length > 0) set.Add(s);
                }
            _junk = set;
            return set;
        }

        internal static int JunkCount(RoguelikeQoLConfig cfg)
        {
            int n = 0;
            if (cfg != null && !string.IsNullOrEmpty(cfg.RemoveEvents.Value))
                foreach (string raw in cfg.RemoveEvents.Value.Split(',')) if (raw.Trim().Length > 0) n++;
            return n;
        }

        private static bool IsJunk(PartyEvent e)
        {
            if (e == null || Cfg == null) return false;
            if (!Cfg.RemoveInCampaign.Value && !Roguelike()) return false;
            HashSet<string> junk = Junk();
            if (junk.Count == 0) return false;
            try
            {
                if (junk.Contains(e.name)) return true;
                if (!string.IsNullOrEmpty(e.EventNameOverride) && junk.Contains(e.EventNameOverride)) return true;
            }
            catch { }
            return false;
        }

        private static void LogBlocked(PartyEvent e)
        {
            if (Cfg != null && Cfg.Verbose.Value) RoguelikeQoLPlugin.Log.LogInfo("Blocked event '" + e.name + "' (junk list)");
        }

        [HarmonyPatch(typeof(PartyEvent), nameof(PartyEvent.CanSpawn), new[] { typeof(TerrainAndTime), typeof(NodeType), typeof(List<ActionStatus>), typeof(EventSpawnType), typeof(int) })]
        private static class CanSpawnFull
        {
            private static bool Prefix(PartyEvent __instance, ref bool __result)
            {
                try { if (IsJunk(__instance)) { __result = false; LogBlocked(__instance); return false; } } catch { }
                return true;
            }
        }

        [HarmonyPatch(typeof(PartyEvent), nameof(PartyEvent.CanSpawn), new[] { typeof(TerrainAndTime), typeof(NodeType) })]
        private static class CanSpawnTerrain
        {
            private static bool Prefix(PartyEvent __instance, ref bool __result)
            {
                try { if (IsJunk(__instance)) { __result = false; return false; } } catch { }
                return true;
            }
        }

        [HarmonyPatch(typeof(PartyEvent), nameof(PartyEvent.CanSpawn), new[] { typeof(NodeType) })]
        private static class CanSpawnNode
        {
            private static bool Prefix(PartyEvent __instance, ref bool __result)
            {
                try { if (IsJunk(__instance)) { __result = false; return false; } } catch { }
                return true;
            }
        }

        // ---------- fortune odds

        /// <summary>System.Random whose first NextDouble() is decided by us; everything else goes to the wrapped instance.</summary>
        private sealed class BiasedRandom : Random
        {
            private readonly Random _inner;
            private bool _first = true;
            private readonly bool _fortune;
            public BiasedRandom(Random inner, bool fortune) { _inner = inner ?? new Random(); _fortune = fortune; }
            public override double NextDouble()
            {
                if (_first) { _first = false; return _fortune ? 0.75 : 0.25; }
                return _inner.NextDouble();
            }
            public override int Next() { return _inner.Next(); }
            public override int Next(int maxValue) { return _inner.Next(maxValue); }
            public override int Next(int minValue, int maxValue) { return _inner.Next(minValue, maxValue); }
            public override void NextBytes(byte[] buffer) { _inner.NextBytes(buffer); }
            protected override double Sample() { return _inner.NextDouble(); }
        }

        [HarmonyPatch(typeof(WorldMapGenerator), nameof(WorldMapGenerator.GetRandomEvent), new[] { typeof(NodeTypeEventSet), typeof(Random), typeof(TerrainAndTime), typeof(List<Character>), typeof(int), typeof(bool), typeof(bool), typeof(bool) })]
        private static class GetRandomEvent
        {
            private static void Prefix(NodeTypeEventSet nodeTypeEventSet, ref Random rand, bool restrictEventsWithBattle)
            {
                _lastBiased = false; _lastRoll = -1;
                try
                {
                    if (Cfg == null || nodeTypeEventSet == null) return;
                    if (nodeTypeEventSet.nodeType != NodeType.Event || nodeTypeEventSet.isMinor || restrictEventsWithBattle) return;
                    if (!Cfg.ApplyInCampaign.Value && !Roguelike()) return;
                    int chance = Cfg.FortuneNodeChance.Value;
                    _lastRoll = UnityEngine.Random.Range(0, 100);
                    _lastForced = _lastRoll < chance;
                    rand = new BiasedRandom(rand, _lastForced);
                    _lastBiased = true;
                }
                catch (Exception e) { RoguelikeQoLPlugin.Log.LogWarning("GetRandomEvent prefix failed: " + e); }
            }

            private static void Postfix(NodeTypeEventSet nodeTypeEventSet, bool restrictEventsWithBattle, PartyEvent __result)
            {
                try
                {
                    if (Cfg == null || !Cfg.Verbose.Value || nodeTypeEventSet == null) return;
                    string pick = __result == null ? "(none)" : "'" + (string.IsNullOrEmpty(__result.EventNameOverride) ? __result.name : __result.EventNameOverride) + "' [" + (__result.LeadsToFortune ? "fortune" : "no fortune") + "]";
                    string how = _lastBiased ? "forced fortune=" + _lastForced + " (roll " + _lastRoll + " vs " + Cfg.FortuneNodeChance.Value + "%)" : (restrictEventsWithBattle ? "battle-restricted node, vanilla" : "vanilla coin");
                    RoguelikeQoLPlugin.Log.LogInfo("Event node " + nodeTypeEventSet.nodeType + (nodeTypeEventSet.isMinor ? " (minor)" : "") + ": " + how + " -> " + pick);
                }
                catch { }
            }
        }
    }
}
