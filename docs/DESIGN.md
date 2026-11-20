# Hammerdeep — design

## Scope (as agreed)

| Question | Decision |
|---|---|
| Engine | Godot 4.7 .NET, C#; tooling as in `../shadowrun` (play.sh, --check, --shots, --agent, assetgen) |
| View | Top-down 2D, one z-level at a time; open air shows the levels below, dimmed |
| Control | Indirect: designations, zones, buildings, work orders, labors; dwarves pick their own tasks |
| Military | Squads with standing orders (station, patrol, attack a target), plus traps, doors, drawbridges |
| First version systems | Dig/haul/build, needs and moods, crafting chains, threats and combat |
| Extras in scope | Animals and husbandry, trade caravans, legendary artifacts, religion/rituals/magic |
| Arc | Open sandbox; lose when the fortress collapses (everyone dead) |
| Setting | Classic expedition: clan dwarves found a daughter-hold. Original names and lore |
| Race | Dwarves only for now (a race is meant to become data, so others can be added) |
| Variety | Four clans with trait leanings and inherited grudges, plus personalities |
| Tone | Dark comedy: grim situations, dry log text |
| Scale | Large: 192 x 192 x 50 cells, up to ~220 dwarves |
| Time | Real time, pausable, 1x / 3x / 10x; orders can be given while paused |
| Saves | Ironman, three slots; autosave each season and on leaving |

## Milestones

1. **Foundation** (done) — worldgen with z-levels, pathing, digging (rooms, stairs, channels,
   ramps), felling, gathering, hauling to stockpiles, workshops and work orders, the crafting
   chains (wood, stone, ore→bars→tools, plants→food and ale, gems), farms, beds/tables/chairs,
   needs (food, drink, sleep), thoughts, moods, stress and tantrums, skills, labors, clans and
   feuds, friendships, migrants by wealth, the fortress falling; title, HUD, info panels,
   overlays, ironman slots; self-check, walkthrough, agent control.
2. **Threats and combat** (done) — health and wounds;
   cave crawlers and deep stalkers in the caverns (woken when breached, more each season),
   fellbuck and dire badgers on the surface (badgers turn on whoever comes close); grimling
   raids from the first autumn, sized by wealth and year, brutes that batter doors down;
   squads that arm and armour themselves from the stores and stand, defend or charge;
   doors that keep beasts out and lock against everyone; stonefall traps; the alarm that
   sends civilians to the meeting hall; corpses that upset passers-by until hauled to a
   refuse pile, coffins and burial (the unburied are grieved, and after a season haunt the
   hold), drawbridges that drop what stands on them into a gap or seal a floor.
3. **Animals and husbandry** (done) — goats and hens from the wagon; grazers need grass
   (pastures keep them on it, Deepfrost snow gives thin grazing); hens lay eggs; breeding in
   Thaw and Highsun (herds capped at 14); taming wild fellbuck and badgers (a failed try can
   turn a badger on its tamer); training badgers as war beasts that guard the squad's station;
   slaughter, and carcasses from slaughter or the hunt butchered into meat, hide and bone;
   tanner and leatherworks for leather armour and caps; bone carvings; petting. Livestock is
   driven into the meeting hall on the alarm; raiders kill it when no dwarf is in sight.
   Caravans will bring more animals.
4. **Trade caravans** (done) — a trade depot; Lowland merchants each Highsun (food, seed,
   wood, leather, live goats and hens) and Mountainhome dwarves each Leaffall (iron, bronze,
   coal, arms and armour, ale, dimcap seed, mechanisms) walk in from the edge, trade three
   days at the depot and leave. Barter by value: they pay x1.5 for the kinds they want and
   x0.9 for the rest, and mark their own goods up 35%, falling to 5% with standing. Standing
   rises with generous deals and gifts (and falls when they find no depot); it scales the
   caravans. Still to come: requests for next year, and raids on caravans.
5. **Legendary artifacts** (done) — a couple of times a year (daily chance 1% + 0.5% per 20
   dwarves, from the second season, one mood at a time, once per dwarf, never a soldier) a
   dwarf is seized: fey, secretive, possessed or macabre. They claim the nearest workshop of
   their best craft (carpenter, mason, craftsdwarf, forge, leatherworks) and demand its
   material plus one or two common things (wood, stone, bone, leather) and often one rare
   (a gem, or something that shines); the first demand for each pauses the game. Given it all,
   two days of work make a named artifact (x20 value) with a description woven from the
   hold's own history; the maker becomes legendary (skill 15) and everyone admires it.
   Denied eight days: melancholy (no more work), berserk (attacks the others until put down),
   or stark raving mad (wanders off into the wild, never seen again). Artifact furniture
   placed stays itself. W: the chronicle -- artifacts, great events, the fallen.
6. **Religion, rituals and magic** (done) — each clan's god (Ankra the Anvil, Velda of the
   Green Dark, Durn the Unbroken, Sith the Glimmering); dwarves need to pray (every 3500
   ticks; unhappy past 7500; pious twice as often, faithless never) at a shrine to their god
   or a temple, where anyone may pray and prayers comfort more. Each Thaw a temple holds the
   Feast of the Founding; temple prayers may calm a restless spirit. Reaching cavern depth
   teaches the old runes: the runeforge carves striking (weapon x1.4), warding (armour +12%,
   helm +8%, shield blocks 40%) or delving (mining x1.6) with a gem. At depth a shrine may be
   raised to That Which Sleeps Below: its worshippers hear sweet whispers, now and then wake
   possessed, and it doubles the speed of rune work.
7. **Polish** (mostly done) — synthesised music that follows the hold (title, hall, deep,
   battle); a Windows export (scripts/export.sh, checked by running the self-check on the
   exported exe) with its own icon; asking caravans for goods next year (up to three per
   people, brought twice over); leather armour made before caps; an agent playtest of every
   screen. Still waiting: generated portraits and banners via ../assetgen (needs a go-ahead:
   it spends Replicate credits).

8. **Water and magma** (done) — liquid depth 0..7 in every cell (four or more cannot be
   walked); ponds and cavern pools lie in basins and the magma sea at the bottom, all still
   until disturbed. Breached, liquid falls through open air and spreads to anything lower;
   still bodies are levelled as a whole. Water meeting magma becomes obsidian. Dwarves and
   beasts in deep water drown (6 a pass) and in magma burn (25), struggling out if they can;
   magma burns wood, plants, flesh and leather. Water leaves mud, and mud grows crops.
   Floodgates (built shut), wells over a shaft with water below, magma smelter and forge
   (no fuel) beside magma. Puddles under the sky dry up.
9. **Other races** (done) — a race is data (`Races`): body, health, pace, appetite, work
   rates, god, quirks, names, look; each new people has one clan. Delvlings (Copperclaw):
   small, quick, x1.3 mining, sleep anywhere, pocket loose gems, bars and figurines (and the
   others grow suspicious); Glimmermaw of the Hoard. Rootfolk (Hollyhock): x1.5 farming,
   x1.35 brewing, x0.6 mining; miserable after two days without the sky; Velda. Stonekin
   (Graniteborn): twice the health, a fist like a hammer, x1.5 mining bare-handed, x1.5
   hauling, slow, eat double; Durn. Founders are dwarves; each migrant wave has a 35% chance
   of bringing a band of a people living near (Stonekin come rarely, alone).
10. **The world** (done) — a 56 x 36 world from a seed (`WorldMap`): terrain from an
   elevation field with a mountain spine (lakes, marsh, plains, forest, hills, mountains,
   peaks), rivers, and sites -- the Mountainhome, dwarven holds, Lowland towns, grimling
   warrens, a Delvling burrow, Rootfolk gardens, a Stonekin crag -- with ~220 years of
   history (foundings, raids, sackings and ruins, heroes, beasts, lost artifacts, trade
   roads). Founding a fortress starts on the world screen: pick a hills or mountains tile;
   its surroundings set the local map (steepness, woods, ponds, ore) and its neighbours the
   hold's fortunes: raid pressure from warrens within reach, Lowland caravans only with a
   town near, migrant bands only from peoples near. Saves keep the seed and the tile. O shows
   the world and its legends in play.

11. **Tutorial and views** (done) — a guided first fortress (`Tutorial`): sixteen goals from
   looking around to a squad, each checked against the fortress and passed as soon as it is
   really done, in an order that keeps the hold alive (wood, a farm and ale before rooms);
   the tool a goal needs glows in the toolbar; offered when founding, on by default for a
   first fortress. Level buttons beside the level's name. An isometric view (F2, remembered):
   the map node tilted 45 degrees and halved in height, raised walls with shaded faces drawn
   back to front, upright figures and trees; every tool works the same in both views.
   Undo (Ctrl+Z, or the toolbar): takes back the last designation or clearing, zone, or
   building not yet built, up to 50 steps in a session; a marked cell's info has "Cancel it".

12. **Painted art and a fuller isometric view** (done) — 61 generated sprites (`assets/art`,
   through ../assetgen's `hammerdeep` project): a figure per clan, the creatures, the
   furniture, and every workshop twice (isometric, and from above). Optional: Painted art in
   the settings switches back to the original drawn look. The isometric view draws back to
   front (walls, markers, buildings, items and figures by diagonal), so walls hide what is
   behind them; drops show their far sides; ramps and stairs rise; clicking a wall's top picks it.

13. **A 3D view** (done) — the view button (F2) goes round top-down, isometric, 3D. `View3D`
   draws the same state as `MapView`, which stays the controller (camera centre, zoom, level,
   cursor, box, ghost, selection): a cell is a metre, everything above the level in view cut
   away, three levels below seen through open air. Terrain meshed per 16x16 chunk in vertex
   colours from the materials (walls, floors, ramps, stairs, trees, shrubs, water and magma),
   rebuilt from `World.Changed`; buildings are low-poly glTF models from assetgen's Blender
   agent (`assets/models`, `scripts/models.py`, read at run time with `GltfDocument`), fitted
   to their footprint, see-through while planned, a lettered block where there is no model;
   figures are the painted sprites standing up and turned to the camera; items small blocks in
   their material. Zones, designations, the drag box (with its soil/grass marks), the ghost and
   selection drawn over it; picking by ray (a wall's top on the level in view, else the floor).
   [ and ] turn the camera by 45 degrees; the wheel zooms, arrows and middle-drag pan as before.

14. **Modelled dwarves** — in the 3D view, dwarves are put together from parts on assetgen's
   humanoid skeleton template (`assets/figures`, `scripts/figures.py`): a stocky dwarf base
   body; cloth (tunic, sleeves, trousers, boots, belt, cloak) in the clan's colour; leather
   or plate where they wear armour, tinted by its metal; one of three beards; a pick, axe,
   hammer, sword or shield in the sockets. Clips play from what they do -- walk, run, idle,
   dig, chop, hammer (building and crafting), strike, eat, pray, sleep, pick up (hauling,
   farming), tantrum, death -- retargeted to the body (positions scaled by hip height) and
   blended; they face the way they walk or what they work on. Other peoples, and every
   dwarf when there is no body, stay sprites.

## The simulation

- **Cells.** Each cell has a shape (Empty, Floor, Wall, StairUp/Down/UD, Ramp, Tree, Water,
  Magma), a material, flags (Hidden, Built, Shrub, Grass, Outside) and a designation. A cell's
  floor is the top of the cell below it.
- **Movement.** 8-way on a level (no corner cutting), stairs between matching stair cells,
  ramps to the level above beside a wall. Step 5 ticks, diagonal 7, stairs 8.
- **World.** Soil 2-5 deep over sandstone/limestone, then granite/slate/marble, basalt and
  obsidian near the bottom, magma pockets at z 1-2. Ores in veins by depth (coal, malachite,
  cassiterite, hematite shallower; argentite and gold deeper), gems richer with depth. A
  cavern layer ~20 levels down with stalkwood, glowmoss and pools, hidden until breached.
- **Jobs.** A dwarf without a task looks after thirst, hunger and sleep first, then ranks the
  job kinds (build 30, craft 26, farm 26, mine 20, chop 16, gather 10, haul 6) plus 3 per skill
  level and 6 for a clan craft, and takes the nearest job of the best kind. Mining needs a pick,
  felling an axe.
- **Needs.** Hungry at 2600 ticks (starving 7000, dead 14000); thirsty at 1500 (parched 4000,
  dead 9000); tired at 2000 awake ticks, sleep recovers 6 a tick. Urgent needs interrupt work
  at most every 500 ticks.
- **Moods.** Thoughts carry a value and a lifetime (good meal +6, ale +5, own bed +6, water -5,
  floor -4, death seen -30, friend lost -45...). Mood is their sum; a negative mood adds to
  stress each 100 ticks, a positive one drains it. Stress over 2500 can break into a tantrum.
  Cheerful and gloomy dwarves weigh thoughts differently.
- **Skills.** Level = sqrt(xp / 40), capped at 15; speed = 1 + 12% a level (x1.2 hard-working,
  x0.8 lazy). Quality of crafted goods rolls from skill (artistic +2): ordinary to masterful.
- **Combat.** Melee every 30 ticks. To hit: 60% + 5% per point of skill difference (15-92%).
  Dwarf blow 9 x weapon (war hammer 1.9, axe 1.5, pick 1.3, bare 0.7; iron x1.25, bronze
  x1.15; +8% a quality level) x (1 + 4% per Fighting level). Armour stops 35% (breastplate)
  and 10% (helm), shields block a quarter of blows. Health 100; regain 1 per 30 ticks, per 6
  asleep. Soldiers on duty drill (+1 xp every 40 ticks). Raids: 30% + wealth/20000 + 10% a
  year (at most 85%) each season from the first autumn; 2-4 + wealth/4000 + 2 a year strong,
  a brute in every four; they leave after two and a half days.
- **Migrants.** Each new season: 1-3, plus one per 4000 wealth (up to 12), plus 2 while the
  hold is under ten; they walk in from the map edge.
