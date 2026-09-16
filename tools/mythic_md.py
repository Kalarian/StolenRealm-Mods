"""Render mythic-items.md from data/mythics.json (+ optional data/mythic_extra_sources.json). Run from stolen-realm/data."""
import json, collections, re, os
my = json.load(open("mythics.json", encoding="utf-8"))
extra = json.load(open("mythic_extra_sources.json", encoding="utf-8")) if os.path.exists("mythic_extra_sources.json") else {}
refs = json.load(open("mythic_refs.json", encoding="utf-8"))
BOSS_EVENTS = extra.pop("_boss_events", {})
TRIG = {19: "on hitting (any)", 0: "on dealing damage", 20: "on healing", 21: "on being hit (any)", 1: "on taking damage", 22: "on being healed", 2: "on casting", 3: "on any death (not self)", 4: "on turn start", 5: "on turn end", 6: "on battle start", 7: "on action completed", 8: "on moving", 9: "on death", 10: "on action completed per target", 11: "on moving or warping", 12: "on any death", 13: "on crit", 14: "on dodge", 15: "after my death", 16: "after any character death", 29: "after any character death (final)", 17: "on any summon spawn", 18: "on warp", 23: "on hitting incl. procs", 24: "on dealing damage incl. procs", 25: "on healing incl. procs", 26: "on being hit incl. procs", 27: "on taking damage incl. procs", 28: "on being healed incl. procs", 30: "on globule pickup"}
ORDER = ["Weapon", "Shield", "Head", "Armor", "Ring", "Amulet", "Consumable", "Material", "Commodity"]


def clean(s):
    s = re.sub(r"<[^>]+>", "", s)
    s = re.sub(r"\{\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\}", r"\1→\2 (scales with item level)", s)
    s = s.replace("{SKL=", "").replace("{STA=", "").replace("}", "").replace("@", "")
    s = re.sub(r"\s+", " ", s).strip()
    return s.replace("|", "/")


def trig(t):
    m = re.match(r"TriggerType=(\d+)", t)
    if m:
        t = TRIG.get(int(m.group(1)), t[:14]) + t[m.end():]
    return clean(t)


def source_lines(d):
    out = []
    for s in d["sources"]:
        if s["kind"] == "personal_loot":
            q = "; ".join(f"{qn} (Act {a}, lvl {lv}, {'main' if mn else 'side'})" for qn, a, lv, mn in s.get("quests", []))
            how = f"{s['chance']:g}% roll" if not s["mustGetOne"] else f"weighted pick, weight {s['chance']:g} of a {s['nInTable']}-item guaranteed table"
            where = BOSS_EVENTS.get(s["enemy"], "")
            out.append(f"Boss loot: **{s['enemy']}** ({s['enemyType']}) — {how}" + (f". Fought in: {q}" if q else "") + (f". {where}" if where else "") + (" (not in the random battle pool)" if s.get("excludeFromPool") else ""))
        elif s["kind"] == "recipe":
            req = ", ".join(f"{n} x{qn}" for n, qn in s["required"])
            out.append(f"Crafting: **{s['recipe']}** — needs {req}" + (f", {s['gold']:g} gold" if s["gold"] else ""))
        elif s["kind"] == "group_loot_table":
            out.append(f"Enemy-group loot table {s['group']} ({s['chance']:g}%)")
        elif s["kind"] == "quest_reward":
            out.append(f"Quest reward: {s['quest']}")
    for r in refs.get(d["name"], []):
        f, top, path = r.split(":", 2)
        if f == "PartyEvent":
            ev = re.sub(r" \d$", "", top)
            if not any(ev in x for x in extra.get(d["name"], [])):
                out.append(f"Island event: **{ev}**")
        elif f == "CharacterPresetFile":
            out.append(f"Roguelike preset starting gear: {top.split('-', 1)[-1]} ({path.strip('/')})")
        elif f == "Shopkeeper":
            out.append(f"Sold by: {top}")
    for e in extra.get(d["name"], []):
        out.append(e)
    if d["allowedShops"]:
        out.append("Shop-restricted to: " + ", ".join(d["allowedShops"]))
    if not d["removeFromUnassignedPool"]:
        out.append("World loot pool (see rules above)")
    elif not out:
        out.append("**No source found in data** (removed from world pool, not referenced by any loot table, recipe, event, shop or quest)")
    out = list(dict.fromkeys(out))
    return out


def stat_line(d):
    parts = []
    if d["isWeapon"]:
        parts.append(f"{d['equipment']}, dmg ratio {d['damageRatio']:g}, range {d['attackRange']}")
    if d["stats"]:
        parts.append(", ".join(f"{k} {v:g}" for k, v in d["stats"].items()))
    if d["armorRatio"] or d["magicArmorRatio"]:
        parts.append(f"armor ratio {d['armorRatio']:g} / magic {d['magicArmorRatio']:g}")
    if d["armorRatioShield"] or d["magicArmorRatioShield"]:
        parts.append(f"shield armor ratio {d['armorRatioShield']:g} / magic {d['magicArmorRatioShield']:g}")
    if d["scaledStats"]:
        parts.append("tooltip @lvl1: " + clean(d["scaledStats"]).replace("Couldn't parse", "expr").rstrip("/ "))
    return "; ".join(parts)


L = []
L.append("# Stolen Realm — Mythic Items (complete list)\n")
L.append("Source: game data extracted from the installed build (2025-06-17, Chaos Pack era) — `data/ItemInfo.json`, `data/WeaponInfo.json`, `data/CharacterInfo.json` (boss loot), `data/CraftingRecipe.json`, `data/PartyEvent.json`, `data/Shopkeeper.json`, `data/GlobalSettings.json`, plus the decompiled loot code (`decomp/LootTable.cs`, `decomp/Character.cs GetLootDrop`, `decomp/GameLogic.cs GenerateLoot`, `decomp/ShopManager.cs`). Numbers below are the live asset values, not code defaults.\n")
L.append(f"Total mythic (Rarity 4) items: **{len(my)}** — " + ", ".join(f"{k} {v}" for k, v in collections.Counter(d['itemType'] for d in my).most_common()) + ".\n")
L.append("Item stat notes: mythic rarity multiplies base weapon damage by 1.8 (20% per rarity step) and item stats by 1.4 (10% per step). Attribute effects written as `Source[\"X\"]` are live formulas. `+%mult 1` means a flag is switched on. Tooltip numbers shown are the level-1 preview stored in the asset.\n")
L.append("## How mythics are obtained (verified in code)\n")
L.append("""1. **Boss personal loot tables.** Each boss `CharacterInfo` has its own loot table. A table marked *guaranteed* always gives exactly one item, picked by weight; a non-guaranteed table rolls each entry's percent (times the loot modifier) separately. Loot is rolled **once per party member** per dead enemy (`GenerateLoot` loops over party size), and in Endless mode bosses roll their personal table **twice**.
2. **World loot pool.** Every item not flagged `RemoveFromUnassignedPool` (and not a material/commodity) is in the world pool. On every enemy death, each rarity is rolled independently against `UnassignedItemChancePercentage`: Common 5%, Uncommon 5%, Rare 1%, Legendary 0.2%, **Mythic 0.05%** (times the loot modifier: difficulty loot bonus, quest modifiers, enemy affixes). Only **Boss**-type enemies are allowed to roll Mythic from this pool (Champions cap at Legendary). If the mythic roll succeeds, an item type is chosen uniformly, then (for weapons) a weapon type uniformly, then an item uniformly within it. A mythic can only drop once your active game level is ≥ 5 (`minLevelForMythics`), or the item's own MinLevel if higher.
3. **The Merchant** (island vendor, `canSpawnOnIslands`, Campaign only). Stocks exactly one equipment piece per visit from the world pool with rarity weights **Legendary 70% / Mythic 30%**. This is the most reliable mythic source. The Roguelike Merchant can stock all rarities.
4. **Island events with a Legendary/Mythic restriction.** These call `GetGuaranteedWorldLoot`, which picks the rarity by weight among the allowed rarities using the same table as the world pool (Legendary 0.2 vs Mythic 0.05), so each such item is **20% Mythic / 80% Legendary**, then a random world-pool item of that rarity. Events verified with this restriction: **Heavy Safe** (any terrain, level 4+, 1 item), **Hold your Breath** (Water Temple Ruins, level 4+, 1 item), **Mistress of Chaos** (any terrain, level 3+, equipment only, spawns at a quarter of normal weight; 1 item). **Treasure Chest** (1 item, or 2 in Roguelike) has the same restriction but is an event of rarity 4, and rarity-4 events have spawn weight 0 in the world-map generator, so it effectively never appears while any other chest event is eligible.
5. **Shrines**: rarity weights 50/30/10/2/**1** (1% mythic). **Gambling** (Ulf's Wager, Act 3): rarity weights 25/50/15/8/**2** per roll, drawn from the world pool. Ordinary events that hand out unrestricted random items use weights 40/30/20/10/**0**, so they never give mythics.
6. **Crafting recipes** whose result is a mythic (listed per item).
7. **Fixed island events** (Sword in the Stone → Excalibur; Alchemist's Workbench brews) and **Roguelike character presets** (starting gear) — listed per item where they apply. Bosses that only exist inside an island event (Anthulk, Fenrir, Hodr, Lady of the Well, The Risen Witch) are noted on their items.
8. Town shops (Armorer/Jeweler/Potion Maker) only stock Common–Rare, and the wandering Dwarven Armorsmith / Mysterious Traveler / Weapons Dealer cap at Legendary. Only The Merchant and the Roguelike Merchant can sell mythic equipment; Tim (wandering potion vendor) sells the mythic potions at fixed prices.
9. **Not obtainable / unused**: the mythic fish, the four Essences, and several commodities are removed from the world pool and referenced by nothing (no loot table, node, event, recipe or stocked shop). They are flagged per item below. Fishing holes only ever yield the single fish in their list, none of which is mythic.

Per-item source lines: "World loot pool" means channels 2, 3, 4, 5 apply. Everything else is explicit.
""")

# ---- boss index ----
L.append("## Boss loot index (which boss drops which mythics)\n")
byboss = collections.defaultdict(list)
for d in my:
    for s in d["sources"]:
        if s["kind"] == "personal_loot":
            byboss[s["enemy"]].append((d["name"], d["itemType"], s))
for boss in sorted(byboss):
    s0 = byboss[boss][0][2]
    q = "; ".join(f"{qn} (Act {a}, lvl {lv})" for qn, a, lv, mn in s0.get("quests", []))
    hdr = f"**{boss}** ({s0['enemyType']}"
    hdr += ", not in the random battle pool" if s0.get("excludeFromPool") else ""
    hdr += ")" + (f" — {q}" if q else "")
    if BOSS_EVENTS.get(boss):
        hdr += f" — {BOSS_EVENTS[boss]}"
    L.append(f"- {hdr}")
    for name, typ, s in sorted(byboss[boss]):
        how = f"{s['chance']:g}%" if not s["mustGetOne"] else f"weight {s['chance']:g}/table of {s['nInTable']} (guaranteed one)"
        L.append(f"  - {name} ({typ}) — {how}")
L.append("")

# ---- items by slot ----
for typ in ORDER:
    items = [d for d in my if d["itemType"] == typ]
    if not items: continue
    L.append(f"## {typ} ({len(items)})\n")
    if typ == "Weapon":
        items.sort(key=lambda d: (d["equipment"], d["name"]))
    for d in items:
        L.append(f"### {d['name']}")
        L.append(f"- Stats: {stat_line(d)}")
        if d["description"]:
            L.append(f"- Effect: {clean(d['description'])}")
        for a in d["attributeEffects"]:
            L.append(f"- Attribute: {clean(a)}")
        for gsk in d["grantedSkills"]:
            L.append(f"- Granted skill: {clean(gsk)}")
        for t in d["triggers"]:
            L.append(f"- Trigger: {trig(t)}")
        if d["consumable"]:
            L.append(f"- On use: {clean(d['consumable'])}")
        if d["fixedBuy"]:
            L.append(f"- Fixed buy price: {d['fixedBuy']:g} gold")
        for s in source_lines(d):
            L.append(f"- Source: {s}")
        L.append("")

open("../mythic-items.md", "w", encoding="utf-8").write("\n".join(L))
print("wrote mythic-items.md", len(L), "lines")
