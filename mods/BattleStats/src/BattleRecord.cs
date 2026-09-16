using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BattleStats
{
    /// <summary>How a credit reached the stat system. Innermost hook wins (a status tick inside a tile's action is a status tick).</summary>
    internal enum CreditPath { Direct, StatusTick, GroundEnter, TurnStart, GroundCreate, Returned, Summon }

    internal sealed class CreditEvent
    {
        public int Turn;
        public string Stat;            // BattleStat name
        public string Character;       // who is credited (player)
        public float Amount;
        public string Path;            // CreditPath
        public string Action;          // skill / action name (asset name)
        public string ActionDisplay;   // localised name if available
        public string Status;          // status name when Path is StatusTick
        public string Target;          // the character hit / healed
        public bool TargetIsPlayer;
        public bool Crit;
        public float TargetHealthBefore;
        public float Overkill;         // damage beyond the target's remaining health
        public bool Kill;              // this credit brought the target to 0
        public string Element;         // DamageType of the hit (Physical, Fire, Cold, Lightning, Shadow, Holy...) when known
    }

    internal sealed class CastEvent
    {
        public int Turn;
        public string Character;
        public string Action;
        public string Kind;            // Action or FreeAction
        public int ActionCost;
        public int ManaCost;
    }

    internal sealed class SkillTotal
    {
        public string Name;
        public float Damage;
        public int Hits;
        public int Crits;
        public float Biggest;
    }

    internal sealed class CharacterSummary
    {
        public string Name;
        public float DamageTotal;                 // everything the game counts as DamageDealt for this character (incl. summons, returned)
        public float Direct, StatusTicks, GroundEnter, TurnStart, GroundCreate, Returned, Summon;
        public int Hits, Crits, Kills;
        public float Overkill;
        public float BiggestHit; public string BiggestHitAction; public string BiggestHitTarget;
        public float DamageTaken, DamageTakenFromTicks, HealingDone, HealingReceived;
        public float BlockedArmor, BlockedMagicArmor, BlockedResist, BlockedDR, BlockedShields;
        public List<SkillTotal> Skills = new List<SkillTotal>();
        public Dictionary<string, float> DamageByElement = new Dictionary<string, float>();
        public int Casts, FreeActions, ActionsUsed, ManaSpent;
        public float HexesMoved;
        public List<string> TopCasts = new List<string>();
        public Dictionary<string, float> GameStats = new Dictionary<string, float>(); // the game's own BattleStats dictionary at battle end
    }

    internal sealed class BattleRecord
    {
        public string Started = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        public string Ended;
        public bool? Victory;
        public int Turns;
        public List<CreditEvent> Events = new List<CreditEvent>();
        public List<CastEvent> Casts = new List<CastEvent>();
        public Dictionary<string, float> HexesMoved = new Dictionary<string, float>();
        public List<CharacterSummary> Summary = new List<CharacterSummary>();

        public void Summarise(int topSkills, Func<string, Dictionary<string, float>> gameStatsFor)
        {
            Summary.Clear();
            foreach (string name in Events.Select(e => e.Character).Concat(Casts.Select(c => c.Character)).Concat(HexesMoved.Keys).Where(n => n != null).Distinct())
            {
                var cs = new CharacterSummary { Name = name };
                var perSkill = new Dictionary<string, SkillTotal>();
                foreach (CreditEvent e in Events.Where(x => x.Character == name))
                {
                    switch (e.Stat)
                    {
                        case "DamageDealt":
                        case "SummonDamageDealt":
                        case "DamageReturned":
                            cs.DamageTotal += e.Amount;
                            switch (e.Path)
                            {
                                case "Direct": cs.Direct += e.Amount; break;
                                case "StatusTick": cs.StatusTicks += e.Amount; break;
                                case "GroundEnter": cs.GroundEnter += e.Amount; break;
                                case "TurnStart": cs.TurnStart += e.Amount; break;
                                case "GroundCreate": cs.GroundCreate += e.Amount; break;
                                case "Returned": cs.Returned += e.Amount; break;
                                case "Summon": cs.Summon += e.Amount; break;
                            }
                            if (e.Stat == "DamageDealt")
                            {
                                string el = string.IsNullOrEmpty(e.Element) ? "Unknown" : e.Element;
                                float cur; cs.DamageByElement.TryGetValue(el, out cur); cs.DamageByElement[el] = cur + e.Amount;
                                cs.Hits++;
                                if (e.Crit) cs.Crits++;
                                if (e.Kill) cs.Kills++;
                                cs.Overkill += e.Overkill;
                                if (e.Amount > cs.BiggestHit) { cs.BiggestHit = e.Amount; cs.BiggestHitAction = SkillKey(e); cs.BiggestHitTarget = e.Target; }
                                string key = SkillKey(e);
                                SkillTotal st;
                                if (!perSkill.TryGetValue(key, out st)) perSkill[key] = st = new SkillTotal { Name = key };
                                st.Damage += e.Amount; st.Hits++; if (e.Crit) st.Crits++; if (e.Amount > st.Biggest) st.Biggest = e.Amount;
                            }
                            break;
                        case "DamageTaken":
                        case "SummonDamageTaken":
                            cs.DamageTaken += e.Amount;
                            if (e.Path == "StatusTick") cs.DamageTakenFromTicks += e.Amount;
                            break;
                        case "HealingAdministered": cs.HealingDone += e.Amount; break;
                        case "HealingReceived": cs.HealingReceived += e.Amount; break;
                        case "BlockedByArmor": cs.BlockedArmor += e.Amount; break;
                        case "BlockedByMagicArmor": cs.BlockedMagicArmor += e.Amount; break;
                        case "BlockedByResistance": cs.BlockedResist += e.Amount; break;
                        case "BlockedByDamageReduction": cs.BlockedDR += e.Amount; break;
                        case "BlockedByShields": cs.BlockedShields += e.Amount; break;
                    }
                }
                cs.Skills = perSkill.Values.OrderByDescending(s => s.Damage).Take(topSkills).ToList();
                var myCasts = Casts.Where(c => c.Character == name).ToList();
                cs.Casts = myCasts.Count;
                cs.FreeActions = myCasts.Count(c => c.Kind == "FreeAction");
                cs.ActionsUsed = myCasts.Sum(c => c.ActionCost);
                cs.ManaSpent = myCasts.Sum(c => c.ManaCost);
                cs.TopCasts = myCasts.GroupBy(c => c.Action ?? "?").OrderByDescending(g => g.Count()).Take(topSkills).Select(g => g.Key + " x" + g.Count()).ToList();
                float hx; HexesMoved.TryGetValue(name, out hx); cs.HexesMoved = hx;
                cs.Name = name;
                try { cs.GameStats = gameStatsFor(name) ?? new Dictionary<string, float>(); } catch { }
                Summary.Add(cs);
            }
        }

        private static string SkillKey(CreditEvent e)
        {
            if (e.Path == "StatusTick" && !string.IsNullOrEmpty(e.Status)) return e.Status + " (tick)";
            string n = !string.IsNullOrEmpty(e.ActionDisplay) ? e.ActionDisplay : e.Action;
            if (string.IsNullOrEmpty(n)) n = "?";
            if (e.Path == "GroundEnter") n += " (tile)";
            else if (e.Path == "TurnStart") n += " (turn start)";
            else if (e.Path == "GroundCreate") n += " (tile placed)";
            else if (e.Path == "Returned") n += " (thorns)";
            else if (e.Path == "Summon") n += " (summon)";
            return n;
        }

        private static string N(float v) { return v.ToString("N0", CultureInfo.InvariantCulture); }

        public IEnumerable<string> SummaryLines()
        {
            yield return "===== BATTLE SUMMARY (" + (Victory == true ? "victory" : Victory == false ? "defeat" : "ended") + ", " + Turns + " turns, " + Events.Count + " credits recorded) =====";
            foreach (CharacterSummary c in Summary)
            {
                float perTurn = Turns > 0 ? c.DamageTotal / Turns : c.DamageTotal;
                yield return c.Name + ": damage " + N(c.DamageTotal) + " (direct " + N(c.Direct) + ", status ticks " + N(c.StatusTicks) + ", tile on-enter " + N(c.GroundEnter)
                    + ", turn-start " + N(c.TurnStart) + ", tile placed " + N(c.GroundCreate) + ", thorns " + N(c.Returned) + ", summons " + N(c.Summon) + ") | " + N(perTurn) + " per turn";
                yield return "  hits " + c.Hits + ", crits " + c.Crits + (c.Hits > 0 ? " (" + (100f * c.Crits / c.Hits).ToString("0") + "%)" : "") + ", kills " + c.Kills + ", overkill " + N(c.Overkill)
                    + ", biggest hit " + N(c.BiggestHit) + (c.BiggestHitAction != null ? " with " + c.BiggestHitAction + " on " + c.BiggestHitTarget : "");
                yield return "  taken " + N(c.DamageTaken) + " (from ticks " + N(c.DamageTakenFromTicks) + "), healing done " + N(c.HealingDone) + ", received " + N(c.HealingReceived)
                    + " | blocked: armor " + N(c.BlockedArmor) + ", magic armor " + N(c.BlockedMagicArmor) + ", resist " + N(c.BlockedResist) + ", DR " + N(c.BlockedDR) + ", shields " + N(c.BlockedShields);
                if (c.DamageByElement.Count > 0)
                    yield return "  by element: " + string.Join(", ", c.DamageByElement.OrderByDescending(k => k.Value).Select(k => k.Key + " " + N(k.Value)).ToArray());
                yield return "  casts " + c.Casts + " (" + c.ActionsUsed + " actions, " + c.FreeActions + " free), mana spent " + N(c.ManaSpent) + ", hexes moved " + N(c.HexesMoved) + (Turns > 0 ? " (" + (c.Casts / (float)Turns).ToString("0.0") + " casts and " + (c.HexesMoved / Turns).ToString("0.0") + " hexes per turn)" : "") + (c.TopCasts.Count > 0 ? " | most cast: " + string.Join(", ", c.TopCasts.ToArray()) : "");
                if (c.Skills.Count > 0)
                    yield return "  by skill: " + string.Join(", ", c.Skills.Select(s => s.Name + " " + N(s.Damage) + " (" + s.Hits + " hits" + (s.Crits > 0 ? ", " + s.Crits + " crit" : "") + ", max " + N(s.Biggest) + ")").ToArray());
                if (c.GameStats.Count > 0)
                    yield return "  GAME'S OWN TOTALS: " + string.Join(", ", c.GameStats.OrderBy(k => k.Key).Select(k => k.Key + " " + N(k.Value)).ToArray());
            }
        }

        public string WriteJson(string folder)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "battle-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented), new UTF8Encoding(false));
            return path;
        }
    }
}
