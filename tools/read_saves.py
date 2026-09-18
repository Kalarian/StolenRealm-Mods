"""Print a markdown summary of everything the player owns, read from the live save files.

Usage (from anywhere):  python C:/Claude/General/stolen-realm/tools/read_saves.py [--out FILE]

Covers every non-deleted campaign character (name, level, attributes, learned skills, equipped gear, bag),
the shared item stash (equipment by slot with mods, then materials/consumables as counts) and the fortune pool
(SharedFortunes.json unioned with every character's own list, highest level kept).

GUIDs are resolved from data/item_guids.json (items, item mods, fortunes, skills) plus the Guid inside each
SkillInfo's Odin serializationData (covers skills missing from item_guids). Rarity/type/stats come from
ItemInfo.json, WeaponInfo.json, ItemMod.json and EventStatus.json of the current extract.
Read-only: never writes to the save folder.
"""
import json, os, sys, glob, uuid, math, re, datetime, collections

ROOT = r"C:\Claude\General\stolen-realm"
DATA = os.path.join(ROOT, "data")
SAVES = os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\Burst2Flame Entertainment\Stolen Realm")

RARITY = ["Common", "Uncommon", "Rare", "Legendary", "Mythic"]
ITEM_TYPE = ["Weapon", "Shield", "Head", "Armor", "Ring", "Amulet", "Consumable", "Material", "Tool", "Commodity"]
EQUIP_TYPE = ["1H Sword", "2H Axe", "2H Sword", "Polearm", "Bow", "1H Gun", "2H Gun", "1H Axe", "Staff", "Wand", "1H Mace", "2H Mace", "Unarmed", "Shield", "Fist Weapon", "None"]
SLOT = {0: "Weapon", 1: "Off-hand", 2: "Armor", 3: "Head", 4: "Ring", 5: "Amulet"}
TREE = {0: "Fire", 1: "Lightning", 2: "Cold", 3: "Warrior", 4: "Light", 5: "Ranger", 6: "Shadow", 7: "Thief", 8: "Basic", 9: "Innate", 10: "Monk", 11: "Nature", 12: "Chaos", 13: "Bard"}
STATS = ["Might", "Dexterity", "Intelligence", "Vitality", "Reflex"]


def load(name):
    with open(os.path.join(DATA, name + ".json"), encoding="utf-8") as f:
        return json.load(f)


def odin_guid(d):
    sd = d.get("serializationData")
    sb = sd.get("SerializedBytes") if isinstance(sd, dict) else None
    if not sb or not isinstance(sb, str):
        return None
    try:
        b = bytes.fromhex(sb)
    except ValueError:
        return None
    k = "Guid".encode("utf-16-le")
    i = b.find(k)
    if i < 0 or i + len(k) + 16 > len(b):
        return None
    return str(uuid.UUID(bytes_le=b[i + len(k):i + len(k) + 16]))


def clean(s):
    s = re.sub(r"<[^>]+>", "", s or "")
    s = s.replace("@", "").replace("\r", "")
    return re.sub(r"\s*\n\s*", "; ", s.strip())


def main():
    out_path = None
    if "--out" in sys.argv:
        out_path = sys.argv[sys.argv.index("--out") + 1]

    guids = json.load(open(os.path.join(DATA, "item_guids.json"), encoding="utf-8"))
    name_of = {}
    for n, gs in guids.items():
        for g in gs:
            name_of.setdefault(g, n)

    items = {d["m_Name"]: d for d in load("ItemInfo")}
    weapons = {d["m_Name"]: d for d in load("WeaponInfo")}
    mods = {d["m_Name"]: d for d in load("ItemMod")}
    mod_by_guid = {odin_guid(d): d for d in load("ItemMod") if odin_guid(d)}
    fortunes = {d["m_Name"]: d for d in load("EventStatus") if d.get("StatusType") == 1}
    fortune_by_guid = {odin_guid(d): d for d in fortunes.values() if odin_guid(d)}
    skills_by_guid = {odin_guid(d): d for d in load("SkillInfo") if odin_guid(d)}

    def item_info(guid):
        n = name_of.get(guid)
        if not n:
            return None, {"_unknown": guid}
        d = weapons.get(n) or items.get(n) or {}
        return n, d

    def item_line(it, with_slot=False):
        n, d = item_info(it["ItemGuid"])
        if n is None:
            return f"unknown item {it['ItemGuid']}"
        t = ITEM_TYPE[d.get("ItemType", 0)] if d else "?"
        if d is weapons.get(n):
            et = d.get("EquipmentType")
            t = EQUIP_TYPE[et] if isinstance(et, int) and et < len(EQUIP_TYPE) else "Weapon"
        r = RARITY[d.get("Rarity", 0)] if d else "?"
        parts = [f"**{n}**", f"{r} {t}", f"iLvl {it.get('ItemLevel')}"]
        if it.get("NumStacks", 1) > 1:
            parts.append(f"x{it['NumStacks']}")
        base = [f"{s} {int(d[s])}" for s in STATS if d and d.get(s)]
        if base:
            parts.append(", ".join(base))
        if d and d.get("DamageRatio"):
            parts.append(f"dmg ratio {d['DamageRatio']:.2f}")
        ml = []
        for mg in it.get("ItemModIDs") or []:
            m = mod_by_guid.get(mg) or mods.get(name_of.get(mg, ""))
            ml.append(f"{m['m_Name']} ({clean(m.get('Description'))})" if m else f"mod {mg[:8]}")
        if ml:
            parts.append("mods: " + "; ".join(ml))
        od = clean(d.get("OptionalDescription")) if d else ""
        if od:
            parts.append(od[:220])
        return " · ".join(parts)

    L = []
    L.append(f"# What the player owns (read from saves {datetime.date.today().isoformat()})\n")
    L.append(f"Save folder: `{SAVES}`\n")

    # ---- characters
    chars = []
    for f in sorted(glob.glob(os.path.join(SAVES, "Character*.json"))):
        if not re.search(r"Character\d+\.json$", f):
            continue
        try:
            c = json.load(open(f, encoding="utf-8"))
        except Exception as e:
            L.append(f"- could not read {os.path.basename(f)}: {e}")
            continue
        if c.get("IsDeleted"):
            continue
        chars.append((os.path.basename(f), c))

    L.append("## Characters\n")
    pool = {}
    for fn, c in chars:
        lvl = int(math.floor(c.get("ExperienceLevel", 1)))
        kind = "Roguelike" if c.get("IsRoguelikeCharacter") else "Campaign"
        L.append(f"### {c.get('CharacterName')} — level {lvl} ({kind}, {fn})\n")
        a = c.get("AttributesAndSkills", {})
        # attribute bases are stored under asset GUIDs whose names are "MightBase" .. "ReflexBase"
        stats = {}
        for k, v in a.items():
            n = name_of.get(k, "")
            m = re.match(r"^(Might|Dexterity|Intelligence|Vitality|Reflex)Base$", n)
            if m:
                stats[m.group(1)] = int(v)
        if stats:
            L.append("- Attributes (stored value; every formula uses value - 8): " + ", ".join(f"{s} {stats[s]}" for s in STATS if s in stats))
        learned = collections.defaultdict(list)
        for k, v in a.items():
            d = skills_by_guid.get(k)
            if d and v >= 1:
                learned[TREE.get(d.get("SkillType"), "?")].append(f"{d['SkillName']} (T{d.get('Tier', 0)})")
        if learned:
            L.append(f"- Learned skills ({sum(len(v) for v in learned.values())}): " + " | ".join(f"{t}: " + ", ".join(v) for t, v in learned.items()))
        eq = sorted([i for i in c.get("Items", []) if i.get("Equipped")], key=lambda i: i["EquippedSlotIndex"])
        if eq:
            L.append("- Equipped:")
            for it in eq:
                L.append(f"  - {SLOT.get(it['EquippedSlotIndex'], it['EquippedSlotIndex'])}: {item_line(it)}")
        bag = [i for i in c.get("Items", []) if not i.get("Equipped")]
        if bag:
            L.append("- Bag:")
            for it in bag:
                L.append(f"  - {item_line(it)}")
        for fd in c.get("FortuneSaveData", []) or []:
            g = fd.get("Guid")
            pool[g] = max(pool.get(g, 0), fd.get("Level", 0))
        L.append("")

    # ---- stash
    L.append("## Item stash (shared)\n")
    st = os.path.join(SAVES, "ItemStash.json")
    if os.path.exists(st):
        s = json.load(open(st, encoding="utf-8"))
        arr = s.get("Items", s) if isinstance(s, dict) else s
        gear, other = [], collections.Counter()
        for it in arr:
            n, d = item_info(it["ItemGuid"])
            t = d.get("ItemType", 0) if d and "ItemType" in d else 6
            if t <= 5:
                gear.append((t, -d.get("Rarity", 0), -it.get("ItemLevel", 0), it))
            else:
                other[(n or it["ItemGuid"], ITEM_TYPE[t] if t < len(ITEM_TYPE) else "?")] += it.get("NumStacks", 1)
        gear.sort(key=lambda x: (x[0], x[1], x[2]))
        L.append(f"{len(gear)} pieces of equipment, {sum(other.values())} other items.\n")
        cur = None
        for t, _, _, it in gear:
            slot = ITEM_TYPE[t]
            if slot != cur:
                L.append(f"### {slot}s\n")
                cur = slot
            L.append(f"- {item_line(it)}")
        if other:
            L.append("\n### Materials, consumables, commodities\n")
            L.append(", ".join(f"{n} x{c}" for (n, t), c in sorted(other.items())))
    else:
        L.append("(no ItemStash.json)")
    L.append("")

    # ---- fortunes
    L.append("## Fortune pool (SharedFortunes.json + every character, highest level kept)\n")
    sf = os.path.join(SAVES, "SharedFortunes.json")
    if os.path.exists(sf):
        for g, lv in json.load(open(sf, encoding="utf-8")).items():
            pool[g] = max(pool.get(g, 0), lv)
    rows = []
    for g, lv in pool.items():
        d = fortune_by_guid.get(g) or fortunes.get(name_of.get(g, ""))
        if d:
            rows.append((-d.get("Rarity", 0), d["m_Name"], RARITY[d.get("Rarity", 0)], int(lv), clean(d.get("statusDescription"))))
        else:
            rows.append((1, name_of.get(g, g), "?", int(lv), ""))
    rows.sort()
    L.append("| Fortune | Rarity | Level | Effect |")
    L.append("|---|---|---|---|")
    for _, n, r, lv, desc in rows:
        L.append(f"| {n} | {r} | {lv} | {desc[:200]} |")
    L.append("")
    owned = {r[1] for r in rows}
    missing = sorted((RARITY[d.get('Rarity', 0)], n) for n, d in fortunes.items() if n not in owned)
    L.append(f"Not yet owned ({len(missing)}): " + ", ".join(f"{n} ({r})" for r, n in sorted(missing, key=lambda x: (-RARITY.index(x[0]), x[1]))))

    text = "\n".join(L) + "\n"
    if out_path:
        open(out_path, "w", encoding="utf-8").write(text)
        print("wrote", out_path, len(L), "lines")
    else:
        sys.stdout.reconfigure(encoding="utf-8")
        print(text)


if __name__ == "__main__":
    main()
