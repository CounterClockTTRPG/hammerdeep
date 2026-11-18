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
2. **Threats and combat** — health and wounds; hostile creatures in the caverns (woken when
   breached) and wild beasts on the surface; seasonal raids by an original goblinoid people;
   squads with equipment and standing orders; doors that lock, traps, a drawbridge; an
   alert that sends civilians to a safe burrow; corpses, burial, grief.
3. **Animals and husbandry** — wild animals, livestock from the wagon and caravans, pastures,
   breeding, butchery (meat, leather, bone), a tanner and a leatherworker, pets, war beasts.
4. **Trade caravans** — seasonal caravans from the mountainhome and the outside, a trade
   depot, barter by value, requests and imports, merchants' moods.
5. **Legendary artifacts** — strange moods: a dwarf seizes a workshop and demands materials;
   success makes a named artifact (generated name and description, a story in the log),
   failure ends in madness.
6. **Religion, rituals and magic** — clan deities, shrines and temples, a need to pray, rites
   at the deep places, runecraft: rune-carved items with effects.
7. **Polish** — generated portraits and banners via ../assetgen (needs the Replicate token:
   costs credits, so it waits for a go-ahead), music, balance, Windows export.

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
- **Migrants.** Each new season: 1-3, plus one per 4000 wealth (up to 12), plus 2 while the
  hold is under ten; they walk in from the map edge.
