"""Validate a build.json (see ~/.claude/skills/sr-build/references/build-spec.md) against the extracted game data.

Usage:  python C:/Claude/General/stolen-realm/tools/check_build_spec.py <BuildN-Name.json>

Checks: every skill exists (name + tree) in data/skills/INDEX.md, no Bard skills, no duplicates, tier gates
(2/4/6/10 earlier-tier points in the same tree) and Requires entries hold at the moment each point is spent, total
points match the level (12 by level 10, then 1 per level), creation stats sum to 50 within 8..14, per-level blocks sum
to 5 and reach the level, weapon class is real, mythics exist in mythic-items.md, fortunes exist in fortunes.md and
none of the four slots is on the ban list, rotation skills are learned (or marked as granted by a fortune/item) and
targets and conditions use the driver's fixed vocabulary, test_loadout items and enchants exist. Prints the level at which each skill arrives and the stats at the final level.
Exit code 1 when any ERROR was printed.
"""
import json, os, re, sys

ROOT = r"C:\Claude\General\stolen-realm"
RULES = os.path.expandvars(r"%USERPROFILE%\.claude\skills\sr-build\references\rules.md")
TIER_COST = [1, 1, 1, 2, 3]
TIER_GATE = [0, 2, 4, 6, 10]
STATS = ["Might", "Dexterity", "Intelligence", "Vitality", "Reflex"]
WEAPONS = {"1H Sword", "2H Sword", "1H Axe", "2H Axe", "1H Mace", "2H Mace", "Polearm", "Bow", "1H Gun", "2H Gun", "Staff", "Wand", "Fist Weapon", "Unarmed"}
TARGETS = {"self", "ally", "partner", "enemy", "strongest", "enemies", "ground"}
SLOTS = {"Weapon", "Off-hand", "Armor", "Head", "Ring", "Amulet"}
COND = re.compile(r"^(ready|turn(==|>=|<=)\d+|enemies_in_range>=\d+|enemies_clustered>=\d+|enemies_near_ally>=\d+|enemies(>=|<=)\d+|health[<>]\d+|mana[<>]\d+|ally_health[<>]\d+|ally_adjacent|partner_in_range|(has|no)_status:.+|ally_(has|no)_status:.+|boss_present)$", re.I)

errors, warnings = [], []
def err(m): errors.append(m); print("ERROR  ", m)
def warn(m): warnings.append(m); print("WARN   ", m)
def info(m): print("       ", m)


def load_index():
    idx = {}
    for line in open(os.path.join(ROOT, "data", "skills", "INDEX.md"), encoding="utf-8"):
        if not line.startswith("| ") or line.startswith("| Skill") or line.startswith("|---"):
            continue
        c = [x.strip() for x in line.strip().strip("|").split("|")]
        if len(c) < 6:
            continue
        name, tree, tier, kind, _, req = c[:6]
        idx.setdefault((name, tree), []).append({"tier": int(tier), "kind": kind, "requires": req})
    return idx


def headings(path):
    out = set()
    for line in open(os.path.join(ROOT, path), encoding="utf-8"):
        if line.startswith("### "):
            out.add(re.split(r"\s+—", line[4:].strip())[0].strip())
    return out


def banned():
    b = set()
    if not os.path.exists(RULES):
        return b
    in_tab = False
    for line in open(RULES, encoding="utf-8"):
        if line.startswith("## Banned fortunes"):
            in_tab = True
            continue
        if in_tab and line.startswith("## "):
            break
        if in_tab and line.startswith("| ") and not line.startswith("| Fortune") and not line.startswith("|---"):
            b.add(line.strip().strip("|").split("|")[0].strip())
    return b


def points_at(level):
    return 12 + max(0, level - 10) if level >= 10 else max(1, level + 2)


def level_of_point(pt):
    return 10 if pt <= 12 else pt - 2


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    spec = json.load(open(sys.argv[1], encoding="utf-8"))
    if spec.get("members"):
        # party spec: every member is a full build spec with member_name and formation; party-level checks first
        names = [m.get("member_name") for m in spec["members"]]
        if len(names) != len(set(names)) or any(not n for n in names):
            err(f"party members need unique member_name values: {names}")
        for m in spec["members"]:
            if m.get("formation", "front") not in ("front", "behind", "hold"):
                err(f"member {m.get('member_name')}: formation '{m.get('formation')}' is not front/behind/hold")
            if not m.get("level"):
                m["level"] = spec.get("level", 30)
        for m in spec["members"]:
            print(f"---- member {m.get('member_name')} ({m.get('formation', 'front')})")
            check_one(m)
        print(f"== party {spec.get('name')}: {len(errors)} error(s), {len(warnings)} warning(s)")
        sys.exit(1 if errors else 0)
    check_one(spec)
    print(f"== {len(errors)} error(s), {len(warnings)} warning(s)")
    sys.exit(1 if errors else 0)


def check_one(spec):
    idx = load_index()
    level = int(spec.get("level", 30))
    print(f"== {spec.get('name') or spec.get('member_name')} (build {spec.get('build', '')}), level {level}")

    # ---- skills
    spent = {}
    learned = []
    total = 0
    for i, e in enumerate(spec.get("skills", []), 1):
        name, tree = e.get("skill"), e.get("tree")
        rows = idx.get((name, tree))
        if not rows:
            near = [k for k in idx if k[0] == name]
            err(f"skill {i}: '{name}' not in tree {tree}" + (f" (exists in {[k[1] for k in near]})" if near else ""))
            continue
        if len(rows) > 1:
            warn(f"skill {i}: '{name}' appears {len(rows)} times in {tree}; using tier {rows[0]['tier']}")
        r = rows[0]
        if tree == "Bard":
            err(f"skill {i}: '{name}' is a Bard skill (unreleased tree)")
        if (name, tree) in learned:
            err(f"skill {i}: '{name}' taken twice")
        tier = r["tier"]
        have = spent.get(tree, 0)
        if have < TIER_GATE[tier - 1]:
            err(f"skill {i}: '{name}' is {tree} tier {tier}, needs {TIER_GATE[tier - 1]} earlier {tree} points, only {have} spent so far")
        if r["requires"] and (r["requires"], tree) not in learned:
            err(f"skill {i}: '{name}' requires '{r['requires']}' first")
        cost = TIER_COST[tier - 1]
        first_pt = total + 1
        total += cost
        spent[tree] = have + cost
        learned.append((name, tree))
        lv = level_of_point(total)
        info(f"pt {first_pt:>2}{'-' + str(total) if cost > 1 else '':<3} L{lv:<2} {tree:<10} T{tier} {r['kind']:<7} {name}")
    need = points_at(level)
    if total != need:
        err(f"skill points: {total} spent, level {level} gives {need}")
    else:
        info(f"skill points: {total} = {need} OK; per tree {dict(sorted(spent.items(), key=lambda x: -x[1]))}")

    # ---- attributes
    a = spec.get("attributes", {})
    cre = a.get("creation", {})
    if set(cre) != set(STATS):
        err(f"attributes.creation must name exactly {STATS}")
    else:
        for s, v in cre.items():
            if not 8 <= int(v) <= 14:
                err(f"attributes.creation {s}={v} outside 8..14")
        if sum(int(v) for v in cre.values()) != 50:
            err(f"attributes.creation sums to {sum(int(v) for v in cre.values())}, must be 50")
    final = {s: int(cre.get(s, 8)) for s in STATS}
    lv = 1
    for b in a.get("per_level", []):
        pts = {s: int(b.get(s, 0)) for s in STATS}
        if sum(pts.values()) != 5:
            err(f"per_level block until {b.get('until_level')} gives {sum(pts.values())} points, must be 5")
        until = int(b.get("until_level", level))
        n = max(0, until - lv)
        for s in STATS:
            final[s] += pts[s] * n
        lv = until
    if a.get("per_level"):
        if lv != level:
            err(f"per_level blocks stop at level {lv}, build level is {level}")
        info("stats at level %d (before gear/fortunes): %s" % (lv, ", ".join(f"{s} {final[s]}" for s in STATS)))

    # ---- gear
    g = spec.get("gear", {})
    if g.get("weapon_class") not in WEAPONS:
        err(f"gear.weapon_class '{g.get('weapon_class')}' not one of {sorted(WEAPONS)}")

    # ---- mythics / fortunes
    myth = headings("mythic-items.md")
    for n in spec.get("mythics", []) + spec.get("mythics_avoid", []):
        if n not in myth:
            err(f"mythic '{n}' not in mythic-items.md")
    fort = headings("fortunes.md")
    ban = banned()
    f = spec.get("fortunes", {})
    slots = f.get("slots", [])
    if len(slots) != 4:
        err(f"fortunes.slots has {len(slots)} entries, need 4")
    for n in slots:
        if n not in fort:
            err(f"fortune '{n}' not in fortunes.md")
        elif n in ban:
            err(f"fortune '{n}' is on the ban list")
    for n in f.get("targets", []) + f.get("avoid", []):
        if n not in fort:
            err(f"fortune '{n}' not in fortunes.md")
    for n in f.get("targets", []):
        if n in ban:
            warn(f"fortune target '{n}' is banned; drop it")

    # ---- rotation
    names = {n for n, _ in learned}
    for i, e in enumerate(spec.get("rotation", {}).get("priority", []), 1):
        if e.get("target") not in TARGETS:
            err(f"rotation {i}: target '{e.get('target')}' not in {sorted(TARGETS)}")
        if e.get("skill") not in names and e.get("source") not in ("fortune", "item", "basic"):
            err(f"rotation {i}: '{e.get('skill')}' is not a learned skill (add \"source\": \"fortune\"/\"item\"/\"basic\" if it comes from one)")
        when = e.get("when", [])
        if isinstance(when, str):
            err(f"rotation {i}: 'when' must be a list of conditions, not a string")
            when = []
        for w in when:
            if not COND.match(str(w).strip().replace(" ", "")):
                err(f"rotation {i}: condition '{w}' is not in the driver vocabulary (ready, turn==N, turn>=N, turn<=N, enemies_in_range>=N, enemies_clustered>=N, enemies>=N, enemies<=N, health<N, health>N, mana<N, mana>N, has_status:Name, no_status:Name, boss_present)")

    # ---- test loadout
    lo = spec.get("test_loadout")
    if not lo:
        warn("no test_loadout: the build cannot be played by /sr-iterate until one is added")
    else:
        item_names = set(json.load(open(os.path.join(ROOT, "data", "item_guids.json"), encoding="utf-8")).keys())
        mod_names = {d["m_Name"] for d in json.load(open(os.path.join(ROOT, "data", "ItemMod.json"), encoding="utf-8"))}
        for slot, entry in lo.items():
            if slot == "consumables":
                for con in entry or []:
                    n = con["item"] if isinstance(con, dict) else con
                    if n not in item_names:
                        err(f"test_loadout consumable '{n}' not found")
                continue
            if slot not in SLOTS:
                err(f"test_loadout slot '{slot}' unknown ({sorted(SLOTS)})")
                continue
            if entry is None:
                continue
            n = entry["item"] if isinstance(entry, dict) else entry
            if n not in item_names:
                err(f"test_loadout {slot}: item '{n}' not found in the game data")
            for m in (entry.get("mods", []) if isinstance(entry, dict) else []):
                if m not in mod_names:
                    err(f"test_loadout {slot}: enchant '{m}' not found in ItemMod.json")



if __name__ == "__main__":
    main()
