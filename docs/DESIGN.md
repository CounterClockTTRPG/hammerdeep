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
   [ and ] turn the camera by 45 degrees, a right-drag turns and tilts it (a right-click that does not drag still cancels); the wheel zooms, arrows and middle-drag pan as before.

14. **Modelled dwarves** — in the 3D view, dwarves are put together from parts on assetgen's
   humanoid skeleton template (`assets/figures`, `scripts/figures.py`): a stocky dwarf base
   body; cloth (tunic, sleeves, trousers, boots, belt, cloak) in the clan's colour; leather
   or plate where they wear armour, tinted by its metal; one of three beards; a pick, axe,
   hammer, sword or shield in the sockets. Clips play from what they do -- walk, run, idle,
   dig, chop, hammer (building and crafting), strike, eat, pray, sleep, pick up (hauling,
   farming), tantrum, death -- retargeted to the body (positions scaled by hip height) and
   blended; they face the way they walk or what they work on. The other peoples and the
   creatures are posed models (`cr-*`, `race-*` in `assets/models`) turned to where they go,
   with a bob as they walk; anything without a model stays a sprite.

15. **Great beasts** (`Fortress.Beasts`) — the five beasts the world's history names come to
   the hold: from the second year, at most one a year (12% a season, +5% a year, at most
   40%), never one the history says was slain, and never while another is about. Each comes
   where its kind would (the Pale Stalker and the Queen up from the caverns once they are
   found, the Thing in the Mere to the water nearest the hold, the rest over the edge) with
   its old tale quoted, and marches on the hold; it never leaves. Their tricks: the Ashwyrm
   breathes fire every 400 ticks on everyone within 2 cells of the dwarf it looks at (14-22,
   armour stops half as much) and burns the wood lying there; the Pale Stalker goes for
   sleepers anywhere it can reach, strikes them 2.5x, and is not seen (no one flees) until
   within 2 cells; the Thing in the Mere goes back to its water, strikes 1.6x at the water's
   edge, and while it lives every drink of water is foul (-6); Gnashjaw batters a door down
   in 40 ticks, not 160, and its first blow after three steps is a charge (2x); the
   Many-Legged Queen lays crawlers every 250 ticks, up to six. Slain, a beast leaves a
   masterwork trophy (the Ashwyrm's skull, Gnashjaw's tusks...), a deed in the hold's own
   legends (the World panel lists them first), and +16 for everyone in place of the dread.
   A hold with no world around it (an old save, a hand-built map) has no tales and no beasts.

16. **Weather** (`Fortress.Weather`) — each season's weather is chosen as it begins (the
   founding Thaw is fair) and shown beside the date. Thaw: floods 25% -- every pond and
   stream under the sky rises 2 and runs (an alert: mind the low ground). Highsun: drought
   20% -- crops under the sky ripen at half speed and grazers get 2 a mouthful, not 7; or a
   heatwave 15% -- each day 4% of the ale not in hand spoils. Deepfrost: a hard winter 30% --
   water under the sky is frozen (a drink there means breaking the ice: three times as long,
   -2) and the grass gives nothing.
   Leaffall is always fair. Underground, a hold hardly notices. `Peaceful` (tests) keeps
   raids, beasts and cavern spawns from the seasons.

17. **Nobles** (`Fortress.Nobles`) — at twenty dwarves the Mountainhome names one (a dwarf,
   not a soldier; the most skilled) Baron or Baroness; if they die, another, once the hold is
   twenty again. Each season they demand one thing by the next season's turn: a room (their
   own bed with a chest and a table within 3 cells), things made (three crafts, a statue,
   two chairs, a coffin, three barrels -- counted from when it was asked), or a ban (no
   crafts, bars, ale or meals traded away; a trade that does breaks it). Met: +3 for everyone.
   Failed: -5 for everyone and the Mountainhome's standing -10. The noble's panel shows the
   demand and how it stands. The overseer humours them (orders what is wanted, sets a chest
   and table by their bed, keeps banned goods out of its trades).

18. **At the gate** (`Fortress.Events`) — each season from the first Highsun, a 50% chance that
   something comes wanting an answer: the game stops and a dialog asks; left two days, the
   last (careful) answer is taken. A bard (Leaffall or Deepfrost) asks to winter: taken in,
   +3 for everyone daily until Thaw, and they eat a meal and drink an ale a day. Someone
   asks shelter (any people): taken in, they join (thin and hungry) -- but 15% are a grimling
   spy who slips away and a raid follows; turned away, -3 for everyone. An envoy from a
   neighbouring hold asks a gift (wealth/20, 100-600, paid in finished goods): given, three
   more migrants next season. A wanderer sells one odd thing (80-200): 60% a named masterwork
   (a cut gem or a craft in gold, silver or stone), 40% a painted stone. Rats in the stores:
   cleared at once (5% of the food), or left (4% a day for six days). The overseer takes in a
   bard if there is plenty, shelters everyone, pays the envoy when the gift is small beside
   the hold's wealth, never buys from the wanderer, and clears the rats. The agent can call
   any of them: `happen bard|refugee|envoy|wanderer|rats` (and `beast KEY`).

19. **The depths** (`Fortress.Caverns`) — three cavern layers where the world is deep enough:
   the upper caverns (as before), the Glimmer Deep six levels down (glimmerstone, a gem worth
   25, in its walls; glimmercap, an edible brewable crop, gathered from its shrubs; packs of
   gloomhounds), and the Ember Halls near the magma (starsilver ore -- a metal worth 30 that
   hits 1.6x, iron 1.25x, and the forge takes it; emberlings). Each is announced the first
   time it is broken into, with its first creatures, and sends more each season after
   (2-3 gloomhounds, 1-2 emberlings, +1 every third year). In the Ember Halls an ancient ruin:
   a sealed marble vault (9 x 7) where the Warden (1100 hp, 38) sleeps -- it never wanders,
   and wakes when someone comes within reach -- beside a named starsilver war hammer, an
   artifact of the first holds. Slain, it leaves its heartstone and a deed. Wealth counts only
   what the hold has found: a treasure in an undiscovered vault is no one's yet.

20. **Clan ambitions and brawls** (`Fortress.Clans`) — each clan wants something of the hold:
   the Ironhands a forge that has armed it (a forge, six weapons and armour), the Mossbeards
   a great garden (40 cells of farm) and forty barrels, the Stonewards a strong guard (six
   under arms) and a door, the Gemhollows eight cut gems, the Copperclaws six things of gold
   or silver, the Hollyhocks 30 cells of farm under the sky, the Graniteborn 800 cells dug.
   Met (checked each season), the clan is proud (+8 for twelve days); still unmet at a new
   year, slighted (-3). A dwarf's panel shows their clan's ambition. Two of feuding clans who
   loathe each other (opinion -70 or worse), when one is having a bad day (mood below 0),
   may come to blows when they meet (30%; each at most once a season): bruises (4-8, never
   below 1 hp), -4 each, and their opinion of each other sinks further.

21. **Things lying about, modelled** — in the 3D view an item is its kind's model (`it-*` in
   `assets/models`; furniture from the buildings', tools and shields from the dwarves' kit),
   merged into one mesh with its parts' colours, fitted to the item's size and drawn as one
   batch per kind; bars, blocks, gems, boulders and crafts take their material's colour.
   Eggs, and anything without a model yet, keep their plain shapes.

22. **The views show it, and it is heard** — drought burns the grass to straw; a hard winter
   turns the water under the sky to ice and lays snow on the trees; the ancient vault is
   worked marble, warm and banded. In 3D the light follows the clock (warm by day, gold at
   dawn and dusk, dim and blue at night; steady below the surface). The sim queues cues
   (`Fortress.Cues`) and the screen plays them: a roar for a great beast or the Warden
   waking, rushing water for a flood, a noble's stamped rage, a knock at the gate, a
   brawl's thumps, a deep bell for a new layer, a fanfare for a beast slain or an ambition
   met; a wintering bard doubles the hall's lute. `--shots music` writes the cues to
   `shots/cue-*.wav`.

23. **Achievements** (`Achievements`) — twenty deeds kept in the player's profile across every
   hold (with the hold and the day each was earned): founding, the first year, three years,
   fifty living, wealth 50,000, five raids broken, a great beast slain and three, the Glimmer
   Deep and the Ember Halls, the Warden slain, starsilver, an artifact, five demands met, a
   hard winter come through, a bard taken in, three sheltered, four clans content, 5,000
   traded, and losing a hold. Checked once a day; earned, a fanfare and a line. The title
   lists them all ("Achievements (n/20)"). The agent's and the self-check's play never count.

24. **Rigged creatures** — the humanoid creatures and other peoples (grimling raider and
   brute, the Lowland merchant, Delvling, Rootfolk, Stonekin, the Pale Stalker, the Warden)
   get bodies on the same skeleton template as the dwarves (`scripts/figures.py`, `body-*`),
   so every clip plays on them, retargeted per body; they come dressed as they are. A
   creature on a body walks (runs if fast), strikes when next to its foe, and stands; the
   raider carries an axe, the brute a club; the Mountainhome trader is a dwarf in an ochre
   coat. Without a body, the posed model; without that, the sprite.

25. **Ranks, raised workshops and great projects** (`Fortress.Ranks`) — the late game. A hold
   rises (checked each season, one rank at a time) from Outpost to **Hold** (20 living, wealth
   10,000), **Great Hold** (40, 40,000, a great project finished) and **Seat of the Mountainhome**
   (60, 100,000, two projects, a great beast or the Warden slain). Each rank: an alert, +10
   for everyone, two more migrants a season, raids one larger; at the Seat the noble is a king
   or queen. The HUD shows the rank and, in its tooltip, what the next asks.
   **Workshop tiers:** every workshop can be raised to tier II (a Hold: 4 blocks and 2 bars)
   and tier III (a Great Hold: 6 blocks, 3 bars, 2 cut gems), by a work order at the shop
   itself (masonry, 1200 and 2000 work). A tier works 1.3x / 1.6x as fast and adds +1 / +2 to
   the quality of what it makes; a tier III kitchen or brewery makes one more each time.
   Higher tiers unlock: jewelled crafts (a cut gem and something shiny) at a tier II
   craftsdwarf's shop; starsilver war hammers, breastplates and helms only at a tier III forge.
   The panel shows the tier, the next upgrade, and greys what a higher tier makes.
   **Great projects** (3 x 3, one of each, masonry): the **Great Gate** (a Hold; 24 blocks,
   8 bars, 2 mechanisms) takes a third off every raid; the **Cistern** (a Hold; 20 blocks,
   6 barrels, 4 bars) is a well: water beside it through drought and hard winter; the
   **Colossus** (a Great Hold; 40 blocks, 6 bars, 4 cut gems) lifts everyone +3 every day;
   the **Hall of Legends** (a Great Hold; 30 blocks, 6 cut gems, 4 shiny) +2 a day, up to
   +6, for the hold's deeds, beasts slain and artifacts. The overseer raises its workshops
   and builds the projects as they open: in a three-year run it becomes a Hold, raises seven
   workshops to tier II and begins the Cistern. Six new achievements follow the ranks, a tier
   III workshop and the projects.

26. **Rooms that matter, and the look of them.** A raised workshop shows its tier's model in
   3D (`<shop>-t2`, `-t3`). Every piece of furniture says in its help what it does, with the
   numbers. **Chests** are claimed like beds (the nearest to one's own bed): +2 a day, +4 when
   it stands beside one's own bed. **Floor styles** (S): smoothed, flagstones, planks, tiles,
   mosaic or bare, laid by masons over a dragged box -- only for the look (`World.Floors`).

27. **Dwarves talk** (`Fortress.Talk`, `UI/Bubbles`). A strong new thought is said aloud (the
   stronger, the likelier), and so are fleeing, a tantrum, a mood, madness: ~70 topics, lines
   varied by trait, with the dwarf's god, clan and hold. One line a dwarf every 400 ticks;
   bubbles for 4.5 s of real time in every view; a setting turns them off. Talk has its own
   dice: it never changes the game.

28. **Mechanisms** (`Fortress.Mechanisms`, `UI/MechanismPanel`). Two switches -- a **lever**
   (a dwarf walks over and pulls it when asked) and a **pressure plate** (on while a foe, a
   dwarf or anyone stands on it, off 80 ticks after) -- linked to what they work, a mechanism
   a link, laid by a builder. On: a door locks, a floodgate opens, a drawbridge rises, a
   **pit hatch** (over a channelled gap) opens and drops what is on it, a **cage trap**
   catches the foe or beast on it (held until slain or let go), a **bolt launcher** fires
   three bolts down its corridor (14 cells, at the first foe; reloads in 200 ticks), a
   stonefall trap drops, a lift stops. Wires are drawn from switch to target, lit while on.
   The **lift** goes in a stair shaft and runs its whole height: a ride between levels costs
   2 ticks against the stairs' 8 (4 mechanisms, 6 blocks, 4 bars, 1200 work); stopped, the
   shaft carries nobody.

29. **Portraits** (`scripts/portraits.mjs`, `Assets.Portrait`). Every dwarf has a face: drawn
   by portraitgen (local, procedural) in its realistic style, five for each clan, sex and age
   (the old ones grey), in the clan's colours and its people's skin and hair; a dwarf always
   gets the same one. In the dwarf's panel and the dwarf list, framed in the clan's colour.

## The simulation

- **Cells.** Each cell has a shape (Empty, Floor, Wall, StairUp/Down/UD, Ramp, Tree, Water,
  Magma), a material, flags (Hidden, Built, Shrub, Grass, Outside) and a designation. A cell's
  floor is the top of the cell below it.
- **Movement.** 8-way on a level (no corner cutting), stairs between matching stair cells,
  ramps to the level above beside a wall. Step 5 ticks, diagonal 7, stairs 8.
- **Wood.** Whatever the site, at least 45 trees stand within 40 cells of the wagon: on a bare
  site the generator plants groves of 5-9 on the soil (wood for beds, barrels and the first shops).
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
  year (at most 90%, times the raid pressure of warrens nearby) each season from the first
  autumn; 2-4 + wealth/5000 + 2 a year strong, but never more than 2 + half the living dwarves
  (a hold bled thin is picked over, not wiped out); a brute in every four; they leave after
  two and a half days.
- **Migrants.** Each new season: 1-3, plus one per 4000 wealth (up to 12), plus 2 while the
  hold is under ten; they walk in from the map edge.
