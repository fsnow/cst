#!/usr/bin/env python3
"""Search blind for commentary/base pairs: test every file with bold lemmas against every other file.

Usage: python3 pairs.py [--canonical]      (default: añña files and the .nrf ṭīkās; --canonical adds all)

Uses content.py's test with lemmas of 10+ letters and a control at n+20, which keeps the control near 0
for real pairs (s0101a -> s0101m: 81% hit, 1% control). Reports pairs with at least 20 tested blocks,
hit >= 50% and control <= 15%. A pair already known from Books.cs links shows up too, which is the check.
"""
import sys
from corpus import books
from content import blocks, lemmas, index

MINLEN, SHIFT = 10, 20


def main():
    bs = books()
    files = [b["file"] for b in bs if "--canonical" in sys.argv or not b["canonical"] or b["file"].endswith(".nrf.xml")]
    lem = {}
    for f in files:
        lem[f] = [(a, b, [l for l in lemmas(h) if len(l) >= MINLEN]) for a, b, h in blocks(f)]
        lem[f] = [x for x in lem[f] if x[2]]
    idx = {f: index([f]) for f in files}
    for c in files:
        if len(lem[c]) < 20:
            continue
        for g in files:
            if g == c:
                continue
            base = idx[g]
            hit = ctl = 0
            for a, b, ls in lem[c]:
                hit += any(l in t for n in range(a, b + 1) for t in base.get(n, ()) for l in ls)
                ctl += any(l in t for n in range(a + SHIFT, b + SHIFT + 1) for t in base.get(n, ()) for l in ls)
            n = len(lem[c])
            if hit / n >= 0.5 and ctl / n <= 0.15:
                print(f"{c} -> {g}: {n} blocks, hit {100 * hit / n:.0f}%, control {100 * ctl / n:.0f}%")


if __name__ == "__main__":
    main()
