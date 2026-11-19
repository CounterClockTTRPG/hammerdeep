#!/usr/bin/env python3
"""HAMMERDEEP -- the parts of the 3D dwarves (assets/figures), from assetgen's Blender agent.

    python3 scripts/figures.py            fetch what is done, ask for what is missing
    python3 scripts/figures.py --list     say what is missing, done, or under way; change nothing
    python3 scripts/figures.py --fetch    only fetch, ask for nothing
    python3 scripts/figures.py --force NAME  ask for one again

Every part is on assetgen's humanoid skeleton template, so any piece fits the body and any
clip plays on it (see Kit in src/View/Figure3D.cs for the names the game looks for):
  body             the dwarf base body (role=base)
  <set>-<slot>     outfit pieces fitted to that body (cloth, leather, plate; beard-N)
  clip-<name>      animation clips, keyed on the project's humanoid rig
  held-<name>      things held in a socket, the grip at the origin
Some parts are borrowed as they are from another project (the template's clips, a sword,
a shield): BORROW names the asset there. The rest are asked of `hammerdeep`. Same server,
same habits as scripts/models.py: asking returns at once, run again later to fetch.
"""
import sys
from pathlib import Path

from models import PROJECT, call

ROOT = Path(__file__).resolve().parent.parent
DIR = ROOT / "assets" / "figures"
MANIFEST = DIR / "manifest.txt"

BODY = "dwarf-base-36aca7"   # the dwarf base body, made first (everything below is fitted to it)
RIG = "humanoid-rig-189046"  # the project's humanoid rig, which clips are keyed on

# Taken as they are from another project: name -> (project, asset name).
BORROW = {
    "clip-walk": ("blender-test", "humanoid walk"),
    "clip-idle": ("blender-test", "humanoid idle"),
    "clip-run": ("blender-test", "humanoid run"),
    "clip-strike": ("blender-test", "humanoid 1h slash"),
    "clip-sleep": ("blender-test", "humanoid lie down"),
    "clip-death": ("blender-test", "humanoid death"),
    "clip-pickup": ("blender-test", "humanoid pick up"),
    "held-sword": ("blender-test", "sword"),
    "held-shield": ("blender-test", "shield"),
}


def outfit(slot, set_, prompt):
    return {"kind": "outfit", "model": "blender-outfit", "params": {"base": BODY, "slot": slot, "set": set_}, "prompt": prompt}


def clip(name, prompt, frames=48, loop=True, weapon=None):
    p = {"rig": RIG, "clip": name, "frames": frames, "loop": loop}
    if weapon:
        p["weapon"] = weapon
    return {"kind": "animation", "model": "blender-anim", "params": p, "prompt": prompt}


def held(weapon, prompt):
    return {"kind": "model3d", "model": "blender", "params": {"weapon": weapon, "triangles": 600}, "prompt": prompt}


# Made for this project: name -> the request (name on the server is the same).
MAKE = {
    "body": None,  # made already: BODY
    # what everyone wears, tinted in the clan's colour in the game
    "cloth-torso": outfit("torso", "cloth", "a dwarven wool tunic, thick and plain, reaching the hips, with a laced collar"),
    "cloth-arms": outfit("arms", "cloth", "the rolled-up sleeves of a dwarven wool tunic, to the forearm"),
    "cloth-legs": outfit("legs", "cloth", "baggy dwarven wool trousers tucked into the boots"),
    "cloth-feet": outfit("feet", "cloth", "heavy dwarven leather boots with thick soles and turned-down cuffs"),
    "cloth-waist": outfit("waist", "cloth", "a broad dwarven leather belt with a big bronze buckle and a pouch"),
    "cloth-back": outfit("back", "cloth", "a short dwarven travelling cloak with a hood down on the shoulders"),
    # leather armour
    "leather-head": outfit("head", "leather", "a dwarven boiled-leather cap with a riveted rim and ear flaps"),
    "leather-torso": outfit("torso", "leather", "a dwarven boiled-leather jerkin with studded shoulder pads"),
    "leather-arms": outfit("arms", "leather", "dwarven boiled-leather bracers laced on the forearms"),
    # plate (the helm and breastplate were made first)
    "plate-head": None,
    "plate-torso": None,
    "plate-arms": outfit("arms", "plate", "dwarven plate vambraces and gauntlets, riveted, heavy"),
    # beards: on the head slot, worn under any helm
    "beard-1": outfit("head", "beard", "a long braided dwarven beard reaching the belt, with bronze beard rings, and a moustache; only the beard, no hair on top"),
    "beard-2": outfit("head", "beard", "a broad bushy dwarven beard to mid-chest, forked at the end, and thick eyebrows; only the beard"),
    "beard-3": outfit("head", "beard", "a short square-cut dwarven beard with a curled moustache and long sideburns; only the beard"),
    # work clips (the rest are borrowed)
    "clip-dig": None,
    "clip-chop": clip("chop", "felling a tree in front with a woodcutter's axe in both hands: swing it back over the right shoulder and chop sideways into the trunk at waist height, twist it free, and again", 36, True, "two_handed"),
    "clip-hammer": clip("hammer", "working at a bench or building: hammering with a hammer in the right hand on something at waist height in front, the left hand steadying it, short strong strokes", 36, True, "one_handed"),
    "clip-eat": clip("eat", "standing and eating: lifting food from the left hand to the mouth with the right, chewing, a swig from a tankard, contented", 60),
    "clip-pray": clip("pray", "kneeling in prayer: down on one knee, head bowed, hands clasped before the chest, a slow breath", 60),
    "clip-tantrum": clip("tantrum", "a dwarf's tantrum: stamping, shaking fists in the air, kicking at the ground, furious", 48),
    # tools held in the right hand
    "held-pick": held("two_handed", "a dwarven mining pick: a heavy iron pick head on a stout oak haft, 1 m long"),
    "held-axe": held("two_handed", "a dwarven woodcutter's axe: a broad iron blade on a long oak haft"),
    "held-hammer": held("one_handed", "a dwarven war hammer: a squared iron head with a spike on the back, on a bronze-bound oak haft"),
}
# made first, by hand: name -> asset id
MADE = {"body": BODY, "plate-head": "plate-head-7e62cf", "plate-torso": "plate-torso-6090b5", "clip-dig": "dig-f1c947"}


def manifest():
    out = {}
    if MANIFEST.exists():
        for line in MANIFEST.read_text().splitlines():
            if line.strip() and not line.startswith("#"):
                name, ref = line.split()[:2]
                out[name] = ref
    return out


def write_manifest(m):
    lines = ["# Hammerdeep 3D figure parts: name, assetgen asset id (see scripts/figures.py).",
             "# All on the humanoid skeleton template; the .glb beside each is that asset's current version."]
    lines += [f"{n} {m[n]}" for n in sorted(m)]
    MANIFEST.write_text("\n".join(lines) + "\n")


def assets(project):
    return call(f"/api/projects/{project}/assets").get("assets", [])


def newest(project, name):
    best = None
    for a in assets(project):
        if a.get("name") == name and (best is None or (bool(a.get("current")), a.get("created_at", "")) > (bool(best.get("current")), best.get("created_at", ""))):
            best = a
    return best


def busy():
    jobs = call("/api/jobs")
    out = set()
    for j in jobs if isinstance(jobs, list) else jobs.get("jobs", []):
        if j.get("project") == PROJECT and j.get("status") in ("queued", "running"):
            for n, req in MAKE.items():
                if req and req["prompt"][:60] in (j.get("label") or ""):
                    out.add(n)
    return out


def fetch(name, ref):
    a = call(f"/api/assets/{ref}")
    a = a.get("asset", a)
    cur = a.get("current")
    if not cur:
        return False
    DIR.mkdir(parents=True, exist_ok=True)
    (DIR / f"{name}.glb").write_bytes(call(f"/api/assets/{ref}/file/{cur}?download=1", raw=True))
    return True


def ask(name):
    req = dict(MAKE[name], name=name)
    r = call(f"/api/projects/{PROJECT}/generate", req)
    job = r.get("job") or {}
    print(f"  asked   {name:14} job {job.get('id')} ({job.get('status')}) {r.get('error') or ''}")


def main(argv):
    mode = argv[0] if argv else ""
    if mode == "--force":
        ask(argv[1])
        return 0
    have = manifest()
    running = busy() if mode != "--fetch" else set()
    ours = {a["name"]: a for a in assets(PROJECT) if a.get("current")}
    names = list(BORROW) + list(MAKE)
    for name in names:
        local = (DIR / f"{name}.glb").exists() and name in have
        if name in BORROW:
            a = newest(*BORROW[name])
            ref = a["id"] if a and a.get("current") else None
        elif name in MADE:
            ref = MADE[name]
        else:
            ref = ours[name]["id"] if name in ours else None
        if mode == "--list":
            print(f"  {'have' if local else 'done, to fetch' if ref else 'under way' if name in running else 'missing':15} {name}")
            continue
        if ref and (not local or have.get(name) != ref):
            if fetch(name, ref):
                have[name] = ref
                print(f"  fetched {name:14} {ref}")
        elif not ref and not local and name not in running and mode != "--fetch" and MAKE.get(name):
            ask(name)
    if mode != "--list":
        write_manifest(have)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
