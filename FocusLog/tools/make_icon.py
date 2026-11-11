# Generates src/FocusLog/Assets/app.ico (dark rounded square + accent progress ring).
import math
from PIL import Image, ImageDraw
S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle((32, 32, S - 32, S - 32), radius=220, fill=(23, 24, 28, 255), outline=(51, 54, 61, 255), width=14)
box = (232, 232, S - 232, S - 232)
d.arc(box, 0, 360, fill=(51, 54, 61, 255), width=70)
d.arc(box, -90, 190, fill=(124, 138, 245, 255), width=70)
cx = cy = S / 2
rad = (S - 464) / 2
for ang in (-90, 190):
    x = cx + rad * math.cos(math.radians(ang)); y = cy + rad * math.sin(math.radians(ang))
    d.ellipse((x - 35, y - 35, x + 35, y + 35), fill=(124, 138, 245, 255))
img.resize((256, 256), Image.LANCZOS).save(
    "src/FocusLog/Assets/app.ico",
    sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (256, 256)],
)
