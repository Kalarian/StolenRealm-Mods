using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Burst2Flame;
using HarmonyLib;

namespace ScalingTooltips.Patches
{
    /// <summary>
    /// Tooltip descriptions carry two kinds of number tokens:
    ///   '[N]' resolved by Tooltip.ApplyDescriptionExpressions(text, expressions, params, fontSize, rangeMod) — expressions[N]
    ///          is the formula (skill-tree / skill-bar tooltips; produces the "118-159" ranges via rangeMod)
    ///   '*N'  resolved by Tooltip.GetDamageString(text, List&lt;string&gt; effects, properties, source, overrides, item) —
    ///          effects[N] comes from the action's GeneralEffect.Action strings (action / status tooltips)
    /// Both loops replace the token in place, so a Prefix that inserts our label immediately AFTER each token leaves the
    /// game to fill in the number and keeps the label glued to it. The label must not contain '[' or '*'.
    /// With Breakdown on, a "Scaling:" line is appended to the description showing stat value x factor x every modifier.
    /// </summary>
    internal static class ScalingPatches
    {
        private static readonly Regex StarToken = new Regex(@"\*(\d)", RegexOptions.Compiled);
        private static readonly Regex BracketToken = new Regex(@"(?<!\[)\[(\d)\](?!\])", RegexOptions.Compiled);

        // Formulas look like:  TargetStored[Source.WeaponDamageType] = Source.AttackPower * .7f
        //                      TargetStored["FireDamage"] = Source.SpellPower("Fire") * .5f
        //                      Source.SpellPower() * .5f * Source.DamageModFire        (description expressions)
        //                      Mathf.Max(1, Source.GetCustomActionDamage("GL-Bleeding", "TargetStored[\"PhysicalDamage\"] = Source.AttackPower * .05f"))
        // so we search for the stat term anywhere and read the factors that follow it.
        private static readonly Regex StatTerm = new Regex(
            @"(?:Source\.(?<stat>BasicAttackPower|AttackPower|SpellPower\(\s*\\?""(?<school>\w+)\\?""\s*\)|SpellPower\(\)|GetFlatDamageValue|MaxHealth)|Source\[""MaxHealth""\])"
            + @"(?<factors>(?:\s*\)?\s*\*\s*[\d.]+f?)*)"
            + @"(?:\s*\)?\s*\*\s*Source\.DamageMod(?<mod>\w+))?",
            RegexOptions.Compiled);
        private static readonly Regex Factor = new Regex(@"\*\s*(?<n>[\d.]+)f?", RegexOptions.Compiled);
        private static readonly Regex StoredKey = new Regex(@"Stored\[\\?""(?<key>\w+)\\?""\]", RegexOptions.Compiled);

        /// <summary>What a formula scales with, parsed from its text.</summary>
        private sealed class Scaling
        {
            public string Stat;      // AttackPower | BasicAttackPower | SpellPower | GetFlatDamageValue | MaxHealth
            public string School;    // Fire, Cold, Lightning, Shadow, Physical, Healing/Light, Holy... or null
            public float Factor = 1f;
            public bool IsAP => Stat == "AttackPower" || Stat == "BasicAttackPower";
            public bool IsSP => Stat == "SpellPower";
            public string ShortStat => IsAP ? "AP" : IsSP ? "SP" : Stat == "GetFlatDamageValue" ? "Flat" : "MaxHP";
            public string LongStat => IsAP ? "Attack Power" : IsSP ? "Spell Power" : Stat == "GetFlatDamageValue" ? "Flat Damage" : "Max Health";
            public bool SchoolImplied => School == null || School == "Basic" || (School == "Physical" && IsAP);
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ApplyDescriptionExpressions))]
        private static class Tooltip_ApplyDescriptionExpressions
        {
            private static void Prefix(ref string text, string[] expressions, GameFunctionParameters gameFunctionParameters, float rangeMod)
            {
                try
                {
                    Character source = gameFunctionParameters.Source;
                    text = Annotate(text, BracketToken, i => (expressions != null && i < expressions.Length) ? expressions[i] : null,
                        "[N]", source, costsMana: true, isBasic: false, rangeMod: rangeMod);
                }
                catch (Exception e)
                {
                    if (ScalingTooltipsPlugin.Cfg != null && ScalingTooltipsPlugin.Cfg.Verbose.Value) ScalingTooltipsPlugin.Log.LogWarning("ApplyDescriptionExpressions prefix: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.GetDamageString),
            new[] { typeof(string), typeof(List<string>), typeof(ActionProperties), typeof(Character), typeof(string[]), typeof(Item) })]
        private static class Tooltip_GetDamageString
        {
            private static void Prefix(ref string text, List<string> effects, ActionProperties properties, Character source, string[] overrides)
            {
                try
                {
                    if (effects == null) return;
                    var merged = new List<string>(effects); // same override merge the game does, on a copy
                    if (overrides != null)
                    {
                        for (int i = 0; i < overrides.Length; i++)
                        {
                            if (i < merged.Count) merged[i] = overrides[i]; else merged.Add(overrides[i]);
                        }
                    }
                    bool isBasic = false;
                    try { isBasic = properties.IsActionType("Basic"); } catch { }
                    text = Annotate(text, StarToken, i => i < merged.Count ? merged[i] : null,
                        "*N", source, costsMana: properties.CostsMana, isBasic: isBasic, rangeMod: 0f);
                }
                catch (Exception e)
                {
                    if (ScalingTooltipsPlugin.Cfg != null && ScalingTooltipsPlugin.Cfg.Verbose.Value) ScalingTooltipsPlugin.Log.LogWarning("GetDamageString prefix: " + e);
                }
            }
        }

        private static string Annotate(string text, Regex token, Func<int, string> formulaFor, string kind, Character source, bool costsMana, bool isBasic, float rangeMod)
        {
            ScalingTooltipsConfig cfg = ScalingTooltipsPlugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value || string.IsNullOrEmpty(text)) return text;
            MatchCollection matches = token.Matches(text);
            if (matches.Count == 0) return text;

            var sb = new StringBuilder(text.Length + 96 * matches.Count);
            var breakdowns = new List<string>();
            var seen = new HashSet<string>();
            int last = 0;
            foreach (Match m in matches)
            {
                int end = m.Index + m.Length;
                sb.Append(text, last, end - last);
                int idx = int.Parse(m.Groups[1].Value);
                string formula = formulaFor(idx);
                Scaling sc = Parse(formula);
                string label = Label(sc, formula, cfg);
                if (cfg.Verbose.Value)
                    ScalingTooltipsPlugin.Log.LogInfo(kind + " token " + idx + " formula [" + (formula ?? "null") + "] -> " + (label ?? "(no label)"));
                if (!string.IsNullOrEmpty(label))
                {
                    sb.Append(" <size=").Append(cfg.SizePercent.Value).Append("%><color=#").Append(cfg.Color.Value).Append(">(").Append(label).Append(")</color></size>");
                    if (cfg.Breakdown.Value && sc != null && source != null && seen.Add(formula))
                    {
                        string bd = Breakdown(sc, source, costsMana, isBasic, rangeMod, cfg);
                        if (!string.IsNullOrEmpty(bd)) breakdowns.Add(bd);
                        if (cfg.Verbose.Value) ScalingTooltipsPlugin.Log.LogInfo("  breakdown: " + bd);
                    }
                }
                last = end;
            }
            sb.Append(text, last, text.Length - last);
            if (breakdowns.Count > 0) sb.Append('\n'); // blank line so the Scaling line is not jammed against the description
            foreach (string bd in breakdowns)
                sb.Append("\n<size=").Append(cfg.SizePercent.Value).Append("%><color=#").Append(cfg.Color.Value).Append('>').Append(bd).Append("</color></size>");
            return sb.ToString();
        }

        private static Scaling Parse(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula)) return null;
            Match m = StatTerm.Match(formula);
            if (!m.Success) return null;
            var sc = new Scaling();
            string stat = m.Groups["stat"].Success ? m.Groups["stat"].Value : "MaxHealth";
            sc.Stat = stat.StartsWith("SpellPower") ? "SpellPower" : stat;
            foreach (Match f in Factor.Matches(m.Groups["factors"].Value))
            {
                float n;
                if (float.TryParse(f.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out n)) sc.Factor *= n;
            }
            // School: explicit SpellPower("Fire") > DamageModFire > the stored key ("FireDamage", "Healing").
            if (m.Groups["school"].Success) sc.School = m.Groups["school"].Value;
            else if (m.Groups["mod"].Success) sc.School = m.Groups["mod"].Value;
            else
            {
                Match k = StoredKey.Match(formula);
                if (k.Success)
                {
                    string key = k.Groups["key"].Value;
                    sc.School = key.EndsWith("Damage") ? key.Substring(0, key.Length - 6) : key;
                }
            }
            return sc;
        }

        private static string Label(Scaling sc, string formula, ScalingTooltipsConfig cfg)
        {
            if (sc == null)
            {
                if (string.IsNullOrWhiteSpace(formula) || !cfg.ShowRawFormula.Value) return null;
                string raw = formula.Replace("Source.", "").Replace("()", "").Replace("f ", " ").Replace("f*", "*").Replace("f)", ")").Trim();
                if (raw.EndsWith("f")) raw = raw.Substring(0, raw.Length - 1);
                return raw.Replace("[", "").Replace("*", "x"); // never feed '[' or '*' back into the game's parsers
            }
            string schoolSuffix = (cfg.ShowDamageType.Value && !sc.SchoolImplied) ? ", " + sc.School : "";
            if (cfg.Style.Value == LabelStyle.Short)
                return Num(sc.Factor) + "x " + sc.ShortStat + schoolSuffix;          // e.g. "0.7x AP"
            float pctVal = sc.Factor * 100f;
            return pctVal.ToString(pctVal % 1f == 0f ? "F0" : "F1", CultureInfo.InvariantCulture) + "% " + sc.LongStat + schoolSuffix; // "70% Attack Power"
        }

        /// <summary>
        /// Mirrors the game's tooltip math: stat x factor, then x (1 + AbilityPower/100) [Might],
        /// x (1 + (DamageMod + DamageMod&lt;School&gt; + ManaPowerMod + DamageModBasic)/100), then + flat school damage.
        /// The [N] path evaluates 'Source.AttackPower * f * Source.DamageModX' which is exactly this; the *N path
        /// (Character.GetActionDamage) applies the same pieces for a target-less tooltip.
        /// </summary>
        private static string Breakdown(Scaling sc, Character c, bool costsMana, bool isBasic, float rangeMod, ScalingTooltipsConfig cfg)
        {
            float statValue;
            string statName;
            if (sc.IsAP)
            {
                statValue = sc.Stat == "BasicAttackPower" ? c.BasicAttackPower : c.AttackPower;
                statName = "AP";
            }
            else if (sc.IsSP)
            {
                statValue = c.SpellPower(sc.School ?? "Unused");
                statName = "SP";
            }
            else if (sc.Stat == "MaxHealth")
            {
                statValue = c["MaxHealth"];
                statName = "Max Health";
            }
            else return null; // flat-damage formulas have no clean stat to show

            string school = NormalizeSchool(sc.School, sc.IsAP);
            var parts = new List<string>();
            float pct = 0f;

            float dm = Attr(c, "DamageMod");
            if (dm != 0f) { pct += dm; parts.Add("Power " + Pct(dm)); }
            if (school != null)
            {
                float sm = Attr(c, "DamageMod" + school);
                if (sm != 0f) { pct += sm; parts.Add(SchoolWord(school) + " " + Pct(sm)); }
            }
            if (costsMana)
            {
                float mp = Attr(c, "ManaPowerMod");
                if (mp != 0f) { pct += mp; parts.Add("Mana power " + Pct(mp)); }
            }
            if (isBasic || sc.Stat == "BasicAttackPower")
            {
                float bm = Attr(c, "DamageModBasic");
                if (bm != 0f) { pct += bm; parts.Add("Basic attack " + Pct(bm)); }
            }
            float ap = Attr(c, "AbilityPower");
            float might = 1f + ap / 100f;
            if (ap != 0f) parts.Add("Might " + Pct(ap));

            float mult = (1f + pct / 100f) * might;
            float flat = school != null ? Attr(c, "DamageFlat" + school) : 0f;
            float result = statValue * sc.Factor * mult + flat;
            if (flat != 0f) parts.Add("+" + Num(flat) + " flat " + SchoolWord(school).ToLowerInvariant());

            var sb = new StringBuilder("Scaling: ");
            sb.Append(Num(sc.Factor)).Append(" x ").Append(Num(statValue)).Append(' ').Append(statName);
            if (mult != 1f) sb.Append(" x ").Append(mult.ToString("0.##", CultureInfo.InvariantCulture));
            if (flat != 0f) sb.Append(" + ").Append(Num(flat));
            sb.Append(" = ").Append(Num(result));
            if (rangeMod > 0f)
                sb.Append(" (").Append(Num(result * (1f - rangeMod))).Append('-').Append(Num(result * (1f + rangeMod))).Append(')');
            if (parts.Count > 0) sb.Append("  ·  ").Append(string.Join(", ", parts.ToArray()));
            return sb.ToString();
        }

        private static float Attr(Character c, string name)
        {
            try { return c[name]; } catch { return 0f; }
        }

        private static string NormalizeSchool(string school, bool isAP)
        {
            if (school == null || school == "Basic") return isAP ? "Physical" : null;
            switch (school)
            {
                case "Physical": case "Fire": case "Cold": case "Lightning": case "Shadow": case "Healing": return school;
                case "Light": case "Holy": return "Healing"; // DamageModLight => DamageModHealing in the game
                default: return null;
            }
        }

        private static string SchoolWord(string school)
        {
            return school == "Healing" ? "Healing/Light" : school;
        }

        private static string Pct(float v)
        {
            return (v >= 0f ? "+" : "") + v.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        private static string Num(float v)
        {
            return v.ToString(v % 1f == 0f ? "0" : "0.##", CultureInfo.InvariantCulture);
        }
    }
}
