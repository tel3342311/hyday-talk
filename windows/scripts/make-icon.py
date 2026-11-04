# Draws the Hyday Talk mark (same geometry as scripts/make-icon.swift) with ImageMagick and saves
# a multi-size .ico: python3 make-icon.py out.ico
import subprocess, sys, tempfile, os
bars = [(18, 86, 14), (35, 18, 6.5), (42.5, 32, 6.5), (50, 24, 6.5), (57.5, 14, 6.5), (65, 20, 6.5), (82, 64, 14)]
x0 = y0 = 72; size = 880; unit = size * 0.72 / 100; ox = x0 + size * 0.14; oy = y0 + size * 0.14
draw = ["-fill", "#F2622E", "-draw", f"roundrectangle {x0},{y0} {x0 + size},{y0 + size} 200,200", "-fill", "#FFFFFF"]
for x, h, w in bars:
    W, H = w * unit, h * unit; left = ox + x * unit - W / 2; top = oy + 50 * unit - H / 2
    draw += ["-draw", f"roundrectangle {left:.1f},{top:.1f} {left + W:.1f},{top + H:.1f} {W / 2:.1f},{W / 2:.1f}"]
with tempfile.TemporaryDirectory() as tmp:
    png = os.path.join(tmp, "icon.png")
    subprocess.run(["convert", "-size", "1024x1024", "xc:none", *draw, "-resize", "256x256", png], check=True)
    subprocess.run(["convert", png, "-define", "icon:auto-resize=256,64,48,32,24,16", sys.argv[1]], check=True)
