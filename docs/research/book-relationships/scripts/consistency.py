#!/usr/bin/env python3
"""Check whether the 2007-08 markup follows the same conventions in every marked file.

Usage: python3 consistency.py

Reports, per marked file:
  - how each inner div ends: <trailer>, a colophon left as <p rend="centre">, or something else;
  - opening sections (ids ending _0, or type="intro") and whether they have a <head>, which decides
    whether ChapterListsService lists them;
  - <head> rend values that differ from the usual one for the div's type;
  - material inside a book div before its first inner div, and material after the book div closes.
"""
import collections, re
from corpus import books, read_xml

EL = re.compile(r'<div\b([^>]*)>|</div>|<(head|trailer|p)\b([^>]*)>(.*?)</\2>', re.S)
COLOPHON = re.compile(r"निट्ठित|समत्त|लीनत्थप्पकासना")
USUAL_HEAD = {"book": "book", "pannasaka": "title"}  # every other type: "chapter"


def scan(text):
    stack, last = [], None
    ends, opening, after, odd_heads, openers = collections.Counter(), collections.Counter(), collections.Counter(), [], []
    seen_inner = False
    for m in EL.finditer(text):
        if m.group(0) == "</div>":
            d = stack.pop()
            if d["type"] != "book":
                if last and last[0] == "trailer":
                    ends["trailer"] += 1
                elif last and last[0] == "p" and last[1] == "centre" and COLOPHON.search(last[2]):
                    ends["p-colophon"] += 1
                else:
                    ends["other"] += 1
            last = None
            continue
        if m.group(1) is not None:
            a = dict(re.findall(r'(\w+)="([^"]*)"', m.group(1)))
            a["has_head"] = False
            if a["type"] != "book":
                seen_inner = True
            if a["id"].endswith("_0") or a["type"] == "intro":
                openers.append(a)
            stack.append(a)
            last = None
            continue
        tag = m.group(2)
        r = re.search(r'rend="([^"]+)"', m.group(3))
        r = r.group(1) if r else "-"
        txt = re.sub(r"<[^>]+>", "", m.group(4)).strip()
        if tag == "head" and stack:
            stack[-1]["has_head"] = True
            d = stack[-1]
            if r != USUAL_HEAD.get(d["type"], "chapter"):
                odd_heads.append((d["id"], d["type"], r, txt[:40]))
        if len(stack) == 1 and tag != "head" and not seen_inner:
            opening[r] += 1
        if not stack and r != "nikaya" and not txt.startswith("॥"):
            after[r] += 1
        last = (tag, r, txt)
    return ends, opening, after, odd_heads, openers


def main():
    print("| File | inner divs | end in <trailer> | end in <p> colophon | other |")
    print("|---|---:|---:|---:|---:|")
    detail = []
    for b in books():
        text = read_xml(b["file"])
        if "<div" not in text:
            continue
        ends, opening, after, odd, openers = scan(text)
        n = sum(ends.values())
        if n:
            print(f"| `{b['file']}` | {n} | {ends['trailer']} | {ends['p-colophon']} | {ends['other']} |")
        detail.append((b, opening, after, odd, openers))
    print("\nOpening sections:")
    for b, _, _, _, openers in detail:
        for a in openers:
            listed = a["has_head"] and a["type"] in b["chapter_list_types"].split(",")
            print(f"  {b['file']:18} {a['id']:8} type={a['type']:9} head={'yes' if a['has_head'] else 'no ':3} "
                  f"in chapter list={'yes' if listed else 'no'}")
    print("\n<head> rend differing from the usual one for its div type:")
    for b, _, _, odd, _ in detail:
        for x in odd:
            print(f"  {b['file']:18} {x}")
    print("\nMaterial inside the book div before its first inner div (excluding the salutation):")
    for b, opening, _, _, _ in detail:
        o = {k: v for k, v in opening.items() if k not in ("centre", "nikaya")}
        if o:
            print(f"  {b['file']:18} {o}")
    print("\nMaterial after the book div closes:")
    for b, _, after, _, _ in detail:
        if after:
            print(f"  {b['file']:18} {dict(after)}")


if __name__ == "__main__":
    main()
