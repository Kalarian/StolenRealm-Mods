"""Rebuild BattleStats run files from the per-battle JSON files written before run history existed.

Usage:  python tools/rebuild_runs.py [--write] [--force] [--gap 45] [--exclude-names TestBuild,Test1,...]

BattleStats has saved one JSON per battle since 2026-09-15 (config WriteJson). Each one carries, per character, the
game's own stat dictionary including the mod's own keys (GameStats: "DamageDealt", "key1000", ...), so a run page can be
rebuilt from a group of battles exactly as the game would have drawn it. This groups the battles of the player's own
characters by time (battles inside a run are minutes apart, runs hours), merges them with the same rules the mod uses
(sums; biggest hit is a max carrying its source; the top-8 source list and best skill are re-derived from the merged
per-source totals) and writes one run-<date>.json per group into BepInEx\\BattleStats\\runs.

What cannot be rebuilt: the per-ability breakdown behind each number (deliberately left out of the battle files, so
hovering a number in a rebuilt run shows nothing) and the difficulty and quest of the run (never recorded per battle).
Rebuilt files carry "reconstructed": true and say so in the history list instead of naming a quest.

Without --write it only prints what it would do. Battles already covered by a saved run file are skipped.
"""
import os, sys, io, json, glob, datetime, collections

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\Stolen Realm"
FOLDER = os.path.join(GAME, "BepInEx", "BattleStats")
RUNS = os.path.join(FOLDER, "runs")
# characters the automated harness fights with: their battles are tests, not the player's runs
TEST_NAMES = {"TestBuild", "Test1", "Test2", "Test3", "Test4", "Test5", "Test6", "Fencer", "Shade", "Warden", "Longbow"}

BATTLE_STATS = ["DamageDealt", "SummonDamageDealt", "DamageReturned", "DamageTaken", "SummonDamageTaken",
                "BlockedByArmor", "BlockedByMagicArmor", "BlockedByResistance", "BlockedByDamageReduction",
                "BlockedByShields", "HealingAdministered", "HealingReceived"]          # the game's enum order
BIGGEST, BIGGEST_SRC = 1014, 1015
BEST_SRC, BEST_DMG = 1016, 1017
TOP_SRC, TOP_DMG, TOP_HITS, TOP_COUNT = 1200, 1220, 1240, 8
KILLS, HITS, CRITS = 1012, 1011, 1010
DIRECT, TICKS, TILES, OVERKILL = 1000, 1001, 1002, 1013
CASTS, FREE, MANA, HEXES, TAKEN_TICKS = 1020, 1021, 1022, 1023, 1024
ELEMENT_BASE, STATUS_FLAG, NONE = 1100, 100000, -1
# DamageType (decomp/DamageType.cs): the element keys are ELEMENT_BASE + this value
DAMAGE_TYPES = {"Physical": 1, "Fire": 2, "Cold": 3, "Lightning": 4, "Shadow": 5, "Healing": 6, "Mana": 7, "Holy": 8, "Weapon": 9}
SKILLS_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "data", "skills")


def source_codes():
    """Display name -> the code BattleStats uses (action index, or 100000 + status index), from the extracted tables.
    Duplicated names are harmless: the game turns the code back into the same name when it draws the page."""
    codes = {}
    for fname, offset in (("ACTIONS.md", 0), ("STATUSES.md", STATUS_FLAG)):
        path = os.path.join(SKILLS_DIR, fname)
        if not os.path.exists(path):
            continue
        for line in io.open(path, encoding="utf-8"):
            parts = [c.strip() for c in line.strip().strip("|").split("|")]
            if len(parts) < 2 or not parts[0].isdigit():
                continue
            name = parts[1]
            if name and name not in codes:
                codes[name] = offset + int(parts[0])
            if offset == STATUS_FLAG and name and (name + " (tick)") not in codes:
                codes[name + " (tick)"] = offset + int(parts[0])
    return codes


CODES = None


def code_of(name):
    global CODES
    if CODES is None:
        CODES = source_codes()
    if not name:
        return None
    return CODES.get(name) or CODES.get(name.replace(" (tick)", ""))


def fill_from_summary(bk, s):
    """Battles recorded before the mod shared its own keys (15 Sep) still hold every number in the recorder's own
    fields, so anything missing from GameStats is taken from there instead."""
    tiles = float(s.get("GroundEnter") or 0) + float(s.get("TurnStart") or 0) + float(s.get("GroundCreate") or 0)
    for key, val in ((DIRECT, s.get("Direct")), (TICKS, s.get("StatusTicks")), (TILES, tiles),
                     (HITS, s.get("Hits")), (CRITS, s.get("Crits")), (KILLS, s.get("Kills")),
                     (OVERKILL, s.get("Overkill")), (CASTS, s.get("Casts")), (FREE, s.get("FreeActions")),
                     (MANA, s.get("ManaSpent")), (HEXES, s.get("HexesMoved")), (TAKEN_TICKS, s.get("DamageTakenFromTicks"))):
        if key not in bk and val:
            bk[key] = float(val)
    for element, amount in (s.get("DamageByElement") or {}).items():
        k = ELEMENT_BASE + DAMAGE_TYPES.get(element, 0)
        if DAMAGE_TYPES.get(element) and k not in bk and amount:
            bk[k] = float(amount)
    if BIGGEST not in bk and s.get("BiggestHit"):
        bk[BIGGEST] = float(s["BiggestHit"])
        c = code_of(s.get("BiggestHitAction"))
        bk[BIGGEST_SRC] = float(c if c is not None else NONE)   # never guess: an unknown source shows as "?"
    return bk


def key_of(name):
    """GameStats entry name -> the integer key the window reads."""
    if name in BATTLE_STATS:
        return BATTLE_STATS.index(name)
    if name.startswith("key"):
        try:
            return int(name[3:])
        except ValueError:
            return None
    return None


def load_battles():
    out = []
    for f in sorted(glob.glob(os.path.join(FOLDER, "battle-*.json"))):
        try:
            d = json.load(open(f, encoding="utf-8"))
        except Exception:
            continue
        summary = d.get("Summary") or []
        names = [s.get("Name") for s in summary if s.get("Name")]
        if not names or any(n in TEST_NAMES for n in names):
            continue
        try:
            start = datetime.datetime.strptime(d["Started"], "%Y-%m-%d %H:%M:%S")
            end = datetime.datetime.strptime(d["Ended"], "%Y-%m-%d %H:%M:%S")
        except Exception:
            continue
        out.append({"file": os.path.basename(f), "start": start, "end": end, "d": d})
    return out


def already_saved_windows():
    """(start, end) of every run already on disk, so its battles are not rebuilt a second time. With --force our own
    rebuilt files do not count, so they can be made again from scratch; runs the game itself saved always do."""
    spans = []
    force = "--force" in sys.argv
    for f in glob.glob(os.path.join(RUNS, "run-*.json")):
        try:
            r = json.load(open(f, encoding="utf-8"))
            if force and r.get("reconstructed"):
                continue
            s = datetime.datetime.strptime(r["started"], "%Y-%m-%d %H:%M:%S")
            e = datetime.datetime.strptime(r["ended"], "%Y-%m-%d %H:%M:%S")
            spans.append((s - datetime.timedelta(minutes=10), e + datetime.timedelta(minutes=10), os.path.basename(f)))
        except Exception:
            continue
    return spans


def merge(run_keys, battle_keys):
    """The mod's own rules: sum everything, keep the biggest hit with its source, re-derive the top list."""
    for k, v in battle_keys.items():
        if k in (BIGGEST, BIGGEST_SRC, BEST_SRC, BEST_DMG):
            continue
        if TOP_SRC <= k < TOP_HITS + TOP_COUNT:
            continue
        run_keys[k] = run_keys.get(k, 0.0) + v
    if BIGGEST in battle_keys and battle_keys[BIGGEST] > run_keys.get(BIGGEST, 0.0):
        run_keys[BIGGEST] = battle_keys[BIGGEST]
        run_keys[BIGGEST_SRC] = battle_keys.get(BIGGEST_SRC, -1)


def rederive_top(run_keys, per_source):
    """per_source: code -> [damage, hits] summed over the run's battles."""
    for i in range(TOP_COUNT):
        for base in (TOP_SRC, TOP_DMG, TOP_HITS):
            run_keys.pop(base + i, None)
    run_keys.pop(BEST_SRC, None)
    run_keys.pop(BEST_DMG, None)
    top = sorted(((c, dh) for c, dh in per_source.items() if dh[0] > 0), key=lambda x: -x[1][0])[:TOP_COUNT]
    for i, (code, dh) in enumerate(top):
        run_keys[TOP_SRC + i] = float(code)
        run_keys[TOP_DMG + i] = dh[0]
        run_keys[TOP_HITS + i] = dh[1]
    if top:
        run_keys[BEST_SRC] = float(top[0][0])
        run_keys[BEST_DMG] = top[0][1][0]


def build_run(battles):
    chars = collections.OrderedDict()      # name -> {"keys":{}, "per_source":{}, "skills":{}, "biggest":(dmg,name)}
    fights = []
    for n, b in enumerate(battles, 1):
        d = b["d"]
        fights.append({"n": n, "victory": bool(d.get("Victory")), "turns": int(d.get("Turns") or 0)})
        for s in d.get("Summary", []):
            name = s.get("Name")
            if not name:
                continue
            c = chars.setdefault(name, {"keys": {}, "per_source": {}, "skills": collections.Counter(),
                                        "skill_hits": collections.Counter(), "biggest": (0.0, None)})
            bk = {}
            for gn, gv in (s.get("GameStats") or {}).items():
                k = key_of(gn)
                if k is not None:
                    bk[k] = float(gv)
            fill_from_summary(bk, s)
            merge(c["keys"], bk)
            got_top = False
            for i in range(TOP_COUNT):                       # per-source totals, for the run-wide top list
                code = bk.get(TOP_SRC + i)
                dmg = bk.get(TOP_DMG + i)
                if code is None or dmg is None or dmg <= 0:
                    continue
                got_top = True
                cur = c["per_source"].setdefault(int(code), [0.0, 0.0])
                cur[0] += dmg
                cur[1] += bk.get(TOP_HITS + i, 0.0)
            if not got_top:                                  # older battle: rebuild the per-source totals from the skill list
                for sk in s.get("Skills", []):
                    code = code_of(sk.get("Name"))
                    dmg = float(sk.get("Damage") or 0)
                    if code is None or dmg <= 0:
                        continue
                    cur = c["per_source"].setdefault(code, [0.0, 0.0])
                    cur[0] += dmg
                    cur[1] += float(sk.get("Hits") or 0)
            for sk in s.get("Skills", []):                   # names, for the readable summary only
                c["skills"][sk.get("Name", "?")] += float(sk.get("Damage") or 0)
                c["skill_hits"][sk.get("Name", "?")] += int(sk.get("Hits") or 0)
            big = float(s.get("BiggestHit") or 0)
            if big > c["biggest"][0]:
                c["biggest"] = (big, s.get("BiggestHitAction"))

    out_chars = []
    for name, c in chars.items():
        rederive_top(c["keys"], c["per_source"])
        summary = {}
        for i, sn in enumerate(BATTLE_STATS):
            v = c["keys"].get(i, 0.0)
            if v:
                summary[sn] = round(v, 1)
        for label, k in (("Kills", KILLS), ("Hits", HITS), ("Crits", CRITS)):
            if c["keys"].get(k):
                summary[label] = c["keys"][k]
        if c["biggest"][0]:
            summary["BiggestHit"] = round(c["biggest"][0], 1)
            if c["biggest"][1]:
                summary["BiggestHitSource"] = c["biggest"][1]
        if c["skills"]:
            best, dmg = c["skills"].most_common(1)[0]
            summary["BestSkill"] = best
            summary["BestSkillDamage"] = round(dmg, 1)
        out_chars.append({
            "name": name, "owner": "", "level": 0,
            "summary": summary,
            "keys": {str(k): round(v, 4) for k, v in sorted(c["keys"].items())},
        })

    return {
        "version": 1,
        "started": battles[0]["start"].strftime("%Y-%m-%d %H:%M:%S"),
        "ended": battles[-1]["end"].strftime("%Y-%m-%d %H:%M:%S"),
        "quest": "reconstructed from battle files",
        "act": 0,
        "battles": len(battles),
        "wins": sum(1 for f in fights if f["victory"]),
        "fights": fights,
        "characters": out_chars,
        "reconstructed": True,
        "rebuilt_from": [b["file"] for b in battles],
    }


def main():
    write = "--write" in sys.argv
    gap = int(sys.argv[sys.argv.index("--gap") + 1]) if "--gap" in sys.argv else 45
    battles = load_battles()
    print("%d battles fought by the player's own characters" % len(battles))
    spans = already_saved_windows()
    keep = []
    for b in battles:
        hit = next((n for s, e, n in spans if s <= b["start"] <= e), None)
        if hit:
            print("  skipping %s: already inside %s" % (b["file"], hit))
        else:
            keep.append(b)
    if not keep:
        print("nothing left to rebuild")
        return
    groups, cur = [], [keep[0]]
    for prev, b in zip(keep, keep[1:]):
        if (b["start"] - prev["end"]).total_seconds() / 60.0 > gap:
            groups.append(cur)
            cur = []
        cur.append(b)
    groups.append(cur)
    print("\n%d run(s) from %d battles (a gap over %d minutes starts a new run):" % (len(groups), len(keep), gap))
    os.makedirs(RUNS, exist_ok=True)
    for g in groups:
        run = build_run(g)
        path = os.path.join(RUNS, "run-" + g[0]["start"].strftime("%Y%m%d-%H%M%S") + ".json")
        who = ", ".join(c["name"] for c in run["characters"])
        dmg = sum(c["summary"].get("DamageDealt", 0) for c in run["characters"])
        print("  %s  %s  %d battle(s), %d won  %-45s  %s damage" % (
            os.path.basename(path), run["started"], run["battles"], run["wins"], who, format(int(dmg), ",")))
        if write:
            if os.path.exists(path):
                try:
                    mine = json.load(open(path, encoding="utf-8")).get("reconstructed")
                except Exception:
                    mine = False
                if not (mine and "--force" in sys.argv):
                    print("     already exists, left alone" + ("" if mine else " (a live run file)"))
                    continue
            json.dump(run, open(path, "w", encoding="utf-8"), indent=1)
            print("     written (%d bytes)" % os.path.getsize(path))
    if not write:
        print("\nnothing written; pass --write to create these files")


if __name__ == "__main__":
    main()
