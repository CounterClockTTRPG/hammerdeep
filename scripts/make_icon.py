# The application icon: a war hammer before a snow-capped peak, by forge-light.
#   python3 scripts/make_icon.py icon  ->  icon.png + icon.ico (needs Pillow).
import sys
from PIL import Image, ImageDraw, ImageFilter
out = sys.argv[1]
S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
bg = Image.new("RGBA", (S, S))
d = ImageDraw.Draw(bg)
for y in range(S):
    t = y / S
    d.line([(0, y), (S, y)], fill=(int(14 + 40 * t), int(12 + 18 * t), int(30 - 10 * t), 255))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([24, 24, S - 24, S - 24], radius=190, fill=255)
img.paste(bg, (0, 0), mask)
# A glow of the forge behind.
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse([212, 330, 812, 930], fill=(255, 120, 30, 200))
glow = glow.filter(ImageFilter.GaussianBlur(110))
img = Image.alpha_composite(img, Image.composite(glow, Image.new("RGBA", (S, S)), mask))
d = ImageDraw.Draw(img)
# The mountain, with snow.
d.polygon([(90, 900), (512, 170), (934, 900)], fill=(52, 44, 44, 255))
d.polygon([(512, 170), (420, 330), (470, 310), (512, 350), (560, 305), (604, 330)], fill=(225, 230, 236, 255))
# A door at its foot, lit.
d.rounded_rectangle([462, 760, 562, 900], radius=50, fill=(255, 170, 70, 255))
# The war hammer, crossing the peak.
d.polygon([(330, 820), (370, 860), (720, 420), (680, 380)], fill=(120, 80, 45, 255))
d.polygon([(600, 250), (820, 470), (760, 530), (540, 310)], fill=(190, 192, 200, 255))
d.polygon([(600, 250), (820, 470), (800, 490), (580, 270)], fill=(235, 236, 240, 255))
img = Image.composite(img, Image.new("RGBA", (S, S), (0, 0, 0, 0)), mask)
img.resize((256, 256), Image.LANCZOS).save(out + ".png")
img.save(out + ".ico", sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
