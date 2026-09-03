#!/usr/bin/env python3
"""Regenerate PAGE_NUMBERING_BY_BOOK.md — which print editions each corpus book is paginated to.

Usage: python3 page_numbering_by_book.py [XML_DIR]
  XML_DIR defaults to $CST_XML_DIR, else the macOS app-support corpus path.

The answer lives in the XML as <pb ed="…"/> and nowhere else, so this scans rather than
records: a hand-maintained copy would drift the first time VRI reissues a book. Re-run it
after a corpus update. (#845)
"""
import collections, datetime, glob, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
XML = (sys.argv[1] if len(sys.argv) > 1 else os.environ.get("CST_XML_DIR")
       or os.path.expanduser("~/Library/Application Support/CSTReader/xml"))
BOOKS = os.path.join(REPO, "src", "CST.Core", "Books.cs")
OUT = os.path.join(HERE, "PAGE_NUMBERING_BY_BOOK.md")

# The five ed= values PageNumbering knows, in its own precedence order — the order the status
# bar and Go To offer them in, so the table reads in the same order the app does.
EDITIONS = [("V", "VRI"), ("M", "Myanmar"), ("P", "PTS"), ("T", "Thai"), ("O", "Other")]
CODES = [c for c, _ in EDITIONS]

PB = re.compile(r'<pb\b[^>]*?\bed="([^"]*)"')


def read(path):
    """The corpus is UTF-16-LE. Decode before matching — byte-level regex is unreliable here."""
    raw = open(path, "rb").read()
    for enc in ("utf-16", "utf-16-le", "utf-8"):
        try:
            return raw.decode(enc)
        except Exception:
            pass
    return raw.decode("utf-16-le", "ignore")


def parse_books(src_path):
    """FileName -> {index, nav, pitaka, matn}, in Books.cs declaration order.

    Both nav paths are captured. The Anya books assign `ShortNavPath = book.LongNavPath`
    rather than a literal, so a short-path-only reader leaves all fifteen `O` books — the
    ones this table exists to explain — with a blank nav column.
    """
    books, cur, index = collections.OrderedDict(), None, None
    for line in open(src_path, encoding="utf-8").read().splitlines():
        m = re.search(r'\.Index\s*=\s*(\d+)', line)
        if m:
            index = int(m.group(1))
            continue
        m = re.search(r'\.FileName\s*=\s*"([^"]+)"', line)
        if m:
            cur = m.group(1)
            books[cur] = {"file": cur, "index": index, "long": "", "short": "",
                          "pitaka": "", "matn": ""}
            index = None      # consumed; a book without its own Index must not inherit one
            continue
        if not cur:
            continue
        for attr, key in (("LongNavPath", "long"), ("ShortNavPath", "short")):
            m = re.search(r'\.' + attr + r'\s*=\s*"([^"]+)"', line)
            if m:
                books[cur][key] = m.group(1)
        m = re.search(r'\.Pitaka\s*=\s*Pitaka\.(\w+)', line)
        if m:
            books[cur]["pitaka"] = m.group(1)
        m = re.search(r'\.Matn\s*=\s*CommentaryLevel\.(\w+)', line)
        if m:
            books[cur]["matn"] = m.group(1)
    for meta in books.values():
        meta["nav"] = meta["short"] or meta["long"] or "—"
    return books


books = parse_books(BOOKS)
found = {}
for path in sorted(glob.glob(os.path.join(XML, "*.xml"))):
    found[os.path.basename(path)] = collections.Counter(PB.findall(read(path)))

missing_xml = [f for f in books if f not in found]
unlisted = [f for f in found if f not in books]
unknown_codes = sorted({c for cts in found.values() for c in cts} - set(CODES))

# Per-edition and per-combination tallies, over the files that exist.
per_edition = collections.Counter()
combos = collections.Counter()
for fn, counts in found.items():
    present = [c for c in CODES if counts.get(c)]
    for c in present:
        per_edition[c] += 1
    combos[" · ".join(present) if present else "none at all"] += 1

scanned = len(found)
today = datetime.date.today().isoformat()
o = []
w = o.append

w("# Page numbering by book")
w("")
w("Which print editions each corpus book carries page breaks for, and therefore which numbering")
w("systems **Go To** can offer (#844), which the status bar can show (#541), and which page a")
w("passage can be cited by.")
w("")
w("> **Generated, not maintained.** `python3 docs/reference/page_numbering_by_book.py` scans")
w(f"> `<pb ed=\"…\"/>` across the corpus. Re-run it after a corpus update rather than editing below.")
w(f"> Figures here are from **{today}**, over **{scanned}** XML files. (#845)")
w("")
w("## The five editions")
w("")
w("| code | edition | books carrying it | share |")
w("|---|---|---:|---:|")
for code, name in EDITIONS:
    n = per_edition[code]
    w(f"| `{code}` | {name} | {n} | {n * 100 // scanned}% |")
w("")
w("## Which combinations occur")
w("")
w("| systems present | books |")
w("|---|---:|")
for combo, n in sorted(combos.items(), key=lambda kv: (-kv[1], kv[0])):
    w(f"| {combo} | {n} |")
w("")

o_only = sorted(fn for fn, c in found.items()
                if c.get("O") and not any(c.get(x) for x in ("V", "M", "P", "T")))
o_with_others = sorted(fn for fn, c in found.items()
                       if c.get("O") and any(c.get(x) for x in ("V", "M", "P", "T")))
none_at_all = sorted(fn for fn, c in found.items() if not any(c.get(x) for x in CODES))

w("### What \"Other\" is")
w("")
if o_only and not o_with_others:
    first, last = o_only[0], o_only[-1]
    navs = {books.get(fn, {}).get("nav", "").split("/")[0] for fn in o_only}
    w(f"**`O` is one edition's pagination, not a miscellany.** All {len(o_only)} books using it use")
    w(f"*nothing else*, and no book combines it with another system. They are a contiguous block,")
    w(f"`{first}`–`{last}` — the Sīhaḷa-gantha-saṅgaho collection, Sinhalese texts paginated to a Sri")
    w("Lankan printing with no counterpart among the VRI, Myanmar, PTS or Thai editions.")
    w("")
    w("That is why a reader never meets `O` in ordinary use: it appears only inside that collection,")
    w("and never as an alternative to a system they already had.")
    if len(navs) == 1:
        w("")
        w(f"All of them sit under the same top-level nav node: `{navs.pop()}`.")
else:
    w(f"{len(o_only)} books use `O` alone; {len(o_with_others)} combine it with another system.")
w("")
w("### Books with no page markers at all")
w("")
if none_at_all:
    w(f"{len(none_at_all)} books carry no `<pb>` element of any edition. **Go To can offer only")
    w("Paragraph for these, and a passage in them has no page reference to cite.**")
    w("")
    w("| file | nav path |")
    w("|---|---|")
    for fn in none_at_all:
        w(f"| `{fn}` | {books.get(fn, {}).get('nav', '—')} |")
    w("")
    w("Worth confirming this is a property of the printed sources rather than a gap in the XML.")
else:
    w("Every book carries at least one page-numbering system.")
w("")

# What PageNumbering.DefaultType lands on, book by book — the same walk it does, so the
# precedence order can be judged against the corpus rather than assumed.
defaults = collections.Counter()
for fn, counts in found.items():
    defaults[next((c for c in CODES if counts.get(c)), "Paragraph")] += 1

w("## What this means for the app")
w("")
w("### How often a preferred system is unavailable (#844)")
w("")
w("Go To remembers the system the reader last navigated with and falls back per book when that")
w("book does not carry it. This is how often each preference falls back:")
w("")
w("| a reader who prefers | falls back in | of 217 |")
w("|---|---:|---:|")
for code, name in EDITIONS:
    miss = scanned - per_edition[code]
    w(f"| {name} (`{code}`) | {miss} books | {miss * 100 // scanned}% |")
w("")
w("So the fallback is not an edge case for anyone but a Myanmar reader, and it is the majority")
w("case for PTS and Thai. A preference that were overwritten by its own fallback would be")
w("destroyed within a few books — which is why #844 records the preference only when the reader")
w("navigates, never when a fallback is merely displayed.")
w("")
w("### What the default precedence lands on (#541)")
w("")
w("`PageNumbering.Order` is " + " → ".join(f"{n} (`{c}`)" for c, n in EDITIONS) + ", and")
w("`DefaultType` returns the first of those a book carries. Across the corpus that resolves to:")
w("")
w("| default for a book with no preference | books |")
w("|---|---:|")
for code, name in EDITIONS:
    if defaults[code]:
        w(f"| {name} (`{code}`) | {defaults[code]} |")
if defaults["Paragraph"]:
    w(f"| Paragraph — no page system at all | {defaults['Paragraph']} |")
w("")
w("VRI first means most books open on VRI numbering even though Myanmar is the more widely")
w("carried system; the books that fall through to Myanmar are the ones VRI did not paginate.")
w("")

if missing_xml or unlisted or unknown_codes:
    w("### Drift between Books.cs and the corpus")
    w("")
    if missing_xml:
        w(f"**{len(missing_xml)} declared in `Books.cs` with no XML file:** "
          + ", ".join(f"`{f}`" for f in missing_xml))
    if unlisted:
        w(f"**{len(unlisted)} XML files not declared in `Books.cs`:** "
          + ", ".join(f"`{f}`" for f in unlisted))
    if unknown_codes:
        w(f"**`ed=` values `PageNumbering` does not know:** "
          + ", ".join(f"`{c}`" for c in unknown_codes))
    w("")
else:
    w("Every book in `Books.cs` has an XML file, every XML file is declared, and no `<pb>` carries")
    w("an `ed=` value outside the five above.")
    w("")

w("## Every book")
w("")
w("Book order is `Books.cs` order — the order the tree presents them. A `·` means the book carries")
w("no page breaks for that edition; the number is how many it carries.")
w("")
w("| # | file | " + " | ".join(f"`{c}`" for c in CODES) + " | nav path |")
w("|---:|---|" + "---:|" * len(CODES) + "---|")
for fn, meta in books.items():
    counts = found.get(fn)
    if counts is None:
        cells = " | ".join("—" for _ in CODES)
        w(f"| {meta.get('index', '')} | `{fn}` | {cells} | {meta['nav']} *(no XML)* |")
        continue
    cells = " | ".join(str(counts[c]) if counts.get(c) else "·" for c in CODES)
    w(f"| {meta.get('index', '')} | `{fn}` | {cells} | {meta['nav']} |")
w("")

open(OUT, "w", encoding="utf-8").write("\n".join(o) + "\n")
print(f"Wrote {OUT}: {scanned} files scanned, "
      + ", ".join(f"{c}={per_edition[c]}" for c in CODES))
