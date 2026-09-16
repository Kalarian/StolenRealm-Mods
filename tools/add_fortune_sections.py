"""Insert the 'Fortunes to target' section into both build guides (idempotent). Run from stolen-realm/."""
import io

def insert(path, section):
    s = open(path, encoding="utf-8").read()
    if "5. Fortunes to target" in s:
        # replace existing section
        a = s.index('<h2 class="pb">5. Fortunes to target</h2>')
        b = s.index('<p class="small">Sources:')
        s = s[:a] + section + "\n" + s[b:]
    else:
        marker = '<p class="small">Sources:'
        s = s.replace(marker, section + "\n" + marker, 1)
    s = s.replace("item data and drop sources from <i>stolen-realm/mythic-items.md</i>.",
                  "item data and drop sources from <i>stolen-realm/mythic-items.md</i>; fortunes from <i>stolen-realm/fortunes.md</i>.")
    open(path, "w", encoding="utf-8").write(s)

intro = '''<h2 class="pb">5. Fortunes to target</h2>
<p class="small">Fortunes are permanent passives earned from island events. Everyone in the party gets the fortune at a level equal to the game level when it was earned (capped at 30); earning it again at a higher level raises it. Numbers written a→b scale from fortune level 1 to 30. Four slots unlock at character levels 1, 8, 15 and 22. "Where" is the map event, its terrain and level requirement, and the option or outcome that grants the fortune; "win the fight" means the fortune is offered on the victory screen of that event's battle.</p>
'''

b2 = intro + '''<table>
<tr><th style="width:19%">Fortune</th><th style="width:33%">Effect</th><th>Where</th></tr>
<tr><td><b>Courage of the Ymir</b> (Mythic)</td><td><b>+1 Action Point every turn</b> while using a one-handed sword, axe, hammer, gun or wand. A permanent second action: two basic attacks a turn before Steal Action, three with it. The single best thing this build can own.</td><td>Ymir Rune, Strength — Frostwrought Mountain, level 3+; take "Accept", then win the Trial of the Ymir fight (Hodr, the Frost Giant).</td></tr>
<tr><td><b>Enchanted Whetstone</b> (Rare)</td><td>Weapon damage +10→40%. Multiplies every attack in the build.</td><td>Jewelry Box Mimic — any terrain; fight the mimic, choose the Whetstone on the victory screen.</td></tr>
<tr><td><b>Bloodied Bargain</b> (Mythic)</td><td>Flat physical damage +8→40 on every hit, and your physical hits apply a Bleed stack.</td><td>Challenger's Gong — Frostwrought Mountain, win the third challenge; or Witch's Pyre — Sunken Swamplands, level 3+, win the fight, "Take".</td></tr>
<tr><td><b>Glory of the Conqueror</b> (Rare)</td><td>Crit chance +4→12%. With Dual-Wield Mastery that's up to 19% before Dexterity.</td><td>Shrine of the Conqueror — any terrain; choose "Protest". ("Submit" gives Gift to the Conquered instead.)</td></tr>
<tr><td><b>Martial Prowess</b> (Rare)</td><td>Physical damage +5→20%.</td><td>The Fortune Teller — any terrain; "See", needs a roll of 11+ (your attributes add to the roll).</td></tr>
<tr><td><b>Sword of the Guardian</b> (Rare)</td><td>Damage +5→20%.</td><td>Shrine of the Guardian — any terrain; "Ponder". ("Salute" gives +4→12% all resistances.)</td></tr>
<tr><td><b>Might of the Warrior</b> (Rare)</td><td>Might +5→25.</td><td>Shrine of the Warrior — any terrain; "Salute".</td></tr>
<tr><td><b>Grace of the Rogue</b> (Rare)</td><td>Reflex +5→25: dodge and counters.</td><td>Shrine of the Rogue — any terrain; "Ponder". ("Salute" gives Dexterity +5→25 if you go the crit route.)</td></tr>
<tr><td><b>Gift to the Conquered</b> (Rare)</td><td>All stats +3→15.</td><td>Shrine of the Conqueror — any terrain; "Submit".</td></tr>
<tr><td><b>Silver Signet</b> (Legendary)</td><td>+8→200 armor, and 1% of your armor is added as weapon damage.</td><td>Treasure Chest Mimic — Water Temple Ruins, Castle Gloom, Forgotten Mines, Dwarven Halls, level 3+; win the fight, choose the Signet.</td></tr>
<tr><td><b>Rune of Refreshment</b> (Mythic)</td><td>Once per battle, reset every cooldown (costs 75% of max mana). Gore, Steal Action and Maim all back at once.</td><td>The Mad Mage — Water Temple Ruins, level 3+; follow the chain to The Spell Council, win, "Accept".</td></tr>
<tr><td><b>Pirate's Rum</b> (Legendary)</td><td>Damage reduction +5→20%.</td><td>Party like a Pirate — Water Temple Ruins; keep going to the third round and "Sip".</td></tr>
<tr><td><b>Tome of Tadashi</b> (Legendary)</td><td>Dodge +2→10% and escape-death spells.</td><td>The Librarian — Water Temple Ruins.</td></tr>
<tr><td><b>Endurance</b> / <b>Fortitude</b> (Rare / Uncommon)</td><td>Max health +5→500 / Vitality +4→20.</td><td>The Fortune Teller "View" (roll 11+) or the Roland boss event / Runed Obelisk "Activate the blessing" — any terrain.</td></tr>
<tr><td><b>Vampirism</b> (Mythic) — <i>optional, read the catch</i></td><td>Life steal +12%, damage +35%, fire resistance −25%, and <b>you can no longer heal from anything except life steal</b>. Blood Drinker still works (it is life steal); Warrior's Boon and any other healing stop working. Only take it if you're comfortable living on Blood Drinker alone.</td><td>Ornate Coffin — Castle Gloom, level 3+; the Bishop route, win the fight, "Accept".</td></tr>
</table>
<p class="small">Suggested four slots at 30: Courage of the Ymir, Enchanted Whetstone, Bloodied Bargain, and Pirate's Rum or Glory of the Conqueror. Shrine fortunes are the easy early picks: no fight, any terrain.</p>
'''

b1 = intro + '''<table>
<tr><th style="width:19%">Fortune</th><th style="width:33%">Effect</th><th>Where</th></tr>
<tr><td><b>Courage of the Ymir</b> (Mythic)</td><td><b>+1 Action Point every turn</b> while using a one-handed sword, axe, hammer, gun or wand. Two basic attacks a turn = two Blood Drinker heals, or an attack plus Cyclone Kick. Best fortune for this build.</td><td>Ymir Rune, Strength — Frostwrought Mountain, level 3+; take "Accept", then win the Trial of the Ymir fight (Hodr, the Frost Giant).</td></tr>
<tr><td><b>Pirate's Rum</b> (Legendary)</td><td><b>Damage reduction +5→20%.</b> Nearly half the 50% cap from one fortune, which frees gear slots from needing the Adamant pieces.</td><td>Party like a Pirate — Water Temple Ruins; keep going to the third round and "Sip".</td></tr>
<tr><td><b>Emblem of the Iron Fortress</b> (Mythic)</td><td>+10→250 armor, and 3% of your armor is dealt back as physical return damage.</td><td>Cursed Mimic Chest — Castle Gloom, Forgotten Mines, Sunken Swamplands, Dwarven Halls, level 4+; win the fight, choose the Emblem.</td></tr>
<tr><td><b>Ancestral Protection</b> (Legendary)</td><td>+20→300 armor and magic armor, and a 10% chance to strike back with Avenging Ancestors when hit.</td><td>Talk ta de Spirits! — Wyrmrest Desert; second stage, choose "Protection".</td></tr>
<tr><td><b>Polished</b> (Uncommon)</td><td>+15→250 armor and magic armor. Uncommon rarity, so it never competes with the mythics for a slot under the rarity rule.</td><td>Talking Suit of Armor — Castle Gloom, Water Temple Ruins, Dwarven Halls.</td></tr>
<tr><td><b>Blessing of the Mountain</b> (Mythic)</td><td>Max health +15→30% and a 50% chance to resist any control effect.</td><td>Desecrated Statue of the First Mountain King — Dwarven Halls, level 3+; win the fight, "Accept".</td></tr>
<tr><td><b>Shield of the Guardian</b> (Rare)</td><td>All resistances +4→12%, including the shadow and holy damage that ignores armor.</td><td>Shrine of the Guardian — any terrain; "Salute".</td></tr>
<tr><td><b>Ethereal</b> (Rare)</td><td>Physical resistance +10→50%, elemental resistances −5→25%. Huge against melee-heavy fights; swap it out for fire or lightning bosses.</td><td>Barbarian Fisherman or Between Worlds — any terrain.</td></tr>
<tr><td><b>Silver Signet</b> (Legendary)</td><td>+8→200 armor, and 1% of your armor becomes weapon damage. On a 1,500-armor tank that's +15 damage per swing for free.</td><td>Treasure Chest Mimic — Water Temple Ruins, Castle Gloom, Forgotten Mines, Dwarven Halls, level 3+; win, choose the Signet.</td></tr>
<tr><td><b>Endurance</b> / <b>Fortitude</b> (Rare / Uncommon)</td><td>Max health +5→500 / Vitality +4→20 (which is also armor after Body and Soul).</td><td>The Fortune Teller "View" (roll 11+) or the Roland boss event / Runed Obelisk "Activate the blessing" — any terrain.</td></tr>
<tr><td><b>Might of the Warrior</b> / <b>Gift to the Conquered</b> (Rare)</td><td>Might +5→25 / all stats +3→15.</td><td>Shrine of the Warrior "Salute" / Shrine of the Conqueror "Submit" — any terrain.</td></tr>
<tr><td><b>Enchanted Pumpkin</b> (Legendary)</td><td>+12 health and mana every turn. Solves the Boon-and-Howl mana drain.</td><td>Enchanted Pumpkin Patch — Freewind Forest; win the fight.</td></tr>
<tr><td><b>Cursed Coin</b> (Legendary)</td><td>The first time you'd die in a battle you instead cannot drop below 1 health for 2 turns. A second Glory.</td><td>Pirate's Treasure — Water Temple Ruins, level 4+; win the fight, "Loot".</td></tr>
<tr><td><b>Rune of Refreshment</b> (Mythic)</td><td>Once per battle, reset every cooldown (75% of max mana). Boon, Howl, Pain Suppression and Unyielding Contender all back for the boss's big turn.</td><td>The Mad Mage — Water Temple Ruins, level 3+; follow the chain to The Spell Council, win, "Accept".</td></tr>
</table>
<p class="small">Suggested four slots at 30: Courage of the Ymir, Pirate's Rum, Emblem of the Iron Fortress, Blessing of the Mountain. Polished and the shrine fortunes are the early fillers: no fight, any terrain. Avoid Brawler's Brew (blinds you each turn) and Call of the Reaper (+10% damage taken) on a tank.</p>
'''

insert("build2/Build2-Thief-Warrior.html", b2)
insert("build1/Build1-Party-Tank.html", b1)
print("sections added")
