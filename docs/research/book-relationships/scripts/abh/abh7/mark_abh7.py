"""
[fsnow, 2026-10-22, picked "Rename it 'paccaya'"]: the paccaya-intro container abh7_1_1_0 is type paccaya, so "intro" means only the opening sections.
Mark up the Paṭṭhāna mūla (abh03m7-m11, book abh7): a DRY-RUN proposal, never written without review.

Levels [suggestion]:
  abh7                book, repeated in each of the five files (as kn10 in s0510m1/m2)
  abh7_N              method: the four methods (1 धम्मानुलोम, 2 धम्मपच्चनीय, 3 धम्मानुलोमपच्चनीय,
                      4 धम्मपच्चनीयानुलोम). The aṭṭhakathā heads methods 2-4 as one section each
                      ("७-१२. पच्चनीयपट्ठानवण्णना"), and the mūla closes each ("पच्चनीयपट्ठानं निट्ठितं।").
  abh7_N_P            patthana: the six in each method (1 tika, 2 duka, 3 dukatika, 4 tikaduka, 5 tikatika,
                      6 dukaduka), numbered as the aṭṭhakathā numbers them ("२. दुकपट्ठानवण्णना").
  abh7_1_P_X          the mūla's chapter headings inside paṭṭhānas 1-4 of the anuloma method: tikas
                      (tika), gocchakas (gocchaka), and the dukatika / tikaduka groups (chapter), numbered by
                      the printed number (a range "६-७." takes its first; "N-1" and "1-N" take the duka N).
  abh7_1_1_0          the paccaya intro before the first tika, headless (type intro), holding
                      (१) पच्चयुद्देसो, (२) पच्चयनिद्देसो, ३. पुच्छावारो as abh7_1_1_0_1.._3.
Closing lines follow the KN conventions: a closing is a trailer only if it names the section and is its
last element.

[fsnow, 2026-10-22] accepted all the suggestions above (relayed by the coordinator), and: the ṭīkās
(abh03t, abh05t) get the book div abh7 only; abh03a gets a head-less abh7_0 for its opening gāthās; m11's
sub-titles stay unmarked; abh03a's range "५-२२." is abh7_1_1_5. ChapterListTypes
"book,patthana,tika,gocchaka,chapter".

[fsnow, 2026-10-22] picked "Don't list patthana in abh03a", whose option also renamed the type 'naya' to
'method' (no collision with the Dhātukathā's nayas); "One type: intro" for every opening _0; for the
ṭīkās, "Use them as <head>": their section divs take their subhead/title lines as heads, rends kept.

Every file is read through markup.strip, so a run on an already-marked file gives the same result.

Usage: mark_abh7.py [MULA_FILE...] [--write]   --write writes only the five mūla files.
parts(name) returns the Paṭṭhāna sections of a shared file (abh03a, abh03t, abh05t) for the coordinator
to merge; it never writes."""
import re, sys, os
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', '..'))
from markup import *
from mark_an import key, squash, plain, TEXT, CLOSING

FILES = ['abh03m7.mul.xml', 'abh03m8.mul.xml', 'abh03m9.mul.xml', 'abh03m10.mul.xml', 'abh03m11.mul.xml']
DEV = str.maketrans('०१२३४५६७८९', '0123456789')
NAYA = [('धम्मपच्\u200dचनीयानुलोमे', 4), ('धम्मानुलोमपच्\u200dचनीये', 3), ('धम्मपच्\u200dचनीये', 2), ('धम्मानुलोमे', 1)]
NAYA_CLOSE = {1: 'अनुलोमपट्ठानं निट्ठितं।', 2: 'पच्\u200dचनीयपट्ठानं निट्ठितं।',
              3: 'अनुलोमपच्\u200dचनीयपट्ठानं निट्ठितं।', 4: 'पच्\u200dचनीयानुलोमपट्ठानं निट्ठितं।'}
KIND = [('दुकतिकपट्ठानं', 3), ('तिकदुकपट्ठानं', 4), ('तिकतिकपट्ठानं', 5), ('दुकदुकपट्ठानं', 6),
        ('तिकपट्ठानं', 1), ('दुकपट्ठानं', 2)]
THIRD_TYPE = {1: 'tika', 2: 'gocchaka', 3: 'chapter', 4: 'chapter'}
# [suggestion] The Piṭṭhiduka (m10, after the Kilesagocchaka) has no heading, only its closing
# "पिट्ठिदुकं निट्ठितं।": a headless section, numbered on from the 12 gocchakas.
PITTHI = ('पिट्ठिदुकं निट्ठितं।', 13)


def text(lines, i):
    m = TEXT.match(lines[i])
    return (m.group(2), plain(m.group(3))) if m and m.group(1) == 'p' else (None, None)


def naya_of(t):
    return next((n for w, n in NAYA if t.startswith(w)), None)


def kind_of(t):
    return next((p for w, p in KIND if t.endswith(w)), None)


def nums(t):
    m = re.match(r'^\(?([०-९]+)\)?(?:-([०-९]+))?\.?\s', t)
    return (int(m.group(1).translate(DEV)), int(m.group(2).translate(DEV)) if m.group(2) else None) if m else (None, None)


def names(lines, j, head):
    r, t = text(lines, j)
    return t is not None and (r == 'centre' or bool(CLOSING.search(t))) and key(head) in squash(t)


def load(name):
    """The file's lines with any existing markup stripped: the same lines as the unmarked original."""
    return strip(read(os.path.join(MASTER, name)))


def build(name):
    lines = load(name)
    body_end = lines.index('</body>')
    bstart = next(i for i in range(body_end) if text(lines, i)[0] == 'book')
    bend = last_element(lines, body_end)
    btr = bool(CLOSING.search(text(lines, bend)[1]))
    secs = [Section('abh7', 'book', bstart, bend, trailer=btr)]
    limit = bend if btr else bend + 1
    report, notes = [], []

    # Paṭṭhāna headings: a title/chapter "<naya> <kind>पट्ठानं", or m7/m8's two titles "धम्मानुलोमे" / "तिकपट्ठानं".
    pts, naya_head = [], {}
    for i in range(bstart, limit):
        r, t = text(lines, i)
        if r in ('title', 'chapter') and t:
            if t == 'धम्मानुलोमे':
                naya_head[1] = i; continue
            if t.endswith('पट्ठानं') and kind_of(t):
                n = naya_of(t) or (1 if 1 in naya_head else None)
                assert n, f'{name}:{i+1} no naya for {t}'
                pts.append((i, n, kind_of(t), t))
    # Naya divs.
    nayas = []
    for n in sorted({n for _, n, _, _ in pts}):
        own = [p for p in pts if p[1] == n]
        start = naya_head.get(n, own[0][0])
        nxt = [p[0] for p in pts if p[1] > n]
        lim = min(nxt) if nxt else limit
        close = next((j for j in range(own[-1][0], lim) if text(lines, j)[1] == NAYA_CLOSE[n]), None)
        end = close if close is not None else last_element(lines, lim)
        secs.append(Section(f'abh7_{n}', 'method', start, end, head=n in naya_head, trailer=close is not None))
        nayas.append((n, start, end, close))
        report.append((f'abh7_{n}', text(lines, start)[1] if n in naya_head else '(no head)', text(lines, end)[1], close is not None))
    # Paṭṭhāna divs.
    for k, (i, n, p, t) in enumerate(pts):
        nend = next(e for nn, s, e, c in nayas if nn == n)
        nclose = next(c for nn, s, e, c in nayas if nn == n)
        lim = pts[k + 1][0] if k + 1 < len(pts) and pts[k + 1][1] == n else (nclose if nclose is not None else nend + 1)
        end = last_element(lines, lim)
        tr = names(lines, end, t.split()[-1]) if ' ' in t else names(lines, end, t)
        # The closing names naya and paṭṭhāna together ("धम्मानुलोमे तिकपट्ठानं निट्ठितं।").
        tr = text(lines, end)[1] in (t + ' निट्ठितं।', 'धम्मानुलोमे ' + t + ' निट्ठितं।')
        pid = f'abh7_{n}_{p}'
        secs.append(Section(pid, 'patthana', i, end, trailer=tr))
        report.append((pid, t, text(lines, end)[1], tr))
        if n != 1 or p > 4: continue
        # Third level: the chapter headings inside.
        inner_lim = end if tr else end + 1
        chs = [j for j in range(i + 1, inner_lim) if text(lines, j)[0] == 'chapter']
        intro = [j for j in chs if text(lines, j)[1].startswith('(') or 'पुच्छावारो' in text(lines, j)[1]]
        if intro:
            first_tika = next(j for j in chs if j not in intro)
            c0 = f'{pid}_0'
            secs.append(Section(c0, 'paccaya', intro[0], last_element(lines, first_tika), head=False))
            report.append((c0, '(no head)', text(lines, last_element(lines, first_tika))[1], False))
            for q, j in enumerate(intro):
                lim3 = intro[q + 1] if q + 1 < len(intro) else first_tika
                e3 = last_element(lines, lim3); h = text(lines, j)[1]
                tr3 = names(lines, e3, h)
                sid = f'{c0}_{nums(h)[0]}'
                secs.append(Section(sid, 'chapter', j, e3, trailer=tr3))
                report.append((sid, h, text(lines, e3)[1], tr3))
            chs = [j for j in chs if j not in intro]
        starts = []
        for j in chs:
            h = text(lines, j)[1]; a, b = nums(h)
            starts.append((j, b if (p == 4 and b) else a, h, True))
        if p == 2 and (pj := next((j for j in range(i, inner_lim) if text(lines, j)[1] == PITHI_TEXT), None)) is not None:
            # headless: it opens after the previous section's last element
            prev = max(s for s, *_ in starts if s < pj)
            starts.append((None, PITTHI[1], '(no head)', False))
        for q, (j, num, h, has_head) in enumerate(starts):
            if j is None: continue
            lim3 = starts[q + 1][0] if q + 1 < len(starts) and starts[q + 1][0] is not None else inner_lim
            e3 = last_element(lines, lim3)
            if q + 1 < len(starts) and starts[q + 1][0] is None:
                # the section before the Piṭṭhiduka ends on its own closing line
                e3 = next(x for x in range(j, lim3) if names(lines, x, h) and squash(text(lines, x)[1]).startswith(key(h)))
            tr3 = names(lines, e3, h) and squash(text(lines, e3)[1]).startswith(key(h))
            sid = f'{pid}_{num}'
            secs.append(Section(sid, THIRD_TYPE[p], j, e3, trailer=tr3))
            report.append((sid, h, text(lines, e3)[1], tr3))
            if q + 1 < len(starts) and starts[q + 1][0] is None:
                ps = next(x for x in range(e3 + 1, inner_lim) if lines[x])
                pe = next(x for x in range(ps, inner_lim) if text(lines, x)[1] == PITHI_TEXT)
                sidp = f'{pid}_{PITTHI[1]}'
                secs.append(Section(sidp, THIRD_TYPE[p], ps, pe, head=False, trailer=True))
                report.append((sidp, '(no head)', text(lines, pe)[1], True))
    result = apply(lines, secs)
    ids = check(lines, result)
    return lines, result, report, ids


PITHI_TEXT = PITTHI[0]

def find_sq(lines, t, start=0, end=None, rend=None):
    """First line from `start` whose text equals t, ignoring joiners and spacing."""
    for i in range(start, len(lines) if end is None else end):
        r, x = text(lines, i)
        if x is not None and squash(x) == squash(t) and (rend is None or r == rend): return i
    raise KeyError(t)


def parts(name):
    """The Paṭṭhāna sections (book div abh7 included) of a shared commentary file, as markup.Section with
    0-based line indices into strip(read(file)) - for an unmarked file, the file's own lines."""
    lines = load(name)
    S = Section
    if name in ('abh03t.tik.xml', 'abh05t.nrf.xml'):
        # [fsnow, 2026-10-22] "Add head-less divs (Recommended)", then "Use them as <head>": sections bounded by
        # the ṭīkās' own closing lines, with the aṭṭhakathā's ids, each headed by its own subhead/title line.
        # Linking "just needs to be unambiguous". The method, paṭṭhāna and intro containers stay head-less.
        title = 'पट्ठानपकरण-मूलटीका' if name == 'abh03t.tik.xml' else 'पट्ठानपकरण-अनुटीका'
        b = find_sq(lines, title, rend='chapter')
        be = find_sq(lines, title + ' समत्ता।', b)
        f = lambda t, a=b, r=None: find_sq(lines, t, a, be + 1, rend=r)
        g = f('गन्थारम्भवण्णना', r='subhead'); ge = f('गन्थारम्भवण्णना निट्ठिता।', g)
        u = f('पच्चयुद्देसवण्णना', ge, 'subhead'); ue = f('पच्चयुद्देसवण्णना निट्ठिता।', u)
        nd = f('पच्चयनिद्देसो', ue, 'subhead'); nde = f('पच्चयनिद्देसवण्णना निट्ठिता।', nd)
        pv = f('पुच्छावारो', nde, 'title'); pve = f('पुच्छावारवण्णना निट्ठिता।', pv)
        k1 = f('१. कुसलत्तिकं', pve, 'subhead'); k1e = f('कुसलत्तिकवण्णना निट्ठिता।', k1)
        out = [S('abh7', 'book', b, be, trailer=True),
               S('abh7_0', 'intro', g, ge, trailer=True),
               S('abh7_1_1_0', 'paccaya', u, pve, head=False),
               S('abh7_1_1_0_1', 'chapter', u, ue, trailer=True),
               # The pakiṇṇaka-vinicchaya after this closing stays in abh7_1_1_0, between _0_2 and _0_3.
               S('abh7_1_1_0_2', 'chapter', nd, nde, trailer=True),
               S('abh7_1_1_0_3', 'chapter', pv, pve, trailer=True),
               S('abh7_1_1_1', 'tika', k1, k1e, trailer=True)]
        a = k1e
        # Tikas 6 and 8 are inside the aṭṭhakathā's range section abh7_1_1_5 ("५-२२."); they take the mūla's ids.
        for h, n in [('वेदनात्तिक', 2), ('विपाकत्तिक', 3), ('उपादिन्नत्तिक', 4), ('वितक्कत्तिक', 6),
                     ('दस्सनेनपहातब्बत्तिक', 8)]:
            i = f(f'{"०१२३४५६७८"[n]}. {h}वण्णना', a, 'subhead'); a = f(f'{h}वण्णना निट्ठिता।', i)
            out.append(S(f'abh7_1_1_{n}', 'tika', i, a, trailer=True))
        # The naya and the tika-paṭṭhāna end on the last tika's closing, which names neither. The
        # "पुच्छागणनगाथायो" after it (abh03t) count the whole Paṭṭhāna: book content, outside abh7_1.
        out[2:2] = [S('abh7_1', 'method', u, a, head=False), S('abh7_1_1', 'patthana', u, a, head=False)]
        return out
    assert name == 'abh03a.att.xml', name
    f = lambda t, a=0, r=None: find_sq(lines, t, a, rend=r)
    b = f('पट्ठानप्पकरण-अट्ठकथा', r='chapter')
    be = f('पट्ठानप्पकरण-अट्ठकथा निट्ठिता।', b)
    u = f('पच्चयुद्देसवण्णना', b, 'chapter')
    t1 = f('धम्मानुलोमे तिकपट्ठानवण्णना।', u)
    n2 = f('७-१२. पच्चनीयपट्ठानवण्णना', u, 'chapter')
    first = next(i for i in range(b + 1, u) if lines[i])
    out = [S('abh7', 'book', b, be, trailer=True),
           S('abh7_0', 'intro', first, last_element(lines, u), head=False),
           S('abh7_1', 'method', u, last_element(lines, n2), head=False),
           S('abh7_1_1', 'patthana', u, t1, head=False, trailer=True)]
    nd = f('पच्चयनिद्देसो', u, 'chapter'); pv = f('पुच्छावारो', nd, 'title'); k1 = f('१. कुसलत्तिकवण्णना', pv, 'chapter')
    out += [S('abh7_1_1_0', 'paccaya', u, last_element(lines, k1), head=False),
            S('abh7_1_1_0_1', 'chapter', u, f('पच्चयुद्देसवण्णना।', u), trailer=True),
            S('abh7_1_1_0_2', 'chapter', nd, f('पच्चयनिद्देसवारवण्णना निट्ठिता।', nd), trailer=True),
            S('abh7_1_1_0_3', 'chapter', pv, f('पुच्छावारवण्णना निट्ठिता।', pv), trailer=True)]
    tikas = [('१. कुसलत्तिकवण्णना', 1, 'निट्ठिता च कुसलत्तिकपट्ठानस्स वण्णनाति।'),
             ('२. वेदनात्तिकवण्णना', 2, 'वेदनात्तिकवण्णना।'), ('३. विपाकत्तिकवण्णना', 3, 'विपाकत्तिकवण्णना।'),
             ('४. उपादिन्नत्तिकवण्णना', 4, 'उपादिन्नत्तिकवण्णना।'), ('५-२२. सङ्किलिट्ठत्तिकादिवण्णना', 5, None)]
    for h, n, c in tikas:
        i = f(h, u, 'chapter')
        e = f(c, i) if c else last_element(lines, t1)
        out.append(S(f'abh7_1_1_{n}', 'tika', i, e, trailer=bool(c)))
    for h, n in [('दुकपट्ठान', 2), ('दुकतिकपट्ठान', 3), ('तिकदुकपट्ठान', 4), ('तिकतिकपट्ठान', 5), ('दुकदुकपट्ठान', 6)]:
        i = f(f'{"२३४५६"[n - 2]}. {h}वण्णना', t1, 'chapter')
        out.append(S(f'abh7_1_{n}', 'patthana', i, f(f'{h}वण्णना।', i), trailer=True))
    for h, n in [('७-१२. पच्चनीयपट्ठानवण्णना', 2), ('१३-१८. अनुलोमपच्चनीयपट्ठानवण्णना', 3),
                 ('१९-२४. पच्चनीयानुलोमपट्ठानवण्णना', 4)]:
        i = f(h, u, 'chapter')
        out.append(S(f'abh7_{n}', 'method', i, f(h.split(' ', 1)[1] + '।', i), trailer=True))
    return out


if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    for name in args or FILES:
        lines, result, report, ids = build(name)
        print(f'== {name}: lines {len(lines)} -> {len(result)}; ids {len(ids)}; strip check OK')
        for sid, h, end, tr in report:
            print(f'{"  " * sid.count("_")}{sid:16} {h[:36]:36} → {(end or "")[:40]} {"[trailer]" if tr else ""}')
        cl = chapter_list(result, {'book', 'patthana', 'tika', 'gocchaka', 'chapter'})
        print(f'   chapter list ({len(cl)} items, types book,patthana,tika,gocchaka,chapter)')
        if '--write' in sys.argv:
            assert name in FILES, f'--write is for the five mūla files only, not {name}'
            write(os.path.join(MASTER, name), result); print('   written')
