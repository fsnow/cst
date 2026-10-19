"""Mark up an Aṅguttara book from an already marked template: a ṭīkā (s0402t, s0403t, s0404t) from its
aṭṭhakathā, or the aṭṭhakathā's AN9-11 (s0404a) from the mūla (s0404m2-m4).

Each ṭīkā heading (title = paṇṇāsaka, chapter = vagga/peyyāla) is matched to the aṭṭhakathā section with
the same name inside the same parent, and takes that section's id and type, so the same id names the same
section at both levels. The book divs already exist.

Conventions [fsnow, 2026-10-19]: a section's own closing line is its trailer, inside it; a paṇṇāsaka's
closing line is its trailer after its last vagga; every rend is kept. Anything left between sections is
printed for review rather than placed by guesswork; EXTRA lists the decisions taken on it.

Usage: mark_an_tika.py TIKA ATTHAKATHA [--write]"""
import re, sys, os, difflib
import xml.etree.ElementTree as ET
sys.path.insert(0, os.path.dirname(__file__))
from markup import *

from markup import MASTER

# Notes on sections the ṭīkā has no heading for, placed by decision: {file: {paragraph n: aṭṭhakathā id}}.
NOTES = {
    's0402t.tik.xml': {'147-156': 'an3_3_5',   # "मङ्गलवग्गो उत्तानत्थोयेव", the skipped (१५) मङ्गलवग्गो
                       '211-220': 'an4_5_2'},  # "दुतियो परिसावग्गो उत्तानत्थोयेव", the skipped (२२) परिसावग्गो
    's0403t.tik.xml': {'107-116': 'an6_3'},   # = the aṭṭhakathā's and the mūla's ११. तिकवग्गो, ¶107-116
}

# Notes left outside every section by decision; no section runs past one. {file: {paragraph n}}
DIVLESS = {
    # ¶231-302 "चतुत्थवग्गादीनि उत्तानत्थानि" covers an5_5_4, an5_5_5 and an5_6, across the end of the 5th
    # paṇṇāsaka. [fsnow, 2026-10-19] "divless"
    's0403t.tik.xml': {'231-302'},
    # ¶81-626 "सेसं उत्तानमेव" covers an8_2_4, an8_2_5 and an8_3, across the end of the 2nd paṇṇāsaka: the
    # same shape as s0403t's ¶231-302, treated the same way.
    's0404t.tik.xml': {'81-626'},
}

# A section's closing line that names it differently from its heading: {file: {id: closing text}}.
OWN_CLOSE = {
    's0404t.tik.xml': {'an8_2_2': 'चापालवग्गवण्णना निट्ठिता।'},   # heading (७) २. भूमिचालवग्गो
}

# A book's own last line that should be its trailer, where the existing book markup left it a <p>.
BOOK_TRAILER = {
    's0404t.tik.xml': ['दसकनिपातवण्णनाय अनुत्तानत्थदीपना समत्ता।'],
}
CLOSING = re.compile('निट्ठित|समत्त')
TEXT = re.compile(r'^<(p|head|trailer) rend="([^"]+)"[^>]*>(.*)</\1>$')


def plain(s): return re.sub(r'<[^>]+>', '', s).strip()


def key(heading):
    """Comparable name: drop the numbering, the commentary suffixes and punctuation."""
    h = re.sub(r'^\(?[०-९\-]+\)?\.?\s*', '', heading)
    h = re.sub(r'^[०-९\-]+\.\s*', '', h)
    h = re.sub(r'(आदि)?वण्णना$', '', h)
    h = re.sub('[\\s\u200d।.()]', '', h)
    return re.sub('[ा-ौं]+$', '', h)   # final vowel sign or anusvara: बालवग्गो -> बालवग्ग


def squash(s): return re.sub('[\\s\u200d]', '', s)


PARENT_CLOSE = re.compile('पण्णासकं? (निट्ठित|समत्त)|मनोरथपूरणिया|निपातवण्णनाय|निपातस्स संवण्णना|सब्बाकारेन निट्ठिता|भागो निट्ठितो')


def skeleton(templates):
    """Template sections per book: (id, type, parent id, head text). `templates` is one file or several,
    comma-separated (the mūla of AN9-11 is split over s0404m2-m4)."""
    out = []
    for att in templates.split(','):
        s = open(os.path.join(MASTER, att), 'rb').read().decode('utf-16')
        out += _skeleton(ET.fromstring(re.sub(r'<\?xml[^>]*\?>', '', s, count=1)))
    return out


def _skeleton(root):
    out = []
    def walk(el, parent):
        for c in el:
            if c.tag == 'div':
                h = c.find('head')
                out.append((c.get('id'), c.get('type'), parent, ''.join(h.itertext()).strip() if h is not None else ''))
                walk(c, c.get('id'))
            else:
                walk(c, parent)
    walk(root, None)
    return out


def build(tika, att, extra=None):
    lines = read(os.path.join(MASTER, tika))
    skel = skeleton(att)
    books = [(i, re.search(r'id="([^"]+)"', l).group(1)) for i, l in enumerate(lines)
             if l.startswith('<div ') and 'type="book"' in l]
    book_ends = []
    for k, (i, bid) in enumerate(books):
        depth, j = 0, i
        while True:
            if lines[j].startswith('<div '): depth += 1
            if lines[j] == '</div>': depth -= 1
            if depth == 0: break
            j += 1
        book_ends.append(j)

    sections, report, between, notes = [], [], [], []
    for (bstart, bid), bend in zip(books, book_ends):
        if any(lines[j].startswith('<div ') for j in range(bstart + 1, bend)):
            continue   # already marked inside (s0404a's AN8): leave it alone
        heads = []
        for i in range(bstart, bend):
            m = TEXT.match(lines[i])
            if m and m.group(1) in ('p', 'head') and m.group(2) in ('title', 'chapter'):
                heads.append((i, m.group(2), plain(m.group(3))))
        att_secs = [s for s in skel if s[0].startswith(bid + '_') and s[0].count('_') >= 1 and s[1] != 'book']
        # The book's own closing lines (the commentary's colophon, a volume line) end its last section.
        BOOK_CLOSE = re.compile('मनोरथपूरणिया|निपातवण्णनाय|निपातस्स संवण्णना|सब्बाकारेन निट्ठिता|भागो निट्ठितो')
        book_close = next((j for j in range(heads[-1][0] if heads else bstart, bend)
                           if TEXT.match(lines[j]) and BOOK_CLOSE.search(plain(lines[j]))), bend)
        parent = bid
        placed = []
        for i, rend, text in heads:
            if rend == 'title':
                cands = [s for s in att_secs if s[2] == bid and s[1] == 'pannasaka']
            else:
                cands = [s for s in att_secs if s[2] in (parent, bid) and s[1] != 'pannasaka']
            best = max(cands, key=lambda s: difflib.SequenceMatcher(None, key(s[3]), key(text)).ratio())
            score = difflib.SequenceMatcher(None, key(best[3]), key(text)).ratio()
            assert score >= 0.8, f'{tika}: no aṭṭhakathā section for {text!r} (best {best[3]!r} {score:.2f})'
            if best[1] == 'pannasaka': parent = best[0]
            elif best[2] == bid: parent = bid
            placed.append((i, best))
            report.append((best[0], best[1], text, best[3], score))

        # Notes placed by decision: paragraph n -> aṭṭhakathā id. Each is a section of its own, with no head.
        note_lines = set()
        for n, nid in (NOTES.get(tika, {}) or {}).items():
            j = next((j for j in range(bstart, bend) if lines[j].startswith(f'<p rend="bodytext" n="{n}">')), None)
            if j is None: continue
            sec = next(s for s in att_secs if s[0] == nid)
            placed.append((j, sec)); note_lines.add(j)
            report.append((nid, sec[1], '(note) ' + plain(lines[j]), sec[3], 1.0))
        placed.sort(key=lambda p: p[0])
        stops = sorted(j for j in range(bstart, bend) for n in DIVLESS.get(tika, set())
                       if lines[j].startswith(f'<p rend="bodytext" n="{n}">'))

        # Ends. A section runs to the line before the next section that is not inside it.
        for k, (i, (sid, stype, spar, _)) in enumerate(placed):
            if i in note_lines:
                sections.append(Section(sid, stype, i, i, head=False, trailer=False))
                continue
            nxt = next((j for j, s in placed[k + 1:] if not s[0].startswith(sid + '_')), None)
            limit = nxt if nxt is not None else book_close
            limit = min([limit] + [j for j in stops if j > i])
            if stype == 'pannasaka':
                inner_last = max([j for j, s in placed if s[0].startswith(sid + '_')], default=i)
                close = next((j for j in range(inner_last, limit)
                              if re.search('पण्णासकं? (निट्ठित|समत्त)', plain(lines[j]))), None)
                end = close if close is not None else last_element(lines, limit)
            else:
                nxt_any = next((j for j, _ in placed[k + 1:]), limit)
                lim = min(limit, nxt_any)
                own = key(plain(lines[i]))
                def names(j):
                    """A closing line of this section: centre or ni\u1e6d\u1e6dhita, and it names the section."""
                    mm = TEXT.match(lines[j])
                    return bool(mm) and bool(own) and own in squash(plain(lines[j])) and \
                        (mm.group(2) == 'centre' or bool(CLOSING.search(plain(lines[j]))))
                def existing_trailer(j):
                    """A <trailer> already in the file closes a section, unless it closes a paṇṇāsaka or the book."""
                    return lines[j].startswith('<trailer ') and not PARENT_CLOSE.search(plain(lines[j]))
                closes = [j for j in range(i + 1, lim) if names(j) or existing_trailer(j)]
                forced = OWN_CLOSE.get(tika, {}).get(sid)
                if forced:
                    closes = [find(lines, forced, start=i)]
                    assert closes[0] < lim, f'{sid}: {forced} is outside the section'
                if closes:
                    end = closes[-1]
                    after = [j for j in range(end + 1, lim) if lines[j] and not PARENT_CLOSE.search(plain(lines[j]))]
                    if after: notes.append((sid, [plain(lines[j])[:80] for j in after]))
                else:
                    end = lim - 1
                    while end > i and (lines[end] == '' or PARENT_CLOSE.search(plain(lines[end]))): end -= 1
            m = TEXT.match(lines[end])
            if stype == 'pannasaka':
                trailer = bool(m) and m.group(1) == 'p' and bool(PARENT_CLOSE.search(plain(lines[end])))
            else:
                trailer = bool(m) and m.group(1) == 'p' and (names(end) or sid in OWN_CLOSE.get(tika, {}))
            sections.append(Section(sid, stype, i, end, head=lines[i].startswith('<p '), trailer=trailer))

    for s in (extra or []): sections.append(s)
    sections.sort(key=lambda s: (s.start, -s.end))
    result = apply(lines, sections)
    for text in BOOK_TRAILER.get(tika, []):
        j = find(result, text)
        assert result[j + 1] == '</div>', f'{text}: not the last line of its book'
        result[j] = retag(result[j], 'trailer')
    ids = check(lines, result)

    # What is left outside every section, inside a book (other than the book's own opening and closing).
    root = ET.fromstring('\n'.join(result[1:]))
    for b in root.iter('div'):
        if not any(c.tag == 'div' for c in b): continue
        seen_section = False
        tail = []
        for c in b:
            if c.tag == 'div':
                if tail: between.append((b.get('id'), prev, [t for t in tail]))
                seen_section, tail, prev = True, [], c.get('id')
            elif seen_section:
                tail.append(f'<{c.tag} {c.get("rend")} n={c.get("n")}> ' + re.sub(r'\s+', ' ', ''.join(c.itertext())).strip()[:70])
        if tail: between.append((b.get('id'), prev + ' (at the end)', tail))
    return lines, result, sections, report, between, ids, notes


if __name__ == '__main__':
    tika, att = sys.argv[1], sys.argv[2]
    lines, result, sections, report, between, ids, notes = build(tika, att)
    print(f'{"id":10} {"type":10} ṭīkā heading  ←  aṭṭhakathā heading (match)')
    for sid, stype, t, a, sc in report:
        print(f'{sid:10} {stype:10} {t[:34]:34} ← {a[:34]} ({sc:.2f})')
    print('\nsection → closing (trailer?)')
    for s in sections:
        print(f'{s.id:10} → {plain(lines[s.end])[:50]} {"[trailer]" if s.trailer else "(stays <p>)"}')
    print("\nafter a section's own closing line, inside it (candidate notes on skipped sections):")
    for sid, items in notes:
        print(f'  {sid}:'); [print('     ' + x) for x in items]
    print('\noutside every section:')
    for bid, after, items in between:
        print(f'  in {bid}, after {after}:'); [print('     ' + x) for x in items]
    print(f'\nlines {len(lines)} -> {len(result)}; ids {len(ids)}; strip check OK')
    if '--write' in sys.argv: write(os.path.join(MASTER, tika), result); print('written')
