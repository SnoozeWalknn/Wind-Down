from pathlib import Path
from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "WindDown.App" / "Assets" / "WindDown.ico"
PREVIEW = ROOT / "artifacts" / "WindDown-icon-preview.png"
SIZE = 1024

canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
rounded = Image.new("L", (SIZE, SIZE), 0)
ImageDraw.Draw(rounded).rounded_rectangle((48, 48, 976, 976), radius=220, fill=255)

top = (23, 38, 64)
bottom = (8, 16, 29)
gradient = Image.new("RGBA", (SIZE, SIZE))
pixels = gradient.load()
for y in range(SIZE):
    for x in range(SIZE):
        amount = (x + y) / (2 * (SIZE - 1))
        color = tuple(round(a + (b - a) * amount) for a, b in zip(top, bottom))
        pixels[x, y] = (*color, 255)
canvas.alpha_composite(Image.composite(gradient, Image.new("RGBA", canvas.size), rounded))

border = Image.new("RGBA", canvas.size)
ImageDraw.Draw(border).rounded_rectangle((49, 49, 975, 975), radius=219, outline=(57, 81, 115, 184), width=3)
canvas.alpha_composite(border)

moon = Image.new("L", canvas.size, 0)
moon_draw = ImageDraw.Draw(moon)
moon_draw.ellipse((176, 224, 752, 800), fill=255)
cutout = Image.new("L", canvas.size, 0)
ImageDraw.Draw(cutout).ellipse((308, 100, 892, 684), fill=255)
moon = ImageChops.subtract(moon, cutout)
moon_layer = Image.new("RGBA", canvas.size, (221, 245, 255, 255))
canvas.alpha_composite(Image.composite(moon_layer, Image.new("RGBA", canvas.size), moon))

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
PREVIEW.parent.mkdir(parents=True, exist_ok=True)
canvas.save(PREVIEW)
canvas.save(OUTPUT, format="ICO", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
