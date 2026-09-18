"""Build the cross-session ledger of every simulation ever run for a build.

Usage:  python ledger.py <build folder>                       e.g. python tools/ledger.py build4
        python ledger.py --check <candidate.json> <build folder>   exit 1 + the matching rows if that configuration was already run

Scans <build>/tests/*/runs.json + scores.json, reads the spec each run used (runs.json "spec"), and describes each run as
a diff against the build's current main spec: skills added/removed, attribute rule, loadout items and enchants, fortune
slots, rotation entries. Writes tests/ledger.md (for reading) and tests/ledger.json (for checking that a candidate is not a
repeat: compare a candidate's "signature" with the ones listed). Run this before choosing variants.
"""
import os, sys, json, glob, hashlib, statistics


def load(p):
    return json.load(open(p, encoding="utf-8"))


def signature(spec):
    """Everything that changes what the game plays, in a stable form. A party spec signs each member."""
    if spec.get("members"):
        parts = [signature(dict(m, level=m.get("level", spec.get("level", 30))))[1] for m in spec["members"]]
        sig = {"members": [(m.get("member_name"), m.get("formation")) for m in spec["members"]], "parts": parts}
        return hashlib.sha1(json.dumps(sig, sort_keys=True, default=str).encode()).hexdigest()[:12], sig
    sig = {
        "creation": spec.get("attributes", {}).get("creation"),
        "per_level": spec.get("attributes", {}).get("per_level"),
        "skills": [(e.get("skill"), e.get("tree")) for e in spec.get("skills", [])],
        "loadout": {k: (v if not isinstance(v, dict) else (v.get("item"), tuple(v.get("mods", [])))) for k, v in spec.get("test_loadout", {}).items() if k != "consumables"},
        "fortunes": spec.get("fortunes", {}).get("slots"),
        "rotation": [(e.get("skill"), e.get("target"), tuple(e.get("when", []))) for e in spec.get("rotation", {}).get("priority", [])],
    }
    return hashlib.sha1(json.dumps(sig, sort_keys=True, default=str).encode()).hexdigest()[:12], sig


def diff(cur, other):
    out = []
    if cur.get("members") or other.get("members"):
        ca = {m.get("member_name"): m for m in cur.get("members", [])}; cb = {m.get("member_name"): m for m in other.get("members", [])}
        for name in sorted(set(ca) | set(cb)):
            if name not in ca or name not in cb:
                out.append(f"member {name} only on one side")
            else:
                d = diff(ca[name], cb[name])
                if d != ["(same as the current spec)"]:
                    out.append(f"{name}: " + "; ".join(d))
        return out or ["(same as the current spec)"]
    a, b = signature(cur)[1], signature(other)[1]
    if a["creation"] != b["creation"]:
        out.append(f"creation {b['creation']}")
    if a["per_level"] != b["per_level"]:
        out.append("per-level " + "; ".join(", ".join(f"{k} {v}" for k, v in blk.items()) for blk in (b["per_level"] or [])))
    sa, sb = set(a["skills"]), set(b["skills"])
    if sa != sb:
        out.append("skills −" + ", ".join(s for s, _ in sorted(sa - sb)) + " +" + ", ".join(s for s, _ in sorted(sb - sa)))
    elif a["skills"] != b["skills"]:
        out.append("skill order changed")
    for slot in sorted(set(a["loadout"]) | set(b["loadout"])):
        if a["loadout"].get(slot) != b["loadout"].get(slot):
            v = b["loadout"].get(slot)
            out.append(f"{slot}: {v[0]} {list(v[1])}" if isinstance(v, tuple) else f"{slot}: {v}")
    if a["fortunes"] != b["fortunes"]:
        out.append("fortunes " + ", ".join(b["fortunes"] or []))
    if a["rotation"] != b["rotation"]:
        out.append("rotation " + " > ".join(f"{s}@{t}" for s, t, _ in b["rotation"]))
    return out or ["(same as the current spec)"]


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    if sys.argv[1] == "--check" and len(sys.argv) >= 4:
        # ledger.py --check <candidate spec> <build folder>: has this exact configuration been simulated before?
        cand = signature(load(sys.argv[2]))[0]
        led = load(os.path.join(os.path.abspath(sys.argv[3]), "tests", "ledger.json"))
        hits = [r for r in led["runs"] if r["signature"] == cand]
        if hits:
            print("ALREADY RUN:", "; ".join(f"{h['folder']} (difficulty {h['difficulty']}, {h['wins']}/{h['fights']} wins, mean {h['mean']})" for h in hits))
            sys.exit(1)
        print("new configuration", cand)
        sys.exit(0)
    bd = os.path.abspath(sys.argv[1])
    main_spec = next((p for p in glob.glob(os.path.join(bd, "Build*-*.json"))), None)
    if not main_spec:
        sys.exit("no BuildN-Name.json in " + bd)
    cur = load(main_spec)
    cur_sig = signature(cur)[0]
    rows = []
    for rd in sorted(glob.glob(os.path.join(bd, "tests", "*"))):
        rj, sj = os.path.join(rd, "runs.json"), os.path.join(rd, "scores.json")
        if not (os.path.isdir(rd) and os.path.exists(rj)):
            continue
        runs = load(rj)
        sp = runs.get("spec")
        snap = os.path.join(rd, "spec-used.json")
        stale = False
        if os.path.exists(snap):
            spec = load(snap)
        elif sp and os.path.exists(sp):
            spec = load(sp)
            stale = os.path.getmtime(sp) > os.path.getmtime(rj)  # the spec file was rewritten after the run: its history is not this file
        else:
            spec = None
        sig = ("stale:" if stale else "") + (signature(spec)[0] if spec else "?")
        fights = load(sj)["fights"] if os.path.exists(sj) else []
        ran = [f for f in fights if not f.get("not_run")]
        wins = sum(1 for f in ran if f.get("victory"))
        mean = statistics.mean(f["score"] for f in ran) if ran else None
        lows = [f.get("lowest_health") for f in ran if f.get("lowest_health") is not None]
        rows.append({
            "folder": os.path.basename(rd), "spec": os.path.basename(sp) if sp else None, "signature": sig, "difficulty": runs.get("difficulty"),
            "seeds": runs.get("seeds"), "scenarios": runs.get("scenarios"), "fights": len(ran), "wins": wins, "mean": round(mean, 1) if mean is not None else None,
            "lowest_health": min(lows) if lows else None, "diff_vs_current": (["spec rewritten after this run; only the numbers are reliable"] if stale else diff(cur, spec)) if spec else ["spec file missing"], "is_current": sig == cur_sig,
        })
    out_dir = os.path.join(bd, "tests")
    os.makedirs(out_dir, exist_ok=True)
    json.dump({"build": os.path.basename(main_spec), "current_signature": cur_sig, "runs": rows}, open(os.path.join(out_dir, "ledger.json"), "w", encoding="utf-8"), indent=1)
    L = [f"# Ledger: every simulation of {os.path.basename(main_spec)} (current spec signature {cur_sig})", "",
         "| Folder | Difficulty | Fights | Wins | Mean | Lowest HP | Signature | Difference from the current spec |", "|---|---|---|---|---|---|---|---|"]
    for r in rows:
        L.append(f"| {r['folder']} | {r['difficulty']} | {r['fights']} | {r['wins']} | {r['mean']} | {r['lowest_health']} | {r['signature']}{' (current)' if r['is_current'] else ''} | {'; '.join(r['diff_vs_current'])[:300]} |")
    L.append("")
    L.append("A candidate whose signature matches a row above has already been run: read that row instead of running it again. "
             "Runs at a different difficulty or with different seeds are not comparable to the current ones.")
    open(os.path.join(out_dir, "ledger.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")
    sys.stdout.reconfigure(encoding="utf-8")
    print("\n".join(L[:4] + L[4:4 + min(len(rows), 40)]))
    print(f"\nwrote {out_dir}\\ledger.md and ledger.json ({len(rows)} runs)")


if __name__ == "__main__":
    main()
