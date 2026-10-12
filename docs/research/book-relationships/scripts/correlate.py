#!/usr/bin/env python3
"""How a commentary's paragraph numbers correlate with the text it comments on, for every linked pair.

Usage: python3 correlate.py [--all]

For each aṭṭhakathā and ṭīkā, the base is every file that links to it in Books.cs, in Books.cs order: the
mūla files whose AtthakathaIndex (or TikaIndex) names it, and for a ṭīkā also the aṭṭhakathā files whose
TikaIndex names it, falling back to the commentary's own MulaIndex/AtthakathaIndex for the later parts of
a split commentary, which no base file names. Books.cs gives a commentary only one MulaIndex, so its forward link alone misses volume
splits (s0402a comments on s0402m1-m3). A paragraph's number covers its range ("5-7" is 5, 6, 7).

  found    share of the commentary's numbered paragraphs whose numbers all exist in the base
  ordered  the longest run of the commentary's paragraph numbers that appears in the base in the same
           order (a longest common subsequence), as a share. A monotonic number shared by both texts
           scores 100%; restarts are fine as long as both texts restart together
Unlinked commentaries (// unlinked in Books.cs) have no base here; they are listed at the end.
--all prints every pair; by default only pairs below 95% ordered are shown in full.
"""
import sys
from corpus import books, read_xml
from paranum import TOK, parse


def numbers(file_name):
    """The file's numbered paragraphs, in order, each as a (first, last) range. n="0" is skipped."""
    out = []
    for m in TOK.finditer(read_xml(file_name)):
        if m.group(2) is None:
            continue
        v = parse(m.group(2))
        if v and v[0] != 0:
            out.append(v)
    return out


def correlate(comm, base):
    seq = [n for a, b in base for n in range(a, b + 1)]
    have = set(seq)
    found = sum(all(n in have for n in range(a, b + 1)) for a, b in comm)
    return found, lcs([a for a, _ in comm], seq)


def lcs(xs, ys):
    """Length of the longest common subsequence (Hunt-Szymanski: longest increasing run of positions)."""
    import bisect, collections
    where = collections.defaultdict(list)
    for i, y in enumerate(ys):
        where[y].append(i)
    tails = []
    for x in xs:
        for i in reversed(where.get(x, ())):
            k = bisect.bisect_left(tails, i)
            if k == len(tails):
                tails.append(i)
            else:
                tails[k] = i
    return len(tails)


def name(group):
    files = [b["file"] for b in group]
    return files[0] if len(files) == 1 else f"{files[0]} .. {files[-1]} ({len(files)})"


def main():
    show_all = "--all" in sys.argv
    bs = books()
    by_index = {b["index"]: b for b in bs}
    rows, unlinked = [], []
    for c in bs:
        if c["matn"] not in ("Atthakatha", "Tika"):
            continue
        key = "atthakatha" if c["matn"] == "Atthakatha" else "tika"
        groups = [[b for b in bs if b["matn"] == "Mula" and b[key] == c["index"]]]
        if c["matn"] == "Tika":
            groups.append([b for b in bs if b["matn"] == "Atthakatha" and b["tika"] == c["index"]])
        # A later part of a split commentary (s0508a2) is named by no base file; use its own forward link.
        if not groups[0] and c["mula"] in by_index:
            groups[0] = [by_index[c["mula"]]]
        if c["matn"] == "Tika" and not groups[1] and c["atthakatha"] in by_index:
            groups[1] = [by_index[c["atthakatha"]]]
        groups = [g for g in groups if g]
        if not groups:
            unlinked.append(c["file"])
            continue
        cn = numbers(c["file"])
        for g in groups:
            f, o = correlate(cn, [v for b in g for v in numbers(b["file"])])
            rows.append((c["file"], name(g), len(cn), f, o))
    print("| commentary | base | paras | found | ordered |")
    print("|---|---|---:|---:|---:|")
    for cf, bf, n, f, o in rows:
        pct = lambda x: f"{100 * x / n:.0f}%" if n else "-"
        if show_all or not n or o / n < 0.95:
            print(f"| {cf} | {bf} | {n} | {pct(f)} | {pct(o)} |")
    good = sum(1 for r in rows if r[2] and r[4] / r[2] >= 0.95)
    print(f"\n{len(rows)} linked pairs; {good} at >=95% ordered.")
    print(f"Commentaries with no linked base ({len(unlinked)}): {', '.join(unlinked)}")


if __name__ == "__main__":
    main()
