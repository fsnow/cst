#!/usr/bin/env python3
"""Profile the 2007-08 structural markup: which state each file is in, and where it departs from the
plain pattern  <div><head>…</head> … <trailer>…</trailer></div>.

Usage: python3 markup_profile.py [--files]

States:
  unmarked         no <div>, <head> or <trailer>. Headings are <p rend="book|chapter|title">, colophons
                   are <p rend="centre">.
  book-only        the only divs are type="book" (with their <head rend="book">); the sections inside are
                   still <p rend="chapter|title"> (the AN ṭīkās s0401t-s0404t).
  partial          has inner divs, but some <trailer>s do not close a div: headings and colophons were
                   converted while the inner divs were not added (s0404a, AN9-11).
  marked           inner divs, and every <trailer> is the last element of a div.
A marked file with no trailers at all is reported as marked; --files lists them.
"""
import collections, re, sys
from corpus import books, read_xml

DIV_OPEN = re.compile(r'<div\b([^>]*)>\s*(<(\w+)[^>]*>)')


def profile(text):
    divs = len(re.findall(r"<div\b", text))
    trailers = len(re.findall(r"<trailer\b", text))
    closing = len(re.findall(r"</trailer>\s*</div>", text))
    no_head = []
    for m in DIV_OPEN.finditer(text):
        if m.group(3) != "head":
            idm = re.search(r'\bid="([^"]+)"', m.group(1))
            no_head.append((idm.group(1) if idm else "?", m.group(2)[:40]))
    types = set(re.findall(r'<div\b[^>]*\btype="([^"]+)"', text))
    if divs == 0:
        state = "unmarked"
    elif types == {"book"}:
        state = "book-only"
    elif trailers != closing:
        state = "partial"
    else:
        state = "marked"
    return dict(divs=divs, heads=len(re.findall(r"<head\b", text)), trailers=trailers,
                trailers_not_closing=trailers - closing, no_head=no_head, state=state)


def id_problems(text):
    """The id scheme: a div's id is its parent's id + "_" + ordinal, and n repeats the id.
    Returns (id, problem) for every duplicate id, id that does not extend its parent's, and n != id."""
    out, stack, seen = [], [], set()
    for m in re.finditer(r"<div\b([^>]*)>|</div>", text):
        if m.group(0) == "</div>":
            stack.pop()
            continue
        id_ = re.search(r'\bid="([^"]+)"', m.group(1)).group(1)
        n = re.search(r'\bn="([^"]+)"', m.group(1)).group(1)
        if id_ in seen:
            out.append((id_, "duplicate id"))
        if n != id_:
            out.append((id_, f'n="{n}"'))
        if id_.count("_") != len(stack) or (stack and not id_.startswith(stack[-1] + "_")):
            out.append((id_, f"does not extend parent {stack[-1] if stack else None}"))
        seen.add(id_)
        stack.append(id_)
    return out


def main():
    rows = [(b, profile(read_xml(b["file"]))) for b in books()]
    states = collections.Counter(p["state"] for _, p in rows)
    print("States:", dict(states))
    unmarked_with_markup = [b["file"] for b, p in rows if p["state"] == "unmarked" and (p["heads"] or p["trailers"])]
    print("Unmarked files that nevertheless have <head>/<trailer>:", unmarked_with_markup or "none")
    print("Files with divs but no <trailer>:",
          [b["file"] for b, p in rows if p["divs"] and not p["trailers"]])
    for b, p in rows:
        if p["state"] == "partial":
            print(f"partial: {b['file']}: {p['trailers_not_closing']} of {p['trailers']} trailers do not close a div")
    nh = [(b["file"], i, e) for b, p in rows for i, e in p["no_head"]]
    print(f"\nDivs whose first child is not <head>: {len(nh)}")
    by_kind = collections.Counter(e.split(">")[0] + ">" for _, _, e in nh)
    for k, v in by_kind.most_common():
        print(f"  {v:3}  {k}")
    for f, i, e in nh:
        print(f"  {f:18} {i:12} {e}")
    print("\nId-scheme problems:")
    for b, _ in rows:
        for id_, problem in id_problems(read_xml(b["file"])):
            print(f"  {b['file']:18} {id_:12} {problem}")
    if "--files" in sys.argv:
        for st in ("marked", "partial", "book-only", "unmarked"):
            fs = [b["file"] for b, p in rows if p["state"] == st]
            print(f"\n{st} ({len(fs)}): " + ", ".join(fs))


if __name__ == "__main__":
    main()
