#!/usr/bin/env python3
"""List every irregularity in paragraph numbering, one per line, for checking against the source PDFs.

Usage: python3 paragaps.py [--all] [--csv OUT.csv] [FILE ...]

paranum.py classifies each file; this lists each place. Numbers are compared as in paranum.py: within a
<div type="book"> segment, ranges ("196-7") cover both ends, n="0" is ignored, and a number that is 1 or
below half the previous one is a restart, not an error.

Kinds:
  dropped   a gap with unnumbered body paragraphs in it: probably a paragraph whose number was lost
            (abh02m's 185, 252, 264 and 298, confirmed in the 1957 print). "pe" marks a gap whose text
            has a peyyala abbreviation, which may explain it; abh02m's four have one, inside the text.
  skipped   a gap with no unnumbered paragraph in it: the next number may be mistyped, or the print skips
  repeated  the same number twice in a row
  peyyala   a gap with no unnumbered paragraph in it, whose text has a peyyala abbreviation (pe): the
            elided paragraphs' numbers are elided with them, as in the Vimanavatthu. Expected; listed
            with --all only.
  regress   lower than the one before but not a restart (e0102n's 590, 561, 592)

By default only mūla and añña files are listed for gaps, since a commentary's gaps are expected (it
comments on some of its base's paragraphs). repeated and regress are listed for every file. --all lists
commentary gaps too.

For a mūla gap, "att" says whether a commentary on that book has the missing number: support for "dropped".
The page is the Myanmar (M) and VRI (V) page where the candidate paragraph starts.

A finding is a place to check, not an error: the print can carry the same irregularity. s0505m's repeated
274 is in both Burmese editions (VipassanaTech/tipitaka-xml#2723), so the corpus keeps it and CST handles
the offset (fsnow/cst#1087).
"""
import collections, csv, re, sys
from corpus import books, read_xml
from paranum import parse

TOK = re.compile(r'<div\b[^>]*>|<pb\b[^>]*/?>|<p\b[^>]*>.*?</p>', re.S)
PE = "\u092a\u0947\u0970"  # pe + abbreviation sign: a peyyala elides text, and its numbers with it


def strip(s):
    return re.sub(r"\s+", " ", re.sub(r"<[^>]+>", "", s)).strip()


def scan(text):
    """[(segment, kind, prev, next, candidates, pe)] where candidates are [(M page, V page, text)]."""
    out, seg, prev, between, page, pe = [], "(none)", None, [], {}, False
    for m in TOK.finditer(text):
        t = m.group(0)
        if t.startswith("<div"):
            if 'type="book"' in t:
                idm = re.search(r'\bid="([^"]+)"', t)
                seg, prev, between, pe = (idm.group(1) if idm else seg), None, [], False
            continue
        if t.startswith("<pb"):
            ed, n = re.search(r'\bed="(\w)"', t), re.search(r'\bn="([^"]+)"', t)
            if ed and n:
                page[ed.group(1)] = n.group(1)
            continue
        head = t[: t.index(">") + 1]
        pe = pe or PE in t
        start = (page.get("M", "?"), page.get("V", "?"))
        for pm in re.finditer(r'<pb\b[^>]*\bed="(\w)"[^>]*\bn="([^"]+)"', t):
            page[pm.group(1)] = pm.group(2)
        nm = re.search(r'\bn="([^"]*)"', head)
        v = parse(nm.group(1)) if nm else None
        if v is None or v[0] == 0:
            if 'rend="bodytext"' in head and strip(t):
                between.append((start[0], start[1], strip(t)[:70]))
            continue
        a, b = v
        if prev is not None:
            if a == prev and b == prev:
                out.append((seg, "repeated", prev, nm.group(1), [(start[0], start[1], strip(t)[:70])], False))
            elif a <= prev:
                if not (a == 1 or a < prev // 2):
                    out.append((seg, "regress", prev, nm.group(1), [(start[0], start[1], strip(t)[:70])], False))
            elif a > prev + 1:
                kind = "dropped" if between else "peyyala" if pe else "skipped"
                out.append((seg, kind, prev, nm.group(1), list(between) or [(start[0], start[1], strip(t)[:70])], pe))
        prev, between, pe = b, [], PE in t
    return out


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    show_all = "--all" in sys.argv
    csv_out = sys.argv[sys.argv.index("--csv") + 1] if "--csv" in sys.argv else None
    if csv_out in args:
        args.remove(csv_out)
    bl = books()
    numbers = {}

    def nums(fname):
        if fname not in numbers:
            s = set()
            for n in re.findall(r'<p\b[^>]*\bn="([^"]*)"', read_xml(fname)):
                v = parse(n)
                if v:
                    s.update(range(v[0], v[1] + 1))
            numbers[fname] = s
        return numbers[fname]

    rows, counts = [], collections.Counter()
    for b in bl:
        if args and b["file"] not in args:
            continue
        commentary = b["matn"] in ("Atthakatha", "Tika")
        atts = [x["file"] for x in bl if x["mula"] == b["index"] and x["matn"] == "Atthakatha"] \
            if b["matn"] == "Mula" else []
        for seg, kind, prev, nxt, cands, pe in scan(read_xml(b["file"])):
            if (kind == "peyyala" or kind in ("dropped", "skipped") and commentary) and not show_all:
                continue
            lo = prev + 1
            hi = parse(nxt)[0] - 1
            missing = f"{lo}" if lo == hi else f"{lo}-{hi}"
            att = ""
            if kind in ("dropped", "skipped", "peyyala") and atts:
                have = [n for n in range(lo, hi + 1) if any(n in nums(f) for f in atts)]
                att = "yes" if len(have) == hi - lo + 1 else ("part" if have else "no")
            c = cands[-1] if cands else ("", "", "")
            counts[kind] += 1
            rows.append(dict(file=b["file"], matn=b["matn"], segment=seg, kind=kind, prev=prev, next=nxt,
                             missing=missing if kind in ("dropped", "skipped", "peyyala") else "",
                             unnumbered=len(cands) if kind in ("dropped", "peyyala") else 0,
                             att=att, pe="pe" if pe else "", M=c[0], V=c[1], text=c[2]))
    for r in rows:
        print(f"{r['file']:17} {r['kind']:8} {r['prev']:>5} -> {r['next']:<9} missing {r['missing'] or '-':9}"
              f" unnum {r['unnumbered']:<3} att {r['att'] or '-':4} {r['pe'] or '-':2} M{r['M']} V{r['V']}  {r['text']}")
    print("\n" + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))
    if csv_out:
        with open(csv_out, "w", newline="", encoding="utf-8") as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0].keys()) if rows else ["file"])
            w.writeheader()
            w.writerows(rows)


if __name__ == "__main__":
    main()
