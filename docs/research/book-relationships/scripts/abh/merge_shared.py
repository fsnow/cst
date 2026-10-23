"""Merge every pakaraṇa's sections into the three shared Abhidhamma commentaries and write each once.
Sections come from the per-book builders' parts(): abh3/abh4, abh5, abh6, abh7."""
import sys, os
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
for d in ('abh3-4', 'abh5', 'abh6', 'abh7'): sys.path.insert(0, os.path.join(HERE, d))
from markup import read, strip, apply, check, write, retag, MASTER
import mark_abh34, mark_abh5, mark_abh6, mark_abh7
BUILDERS = [('abh3/abh4', mark_abh34), ('abh5', mark_abh5), ('abh6', mark_abh6), ('abh7', mark_abh7)]

def merged(name):
    # Stripped: main's lines, whether or not the file has been marked already.
    lines = strip(read(os.path.join(MASTER, name)))
    secs, spans = [], []
    for label, b in BUILDERS:
        ps = b.parts(name)
        book = [s for s in ps if s.type == 'book']
        for s in book: spans.append((s.start, s.end, s.id))
        secs += ps
    spans.sort()
    for (a1, e1, i1), (a2, e2, i2) in zip(spans, spans[1:]):
        assert e1 < a2, f'{name}: book {i1} ({a1+1}-{e1+1}) overlaps {i2} ({a2+1}-{e2+1})'
    heads = [s.start for s in secs if s.head]
    assert len(heads) == len(set(heads)), f'{name}: two sections claim one head line'
    # Heads that are not their section's first element (abh5_0 in abh03a opens at the gāthās).
    work = list(lines)
    for _, b in BUILDERS:
        for i in getattr(b, 'extra_heads', lambda n: [])(name): work[i] = retag(work[i], 'head')
    result = apply(work, secs)
    ids = check(lines, result)
    return lines, result, ids, spans

if __name__ == '__main__':
    for name in ('abh03a.att.xml', 'abh03t.tik.xml', 'abh05t.nrf.xml'):
        lines, result, ids, spans = merged(name)
        print(f'{name}: {len(ids)} ids; books ' + ', '.join(f'{i} {a+1}-{e+1}' for a, e, i in spans) + '; strip check OK')
        outside = [i+1 for i in range(len(lines)) if lines[i] and lines[i].startswith('<p') and not any(a <= i <= e for a, e, _ in spans)]
        print('   lines outside every book div:', outside)
        if '--write' in sys.argv: write(os.path.join(MASTER, name), result); print('   written')
