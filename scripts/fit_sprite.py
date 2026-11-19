#!/usr/bin/env python3
"""Cut a generated sprite out of its flat background and shrink it to game size.

    fit_sprite.py <in.png> <out.webp> <pixels> [key|fill]

Run with ../assetgen's interpreter (Pillow and numpy live there) -- see
scripts/assets.sh. The model paints each subject on a flat plain background,
because none of the image models here can paint transparency -- and not in the
colour asked for (the house style pulls it towards the palette), so the key
colour is measured from the picture's border. Then:

- only background connected to the border goes, by flood fill, so a purple
  cloak or a gem inside the figure is never punched through;
- the edge gets a soft alpha from the colour distance, and the background's
  colour is un-mixed from the edge pixels, so there is no tinted fringe;
- the result is cropped to what is left, padded a little, and its longest
  side scaled to <pixels>.

With `fill` (a top-down workshop, which fills its square) it is only scaled,
unless the model painted it on a plain background after all: then that is cut
away and the rest stretched to the square. Lossy WebP with alpha at quality 90; Godot decodes it from bytes.
"""

import sys
from collections import deque

import numpy as np
from PIL import Image

# How close to the background colour counts as background is measured per picture: the
# spread of its border (the model's "flat" background has a little noise and gradient),
# plus a margin; then a soft edge of EDGE levels. A fixed wide tolerance ate dark subjects
# painted on a dark background.
MARGIN = 10.0
EDGE = 35.0
POCKET = 0.0004   # an enclosed patch of background at least this share of the picture goes too
FLAT = 20.0       # a border straying less than this is a plain background, not a picture to the edge
QUALITY = 90
PAD = 0.04


def border(rgb: np.ndarray):
    """The background colour (median of a thin frame of border pixels) and how far it strays."""
    k = 6
    frame = np.concatenate([rgb[:k].reshape(-1, 3), rgb[-k:].reshape(-1, 3),
                            rgb[:, :k].reshape(-1, 3), rgb[:, -k:].reshape(-1, 3)]).astype(np.float32)
    key = np.median(frame, axis=0)
    spread = float(np.percentile(np.sqrt(((frame - key) ** 2).sum(axis=1)), 95))
    return key, spread


def fill(passable: np.ndarray, seeds: np.ndarray) -> np.ndarray:
    """Every passable pixel 4-connected to a passable seed."""
    h, w = passable.shape
    got = seeds & passable
    q = deque(zip(*np.nonzero(got)))
    while q:
        y, x = q.popleft()
        for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
            if 0 <= ny < h and 0 <= nx < w and passable[ny, nx] and not got[ny, nx]:
                got[ny, nx] = True
                q.append((ny, nx))
    return got


def big_patches(mask: np.ndarray, min_size: int) -> np.ndarray:
    """The 4-connected patches of mask with at least min_size pixels, in one pass."""
    h, w = mask.shape
    seen = np.zeros((h, w), bool)
    out = np.zeros((h, w), bool)
    for y0, x0 in zip(*np.nonzero(mask)):
        if seen[y0, x0]:
            continue
        seen[y0, x0] = True
        patch, q = [], deque([(y0, x0)])
        while q:
            y, x = q.popleft()
            patch.append((y, x))
            for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    q.append((ny, nx))
        if len(patch) >= min_size:
            ys, xs = zip(*patch)
            out[list(ys), list(xs)] = True
    return out


def key_out(rgb: np.ndarray, key: np.ndarray, spread: float) -> np.ndarray:
    h, w, _ = rgb.shape
    pix = rgb.astype(np.float32)
    dist = np.sqrt(((pix - key) ** 2).sum(axis=2))
    hard = spread + MARGIN
    soft = hard + EDGE
    # Flood fill from every border pixel through pixels near the background colour.
    seeds = np.zeros((h, w), bool)
    seeds[0, :] = seeds[-1, :] = seeds[:, 0] = seeds[:, -1] = True
    bg = fill(dist < soft, seeds)
    # Pockets of background the subject closes in (between the legs, under a table top):
    # sizeable patches very close to the background colour, with their soft rims.
    pockets = big_patches((dist < hard) & ~bg, int(POCKET * h * w))
    if pockets.any():
        bg |= fill(dist < soft, pockets)
    alpha = np.ones((h, w), np.float32)
    alpha[bg] = np.clip((dist[bg] - hard) / (soft - hard), 0, 1)
    # The model paints a ground shadow however it is asked not to: the background's own hue,
    # darker. Reached from the background through such pixels, it goes too -- the game draws
    # its own shadows.
    chroma = pix / (pix.sum(axis=2, keepdims=True) + 1)
    kchroma = key / (key.sum() + 1)
    shadowy = (np.sqrt(((chroma - kchroma) ** 2).sum(axis=2)) < 0.10) & (pix.sum(axis=2) < key.sum() + 10)
    shadow = fill(shadowy | bg, bg) & ~bg
    alpha[shadow] = 0
    # Un-mix the background from the soft edge: pixel = a * colour + (1 - a) * key.
    edge = bg & (alpha > 0) & (alpha < 1)
    a3 = np.maximum(alpha, 1e-3)[..., None]
    unmixed = np.clip((pix - (1 - a3) * key) / a3, 0, 255)
    out = np.where(edge[..., None], unmixed, pix)
    return np.dstack([out, alpha * 255]).astype(np.uint8)


def main() -> int:
    if len(sys.argv) not in (4, 5):
        print("usage: fit_sprite.py <in.png> <out.webp> <pixels> [key|fill]", file=sys.stderr)
        return 2
    source, target, pixels = sys.argv[1], sys.argv[2], int(sys.argv[3])
    mode = sys.argv[4] if len(sys.argv) == 5 else "key"
    with Image.open(source) as im:
        rgb = np.asarray(im.convert("RGB"))
    key, spread = border(rgb)
    if mode == "fill":
        # A top-down workshop fills its square. If the model painted it on a plain background
        # anyway, cut that away and stretch what is left to the square.
        img = Image.fromarray(rgb, "RGB").convert("RGBA")
        if spread < FLAT and key.mean() > 128:  # a dark frame is the picture's own edge
            img = Image.fromarray(key_out(rgb, key, spread), "RGBA")
            img = img.crop(img.getchannel("A").point(lambda a: 255 if a > 16 else 0).getbbox())
        img.resize((pixels, pixels), Image.LANCZOS).save(target, "WEBP", quality=QUALITY, method=6, alpha_quality=100)
        return 0
    img = Image.fromarray(key_out(rgb, key, spread), "RGBA")
    box = img.getchannel("A").point(lambda a: 255 if a > 16 else 0).getbbox()
    if box is None:
        print("nothing left after keying", file=sys.stderr)
        return 1
    img = img.crop(box)
    w, h = img.size
    pad = int(max(w, h) * PAD)
    canvas = Image.new("RGBA", (w + 2 * pad, h + 2 * pad), (0, 0, 0, 0))
    canvas.paste(img, (pad, pad))
    w, h = canvas.size
    scale = pixels / float(max(w, h))
    canvas = canvas.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)
    canvas.save(target, "WEBP", quality=QUALITY, method=6, alpha_quality=100)
    return 0


if __name__ == "__main__":
    sys.exit(main())
