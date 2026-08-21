#!/usr/bin/env python3
"""Measure characters-per-token for romanized Pali. (#672)

The estimate the assistant shows a reader used 4 characters per token -- the familiar rule of thumb for
English prose, applied to a corpus that tokenizes nothing like English. This measures the real figure.

    python3 -m venv venv && ./venv/bin/pip install tiktoken
    ./venv/bin/python measure.py pali-windows.txt

pali-windows.txt is a sample of real passage windows, Latin script, produced exactly the way the bundler
produces them -- see TokenRatioSampler.cs.txt, which is the instrument that wrote it. Regenerate it by
dropping that file into src/CST.Avalonia.Tests/Search/ and running its one test.

Anthropic's tokenizer is not public and so is not in this sample. The way to check the constant against the
models actually in use is the input-token count every provider returns on a completed turn.
"""
import sys, tiktoken

path = sys.argv[1] if len(sys.argv) > 1 else "pali-windows.txt"
lines = [l.rstrip("\n") for l in open(path, encoding="utf-8") if l.strip()]
print(f"windows: {len(lines)}  total chars: {sum(len(l) for l in lines):,}")

for name in ("o200k_base", "cl100k_base"):
    enc = tiktoken.get_encoding(name)
    ratios, tot_c, tot_t = [], 0, 0
    for l in lines:
        c, t = len(l), len(enc.encode(l))
        tot_c += c; tot_t += t; ratios.append(c / t)
    ratios.sort()
    q = lambda f: ratios[int(len(ratios) * f)]
    print(f"\n{name}: aggregate {tot_c/tot_t:.3f} chars/token  ({tot_t:,} tokens for {tot_c:,} chars)")
    print(f"  per window - min {ratios[0]:.2f}  p10 {q(.10):.2f}  median {q(.50):.2f}  "
          f"p90 {q(.90):.2f}  max {ratios[-1]:.2f}")
