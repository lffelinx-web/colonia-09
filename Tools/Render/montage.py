"""Joins PNGs side by side into one sheet (Blender's Python, for previews). Args: OUT files..."""
import sys, bpy
import numpy as np
args = sys.argv[sys.argv.index("--") + 1:]
out, files = args[0], args[1:]
imgs = []
for f in files:
    im = bpy.data.images.load(f)
    w, h = im.size
    a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
    imgs.append(a)
H = max(a.shape[0] for a in imgs)
W = sum(a.shape[1] for a in imgs)
sheet = np.zeros((H, W, 4), dtype=np.float32)
sheet[..., :3] = (.33, .4, .45)
sheet[..., 3] = 1
x = 0
for a in imgs:
    h, w = a.shape[:2]
    alpha = a[..., 3:4]
    sheet[:h, x:x + w, :3] = a[..., :3] * alpha + sheet[:h, x:x + w, :3] * (1 - alpha)
    x += w
img = bpy.data.images.new("sheet", W, H, alpha=True)
img.pixels = sheet.ravel()
img.filepath_raw = out
img.file_format = "PNG"
img.save()
