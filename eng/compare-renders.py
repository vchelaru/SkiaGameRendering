"""Pixel-diff PNG captures of the shared Scene: python -I eng/compare-renders.py a.png b.png [c.png ...]

Compares every capture to the first and prints, per pair: differing pixels, max channel delta, the largest
delta among pixels that are opaque in both, and an md5 of each file. Exit code 1 if any pair differs by more
than --tol (default 1). Needs Pillow.
"""
import argparse, hashlib, sys
from PIL import Image


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("images", nargs="+")
    ap.add_argument("--tol", type=int, default=1, help="max allowed channel delta (default 1)")
    ap.add_argument("--diff-dir", help="write <name>-diff.png (changed pixels white) for each pair")
    a = ap.parse_args()
    ref = Image.open(a.images[0]).convert("RGBA")
    print(f"ref {a.images[0]} {ref.size} md5 {hashlib.md5(open(a.images[0], 'rb').read()).hexdigest()[:8]}")
    bad = False
    for path in a.images[1:]:
        img = Image.open(path).convert("RGBA")
        md5 = hashlib.md5(open(path, "rb").read()).hexdigest()[:8]
        if img.size != ref.size:
            print(f"{path}: size {img.size} != {ref.size}")
            bad = True
            continue
        rp, ip = ref.load(), img.load()
        w, h = ref.size
        count = worst = worst_opaque = 0
        first = None
        mask = Image.new("L", ref.size, 0)
        mp = mask.load()
        for y in range(h):
            for x in range(w):
                r, i = rp[x, y], ip[x, y]
                if r == i:
                    continue
                d = max(abs(r[k] - i[k]) for k in range(4))
                count += 1
                worst = max(worst, d)
                if r[3] == 255 and i[3] == 255:
                    worst_opaque = max(worst_opaque, d)
                mp[x, y] = 255
                first = first or (x, y, r, i)
        print(f"{path}: differing {count} max-delta {worst} max-delta-opaque {worst_opaque} md5 {md5}" + (f" first {first}" if first else ""))
        if worst > a.tol:
            bad = True
        if a.diff_dir and count:
            import os
            os.makedirs(a.diff_dir, exist_ok=True)
            mask.save(os.path.join(a.diff_dir, os.path.splitext(os.path.basename(path))[0] + "-diff.png"))
    return 1 if bad else 0


sys.exit(main())
