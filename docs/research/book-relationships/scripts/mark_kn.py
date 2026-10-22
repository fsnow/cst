"""Mark up a Khuddaka Nikāya aṭṭhakathā from its marked mūla, or a ṭīkā from its marked aṭṭhakathā.

The aṭṭhakathā has no divs at all, so this adds the book div as well, laid out as in the mūla: the Namo
and nikāya lines stay outside it, and the book's own closing line is its trailer, inside it.

Sections start at the book's numbered section headings and take the mūla's id with the same printed
number (kn1_2 is chapter 2 in s0501m and s0501a), and its type. Headings are matched by number, not name:
the commentary names its sections its own way ("२. सिक्खापदवण्णना" for "२. दससिक्खापदं"); the review table
shows both names. An unnumbered heading before section 1 starts the opening section, _0, with a <head>
[fsnow, 2026-10-19: "yes" on head for _0].

Closing lines follow the AN conventions (markup-conventions.md, "Adding markup"): a section's own closing
line is its trailer, the last of several in a row; colophons of smaller units stay <p>; every rend is kept.
[fsnow, 2026-10-21] "A closing line becomes a section's trailer only if it names that section", and "a
closing line that is the closing of a smaller division that we are not marking up should not be the trailer
of the larger division" - even when it is the section's last line (s0501a's "कतं निदानसोधनं।").

Usage: mark_kn.py FILE [--write]. Per-file settings are in CONFIG."""
import re, sys, os, difflib
import xml.etree.ElementTree as ET
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from markup import *
from mark_an import key, squash, plain, TEXT, CLOSING

DEV = str.maketrans('०१२३४५६७८९', '0123456789')

# Per file: the marked mūla; the rend of its section headings; the opening section's heading, if it has one
# (matched exactly); where the book's own closing matter begins (its first line, matched exactly), so that no
# section runs into it.
CONFIG = {
    's0501a.att.xml': dict(template='s0501m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close='निगमनकथा'),
    's0502a.att.xml': dict(template='s0502m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close='निगमनकथा'),
    's0503a.att.xml': dict(template='s0503m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close='निगमनकथा',
                           # (५) सोणवग्गो closes under its other name, which the mūla's heading also gives.
                           own_close={'kn3_5': 'निट्ठिता च महावग्गवण्णना।'}),
    's0504a.att.xml': dict(template='s0504m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close='निगमनकथा'),
    's0505a.att.xml': dict(template='s0505m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close='निगमनकथा'),
    's0506a.att.xml': dict(template='s0506m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close='निगमनकथा'),
    's0507a.att.xml': dict(template='s0507m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close='निगमनकथा'),
    # Theragāthā: two files, as the Apadāna mūla is two (s0510m1/m2, both book kn10); the first ends with its
    # volume line, which is its book's trailer (as in s0401t).
    's0508a1.att.xml': dict(template='s0508m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close='पठमो भागो निट्ठितो।'),
    's0508a2.att.xml': dict(template='s0508m.mul.xml', rend='chapter', book_close='बदरतित्थमहाविहारवासिना आचरियधम्मपालत्थेरेन कता'),
    # Apadāna: the commentary covers the Therāpadāna only (kn10, whose mūla is split over s0510m1/m2). Four
    # headings combine vaggas ("२१-२३. …"); like an1_11 they take the first one's id [fsnow, 2026-10-19, on
    # s0401t: "an1_9 and an1_11"]. The part title "थेरापदानं" belongs to what follows, so it ends the
    # section before it.
    's0510a.att.xml': dict(template='s0510m1.mul.xml,s0510m2.mul.xml', rend='chapter', opening='गन्थारम्भकथा',
                           book_close='इति विसुद्धजनविलासिनिया अपदान-अट्ठकथाय', stops=['थेरापदानं'],
                           # Vaggas mostly close by ordinal ("चतुत्थवग्गवण्णना समत्ता।"); a vagga here holds
                           # apadānas, never smaller vaggas, so a closing line about a vagga is its own.
                           level_word='वग्ग',
                           # The source writes the 20th vagga's closing without "वग्ग": "वीसतिमवण्णना समत्ता।".
                           own_close={'kn10_20': 'वीसतिमवण्णना समत्ता।'}),
    # Buddhavaṃsa: chapters close twice, by name and by ordinal ("…बुद्धवंसवण्णना निट्ठिता।" then "निट्ठितो पठमो
    # बुद्धवंसो।"); the part title "अब्भन्तरनिदानं" before chapter 1 stays outside the sections, as its own
    # closing after chapter 1 does.
    's0511a.att.xml': dict(template='s0511m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close='निगमनकथा',
                           stops=['अब्भन्तरनिदानं'], level_word='बुद्धवंस'),
    # Cariyāpiṭaka: vaggas close by ordinal; the पकिण्णककथा after vagga 3 is the book's closing matter.
    's0512a.att.xml': dict(template='s0512m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close='पकिण्णककथा',
                           level_word='वग्ग'),
    # Jātaka: one book, kn14, whose mūla is split over s0513m/s0514m; seven aṭṭhakathā files, one per volume
    # (s0513a4 holds volume 4 and the start of 5). Each file is book kn14, as the Theragāthā's two are kn8.
    's0513a1.att.xml': dict(template='s0513m.mul.xml,s0514m.mul.xml', rend='chapter', opening='गन्थारम्भकथा',
                            book_close='(पठमो भागो निट्ठितो)।'),
    's0513a2.att.xml': dict(template='s0513m.mul.xml,s0514m.mul.xml', rend='chapter', book_close='(दुतियो भागो निट्ठितो।)'),
    's0513a3.att.xml': dict(template='s0513m.mul.xml,s0514m.mul.xml', rend='chapter', book_close='(ततियो भागो निट्ठितो।)'),
    's0513a4.att.xml': dict(template='s0513m.mul.xml,s0514m.mul.xml', rend='chapter', book_close=None),
    # s0514a1 continues volume 5 and has no book heading: its book div opens at nipāta 17, with no <head>.
    # [fsnow, 2026-10-22] "0514 div with no head". Its ChapterListTypes leaves out "book", so the dropdown
    # does not list nipāta 17's heading twice.
    's0514a1.att.xml': dict(template='s0513m.mul.xml,s0514m.mul.xml', rend='chapter', book_close='पञ्\u200dचमो भागो निट्ठितो।'),
    's0514a2.att.xml': dict(template='s0513m.mul.xml,s0514m.mul.xml', rend='chapter', book_close='(छट्ठो भागो निट्ठितो।)'),
    's0514a3.att.xml': dict(template='s0513m.mul.xml,s0514m.mul.xml', rend='chapter', book_close='जातक-अट्ठकथा समत्ता।'),
    # Mahāniddesa: the "अट्ठकवग्गो" around all 16 chapters is not marked in the mūla; its title before chapter 1
    # and its closing after chapter 16 stay outside the sections. The book's own last line is its trailer.
    's0515a.att.xml': dict(template='s0515m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close=None,
                           stops=['१. अट्ठकवग्गो']),
    # Paṭisambhidāmagga: the three vaggas are headed "(१) महावग्गो" etc.; the kathās inside them share the rend and
    # are numbered, so sections come from the names alone. As in s0201a, the second part's closing and front
    # matter (mid-Mahāvagga) stay plain paragraphs. [fsnow, 2026-10-22] "one book div as I did before".
    # Vaggas 2 and 3 close on two lines; the trailer is the last, which names the vagga by its place
    # ("मज्झिमवग्गस्स", "चूळवग्गस्स"). [fsnow, 2026-10-22] "I agree with your suggestion".
    's0517a.att.xml': dict(template='s0517m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close='निगमनकथा',
                           numbered=False,
                           names={'(१) महावग्गो': 'kn17_1', '(२) युगनद्धवग्गो': 'kn17_2',
                                  '(३) पञ्\u200dञावग्गो': 'kn17_3'},
                           own_close={'kn17_1': 'निट्ठिता च महावग्गवण्णना।',
                                      'kn17_2': 'निट्ठिता च मज्झिमवग्गस्स अपुब्बत्थानुवण्णना।',
                                      'kn17_3': 'निट्ठिता चूळवग्गस्स अपुब्बत्थानुवण्णना।'}),
    # Netti: chapters 5 and 6 are unnumbered in both texts. Chapter 4 has no closing of its own in either text;
    # its last line closes the hārasampāta inside it, so it gets no trailer.
    's0519a.att.xml': dict(template='s0519m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close='निगमनकथा',
                           names={'नयसमुट्ठानवारवण्णना': 'kn19_5', 'सासनपट्ठानवारवण्णना': 'kn19_6'}),
    # Netti ṭīkā: its opening covers the gantha-ārambha and the nidāna, so by the _0 rule it gets no trailer.
    's0519t.tik.xml': dict(template='s0519a.att.xml', rend='chapter', opening='गन्थारम्भकथावण्णना',
                           book_close='निगमनकथावण्णना',
                           names={'नयसमुट्ठानवारवण्णना': 'kn19_5', 'सासनपट्ठानवारवण्णना': 'kn19_6'}),
    # kn19_4 and kn19_5 end with a note after their closing ("पण्डितेहि पन…"). A trailer must be the last thing
    # in its div (TEI: closing matter follows the body), so neither gets one. [fsnow, 2026-10-22] "ok, then no
    # trailer".
    's0501t.nrf.xml': dict(template='s0519m.mul.xml', rend='chapter', opening='गन्थारम्भकथा', book_close='निगमनकथा',
                           names={'नयसमुट्ठानविभावना': 'kn19_5', 'सासनपट्ठानविभावना': 'kn19_6'},
                           # Closings that name their section in another form: "सङ्गहवारस्स अत्थविभावना";
                           # kn19_6's colophon runs on into a bodytext "निट्ठिता।", which completes it (as s0505a).
                           own_close={'kn19_1': 'सङ्गहवारस्स अत्थविभावना निट्ठिता।',
                                      'kn19_2': 'उद्देसवारस्स अत्थविभावना निट्ठिता।',
                                      'kn19_6': 'निट्ठिता।'}),
    # Cūḷaniddesa: two sections, numbered in neither text.
    's0516a.att.xml': dict(template='s0516m.mul.xml', rend='chapter', book_close='निगमनकथा',
                           names={'पारायनवग्गनिद्देसो': 'kn16_1', 'खग्गविसाणसुत्तनिद्देसो': 'kn16_2'},
                           # The mūla closes kn16_1 "पारायनवग्गो समत्तो।"; this closing names the vagga, not the heading's "-निद्देसो".
                           own_close={'kn16_1': 'पारायनवग्गवण्णना निट्ठिता।'}),
    's0509a.att.xml': dict(template='s0509m.mul.xml', rend='chapter', book_close='निगमनगाथा',
                           # Headings "एकादसक-", "द्वादसकनिपातो"; their closings spell them without the -क-.
                           own_close={'kn9_10': 'एकादसनिपातवण्णना निट्ठिता।', 'kn9_11': 'द्वादसनिपातवण्णना निट्ठिता।'}),
}


def template_sections(templates):
    """The first template book's id, and the sections of that book across the template files."""
    bid, out = None, {}
    for tpl in templates.split(','):
        s = open(os.path.join(MASTER, tpl), 'rb').read().decode('utf-16')
        root = ET.fromstring(re.sub(r'<\?xml[^>]*\?>', '', s, count=1))
        book = next(d for d in root.iter('div') if d.get('type') == 'book')
        bid = bid or book.get('id')
        if book.get('id') != bid: continue
        for d in book.iter('div'):
            if d is book: continue
            h = d.find('head')
            # The first div with an id wins: s0510m1 has a slip, vagga 23 repeating the id kn10_2.
            out.setdefault(d.get('id'), (d.get('type'), ''.join(h.itertext()).strip() if h is not None else ''))
    return bid, out


def build(name):
    cfg = CONFIG[name]
    lines = read(os.path.join(MASTER, name))
    assert not any(l.startswith('<div ') for l in lines), f'{name} already has divs'
    bid, tsecs = template_sections(cfg['template'])

    body_end = lines.index('</body>')
    book_end = last_element(lines, body_end)
    # A file that continues a volume can have no book heading (s0514a1): its book div then opens at its first
    # section, with no <head>.
    book_head = next((i for i, l in enumerate(lines) if l.startswith('<p rend="book">')), None)
    first_heading = next(i for i, l in enumerate(lines) if l.startswith(f'<p rend="{cfg["rend"]}">'))
    book_start = book_head if book_head is not None else first_heading
    # The LAST line with this text: a colophon's first line can recur at every section's end (s0510a). A file
    # that ends inside a volume has no closing matter of its own (s0513a4): book_close is None.
    if cfg.get('book_close'):
        close_start = max(j for j in range(book_start, body_end)
                          if TEXT.match(lines[j]) and plain(TEXT.match(lines[j]).group(3)) == cfg['book_close'])
    else:
        close_start = book_end + 1

    # Section starts: the opening section's heading, then the numbered headings of the configured rend.
    starts = []
    if cfg.get('opening'):
        starts.append((find(lines, cfg['opening'], start=book_start), f'{bid}_0'))
    for i in range(book_start, close_start):
        m = TEXT.match(lines[i])
        if not (m and m.group(1) == 'p' and m.group(2) == cfg['rend']): continue
        num = re.match(r'^\(?([०-९]+)(?:-[०-९]+)?\)?\.', plain(m.group(3)))
        if num and cfg.get('numbered', True):
            starts.append((i, f'{bid}_{int(num.group(1).translate(DEV))}'))
        elif plain(m.group(3)) in cfg.get('names', {}):
            # Unnumbered headings, mapped by name (s0516a: neither text numbers its two sections).
            starts.append((i, cfg['names'][plain(m.group(3))]))
    starts.sort()

    sections, report = [], []
    stops = sorted(j for t in cfg.get('stops', []) for j in range(book_start, close_start)
                   if TEXT.match(lines[j]) and plain(TEXT.match(lines[j]).group(3)) == t)
    for k, (i, sid) in enumerate(starts):
        lim = starts[k + 1][0] if k + 1 < len(starts) else close_start
        lim = min([lim] + [j for j in stops if j > i])
        if sid.endswith('_0'):
            stype = tsecs[f'{bid}_1'][0]
            tname = '(opening section)'
        else:
            assert sid in tsecs, f'{name}: no {sid} in {cfg["template"]}'
            stype, tname = tsecs[sid]
        own = key(plain(lines[i]))
        def names(j):
            mm = TEXT.match(lines[j])
            return bool(mm) and bool(own) and own in squash(plain(lines[j])) and \
                (mm.group(2) == 'centre' or bool(CLOSING.search(plain(lines[j]))) or continues_closing(j)) or level_closing(j)
        def level_closing(j):
            mm = TEXT.match(lines[j])
            return bool(cfg.get('level_word')) and bool(mm) and mm.group(2) == 'centre' and \
                cfg['level_word'] in plain(lines[j]) and bool(CLOSING.search(plain(lines[j])))
        def continues_closing(j):
            # A colophon split over lines: a centre line that does not end its sentence (no danda) runs on into
            # the next, whatever that line's rend. s0505a: "…अत्थवण्णनानयतो, नामेन" / "उरगवग्गोति।"; s0508a1:
            # "…एककनिपातस्स" / "अत्थवण्णना।". The completing line is rend="bodytext" in the source.
            k = j - 1
            while k > 0 and lines[k] == '': k -= 1
            prev = TEXT.match(lines[k])
            return bool(prev) and prev.group(2) == 'centre' and not re.search('[\u0964\u0965]$', plain(prev.group(3)))
        closes = [j for j in range(i + 1, lim) if names(j) or level_closing(j)]
        if sid.endswith('_0'):
            # The opening section runs to section 1, whatever it closes on the way (s0512a's ganthārambha
            # closes before its nidānakathā); its last line is its trailer only if it names the section.
            last = last_element(lines, lim)
            closes = [last] if names(last) else []
        if sid in cfg.get('own_close', {}):
            closes = [find(lines, cfg['own_close'][sid], start=i)]
            assert closes[0] < lim, f'{sid}: its closing line is outside it'
        end = closes[-1] if closes else last_element(lines, lim)
        # A colophon that runs on: the completing line(s) after a closing line without a danda.
        while closes:
            j = end + 1
            while j < lim and lines[j] == '': j += 1
            if j < lim and continues_closing(j) and TEXT.match(lines[j]): end = j
            else: break
        # A closing run: centre lines straight after the section's own closing line belong to it, and the
        # last of the run is the trailer ("छब्बीसतिमो वग्गो।" after "ब्राह्मणवग्गवण्णना निट्ठिता।").
        while closes:
            j = end + 1
            while j < lim and lines[j] == '': j += 1
            tail = plain(lines[j]) if j < lim else ''
            # Only a short tail of the same colophon: a centre line with no closing word of its own (so not a
            # bhāṇavāra's "पठमभाणवारं निट्ठितं") and not a Namo or volume line.
            # It must also end its sentence: "एत्तावता चतुवीसतिया बुद्धानं बुद्धवंसवण्णना" (s0511a) starts a
            # colophon of a larger division, not a tail of this one.
            if j < lim and lines[j].startswith('<p rend="centre">') and not CLOSING.search(tail) \
                    and 'नमो' not in tail and 'भागो' not in tail and re.search('[\u0964\u0965]$', tail):
                end = j
            else: break
        sections.append(Section(sid, stype, i, end, head=True, trailer=bool(closes)))
        score = difflib.SequenceMatcher(None, key(tname), own).ratio() if not sid.endswith('_0') else 1.0
        after = [plain(lines[j])[:60] for j in range(end + 1, lim) if lines[j]]
        report.append((sid, stype, plain(lines[i]), tname, score, plain(lines[end]), bool(closes), after))

    # The book: from its heading to its last element; its last line is its trailer if it closes the book.
    # The book's trailer is its last line if that closes it, unless a section already ends on that line.
    last_is_section_trailer = any(sec.trailer and sec.end == book_end for sec in sections)
    sections.append(Section(bid, 'book', book_start, book_end, head=book_head is not None,
                            trailer=bool(CLOSING.search(plain(lines[book_end]))) and not last_is_section_trailer))
    result = apply(lines, sections)
    ids = check(lines, result)
    return lines, result, report, ids, plain(lines[book_end])


if __name__ == '__main__':
    name = sys.argv[1]
    lines, result, report, ids, book_last = build(name)
    print(f'{"id":8} {"type":8} heading  ←  mūla heading (name match)   → closing')
    for sid, stype, h, t, sc, end, tr, after in report:
        print(f'{sid:8} {stype:8} {h[:30]:30} ← {t[:26]:26} ({sc:.2f}) → {end[:40]} {"[trailer]" if tr else "(no closing line)"}')
        if after: print(f'{"":17} after the closing line, inside the section: {after}')
    print(f'book trailer: {book_last}')
    print(f'\nlines {len(lines)} -> {len(result)}; ids {len(ids)}; strip check OK')
    if '--write' in sys.argv: write(os.path.join(MASTER, name), result); print('written')
