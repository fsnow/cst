#!/usr/bin/env python3
"""Classify every file's paragraph numbering: consecutive, consecutive with gaps, typos, restarting, none.

Usage: python3 paranum.py [--json OUT.json] [--list]

A paragraph number is the n attribute of a <p>. Ranges ("196-197", or abbreviated "196-7") count as one
paragraph covering both ends. n="0" (used on AN salutations) is ignored. Numbers are compared within a
<div type="book"> segment when the file has one, otherwise across the whole file.

Classes:
  consecutive     every number is the previous end + 1
  monotonic-gaps  always increasing, but with skips
  monotonic-typos increasing apart from isolated regressions (a number lower than the one before, but not
                  a restart -- e.g. e0102n's 590, 561, 592)
  restarting      at least one restart: a number that is 1, or below half the previous one
  no-numbers      no numbered paragraphs at all
The restart threshold is a heuristic. restarts.py prints every restart with its page, for checking by eye.
"""
import collections, json, re, sys
from corpus import books, read_xml

TOK = re.compile(r'<div\b[^>]*\btype="([^"]+)"[^>]*>|<p\b[^>]*\bn="([^"]*)"[^>]*>')


def parse(n):
    m = re.fullmatch(r"(\d+)(?:-(\d+))?", n.strip())
    if not m:
        return None
    a, b = int(m.group(1)), m.group(2)
    if b is None:
        return a, a
    if len(b) < len(m.group(1)) and int(b) < a:  # "196-7"
        b = m.group(1)[: len(m.group(1)) - len(b)] + b
    return a, int(b)


def classify(text):
    segs = collections.OrderedDict()
    seg, div_since = "(none)", False
    for m in TOK.finditer(text):
        if m.group(1):
            if m.group(1) == "book":
                idm = re.search(r'\bid="([^"]+)"', m.group(0))
                seg = idm.group(1) if idm else seg
            else:
                div_since = True
            continue
        v = parse(m.group(2))
        if v is None or v[0] == 0:
            continue
        segs.setdefault(seg, []).append((v, div_since))
        div_since = False
    r = dict(segments=len(segs), paras=sum(len(x) for x in segs.values()),
             gaps=0, restarts=0, restart_to_1=0, restart_at_div=0, regress=0)
    for lst in segs.values():
        prev = None
        for (a, b), at_div in lst:
            if prev is not None:
                if a <= prev:
                    if a == 1 or a < prev // 2:
                        r["restarts"] += 1
                        r["restart_to_1"] += a == 1
                        r["restart_at_div"] += at_div
                    else:
                        r["regress"] += 1
                elif a > prev + 1:
                    r["gaps"] += 1
            prev = b
    if r["paras"] == 0:
        r["cls"] = "no-numbers"
    elif r["restarts"]:
        r["cls"] = "restarting"
    elif r["regress"]:
        r["cls"] = "monotonic-typos"
    else:
        r["cls"] = "consecutive" if r["gaps"] == 0 else "monotonic-gaps"
    return r


def main():
    rows = []
    for b in books():
        rows.append(dict(file=b["file"], matn=b["matn"], canonical=b["canonical"], **classify(read_xml(b["file"]))))
    if "--json" in sys.argv:
        json.dump(rows, open(sys.argv[sys.argv.index("--json") + 1], "w"), ensure_ascii=False, indent=1)
    classes = ["consecutive", "monotonic-gaps", "monotonic-typos", "restarting", "no-numbers"]
    print("| Group | Files | " + " | ".join(classes) + " |")
    print("|---|---:|" + "---:|" * len(classes))
    groups = [("Canonical", [r for r in rows if r["canonical"]])]
    groups += [(f"  {lvl}", [r for r in rows if r["matn"] == lvl]) for lvl in ("Mula", "Atthakatha", "Tika")]
    groups += [("Añña (Other)", [r for r in rows if not r["canonical"]])]
    for name, rs in groups:
        c = collections.Counter(r["cls"] for r in rs)
        print(f"| {name} | {len(rs)} | " + " | ".join(str(c[k]) for k in classes) + " |")
    if "--list" in sys.argv:
        for k in classes:
            fs = [r["file"] for r in rows if r["cls"] == k]
            print(f"\n{k} ({len(fs)}): " + ", ".join(fs))


if __name__ == "__main__":
    main()
