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

# Books.cs CommentaryLevel -> the name the collection goes by.
LEVELS = [("Mula", "M\u016Bla"), ("Atthakatha", "A\u1E6D\u1E6Dhakath\u0101"),
          ("Tika", "\u1E6C\u012Bk\u0101"), ("Other", "A\u00F1\u00F1a (other)")]

PB = re.compile(r'<pb\b[^>]*?\bed="([^"]*)"')

# Devanagari -> Latin, mirroring CST.Conversion.Deva2Latn's table and ScriptConverter's
# ToTitleCase — the pair the book tree itself applies (BookDisplayViewModel.FormatNavPath
# calls ScriptConverter.Convert(..., Script.Latin, toTitleCase: true)). Ported rather than
# invoked because this is a Python script and the mapping is frozen; verified character for
# character against Deva2Latn.ConvertReference over all 217 nav paths.
DEVA2LATN = {
    "\u0902": "\u1E43",                                                   # niggahita
    "\u0905": "a",  "\u0906": "\u0101", "\u0907": "i",  "\u0908": "\u012B",
    "\u0909": "u",  "\u090A": "\u016B", "\u090F": "e",  "\u0910": "ai",
    "\u0913": "o",  "\u0914": "au",                                      # independent vowels
    "\u0915": "k",  "\u0916": "kh", "\u0917": "g",  "\u0918": "gh", "\u0919": "\u1E45",
    "\u091A": "c",  "\u091B": "ch", "\u091C": "j",  "\u091D": "jh", "\u091E": "\u00F1",
    "\u091F": "\u1E6D", "\u0920": "\u1E6Dh", "\u0921": "\u1E0D", "\u0922": "\u1E0Dh",
    "\u0923": "\u1E47",
    "\u0924": "t",  "\u0925": "th", "\u0926": "d",  "\u0927": "dh", "\u0928": "n",
    "\u092A": "p",  "\u092B": "ph", "\u092C": "b",  "\u092D": "bh", "\u092E": "m",
    "\u092F": "y",  "\u0930": "r",  "\u0932": "l",  "\u0935": "v",
    "\u0938": "s",  "\u0939": "h",  "\u0933": "\u1E37",
    "\u093E": "\u0101", "\u093F": "i", "\u0940": "\u012B", "\u0941": "u",
    "\u0942": "\u016B", "\u0947": "e", "\u0948": "ai", "\u094B": "o", "\u094C": "au",
    "\u094D": "",                                                        # virama
    "\u0966": "0", "\u0967": "1", "\u0968": "2", "\u0969": "3", "\u096A": "4",
    "\u096B": "5", "\u096C": "6", "\u096D": "7", "\u096E": "8", "\u096F": "9",
    "\u0970": ".",                                                       # abbreviation sign
    "\u200C": "", "\u200D": "",                                          # ZWNJ, ZWJ
}
INHERENT_A = re.compile("([\u0915-\u0939])([^\u093E-\u094Da])")


def to_latin(deva):
    """Deva2Latn.ConvertReference + ScriptConverter.ToTitleCase, in that order."""
    if not deva:
        return deva
    # Insert the inherent 'a' after any consonant not followed by a vowel sign, virama or 'a'.
    # Twice, and then once at end of string — the C# does exactly this, the repetition standing
    # in for a backtrack it never worked out.
    deva = INHERENT_A.sub(r"\1a\2", deva)
    deva = INHERENT_A.sub(r"\1a\2", deva)
    deva = re.sub("([\u0915-\u0939])$", r"\1a", deva)
    latin = "".join(DEVA2LATN.get(c, c) for c in deva)

    out, last_was_letter = [], False
    for c in latin:
        if c.isalpha():
            if not last_was_letter:
                c = c.upper()
            last_was_letter = True
        else:
            last_was_letter = False
        out.append(c)
    return "".join(out)


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
        meta["nav"] = meta["short"] or meta["long"] or ""
        meta["latin"] = to_latin(meta["nav"]) or "—"
        meta["nav"] = meta["nav"] or "—"
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
    navs = {books.get(fn, {}).get("latin", "").split("/")[0] for fn in o_only}
    # Name the collection from the data rather than from prose: the nav paths read Sihaḷa,
    # and a tidied spelling here would not match the table below.
    colls = {books.get(fn, {}).get("latin", "").split("/")[1]
             for fn in o_only if books.get(fn, {}).get("latin", "").count("/") >= 1}
    coll = colls.pop() if len(colls) == 1 else "one collection"
    w(f"**`O` is one edition's pagination, not a miscellany.** All {len(o_only)} books using it use")
    w(f"*nothing else*, and no book combines it with another system. They are a contiguous block,")
    w(f"`{first}`–`{last}` — the **{coll}** collection: Sinhalese texts paginated to a Sri Lankan")
    w("printing with no counterpart among the VRI, Myanmar, PTS or Thai editions.")
    w("")
    w("That is why a reader never meets `O` in ordinary use: it appears only inside that collection,")
    w("and never as an alternative to a system they already had.")
    if len(navs) == 1:
        w("")
        w(f"All of them sit under the same top-level nav node: **{to_latin(navs.pop())}**.")
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
        w(f"| `{fn}` | {books.get(fn, {}).get('latin', '—')} |")
    w("")
    w("**Expected, not a gap in the XML.** These are `e*` texts, and the VRI printed set does not")
    w("extend to them \u2014 so there is no printed pagination for them to carry, in any edition. The same")
    w("reason accounts for the thin coverage across A\u00f1\u00f1a generally. **[fsnow]**")
else:
    w("Every book carries at least one page-numbering system.")
w("")

# What PageNumbering.DefaultType lands on, book by book — the same walk it does, so the
# precedence order can be judged against the corpus rather than assumed.
defaults = collections.Counter()
for fn, counts in found.items():
    defaults[next((c for c in CODES if counts.get(c)), "Paragraph")] += 1

# Per commentary level: how the five systems land across Mula / Atthakatha / Tika / Anya.
by_level = {key: collections.Counter() for key, _ in LEVELS}
level_totals = collections.Counter()
level_defaults = {key: collections.Counter() for key, _ in LEVELS}
unlevelled = []
for fn, counts in found.items():
    key = books.get(fn, {}).get("matn", "")
    if key not in by_level:
        unlevelled.append(fn)
        continue
    level_totals[key] += 1
    for c in CODES:
        if counts.get(c):
            by_level[key][c] += 1
    level_defaults[key][next((c for c in CODES if counts.get(c)), "Paragraph")] += 1

w("## By commentary level")
w("")
w("The four levels the tree groups by, and how the editions fall across them. The pattern is not")
w("uniform, which is the point: a system's overall share says little about the books a given")
w("reader actually opens.")
w("")
w("| level | books | " + " | ".join(f"`{c}`" for c in CODES) + " |")
w("|---|---:|" + "---:|" * len(CODES))
for key, name in LEVELS:
    total = level_totals[key]
    if not total:
        continue
    cells = " | ".join(
        (f"{by_level[key][c]} ({by_level[key][c] * 100 // total}%)" if by_level[key][c] else "\u00b7")
        for c in CODES)
    w(f"| {name} | {total} | {cells} |")
w("")
if unlevelled:
    w(f"({len(unlevelled)} books carry no `CommentaryLevel`: "
      + ", ".join(f"`{f}`" for f in sorted(unlevelled)) + ".)")
    w("")
w("And what a book of each level opens on by default, walking the same precedence `DefaultType`")
w("does:")
w("")
w("| level | " + " | ".join(n for _, n in EDITIONS) + " | Paragraph |")
w("|---|" + "---:|" * (len(EDITIONS) + 1))
for key, name in LEVELS:
    if not level_totals[key]:
        continue
    cells = " | ".join(str(level_defaults[key][c]) if level_defaults[key][c] else "\u00b7"
                       for c in CODES)
    para = level_defaults[key]["Paragraph"]
    para_cell = str(para) if para else "\u00b7"
    w(f"| {name} | {cells} | {para_cell} |")
w("")

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
w("| # | file | level | " + " | ".join(f"`{c}`" for c in CODES) + " | nav path |")
w("|---:|---|---|" + "---:|" * len(CODES) + "---|")
for fn, meta in books.items():
    lvl = dict(LEVELS).get(meta.get("matn", ""), meta.get("matn") or "?")
    counts = found.get(fn)
    if counts is None:
        cells = " | ".join("—" for _ in CODES)
        w(f"| {meta.get('index', '')} | `{fn}` | {lvl} | {cells} | {meta['latin']} *(no XML)* |")
        continue
    cells = " | ".join(str(counts[c]) if counts.get(c) else "·" for c in CODES)
    w(f"| {meta.get('index', '')} | `{fn}` | {lvl} | {cells} | {meta['latin']} |")
w("")

open(OUT, "w", encoding="utf-8").write("\n".join(o) + "\n")
print(f"Wrote {OUT}: {scanned} files scanned, "
      + ", ".join(f"{c}={per_edition[c]}" for c in CODES))
