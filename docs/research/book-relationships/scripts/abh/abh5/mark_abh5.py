"""Dry run: Kathāvatthu (abh5) markup proposal. The mūla abh03m3 on its own; the Kathāvatthu parts of the
shared commentary files (abh03a, abh03t, abh05t) as a part only, so other pakaraṇas stay untouched.

Proposal [suggestion], not decided:
  - book abh5; 23 vaggas abh5_N (type vagga); kathās abh5_N_M (type katha), M = the kathā's number within
    its vagga (the mūla prints "(१०) १. परूपहारकथा": global, then local);
  - ChapterListTypes "book,vagga" (24 items); kathās are marked for linking but not listed;
  - mūla vagga 1 has no heading: a div without <head> (head=False);
  - a vagga's closing line is its trailer; its uddāna, and any paṇṇāsaka closing, follow it between vaggas,
    as the uddānas do in the MN mūla ([observed] s0201m: "</div>", then "तस्सुद्दानं", then the next vagga);
  - the running title the mūla repeats before each kathā ("२. दुतियवग्गो") sits between kathā divs;
  - commentaries: the nidāna/ganthārambha opening is abh5_0 (KN convention, type of its siblings).
Never writes: it prints the review table and runs markup.check on the result in memory.

Usage: mark_abh5.py"""
import re, sys, os, difflib
SCRATCH = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
sys.path.insert(0, SCRATCH)
from markup import *
from mark_an import key, squash, plain, TEXT, CLOSING

DEV = str.maketrans('०१२३४५६७८९', '0123456789')
LEAD = re.compile(r'^(?:\([०-९\-]+\)\s*([०-९]+)(?:-[०-९]+)*\.?|([०-९]+)(?:-[०-९]+)*\.)\s')   # "(१०६-१०८) १-३. …": the first
VAGGA = re.compile(r'^([०-९]+)\.\s*(\S+वग्गो)$')


def txt(lines, i):
    m = TEXT.match(lines[i])
    return (m.group(2), plain(m.group(3))) if m and m.group(1) == 'p' else (None, None)


def lead(t):
    # After a global number the local one may lack its dot: "(१८८) ३ सामञ्ञफलकथा".
    m = LEAD.match(t or '')
    return int((m.group(1) or m.group(2)).translate(DEV)) if m else None


def runs_on(lines, j):
    r, t = txt(lines, j)
    return r == 'centre' and not re.search('[।॥]$', t)


def closing_start(lines, t):
    k = last_element(lines, t)
    while k > 0 and runs_on(lines, k):
        t = k; k = last_element(lines, k)
    return t


def names(lines, j, stems, fuzzy=False):
    r, t = txt(lines, j)
    m = TEXT.match(lines[j])
    if m: t = plain(re.sub(r'<note>.*?</note>', '', m.group(3)))     # variant readings are not the name
    if not (r == 'centre' or (t and CLOSING.search(t))): return False
    s = squash(t)
    if any(st and st in s for st in stems): return True
    # A kathā's closing often words its name a little differently from its heading: "पटुप्पन्नञाणकथा
    # निट्ठिता।" for "पटुप्पन्नकथा", "अत्थि अरहतो पुञ्ञूपचयोतिकथा" for "अरहतो पुञ्ञूपचयकथा".
    name = key(re.sub(r'\s*(निट्ठिता|समत्ता)?।$', '', t))
    return fuzzy and any(difflib.SequenceMatcher(None, st, name).ratio() >= 0.75 for st in stems)


def vagga_stems(heading):
    """"२. दुतियवग्गो" closes as "दुतियवग्गो।", "दुतियो वग्गो।" or "दुतियवग्गवण्णना निट्ठिता।"."""
    k = key(heading)                          # दुतियवग्ग
    ordinal = k[:-len('वग्ग')]                # दुतिय
    return [k, ordinal + 'ोवग्ग']


def kathas_of_mula():
    """{vagga: {local number: key}} from the mūla."""
    # Stripped, so this reads the same whether or not the mūla has been marked already.
    lines = strip(read(os.path.join(MASTER, 'abh03m3.mul.xml')))
    out, n = {1: {}}, 1
    for i in range(len(lines)):
        r, t = txt(lines, i)
        if r == 'chapter' and VAGGA.match(t): n = lead(t); out[n] = {}
        elif (r == 'chapter' and n == 1) or (r in ('subhead', 'title') and n > 1 and t.startswith('(')):
            out[n][lead(t)] = key(t)
    return out


def similar(a, b):
    return a in b or b in a or difflib.SequenceMatcher(None, a, b).ratio() >= 0.6


def mark(lines, part, mula=None):
    """part: (book head line, book closing line, opening heading line or None). Returns sections, report."""
    bstart, bend, opening = part
    secs, rep = [Section('abh5', 'book', bstart, bend, head=True, trailer=True)], []
    bclose = closing_start(lines, bend)
    for j in range(bstart, bclose):          # the commentaries' nigamana is book closing matter
        if txt(lines, j)[1] == 'निगमनकथा': bclose = j
    # Vagga starts.
    vs = []
    for i in range(bstart + 1, bclose):
        r, t = txt(lines, i)
        if r in ('chapter', 'subhead') and t and VAGGA.match(t):
            if not (mula is None and r == 'subhead'): vs.append((i, lead(t), t, True))
        elif t == 'महावग्गो' and r == 'title':
            vs.append((i, 1, t, True))
    if not any(n == 1 for _, n, _, _ in vs):        # the mūla: vagga 1 opens at its first kathā, no heading
        first = next(i for i in range(bstart + 1, bclose) if txt(lines, i)[0] == 'chapter')
        vs.insert(0, (first, 1, None, False))
    # The opening section.
    if opening is not None:
        lim = vs[0][0]
        end = last_element(lines, lim)
        _, ot = txt(lines, opening)
        tr = names(lines, end, [key(ot)])
        secs.append(Section('abh5_0', 'vagga', opening, end, trailer=tr))
        rep.append(('abh5_0', ot, txt(lines, end)[1], tr))
    for k, (i, n, t, has_head) in enumerate(vs):
        lim = vs[k + 1][0] if k + 1 < len(vs) else bclose
        stems = vagga_stems(t) if t and t != 'महावग्गो' else ['महावग्ग', 'पठमवग्ग']
        closes = [j for j in range(i + 1, lim) if names(lines, j, stems)]
        end = closes[-1] if closes else last_element(lines, lim)
        tr = bool(closes)
        sid = f'abh5_{n}'
        secs.append(Section(sid, 'vagga', i, end, head=has_head, trailer=tr))
        rep.append((sid, t or '(no heading)', txt(lines, end)[1], tr))
        inner = closing_start(lines, end) if tr else end + 1
        # Kathā starts.
        ks, used = [], set()
        for j in range(i if not has_head else i + 1, inner):
            r, kt = txt(lines, j)
            if r not in ('chapter', 'subhead', 'title') or VAGGA.match(kt or '') or kt == 'महावग्गो': continue
            m = lead(kt)
            if m is None or m in used: continue
            if mula is None:
                if (n == 1 and r == 'chapter') or (n > 1 and r in ('subhead', 'title') and kt.startswith('(')):
                    ks.append((j, m, kt)); used.add(m)
            elif m in mula.get(n, {}) and similar(key(kt), mula[n][m]) and (not ks or m > ks[-1][1]):
                ks.append((j, m, kt)); used.add(m)
        for q, (j, m, kt) in enumerate(ks):
            lim2 = ks[q + 1][0] if q + 1 < len(ks) else inner
            # The mūla repeats the vagga's title before each kathā: it stays between kathās.
            p = last_element(lines, lim2)
            while txt(lines, p)[0] == 'title' and VAGGA.match(txt(lines, p)[1] or ''):
                lim2 = p; p = last_element(lines, p)
            end2 = last_element(lines, lim2)
            if q + 1 == len(ks):
                # The last kathā ends at its own closing: what follows (vagga 1's uddāna) is the vagga's.
                own = [x for x in range(j + 1, lim2) if names(lines, x, [key(kt)], fuzzy=True)]
                if own: end2 = own[-1]
            tr2 = names(lines, end2, [key(kt)], fuzzy=True)
            sid2 = f'{sid}_{m}'
            secs.append(Section(sid2, 'katha', j, end2, trailer=tr2))
            rep.append((sid2, kt, txt(lines, end2)[1], tr2))
    return secs, rep


# Where each shared file's Kathāvatthu part is: (book head text, its rend; book closing text; opening heading).
PARTS = {
    'abh03a.att.xml': (('कथावत्थु-अट्ठकथा', 'chapter'), 'कथावत्थु-अट्ठकथा निट्ठिता।', ('निदानकथा', 'chapter')),
    'abh03t.tik.xml': (('कथावत्थुपकरण-मूलटीका', 'chapter'), 'कथावत्थुपकरण-मूलटीका समत्ता।',
                       ('गन्थारम्भकथावण्णना', None)),
    'abh05t.nrf.xml': (('कथावत्थुपकरण-अनुटीका', 'chapter'), 'कथावत्थुपकरण-अनुटीका समत्ता।',
                       ('गन्थारम्भवण्णना', None)),
}


def part_of(lines, name):
    (head, rend), close, (opening, orend) = PARTS[name]
    s = find(lines, head, rend)
    return s, find(lines, close, start=s), find(lines, opening, orend, start=s)


def _sec(secs, sid):
    return next(x for x in secs if x.id == sid)


def parts(name):
    """The Kathāvatthu sections of a shared commentary file (abh03a, abh03t, abh05t), book div abh5
    included, as line indexes into strip(read(file)), which equals the file at main. Accepted 2026-10-22:
    "I accept your Kathāvatthu suggestions". A section whose <head> is not its first element lists the head
    line in extra_heads(name); retag it before apply()."""
    lines = strip(read(os.path.join(MASTER, name)))
    secs, _ = mark(lines, part_of(lines, name), kathas_of_mula())
    # [fsnow, 2026-10-22, "One type: intro"]: every opening section _0 is type intro.
    _sec(secs, 'abh5_0').type = 'intro'
    if name != 'abh03a.att.xml':
        return secs
    def at(text, start):
        return find(lines, text, start=start)
    def bodytext(n):
        return next(i for i, l in enumerate(lines) if l.startswith(f'<p rend="bodytext" n="{n}">'))
    # [fsnow, 2026-10-22, "Head-less _0 for all five"]: abh5_0 opens at the opening gāthās and runs through
    # the nidānakathā, as the ṭīkās' abh5_0 does; "निदानकथा" stays its <head> (see extra_heads).
    o = _sec(secs, 'abh5_0')
    o.start = next(i for i in range(_sec(secs, 'abh5').start + 1, o.start) if lines[i])
    o.head = False
    # Vagga 6: "१०. पथवीधातुसनिदस्सनातिआदिकथावण्णना" (¶465-470) covers the mūla's kathās 8-10; its "१०."
    # looks like a slip for "८.". A div abh5_6_8 headed by it; nothing closes it.
    h = find(lines, '१०. पथवीधातुसनिदस्सनातिआदिकथावण्णना', 'subhead')
    v6 = _sec(secs, 'abh5_6')
    secs.append(Section('abh5_6_8', 'katha', h, last_element(lines, closing_start(lines, v6.end)), trailer=False))
    # Vagga 12, kathās 6-7 (कोलङ्कोल, एकबीजी): ¶646-647 and its closing, with no heading. A div with no
    # <head> and the first kathā's id (the AN note convention); its closing names the kathās it covers and is
    # its last element, so it is the trailer (KN rule).
    start = bodytext('646-647')
    k5 = _sec(secs, 'abh5_12_5')
    assert k5.start < start <= k5.end, 'abh5_12_5 does not cover ¶646-647'
    k5.end = last_element(lines, start)
    k5.trailer = names(lines, k5.end, [key(txt(lines, k5.start)[1])])
    secs.append(Section('abh5_12_6', 'katha', start, at('कोलंकोलएकबीजिकथावण्णना।', start), head=False, trailer=True))
    # Vagga 12, kathā 10 (सत्तमभविक): ¶653 alone, with no heading and no closing.
    start = bodytext('653')
    v12 = _sec(secs, 'abh5_12')
    secs.append(Section('abh5_12_10', 'katha', start, last_element(lines, closing_start(lines, v12.end)),
                        head=False, trailer=False))
    # Vagga 16: kathā 5 ends at its own closing; kathā 6 (रूपं सहेतुकं), ¶757-759, has no heading. [fsnow]
    # "A closing line becomes a section's trailer only if it names that section".
    k = _sec(secs, 'abh5_16_5')
    start = bodytext('757-759')
    assert k.start < start <= k.end
    k.end = at('रूपं हेतूतिकथावण्णना।', k.start); k.trailer = True
    assert k.end < start
    secs.append(Section('abh5_16_6', 'katha', start, at('रूपं सहेतुकन्तिकथावण्णना।', start), head=False, trailer=True))
    return secs


def extra_heads(name):
    """Head lines that are not their section's first element: retag these <p> to <head> before apply()."""
    if name != 'abh03a.att.xml': return []
    lines = strip(read(os.path.join(MASTER, name)))
    s = find(lines, 'कथावत्थु-अट्ठकथा', 'chapter')
    return [find(lines, 'निदानकथा', 'chapter', start=s)]


def run(name, part, mula=None):
    lines = read(os.path.join(MASTER, name))
    secs, rep = mark(lines, part, mula)
    result = apply(lines, secs)
    ids = check(lines, result)
    print(f'== {name}  lines {part[0]+1}-{part[1]+1}: {len(ids)} ids, strip check OK')
    for sid, h, end, tr in rep:
        print(f'{"  " * sid.count("_")}{sid:11} {(h or "")[:34]:34} → {(end or "")[:38]} {"[T]" if tr else "(no trailer)"}')
    vaggas = [s for s in secs if s.type == 'vagga' and s.id != 'abh5_0']
    print(f'   vaggas {len(vaggas)} (with head {sum(s.head for s in vaggas)}), kathās '
          f'{sum(s.type == "katha" for s in secs)}, trailers {sum(s.trailer for s in secs)}')
    return lines, secs


if __name__ == '__main__':
    # The mūla abh03m3 is already written (by the coordinator); this checks the shared files' parts in memory.
    for name in PARTS:
        lines = strip(read(os.path.join(MASTER, name)))
        work = list(lines)
        for i in extra_heads(name): work[i] = retag(work[i], 'head')
        secs = parts(name)
        ids = check(lines, apply(work, secs))
        print(f'{name}: {len(ids)} ids, strip check OK, lines {min(x.start for x in secs) + 1}-{max(x.end for x in secs) + 1}')
