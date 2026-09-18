using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Burst2Flame;
using Burst2Flame.Observable;
using HexMapTools;
using Newtonsoft.Json;
using UnityEngine;

namespace TestDriver
{
    /// <summary>
    /// Plays the party from a priority list (rotation.json, written by the build tester from the build or party spec).
    /// Each tick the first entry whose skill is affordable, whose conditions hold and that has a castable target is cast;
    /// when nothing applies the character basic-attacks the nearest enemy, else moves according to its formation, else
    /// ends the turn. Enemies are played by the game's own AI.
    ///
    /// rotation.json, single build:   {"priority":[{"skill":"Seal of Might","target":"self","when":["ready"]}, ...]}
    /// rotation.json, party:          {"characters":{"Warden":{"formation":"front","priority":[...]},"Longbow":{"formation":"behind","priority":[...]}}}
    /// formation: front (walk at the nearest enemy, the old behaviour) | behind (stay within 2 hexes of the front ally and
    ///   never adjacent to an enemy; a ranged character) | hold (never move on its own)
    /// target: self | ally (lowest-health party member incl. self) | partner (the other party member) | enemy (lowest health
    ///   in reach) | strongest (champion/boss, then highest max health) | enemies (best cluster) | ground
    /// when (all must hold): ready · turn==N · turn>=N · turn<=N · enemies_in_range>=N · enemies_clustered>=N ·
    ///   enemies>=N · enemies<=N · health<N · health>N · mana<N · mana>N · has_status:X · no_status:X · boss_present ·
    ///   ally_health<N · ally_health>N (the other member) · ally_adjacent · ally_has_status:X · ally_no_status:X ·
    ///   enemies_near_ally>=N (within 1 hex of the other member) · partner_in_range (this skill can be cast on the partner)
    /// </summary>
    internal sealed class Rotation
    {
        public sealed class Entry
        {
            public string skill;
            public string target = "enemy";
            public List<string> when = new List<string>();
            public string source = null;
        }

        public sealed class CharacterPlan
        {
            public string formation = "front";
            public List<Entry> priority = new List<Entry>();
        }

        public sealed class File
        {
            public List<Entry> priority = null;
            public Dictionary<string, CharacterPlan> characters = null;
        }

        private readonly File _file;
        private readonly Action<string> _say;
        private readonly HashSet<string> _unknownLogged = new HashSet<string>();
        private int _turn = -1;
        private readonly Dictionary<Character, HashSet<ActionInfo>> _failedThisTurn = new Dictionary<Character, HashSet<ActionInfo>>();
        private readonly Dictionary<Character, int> _actsThisTurn = new Dictionary<Character, int>();
        private readonly HashSet<Character> _movedThisTurn = new HashSet<Character>();
        public int Casts, Basics, Moves, TurnEnds;
        public readonly List<string> Trace = new List<string>();

        public static Rotation Load(string path, Action<string> say)
        {
            try
            {
                File f = JsonConvert.DeserializeObject<File>(System.IO.File.ReadAllText(path));
                if (f == null || ((f.priority == null || f.priority.Count == 0) && (f.characters == null || f.characters.Count == 0))) { say("rotation: empty file " + path); return null; }
                if (f.characters != null && f.characters.Count > 0)
                    say("rotation: party plan for " + string.Join(", ", f.characters.Select(kv => kv.Key + " (" + kv.Value.formation + ", " + kv.Value.priority.Count + " entries)").ToArray()));
                else say("rotation: " + f.priority.Count + " entries from " + Path.GetFileName(path) + ": " + string.Join(" > ", f.priority.Select(e => e.skill).ToArray()));
                return new Rotation(f, say);
            }
            catch (Exception e) { say("rotation: could not read " + path + ": " + e.Message); return null; }
        }

        private Rotation(File f, Action<string> say) { _file = f; _say = say; }

        public void Reset() { _turn = -1; _failedThisTurn.Clear(); _actsThisTurn.Clear(); _movedThisTurn.Clear(); }

        private CharacterPlan PlanFor(Character c)
        {
            if (_file.characters != null)
            {
                foreach (var kv in _file.characters) if (string.Equals(kv.Key, c.CharacterName, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                if (_unknownLogged.Add("plan:" + c.CharacterName)) _say("rotation: no plan for " + c.CharacterName + "; using the default list");
            }
            return new CharacterPlan { formation = "front", priority = _file.priority ?? new List<Entry>() };
        }

        private void NewTurn(int turn)
        {
            if (turn == _turn) return;
            _turn = turn; _failedThisTurn.Clear(); _actsThisTurn.Clear(); _movedThisTurn.Clear();
        }

        private static List<Character> Party()
        {
            var list = new List<Character>();
            try { foreach (Character p in NetworkingManager.Instance.PartyCharacters) if (p != null && !p.IsDead && p.Cell != null) list.Add(p); } catch { }
            return list;
        }

        private static Character Partner(Character c)
        {
            Character best = null; int bd = int.MaxValue;
            foreach (Character p in Party()) { if (p == c) continue; int d = c.Cell.Distance(p.Cell); if (d < bd) { bd = d; best = p; } }
            return best;
        }

        /// <summary>One thing for this character; true when something was done (cast, attack, move or end turn).</summary>
        public bool Act(Character c, List<Character> enemies, int turn)
        {
            NewTurn(turn);
            CharacterPlan plan = PlanFor(c);
            int acts; _actsThisTurn.TryGetValue(c, out acts);
            if (acts > 14) { End(c, "safety cap"); return true; }
            HashSet<ActionInfo> failed;
            if (!_failedThisTurn.TryGetValue(c, out failed)) _failedThisTurn[c] = failed = new HashSet<ActionInfo>();

            Dictionary<string, ActionInfo> known = Known(c);
            foreach (Entry e in plan.priority)
            {
                ActionInfo ai;
                if (string.IsNullOrEmpty(e.skill) || !known.TryGetValue(Key(e.skill), out ai))
                {
                    if (!string.IsNullOrEmpty(e.skill) && _unknownLogged.Add(c.CharacterName + ":" + e.skill)) _say("rotation: '" + e.skill + "' is not an action " + c.CharacterName + " has (known: " + string.Join(", ", known.Keys.Take(40).ToArray()) + ")");
                    continue;
                }
                if (failed.Contains(ai)) continue;
                int ap = 0, mana = 0;
                try { ap = ai.GetActionCost(c); mana = ai.GetManaCost(c); } catch { failed.Add(ai); continue; }
                if (ap > c.ActionPoints || mana > c["Mana"]) continue;
                if (!Conditions(c, ai, e, enemies, turn)) continue;
                HexCell cell = PickCell(c, ai, e.target, enemies);
                if (cell == null) { failed.Add(ai); continue; }
                if (!Cast(c, ai, cell)) { failed.Add(ai); continue; }
                _actsThisTurn[c] = acts + 1;
                Casts++;
                Trace.Add("T" + turn + " " + c.CharacterName + ": " + Name(ai) + " -> " + Describe(cell));
                return true;
            }

            // fallback: basic attack the nearest castable enemy (works from range for a bow)
            if (c.BasicAttacks != null && c.ActionPoints > 0)
            {
                foreach (ActionInfo ba in c.BasicAttacks)
                {
                    if (ba == null || failed.Contains(ba)) continue;
                    int ap = 0; try { ap = ba.GetActionCost(c); } catch { continue; }
                    if (ap > c.ActionPoints) continue;
                    foreach (Character t in enemies.OrderBy(x => c.Cell.Distance(x.Cell)))
                    {
                        if (CanCast(c, ba, t.Cell))
                        {
                            if (!Cast(c, ba, t.Cell)) continue;
                            Basics++; _actsThisTurn[c] = acts + 1;
                            Trace.Add("T" + turn + " " + c.CharacterName + ": basic " + Name(ba) + " -> " + t.CharacterName);
                            return true;
                        }
                    }
                    failed.Add(ba);
                }
            }
            if (c.FreeMovementPoints > 0f && !_movedThisTurn.Contains(c) && enemies.Count > 0)
            {
                _movedThisTurn.Add(c);
                string form = (plan.formation ?? "front").ToLowerInvariant();
                if (form == "front")
                {
                    Character target = enemies.OrderBy(x => c.Cell.Distance(x.Cell)).First();
                    if (c.Cell.Distance(target.Cell) > 1 && MoveToward(c, target.Cell, target.Cell)) { Moves++; Trace.Add("T" + turn + " " + c.CharacterName + ": move toward " + target.CharacterName); return true; }
                }
                else if (form == "behind")
                {
                    HexCell dest = BehindCell(c, enemies);
                    if (dest != null && dest != c.Cell && MoveToward(c, dest, null)) { Moves++; Trace.Add("T" + turn + " " + c.CharacterName + ": reposition behind -> " + dest.Coordinates); return true; }
                }
            }
            End(c, null);
            return true;
        }

        /// <summary>A cell within 2 hexes of the partner, reachable this turn, not adjacent to any enemy, as far from the
        /// enemies as possible. Null when the current cell is already fine.</summary>
        private static HexCell BehindCell(Character c, List<Character> enemies)
        {
            Character partner = Partner(c);
            HexCell anchor = partner != null ? partner.Cell : c.Cell;
            Func<HexCell, int> nearestEnemy = h => { int m = int.MaxValue; foreach (Character e in enemies) m = Math.Min(m, h.Distance(e.Cell)); return m; };
            bool okNow = c.Cell.Distance(anchor) <= 2 && nearestEnemy(c.Cell) >= 2;
            if (okNow) return null;
            var seen = new HashSet<HexCell> { anchor };
            var frontier = new List<HexCell> { anchor };
            var ring = new List<HexCell>();
            for (int r = 0; r < 2; r++)
            {
                var next = new List<HexCell>();
                foreach (HexCell h in frontier) foreach (HexCell n in h.Neighbors) if (n != null && seen.Add(n)) { next.Add(n); ring.Add(n); }
                frontier = next;
            }
            HexCell best = null; int bestScore = int.MinValue;
            foreach (HexCell h in ring)
            {
                if (!(h.IsEmpty || h == c.Cell) || !HexCellManager.IsValidAndEnabled(h)) continue;
                int ne = nearestEnemy(h);
                if (ne < 2) continue;
                int score = ne * 10 - h.Distance(c.Cell); // far from enemies, short walk
                if (score > bestScore) { bestScore = score; best = h; }
            }
            return best;
        }

        private void End(Character c, string why)
        {
            c.PlayerMovement.EndTurn(); TurnEnds++;
            Trace.Add("T" + _turn + " " + c.CharacterName + ": end turn" + (why != null ? " (" + why + ")" : ""));
        }

        /// <summary>The game's own cast path: pays costs, sets cooldowns, consumes charges, rolls Recharge, starts the action.</summary>
        private static bool Cast(Character c, ActionInfo ai, HexCell cell)
        {
            try { return c.PlayerMovement.ExecuteAction(new StructList<HexCell> { cell }, ai, null); } catch { return false; }
        }

        /// <summary>Walk along the path to dest as far as this turn's movement allows; stopCell is excluded (an enemy's own cell).</summary>
        private static bool MoveToward(Character c, HexCell dest, HexCell stopCell)
        {
            var path = new List<HexCoordinates>();
            HexCellManager.instance.GetHexPathCoordinates(c.Cell.Coordinates, dest.Coordinates, c, ref path);
            HexCell last = null; float spent = 0f; HexCoordinates prev = c.Cell.Coordinates;
            foreach (HexCoordinates hc in path)
            {
                if (hc == c.Cell.Coordinates) continue;
                float step = HexCellManager.instance.HexCost(prev, hc, c, true);
                if (float.IsInfinity(step) || spent + step > c.FreeMovementPoints) break;
                HexCell cell = HexCellManager.instance.cells[hc];
                if (cell == null || cell == stopCell || cell.Player != null) break;
                spent += step; last = cell; prev = hc;
            }
            if (last == null) return false;
            c.SendSetDestinationToServer(c.Cell.transform.position, last.transform.position, c);
            return true;
        }

        // ---- lookup

        private static string Key(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            int i = s.IndexOf("] ", StringComparison.Ordinal);
            if (s.StartsWith("[") && i > 0) s = s.Substring(i + 2);
            return s;
        }

        private static string Name(ActionInfo ai)
        {
            try { if (ai.OverrideActionName && !string.IsNullOrEmpty(ai.ActionNameOverride)) return ai.ActionNameOverride; } catch { }
            return ai.name;
        }

        private static Dictionary<string, ActionInfo> Known(Character c)
        {
            var d = new Dictionary<string, ActionInfo>();
            try
            {
                foreach (ActionAndSkill aas in c.Actions)
                {
                    if (aas.ActionInfo == null) continue;
                    if (aas.SkillInfo != null && !string.IsNullOrEmpty(aas.SkillInfo.SkillName) && !d.ContainsKey(Key(aas.SkillInfo.SkillName))) d[Key(aas.SkillInfo.SkillName)] = aas.ActionInfo;
                    string n = Key(Name(aas.ActionInfo)); if (!d.ContainsKey(n)) d[n] = aas.ActionInfo;
                    string raw = Key(aas.ActionInfo.name); if (!d.ContainsKey(raw)) d[raw] = aas.ActionInfo;
                }
            }
            catch { }
            return d;
        }

        // ---- conditions

        private bool Conditions(Character c, ActionInfo ai, Entry e, List<Character> enemies, int turn)
        {
            if (e.when == null) return true;
            Character partner = null; bool partnerLooked = false;
            Func<Character> P = () => { if (!partnerLooked) { partner = Partner(c); partnerLooked = true; } return partner; };
            foreach (string raw in e.when)
            {
                string w = (raw ?? "").Trim().ToLowerInvariant().Replace(" ", "");
                if (w.Length == 0 || w == "ready") continue;
                int n;
                if (w.StartsWith("turn==")) { if (!(Int(w, 6, out n) && turn == n)) return false; }
                else if (w.StartsWith("turn>=")) { if (!(Int(w, 6, out n) && turn >= n)) return false; }
                else if (w.StartsWith("turn<=")) { if (!(Int(w, 6, out n) && turn <= n)) return false; }
                else if (w.StartsWith("enemies_in_range>=")) { if (!(Int(w, 18, out n) && EnemiesInRange(c, ai, e, enemies) >= n)) return false; }
                else if (w.StartsWith("enemies_clustered>=")) { if (!(Int(w, 19, out n) && BestCluster(c, ai, enemies).Value >= n)) return false; }
                else if (w.StartsWith("enemies_near_ally>=")) { if (!(Int(w, 19, out n) && P() != null && enemies.Count(t => P().Cell.Distance(t.Cell) <= 1) >= n)) return false; }
                else if (w.StartsWith("enemies>=")) { if (!(Int(w, 9, out n) && enemies.Count >= n)) return false; }
                else if (w.StartsWith("enemies<=")) { if (!(Int(w, 9, out n) && enemies.Count <= n)) return false; }
                else if (w.StartsWith("health<")) { if (!(Int(w, 7, out n) && c.HealthRatio * 100f < n)) return false; }
                else if (w.StartsWith("health>")) { if (!(Int(w, 7, out n) && c.HealthRatio * 100f > n)) return false; }
                else if (w.StartsWith("mana<")) { if (!(Int(w, 5, out n) && c.ManaRatio * 100f < n)) return false; }
                else if (w.StartsWith("mana>")) { if (!(Int(w, 5, out n) && c.ManaRatio * 100f > n)) return false; }
                else if (w.StartsWith("ally_health<")) { if (!(Int(w, 12, out n) && P() != null && P().HealthRatio * 100f < n)) return false; }
                else if (w.StartsWith("ally_health>")) { if (!(Int(w, 12, out n) && P() != null && P().HealthRatio * 100f > n)) return false; }
                else if (w == "ally_adjacent") { if (!(P() != null && c.Cell.Distance(P().Cell) <= 1)) return false; }
                else if (w == "partner_in_range") { if (!(P() != null && CanCast(c, ai, P().Cell))) return false; }
                else if (w.StartsWith("ally_has_status:")) { if (!(P() != null && HasStatus(P(), raw.Substring(raw.IndexOf(':') + 1)))) return false; }
                else if (w.StartsWith("ally_no_status:")) { if (P() != null && HasStatus(P(), raw.Substring(raw.IndexOf(':') + 1))) return false; }
                else if (w.StartsWith("has_status:")) { if (!HasStatus(c, raw.Substring(raw.IndexOf(':') + 1))) return false; }
                else if (w.StartsWith("no_status:")) { if (HasStatus(c, raw.Substring(raw.IndexOf(':') + 1))) return false; }
                else if (w == "boss_present") { if (!enemies.Any(t => t.EnemyType == EnemyType.Boss || t.EnemyType == EnemyType.Champion)) return false; }
                else { if (_unknownLogged.Add("cond:" + raw)) _say("rotation: unknown condition '" + raw + "' (ignored)"); }
            }
            return true;
        }

        private static bool Int(string w, int from, out int n) { return int.TryParse(w.Substring(from), out n); }

        private static int EnemiesInRange(Character c, ActionInfo ai, Entry e, List<Character> enemies)
        {
            string tg = (e.target ?? "enemy").Trim().ToLowerInvariant();
            if (tg == "enemy" || tg == "enemies" || tg == "strongest") return enemies.Count(t => CanCast(c, ai, t.Cell));
            return enemies.Count(t => c.Cell.Distance(t.Cell) <= 4);
        }

        /// <summary>Match a status by display name, asset name or the GL- global name.</summary>
        private static bool HasStatus(Character c, string name)
        {
            name = name.Trim();
            try
            {
                foreach (ActionStatus s in c.ActionStatuses)
                {
                    if (s == null || s.ActionStatusInfo == null) continue;
                    string asset = s.ActionStatusInfo.name ?? "", disp = s.ActionStatusInfo.Name ?? "";
                    if (string.Equals(asset, name, StringComparison.OrdinalIgnoreCase) || string.Equals(disp, name, StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(asset, "GL-" + name, StringComparison.OrdinalIgnoreCase)) return true;
                    int us = asset.LastIndexOf('_');
                    if (us >= 0 && string.Equals(asset.Substring(us + 1), name, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        // ---- targeting

        private static bool CanCast(Character c, ActionInfo ai, HexCell cell)
        {
            if (cell == null) return false;
            try { return c.PlayerMovement.CanCast(new StructList<HexCell> { cell }, ai).CanCast; } catch { return false; }
        }

        private static KeyValuePair<HexCell, int> BestCluster(Character c, ActionInfo ai, List<Character> enemies)
        {
            HexCell best = null; int bestN = 0; int bestDist = int.MaxValue;
            foreach (HexCell cell in ClusterCandidates(enemies))
            {
                int n = enemies.Count(t => t.Cell == cell || t.Cell.Distance(cell) <= 1);
                int dist = c.Cell.Distance(cell);
                if (n > bestN || (n == bestN && dist < bestDist))
                {
                    if (!CanCast(c, ai, cell)) continue;
                    best = cell; bestN = n; bestDist = dist;
                }
            }
            return new KeyValuePair<HexCell, int>(best, bestN);
        }

        private static IEnumerable<HexCell> ClusterCandidates(List<Character> enemies)
        {
            var seen = new HashSet<HexCell>();
            foreach (Character t in enemies) if (t.Cell != null && seen.Add(t.Cell)) yield return t.Cell;
            foreach (Character t in enemies)
                if (t.Cell != null)
                    foreach (HexCell n in t.Cell.Neighbors)
                        if (n != null && n.IsEmpty && HexCellManager.IsValidAndEnabled(n) && seen.Add(n)) yield return n;
        }

        private HexCell PickCell(Character c, ActionInfo ai, string target, List<Character> enemies)
        {
            switch ((target ?? "enemy").Trim().ToLowerInvariant())
            {
                case "self":
                    return CanCast(c, ai, c.Cell) ? c.Cell : null;
                case "partner":
                {
                    Character p = Partner(c);
                    return p != null && CanCast(c, ai, p.Cell) ? p.Cell : null;
                }
                case "ally":
                {
                    foreach (Character a in Party().OrderBy(a => a.HealthRatio)) if (CanCast(c, ai, a.Cell)) return a.Cell;
                    return null;
                }
                case "enemies":
                    return BestCluster(c, ai, enemies).Key;
                case "strongest":
                {
                    foreach (Character t in enemies.OrderByDescending(x => (x.EnemyType == EnemyType.Champion || x.EnemyType == EnemyType.Boss) ? 1 : 0).ThenByDescending(x => x.MaxHealth)) if (CanCast(c, ai, t.Cell)) return t.Cell;
                    return null;
                }
                case "ground":
                {
                    foreach (Character t in enemies.OrderBy(x => c.Cell.Distance(x.Cell)))
                        foreach (HexCell n in t.Cell.Neighbors)
                            if (n != null && n.IsEmpty && HexCellManager.IsValidAndEnabled(n) && CanCast(c, ai, n)) return n;
                    foreach (HexCell n in c.Cell.Neighbors) if (n != null && n.IsEmpty && HexCellManager.IsValidAndEnabled(n) && CanCast(c, ai, n)) return n;
                    return null;
                }
                default: // enemy: lowest health first among castable targets, nearest as tie-break
                {
                    foreach (Character t in enemies.OrderBy(x => x.HealthRatio).ThenBy(x => c.Cell.Distance(x.Cell))) if (CanCast(c, ai, t.Cell)) return t.Cell;
                    return null;
                }
            }
        }

        private static string Describe(HexCell cell)
        {
            try { return cell.Player != null ? cell.Player.CharacterName : "hex " + cell.Coordinates; } catch { return "hex"; }
        }
    }
}
