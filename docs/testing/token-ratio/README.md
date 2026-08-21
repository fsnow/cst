# Characters per token, for Pāli

Why `AiTokens.PaliCharsPerToken` is 2.0 and not 4. (#672)

## The problem

`AiContextBundler.ApproximateTokens` divided characters by 4 — the familiar rule of thumb for English prose —
and the result was shown to the reader as "Estimated context ~N tokens". Romanized Pāli with diacritics
tokenizes considerably worse than English, so the figure was low, and the code said so in a comment without
anyone measuring by how much.

## The measurement

292 passage windows drawn from every third book, converted to Latin exactly as the bundler sends them —
161,266 characters. Run 2026-08-21.

| tokenizer | chars/token | min | p10 | median | p90 | max |
|---|---|---|---|---|---|---|
| `o200k_base` | **2.30** | 1.81 | 2.15 | 2.29 | 2.45 | 2.77 |
| `cl100k_base` | **1.73** | 1.33 | 1.57 | 1.72 | 1.93 | 2.21 |

So `/4` under-counted by **1.7×–2.3×**.

## The choice

**2.0**, deliberately below the modern tokenizer's figure and above the older one's. For a number nobody can
make exact, over-reporting is the safer error: a reader surprised by a bigger estimate is not harmed, one
surprised by a bill is.

Anthropic's tokenizer is not public and is not in this sample. The way to check the constant against the
models actually in use is the input-token count every provider returns on a completed turn — an observation
this repo can make in ordinary use rather than a curated table it would have to maintain.

## Re-deriving it

```bash
python3 -m venv venv && ./venv/bin/pip install tiktoken
./venv/bin/python measure.py pali-windows.txt
```

To regenerate the sample itself, drop `TokenRatioSampler.cs.txt` into `src/CST.Avalonia.Tests/Search/` as a
`.cs` file and run its single test. It is kept out of the test project on purpose: it reads the installed
corpus, which a test run cannot assume is present.
