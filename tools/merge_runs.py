"""Merge BattleStats run files that belong to the same run into one.

Usage:  python tools/merge_runs.py <run-a.json> <run-b.json> [more...] [--write] [--closed|--open]
                                   [--quest-id "<id>"] [--out <file>]

A run split across several files (for instance a quest played before the mod could carry a run across a restart) is put
back together with the same rules the mod uses: every number is summed, the biggest hit keeps the larger value with its
own source, and the best skill and the top-source list are re-derived from the merged per-ability totals. The result is
written to the earliest file's name and the other files are deleted, so the run appears once in the history list.

Without --write it only prints what it would do. --quest-id sets the quest fingerprint (RunStats.QuestId) so the mod can
carry the merged run on when you load back into that quest; leave it out to keep whatever the inputs had.
"""
import os, sys, json, shutil, datetime

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rebuild_runs as rb

BUCKET_BASE, BUCKET_HITS_BASE, BUCKET_SPAN, B_DEALT = 2000000, 6000000, 200000, 98


def name_of(code):
    """Source code -> display name, the way the game renders it in the window."""
    if not hasattr(name_of, "table"):
        name_of.table = {v: k for k, v in rb.source_codes().items()}
    code = int(code)
    name = name_of.table.get(code)
    if name is None and code >= rb.STATUS_FLAG:
        name = name_of.table.get(code)
    return name or "?"


def load(path):
    d = json.load(open(path, encoding="utf-8"))
    d["_path"] = path
    return d


def merge_chars(runs):
    order, by_name = [], {}
    for r in runs:
        for c in r["characters"]:
            n = c["name"]
            if n not in by_name:
                by_name[n] = {"name": n, "owner": c.get("owner", ""), "level": c.get("level", 0), "keys": {},
                              "biggest": (0.0, None), "summary_seed": {}}
                order.append(n)
            tgt = by_name[n]
            tgt["level"] = max(tgt["level"], c.get("level", 0))
            src = {int(k): float(v) for k, v in c["keys"].items()}
            for k, v in src.items():
                if k in (rb.BIGGEST, rb.BIGGEST_SRC, rb.BEST_SRC, rb.BEST_DMG):
                    continue
                if rb.TOP_SRC <= k < rb.TOP_HITS + rb.TOP_COUNT:
                    continue
                tgt["keys"][k] = tgt["keys"].get(k, 0.0) + v
            big = src.get(rb.BIGGEST, 0.0)
            if big > tgt["biggest"][0]:
                src_name = (c.get("summary") or {}).get("BiggestHitSource")
                if not src_name and rb.BIGGEST_SRC in src:
                    src_name = name_of(src[rb.BIGGEST_SRC])
                tgt["biggest"] = (big, src_name)
                tgt["keys"][rb.BIGGEST] = big
                if rb.BIGGEST_SRC in src:
                    tgt["keys"][rb.BIGGEST_SRC] = src[rb.BIGGEST_SRC]

    out = []
    for n in order:
        c = by_name[n]
        # the top list and best skill come from the merged per-ability damage, exactly as the mod re-derives them
        lo = BUCKET_BASE + B_DEALT * BUCKET_SPAN
        hlo = BUCKET_HITS_BASE + B_DEALT * BUCKET_SPAN
        per = {}
        for k, v in c["keys"].items():
            if lo <= k < lo + BUCKET_SPAN and v > 0:
                per[k - lo] = [v, c["keys"].get(hlo + (k - lo), 0.0)]
        rb.rederive_top(c["keys"], per)
        summary = {}
        for i, sn in enumerate(rb.BATTLE_STATS):
            if c["keys"].get(i):
                summary[sn] = round(c["keys"][i], 1)
        for label, k in (("Kills", rb.KILLS), ("Hits", rb.HITS), ("Crits", rb.CRITS)):
            if c["keys"].get(k):
                summary[label] = c["keys"][k]
        if c["biggest"][0]:
            summary["BiggestHit"] = round(c["biggest"][0], 1)
            if c["biggest"][1]:
                summary["BiggestHitSource"] = c["biggest"][1]
        if rb.BEST_DMG in c["keys"]:
            summary["BestSkillDamage"] = round(c["keys"][rb.BEST_DMG], 1)
            summary["BestSkill"] = name_of(c["keys"][rb.BEST_SRC])
        out.append({"name": c["name"], "owner": c["owner"], "level": c["level"], "summary": summary,
                    "keys": {str(k): round(v, 4) for k, v in sorted(c["keys"].items())}})
    return out


def better_quest(values):
    """Prefer a real quest name over an unfilled template token or a bare level."""
    named = [v for v in values if v and "[" not in v and not v.lower().startswith("level ")]
    if named:
        return named[-1]
    plain = [v for v in values if v and "[" not in v]
    return plain[-1] if plain else (values[-1] if values else None)


def main():
    paths = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(paths) < 2:
        sys.exit(__doc__)
    write = "--write" in sys.argv
    quest_id = sys.argv[sys.argv.index("--quest-id") + 1] if "--quest-id" in sys.argv else None
    runs = sorted((load(p) for p in paths), key=lambda r: r["started"])
    out_path = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else runs[0]["_path"]

    fights = []
    for r in runs:
        for f in r.get("fights") or []:
            fights.append({"n": len(fights) + 1, "victory": bool(f.get("victory")), "turns": int(f.get("turns") or 0)})
    merged = {
        "version": 1,
        "started": runs[0]["started"],
        "ended": runs[-1]["ended"],
        "mode": next((r.get("mode") for r in runs if r.get("mode")), None),
        "difficulty": next((r.get("difficulty") for r in runs if r.get("difficulty")), None),
        "quest": better_quest([r.get("quest") for r in runs]),
        "act": max((r.get("act") or 0) for r in runs),
        "quest_id": quest_id or next((r.get("quest_id") for r in runs if r.get("quest_id")), None),
        "closed": True if "--closed" in sys.argv else (False if "--open" in sys.argv else all(bool(r.get("closed")) for r in runs)),
        "battles": sum(r["battles"] for r in runs),
        "wins": sum(r.get("wins", 0) for r in runs),
        "fights": fights,
        "characters": merge_chars(runs),
        "merged_from": [os.path.basename(r["_path"]) for r in runs],
    }
    print("merging %d files into %s" % (len(runs), os.path.basename(out_path)))
    for r in runs:
        print("   %-32s %s  %d battle(s)" % (os.path.basename(r["_path"]), r["started"], r["battles"]))
    print("result: %s -> %s, %d battles, %d won, quest %r%s" % (
        merged["started"], merged["ended"], merged["battles"], merged["wins"], merged["quest"],
        ", quest id set" if merged["quest_id"] else ""))
    for c in merged["characters"]:
        print("   %-18s dealt %12s  kills %3d  biggest %9s %-20s best %s" % (
            c["name"], format(int(c["summary"].get("DamageDealt", 0)), ","), int(c["summary"].get("Kills", 0)),
            format(int(c["summary"].get("BiggestHit", 0)), ","), c["summary"].get("BiggestHitSource", ""),
            c["summary"].get("BestSkill", "")))
    if not write:
        print("\nnothing written; pass --write to merge")
        return
    backup = os.path.join(os.path.dirname(os.path.dirname(out_path)), "merged-backup-" + datetime.datetime.now().strftime("%Y%m%d-%H%M%S"))
    os.makedirs(backup, exist_ok=True)
    for r in runs:
        shutil.copy2(r["_path"], os.path.join(backup, os.path.basename(r["_path"])))
    json.dump(merged, open(out_path, "w", encoding="utf-8"), indent=1)
    for r in runs:
        if os.path.abspath(r["_path"]) != os.path.abspath(out_path):
            os.remove(r["_path"])
    print("\nwritten %s (%d bytes); the originals are in %s" % (os.path.basename(out_path), os.path.getsize(out_path), backup))


if __name__ == "__main__":
    main()
