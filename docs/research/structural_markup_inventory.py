#!/usr/bin/env python3
"""Regenerate STRUCTURAL_MARKUP_INVENTORY.md: the state of the structural <div> markup in every book, and how
it compares with each book's ChapterListTypes in Books.cs.

Usage:
  python3 structural_markup_inventory.py [--xml-rev REV] [--books-rev REV]

  --xml-rev REV    read "deva master" at REV in the tipitaka-xml clone ($TIPITAKA_XML_REPO, default
                   ~/github/VipassanaTech/tipitaka-xml), e.g. a PR's commit. Without it, the corpus is read
                   from $CST_XML_DIR or the app's download, as the other book-relationships scripts do.
  --books-rev REV  read src/CST.Core/Books.cs at REV in this repo (e.g. a PR branch). Default: the working tree.

The states and the canonical / añña division come from book-relationships/scripts (markup_profile.profile and
corpus.books), so this inventory and those measurements cannot disagree. Every number below is computed.
"""
import argparse, collections, datetime, os, re, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(HERE, "STRUCTURAL_MARKUP_INVENTORY.md")
sys.path.insert(0, os.path.join(HERE, "book-relationships", "scripts"))
import corpus                      # noqa: E402
from markup_profile import profile  # noqa: E402

TX_REPO = os.environ.get("TIPITAKA_XML_REPO") or os.path.expanduser("~/github/VipassanaTech/tipitaka-xml")
STATES = ("marked", "partial", "book-only", "unmarked")
NIKAYA = {"s01": "DN", "s02": "MN", "s03": "SN", "s04": "AN", "s05": "KN"}
LAYERS = ("Mula", "Atthakatha", "Tika")
LAYER_NAME = {"Mula": "mūla", "Atthakatha": "aṭṭhakathā", "Tika": "ṭīkā"}


def git(repo, *args):
    return subprocess.run(["git", "-C", repo, *args], check=True, capture_output=True).stdout


def group(b):
    if not b["canonical"]:
        return "añña"
    if b["pitaka"] == "Sutta":
        return NIKAYA.get(b["file"][:3], "Sutta")
    return b["pitaka"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--xml-rev")
    ap.add_argument("--books-rev")
    a = ap.parse_args()

    tmp = tempfile.mkdtemp()
    if a.books_rev:
        corpus.BOOKS_CS = os.path.join(tmp, "Books.cs")
        open(corpus.BOOKS_CS, "wb").write(git(REPO, "show", f"{a.books_rev}:src/CST.Core/Books.cs"))
    books = corpus.books()
    if a.xml_rev:
        corpus.XML_DIR = os.path.join(tmp, "xml")
        os.makedirs(corpus.XML_DIR)
        for b in books:
            open(os.path.join(corpus.XML_DIR, b["file"]), "wb").write(
                git(TX_REPO, "show", f"{a.xml_rev}:deva master/{b['file']}"))

    rows = []
    for b in books:
        text = corpus.read_xml(b["file"])
        p = profile(text)
        types = collections.Counter(re.findall(r'<div\b[^>]*\btype="([^"]+)"', text))
        declared = {t for t in b["chapter_list_types"].split(",") if t}
        present = set(types)
        rows.append(dict(b=b, state=p["state"], types=types, group=group(b),
                         missing=sorted(declared - present),
                         unlisted=sorted((present - {"book"}) - declared) if declared or present - {"book"} else []))

    def source(rev, what):
        return f"{what} at `{rev}`" if rev else f"{what} (working copy)"
    today = datetime.date.today().isoformat()
    o = []
    o.append("# Structural markup inventory: corpus `<div>` coverage\n")
    o.append(f"**Status:** research, part of the book-relationships epic (#422). **Generated:** {today} by "
             f"`structural_markup_inventory.py`, from {source(a.xml_rev, 'tipitaka-xml `deva master`')} and "
             f"{source(a.books_rev, '`Books.cs`')}.\n")
    o.append("Related: [book-relationships/markup-conventions.md](book-relationships/markup-conventions.md) "
             "describes the markup and records the decisions for adding it; "
             "[book-relationships/README.md](book-relationships/README.md) is the epic's working doc.\n")

    o.append("## Background\n")
    o.append("[fsnow, 2026-10-09] \"The structural markup that is there was done by me in 2007 - 2008, manually, "
             "and stopped arbitrarily when I ran out of time to develop CST4.\" The work resumed in October 2026, "
             "starting with the Aṅguttara ṭīkās (VipassanaTech/tipitaka-xml#2729).\n")
    o.append("[observed] The chapter list (`ChapterListsService`) shows the divs whose type is in the book's "
             "`ChapterListTypes`, and `MultiBookCodes` (#266) reads a Multi book's sub-book codes from its book "
             "divs. Both have only as much to work with as the markup provides.\n")

    o.append("## Method\n")
    o.append("Each of the 217 books in `Books.cs` is read and given one of four states, by "
             "`markup_profile.profile` (book-relationships/scripts):\n")
    o.append("| State | Meaning |\n|---|---|")
    o.append("| marked | inner section divs, and every `<trailer>` closes a div |")
    o.append("| partial | inner divs, but some `<trailer>`s close nothing: headings and colophons were converted "
             "without all their divs |")
    o.append("| book-only | the only divs are `type=\"book\"` |")
    o.append("| unmarked | no divs |\n")
    o.append("Books are grouped by `Matn` in `Books.cs`, never by file extension: 21 canonical files are `.nrf`. "
             "Canonical books are split by piṭaka, and the Sutta piṭaka by nikāya; `Matn = Other` is añña.\n")

    o.append("## Summary\n")
    st = collections.Counter(r["state"] for r in rows)
    o.append(f"**{st['marked']} of {len(rows)} books are marked**" +
             "".join(f", {st[s]} {s}" for s in STATES[1:] if st[s]) + ".\n")
    order = ["DN", "MN", "SN", "AN", "KN", "Vinaya", "Abhidhamma"]
    o.append("| Group | Layer | " + " | ".join(STATES) + " | total |\n|---|---|" + "---:|" * (len(STATES) + 1))
    for g in order:
        for layer in LAYERS:
            rs = [r for r in rows if r["group"] == g and r["b"]["matn"] == layer]
            if not rs: continue
            c = collections.Counter(r["state"] for r in rs)
            o.append(f"| {g} | {LAYER_NAME[layer]} | " + " | ".join(str(c[s]) for s in STATES) + f" | {len(rs)} |")
    rs = [r for r in rows if r["group"] == "añña"]
    c = collections.Counter(r["state"] for r in rs)
    o.append("| añña | | " + " | ".join(str(c[s]) for s in STATES) + f" | {len(rs)} |\n")

    o.append("## Div types present\n")
    typect = collections.Counter(t for r in rows for t in r["types"])
    o.append("  ".join(f"`{k}` ({v})" for k, v in typect.most_common()) + " (number of books with that type)\n")

    o.append("## Mismatches with `ChapterListTypes`\n")
    miss = [r for r in rows if r["missing"]]
    unl = [r for r in rows if r["unlisted"]]
    extra = [r for r in miss if r["state"] != "unmarked"]
    placeholder = [r for r in miss if r["state"] == "unmarked"]
    o.append("**Declared but absent:** the book's `ChapterListTypes` names a type its XML has no div of. Harmless "
             "either way, but the two kinds mean different things.\n")
    o.append("*Marked books that declare a type they lack.* The book has sections; the extra type has none to "
             "list.\n")
    o.extend([f"- `{r['b']['file']}`: declares `{r['b']['chapter_list_types']}`, lacks `{','.join(r['missing'])}`"
              for r in extra] or ["- none"])
    o.append("\n*Unmarked books with a placeholder list.* The book has no divs yet, but `ChapterListTypes` already "
             "names the types its markup is expected to have, so its chapter list will appear once it is marked.\n")
    o.extend([f"- `{r['b']['file']}`: `{r['b']['chapter_list_types']}`" for r in placeholder] or ["- none"])
    o.append("\n**Present but not listed:** the XML has sections of a type its `ChapterListTypes` leaves out, so "
             "the chapter list skips them.\n")
    o.extend([f"- `{r['b']['file']}`: `{','.join(r['unlisted'])}`" for r in unl] or ["- none"])
    o.append("")

    o.append("## Priorities\n")
    o.append("Not every unmarked book is meant to be marked. The epic's goal 6 (#422, 2026-07-31) is chapter "
             "lists \"wherever that is correct and relevant\", and ranks the añña books \"likely never, and not "
             "merely 'later'\"; the remaining canonical commentaries are \"to be judged on how much they are "
             "actually cited\". The order of work is in "
             "[markup-conventions.md](book-relationships/markup-conventions.md#adding-markup).\n")

    o.append("## Per-book inventory\n")
    o.append("| Group | Layer | File | State | `<div>` types | `ChapterListTypes` |\n|---|---|---|---|---|---|")
    for r in sorted(rows, key=lambda r: ((order + ["añña"]).index(r["group"]), r["b"]["index"])):
        b = r["b"]
        o.append(f"| {r['group']} | {LAYER_NAME.get(b['matn'], '')} | `{b['file']}` | {r['state']} | "
                 + (" ".join(f"{k}×{v}" for k, v in r["types"].most_common()) or "—")
                 + f" | {b['chapter_list_types'] or '—'} |")
    open(OUT, "w", encoding="utf-8").write("\n".join(o) + "\n")
    print(f"wrote {OUT}: " + ", ".join(f"{s} {st[s]}" for s in STATES))


if __name__ == "__main__":
    main()
