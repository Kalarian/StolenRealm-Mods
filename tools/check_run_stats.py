"""Check BattleStats' run page against the per-battle pages of a batched test run.

Usage:  python tools/check_run_stats.py <tests/<stamp> folder written by run_build_test.py>

Every fight folder holds result.txt with the driver's `STATS row N: label | v1 | v2...` lines (the post-battle page) and
`RUNSTATS row N: ...` lines (BattleStats' run page opened right after it). Fights of one launch never pass through town,
so the run page after fight k must equal the sum of the battle pages 1..k for every sum-type row, the max for Biggest
Hit, and the leader of the merged per-source totals for Best Skill. Fights are ordered by the instance that played them
(runs.json "instance" + order); fights of different instances are separate runs, and a lost fight is followed by the
driver's Retry, which resets the run (so the fight after a loss starts a new run of 1).
Exit code 1 and the first mismatches on failure; prints RUN STATS PASS otherwise.
"""
import os, re, sys, json, glob, collections

SUM_ROWS = None  # every row not listed below is treated as a plain sum
MAX_ROWS = {"Biggest Hit"}
SKIP_ROWS = {"Best Skill", "Damage Per Turn"}   # derived: checked separately / not additive
# Section headers hold the sum of their children AFTER each child was rounded up, and rounding up does not survive
# addition (the run page rounds the run total once, the battle pages round every battle), so a header is checked against
# its own children on the same page instead of against the sum of the battle pages. The game's healing rows are their
# own one-stat sections with hidden children, which is why the sections are listed instead of inferred from row order.
SECTIONS = {
    "Total Damage": ["Damage Dealt", "Damage Returned", "Summon Damage Dealt"],
    "Total Damage Taken": ["Damage Taken", "Summon Damage Taken"],
    "Total Damage Blocked": ["Blocked By Armor", "Blocked By Magic Armor", "Blocked By Resistance", "Blocked By Damage Reduction", "Blocked By Shields"],
    "Damage Breakdown": ["Direct Hits", "Over Time", "Ground Tiles", "Summons", "Thorns"],
    "Damage By Element": ["Physical", "Fire", "Cold", "Lightning", "Shadow", "Holy", "Untyped"],
}
HEADER_ROWS = set(SECTIONS)
TOL = 1.01  # ceil per battle vs ceil of the sum can differ by 1 per battle


def num(s):
    """First number in a cell ('12,345', '5 / 2 (40%)' -> [12345] / [5, 2])."""
    s = s.replace(",", "")
    parts = re.findall(r"-?\d+(?:\.\d+)?", s.split("(")[0])
    return [float(p) for p in parts]


def parse(path, prefix):
    rows = collections.OrderedDict(); cols = []
    for line in open(path, encoding="utf-8", errors="replace"):
        line = line.strip()
        m = re.match(re.escape(prefix) + r" columns: (.*)", line)
        if m: cols = [c.strip() for c in m.group(1).split("|")]
        m = re.match(re.escape(prefix) + r" row (\d+): (.*)", line)
        if not m: continue
        cells = [c.strip() for c in m.group(2).split("|")]
        rows[(int(m.group(1)), cells[0])] = cells[1:]
    return cols, rows


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    out = os.path.abspath(sys.argv[1])
    runs = json.load(open(os.path.join(out, "runs.json"), encoding="utf-8"))
    by_inst = collections.defaultdict(list)
    for r in runs["runs"]:
        by_inst[r.get("instance", 1)].append(r)
    errors = []; checked = 0
    runs_seen = collections.defaultdict(list)   # instance -> [{"fights": n, "wins": n, "damage": {name: value}}]
    for inst, fights in sorted(by_inst.items()):
        acc = {}   # (row index, label) -> per column list of sums / max
        k = 0      # fights in the current run: a lost fight is followed by the driver's Retry, which starts a new run (the mod resets on Retry)
        for r in fights:
            tag = f"{r['scenario']}-s{r['seed']}"
            res = os.path.join(out, tag, "result.txt")
            if not os.path.exists(res):
                errors.append(f"{tag}: no result.txt"); continue
            k += 1
            text = open(res, encoding="utf-8", errors="replace").read()
            lost = text.splitlines()[0].strip().startswith("LOSS") if text else False
            # the driver's -srresetruns flag ends the run after a fight, exactly as leaving town does
            forced_reset = "RUNSTATS: run reset" in text
            cols_b, battle = parse(res, "STATS")
            cols_r, run = parse(res, "RUNSTATS")
            if not battle or not run:
                errors.append(f"{tag}: missing STATS ({len(battle)} rows) or RUNSTATS ({len(run)} rows) lines"); continue
            if cols_b != cols_r:
                errors.append(f"{tag}: column names differ: {cols_b} vs {cols_r}")
            for key, cells in battle.items():
                label = key[1]
                if label in SKIP_ROWS: continue
                vals = [num(c) for c in cells]
                if key not in acc: acc[key] = [list(v) for v in vals]
                else:
                    for ci, v in enumerate(vals):
                        for vi, x in enumerate(v):
                            if vi >= len(acc[key][ci]): acc[key][ci].append(x); continue
                            acc[key][ci][vi] = max(acc[key][ci][vi], x) if label in MAX_ROWS else acc[key][ci][vi] + x
            # each section header must equal the sum of the rows shown under it on the same page
            by_label = {k[1]: v for k, v in run.items()}
            for header, kid_labels in SECTIONS.items():
                if header not in by_label: continue
                kids = [by_label[l] for l in kid_labels if l in by_label]
                if not kids: continue
                for ci in range(len(by_label[header])):
                    head = num(by_label[header][ci])
                    child_sum = sum(num(kc[ci])[0] for kc in kids if ci < len(kc) and num(kc[ci]))
                    if head and abs(head[0] - child_sum) > len(kids) + 1:
                        errors.append(f"{tag}: section '{header}' column {ci + 1}: header {head[0]:g} but its rows add up to {child_sum:g}")
                    checked += 1
            for key, cells in run.items():
                label = key[1]
                if label in SKIP_ROWS or label in HEADER_ROWS or key not in acc: continue
                vals = [num(c) for c in cells]
                for ci, v in enumerate(vals):
                    want = acc[key][ci]
                    for vi, x in enumerate(v):
                        if vi >= len(want): continue
                        w = want[vi]
                        if abs(x - w) > TOL * k:
                            errors.append(f"{tag} (fight {k} of instance {inst}): row '{label}' column {ci + 1}: run page {x:g}, sum of battles {w:g}")
                        checked += 1
            # Best Skill: the run's leader must be one of the skills seen, with damage >= its per-battle best
            bk = next((kk for kk in run if kk[1] == "Best Skill"), None)
            if bk is not None and k > 1:
                for ci, cell in enumerate(run[bk]):
                    if cell.strip() in ("-", "n/a", ""): continue
                    v = num(cell)
                    prev = num(battle[bk][ci]) if bk in battle else []
                    if v and prev and v[0] + 0.5 < prev[0]:
                        errors.append(f"{tag}: Best Skill column {ci + 1}: run leader {v[0]:g} is below this battle's best {prev[0]:g}")
            title = next((l.strip()[len("RUNSTATS title:"):].strip() for l in open(res, encoding="utf-8", errors="replace") if l.strip().startswith("RUNSTATS title:")), "")
            if title and f"{k} battle" not in title:
                errors.append(f"{tag}: run title '{title}' does not say {k} battle(s)")
            # remember this run's state so the saved file can be checked against it
            dmg_key = next((kk for kk in run if kk[1] == "Damage Dealt"), None)
            if dmg_key is not None:
                snap = {cols_r[ci]: num(cell)[0] for ci, cell in enumerate(run[dmg_key]) if ci < len(cols_r) and num(cell)}
                cur = runs_seen[inst][-1] if runs_seen[inst] and runs_seen[inst][-1]["open"] else None
                if cur is None:
                    cur = {"fights": 0, "wins": 0, "damage": {}, "open": True}
                    runs_seen[inst].append(cur)
                cur["fights"] = k; cur["damage"] = snap
                if not lost: cur["wins"] += 1
            if lost or forced_reset:
                if runs_seen[inst]: runs_seen[inst][-1]["open"] = False
                acc = {}; k = 0   # the next fight follows a Retry or a forced reset: fresh run
    # saved run files: one per run, with the same battle count and per-character damage as the last page of that run
    saved = 0
    for inst, seen in sorted(runs_seen.items()):
        folder = os.path.join(out, "runs", str(inst))
        files = sorted(glob.glob(os.path.join(folder, "run-*.json")))
        if not files:
            errors.append(f"instance {inst}: no saved run files in {folder}"); continue
        if len(files) != len(seen):
            errors.append(f"instance {inst}: {len(files)} saved run file(s) but {len(seen)} run(s) were played")
        for f, exp in zip(files, seen):
            try: rf = json.load(open(f, encoding="utf-8"))
            except Exception as e:
                errors.append(f"{os.path.basename(f)}: unreadable ({e})"); continue
            saved += 1
            if rf.get("battles") != exp["fights"]:
                errors.append(f"{os.path.basename(f)}: says {rf.get('battles')} battles, the run had {exp['fights']}")
            if rf.get("wins") != exp["wins"]:
                errors.append(f"{os.path.basename(f)}: says {rf.get('wins')} wins, the run had {exp['wins']}")
            if len(rf.get("fights") or []) != exp["fights"]:
                errors.append(f"{os.path.basename(f)}: {len(rf.get('fights') or [])} fight entries for {exp['fights']} battles")
            for c in rf.get("characters") or []:
                want = exp["damage"].get(c.get("name"))
                got = (c.get("summary") or {}).get("DamageDealt", 0.0)
                if want is None:
                    errors.append(f"{os.path.basename(f)}: character '{c.get('name')}' is not a column of the run page"); continue
                if abs(float(got) - want) > 1.01 * max(1, exp["fights"]):
                    errors.append(f"{os.path.basename(f)}: {c.get('name')} DamageDealt {got:g}, run page {want:g}")
                if not c.get("keys"):
                    errors.append(f"{os.path.basename(f)}: {c.get('name')} has no raw keys (the page could not be redrawn from it)")
    print(f"checked {checked} run-page values over {sum(len(f) for f in by_inst.values())} fights in {len(by_inst)} instance(s), and {saved} saved run file(s)")
    if errors:
        print("RUN STATS FAIL")
        for e in errors[:30]: print("  " + e)
        sys.exit(1)
    print("RUN STATS PASS")


if __name__ == "__main__":
    main()
