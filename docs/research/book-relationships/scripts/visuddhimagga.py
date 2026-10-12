#!/usr/bin/env python3
"""Check the Visuddhimagga's own mūla/ṭīkā linking: does every Mahāṭīkā paragraph number exist in the
main text, and are both split across volumes at the same paragraph?

  main text   e0101n (vol 1) + e0102n (vol 2)
  Mahāṭīkā    e0103n (vol 1) + e0104n (vol 2)
  e0105n (Nidānakathā) is a separate work with its own restarting numbers and is not checked here.
"""
import re
from corpus import read_xml
from paranum import parse


def numbers(file_name):
    out = []
    for n in re.findall(r'<p\b[^>]*\bn="([^"]*)"', read_xml(file_name)):
        v = parse(n)
        if v and v[0]:
            out.append(v)
    return out


def main():
    main_vols = {f: numbers(f) for f in ("e0101n.mul.xml", "e0102n.mul.xml")}
    tika_vols = {f: numbers(f) for f in ("e0103n.att.xml", "e0104n.att.xml")}
    for f, ns in {**main_vols, **tika_vols}.items():
        print(f"{f}: {len(ns)} numbered paragraphs, {ns[0][0]}-{ns[-1][1]}")
    covered = {n for ns in main_vols.values() for a, b in ns for n in range(a, b + 1)}
    tika = [v for ns in tika_vols.values() for v in ns]
    missing = [v for v in tika if not any(n in covered for n in range(v[0], v[1] + 1))]
    print(f"\nMahāṭīkā paragraphs whose number exists in the main text: {len(tika) - len(missing)} of {len(tika)}")
    print("Not found in the main text:", missing or "none")
    main_all = [v for ns in main_vols.values() for v in ns]
    regress = [(main_all[i - 1], main_all[i]) for i in range(1, len(main_all)) if main_all[i][0] <= main_all[i - 1][1]]
    print("Main-text regressions (number not above the one before):", regress or "none")
    print("Volume split -- main:", [ns[-1][1] for ns in main_vols.values()][0], "|", [ns[0][0] for ns in main_vols.values()][1],
          "   ṭīkā:", [ns[-1][1] for ns in tika_vols.values()][0], "|", [ns[0][0] for ns in tika_vols.values()][1])


if __name__ == "__main__":
    main()
