"""Create lightweight game instances so several headless test runs can execute at once.

Usage:  python make_instances.py [N]      (default 3; re-running refreshes the copied files)

Each instance is C:\\Claude\\General\\stolen-realm\\instances\\iK with:
  - directory junctions to the real "Stolen Realm_Data" (5 GB, read-only assets) and MonoBleedingEdge
  - copies of the launcher files (Stolen Realm.exe, UnityPlayer.dll, UnityCrashHandler64.exe, winhttp.dll,
    doorstop_config.ini, .doorstop_version) and of BepInEx\\core, patchers, plugins, config (~50 MB)
  - its own BepInEx\\LogOutput.log, TestDriver\\ and BattleStats\\ output, and a saves\\ folder the driver is pointed at
    with -srsaves (FileSystem.persistentDataPath redirect), so the real save folder is never opened by a test.
Junctions need no admin rights. The real game folder is only read.
"""
import os, sys, shutil, subprocess

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\Stolen Realm"
ROOT = r"C:\Claude\General\stolen-realm"
INST = os.path.join(ROOT, "instances")
FILES = ["Stolen Realm.exe", "UnityPlayer.dll", "UnityCrashHandler64.exe", "winhttp.dll", "doorstop_config.ini", ".doorstop_version"]
JUNCTIONS = ["Stolen Realm_Data", "MonoBleedingEdge", "Stolen Realm_BurstDebugInformation_DoNotShip"]
BEPINEX_DIRS = ["core", "patchers", "plugins", "config"]


def junction(link, target):
    if os.path.islink(link) or os.path.isdir(link):
        return
    r = subprocess.run(["cmd", "/c", "mklink", "/J", link, target], capture_output=True, text=True)
    if r.returncode != 0:
        raise SystemExit("junction failed: " + r.stdout + r.stderr)


def make(k):
    d = os.path.join(INST, "i%d" % k)
    os.makedirs(d, exist_ok=True)
    for j in JUNCTIONS:
        src = os.path.join(GAME, j)
        if os.path.isdir(src):
            junction(os.path.join(d, j), src)
    for f in FILES:
        src = os.path.join(GAME, f)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(d, f))
    bd = os.path.join(d, "BepInEx")
    os.makedirs(bd, exist_ok=True)
    for sub in BEPINEX_DIRS:
        src, dst = os.path.join(GAME, "BepInEx", sub), os.path.join(bd, sub)
        if os.path.isdir(src):
            if os.path.isdir(dst):
                shutil.rmtree(dst)
            shutil.copytree(src, dst)
    for sub in ("TestDriver", "BattleStats"):
        os.makedirs(os.path.join(bd, sub), exist_ok=True)
    os.makedirs(os.path.join(d, "saves"), exist_ok=True)
    return d


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 3
    os.makedirs(INST, exist_ok=True)
    for k in range(1, n + 1):
        d = make(k)
        print("instance", d)


if __name__ == "__main__":
    main()
