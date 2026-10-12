#!/usr/bin/env python3
"""Does a commentary's paragraph number point at the right text? A content test, with a control.

Usage: python3 content.py COMMENTARY BASE [BASE ...]
       python3 content.py --best COMMENTARY BASE [BASE ...]
       python3 content.py --linked        (every pair correlate.py finds, plus the candidates below)
       add --stem to any of these to match on stems

Paragraph numbers alone cannot show a correlation: any increasing run of numbers is a subsequence of a
long enough base, so a monotonic commentary "fits" any base. What can show it is content. A commentary
paragraph numbered n explains words from base paragraph n, and marks them in bold (<hi rend="bold">).

A block is a numbered paragraph plus the unnumbered paragraphs after it. For each commentary block with
bold lemmas, the lemmas are looked up in the base block(s) carrying the same number:
  hit      share of commentary blocks with at least one lemma (4+ letters) found in a same-numbered base block
  control  the same, against the base block 5 numbers on (n+5). A real correlation shows hit >> control.
--best is the stricter test, for bases whose blocks are long and share stock phrases (Udāna, where the
n+5 control hits 89%): for each commentary block, which base block shares the most of its lemmas (6+
letters)? It reports how often that is the block with the same number, against chance (1 / blocks).

--stem matches on the stem instead: each lemma (6+ letters) with its last two characters dropped. A
commentary often cites a word with a different case ending from the base's (fsnow, 2026-10-11), which
exact matching misses. Misses lower hit and control alike, so exact figures are lower bounds.

When the base numbering restarts, a number names several blocks and any of them may hit; the control has
the same advantage, so the comparison stays fair.
"""
import collections, re, sys
from corpus import books, read_xml
from paranum import parse

BLOCK = re.compile(r'<p\b([^>]*)>(.*?)</p>', re.S)
ZW = re.compile('[‌‍ ]')


def plain(s):
    s = re.sub(r'<note>.*?</note>', '', s, flags=re.S)
    s = re.sub(r'<[^>]+>', '', s)
    return ZW.sub('', re.sub(r'\s+', ' ', s))


def blocks(file_name):
    """[(first, last, html)] for each numbered block, unnumbered paragraphs folded into the one before."""
    out = []
    for m in BLOCK.finditer(read_xml(file_name)):
        nm = re.search(r'\bn="([^"]*)"', m.group(1))
        v = parse(nm.group(1)) if nm else None
        if v and v[0] != 0:
            out.append([v[0], v[1], m.group(2)])
        elif out:
            out[-1][2] += ' ' + m.group(2)
    return out


STEM = False


def lemmas(html):
    """Bold words, without the closing quotative. With STEM, each word's last two characters are dropped,
    so a different case ending still matches."""
    out = []
    for b in re.findall(r'<hi rend="bold">(.*?)</hi>', html, re.S):
        w = plain(b).strip(" .,;:'‘’“”–-")
        w = re.sub('(न्ति|ति)$', '', w)  # the quotative -ti that closes a lemma
        w = w.replace(' ', '')
        if STEM:
            if len(w) >= 6:
                out.append(w[:-2])
        elif len(w) >= 4:
            out.append(w)
    return out


def index(files):
    by_n = collections.defaultdict(list)
    for f in files:
        for a, b, html in blocks(f):
            text = plain(html).replace(' ', '')
            for n in range(a, b + 1):
                by_n[n].append(text)
    return by_n


def test(comm, bases, shift=5):
    base = index(bases)
    tried = hit = ctl = 0
    for a, b, html in blocks(comm):
        ls = lemmas(html)
        if not ls:
            continue
        tried += 1
        def found(ns):
            return any(l in t for n in ns for t in base.get(n, ()) for l in ls)
        hit += found(range(a, b + 1))
        ctl += found(range(a + shift, b + shift + 1))
    return tried, hit, ctl


CANDIDATES = [  # unlinked in Books.cs; bases proposed from titles and correlate.py
    ("s0104t.nrf.xml", ["s0101m.mul.xml"]), ("s0105t.nrf.xml", ["s0101m.mul.xml"]),
    ("s0519t.tik.xml", ["s0519m.mul.xml"]), ("s0501t.nrf.xml", ["s0519m.mul.xml"]),
    ("abh01t.tik.xml", ["abh01m.mul.xml"]), ("abh04t.nrf.xml", ["abh01m.mul.xml"]),
    ("abh05t.nrf.xml", [f"abh03m{i}.mul.xml" for i in range(1, 12)]),
    ("vin06t.nrf.xml", ["vin01m.mul.xml", "vin02m1.mul.xml", "vin02m2.mul.xml", "vin02m3.mul.xml", "vin02m4.mul.xml"]),
    ("vin07t.nrf.xml", ["vin01m.mul.xml", "vin02m1.mul.xml", "vin02m2.mul.xml", "vin02m3.mul.xml", "vin02m4.mul.xml"]),
    ("vin09t.nrf.xml", ["vin04t.nrf.xml"]), ("vin11t.nrf.xml", ["vin10t.nrf.xml"]),
    ("vin08t.nrf.xml", ["vin05t.nrf.xml"]), ("abh08t.nrf.xml", ["abh06t.nrf.xml"]),
    ("e0103n.att.xml", ["e0101n.mul.xml"]), ("e0104n.att.xml", ["e0102n.mul.xml"]),
]


def linked():
    bs = books()
    pairs = []
    for c in bs:
        if c["matn"] not in ("Atthakatha", "Tika"):
            continue
        key = "atthakatha" if c["matn"] == "Atthakatha" else "tika"
        g = [b["file"] for b in bs if b["matn"] == "Mula" and b[key] == c["index"]]
        if not g and c["mula"] is not None:
            g = [b["file"] for b in bs if b["index"] == c["mula"]]
        if g:
            pairs.append((c["file"], g))
    return pairs


def best(comm, bases, minlen=6):
    base = index(bases)
    tried = ok = 0
    for a, b, html in blocks(comm):
        ls = {l for l in lemmas(html) if len(l) >= minlen}
        if not ls:
            continue
        tried += 1
        score = {n: sum(any(l in t for t in texts) for l in ls) for n, texts in base.items()}
        top = max(score.values())
        ok += bool(top) and any(score.get(n) == top for n in range(a, b + 1))
    return tried, ok, len(base)


def main():
    global STEM
    args = sys.argv[1:]
    if "--stem" in args:
        STEM = True
        args.remove("--stem")
    if args and args[0] == "--best":
        tried, ok, nb = best(args[1], args[2:])
        print(f"{tried} blocks; the same-numbered base block is the (joint) best match in "
              f"{100 * ok / tried:.0f}% (chance {100 / nb:.1f}%)")
        return
    if args and args[0] == "--linked":
        pairs = [(c, g, "linked") for c, g in linked()] + [(c, g, "candidate") for c, g in CANDIDATES]
    else:
        pairs = [(args[0], args[1:], "")]
    print("| commentary | base | kind | blocks with lemmas | hit | control (n+5) |")
    print("|---|---|---|---:|---:|---:|")
    for c, g, kind in pairs:
        try:
            tried, hit, ctl = test(c, g)
        except FileNotFoundError as e:
            print(f"| {c} | missing {e.filename} |"); continue
        base = g[0] if len(g) == 1 else f"{g[0]} .. {g[-1]} ({len(g)})"
        pct = lambda x: f"{100 * x / tried:.0f}%" if tried else "-"
        print(f"| {c} | {base} | {kind} | {tried} | {pct(hit)} | {pct(ctl)} |")


if __name__ == "__main__":
    main()
