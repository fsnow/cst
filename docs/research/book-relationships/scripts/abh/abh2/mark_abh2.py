"""Mark up the Vibhaṅga: its mūla from its own headings, and its aṭṭhakathā and ṭīkās from the mūla.

Decisions [fsnow, 2026-10-22] "I accept your suggestions", on these proposals:
  - ids abh1-abh7 for the seven books in canonical order ("abh" is his: "I am think "abh"");
  - two levels: the 18 vibhaṅgas (type vibhanga) and, inside them, the bhājanīyas (bhajaniya), the
    Khuddakavatthuvibhaṅga's niddesas (niddesa) and the Dhammahadayavibhaṅga's vāras (vara);
  - only book and vibhanga in the chapter list: [fsnow] "we may exclude divisions in the chapter list types
    while still including them in the markup";
  - Ñāṇavibhaṅga (16): nothing below the vibhaṅga. Its mātikā and niddesas are subheads, and its one title
    ("मातिका") stands after the mātikā, where vibhaṅga 17 has the closing line "मातिका।";
  - Khuddakavatthuvibhaṅga (17): the mātikā is the opening section abh2_17_0, headed "१. एककमातिका";
  - abh02t holds the mūlaṭīkā and the anuṭīkā: the mūlaṭīkā takes the abh2 ids; the anuṭīkā is book
    abh2a, its sections abh2a_N and abh2a_N_M.

Closing lines follow the KN conventions (markup-conventions.md): a closing line is a section's trailer only
if it names the section and is the section's last element; a colophon that runs on from a line without a
danda belongs to the closing it completes.

Usage: mark_abh2.py FILE [--write]"""
import re, sys, os
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
from markup import *
from mark_an import key, squash, plain, TEXT, CLOSING

DEV = str.maketrans('०१२३४५६७८९', '0123456789')
MULA = 'abh02m.mul.xml'
L2_TYPE = {17: 'niddesa', 18: 'vara'}          # otherwise bhajaniya
NUM = re.compile(r'^\(?([०-९]+)\.?\)?\.?\s')


def num(text):
    m = NUM.match(text)
    return int(m.group(1).translate(DEV)) if m else None


def heading(lines, i, rend):
    m = TEXT.match(lines[i])
    return plain(m.group(3)) if m and m.group(1) == 'p' and m.group(2) == rend else None


def mula_sections():
    """The mūla's second-level sections: {vibhaṅga number: [(id suffix, key)]}."""
    # Stripped, so this reads the same whether or not the mūla has been marked already.
    lines = strip(read(os.path.join(MASTER, MULA)))
    out, n = {}, None
    for i in range(len(lines)):
        if (h := heading(lines, i, 'chapter')) is not None:
            n = num(h); out[n] = []
        elif n and (h := heading(lines, i, 'title')) is not None:
            if n == 16: continue
            if n == 17 and h == '१. एककमातिका': out[n].append((0, key(h))); continue
            out[n].append((num(h), key(h)))
    return out


def names(lines, j, head, k=None):
    m = TEXT.match(lines[j])
    if not (m and (m.group(2) == 'centre' or CLOSING.search(plain(m.group(3))))): return False
    text = squash(plain(m.group(3))).replace('पञ्हपुच्छक', 'पञ्हापुच्छक')
    k = k or key(head)
    # The Khuddakavatthuvibhaṅga closes its mātikā "मातिका।" and each niddesa by its group alone ("दुकं।").
    short = {'एककमातिक': 'मातिका'}.get(k) or re.sub('निद्देस$', '', k)
    return k in text or bool(short) and text.startswith(short)


def runs_on(lines, j):
    """Line j is a centre line that does not end its sentence: it runs on into the next."""
    m = TEXT.match(lines[j])
    return bool(m) and m.group(2) == 'centre' and not re.search('[।॥]$', plain(m.group(3)))


def closing_start(lines, t):
    """The first line of a closing whose last line is t, taking in the run-on lines above it."""
    k = last_element(lines, t)
    while k > 0 and runs_on(lines, k):
        t = k; k = last_element(lines, k)
    return t


def build(name):
    # Stripped: the same lines as main, whether or not the file has been marked already.
    lines = strip(read(os.path.join(MASTER, name)))
    is_mula = name == MULA
    msecs = None if is_mula else mula_sections()
    body_end = lines.index('</body>')
    books = [i for i in range(body_end) if heading(lines, i, 'book') is not None]
    book_ids = ['abh2', 'abh2a'][:len(books)]
    sections, report, unmatched = [], [], []
    for b, (bstart, bid) in enumerate(zip(books, book_ids)):
        bnext = books[b + 1] if b + 1 < len(books) else body_end
        # The book ends at its last closing line; the next book's Namo and nikāya lines sit between books.
        bend = last_element(lines, bnext)
        while b + 1 < len(books) and not CLOSING.search(plain(TEXT.match(lines[bend]).group(3))):
            bend = last_element(lines, bend)
        btrailer = bool(CLOSING.search(plain(TEXT.match(lines[bend]).group(3))))
        bclose = closing_start(lines, bend) if btrailer else bend + 1
        # The book's own closing matter (the aṭṭhakathā's nigamana) is outside the vibhaṅgas.
        for j in range(bstart, bclose):
            if heading(lines, j, 'title') == 'निगमनकथा': bclose = j
        sections.append(Section(bid, 'book', bstart, bend, trailer=btrailer))
        l1 = [i for i in range(bstart, bclose) if heading(lines, i, 'chapter') is not None]
        for k, i in enumerate(l1):
            h = heading(lines, i, 'chapter'); n = num(h); sid = f'{bid}_{n}'
            lim = l1[k + 1] if k + 1 < len(l1) else bclose
            end = last_element(lines, lim)
            tr = names(lines, end, h)
            sections.append(Section(sid, 'vibhanga', i, end, trailer=tr))
            report.append((sid, h, plain(TEXT.match(lines[end]).group(3)), tr))
            inner_lim = closing_start(lines, end) if tr else end + 1
            # Second level.
            l2 = []
            for j in range(i + 1, inner_lim):
                # Commentaries also head sections with subheads (review of ebe7ee7: vibhaṅga 17's
                # "(१.) एककनिद्देसवण्णना", "तण्हाविचरितनिद्देसवण्णना"); the name match below keeps the
                # other subheads out.
                t = heading(lines, j, 'title')
                if t is None and not is_mula: t = heading(lines, j, 'subhead')
                if t is None: continue
                if is_mula:
                    if n == 16: continue
                    m, mk = (0 if (n == 17 and t == '१. एककमातिका') else num(t)), key(t)
                else:
                    # The ṭīkās spell the pañhāpucchaka "पञ्हपुच्छक".
                    tk = key(t).replace('पञ्हपुच्छक', 'पञ्हापुच्छक')
                    # By name: the commentary numbers differently in places (vibhaṅga 14's pañhāpucchaka is
                    # "२." in the mūla, "३." in the aṭṭhakathā) and names more ("सुत्तन्तभाजनीयं उद्देसवार").
                    # The heading must contain the mūla's name: the ṭīkās' "१. उप्पादककम्मवण्णना" and
                    # "२. आयुप्पमाणवण्णना" are parts of vāra 6, "उप्पादककम्मआयुप्पमाणवारो", not the vāra.
                    m, mk = next(((m, mk) for m, mk in msecs.get(n, []) if mk and mk in tk), (None, None))
                    if m is None or any(x[1] == m for x in l2): unmatched.append((n, t)); continue
                l2.append((j, m, t, mk))
            for q, (j, m, t, mk) in enumerate(l2):
                lim2 = l2[q + 1][0] if q + 1 < len(l2) else inner_lim
                end2 = last_element(lines, lim2)
                # A closing names the section as the mūla does: "सुत्तन्तभाजनीयवण्णना।" closes
                # "सुत्तन्तभाजनीयं उद्देसवारवण्णना".
                tr2 = names(lines, end2, t, mk)
                sid2 = f'{sid}_{m}'
                sections.append(Section(sid2, L2_TYPE.get(n, 'bhajaniya'), j, end2, trailer=tr2))
                report.append((sid2, t, plain(TEXT.match(lines[end2]).group(3)), tr2))
    result = apply(lines, sections)
    ids = check(lines, result)
    return lines, result, report, unmatched, ids


if __name__ == '__main__':
    name = sys.argv[1]
    lines, result, report, unmatched, ids = build(name)
    for sid, h, end, tr in report:
        print(f'{"  " * sid.count("_")}{sid:14} {h[:34]:34} → {end[:40]} {"[trailer]" if tr else "(no trailer)"}')
    for n, t in unmatched: print(f'   no mūla section in vibhaṅga {n}: {t}')
    print(f'\nlines {len(lines)} -> {len(result)}; ids {len(ids)}; strip check OK')
    if '--write' in sys.argv: write(os.path.join(MASTER, name), result); print('written')
