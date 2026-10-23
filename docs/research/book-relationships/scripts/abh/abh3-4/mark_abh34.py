"""Mark up the Dhātukathā (abh3) and the Puggalapaññatti (abh4).

Decided [fsnow, 2026-10-22], accepting the proposal with one change ("'pannatti' for the groups"); every
Abhidhamma opening section _0 is type "intro" [fsnow picked "One type: intro"].
  abh3  book
    abh3_0     intro     abh03a: no <head>, the opening gāthās; the ṭīkās' "गन्थारम्भवण्णना" has a head
    abh3_1     matika    mūla "उद्देसो"; commentaries "१. मातिकावण्णना"
    abh3_2     niddesa   no <head> in the mūla, which has no niddesa heading, around the 14 nayas; the
                         commentaries' "२. निद्देसवण्णना". Not in the mūla's ChapterListTypes: a head-less
                         div of a listed type is listed under the first heading inside it.
      abh3_2_N naya      mūla: the chapter line "N. ...नयो" is the head; the title after it (the naya's
                         niddesa name) stays <p>, and the closing that repeats that name is the trailer
  abh4  book
    abh4_0     intro     abh03a only: no <head>, the opening gāthās
    abh4_1     matika    "मातिका" / "१. मातिकावण्णना"
      abh4_1_N uddesa    mūla only: "N. ...उद्देसो"
    abh4_2     niddesa   "निद्देसो" / "२. निद्देसवण्णना"
      abh4_2_N pannatti  mūla "N. ...पुग्गलपञ्ञत्ति"; commentaries "N. ...निद्देसवण्णना". abh03a's ¶207 covers
                         the skipped 8 and 9: a head-less abh4_2_8, as for the AN notes on skipped sections

Trailers follow the KN conventions: the section's last element, and only if it names the section; a short
form counts ("एककं।" closes "एककउद्देसो"). Where levels end on the same line, the deepest section the line
names takes it. A book's last line is its trailer when it is a closing.

In the shared commentary files (abh03a, abh03t, abh05t) a book div opens at its part's heading
(rend="chapter"); the file's own title line, and the Namo/nikāya lines between parts, stay outside.

Every file is read through strip(), so the result is the same before and after a file is marked.

Usage: mark_abh34.py [FILE ...] [--write]   (--write: the two mūlas only)"""
import re, sys, os
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
from markup import *
from mark_an import key, squash, plain, TEXT, CLOSING

DEV = str.maketrans('०१२३४५६७८९', '0123456789')
SHORT = ('उद्देस', 'पुग्गलपञ्ञत्त', 'निद्देस')   # suffixes a closing may leave out (key() drops ZWJ and a final vowel)
MULAS = ('abh03m1.mul.xml', 'abh03m2.mul.xml')
SHARED = ('abh03a.att.xml', 'abh03t.tik.xml', 'abh05t.nrf.xml')


def load(name):
    """The file as unmarked lines (strip() of what is on disk), and whether it was already marked."""
    disk = read(os.path.join(MASTER, name))
    return strip(disk), disk


def text(lines, i):
    m = TEXT.match(lines[i])
    return (m.group(2), plain(m.group(3))) if m else (None, None)


def num(t):
    m = re.match(r'^([०-९]+)\.', t or '')
    return int(m.group(1).translate(DEV)) if m else None


def names(lines, j, *heads):
    rend, t = text(lines, j)
    if t is None or not (rend == 'centre' or CLOSING.search(t)): return False
    s = squash(t)
    for h in heads:
        k = key(h)
        if k and k in s: return True
        for suf in SHORT:
            if k.endswith(suf) and len(k) > len(suf) and s.startswith(k[:-len(suf)]): return True
    return False


def next_element(lines, i):
    i += 1
    while lines[i] == '': i += 1
    return i


def sections_for(lines, st, book_end, stops=()):
    """st: (level, start line, id, type, has head, extra names) for one book, its level-0 entry first.
    book_end: the book's last line. stops: lines (e.g. निगमनकथा) that no level-1 section runs past."""
    ends = {}
    for k, (lv, i, sid, typ, head, extra) in enumerate(st):
        if lv == 0:
            ends[sid] = book_end; continue
        parent = max((s for s in st[:k] if s[0] < lv), key=lambda s: s[1])
        nxt = next((s[1] for s in st[k + 1:] if s[0] <= lv), None)
        lim = min(x for x in (nxt, ends[parent[2]] + 1, *[s for s in stops if s > i]) if x is not None)
        ends[sid] = last_element(lines, lim)

    def trailers():
        tr = {}
        for k in sorted(range(len(st)), key=lambda k: -st[k][0]):
            lv, i, sid, typ, head, extra = st[k]
            end = ends[sid]
            kids = [s for s in st if s[2].startswith(sid + '_') and s[0] == lv + 1]
            if any(ends[c[2]] == end and tr[c[2]] for c in kids): tr[sid] = False
            elif typ == 'book': tr[sid] = bool(CLOSING.search(text(lines, end)[1] or ''))
            else: tr[sid] = head and names(lines, end, text(lines, i)[1], *extra)
        return tr
    tr = trailers()
    for lv, i, sid, typ, head, extra in st:          # a parent's trailer is below all its children
        if tr[sid]:
            for c in st:
                if c[2].startswith(sid + '_') and ends[c[2]] >= ends[sid]:
                    ends[c[2]] = last_element(lines, ends[sid])
    tr = trailers()
    return [Section(sid, typ, i, ends[sid], head=head, trailer=tr[sid]) for lv, i, sid, typ, head, extra in st]


# ---- the mūlas ----

def mula(name):
    lines, disk = load(name)
    body_end = lines.index('</body>')
    book = next(i for i in range(body_end) if text(lines, i)[0] == 'book')
    st = []
    if name == 'abh03m1.mul.xml':
        st.append((0, book, 'abh3', 'book', True, ()))
        for i in range(book, body_end):
            rend, t = text(lines, i)
            if rend == 'chapter' and t == 'उद्देसो':
                st.append((1, i, 'abh3_1', 'matika', True, ()))
            elif rend == 'chapter' and num(t):
                if num(t) == 1: st.append((1, i, 'abh3_2', 'niddesa', False, ()))
                st.append((2, i, f'abh3_2_{num(t)}', 'naya', True, (text(lines, next_element(lines, i))[1],)))
    else:
        st.append((0, book, 'abh4', 'book', True, ()))
        part = None
        for i in range(book, body_end):
            rend, t = text(lines, i)
            if rend == 'chapter':
                part = {'मातिका': 1, 'निद्देसो': 2}[t]
                st.append((1, i, f'abh4_{part}', ['matika', 'niddesa'][part - 1], True, ()))
            elif rend == 'subhead' and part:
                st.append((2, i, f'abh4_{part}_{num(t)}', ['uddesa', 'pannatti'][part - 1], True, ()))
    return lines, disk, sections_for(lines, st, last_element(lines, body_end))


# ---- the shared commentary files ----

BOOKS = (('abh3', 'धातुकथा'), ('abh4', 'पुग्गलपञ्ञत्ति'))
L2 = {'abh3': ('naya', re.compile(r'^[०-९]+\. \S+नयो ')), 'abh4': ('pannatti', re.compile(r'^[०-९]+\. \S+निद्देसवण्णना$'))}


def parts(name):
    """Sections of abh3 and abh4 in a shared commentary file, as line indexes into the UNMARKED file
    (strip() of what is on disk). Every Section with head=True has its heading on its start line."""
    lines, disk = load(name)
    body_end = lines.index('</body>')
    out = []
    for bid, stem in BOOKS:
        start = next(i for i in range(body_end) if text(lines, i)[0] == 'chapter'
                     and squash(text(lines, i)[1]).startswith(stem))
        head = squash(text(lines, start)[1])
        end = next(i for i in range(start + 1, body_end) if text(lines, i)[0] == 'centre'
                   and squash(text(lines, i)[1]).startswith(head) and CLOSING.search(text(lines, i)[1]))
        st = [(0, start, bid, 'book', True, ())]
        stops = [i for i in range(start, end) if text(lines, i)[1] == 'निगमनकथा']
        first = next_element(lines, start)
        if text(lines, first)[1] == 'गन्थारम्भवण्णना':
            st.append((1, first, f'{bid}_0', 'intro', True, ()))
        m1 = next(i for i in range(start, end) if text(lines, i)[1] == '१. मातिकावण्णना')
        if not st[-1][2].endswith('_0') and first < m1:
            st.append((1, first, f'{bid}_0', 'intro', False, ()))        # abh03a's opening gāthās
        st.append((1, m1, f'{bid}_1', 'matika', True, ()))
        m2 = next(i for i in range(m1, end) if text(lines, i)[1] == '२. निद्देसवण्णना')
        st.append((1, m2, f'{bid}_2', 'niddesa', True, ()))
        typ, pat = L2[bid]
        prev = 0
        for i in range(m2 + 1, end):
            rend, t = text(lines, i)
            if rend in ('title', 'subhead') and pat.match(t):
                n = num(t)
                if n > prev + 1:
                    # A note on the skipped sections sits after the previous one's closing (abh03a ¶207).
                    close = max(j for j in range(m2, i) if names(lines, j, *[s for s in st if s[2] == f'{bid}_2_{prev}'][0][5:6][0]))
                    note = next_element(lines, close)
                    if note < i: st.append((2, note, f'{bid}_2_{prev + 1}', typ, False, ()))
                extra = (t.split('नयो ', 1)[1],) if bid == 'abh3' else ()
                st.append((2, i, f'{bid}_2_{n}', typ, True, (t, *extra)))
                prev = n
        out += sections_for(lines, st, end, stops)
    return out


def check_parts(name):
    lines, disk = load(name)
    secs = parts(name)
    result = apply(lines, secs)
    return lines, result, secs, check(lines, result)


def show(lines, secs):
    for s in secs:
        h = text(lines, s.start)[1] if s.head else '(no head)'
        print(f'{"  " * s.id.count("_")}{s.id:10} {s.type:8} {s.start + 1:5}-{s.end + 1:<5} {h[:30]:30} -> '
              f'{(text(lines, s.end)[1] or "")[:34]} {"[trailer]" if s.trailer else ""}')


if __name__ == '__main__':
    names_ = [a for a in sys.argv[1:] if not a.startswith('--')] or [*MULAS, *SHARED]
    for name in names_:
        print('==', name)
        if name in MULAS:
            lines, disk, secs = mula(name)
            result = apply(lines, secs)
            ids = check(lines, result)
            show(lines, secs)
            print(f'lines {len(lines)} -> {len(result)}; ids {len(ids)}; strip check OK')
            if disk != lines:
                assert disk == result, f'{name} is marked differently on disk'
                print('already marked: identical')
            elif '--write' in sys.argv:
                write(os.path.join(MASTER, name), result); print('written')
        else:
            lines, result, secs, ids = check_parts(name)
            show(lines, secs)
            print(f'parts only (not written): ids {len(ids)}; strip check OK')
        print()
