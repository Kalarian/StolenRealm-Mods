"""Play a build through the standard test fights, in parallel game instances, and collect the numbers.
The real save folder is never opened by a test: every instance gets its own copy of the saves via -srsaves.

Usage:  python run_build_test.py <BuildN-Name.json> [--scenarios pack,elite,boss] [--seeds 11,22,33] [--difficulty 2]
                                 [--parallel 3] [--speed 4] [--mode headless|windowed] [--out <folder>] [--force]

Steps: refuse to run while a test instance is open -> check the spec -> make/refresh N instances (tools/make_instances.py:
junctions to the game data + copies of the loader and BepInEx) -> seed each instance's saves\\ from the real save folder and
write the TestBuild character + rotation.json there -> split the fights round-robin over the instances and launch them all
at once (-srtest -srfights ... -srsaves ...) -> collect each fight's driver lines + BattleStats JSON into
<out>/<scenario>-s<seed>/ -> fights a batch did not finish are rerun one launch each in instance 1 -> runs.json.
Real saves are hashed before and after and the run refuses to report success if they changed.
Default out folder: <spec folder>/tests/<timestamp>/.
"""
import os, sys, json, glob, shutil, hashlib, subprocess, time, datetime, re

ROOT = r"C:\Claude\General\stolen-realm"
SAVES = os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\Burst2Flame Entertainment\Stolen Realm")
INST = os.path.join(ROOT, "instances")


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def real_save_hashes():
    return {f: sha(os.path.join(SAVES, f)) for f in os.listdir(SAVES) if os.path.isfile(os.path.join(SAVES, f))}


def game_running():
    try:
        out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Stolen Realm.exe"], capture_output=True, text=True).stdout
        return "Stolen Realm.exe" in out
    except Exception:
        return False


def set_cfg(text, key, value):
    pat = re.compile(r"^(\s*%s\s*=\s*).*$" % re.escape(key), re.M)
    if pat.search(text):
        return pat.sub(lambda m: m.group(1) + value, text)
    return text + "\n%s = %s\n" % (key, value)


def json_stamp(name):
    m = re.match(r"battle-(\d{8})-(\d{6})\.json", os.path.basename(name))
    return datetime.datetime.strptime(m.group(1) + m.group(2), "%Y%m%d%H%M%S") if m else datetime.datetime.min


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    spec_path = os.path.abspath(sys.argv[1])
    spec = json.load(open(spec_path, encoding="utf-8"))
    args = sys.argv[2:]
    def opt(name, default):
        return args[args.index(name) + 1] if name in args else default
    scenarios = opt("--scenarios", "pack,elite,boss").split(",")
    seeds = [int(s) for s in opt("--seeds", "11,22,33").split(",")]
    difficulty = int(opt("--difficulty", "2"))
    mode = opt("--mode", "headless")
    speed = opt("--speed", "4")
    parallel = max(1, int(opt("--parallel", "3")))
    reset_runs = "--resetruns" in args   # tell BattleStats every fight is its own run (tests one run file per run)
    resume_runs = "--resumeruns" in args  # forget the run after each fight, so it has to be resumed from its file
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    out = opt("--out", os.path.join(os.path.dirname(spec_path), "tests", stamp))

    if game_running():
        sys.exit("Stolen Realm is running (a game or another test); wait for it to finish.")
    chk = subprocess.run([sys.executable, os.path.join(ROOT, "tools", "check_build_spec.py"), spec_path], capture_output=True, text=True)
    if chk.returncode != 0 and "--force" not in args:
        print("\n".join(ln for ln in chk.stdout.splitlines() if ln.startswith("ERROR")))
        sys.exit("the spec fails the checker (banned fortune, bad names or gates); fix it or pass --force")
    os.makedirs(out, exist_ok=True)
    shutil.copy2(spec_path, os.path.join(out, "spec-used.json"))  # the ledger reads this, so a later rewrite of the main spec cannot confuse history
    before_real = real_save_hashes()
    backup_real = os.path.join(INST, "real-saves-backup")
    shutil.rmtree(backup_real, ignore_errors=True)
    os.makedirs(backup_real, exist_ok=True)
    for f in before_real:
        shutil.copy2(os.path.join(SAVES, f), os.path.join(backup_real, f))

    members = [m["member_name"] for m in spec["members"]] if spec.get("members") else ["TestBuild"]
    wanted = [(sc, seed) for sc in scenarios for seed in seeds]
    n_inst = min(parallel, len(wanted))
    subprocess.run([sys.executable, os.path.join(ROOT, "tools", "make_instances.py"), str(n_inst)], check=True, capture_output=True)
    results = []

    def prepare(k):
        d = os.path.join(INST, "i%d" % k)
        sv = os.path.join(d, "saves")
        shutil.rmtree(sv, ignore_errors=True)
        os.makedirs(sv)
        for f in before_real:
            shutil.copy2(os.path.join(SAVES, f), os.path.join(sv, f))
        rot = os.path.join(d, "BepInEx", "TestDriver", "rotation.json")
        r = subprocess.run([sys.executable, os.path.join(ROOT, "tools", "make_build_character.py"), spec_path, "--name", "TestBuild", "--rotation", rot, "--saves", sv], capture_output=True, text=True)
        if r.returncode != 0:
            raise SystemExit("character writer failed: " + r.stderr.strip())
        if k == 1:
            print(r.stdout.strip().splitlines()[-2] if r.stdout.strip() else "")
            shutil.copy2(rot, os.path.join(out, "rotation.json"))
        cfg = os.path.join(d, "BepInEx", "config", "stolenrealm.battlestats.cfg")
        if os.path.exists(cfg):
            t = open(cfg, encoding="utf-8").read()
            open(cfg, "w", encoding="utf-8").write(set_cfg(set_cfg(t, "WriteJson", "true"), "Enabled", "true"))
        shutil.rmtree(os.path.join(d, "BepInEx", "TestDriver", "fights"), ignore_errors=True)
        os.makedirs(os.path.join(d, "BepInEx", "TestDriver", "fights"), exist_ok=True)
        for old in glob.glob(os.path.join(d, "BepInEx", "BattleStats", "battle-*.json")):
            os.remove(old)
        shutil.rmtree(os.path.join(d, "BepInEx", "BattleStats", "runs"), ignore_errors=True)   # the run files this launch writes
        res = os.path.join(d, "BepInEx", "TestDriver", "result.txt")
        if os.path.exists(res):
            os.remove(res)
        return d, sv, rot

    def launch(d, sv, rot, fights):
        cmd = [os.path.join(d, "Stolen Realm.exe"), "-srtest", "-srparty", str(len(members)), "-srchars", ",".join(members), "-srdifficulty", str(difficulty),
               "-srrotation", rot, "-srspeed", speed, "-srsaves", sv, "-srfights", ",".join("%s:%d" % w for w in fights)]
        if reset_runs:
            cmd.append("-srresetruns")
        if resume_runs:
            cmd.append("-srresumeruns")
        cmd += ["-screen-width", "1280", "-screen-height", "720", "-screen-fullscreen", "0"] if mode == "windowed" else ["-batchmode", "-nographics"]
        return subprocess.Popen(cmd, cwd=d, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def collect(d, sc, seed, took):
        tag = "%s-s%d" % (sc, seed)
        od = os.path.join(out, tag)
        os.makedirs(od, exist_ok=True)
        summary = {"scenario": sc, "seed": seed, "seconds": took, "result": "no result file", "json": None, "instance": os.path.basename(d)}
        f = os.path.join(d, "BepInEx", "TestDriver", "fights", "%s-s%d.txt" % (sc, seed))
        txt = open(f, encoding="utf-8", errors="replace").read() if os.path.exists(f) else None
        if txt is not None and "battle over" in txt:
            open(os.path.join(od, "result.txt"), "w", encoding="utf-8").write(txt)
            summary["result"] = txt.splitlines()[0] if txt else "empty"
            m = re.search(r"battle over after (\d+)s.*?turn (\d+), party alive (\d+)", txt)
            if m:
                summary.update({"battle_seconds": int(m.group(1)), "turns": int(m.group(2)), "alive": int(m.group(3))})
            m = re.search(r"SCENARIO .*", txt)
            if m:
                summary["enemies"] = m.group(0)
            closed = datetime.datetime.fromtimestamp(os.path.getmtime(f))
            cands = [j for j in glob.glob(os.path.join(d, "BepInEx", "BattleStats", "battle-*.json")) if json_stamp(j) <= closed + datetime.timedelta(seconds=2)]
            if cands:
                best = max(cands, key=json_stamp)
                shutil.copy2(best, os.path.join(od, os.path.basename(best)))
                summary["json"] = os.path.basename(best)
                os.remove(best)
            results.append(summary)
            print("%-10s %s  %ss  turns %s  alive %s  json %s  (%s)" % (tag, summary["result"], took, summary.get("turns", "?"), summary.get("alive", "?"), summary["json"], summary["instance"]))
            return True
        return False

    def collect_runs(d):
        """The run files BattleStats saved during this launch (one per run, rewritten after each fight)."""
        src = os.path.join(d, "BepInEx", "BattleStats", "runs")
        if not os.path.isdir(src):
            return
        od = os.path.join(out, "runs", os.path.basename(d))
        os.makedirs(od, exist_ok=True)
        for f in glob.glob(os.path.join(src, "run-*.json")):
            shutil.copy2(f, os.path.join(od, os.path.basename(f)))

    def save_log(d, name):
        try:
            txt = open(os.path.join(d, "BepInEx", "LogOutput.log"), encoding="utf-8", errors="replace").read()
            i = txt.rfind("SESSION START")
            open(os.path.join(out, name), "w", encoding="utf-8").write(txt[i:] if i >= 0 else txt[-200000:])
        except Exception:
            pass

    # ---- parallel batches
    groups = [wanted[i::n_inst] for i in range(n_inst)]
    procs = []
    t0 = time.time()
    for k, fights in enumerate(groups, 1):
        d, sv, rot = prepare(k)
        procs.append((d, fights, launch(d, sv, rot, fights)))
        time.sleep(8)  # stagger the launches; Steam and the loader are happier
    print("launched %d instance(s) for %d fights" % (len(procs), len(wanted)))
    limit = 300 + 150 * max(len(g) for g in groups)
    for d, fights, p in procs:
        try:
            p.wait(timeout=max(30, limit - (time.time() - t0)))
        except subprocess.TimeoutExpired:
            print(os.path.basename(d), "TIMEOUT; killing")
            p.kill()
    took = int(time.time() - t0)
    done = set()
    for d, fights, p in procs:
        save_log(d, "log-%s.txt" % os.path.basename(d))
        collect_runs(d)
        for sc, seed in fights:
            if collect(d, sc, seed, took):
                done.add((sc, seed))
    print("batch: %d of %d fights finished in one launch per instance (%ss)" % (len(done), len(wanted), took))

    # ---- rerun the stragglers one at a time in instance 1
    for sc, seed in wanted:
        if (sc, seed) in done:
            continue
        d, sv, rot = prepare(1)
        t1 = time.time()
        p = launch(d, sv, rot, [(sc, seed)])
        try:
            p.wait(timeout=720)
        except subprocess.TimeoutExpired:
            print("%s-s%d TIMEOUT; killing" % (sc, seed))
            p.kill()
        save_log(d, "log-%s-s%d.txt" % (sc, seed))
        if not collect(d, sc, seed, int(time.time() - t1)):
            res = os.path.join(d, "BepInEx", "TestDriver", "result.txt")
            txt = open(res, encoding="utf-8", errors="replace").read() if os.path.exists(res) else "no result file"
            od = os.path.join(out, "%s-s%d" % (sc, seed))
            os.makedirs(od, exist_ok=True)
            open(os.path.join(od, "result.txt"), "w", encoding="utf-8").write(txt)
            results.append({"scenario": sc, "seed": seed, "seconds": int(time.time() - t1), "result": txt.splitlines()[0] if txt else "empty", "json": None, "instance": "i1"})
            print("%s-s%d  %s (not run)" % (sc, seed, results[-1]["result"]))

    # Plugins load before the driver can redirect FileSystem.persistentDataPath, so a mod that reads or writes its pool
    # file in Awake (LevelSync, SharedProgress...) still touches the real folder for those few frames. Nothing the test
    # itself does should survive there, so anything that changed is put back exactly as it was before the run.
    after_real = real_save_hashes()
    changed = sorted(f for f in set(before_real) | set(after_real) if before_real.get(f) != after_real.get(f))
    if changed:
        restored, failed = [], []
        for f in changed:
            src, dst = os.path.join(backup_real, f), os.path.join(SAVES, f)
            try:
                if f in before_real:
                    shutil.copy2(src, dst)
                else:
                    os.remove(dst)          # the run created a file that was not there before
                restored.append(f)
            except Exception as e:
                failed.append("%s (%s)" % (f, e))
        again = real_save_hashes()
        if again == before_real:
            print("real saves: %s changed during the run and %s put back (verified byte for byte)" % (", ".join(changed), "was" if len(changed) == 1 else "were"))
        else:
            print("WARNING: the real save folder changed during the run and could not be fully restored:",
                  sorted(f for f in set(before_real) | set(again) if before_real.get(f) != again.get(f)), failed)
    else:
        print("real saves untouched (verified)")
    results.sort(key=lambda r: (scenarios.index(r["scenario"]), seeds.index(r["seed"])))
    json.dump({"spec": spec_path, "build": spec.get("name"), "party": members, "difficulty": difficulty, "scenarios": scenarios, "seeds": seeds, "parallel": n_inst, "runs": results},
              open(os.path.join(out, "runs.json"), "w", encoding="utf-8"), indent=1)
    print("results in", out)


if __name__ == "__main__":
    main()
