#!/usr/bin/env node
// Dwarf portraits from ../portraitgen (procedural, local, free): a pool per clan, sex and age, in the clan's colour.
// Writes assets/portraits/<clan>-<f|m>-<young|old>-<n>.webp (needs Pillow); the game picks one per dwarf by its id.
//
//   node scripts/portraits.mjs              make the pool (skips files already there)
//   node scripts/portraits.mjs --force      make it again
//   node scripts/portraits.mjs --sample     one dwarf in each render style, to shots/portrait-styles-*.png
//
// PORTRAITGEN (default ../portraitgen) is where portraitgen lives.
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
// Beside the repository (walking up, so it is found from a worktree too), unless PORTRAITGEN says where.
function findPortraitgen() {
  if (process.env.PORTRAITGEN) return path.resolve(process.env.PORTRAITGEN);
  for (let dir = root; dir !== path.dirname(dir); dir = path.dirname(dir)) {
    const p = path.join(path.dirname(dir), 'portraitgen');
    if (fs.existsSync(path.join(p, 'src/index.js'))) return p;
  }
  throw new Error('portraitgen not found: set PORTRAITGEN');
}
const pg = findPortraitgen();
const { generate } = await import(pathToFileURL(path.join(pg, 'src/index.js')).href);
const { toPNG } = await import(pathToFileURL(path.join(pg, 'src/png-node.js')).href);

// Nothing modern or out of the hold's world, and no crown (that would read as the noble).
const AVOID = ['glassesRound', 'glassesSquare', 'sunglasses', 'goggles', 'visor', 'cyberEye', 'tattooCircuit', 'cyberImplant',
  'crown', 'beanie', 'headphones', 'headset', 'halo', 'antennae', 'helmetSpace', 'helmetCombat', 'wizardHat', 'topHat',
  'wideBrimHat', 'faceMask', 'acne', 'bindi', 'catEars', 'whiskers', 'felineMuzzle', 'furMarkings', 'felineFur',
  'shoulderCat', 'shoulderDragon', 'flowerCrown', 'noseRing', 'piercings', 'choker'];

export const STYLE = 'realistic', SIZE = 192, PER = 5;

// The clans and their colours, read from the game's own tables so the two never drift apart.
const tables = fs.readFileSync(path.join(root, 'src/Sim/Tables.cs'), 'utf8');
const clans = [...tables.matchAll(/C\("(\w+)", "[^"]+", 0x([0-9a-f]{6})/g)].map(m => ({ id: m[1], color: '#' + m[2] }));
// Which people each clan belongs to (dwarves have no clan entry of their own), and their skin and hair.
const races = fs.readFileSync(path.join(root, 'src/Sim/Races.cs'), 'utf8');
const peoples = {};
for (const m of races.matchAll(/Key = "(\w+)"[^]*?Clan = (?:"(\w+)"|null)[^]*?Skin = Rgb\.Hex\(0x([0-9a-f]{6})\), Hair = Rgb\.Hex\(0x([0-9a-f]{6})\)/g))
  peoples[m[2] || '*'] = { race: m[1], skin: '#' + m[3], hair: '#' + m[4] };

// How each people is drawn: portraitgen's fantasy archetypes, with the people's own colours where they differ.
function look(clan) {
  const p = peoples[clan.id];
  if (!p || p.race === 'dwarf') return { archetype: 'dwarf' };
  if (p.race === 'delvling') return { archetype: 'orc', colors: { skin: p.skin, hair: p.hair } };
  if (p.race === 'rootfolk') return { archetype: 'human', colors: { skin: p.skin, hair: p.hair }, build: 'slender' };
  if (p.race === 'stonekin') return { archetype: 'orc', colors: { skin: p.skin, hair: p.hair }, build: 'heavy' };
  return { archetype: 'dwarf' };
}

function traits(clan, female, old, n, style = STYLE) {
  const l = look(clan);
  return generate({
    theme: 'fantasy', style, seed: `hammerdeep-${clan.id}-${female ? 'f' : 'm'}-${old ? 'old' : 'young'}-${n}`,
    archetype: l.archetype, presentation: female ? 'feminine' : 'masculine', build: l.build,
    age: old ? 'elder' : n % 2 ? 'adult' : 'young',
    background: n % 2 ? 'candlehall' : 'archway', frame: 'none',
    // Working clothes in the clan's colour (plate armour would hide it).
    clothing: ['tunic', 'leather', 'tabard'][n % 3],
    colors: { clothing: clan.color, ...(l.colors || {}) },
    avoid: AVOID,
  });
}

const args = process.argv.slice(2);
if (args.includes('--sample')) {
  fs.mkdirSync(path.join(root, 'shots'), { recursive: true });
  for (const style of ['illustrated', 'painterly', 'realistic', 'pixel', 'engraving']) {
    const png = await toPNG(traits(clans[0], false, true, 1, style), { size: 256 });
    fs.writeFileSync(path.join(root, 'shots', `portrait-styles-${style}.png`), png);
    console.log(`  shots/portrait-styles-${style}.png`);
  }
  process.exit(0);
}

const out = path.join(root, 'assets', 'portraits');
fs.mkdirSync(out, { recursive: true });
let made = 0, had = 0;
for (const clan of clans)
  for (const female of [false, true])
    for (const old of [false, true])
      for (let n = 1; n <= PER; n++) {
        const file = path.join(out, `${clan.id}-${female ? 'f' : 'm'}-${old ? 'old' : 'young'}-${n}.webp`);
        if (fs.existsSync(file) && !args.includes('--force')) { had++; continue; }
        const png = file.replace(/\.webp$/, '.png');
        fs.writeFileSync(png, await toPNG(traits(clan, female, old, n), { size: SIZE }));
        // The game reads webp, a tenth of the size: Pillow converts it.
        execFileSync('python3', ['-c', 'import sys; from PIL import Image; Image.open(sys.argv[1]).convert("RGB").save(sys.argv[2], quality=86)', png, file]);
        fs.unlinkSync(png);
        made++;
      }
// The game finds the folder by its manifest (in the project, or beside an exported build).
fs.writeFileSync(path.join(out, 'manifest.txt'), fs.readdirSync(out).filter(f => f.endsWith('.webp')).sort().join('\n') + '\n');
console.log(`portraits: ${made} made, ${had} already there, in assets/portraits (${clans.length} clans)`);
