using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace ThreatOverlay.Patches
{
    /// <summary>
    /// While the hold key is down in battle: for every living enemy, walk outward from its hex with the game's own
    /// per-step cost (HexCellManager.HexCost: terrain, offsets, blocked cells, movement-changing ground effects) up to
    /// its next-turn movement budget (TurnFreeMovementPoints, zero when Rooted) to get the hexes it can stand on, then
    /// add every hex within its longest attack range of those. Reach hexes are tinted one colour, strike-only hexes
    /// another, by driving the same overlay object the game uses for its own move/action highlights. The game refreshes
    /// a cell's colour only from HexCell.UpdateHexCell, so a postfix there re-applies our tint while the key is held,
    /// and on release we call UpdateHexCell on every cell we touched so the game restores its own look.
    /// Read-only with respect to game state; nothing is networked.
    /// </summary>
    internal static class OverlayPatches
    {
        private static bool _held;
        private static float _nextRefresh;
        private static readonly HashSet<HexCell> _reach = new HashSet<HexCell>();
        private static readonly HashSet<HexCell> _strike = new HashSet<HexCell>();
        private static readonly HashSet<HexCell> _touched = new HashSet<HexCell>();
        private static Color _reachColor = new Color(0.78f, 0.2f, 0.2f, 1f);
        private static Color _strikeColor = new Color(0.88f, 0.54f, 0.24f, 1f);
        /// <summary>Set by the development TestDriver plugin (reflection) to simulate holding the key.</summary>
        public static bool TestForceHold;
        private static string _lastSig; private static string _detail = "";

        private static ThreatOverlayConfig Cfg => ThreatOverlayPlugin.Cfg;

        internal static void Reset() { Release(); }

        // ---------- per-frame ----------

        internal static void Tick()
        {
            ThreatOverlayConfig cfg = Cfg;
            if (cfg == null) return;
            bool inBattle = false;
            try { inBattle = cfg.Enabled.Value && GUIManager.instance != null && GUIManager.instance.CurrentGuiState == GUIState.InBattle && HexCellManager.instance != null; } catch { }
            bool typing = false;
            try { typing = MessageWindowManager.instance != null && MessageWindowManager.instance.TextInputIsFocused; } catch { }
            bool want = inBattle && !typing && (cfg.HoldKey.Value.IsPressed() || TestForceHold);
            if (!want)
            {
                if (_held) Release();
                return;
            }
            if (!_held || Time.unscaledTime >= _nextRefresh)
            {
                _held = true;
                _nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, cfg.RefreshSeconds.Value);
                try { Recompute(); Apply(); }
                catch (Exception e) { ThreatOverlayPlugin.Log.LogWarning("Threat overlay failed: " + e); Release(); }
            }
        }

        private static void Release()
        {
            _held = false;
            var cells = _touched.ToList();
            _reach.Clear(); _strike.Clear(); _touched.Clear();
            foreach (HexCell c in cells)
            {
                try { if (c != null) c.UpdateHexCell(); } catch { }
            }
        }

        // ---------- computing the threat map ----------

        private static void Recompute()
        {
            ThreatOverlayConfig cfg = Cfg;
            _reach.Clear(); _strike.Clear();
            ParseColors(cfg);
            HexCellManager mgr = HexCellManager.instance;
            Root root = NetworkingManager.Instance != null && NetworkingManager.Instance.NetworkManager != null ? NetworkingManager.Instance.NetworkManager.Root : null;
            if (mgr == null || root == null) return;
            IEnumerable<Character> enemies;
            if (cfg.Mode.Value == ThreatMode.HoveredEnemy)
            {
                Character hovered = mgr.CurrentlyHoveringHexCell != null ? mgr.CurrentlyHoveringHexCell.Player : null;
                enemies = hovered != null && hovered.TeamIndex == 1 ? new[] { hovered } : new Character[0];
            }
            else enemies = root.CharactersInBattleEnemyTeam ?? new List<Character>();

            int count = 0;
            foreach (Character e in enemies)
            {
                if (e == null || e.IsDead || e.Cell == null) continue;
                count++;
                float budget = Budget(e);
                int range = AttackRange(e, cfg);
                var reach = Reach(mgr, e, budget);
                foreach (HexCell c in reach) _reach.Add(c);
                if (cfg.ShowStrike.Value)
                {
                    foreach (HexCell c in Within(reach, range)) if (!_reach.Contains(c)) _strike.Add(c);
                }
                if (cfg.Verbose.Value) _detail += " | " + e.CharacterName + ": move " + budget.ToString("0.#") + ", range " + range + ", reach " + reach.Count;
            }
            if (!cfg.ShowReach.Value)
            {
                // strike-only view: still need the reach cells to be tinted as strike (an enemy standing next to you is a threat)
                foreach (HexCell c in _reach) _strike.Add(c);
                _reach.Clear();
            }
            if (cfg.Verbose.Value)
            {
                string sig = count + "/" + _reach.Count + "/" + _strike.Count;
                if (sig != _lastSig) { _lastSig = sig; ThreatOverlayPlugin.Log.LogInfo("Threat overlay: " + count + " enemies, " + _reach.Count + " reach + " + _strike.Count + " strike hexes" + _detail); }
            }
            _detail = "";
        }

        private static float Budget(Character e)
        {
            try
            {
                if (e["Rooted"] > 0f) return 0f;
                float next = e["TurnFreeMovementPoints"];
                float now = e["FreeMovementPoints"];
                return Mathf.Max(next, now);
            }
            catch { return 0f; }
        }

        private static int AttackRange(Character e, ThreatOverlayConfig cfg)
        {
            int range = 1;
            try { range = Mathf.Max(range, e.WeaponAttackRange); } catch { }
            if (!cfg.IncludeSkillRanges.Value) return range;
            try
            {
                foreach (ActionAndSkill aas in e.Actions)
                {
                    ActionInfo ai = aas.ActionInfo; // ActionAndSkill is a struct
                    if (ai == null || !ai.IsHarmful || ai.Targets == null || ai.Targets.Length == 0) continue;
                    TargetInfo t = ai.Targets[0] as TargetInfo;
                    if (t == null || !t.UseSimpleTargetingRange || t.TargetSelf) continue;
                    int r = ai.GetSimpleRange(e, t);
                    if (r > range && r < 50) range = r;
                }
            }
            catch { }
            return range;
        }

        /// <summary>Dijkstra over neighbours with the game's step cost; returns every hex whose cheapest path costs at most the budget.</summary>
        private static List<HexCell> Reach(HexCellManager mgr, Character e, float budget)
        {
            var best = new Dictionary<HexCell, float> { { e.Cell, 0f } };
            var open = new List<HexCell> { e.Cell };
            var result = new List<HexCell> { e.Cell };
            if (budget <= 0f) return result;
            while (open.Count > 0)
            {
                // smallest tentative cost first (maps are small; a linear scan is fine)
                int bi = 0; float bc = best[open[0]];
                for (int i = 1; i < open.Count; i++) { float c = best[open[i]]; if (c < bc) { bc = c; bi = i; } }
                HexCell cur = open[bi]; open.RemoveAt(bi);
                foreach (HexCell n in cur.Neighbors)
                {
                    if (n == null) continue;
                    float step;
                    try { step = mgr.HexCost(cur.Coordinates, n.Coordinates, e, true); } catch { continue; }
                    if (float.IsInfinity(step) || float.IsNaN(step)) continue;
                    float total = bc + step;
                    if (total > budget + 0.0001f) continue;
                    float known;
                    if (best.TryGetValue(n, out known) && known <= total) continue;
                    best[n] = total;
                    if (!open.Contains(n)) open.Add(n);
                    if (!result.Contains(n)) result.Add(n);
                }
            }
            return result;
        }

        /// <summary>All valid hexes within `range` steps (hex distance) of any cell in the set.</summary>
        private static HashSet<HexCell> Within(List<HexCell> from, int range)
        {
            var seen = new HashSet<HexCell>(from);
            var frontier = new List<HexCell>(from);
            for (int d = 0; d < range && frontier.Count > 0; d++)
            {
                var next = new List<HexCell>();
                foreach (HexCell c in frontier)
                    foreach (HexCell n in c.Neighbors)
                        if (n != null && seen.Add(n)) next.Add(n);
                frontier = next;
            }
            seen.RemoveWhere(c => !HexCellManager.IsValidAndEnabled(c));
            return seen;
        }

        // ---------- painting ----------

        private static void Apply()
        {
            foreach (HexCell c in _reach) Paint(c, _reachColor);
            foreach (HexCell c in _strike) Paint(c, _strikeColor);
            // cells that dropped out since the last refresh go back to normal
            foreach (HexCell c in _touched.ToList())
            {
                if (_reach.Contains(c) || _strike.Contains(c)) continue;
                _touched.Remove(c);
                try { c.UpdateHexCell(); } catch { }
            }
        }

        private static void Paint(HexCell c, Color color)
        {
            try
            {
                if (c == null) return;
                if (c.overlay == null) c.CreateOverlay();
                if (c.overlay == null) return;
                if (!c.overlay.activeSelf) c.overlay.SetActive(true);
                c.SetHighlightColor(color);
                var list = HexCellManager.instance != null ? HexCellManager.instance.HexCellsWithActiveOverlays : null;
                if (list != null && !list.Contains(c)) list.Add(c);
                _touched.Add(c);
            }
            catch { }
        }

        private static void ParseColors(ThreatOverlayConfig cfg)
        {
            Color c;
            if (ColorUtility.TryParseHtmlString("#" + cfg.ReachColor.Value.Trim().TrimStart('#'), out c)) _reachColor = c;
            if (ColorUtility.TryParseHtmlString("#" + cfg.StrikeColor.Value.Trim().TrimStart('#'), out c)) _strikeColor = c;
        }

        // The game only recolours a cell from UpdateHexCell; keep our tint on top while the key is held.
        [HarmonyPatch(typeof(HexCell), nameof(HexCell.UpdateHexCell))]
        private static class HexCell_UpdateHexCell
        {
            private static void Postfix(HexCell __instance)
            {
                if (!_held || __instance == null) return;
                try
                {
                    if (_reach.Contains(__instance)) Paint(__instance, _reachColor);
                    else if (_strike.Contains(__instance)) Paint(__instance, _strikeColor);
                }
                catch { }
            }
        }
    }
}
