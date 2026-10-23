"""Yamaka (abh6): dry-run markup proposal. Never writes.

Levels [suggestion]: book abh6 (repeated in each of abh03m4-m6, as kn10 is in s0510m1/m2), the ten yamakas
abh6_1..abh6_10 (type yamaka), and in each yamaka its main vāras (type vara):
  1 Mūla, 8 Citta:            _1 (क) uddesa, _2 (ख) niddesa
  2-6, 9, 10:                 _1 paṇṇatti, _2 pavatti, _3 pariññā (bhāvanā in 9)
  7 Anusaya:                  _1 uppattiṭṭhāna, _2 mahāvāra
A commentary has a vāra div only where it heads that vāra; ids follow the mūla.

Trailers follow the KN conventions: a closing line that names the section and is its last element.

Usage: mark_abh6.py [--write]  dry-run the three mūla files and the three commentary parts;
       --write writes the mūla files only. Shared files: parts(name).
"""
import re, sys, os
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))   # scripts/: markup.py, mark_an.py
from markup import *
from mark_an import squash, plain, TEXT, CLOSING

DEV = str.maketrans('\u0966\u0967\u0968\u0969\u096a\u096b\u096c\u096d\u096e\u096f', '0123456789')
MULA = ['abh03m4.mul.xml', 'abh03m5.mul.xml', 'abh03m6.mul.xml']
# Commentary parts: (file, first line of the part's heading, text of its closing line).
PARTS = [('abh03a.att.xml', 'यमकप्पकरण-अट्ठकथा', 'यमकप्पकरण-अट्ठकथा निट्ठिता।'),
         ('abh03t.tik.xml', 'यमकपकरण-मूलटीका', 'यमकपकरण-मूलटीका समत्ता।'),
         ('abh05t.nrf.xml', 'यमकपकरण-अनुटीका', 'यमकपकरण-अनुटीका समत्ता।')]

# Vāra headings by yamaka: (suffix, name stem that the heading starts with once its number is dropped,
# the stem a closing line must contain to name it).
UD = [(1, 'उद्देस', 'उद्देसवार'), (2, 'निद्देस', 'निद्देसवार')]
PPP = [(1, 'पण्णत्ति', 'पण्णत्तिवार'), (2, 'पवत्तिवार', 'पवत्तिवार'), (3, 'परिञ्ञावार', 'परिञ्ञावार')]   # ZWJ dropped: stem() squashes it
VARAS = {1: UD, 8: UD, 7: [(1, 'उप्पत्तिट्ठानवार', 'उप्पत्तिट्ठानवार'), (2, 'महावार', 'महावार')],
         9: [(1, 'पण्णत्ति', 'पण्णत्तिवार'), (2, 'पवत्तिवार', 'पवत्तिवार'), (3, 'भावनावार', 'भावनावार')]}
YAMAKA = re.compile('^([\u0966-\u096f]+)\\.\\s*(\\S+यमकं)$')


def text(lines, i):
    m = TEXT.match(lines[i])
    return (m.group(2), plain(m.group(3))) if m and m.group(1) == 'p' else (None, None)


def stem(t):
    """Drop a leading number, (क)/(ख) and spaces; vāra headings compare on what remains."""
    t = re.sub('^\\(?[\u0966-\u096f\u0915\u0916]+\\)?\\.?\\s*', '', t)
    return squash(t)


def names(lines, j, needle):
    rend, t = text(lines, j)
    if rend is None: return False
    if not (rend == 'centre' or CLOSING.search(t)): return False
    # [fsnow, 2026-10-22] accepted: abh03a closes the Khandha pavatti as "पवत्तिमहावारवण्णना।".
    alts = [needle] + (['पवत्तिमहावार'] if needle == 'पवत्तिवार' else [])
    return any(squash(a) in squash(t) for a in alts)


def runs_on(lines, j):
    rend, t = text(lines, j)
    return rend == 'centre' and not re.search('[\u0964\u0965]$', t)


def closing_start(lines, t):
    k = last_element(lines, t)
    while k > 0 and runs_on(lines, k):
        t = k; k = last_element(lines, k)
    return t


def yamaka_starts(lines, a, b, rends):
    out = []
    for i in range(a, b):
        rend, t = text(lines, i)
        if rend in rends and (m := YAMAKA.match(t or '')):
            out.append((i, int(m.group(1).translate(DEV)), t))
    return out


def mark(lines, a, b, book_head, rends, report):
    """Sections for the Yamaka between lines a (book head) and b (exclusive). Returns a list of Section."""
    secs = []
    end = last_element(lines, b)
    starts = yamaka_starts(lines, a, b, rends)
    first = starts[0][0]
    # The book's last line is its trailer unless a yamaka ends on it (abh03m4/m5 end on their last yamaka).
    last_y = starts[-1]
    ynames = {n: squash(re.sub('\u0902$', '', t.split('. ', 1)[1])) for _, n, t in starts}
    book_tr = not names(lines, end, ynames[last_y[1]]) and bool(CLOSING.search(text(lines, end)[1] or ''))
    secs.append(Section('abh6', 'book', a, end, head=book_head, trailer=book_tr))
    yclose = closing_start(lines, end) if book_tr else end + 1
    # The aṭṭhakathā's nigamana is the book's closing matter, outside the last yamaka.
    yclose = next((i for i in range(first, yclose) if text(lines, i) == ('subhead', 'निगमनकथा')), yclose)
    # An opening section before the first yamaka, type intro [fsnow, 2026-10-22: "One type: intro"]: the
    # ṭīkās' "गन्थारम्भवण्णना", and in a commentary without such a heading (abh03a's opening gāthās) a
    # head-less one [fsnow, 2026-10-22: "Head-less _0 for all five"], so the ṭīkās' abh6_0 has a target.
    body = [i for i in range(a + 1, first) if lines[i] != '']
    if body and rends != ('chapter',):
        i = body[0]
        rend, t = text(lines, i)
        head = rend == 'subhead'
        e = last_element(lines, first)
        tr = head and names(lines, e, squash(t))
        secs.append(Section('abh6_0', 'intro', i, e, head=head, trailer=tr))
        report.append(('abh6_0', t if head else '(no head) ' + t, text(lines, e)[1], tr))
    for k, (i, n, t) in enumerate(starts):
        lim = starts[k + 1][0] if k + 1 < len(starts) else yclose
        # The next yamaka's salutation sits between yamakas, outside both.
        e = last_element(lines, lim)
        while k + 1 < len(starts) and e > i and (text(lines, e)[0] in ('book', 'nikaya')
                                                 or 'नमो' in (text(lines, e)[1] or '')):
            e = last_element(lines, e)
        tr = names(lines, e, ynames[n])
        sid = f'abh6_{n}'
        secs.append(Section(sid, 'yamaka', i, e, trailer=tr))
        report.append((sid, t, text(lines, e)[1], tr))
        inner = closing_start(lines, e) if tr else e + 1
        vs = []
        for suffix, s, needle in VARAS.get(n, PPP):
            for j in range(i + 1, inner):
                rend, h = text(lines, j)
                if rend in ('title', 'subhead') and stem(h).startswith(s):
                    vs.append((j, suffix, h, needle)); break
        vs.sort()
        for q, (j, suffix, h, needle) in enumerate(vs):
            lim2 = vs[q + 1][0] if q + 1 < len(vs) else inner
            e2 = last_element(lines, lim2)
            tr2 = names(lines, e2, needle)
            secs.append(Section(f'{sid}_{suffix}', 'vara', j, e2, trailer=tr2))
            report.append((f'{sid}_{suffix}', h, text(lines, e2)[1], tr2))
    return secs


def unmarked(name):
    """The file's lines with any divs removed, so the result is the same before and after marking."""
    return strip(read(os.path.join(MASTER, name)))


def sections(name, lines, report=None):
    report = [] if report is None else report
    part = next(((h, c) for f, h, c in PARTS if f == name), None)
    if part is None:
        a = next(i for i in range(len(lines)) if text(lines, i)[0] == 'book')
        b = lines.index('</body>')
        return mark(lines, a, b, True, ('chapter',), report), a, b
    head, close = part
    a = find(lines, head)
    b = find(lines, close, start=a) + 1
    return mark(lines, a, b, True, ('chapter', 'subhead'), report), a, b


def parts(name):
    """The Yamaka's sections in a shared commentary file (abh03a, abh03t, abh05t), book div abh6 included.
    Line indices refer to the file's lines as read from disk with any divs stripped (markup.read, then
    markup.strip): for the unmarked file, its own lines."""
    assert name in [f for f, _, _ in PARTS], name
    return sections(name, unmarked(name))[0]


def run(name, write_it=False):
    lines = unmarked(name)
    report = []
    secs, a, b = sections(name, lines, report)
    part = name not in MULA
    result = apply(lines, secs)
    ids = check(lines, result)
    print(f'== {name}' + (f'  lines {a + 1}-{b}' if part else ''))
    for sid, h, e, tr in report:
        print(f'{"  " * sid.count("_")}{sid:10} {h[:34]:34} -> {(e or "")[:40]} {"[trailer]" if tr else "(no trailer)"}')
    types = {'book', 'yamaka', 'vara'}
    print(f'   chapter list (book,yamaka,vara): {len(chapter_list(result, types))}; '
          f'(book,yamaka): {len(chapter_list(result, {"book", "yamaka"}))}; ids {len(ids)}; strip check OK')
    if write_it:
        # Cleared for the three mūla files only; the shared files are merged and written by the caller.
        assert name in MULA, f'{name}: shared file, use parts()'
        check(strip(read(os.path.join(MASTER, name))), result)
        write(os.path.join(MASTER, name), result)
        print('   written')


if __name__ == '__main__':
    w = '--write' in sys.argv
    for f in MULA: run(f, write_it=w)
    for f, h, c in PARTS: run(f)
