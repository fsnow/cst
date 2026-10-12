#!/usr/bin/env python3
"""Find where a commentary's paragraph numbers drift off its base's: a numbering slip in one of the two.

Usage: python3 drift.py [COMMENTARY BASE [BASE ...]]     (no arguments: every pair content.py --linked tests)

Uses content.py's lemma test. The commentary's blocks are taken in windows of 30; for each window the
lemma hit rate is measured with the base number shifted by k (-3..+3). A window where some k != 0 beats
k = 0 by 10 or more hits is reported, with the commentary's number range. A run of such windows that
starts at one place and continues to the end of a file is a single slip: a number repeated or skipped
in one text and never corrected (s0505m numbers ¶274 twice).
"""
import sys
from content import blocks, lemmas, index, linked, CANDIDATES

WINDOW, SHIFTS, MARGIN = 30, range(-3, 4), 10


def drift(comm, bases):
    base = index(bases)
    cm = [(a, b, lemmas(h)) for a, b, h in blocks(comm)]
    cm = [x for x in cm if x[2]]
    out = []
    for i in range(0, len(cm), WINDOW):
        w = cm[i:i + WINDOW]
        score = {k: sum(any(l in t for n in range(a + k, b + k + 1) for t in base.get(n, ()) for l in ls)
                        for a, b, ls in w) for k in SHIFTS}
        k = max(score, key=score.get)
        if k != 0 and score[k] - score[0] >= MARGIN:
            out.append((w[0][0], w[-1][1], k, score[k], score[0], len(w)))
    return out


def main():
    a = sys.argv[1:]
    pairs = [(a[0], a[1:])] if a else linked() + CANDIDATES
    for c, g in pairs:
        for lo, hi, k, sk, s0, n in drift(c, g):
            print(f"{c} vs {g[0]}{'..' if len(g) > 1 else ''}: ¶{lo}-{hi} matches base n{k:+d} ({sk}/{n} vs {s0}/{n} unshifted)")


if __name__ == "__main__":
    main()
