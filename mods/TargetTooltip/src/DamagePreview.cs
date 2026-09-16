using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Burst2Flame;
using UnityEngine;

namespace TargetTooltip
{
    /// <summary>
    /// Expected damage of an action against a specific enemy. Base numbers come from the game's own
    /// Character.GetActionDamage(source, target, ...) per GeneralEffect (so source mods, target-side mods like Marked Prey
    /// and flat target damage are included). Mitigation then mirrors Character.ApplyAction: general reduction, resists
    /// and armor in GlobalSettings.DamageReductionOrder. Crits are random in GetActionDamage, so we resample a few
    /// times for a non-crit baseline unless the crit is guaranteed.
    /// </summary>
    internal static class DamagePreview
    {
        private sealed class Part
        {
            public DamageType Type;
            public float Min, Max;
            public bool Crit;
        }

        public static string Build(Character source, Character target, ActionInfo action, TargetTooltipConfig cfg)
        {
            if (source == null || target == null || action == null) return null;
            ActionInfo dmgAction = action.TooltipDamageInfoRefAction != null ? action.TooltipDamageInfoRefAction : action;
            if (dmgAction.Effects == null) return null;
            ActionProperties props = action.Properties;
            List<string> formulas = dmgAction.Effects.Where(x => x is GeneralEffect).Select(x => ((GeneralEffect)x).Action).Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (formulas.Count == 0) return null;

            float maxRange = 0f;
            try { if (action.Targets != null && action.Targets.Length > 0 && action.Targets[0] is TargetInfo) maxRange = action.GetSimpleRange(source, (TargetInfo)action.Targets[0]); } catch { }

            var parts = new List<Part>();
            bool anyCrit = false;
            foreach (string formula in formulas)
            {
                List<DamageInstance> inst = Sample(source, target, props, formula, maxRange);
                if (inst == null) continue;
                foreach (DamageInstance d in inst)
                {
                    if (d.DamageType == DamageType.Healing || d.DamageType == DamageType.Mana) continue;
                    if (d.DamageMax <= 0f) continue;
                    parts.Add(new Part { Type = d.DamageType, Min = d.DamageMin, Max = d.DamageMax, Crit = d.DidCrit });
                    anyCrit |= d.DidCrit;
                }
            }
            if (parts.Count == 0) return null;

            // Mitigation, in the game's configured order.
            GlobalSettings gs = GlobalSettingsManager.instance.globalSettings;
            DamageReductionCalcOrder[] order = gs.DamageReductionOrder;
            if (order == null || order.Length != 3) order = new[] { DamageReductionCalcOrder.GeneralReduction, DamageReductionCalcOrder.Resists, DamageReductionCalcOrder.Armor };
            var notes = new List<string>();
            float general = 0f;
            if (props.DealsDamage)
            {
                general = Attr(target, "DamageReduction") + Attr(target, "Resilience");
                try { if (Attr(target, "TakeCover") > 0f && source.Cell != null && target.Cell != null && source.Cell.Distance(target.Cell) > 2) general += 35f; } catch { }
            }
            float armor = Attr(target, "Armor"), magicArmor = Attr(target, "MagicArmor");
            float armorCap = (100f - gs.maxArmorReductionPercent) / 100f;
            var usedResist = new Dictionary<string, float>();

            foreach (DamageReductionCalcOrder step in order)
            {
                foreach (Part p in parts)
                {
                    switch (step)
                    {
                        case DamageReductionCalcOrder.GeneralReduction:
                            if (!props.IgnoreMultipliers && general != 0f) { p.Min *= 1f - general / 100f; p.Max *= 1f - general / 100f; }
                            break;
                        case DamageReductionCalcOrder.Resists:
                            if (!props.IgnoreResists)
                            {
                                string attr = ResistAttr(p.Type);
                                if (attr != null)
                                {
                                    float r = Attr(target, attr);
                                    if (r != 0f) { p.Min *= 1f - r / 100f; p.Max *= 1f - r / 100f; usedResist[SchoolName(p.Type)] = r; }
                                }
                            }
                            break;
                        case DamageReductionCalcOrder.Armor:
                            if (!props.IgnoreArmor)
                            {
                                float a = p.Type == DamageType.Physical ? armor : (p.Type == DamageType.Fire || p.Type == DamageType.Cold || p.Type == DamageType.Lightning) ? magicArmor : 0f;
                                if (a > 0f)
                                {
                                    float red = a / gs.ArmorPerDamagePointReduction;
                                    p.Min = Mathf.Max(p.Min * armorCap, p.Min - red);
                                    p.Max = Mathf.Max(p.Max * armorCap, p.Max - red);
                                }
                            }
                            break;
                    }
                }
            }

            float totalMin = 0f, totalMax = 0f;
            foreach (Part p in parts) { totalMin += Mathf.Ceil(p.Min); totalMax += Mathf.Ceil(p.Max); }
            if (totalMax <= 0f) return null;

            var sb = new StringBuilder();
            sb.Append("<color=#CBB396>").Append(OptionsManager.Localize("vs")).Append(' ').Append(target.LocalizedCharacterName).Append(":</color> <color=#FFFFFF>");
            sb.Append(Range(totalMin, totalMax)).Append("</color>");
            if (anyCrit) sb.Append(" <color=#E0B04A>(crit)</color>");
            float hp = target.Health;
            if (hp > 0f)
            {
                float pctMin = totalMin / hp * 100f, pctMax = totalMax / hp * 100f;
                sb.Append(" <color=#9AA5B1>").Append(pctMax >= 100f && pctMin >= 100f ? OptionsManager.Localize("kills") : Range(Mathf.Min(100f, pctMin), Mathf.Min(100f, pctMax)) + "% " + OptionsManager.Localize("of its health")).Append("</color>");
            }
            if (cfg.PreviewDetail.Value)
            {
                if (parts.Count > 1 || parts[0].Type != DamageType.Physical)
                    notes.Add(string.Join(" + ", parts.Select(p => Range(Mathf.Ceil(p.Min), Mathf.Ceil(p.Max)) + " " + SchoolName(p.Type).ToLowerInvariant()).ToArray()));
                foreach (KeyValuePair<string, float> kv in usedResist) notes.Add(kv.Key.ToLowerInvariant() + " resist " + kv.Value.ToString("0", CultureInfo.InvariantCulture) + "%");
                bool phys = parts.Any(p => p.Type == DamageType.Physical), magic = parts.Any(p => p.Type == DamageType.Fire || p.Type == DamageType.Cold || p.Type == DamageType.Lightning);
                if (phys && armor > 0f && !props.IgnoreArmor) notes.Add("armor " + armor.ToString("0", CultureInfo.InvariantCulture));
                if (magic && magicArmor > 0f && !props.IgnoreArmor) notes.Add("magic armor " + magicArmor.ToString("0", CultureInfo.InvariantCulture));
                if (general != 0f) notes.Add("damage reduction " + general.ToString("0", CultureInfo.InvariantCulture) + "%");
                float dodge = Attr(target, "DodgeChance");
                if (dodge > 0f) notes.Add("dodge " + dodge.ToString("0", CultureInfo.InvariantCulture) + "%");
                if (notes.Count > 0) sb.Append("\n<size=85%><color=#9AA5B1>").Append(string.Join(" · ", notes.ToArray())).Append("</color></size>");
            }
            return sb.ToString();
        }

        /// <summary>Run the game's damage calc; resample a few times to avoid a random crit unless crit is guaranteed.</summary>
        private static List<DamageInstance> Sample(Character source, Character target, ActionProperties props, string formula, float maxRange)
        {
            float critChance = Attr(source, "CritChance") + Attr(target, "CritChanceTarget");
            bool guaranteed = critChance >= 100f || source.NumStealthCritActions > 0;
            List<DamageInstance> best = null;
            for (int i = 0; i < 6; i++)
            {
                var ss = new SimpleDefaultDictionary<string, float>(new Dictionary<string, float>());
                var ts = new SimpleDefaultDictionary<string, float>(new Dictionary<string, float>());
                List<DamageInstance> inst;
                try
                {
                    inst = Character.GetActionDamage(source, target, props, formula, new ActionDamageInfo
                    {
                        SourceItem = null, DontCalculateDamageRange = false, ChainCount = 0, ForceCrit = false, PierceCount = 0, StatusStackIndex = 0, DamageModifier = 1f, MaxRange = maxRange
                    }, ref ss, ref ts);
                }
                catch (Exception e)
                {
                    if (TargetTooltipPlugin.Cfg.Verbose.Value) TargetTooltipPlugin.Log.LogWarning("GetActionDamage failed for [" + formula + "]: " + e.Message);
                    return null;
                }
                if (inst == null) return null;
                best = inst;
                if (guaranteed || !inst.Any(d => d.DidCrit)) break;
            }
            return best;
        }

        private static string ResistAttr(DamageType t)
        {
            switch (t)
            {
                case DamageType.Physical: return "ResistPhysical";
                case DamageType.Fire: return "ResistFire";
                case DamageType.Cold: return "ResistCold";
                case DamageType.Lightning: return "ResistLightning";
                case DamageType.Shadow: case DamageType.Holy: return "ResistDivine";
                default: return null;
            }
        }

        private static string SchoolName(DamageType t) { return t.ToString(); }

        private static float Attr(Character c, string name)
        {
            try { return c[name]; } catch { return 0f; }
        }

        private static string Range(float min, float max)
        {
            string a = min.ToString("0", CultureInfo.InvariantCulture), b = max.ToString("0", CultureInfo.InvariantCulture);
            return a == b ? a : a + "-" + b;
        }
    }
}
