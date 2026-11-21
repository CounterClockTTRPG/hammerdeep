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
    # great projects: 3 x 3, grand but no taller than about 1.4 m once fitted (the game caps the height)
    "greatgate": "a dwarven Great Gate, a 3 x 3 m squat gatehouse of dressed stone blocks with a heavy bronze-bound double door in the middle, carved runes over the arch",
    "cistern": "a dwarven cistern, a 3 x 3 m vaulted stone basin full of still water, with a carved rim, a bronze pump and wooden buckets",
    "colossus": "a dwarven colossus, a 3 x 3 m stone plinth with a great statue of three bearded dwarf founders standing shoulder to shoulder, axes raised",
    "halloflegends": "a dwarven Hall of Legends, a 3 x 3 m stone floor with tall engraved stone slabs telling stories in carved pictures, a bronze brazier in the middle",
}

# Raised workshops: "<shop>-t2" and "<shop>-t3", the same workshop grander (the game shows the one for its tier).
_TIERS = {
    2: "raised to its second tier and grander: dressed stone instead of rough timber, bronze fittings and trim, "
       "more and finer tools, a tidier and larger version of the same work",
    3: "raised to its third and finest tier, magnificent: carved stone with gold trim and inlaid runes, "
       "hanging clan banners, the finest masterwork tools, the main work piece larger and ornate",
}
for _shop in ["carpenter", "mason", "craftsdwarf", "furnace", "smelter", "forge", "kitchen", "brewery", "butcher",
              "tanner", "leatherworks", "runeforge", "msmelter", "mforge"]:
    for _t, _how in _TIERS.items():
        MODELS[f"{_shop}-t{_t}"] = f"{MODELS[_shop]}; {_how}; still on a 3 x 3 m footprint and low enough to read from above"

# Creatures and the other peoples: "cr-<creature key>" and "race-<race key>", one posed figure each, standing
# on the ground at the origin and facing forward (Blender -Y, glTF +Z); the game turns them to where they go.
_POSE = "a single low-poly game figure in a neutral standing pose, flat colours, standing on the ground at the origin and facing forward (-Y)"
MODELS.update({
    "cr-goat": f"{_POSE}: a shaggy white mountain goat with curved horns and a short beard, about 0.9 m tall at the head",
    "cr-hen": f"{_POSE}: a plump cream-white hen with a red comb and wattle and yellow legs, about 0.45 m tall",
    "cr-fellbuck": f"{_POSE}: a fellbuck, a lean brown deer with a white tail and branching antlers, about 1.5 m tall at the antlers",
    "cr-badger": f"{_POSE}: a dire badger, a heavy low grey badger with a black-and-white striped face and big claws, about 0.7 m tall, 1.3 m long",
    "cr-crawler": f"{_POSE}: a cave crawler, a purple many-legged centipede-like cave creature with a raised segmented tail and pincers, about 0.5 m tall, 1.2 m long",
    "cr-stalker": f"{_POSE}: a deep stalker, a gaunt hunched dark-violet cave beast with long clawed arms, a shaggy mane and pale yellow eyes, about 2.1 m tall",
    "cr-raider": f"{_POSE}: a grimling raider, a wiry green goblin with big pointed ears, leather scraps and a crude copper axe, about 1.2 m tall",
    "cr-brute": f"{_POSE}: a grimling brute, a hulking green orc-like warrior with a loincloth, bone trinkets and a huge wooden club, about 2.1 m tall",
    "cr-trader": f"{_POSE}: a Mountainhome trader, a stout white-bearded dwarf in an ochre travelling coat and wide hat with a pack on his back, about 1.3 m tall",
    "cr-merchant": f"{_POSE}: a Lowland merchant, a tall human in a long blue coat with a satchel and a ledger, about 1.75 m tall",
    "race-delvling": f"{_POSE}: a Delvling, a small orange lizard-folk tunneller with huge yellow eyes, a long tail, a hooded tunic and a lantern, about 1.1 m tall",
    "race-rootfolk": f"{_POSE}: a Rootfolk, a small green-skinned gardener with a broad mushroom cap for a hat, a leafy smock and a watering can, about 1.0 m tall",
    "race-stonekin": f"{_POSE}: a Stonekin, a hulking half-giant of living grey stone with massive arms and a loincloth, about 2.2 m tall",
    # The great beasts of the legends (Tables: ashwyrm, palestalker, merething, gnashjaw, queen)
    "cr-ashwyrm": f"{_POSE}: the Ashwyrm, a great wingless fire drake with charcoal-black scales cracked with glowing orange, a horned head and smoking jaws, about 2.6 m tall, 5 m long",
    "cr-palestalker": f"{_POSE}: the Pale Stalker, a tall emaciated cave horror with bone-white skin, no eyes, very long fingers and a stooped back, about 2.5 m tall",
    "cr-merething": f"{_POSE}: the Thing in the Mere, a bloated swamp monster of green-black hide, weed and tentacles with a wide toothy mouth and small glowing eyes, about 2.2 m tall",
    "cr-gnashjaw": f"{_POSE}: Gnashjaw, a colossal armoured boar-beast with iron-grey bristles, four tusks and a scarred hide, about 2.2 m tall at the shoulder",
    "cr-gloomhound": f"{_POSE}: a gloomhound, a lean slate-blue cave hound with pale glowing eyes and a ridged back, about 0.9 m tall",
    "cr-emberling": f"{_POSE}: an emberling, a small fire imp of cracked black rock with glowing orange seams and ember eyes, about 0.8 m tall",
    "cr-warden": f"{_POSE}: the Warden, a towering ancient stone guardian of carved grey marble with runes on its chest and massive stone fists, about 2.6 m tall",
    "cr-queen": f"{_POSE}: the Many-Legged Queen, a giant purple centipede queen with a raised armoured front, a crown of spines, many legs and a swollen egg-sac, about 2 m tall, 4 m long",
})

# Small items, lying where they were dropped: "it-<kind>". One sits in a quarter of a cell (about 0.4 m across);
# the game tints those made of a material (bars, blocks, gems) by it. Furniture, tools and shields reuse the
# building models and the dwarves' held things.
_ITEM = "a single small low-poly game prop lying on the ground at the origin, flat colours, no base or plinth"
MODELS.update({
    "it-boulder": f"{_ITEM}: a rough grey boulder of raw stone, irregular and chipped, about 0.35 m across",
    "it-log": f"{_ITEM}: a felled tree log lying on its side, bark and a pale cut end with rings, 0.5 m long, 0.15 m thick",
    "it-block": f"{_ITEM}: a neatly cut square stone building block with chisel marks, 0.3 m cube, light grey",
    "it-bar": f"{_ITEM}: a small stack of three trapezoid metal ingots, 0.3 m long, light grey metal",
    "it-roughgem": f"{_ITEM}: a rough uncut gemstone in a lump of rock, crystal points poking out, 0.15 m",
    "it-cutgem": f"{_ITEM}: a single brilliant-cut gemstone with facets, 0.1 m, lying on its side, pale",
    "it-seed": f"{_ITEM}: a small tied cloth sack of seeds, a few seeds spilling out, 0.2 m tall",
    "it-plant": f"{_ITEM}: a bundle of harvested plants, stalks with leaves and a few berries tied with twine, 0.35 m long",
    "it-meal": f"{_ITEM}: a wooden bowl of hearty stew with a chunk of bread beside it, 0.25 m across",
    "it-drink": f"{_ITEM}: a small oak keg of dwarven ale with bronze hoops and a tap, lying on a cradle, 0.35 m long",
    "it-charcoal": f"{_ITEM}: a small heap of black charcoal lumps, 0.3 m across",
    "it-armor": f"{_ITEM}: a dwarven metal breastplate lying on its back, broad and riveted, 0.4 m",
    "it-helm": f"{_ITEM}: a dwarven metal helmet with a nose guard and cheek plates, lying on its side, 0.25 m",
    "it-barrel": f"{_ITEM}: an upright oak barrel with iron hoops, 0.4 m tall",
    "it-craft": f"{_ITEM}: a small carved stone figurine of a seated dwarf beside a little goblet, 0.2 m tall",
    "it-mechanism": f"{_ITEM}: a dwarven clockwork mechanism of interlocking bronze gears and a lever on a small frame, 0.25 m",
    "it-carcass": f"{_ITEM}: the carcass of a hunted goat lying on its side, legs bound, 0.6 m long",
    "it-meat": f"{_ITEM}: a joint of raw red meat with a white bone end, 0.25 m long",
    "it-hide": f"{_ITEM}: a folded raw animal hide, shaggy brown fur on one side, 0.4 m",
    "it-leather": f"{_ITEM}: a rolled sheet of tanned brown leather tied with a cord, 0.35 m long",
    "it-bone": f"{_ITEM}: a small pile of animal bones, a skull and long bones, 0.35 m across",
})


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
    tris = 4000 if "3 x 3" in MODELS[name] else 2500 if name.startswith(("cr-", "race-")) else 500 if name.startswith("it-") else 1500
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
