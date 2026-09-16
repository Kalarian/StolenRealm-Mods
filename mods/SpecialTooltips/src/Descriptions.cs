using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SpecialTooltips
{
    /// <summary>
    /// Tooltip text per SpecialEffect. The game ships no descriptions for these tags (they are designer labels;
    /// the code never reads them), so the defaults below were written from what the tagged enemies actually
    /// carry: enemy-affix descriptions where the tag is also an affix, the passive skill it maps to, or the
    /// enemy's stats/AI. {AttrName} tokens are replaced with the examined character's live attribute value.
    /// Users can override any entry in the descriptions file; F9 reloads it.
    /// </summary>
    internal sealed class Descriptions
    {
        private static readonly Regex Token = new Regex(@"\{([A-Za-z]+)\}", RegexOptions.Compiled);
        private readonly string _path;
        private readonly Dictionary<string, string> _text = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int Count => _text.Count;
        public int CustomCount { get; private set; }

        public Descriptions(string path) { _path = path; }

        public void Load()
        {
            _text.Clear();
            foreach (KeyValuePair<string, string> kv in Defaults) _text[kv.Key] = kv.Value;
            CustomCount = 0;
            try
            {
                if (!File.Exists(_path))
                {
                    WriteDefaultFile();
                    return;
                }
                foreach (string raw in File.ReadAllLines(_path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim().Replace("\\n", "\n");
                    if (val.Length == 0) continue;
                    string def = null;
                    foreach (KeyValuePair<string, string> kv in Defaults) if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) { def = kv.Value; break; }
                    if (def != val) CustomCount++;
                    _text[key] = val;
                }
            }
            catch (Exception e)
            {
                SpecialTooltipsPlugin.Log.LogWarning("Could not read " + _path + ": " + e.Message);
            }
        }

        private void WriteDefaultFile()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SpecialTooltips descriptions. One line per special: EnumName = text shown in the tooltip.");
            sb.AppendLine("# Use \\n for a line break. {AttrName} is replaced with the examined enemy's live stat, e.g. {ResistFire}.");
            sb.AppendLine("# Lines starting with # are ignored. Delete this file to get the defaults back. F9 in game reloads it.");
            sb.AppendLine();
            foreach (KeyValuePair<string, string> kv in Defaults)
                sb.Append(kv.Key).Append(" = ").AppendLine(kv.Value.Replace("\n", "\\n"));
            File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
            SpecialTooltipsPlugin.Log.LogInfo("Wrote default descriptions to " + _path);
        }

        public string Get(SpecialEffect effect, Character character)
        {
            string text;
            if (!_text.TryGetValue(effect.ToString(), out text) || string.IsNullOrEmpty(text))
                text = "No description available for this special. Add a line '" + effect + " = ...' to stolenrealm.specialtooltips.descriptions.txt.";
            if (character == null) return text;
            return Token.Replace(text, m =>
            {
                try
                {
                    float v = character[m.Groups[1].Value];
                    return v.ToString("F0");
                }
                catch
                {
                    return m.Value;
                }
            });
        }

        // Ordered like the enum so the generated file reads naturally.
        internal static readonly List<KeyValuePair<string, string>> Defaults = new List<KeyValuePair<string, string>>
        {
            D("AttacksOfOpportunity", "Gets a free attack on any character that moves out of a hex next to it. Walking away from it or past it costs you a hit, so kill it, disengage with a teleport or dash, or stay put."),
            D("PhysicalResistant", "Takes reduced physical damage. Physical resist: {ResistPhysical}%. Use fire, cold, lightning or shadow damage instead."),
            D("FireResistant", "Takes reduced fire damage. Fire resist: {ResistFire}%."),
            D("ColdResistant", "Takes reduced cold damage. Cold resist: {ResistCold}%."),
            D("LightningResistant", "Takes reduced lightning damage. Lightning resist: {ResistLightning}%."),
            D("ElementalResistant", "Takes reduced fire, cold and lightning damage. Fire {ResistFire}%, cold {ResistCold}%, lightning {ResistLightning}%. Physical damage is usually the answer."),
            D("WeakToPhysical", "Takes extra physical damage. Physical resist: {ResistPhysical}% (negative means extra damage)."),
            D("WeakToFire", "Takes extra fire damage. Fire resist: {ResistFire}% (negative means extra damage)."),
            D("WeakToCold", "Takes extra cold damage. Cold resist: {ResistCold}% (negative means extra damage)."),
            D("WeakToLightning", "Takes extra lightning damage. Lightning resist: {ResistLightning}% (negative means extra damage)."),
            D("WeakToElemental", "Takes extra fire, cold and lightning damage. Fire {ResistFire}%, cold {ResistCold}%, lightning {ResistLightning}% (negative means extra damage)."),
            D("Explosive", "Explodes when it dies, damaging everything around it (acid, poison cloud, stone burst or fire depending on the creature). Finish it at range or away from your party."),
            D("HealsAllies", "Casts healing on its allies (Regenerate, Mass Cure). Kill or disable it first."),
            D("Armored", "Heavily armoured: about 25% resistance to every damage type. Physical {ResistPhysical}%, fire {ResistFire}%, cold {ResistCold}%, lightning {ResistLightning}%. Armor: {Armor}, magic armor: {MagicArmor}."),
            D("Berserking", "Deals more damage the lower its health gets: +1% damage for every 1% of health missing (Berserker's Blood). As an enemy affix it also has 25% more maximum health. Burst it down rather than chipping."),
            D("ManaShield", "90% of the damage it takes is lost from mana instead of health while it has mana. Its health bar barely moves until the mana bar is empty."),
            D("Critical", "Very high critical hit chance (about 50% on these enemies). Crit chance: {CritChance}%. Expect spiky damage."),
            D("LifeSteal", "Heals itself for a share of the damage it deals. Life steal: {LifeSteal}%. Blind, dodge or kill it quickly to deny the healing."),
            D("Regenerating", "Regenerates health at the start of each of its turns. Focus it down in one turn instead of spreading damage."),
            D("ReflectsDamage", "Physical Thorns: reflects a flat amount of physical damage back at every attacker that hits it. Fewer, bigger hits beat many small ones; spells and ranged attacks avoid the worst of it."),
            D("Summoner", "Summons allies during the fight (wolves, skeletons, scorplings, vampires...). Kill the summoner first or the reinforcements never stop."),
            D("Ambusher", "Deals extra damage (about +24%, Ambush II) to a target that has no allies within 3 hexes. Stay grouped so nobody counts as isolated."),
            D("Stealth", "Uses Hide in Shadows: becomes stealthed for 2 turns and cannot be targeted until it attacks or is revealed. Area attacks still hit it."),
            D("Indestructible", "Gains 50% resistance to the last type of damage it took. Alternate damage types between hits (physical, fire, cold, lightning, shadow) instead of repeating one."),
            D("Glory", "Not used by any current enemy."),
            D("Teleporting", "Teleports to a random hex when struck. Do not rely on positioning it; area attacks and ranged damage work best."),
            D("ControlResistance", "Has a chance to resist any control effect (stun, freeze, root, slow, knockback, fear). Do not build a plan around locking it down."),
            D("Summon", "A summoned creature. It usually disappears when its summoner dies and grants no loot of its own."),
            D("Abyssal", "Child of the Abyss: maximum health doubled, and it cannot be healed by anything except life steal."),
            D("Giant", "Damage and health increased by 25%. Size increased by 25%."),
            D("Miniature", "Movement increased by 3. Action points increased by 1. Maximum health reduced by 25%. Damage reduced by 25%."),
            D("Cursed", "Applies Curse on striking and when struck."),
            D("Destructive", "Damage increased by 50%."),
            D("TakesDamage", "Takes increased damage from all sources."),
            D("Elusive", "Dodge chance increased by about 25-30%. Dodge: {DodgeChance}%. Crit rating and accuracy buffs help; guaranteed-hit skills ignore it."),
            D("Freezing", "Freezes all enemies when it dies. Kill it when your party can afford to lose a turn, or with a character that is immune to freeze."),
            D("Vampiric", "Life steal increased. Life steal: {LifeSteal}%."),
            D("Necromantic", "Raises a skeletal ally when it dies (Skeletal Warrior or Skeletal Minion). Expect one more body after the kill."),
            D("ExtraAction", "Gets an extra action every turn."),
            D("ExtraMovement", "Gets extra movement points every turn."),
            D("Redemptive", "Heals its allies when it dies (Valkyries also cast Mass Cure and Holy Ground). Kill it last, or when nothing else is hurt."),
            D("Burning", "Burning Aura: characters near it are burned (heat stacks) every turn. Keep your distance between turns."),
            D("Chilling", "Chilling Aura: characters near it are chilled (slowed) every turn. Keep your distance between turns."),
            D("Shocking", "Shocking Aura: characters near it are shocked every turn. Keep your distance between turns."),
            D("Toxic", "Applies poison when striking. Applies poison when struck. Poison immunity or cleansing helps a lot."),
            D("Undying", "Casts Dark Ritual on death: it rises again once. Kill it twice, and note a character cannot be ritualed again soon after."),
            D("Inspiring", "Inspiring Aura: buffs nearby allies. Pull it away from the pack or kill it first."),
            D("FuriousFodderI", "25% increased health and damage."),
            D("FuriousFodderII", "50% increased health and damage."),
            D("FuriousFodderIII", "100% increased health and damage."),
            D("SavageSoldiersI", "25% increased health and damage."),
            D("SavageSoldiersII", "50% increased health and damage."),
            D("SavageSoldiersIII", "100% increased health and damage."),
            D("CalamitousChampionsI", "25% increased health and damage."),
            D("CalamitousChampionsII", "50% increased health and damage."),
            D("CalamitousChampionsIII", "100% increased health and damage."),
            D("BeastlyBossesI", "25% increased health and damage."),
            D("BeastlyBossesII", "50% increased health and damage."),
            D("BeastlyBossesIII", "100% increased health and damage."),
            D("MonstrousMomentumI", "Movement points increased by 1."),
            D("MonstrousMomentumII", "Movement points increased by 2."),
            D("MonstrousMomentumIII", "Movement points increased by 2."),
            D("Rampaging", "Damage increased by 50%. All resistances lowered by 25%. Has control resistance. Hits hard but dies fast to any damage type."),
            D("Resilient", "All resistances increased by 25%."),
            D("BleedImmunity", "Immune to bleeding."),
            D("PoisonImmunity", "Immune to poison."),
            D("HeatImmunity", "Immune to heat and burning."),
            D("ShockImmunity", "Immune to shock."),
            D("ChillImmunity", "Immune to chill and freeze."),
            D("PackHunter", "Gains 40% increased damage for every ally within 1 hex of it. Split the pack up, or kill the neighbours first."),
            D("BleedsFire", "When struck it spawns Burning Ground around itself. Melee attackers will be standing in fire; hit it from range or move out after attacking."),
            D("Hellfire", "Engulfed in Hellfire: every heat stack it applies to you also deals fire damage per turn, and it is immune to heat and chill."),
            D("Rampage", "When hit while below 50% health it goes on a Rampage: +50% damage dealt AND taken, plus 1 extra action point. Either finish it in the same turn you drop it under half, or expect a double-strike."),
            D("BuffsAllies", "Casts buffs on its allies (Battle Fury: +25% damage dealt and taken; Blood Frenzy: +25% damage and life steal). Kill the buffer first."),
            D("Cannibal", "Heals for 20% of its maximum health whenever any character dies, friend or foe. Kill the cannibals first, or all at once."),
            D("GlobalFire", "Deals fire damage to every enemy on the battlefield each turn, wherever they stand. Destroy it as fast as possible."),
            D("Icefall", "Casts Icefall (falling ice on an area). Spread out."),
            D("Blind", "Its attacks have a chance to blind the target, making your attacks miss. Cleanse or wait it out before committing big hits."),
            D("ObsidianPlating", "Obsidian Plating: reduces damage taken by 50%."),
            D("SandstonePlating", "Sandstone Plating: reduces damage taken by 35%."),
            D("Explodes", "A walking bomb (Bomber Bug acid burst, Red Imp Sapper detonate): it runs at you and explodes on its own turn. Kill it at range before it reaches you, or step away."),
            D("MoltenPlating", "Molten Plating: reduces damage taken by 40%."),
            D("VampLord", "Vampire Lord Aura: nearby allies gain life steal. Kill it before the rest of the pack."),
            D("StormCall", "Casts Storm Call (lightning strikes over an area). Spread out."),
            D("Earthshatter", "Casts Earthshatter (ground slam over an area). Spread out."),
            D("Necromancer", "Summons skeletons throughout the fight. Kill the necromancer first."),
            D("Vengeful", "Gains increased damage for each ally that has been killed. Kill it first, before its friends."),
            D("IceTomb", "Casts Ice Tomb (freezes a target in place). Bring freeze immunity or a cleanse."),
            D("Ignite", "Casts Ignite (sets a target on fire). Bring heat immunity or a cleanse."),
            D("Unpredictable", "Picks its target at random instead of attacking the closest character, so anyone in the party can be hit, including your back line. (Most enemies with this tag use random targeting; a few are just erratic movers.)"),
        };

        private static KeyValuePair<string, string> D(string k, string v) => new KeyValuePair<string, string>(k, v);
    }
}
