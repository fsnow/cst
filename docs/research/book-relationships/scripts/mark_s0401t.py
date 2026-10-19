"""Mark up s0401t.tik.xml (Ekakanipāta-ṭīkā). Decisions [fsnow, 2026-10-19]: opening section in an
an1_0 intro div with a <head>; ids follow the printed vagga numbers; a section's closing line is its
trailer; vagga 15's second chapter line stays a <p>; the volume line is the book's trailer."""
import re, sys, os
sys.path.insert(0, os.path.dirname(__file__))
from markup import *

DEV = str.maketrans('०१२३४५६७८९', '0123456789')
# Usage: mark_s0401t.py [OUT [SRC]]. SRC defaults to deva master's s0401t; OUT defaults to SRC.
src = sys.argv[2] if len(sys.argv) > 2 else os.path.join(MASTER, 's0401t.tik.xml')
out = sys.argv[1] if len(sys.argv) > 1 else src

lines = read(src)
sections = []

# Opening section: from the ṭīkā's own "गन्थारम्भकथा" to the close of its "गन्थारम्भकथावण्णना".
s0 = find(lines, 'गन्थारम्भकथा', 'subsubhead')
e0 = find(lines, 'गन्थारम्भकथावण्णना निट्ठिता।', 'centre')
sections.append(Section('an1_0', 'intro', s0, e0, head=True, trailer=True))

# Vaggas: every chapter heading that starts with its printed number, except vagga 15's second line.
starts = []
for i, l in enumerate(lines):
    m = re.match(r'^<p rend="chapter">([०-९]+)\. (.*)</p>$', l)
    if m:
        if 'दुतियवग्ग' in m.group(2) and m.group(1) == '१५': continue
        starts.append((i, int(m.group(1).translate(DEV)), m.group(2)))

book_close = find(lines, 'इति मनोरथपूरणिया अङ्गुत्तरनिकाय-अट्ठकथाय', 'centre', start=starts[-1][0])
# A note on vaggas the ṭīkā skips follows the previous vagga's own closing line, under the skipped
# vaggas' paragraph numbers (¶81-82 = vagga 9, ¶140-150 = vaggas 11-12). The vagga ends at its closing
# line; the note stays in the book, outside every section.
ENDS_AT = {8: 'कल्याणमित्तादिवग्गवण्णना निट्ठिता।', 10: 'दुतियपमादादिवग्गवण्णना निट्ठिता।'}
CLOSING = re.compile('निट्ठित|समत्त')
for k, (i, num, title) in enumerate(starts):
    nxt = starts[k + 1][0] if k + 1 < len(starts) else book_close
    end = find(lines, ENDS_AT[num], start=i) if num in ENDS_AT else last_element(lines, nxt)
    assert end < nxt
    m = P_LINE.match(lines[end])
    is_closing = bool(m) and (m.group(1) == 'centre' or bool(CLOSING.search(m.group(3))))
    sections.append(Section(f'an1_{num}', 'vagga', i, end, head=True, trailer=is_closing))

# The ṭīkā's commentary on vagga 9, and on vaggas 11-12 together, is a single numbered note with no
# heading. [fsnow, 2026-10-19] they go in "an1_9 and an1_11": a div each, no <head>, no trailer.
for vid, n in (('an1_9', '81-82'), ('an1_11', '140-150')):
    i = next(k for k, l in enumerate(lines) if l.startswith(f'<p rend="bodytext" n="{n}">'))
    sections.append(Section(vid, 'vagga', i, i, head=False, trailer=False))
sections.sort(key=lambda s: s.start)

result = apply(lines, sections)
vol = find(result, 'पठमो भागो निट्ठितो।', 'centre')
result[vol] = retag(result[vol], 'trailer')

ids = check(lines, result)
write(out, result)

def txt(l): return re.sub(r'<[^>]+>', '', l)
print(f'{"id":8} {"type":6} heading → closing (trailer?)')
for s in sections:
    print(f'{s.id:8} {s.type:6} {txt(lines[s.start])[:34]:34} → {txt(lines[s.end])[:44]} '
          f'{"[trailer]" if s.trailer else "(no closing line: last element stays)"}')
print('book trailer:', txt(result[vol]))
print(f'\nlines {len(lines)} -> {len(result)}; ids {len(ids)}; strip check OK')
print('\nchapter list (book,intro,vagga):')
for e in chapter_list(result, {'book', 'intro', 'vagga'}): print('  ' + e)
