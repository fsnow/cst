#!/usr/bin/env python3
"""Print each paragraph-number restart in the restarting files, with the Myanmar page it falls on
and whether a <div> opened since the previous numbered paragraph.

Usage: python3 restarts.py [--all] [FILE ...]
Without --all, files with more than 4 restarts are summarised on one line.
"""
import re, sys
from corpus import books, read_xml
from paranum import classify

TOK = re.compile(r'<div\b[^>]*>|<pb\b[^>]*\bed="M"[^>]*\bn="([^"]+)"[^>]*/?>|<p\b[^>]*\bn="(\d+)(?:-\d+)?"[^>]*>')


def restarts(text):
    prev, page, div_since, out = None, "?", False, []
    for m in TOK.finditer(text):
        if m.group(0).startswith("<div"):
            div_since = True
            continue
        if m.group(1):
            page = m.group(1)
            continue
        n = int(m.group(2))
        if n == 0:
            continue
        if prev is not None and n <= prev and (n == 1 or n < prev // 2):
            out.append(f"{prev}->{n}@M{page}{' [div]' if div_since else ''}")
        prev, div_since = n, False
    return out


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    for b in books():
        if args and b["file"] not in args:
            continue
        text = read_xml(b["file"])
        if not args and classify(text)["cls"] != "restarting":
            continue
        rs = restarts(text)
        kind = "canon" if b["canonical"] else "añña"
        if len(rs) > 4 and "--all" not in sys.argv and not args:
            print(f"{b['file']:18} {kind:5} {len(rs)} restarts, first: {'  '.join(rs[:3])}")
        else:
            print(f"{b['file']:18} {kind:5} " + "  ".join(rs))


if __name__ == "__main__":
    main()
