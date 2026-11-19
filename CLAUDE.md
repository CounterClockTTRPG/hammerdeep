# Hammerdeep

A dwarven fortress-management game in Godot 4.7 (.NET / C#), in the genre of
the classic colony sims (original names, lore and look). Seven dwarves embark
on a generated mountainside; you designate digging, zones, buildings and work
orders, and the dwarves choose their own tasks by skill, labor and need.
Top-down 2D, one z-level at a time, open air showing the levels below. Real
time with pause and 1x/3x/10x; ironman saves in three slots. Same machine
setup as `../shadowrun`: Windows Godot and `dotnet.exe` driven from WSL.
Design, scope and milestones: `docs/DESIGN.md`; every number: `src/Sim/Tables.cs`.

## Commands

    ./play.sh                   play
    ./play.sh --build           compile only (dotnet, ~5 s)
    ./play.sh --check           headless self-check -- run after every change
    ./play.sh --check --only X  just the sections whose name contains X (Year plays a whole year with the overseer;
                                --only LongRun plays three, a line a season -- the balance view, not run by default)
    ./play.sh --shots [X]       windowed walkthrough, writes shots/*.png -- look at them
    ./play.sh --agent           remote control for an agent playtester; drive it with
                                scripts/agent.sh "state; key d; drag 10 10 20 20" ('help' lists
                                commands; 'quit' closes it). Own save file (user://agent_fort.sav).
                                Game time only passes while a command runs ('freeze off' to change).
                                Debug: 'caravan low|mountain', 'raid N', 'mood [NAME]'. Founding goes through
                                the world screen: 'press Found a fortress; press Embark here; press Strike the earth'.
    bash scripts/export.sh [DIR]  Windows build (exe + pack + .NET data + assets) to DIR (default
                                C:/temp/Hammerdeep), then the self-check against the exported exe.
    python3 scripts/make_icon.py icon   redraw icon.png / icon.ico (needs Pillow).
    python3 scripts/models.py   3D models via assetgen's Blender agent (REST, ASSETGEN_URL, default
                                http://192.168.86.29:8765, project hammerdeep): fetches finished ones,
                                asks for missing ones (minutes each; run again later); --list, --force NAME
    python3 scripts/figures.py  the 3D dwarves' parts (body, outfits, clips, tools), same server and habits
    bash scripts/assets.sh      generate missing sprites via ../assetgen (project `hammerdeep`, Replicate
                                credits: ask first); --list, --force NAME, --recut (re-cut from the library, free)

Machine paths default in `scripts/common.sh`; override in `.env` (see `.env.example`).
Settings live in `user://hammerdeep.json`, the fortresses in `user://fort1.sav` .. `fort3.sav`.
`--check` and `--shots` use a scratch profile and never touch them. Never kill Godot by image
name (`taskkill /IM`): other projects' games may be running -- use the agent's `quit`.

## Layout

- `src/Sim` -- the rules, no Godot types. `Fortress` (+ `.Work`) is the whole game as plain data
  (a save is it as gzipped JSON, `Save`): dwarves, items, buildings, zones, crops, the log, and
  `Step()`, one tick. `World` -- the W x H x D cell arrays (shape, material, flags, designation),
  walkability and the step graph (`Neighbors`: 8-way, stairs, ramps), connected components,
  fog of war. `Pathing` -- A* and nearest-of-many over that graph with reused buffers.
  `Fortress.Combat` -- creatures (AI, door bashing), blows and wounds, traps, spawns and raids,
  dwarves under threat (flee; shelter on the alarm), squads and their orders.
  `Fortress.Rites` -- burial, grief and restless spirits; drawbridges. `Fortress.Animals` --
  livestock (grazing, eggs, breeding), taming, war beasts, slaughter and butchery yields.
  `Fortress.Trade` -- the trading peoples, caravans and their merchants, the barter.
  `Fortress.Moods` -- strange moods, artifacts and their descriptions, madness, admiration.
  `Fortress.Faith` -- the gods, prayer, shrines and the temple, the feast, runes and the Sleeper.
  `Races` -- the peoples as data; `Fortress.Peoples` -- their quirks (sky, light fingers), neighbours.
  `Fortress.Fluids` -- water and magma: flow, levelling, obsidian, drowning and burning, wells, gates.
  Regions (`World.Comp`) are merged in place when cells open (`World.Opened`); only closing a
  cell (a wall, a flood, a lock) forces a full rebuild -- the self-check's Regions section keeps
  the two in agreement.
  `Entities` -- Item, Dwarf, Task, Building, Order, Zone, Crop, Creature, Squad. `Tables` --
  materials, items, buildings, recipes, creatures, thoughts, traits, clans, names, all the numbers. `Overseer` -- a plain-rules
  autoplayer used by the self-check's Year and the walkthrough.
- `src/World/WorldMap.cs` -- the world: terrain, sites, history, embark land and neighbours (all
  from one seed). `src/UI/WorldScreen.cs` -- choosing the embark, and the in-game world panel.
- `src/World/WorldGen.cs` -- the embark map from a seed (and the `EmbarkLand` of its tile): heightmap with ramps, soil and rock
  layers, ore veins, gems, the cavern layer, magma, ponds, woods; the embark site.
- `src/Sim/Tutorial.cs` -- the guided first fortress: its goals and how each is checked.
- `src/View/MapView.cs` -- the level in view (and the isometric option: the node is tilted, so map
  coordinates always go through `GetGlobalTransformWithCanvas`, never `GetCanvasTransform`): terrain painted per 32x32 chunk as pixel art
  (repainted from `World.Changed`), open air showing lower levels; zones, designations,
  buildings, items and dwarves drawn over it each frame.
- `src/View/View3D.cs` -- the 3D view (F2 cycles top-down, isometric, 3D): chunked vertex-colour
  terrain, buildings from `assets/models/*.glb` (glTF read at run time; `-colonly` collider
  meshes dropped), sprite billboards for figures, an overlay for zones and the cursor; `MapView`
  keeps the state and asks it for picking (`CellAt`), panning and screen positions when `Is3D`.
- `src/View/Figure3D.cs` -- modelled dwarves for the 3D view: `Kit` loads `assets/figures` (a base body,
  outfit pieces, clips, held things -- all on assetgen's humanoid skeleton template), `Figure3D` puts one
  together (pieces moved onto the body's skeleton by bone name, held things in the socket_* nodes, clips
  retargeted to the body). `View3D.Animate` dresses it from the dwarf's gear and picks the clip from the task.
- `src/UI` -- `FortressScreen` (clock, camera, tools, HUD; `Panels.cs`: info panel and the
  overlays: dwarves, labors, military, animals, trade, chronicle, stocks, help, fallen), `TitleScreen`, `UITheme` (palette, fonts,
  widget builders, `Anchor` -- always anchor a control *after* adding it).
- `src/Core` -- `Root` (screen flow, CLI flags), `Profile` (settings, slots), `Sfx` (synthesised),
  `Music` (composed as it plays: drone, lute, bells, anvils, drums; moods Title/Hall/Deep/Battle;
  `--shots music` writes 30 s of each to shots/music-*.wav).
- `src/Dev` -- `ShotRunner` (the walkthrough), `AgentRunner` (remote control).
- `tests/SmokeTest.cs` -- the self-check: rules on tiny hand-built maps, the generator over a
  real seed, a save round trip, and a whole year with the overseer plus a balance report.
- `assets/fonts` -- Cinzel, Barlow, Inconsolata (OFL, licences beside them), read as bytes.
- `assets/art` -- the generated sprites (figures per clan, creatures, furniture, workshops iso and
  top-down) and their `manifest.txt`; `Assets.Sprite` returns null when one is missing or the player
  turned Painted art off, and every caller then draws its own. `scripts/fit_sprite.py` cuts them out.
  The terrain is drawn by `GroundLayer` (nearest filtering) behind `MapView` (smooth, for the sprites).

## Conventions

- Ticks: 1000 per day, 12 days a season, 4 seasons a year; 20 ticks/s at 1x.
- A cell's floor is the top of the cell below: digging a wall leaves Floor, channelling leaves
  Empty (and a ramp below). z grows upward; 0 is the bottom of the world.
- Every claim a task takes (designation, item reservation, stockpile cell, order, builder,
  seat, crop) is given back by `Fortress.Release` however the task ends.
- `Task` is the sim's job type: alias `System.Threading.Tasks.Task` (`STask`) in async code.
