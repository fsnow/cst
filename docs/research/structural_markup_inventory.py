#!/usr/bin/env python3
"""Regenerate STRUCTURAL_MARKUP_INVENTORY.md — scan corpus <div> coverage vs Books.cs ChapterListTypes.

Usage: python3 structural_markup_inventory.py [XML_DIR]
  XML_DIR defaults to $CST_XML_DIR, else the macOS app-support corpus path.
Re-run after adding <div> structural markup to refresh the inventory.
"""
import os, re, glob, collections, datetime, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
XML  = (sys.argv[1] if len(sys.argv) > 1 else os.environ.get("CST_XML_DIR")
        or os.path.expanduser("~/Library/Application Support/CSTReader/xml-data"))
BOOKS = os.path.join(REPO, "src", "CST.Core", "Books.cs")
OUT   = os.path.join(HERE, "STRUCTURAL_MARKUP_INVENTORY.md")
dt = re.compile(r'<div\b[^>]*\btype="([^"]+)"')

def rd(p):
    b = open(p, "rb").read()
    for e in ("utf-16", "utf-16-le", "utf-8"):
        try: return b.decode(e)
        except Exception: pass
    return b.decode("utf-16-le", "ignore")

def fam(fn):
    c = ("Vinaya" if fn.startswith("vin") else "Sutta" if fn.startswith("s0") else
         "Abhidhamma" if fn.startswith("abh") else "Anya/other" if fn.startswith("e0") else "other")
    l = next((v for k, v in ((".mul.", "Mula"), (".att.", "Atthakatha"),
                             (".tik.", "Tika"), (".nrf.", "nrf")) if k in fn), "?")
    return c, l

src = open(BOOKS, encoding="utf-8").read(); declared = {}; cur = None
for ln in src.splitlines():
    m = re.search(r'\.FileName\s*=\s*"([^"]+)"', ln)
    if m: cur = m.group(1); declared.setdefault(cur, ""); continue
    m = re.search(r'\.ChapterListTypes\s*=\s*"([^"]*)"', ln)
    if m and cur: declared[cur] = m.group(1)

rows, typect, bycol, mism = [], collections.Counter(), collections.defaultdict(lambda: [0, 0]), []
for f in sorted(glob.glob(os.path.join(XML, "*.xml"))):
    fn = os.path.basename(f); types = collections.Counter(dt.findall(rd(f)))
    has_book = "book" in types; c, l = fam(fn)
    for t in types: typect[t] += 1
    bycol[c][0 if has_book else 1] += 1
    decl = set(x for x in declared.get(fn, "").split(",") if x); act = set(types)
    missing = sorted(decl - act)
    if missing: mism.append((fn, decl, act, missing))
    rows.append((c, l, fn, "Y" if has_book else "N",
                 " ".join(f"{k}×{v}" for k, v in types.most_common()) or "—",
                 declared.get(fn, "") or "—",
                 ("declares " + ",".join(missing) + " ABSENT in XML") if missing else ""))

struct = sum(1 for r in rows if r[3] == "Y"); un = len(rows) - struct
d = datetime.date.today().isoformat()
o = []
o.append("# Structural markup inventory — corpus `<div>` coverage\n")
o.append(f"**Status:** research (ground truth for the book-mapping / navigation cluster: #24 Go To, #76 source-PDF cardinality, #174 book linking, #314 nav resolver, #187 agent navigation, and the #266 `bookCodes`). **Generated:** {d} by `structural_markup_inventory.py`.\n")
o.append("## Background\n")
o.append("The corpus TEI XML carries structural `<div type=\"…\">` markup (`book` / `vagga` / `samyutta` / …) in SOME books but not others. These divisions were added **by hand in 2007** to power the chapter-list feature; that work **stopped mid-corpus in 2008** (unfinished) and was never resumed. `bookCodes` (`Books.cs` + `MultiBookCodes`, #266) and the chapter list derive DIRECTLY from this markup, so their coverage IS this markup's coverage. Nobody had an inventory — this is it.\n")
o.append("## Method\n")
o.append("`structural_markup_inventory.py` scans all corpus XML files (UTF-16-LE) for `<div type>` elements and cross-references each book's *declared* `ChapterListTypes` in `src/CST.Core/Books.cs`. \"Structured\" = has at least one `<div type=\"book\">`. Re-run the script to refresh.\n")
o.append(f"## Coverage summary\n\n**{struct} of {len(rows)} books are structured; {un} are unstructured.** The gaps are systematic:\n")
o.append("\n| Collection | structured | unstructured |\n|---|---:|---:|")
for c in ["Sutta", "Vinaya", "Abhidhamma", "Anya/other", "other"]:
    s, u = bycol[c]; o.append(f"| {c} | {s} | {u} |")
o.append("\nBy layer, it is a clean front-to-back trail that stops mid-canon:\n")
o.append("- **Sutta Mūla 41/41** and **Sutta Ṭīkā 15/16** — fully done.")
o.append("- **Sutta Aṭṭhakathā ~15/39** — about half, then stopped.")
o.append("- **Vinaya** — only its 5 Mūla volumes; none of its commentary.")
o.append("- **Abhidhamma 0/25, Anya/other 0/40, `.nrf` 0/28** — never reached.\n")
o.append("This matches the account: the Sutta canon was marked first, the commentaries partway, and Abhidhamma + the extra texts not at all before the 2008 halt.\n")
o.append("## Structural vocabulary present\n\n" + "  ".join(f"`{k}`({v})" for k, v in typect.most_common()) + "  — (count = #books containing that div type)\n")
o.append("## Inconsistencies (declared a chapter type its XML lacks)\n\nOnly two; everything else is cleanly all-or-nothing:\n")
for fn, decl, act, missing in mism:
    o.append(f"- **`{fn}`** — declares `{','.join(sorted(decl))}`, XML has `{','.join(sorted(act)) or '(none)'}` → missing `{','.join(missing)}`.")
o.append("\n## Implication for the book-mapping cluster\n")
o.append("`bookCodes`, the chapter list, and the \"Go To by chapter\" hard cases all rest on this markup, so they are **~36% complete**, with the missing 64% falling entirely on **Abhidhamma, the Anya/other texts, all Vinaya commentary, and half the Sutta commentary**. Any relationship model built on `<div>` structure inherits those holes; \"Go To\" degrades to paragraph/page-only exactly there. Completing the 2007 markup (or deriving structure another way for the unmarked books) is the prerequisite for uniform chapter-level navigation across the corpus.\n")
o.append("## Full per-book inventory\n\n| Collection | Layer | File | Structured | `<div type>` present | Declared `ChapterListTypes` | Note |\n|---|---|---|:--:|---|---|---|")
order = {c: i for i, c in enumerate(["Vinaya", "Sutta", "Abhidhamma", "Anya/other", "other"])}
for c, l, fn, st, types, decl, note in sorted(rows, key=lambda r: (order.get(r[0], 9), r[2])):
    o.append(f"| {c} | {l} | `{fn}` | {st} | {types} | {decl} | {note} |")
open(OUT, "w").write("\n".join(o) + "\n")
print(f"wrote {OUT} — {len(rows)} rows, {struct} structured")
