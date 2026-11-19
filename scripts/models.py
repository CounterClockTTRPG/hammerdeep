#!/usr/bin/env python3
"""HAMMERDEEP -- the 3D models for the 3D view, made by assetgen's Blender agent.

    python3 scripts/models.py             fetch what is done, ask for what is missing
    python3 scripts/models.py --list      say what is missing, done, or under way; change nothing
    python3 scripts/models.py --fetch     only fetch finished models, ask for nothing
    python3 scripts/models.py --force NAME  ask for one again, even if it exists

The models are asked of the assetgen REST server (ASSETGEN_URL, default
http://192.168.86.29:8765), project `hammerdeep`, model `blender`: an agent writes a
Blender script, renders it, looks and revises, and hands back a low-poly glTF with named
parts and flat materials. A run takes minutes, so asking returns at once; run this again
later to fetch what has finished. Fetched models go to assets/models/<name>.glb and are
listed in assets/models/manifest.txt (name, asset id); they are committed, so the game
never needs the server -- and draws a plain block for any building without a model.

Sizes: one map cell is one metre and a level is one metre high; furniture fits a cell,
workshops a 3 x 3 square. The game fits each model to its footprint anyway.
"""
import json
import os
import sys
import urllib.request
from pathlib import Path

URL = os.environ.get("ASSETGEN_URL", "http://192.168.86.29:8765").rstrip("/")
PROJECT = os.environ.get("ASSETGEN_PROJECT", "hammerdeep")
ROOT = Path(__file__).resolve().parent.parent
DIR = ROOT / "assets" / "models"
MANIFEST = DIR / "manifest.txt"

# name (the building's id in Tables) -> what to model
MODELS = {
    # furniture: one cell, 1 x 1 m
    "table": "a sturdy dwarven stone table carved from one block of grey granite, thick square top on a squat pedestal, fits a 1 x 1 m cell, about 0.75 m tall",
    "chair": "a heavy dwarven stone chair with a high square back and bronze studs, fits a 1 x 1 m cell, seat at 0.45 m",
    "bed": "a low dwarven bed of oak planks with a straw mattress, a woollen blanket and a bolster, fits a 1 x 1 m cell (a short stout bed), about 0.5 m tall",
    "door": "a thick dwarven door of oak planks bound with bronze bands in a stone frame, 1 m wide, 2 m tall, 0.25 m deep, standing upright",
    "chest": "a dwarven storage chest of oak with bronze corners and a heavy hasp, fits a 1 x 1 m cell, about 0.6 m tall",
    "statue": "a stone statue of a bearded dwarf warrior leaning on an axe, on a square plinth, fits a 1 x 1 m cell, about 1.8 m tall",
    "shrine": "a small dwarven shrine: a carved stone altar with a bronze offering bowl and two candles, fits a 1 x 1 m cell, about 1 m tall",
    "well": "a dwarven well: a round stone wall with a wooden crank, rope and bucket, fits a 1 x 1 m cell, about 1.3 m tall",
    "coffin": "a dwarven stone coffin with a carved lid showing an axe, fits a 1 x 1 m cell, lying on the ground, about 0.5 m tall",
    "trap": "a stonefall trap: a flat stone pressure plate set in a floor tile with a mechanism housing, fits a 1 x 1 m cell, very low, about 0.15 m",
    "floodgate": "a dwarven floodgate: a heavy bronze sluice gate in a stone frame, 1 m wide, 1.5 m tall",
    "bridge": "a dwarven drawbridge section: a heavy oak deck bound with bronze, lying flat, 1 x 1 m, 0.2 m thick, with chain rings",
    # workshops: 3 x 3 cells, open so the work shows from above
    "carpenter": "a dwarven carpenter's workshop, an open-sided work area on a 3 x 3 m stone floor: a workbench with saws and a plane, a stack of oak logs, sawhorses, wood shavings; low walls at most 1 m high so it reads from above",
    "mason": "a dwarven mason's workshop, an open work area on a 3 x 3 m stone floor: a stone-cutting bench with chisels and mallets, rough boulders, a few cut blocks; low walls at most 1 m high",
    "craftsdwarf": "a dwarven craftsdwarf's workshop, an open work area on a 3 x 3 m stone floor: a crafting table with small tools, shelves of trinkets, bone and stone carvings; low walls at most 1 m high",
    "brewery": "a dwarven brewery, an open work area on a 3 x 3 m stone floor: a copper brewing vat over a small hearth, oak ale barrels, sacks of grain; low walls at most 1 m high",
    "kitchen": "a dwarven kitchen, an open work area on a 3 x 3 m stone floor: a stone hearth with a hanging cauldron, a chopping table, hanging sausages and pots; low walls at most 1 m high",
    "smelter": "a dwarven smelter, a 3 x 3 m stone floor with a squat stone furnace with a glowing mouth and chimney stub, a pile of ore and charcoal, bronze ingots",
    "forge": "a dwarven metalsmith's forge, a 3 x 3 m stone floor with a stone forge and glowing coals, bellows, a heavy iron anvil, a quench trough and hanging tongs",
    "furnace": "a dwarven wood furnace for charcoal, a 3 x 3 m stone floor with a domed clay kiln, stacked logs and baskets of charcoal",
    "butcher": "a dwarven butcher's shop, an open work area on a 3 x 3 m stone floor: a heavy chopping block with cleavers, a rack of hanging meat, a drain grate; low walls at most 1 m high",
    "tanner": "a dwarven tanner's shop, a 3 x 3 m stone floor with a stretched hide on a wooden frame, tanning vats, scraping tools",
    "leatherworks": "a dwarven leatherworks, an open work area on a 3 x 3 m stone floor: a workbench with leather pieces, awls and thread, a dummy wearing a leather jerkin",
    "depot": "a dwarven trade depot, a 3 x 3 m raised stone platform with a wooden awning on posts, crates, sacks and a weighing scale",
    "temple": "a small dwarven temple, a 3 x 3 m stone platform with carved pillars, a central altar with a bronze brazier and banners",
    "runeforge": "a dwarven runeforge, a 3 x 3 m stone floor with an anvil carved with glowing blue runes, a rune-etched stone forge and engraving tools",
    "msmelter": "a dwarven magma smelter, a 3 x 3 m stone floor with a heavy obsidian furnace fed by a glowing magma channel, ore and ingots",
    "mforge": "a dwarven magma forge, a 3 x 3 m stone floor with an obsidian forge glowing with magma, a great anvil and hammers",
}


def call(path, body=None, method=None, raw=False):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(URL + path, data=data, method=method or ("POST" if data else "GET"),
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read() if raw else json.loads(r.read())


def manifest():
    out = {}
    if MANIFEST.exists():
        for line in MANIFEST.read_text().splitlines():
            if line.strip() and not line.startswith("#"):
                name, ref = line.split()[:2]
                out[name] = ref
    return out


def write_manifest(m):
    lines = ["# Hammerdeep 3D models: name, assetgen asset id (project hammerdeep, Blender agent).",
             "# Written by scripts/models.py; the .glb beside each is that asset's current version."]
    lines += [f"{n} {m[n]}" for n in sorted(m)]
    MANIFEST.write_text("\n".join(lines) + "\n")


def remote():
    """The project's assets by name: the newest that has a model, else the newest at all."""
    out = {}
    for a in call(f"/api/projects/{PROJECT}/assets?kind=model3d").get("assets", []):
        name = a.get("name")
        if name not in MODELS:
            continue
        done = bool(a.get("current"))
        prev = out.get(name)
        if prev is None or (done and not prev[1]) or (done == prev[1] and a.get("created_at", "") > prev[2]):
            out[name] = (a["id"], done, a.get("created_at", ""))
    return out


def running():
    """Names with a generate job queued or running."""
    names = set()
    jobs = call("/api/jobs")
    for j in jobs if isinstance(jobs, list) else jobs.get("jobs", []):
        if j.get("project") == PROJECT and j.get("status") in ("queued", "running"):
            for n, prompt in MODELS.items():
                if prompt[:70] in (j.get("label") or ""):
                    names.add(n)
    return names


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
    tris = 4000 if "3 x 3" in MODELS[name] else 1500
    r = call(f"/api/projects/{PROJECT}/generate", {"kind": "model3d", "model": "blender", "name": name,
                                                   "prompt": MODELS[name], "params": {"triangles": tris}})
    job = r.get("job") or {}
    print(f"  asked  {name:13} job {job.get('id')} ({job.get('status')})")


def main(argv):
    mode = argv[0] if argv else ""
    if mode not in ("", "--list", "--fetch", "--force"):
        print(__doc__)
        return 2
    have = manifest()
    there = remote()
    busy = running()
    if mode == "--force":
        name = argv[1]
        if name not in MODELS:
            print(f"no model called {name}; known: {', '.join(MODELS)}")
            return 2
        ask(name)
        return 0
    for name in MODELS:
        local = (DIR / f"{name}.glb").exists() and name in have
        ref, done, _ = there.get(name, (None, False, ""))
        if mode == "--list":
            state = "have" if local else "done, to fetch" if done else "under way" if name in busy else "missing"
            print(f"  {state:15} {name}")
            continue
        if done and (not local or have.get(name) != ref):
            if fetch(name, ref):
                have[name] = ref
                print(f"  fetched {name:12} {ref}")
        elif not local and not done and name not in busy and mode != "--fetch":
            ask(name)
    if mode != "--list":
        write_manifest(have)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
