#!/usr/bin/env python3
"""Where numbers restart, do a commentary's sections line up with its base's? (section-qualified ¶)

Usage: python3 sections.py [COMMENTARY BASE [BASE ...]]   (no arguments: every linked pair whose base restarts)

A section here is a run of paragraph numbers between restarts (a number that is 1, or below half the
previous one: paranum.py's rule), taken across the base files in Books.cs order. For each commentary
section, every base section is scored with content.py's lemma test (lemmas of 6+ letters, same number).
The best one is the section the commentary is on. Reported per pair:
  sections   commentary sections with lemmas / base sections
  in order   whether the best-matching base sections never go backwards
  matched    blocks hit in their best base section, as a share of blocks tested
A section where no base section scores above zero is listed as unmatched.
"""
import sys
from content import blocks, lemmas, plain, linked, CANDIDATES


def split(items):
    segs = [[]]
    for it in items:
        if segs[-1] and (it[0] == 1 or it[0] < segs[-1][-1][1] // 2):
            segs.append([])
        segs[-1].append(it)
    return [s for s in segs if s]


def align(comm, bases):
    bsegs = []
    for f in bases:
        for seg in split(blocks(f)):
            idx = {}
            for a, b, h in seg:
                t = plain(h).replace(' ', '')
                for n in range(a, b + 1):
                    idx.setdefault(n, []).append(t)
            bsegs.append(idx)
    out = []
    for seg in split(blocks(comm)):
        items = [(a, b, [l for l in lemmas(h) if len(l) >= 6]) for a, b, h in seg]
        items = [x for x in items if x[2]]
        if not items:
            continue
        best, bi = 0, None
        for i, idx in enumerate(bsegs):
            sc = sum(any(l in t for n in range(a, b + 1) for t in idx.get(n, ()) for l in ls) for a, b, ls in items)
            if sc > best:
                best, bi = sc, i
        out.append((seg[0][0], seg[-1][1], len(items), bi, best))
    return out, len(bsegs)


def main():
    a = sys.argv[1:]
    pairs = [(a[0], a[1:])] if a else linked() + CANDIDATES
    print("| commentary | base | sections | in order | matched | unmatched sections |")
    print("|---|---|---|---|---:|---|")
    for c, g in pairs:
        res, nb = align(c, g)
        if nb < 2 and not a:
            continue
        hits = [r[3] for r in res if r[3] is not None]
        ordered = all(x <= y for x, y in zip(hits, hits[1:]))
        tested = sum(r[2] for r in res)
        matched = sum(r[4] for r in res)
        un = [f"¶{r[0]}-{r[1]}" for r in res if r[3] is None]
        base = g[0] if len(g) == 1 else f"{g[0]} .. {g[-1]} ({len(g)})"
        print(f"| {c} | {base} | {len(res)} / {nb} | {'yes' if ordered else 'NO'} | "
              f"{100 * matched / tested:.0f}% | {', '.join(un) or '-'} |")


if __name__ == "__main__":
    main()
