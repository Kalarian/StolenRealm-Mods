"""Turn a run_build_test.py results folder into report.md: one table per scenario, per-skill usage, and plain flags.

Usage:  python analyze_battles.py <tests/<timestamp> folder>

Reads runs.json, every <scenario>-s<seed>/result.txt (driver log: enemies, turns, party alive, rotation trace) and the
BattleStats battle-*.json beside it (every credit and cast). Writes report.md in the folder and prints it.
"""
import os, sys, json, glob, re, collections, math

# Grading thresholds (see ~/.claude/skills/sr-iterate/references/rubric.md). First calibration 2026-09-17.
RUBRIC = {
    "turn_target": {"pack": 2, "elite": 3, "boss": 4},   # player turns to win for full kill-speed points
    "turn_penalty": 6,                                    # per extra player turn (of 30)
    "closeness": [(60, 0), (40, 5), (20, 12), (0, 20)],   # lowest health % >= threshold -> penalty (of 40)
    "efficiency_ok": (0.9, 1.3), "overkill_step": (0.2, 3),
    "idle_penalty": 5,
    "sustain": [(0.25, 15), (0.5, 10), (1.0, 5)],         # damage taken / max health -> points
    "letters": [(92, "S"), (80, "A"), (65, "B"), (50, "C"), (35, "D"), (0, "F")],
}


def letter(score):
    for th, l in RUBRIC["letters"]:
        if score >= th:
            return l
    return "F"


def grade_fight(scenario, victory, timeout, player_turns, lowest, taken_ratio, efficiency, idle):
    """Returns (score, parts) from the rubric; every input may be None when not measured."""
    parts = {}
    if timeout or not victory:
        parts["survival"] = 0
        parts["speed"] = 0
    else:
        pen = 0
        if lowest is not None:
            pen = 20
            for th, p_ in RUBRIC["closeness"]:
                if lowest >= th:
                    pen = p_
                    break
        parts["survival"] = 40 - pen
        tgt = RUBRIC["turn_target"].get(scenario, 3)
        parts["speed"] = max(0, 30 - RUBRIC["turn_penalty"] * max(0, (player_turns or tgt) - tgt))
    eff = 15
    if efficiency is not None:
        lo, hi = RUBRIC["efficiency_ok"]
        if efficiency > hi:
            step, pts = RUBRIC["overkill_step"]
            eff -= int((efficiency - hi) / step + 0.999) * pts
        elif efficiency < lo and victory:
            eff -= 3
    eff -= RUBRIC["idle_penalty"] * (idle or 0)
    parts["efficiency"] = max(0, eff)
    sus = 0
    if taken_ratio is not None:
        for th, p_ in RUBRIC["sustain"]:
            if taken_ratio <= th:
                sus = p_
                break
    parts["sustain"] = sus
    return sum(parts.values()), parts


def load_run(d):
    out = {"dir": d, "result": None, "json": None}
    r = os.path.join(d, "result.txt")
    if os.path.exists(r):
        out["result"] = open(r, encoding="utf-8", errors="replace").read()
    js = sorted(glob.glob(os.path.join(d, "battle-*.json")))
    if js:
        out["json"] = json.load(open(js[-1], encoding="utf-8"))
    return out


def team_summary(members):
    """One summary for the party: sums for damage/healing/casts, per-member skill totals kept with the member's name."""
    if len(members) == 1:
        return members[0]
    s = {"Name": " + ".join(m["Name"] for m in members), "Skills": [], "DamageByElement": {}, "TopCasts": []}
    for k in ("DamageTotal", "DamageTaken", "HealingDone", "HealingReceived", "Overkill", "HexesMoved"):
        s[k] = sum(m.get(k, 0) for m in members)
    for k in ("Hits", "Crits", "Kills", "Casts", "FreeActions", "ActionsUsed", "ManaSpent"):
        s[k] = sum(int(m.get(k, 0)) for m in members)
    big = max(members, key=lambda m: m.get("BiggestHit", 0))
    s["BiggestHit"], s["BiggestHitAction"] = big.get("BiggestHit", 0), f"{big.get('BiggestHitAction', '')} ({big['Name']})"
    for m in members:
        for sk in m.get("Skills", []):
            s["Skills"].append(dict(sk, Name=f"{sk['Name']} ({m['Name']})"))
        for k, v in (m.get("DamageByElement") or {}).items():
            s["DamageByElement"][k] = s["DamageByElement"].get(k, 0) + v
    return s


def n(x):
    return f"{x:,.0f}" if isinstance(x, (int, float)) else str(x)


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    folder = os.path.abspath(sys.argv[1])
    runs = json.load(open(os.path.join(folder, "runs.json"), encoding="utf-8"))
    rot = {}
    rp = os.path.join(folder, "rotation.json")
    if os.path.exists(rp):
        rot = json.load(open(rp, encoding="utf-8"))
    if rot.get("characters"):
        # party rotation: one plan per member; every name in the report carries the member so the rows line up with the per-member Skills
        rot_skills = [f"{e['skill']} ({name})" for name, plan in rot["characters"].items() for e in plan.get("priority", [])]
    else:
        rot_skills = [e["skill"] for e in rot.get("priority", [])]
    L = [f"# Build test report: {runs.get('build')}", "",
         f"Spec `{runs.get('spec')}` · difficulty index {runs.get('difficulty')} · scenarios {', '.join(runs.get('scenarios', []))} · seeds {runs.get('seeds')}", ""]
    flags = []
    per_scenario = collections.defaultdict(list)
    rows = []
    cast_totals = collections.Counter()
    for r in runs["runs"]:
        tag = f"{r['scenario']}-s{r['seed']}"
        run = load_run(os.path.join(folder, tag))
        res = run["result"] or ""
        js = run["json"]
        victory = turns = alive = None
        m = re.search(r"battle over after (\d+)s.*?turn (\d+), party alive (\d+)", res)
        if m:
            turns, alive = int(m.group(2)), int(m.group(3))
        enemies = ""
        m = re.search(r"SCENARIO .*?: level (\d+), (.*?), group (\w+), (\d+) points, (\d+) enemies: (.*)", res)
        if m:
            enemies = f"level {m.group(1)}, {m.group(2)}, {m.group(3)}, {m.group(5)} enemies: {m.group(6)}"
        summary = None
        if js:
            victory = js.get("Victory")
            turns = js.get("Turns", turns)
            party = runs.get("party") or ["TestBuild"]
            members = [s for s in js.get("Summary", []) if s.get("Name") in party] or (js.get("Summary") or [])[:1]
            summary = team_summary(members) if members else None
            for c in js.get("Casts", []):
                if c.get("Character") in party:
                    cast_totals[c.get("Action") + (f" ({c.get('Character')})" if len(party) > 1 else "")] += 1
        timeout = "TIMEOUT" in res
        not_run = "battle active" not in res  # harness failure (crash, no enemies, load problem): not the build's fault, excluded from the grade
        if victory is None and not timeout and alive is not None:
            victory = alive > 0
        pool = party_hp = None
        m = re.search(r"POOL enemy hp (\d+) over (\d+) enemies, party hp (\d+)", res)
        if m:
            pool, party_hp = int(m.group(1)), int(m.group(3))
        end_h = low_h = None
        m = re.search(r"HEALTH end (\d+)% lowest (\d+)%", res)
        if m:
            end_h, low_h = int(m.group(1)), int(m.group(2))
        player_turns = int(math.ceil(turns / 2.0)) if turns else None
        s0 = summary or {}
        efficiency = (s0.get("DamageTotal", 0) / pool) if pool else None
        taken_ratio = (s0.get("DamageTaken", 0) / party_hp) if party_hp else None
        idle = 0
        if js and summary and turns:
            party_names = set(runs.get("party") or ["TestBuild"])
            acted = {c.get("Turn") for c in js.get("Casts", []) if c.get("Character") in party_names and c.get("Kind") == "Action"}  # a party turn is idle only when no member spent an Action
            idle = sum(1 for pt in range(1, turns + 1, 2) if pt not in acted and pt < turns)
        score, parts = grade_fight(r["scenario"], victory, timeout, player_turns, low_h, taken_ratio, efficiency, idle)
        party_size = len(runs.get("party") or ["TestBuild"])
        if victory and alive is not None and alive < party_size:
            parts["survival"] = max(0, parts.get("survival", 0) - 15)  # a win with a dead member is not a clean win
            score = sum(parts.values())
        row = {"tag": tag, "scenario": r["scenario"], "victory": victory, "turns": turns, "player_turns": player_turns, "alive": alive, "timeout": timeout, "enemies": enemies,
               "seconds": r.get("seconds"), "s": summary, "pool": pool, "party_hp": party_hp, "end_health": end_h, "lowest_health": low_h,
               "efficiency": efficiency, "taken_ratio": taken_ratio, "idle": idle, "score": score, "parts": parts, "not_run": not_run}
        rows.append(row)
        per_scenario[r["scenario"]].append(row)
        if not_run:
            first_err = next((ln for ln in res.splitlines() if "EXCEPTION" in ln or "no enemies" in ln or "TIMEOUT" in ln), "no result")
            flags.append(f"{tag}: NOT RUN (harness failure, excluded from the grade): {first_err[:160]}")
        elif timeout:
            flags.append(f"{tag}: the battle never ended (driver timeout). The rotation is probably stuck or the character cannot reach the enemies.")
        elif victory is False or (alive is not None and alive == 0):
            flags.append(f"{tag}: LOST. Turns {turns}. Enemies: {enemies}")
        if summary:
            if summary.get("DamageTaken", 0) > 0 and summary.get("HealingReceived", 0) == 0 and summary.get("DamageTaken", 0) > 0.6 * max(1, summary.get("GameStats", {}).get("MaxHealth", 0) or 1e9):
                pass
            if turns and turns > 10 and victory:
                flags.append(f"{tag}: won but took {turns} turns; damage per turn is low for this scenario.")

    L.append("## Results")
    L.append("")
    L.append("| Fight | Result | Player turns | Damage dealt | per turn | Enemy pool | Efficiency | Damage taken | Lowest HP | End HP | Idle | Kills | Casts (free) | Mana | Biggest hit | Score |")
    L.append("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    for row in rows:
        s = row["s"] or {}
        result = "not run" if row["not_run"] else ("timeout" if row["timeout"] else ("WIN" if row["victory"] else ("LOSS" if row["victory"] is False else "?")))
        if row["alive"] == 0 and not row["timeout"]:
            result = "LOSS"
        big = f"{n(s.get('BiggestHit', 0))} ({s.get('BiggestHitAction', '')})" if s else ""
        dpt = (s.get("DamageTotal", 0) / row["turns"]) if s and row["turns"] else 0
        dpt = (s.get("DamageTotal", 0) / row["player_turns"]) if s and row["player_turns"] else 0
        effs = f"{row['efficiency']:.2f}" if row["efficiency"] is not None else ""
        lowp = f"{row['lowest_health']}%" if row["lowest_health"] is not None else ""
        endp = f"{row['end_health']}%" if row["end_health"] is not None else ""
        L.append(f"| {row['tag']} | {result} | {row['player_turns'] or '?'} | {n(s.get('DamageTotal', 0))} | {n(dpt)} | {n(row['pool']) if row['pool'] else ''} | {effs} | {n(s.get('DamageTaken', 0))} | {lowp} | {endp} | {row['idle']} | {s.get('Kills', '')} | {s.get('Casts', '')} ({s.get('FreeActions', '')}) | {s.get('ManaSpent', '')} | {big} | {row['score']} |")
    L.append("")
    for sc, rs in per_scenario.items():
        wins = sum(1 for x in rs if x["victory"] or (x["alive"] and not x["timeout"] and x["victory"] is None))
        L.append(f"- **{sc}**: {wins}/{len(rs)} won. " + " · ".join(f"seed {x['tag'].split('-s')[1]}: {x['enemies'][:160]}" for x in rs))
    L.append("")

    L.append("## Skill usage (all fights)")
    L.append("")
    L.append("| Skill | Casts | In rotation | Damage | Hits | Crits | Biggest |")
    L.append("|---|---|---|---|---|---|---|")
    dmg = collections.defaultdict(lambda: [0.0, 0, 0, 0.0])
    for row in rows:
        s = row["s"] or {}
        for sk in s.get("Skills", []):
            d = dmg[sk["Name"]]
            d[0] += sk.get("Damage", 0); d[1] += sk.get("Hits", 0); d[2] += sk.get("Crits", 0); d[3] = max(d[3], sk.get("Biggest", 0))
    # the game's action names differ from the tree names in case only ("Shield Of Light"): fold rotation names onto the cast names
    lower_casts = {k.lower(): k for k in list(cast_totals) + list(dmg)}
    rot_skills = [lower_casts.get(k.lower(), k) for k in rot_skills]
    names = set(cast_totals) | set(dmg) | set(rot_skills)
    for name in sorted(names, key=lambda k: (-cast_totals.get(k, 0), -dmg[k][0] if k in dmg else 0)):
        d = dmg.get(name)
        L.append(f"| {name} | {cast_totals.get(name, 0)} | {'yes' if name in rot_skills else ''} | {n(d[0]) if d else ''} | {d[1] if d else ''} | {d[2] if d else ''} | {n(d[3]) if d else ''} |")
    never = [k for k in rot_skills if cast_totals.get(k, 0) == 0]
    if never:
        flags.append("Rotation entries never cast in any fight: " + ", ".join(sorted(set(never))) + ". Either the condition never held, the target was never castable, or the name does not match an action the character has.")
    L.append("")

    L.append("## Damage by element")
    L.append("")
    el = collections.Counter()
    for row in rows:
        for k, v in ((row["s"] or {}).get("DamageByElement", {}) or {}).items():
            el[k] += v
    L.append(", ".join(f"{k} {n(v)}" for k, v in el.most_common()) or "(none)")
    L.append("")

    L.append("## Grade")
    L.append("")
    ran = [r_ for r_ in rows if not r_["not_run"]]
    scores = [r_["score"] for r_ in ran]
    mean = sum(scores) / len(scores) if scores else 0
    mixed = [sc for sc, rs in per_scenario.items() if len({bool(x["victory"]) and not x["timeout"] for x in rs if not x["not_run"]}) > 1]
    any_loss = any(not x["victory"] and not x["timeout"] for x in ran)
    any_timeout = any(x["timeout"] for x in ran)
    skipped = len(rows) - len(ran)
    g = letter(mean)
    order = "SABCDF"
    if mixed:
        g = order[min(len(order) - 1, order.index(g) + 1)]
    if any_loss and order.index(g) < order.index("B"):
        g = "B"
    if any_timeout and order.index(g) < order.index("C"):
        g = "C"
    L.append(f"**{g}** - mean fight score {mean:.0f}/100 over {len(ran)} fights" + (f" ({skipped} not run, see flags)" if skipped else "") + (f"; inconsistent across seeds in: {', '.join(mixed)} (one letter down)" if mixed else "") + ("; capped at B because of a loss" if any_loss else "") + ("; capped at C because of a timeout" if any_timeout else ""))
    L.append("")
    L.append("| Fight | Survival /40 | Kill speed /30 | Efficiency /15 | Sustain /15 | Score |")
    L.append("|---|---|---|---|---|---|")
    for r_ in rows:
        p_ = r_["parts"]
        L.append(f"| {r_['tag']} | {p_.get('survival', 0)} | {p_.get('speed', 0)} | {p_.get('efficiency', 0)} | {p_.get('sustain', 0)} | {r_['score']} |")
    for sc, rs in per_scenario.items():
        rs2 = [x for x in rs if not x["not_run"]]
        L.append(f"- {sc}: " + ", ".join(f"seed {x['tag'].split('-s')[1]} {x['score'] if not x['not_run'] else 'not run'}" for x in rs) + (f" (mean {sum(x['score'] for x in rs2) / len(rs2):.0f})" if rs2 else ""))
    L.append("")
    json.dump({"grade": g, "mean": mean, "fights": [{k: v for k, v in r_.items() if k not in ("s", "dir")} for r_ in rows]},
              open(os.path.join(folder, "scores.json"), "w", encoding="utf-8"), indent=1)

    L.append("## Flags")
    L.append("")
    L.extend(f"- {f}" for f in flags) if flags else L.append("- none")
    L.append("")

    L.append("## Rotation trace (first fight)")
    L.append("")
    first = rows[0] if rows else None
    if first:
        res = load_run(first["dir"] if "dir" in first else os.path.join(folder, first["tag"]))["result"] or ""
        tr = [ln[9:] for ln in res.splitlines() if ln.startswith("ROTATION ")]
        L.extend("    " + ln for ln in tr[:80])
        if len(tr) > 80:
            L.append(f"    ... {len(tr) - 80} more")
    text = "\n".join(L) + "\n"
    open(os.path.join(folder, "report.md"), "w", encoding="utf-8").write(text)
    sys.stdout.reconfigure(encoding="utf-8")
    print(text)


if __name__ == "__main__":
    main()
