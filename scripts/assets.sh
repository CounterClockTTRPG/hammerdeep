#!/usr/bin/env bash
# HAMMERDEEP -- generate the sprites, via ../assetgen.
#
#   bash scripts/assets.sh               generate anything missing
#   bash scripts/assets.sh --list        say what is missing, generate nothing
#   bash scripts/assets.sh --force NAME  regenerate one, even if it exists
#   bash scripts/assets.sh --recut       cut every sprite again from the library's originals
#                                        (after changing fit_sprite.py; costs nothing)
#
# Reads assets/art/manifest.txt (name | pixels [green] | subject) and writes
# assets/art/<name>.webp. The view wording comes from the name's prefix:
#   fig-, cr-, furn-   upright, from the front and a little above, cut out
#   shop-*-iso         an isometric open-air workshop on a diamond base, cut out
#   shop-*-top         a workshop seen from directly above, filling its square
# The Replicate token, the house style and the cost history live in assetgen's
# `hammerdeep` project; the library is asked before Replicate, so a re-run of a
# half-finished batch does not pay twice. The results are committed, so the game
# never needs a token or the network -- and it draws its own art for anything missing.
set -uo pipefail
cd "$(dirname "$0")/.."

ASSETGEN="${ASSETGEN:-../assetgen}"
[ -x "$ASSETGEN/.venv/bin/assetgen" ] || ASSETGEN="$HOME/claude/assetgen"
export ASSETGEN_PROJECT="${ASSETGEN_PROJECT:-hammerdeep}"
CLI="$ASSETGEN/.venv/bin/assetgen"
PY="$ASSETGEN/.venv/bin/python"
JOBS="${JOBS:-6}"

if [ ! -x "$CLI" ]; then
    echo "No assetgen at $ASSETGEN (set ASSETGEN). The sprites already in assets/art are enough to play." >&2
    exit 1
fi

mode="generate"; only=""
case "${1:-}" in
    --list)  mode="list" ;;
    --force) mode="force"; only="${2:?--force needs a name}" ;;
    --recut) mode="recut" ;;
    "")      ;;
    *)       echo "unknown option: $1" >&2; exit 2 ;;
esac

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

prompt_for() {
    local name="$1" subject="$2" bg="$3"
    case "$name" in
        shop-*-iso) echo "$subject, as an isometric game building tile: a square open-air workshop on a diamond-shaped flagstone base seen from above at a 45 degree angle, low stone half-walls on the back two sides only, no roof, the whole base visible, on a flat plain solid pure $bg background" ;;
        shop-*-top) echo "$subject, seen from directly overhead, orthographic top-down view, a square flagstone workshop floor filling the frame edge to edge, low stone walls along the edges, no roof, no perspective" ;;
        furn-*)     echo "$subject, a single object, seen from the front and slightly above, on a flat plain solid pure $bg background" ;;
        *)          echo "$subject, full body, standing, seen from the front and slightly above, on a flat plain solid pure $bg background" ;;
    esac
}

# One sprite: generate (or fetch from the library), then cut out and fit.
one() {
    local name="$1" size="$2" subject="$3" target="assets/art/$1.webp"
    local px bg="magenta (#FF00FF)" cut="key"
    px="$(echo "$size" | awk '{print $1}')"
    case "$size" in *green*) bg="lime green (#00FF00)" ;; esac
    case "$name" in shop-*-top) cut="fill" ;; esac
    local text; text="$(prompt_for "$name" "$subject" "$bg")"
    local shown
    if shown="$("$CLI" show "$name" 2>/dev/null)" && printf '%s\n' "$shown" | grep -q '^  error '; then
        # A refused or failed run leaves an asset with no picture: drop it and ask again.
        echo y | "$CLI" rm "$name" >/dev/null
    fi
    if "$CLI" show "$name" >/dev/null 2>&1; then
        if [ "$mode" = "force" ]; then "$CLI" regen "$name" >/dev/null || { echo "  $name: regen FAILED" >&2; return 1; }; fi
    elif [ "$mode" = "recut" ]; then
        echo "  $name: not in the library, nothing to re-cut" >&2; return 1
    else
        "$CLI" new --kind image --name "$name" --param aspect_ratio=1:1 --text "$text" >/dev/null || { echo "  $name: generate FAILED" >&2; return 1; }
    fi
    "$CLI" get "$name" --out "$TMP/$name.png" >/dev/null || { echo "  $name: fetch FAILED" >&2; return 1; }
    "$PY" scripts/fit_sprite.py "$TMP/$name.png" "$target" "$px" "$cut" || { echo "  $name: cut-out FAILED" >&2; return 1; }
    echo "  $name  ${px}px  $(du -h "$target" | cut -f1)"
}

COUNT=0; MISSING=0; FAILED=0
while IFS= read -r raw || [ -n "$raw" ]; do
    case "$raw" in ''|'#'*) continue ;; esac
    name="$(echo "$raw" | cut -d'|' -f1 | xargs)"
    size="$(echo "$raw" | cut -d'|' -f2 | xargs)"
    subject="$(echo "$raw" | cut -d'|' -f3- | sed 's/^ *//; s/ *$//')"
    [ -n "$name" ] && [ -n "$size" ] && [ -n "$subject" ] || { echo "malformed line: $raw" >&2; exit 1; }
    COUNT=$((COUNT + 1))
    if [ "$mode" = "force" ]; then [ "$name" = "$only" ] || continue
    elif [ "$mode" = "recut" ]; then :
    elif [ -f "assets/art/$name.webp" ]; then continue
    elif [ "$mode" = "list" ]; then echo "  missing  $name"; MISSING=$((MISSING + 1)); continue
    fi
    # A few at a time: each waits on Replicate, not on this machine.
    while [ "$(jobs -rp | wc -l)" -ge "$JOBS" ]; do wait -n || FAILED=$((FAILED + 1)); done
    one "$name" "$size" "$subject" </dev/null &
done < assets/art/manifest.txt
while [ "$(jobs -rp | wc -l)" -gt 0 ]; do wait -n || FAILED=$((FAILED + 1)); done

if [ "$mode" = "list" ]; then echo "assets: $COUNT in the manifest, $MISSING missing"; exit 0; fi
if [ "$FAILED" -gt 0 ]; then echo "assets: $FAILED failed" >&2; exit 1; fi
echo "assets: $COUNT in the manifest"
