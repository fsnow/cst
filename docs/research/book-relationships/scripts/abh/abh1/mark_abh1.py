"""Mark up the Dhammasaṅgaṇī (abh1): mūla abh01m, aṭṭhakathā abh01a, mūlaṭīkā abh01t, anuṭīkā abh04t.

A DRAFT for the maintainer's review: the decisions marked [suggestion] below are open.

Levels [suggestion]:
  abh1          book
  abh1_0        the mūla's mātikā (unnumbered, before kaṇḍa 1)                         type kanda
  abh1_1..4     the four kaṇḍas                                                       type kanda
  abh1_0_1..2   tika- and duka-mātikā (mūla only: the commentaries comment on the
                mātikā inside kaṇḍa 1, after its heading)                             type section
  abh1_1_1..10  kaṇḍa 1: the mūla's ten titles (kāmāvacarakusala … kiriyā)            type section
  abh1_2_1..2   kaṇḍa 2: uddesa (with its mātikā), rūpavibhatti                       type section
  abh1_3_1..3   kaṇḍa 3: tika-, duka-, suttantikaduka-nikkhepa                       type section
  abh1_4_1..2   kaṇḍa 4: tika-, duka-atthuddhāra                                      type section

The commentaries head their sections with any of chapter, title or subhead, so each file's sections are
given by line below, each checked against the line's text. A section ends where its next sibling starts, or
at its parent's closing. A closing line is a trailer only if it names the section and is its last element
(the KN conventions); TRAILER overrides the name test where a closing names its section another way.

Usage: mark_abh1.py FILE   (dry run; the files were written from build())"""
import re, sys, os
SCRATCH = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
sys.path.insert(0, SCRATCH)
from markup import *
from mark_an import key, squash, plain, TEXT, CLOSING

L2_TYPE = 'section'
# Per file: book heading line; where the book's own closing matter begins (a heading, or None: the book's
# last closing line); and the sections as (id, line, heading text). Line numbers are 1-based, as an editor
# shows them, at tipitaka-xml main 05d5d3c.
SPEC = {
    'abh01m.mul.xml': dict(book=(12, 'धम्मसङ्गणीपाळि'), sections=[
        # [fsnow, 2026-10-22] "if we need to change the mul div IDs that would be ok too": the mātikā is
        # abh1_1_0, inside kaṇḍa 1, as the commentaries comment on it after the kaṇḍa's heading. The kaṇḍa's
        # div opens at the mātikā; its head is the kaṇḍa's own heading (782). Nothing in the mātikā is a
        # <head>, or the chapter list (".//head") would name kaṇḍa 1 after it.
        ('abh1_1', 14, 'मातिका', False),
        ('abh1_1_0', 14, 'मातिका', False),
        ('abh1_1_0_1', 16, '१. तिकमातिका', False),
        ('abh1_1_0_2', 152, '२. दुकमातिका', False),
        ('abh1_1_1', 784, 'कामावचरकुसलं'),
        ('abh1_1_2', 1172, 'रूपावचरकुसलं'),
        ('abh1_1_3', 1474, 'अरूपावचरकुसलं'),
        ('abh1_1_4', 1490, 'तेभूमककुसलं'),
        ('abh1_1_5', 1520, 'लोकुत्तरकुसलं'),
        ('abh1_1_6', 1738, 'द्वादस अकुसलानि'),
        ('abh1_1_7', 1888, 'अब्याकतविपाको'),
        ('abh1_1_8', 2076, 'लोकुत्तरविपाक-पठममग्गविपाका'),
        ('abh1_1_9', 2220, 'अकुसलविपाकअब्याकतं'),
        ('abh1_1_10', 2262, 'अहेतुककिरियाअब्याकतं'),
        ('abh1_2', 2334, '२. रूपकण्डं'),
        ('abh1_2_1', 2336, 'उद्देसो'),
        ('abh1_2_2', 2642, 'रूपविभत्ति'),
        ('abh1_3', 3508, '३. निक्खेपकण्डं'),
        ('abh1_3_1', 3510, 'तिकनिक्खेपं'),
        ('abh1_3_2', 3662, 'दुकनिक्खेपं'),
        ('abh1_3_3', 4204, 'सुत्तन्तिकदुकनिक्खेपं'),
        ('abh1_4', 4370, '४. अट्ठकथाकण्डं'),
        ('abh1_4_1', 4372, 'तिकअत्थुद्धारो'),
        ('abh1_4_2', 4490, 'दुकअत्थुद्धारो'),
    ], trailer={
        # Closings that name their section another way [suggestion]:
        'abh1_1_0_2': False,  # "सुत्तन्तिकदुकमातिका।" closes only the suttantika dukas, a smaller unit
        'abh1_1': True,     # "चित्तुप्पादकण्डं निट्ठितं।": names the kaṇḍa, whose div opens at the mātikā
        'abh1_1_1': None,   # ends on "दुतियभाणवारो।" after "अट्ठ कामावचरमहाकुसलचित्तानि।": not its last element
        'abh1_1_5': True,   # "लोकुत्तरं चित्तं।"
        'abh1_1_6': True,   # "द्वादस अकुसलचित्तानि।"
        'abh1_1_8': True,   # "लोकुत्तरविपाको।"
        'abh1_1_10': True,  # "किरिया अब्याकता।" (the title says अहेतुक-, the section also has the sahetuka kiriyā)
        'abh1_2_1': True,   # "मातिका।": the uddesa is the rūpa-mātikā
        'abh1_3_1': True,   # "तिकं।"
        'abh1_3_2': True,   # "अभिधम्मदुकं।"
        'abh1_4': True,     # "अत्थुद्धारो निट्ठितो।": the kaṇḍa's other name
        'abh1_4_1': True,   # "तिकं।"
    }, end={'abh1_1_0': 780}, extra_head=782),
    'abh01a.att.xml': dict(book=(14, 'धम्मसङ्गणी-अट्ठकथा'), close='निगमनकथा', sections=[
        ('abh1_0', 16, 'गन्थारम्भकथा'),
        ('abh1_1', 1668, '१. चित्तुप्पादकण्डो'),
        ('abh1_1_0', 1670, 'तिकमातिकापदवण्णना', False),
        ('abh1_1_0_1', 1670, 'तिकमातिकापदवण्णना'),
        ('abh1_1_0_2', 1754, 'दुकमातिकापदवण्णना'),
        ('abh1_1_1', 1802, 'कामावचरकुसलपदभाजनीयं'),
        ('abh1_1_2', 2812, 'रूपावचरकुसलवण्णना'),
        ('abh1_1_3', 3166, 'अरूपावचरकुसलवण्णना'),
        ('abh1_1_4', 3274, 'तेभूमककुसलवण्णना'),
        ('abh1_1_5', 3286, 'लोकुत्तरकुसलवण्णना'),
        ('abh1_1_6', 3536, 'अकुसलपदं'),
        ('abh1_1_7', 3670, 'अब्याकतपदं'),
        ('abh1_1_8', 3878, 'लोकुत्तरविपाककथा'),
        ('abh1_1_9', 3898, 'अकुसलविपाककथा'),
        ('abh1_1_10', 3906, 'किरियाब्याकतवण्णना'),
        ('abh1_2', 3942, '२. रूपकण्डो'),
        ('abh1_2_1', 3944, 'उद्देसवण्णना'),
        ('abh1_2_2', 4086, 'रूपविभत्तिएककनिद्देसवण्णना'),
        ('abh1_3', 4378, '३. निक्खेपकण्डो'),
        ('abh1_3_1', 4380, 'तिकनिक्खेपकथा'),
        ('abh1_3_2', 4528, 'दुकनिक्खेपकथा'),
        ('abh1_3_3', 4682, 'सुत्तन्तिकदुकनिक्खेपकथा'),
        ('abh1_4', 4826, '४. अट्ठकथाकण्डो'),
        ('abh1_4_1', 4828, 'तिकअत्थुद्धारवण्णना'),
        ('abh1_4_2', 4918, 'दुकअत्थुद्धारवण्णना'),
    ], trailer={
        'abh1_1_1': True,   # "कामावचरकुसलनिद्देसो समत्तो।"
        'abh1_1_6': True,   # "अकुसला धम्मातिपदस्स वण्णना निट्ठिता।"
        'abh1_1_7': False,  # its last line closes the rūpāvacara/arūpāvacara vipāka inside it
    }, end={
        # The kaṇḍa's colophon ("अट्ठसालिनिया धम्मसङ्गहअट्ठकथाय" / "चित्तुप्पादकण्डकथा निट्ठिता।") and the
        # note "अब्याकतपदं पन नेव ताव निट्ठितन्ति।" belong to the kaṇḍa's closing, not to the kiriyā section.
        'abh1_1_10': 3932,
    }),
    # The ṭīkā's closing verses (1440-1446) are the book's closing matter, after kaṇḍa 4's closing.
    'abh01t.tik.xml': dict(book=(12, 'धम्मसङ्गणी-मूलटीका'), close_line=1440, sections=[
        ('abh1_0', 14, 'वीसतिगाथावण्णना'),
        ('abh1_1', 182, '१. चित्तुप्पादकण्डं'),
        ('abh1_1_0', 184, 'तिकमातिकापदवण्णना', False),
        ('abh1_1_0_1', 184, 'तिकमातिकापदवण्णना'),
        ('abh1_1_0_2', 284, 'दुकमातिकापदवण्णना'),
        ('abh1_1_1', 336, 'कामावचरकुसलपदभाजनीयवण्णना'),
        ('abh1_1_2', 662, 'रूपावचरकुसलं'),
        ('abh1_1_3', 748, 'अरूपावचरकुसलकथावण्णना'),
        ('abh1_1_4', 766, 'तेभूमककुसलवण्णना'),
        ('abh1_1_5', 774, 'लोकुत्तरकुसलवण्णना'),
        ('abh1_1_6', 840, 'अकुसलपदं'),
        ('abh1_1_7', 900, 'अब्याकतपदं'),
        ('abh1_1_8', 968, 'लोकुत्तरविपाककथावण्णना'),
        ('abh1_1_9', 976, 'अकुसलविपाककथावण्णना'),
        ('abh1_1_10', 982, 'किरियाब्याकतं'),
        ('abh1_2', 1000, '२. रूपकण्डं'),
        ('abh1_2_1', 1002, 'उद्देसवण्णना'),
        ('abh1_2_2', 1028, 'रूपविभत्ति'),
        ('abh1_3', 1144, '३. निक्खेपकण्डं'),
        ('abh1_3_1', 1146, 'तिकनिक्खेपकथावण्णना'),
        ('abh1_3_2', 1202, 'दुकनिक्खेपकथावण्णना'),
        ('abh1_3_3', 1266, 'सुत्तन्तिकदुकनिक्खेपकथावण्णना'),
        ('abh1_4', 1372, '४. अट्ठकथाकण्डं'),
        ('abh1_4_1', 1374, 'तिकअत्थुद्धारवण्णना'),
        ('abh1_4_2', 1424, 'दुकअत्थुद्धारवण्णना'),
    ], trailer={
        'abh1_1_1': True,   # "कामावचरकुसलवण्णना निट्ठिता।"
        'abh1_1_7': False,  # its last line closes the rūpāvacara/arūpāvacara vipāka inside it
        'abh1_1_10': False, # nothing closes it; the kaṇḍa's closing follows
        'abh1_1_5': True,   # "लोकुत्तरकुसलवण्णना निट्ठिता।"
    }, end={
        # [fsnow, 2026-10-22, picked "Between sections"]: "कुसलकथावण्णना निट्ठिता।" (838) closes the unmarked
        # kusala group (sections 1-5), so it sits after section 5, inside the kaṇḍa (as s0515a's Aṭṭhakavagga).
        'abh1_1_5': 836,
    }),
    'abh04t.nrf.xml': dict(book=(12, 'धम्मसङ्गणी-अनुटीका'), sections=[
        ('abh1_0', 14, 'वीसतिगाथावण्णना'),
        ('abh1_1', 160, '१. चित्तुप्पादकण्डं'),
        ('abh1_1_0', 162, 'तिकमातिकापदवण्णना', False),
        ('abh1_1_0_1', 162, 'तिकमातिकापदवण्णना'),
        ('abh1_1_0_2', 314, 'दुकमातिकापदवण्णना'),
        ('abh1_1_1', 366, 'कामावचरकुसलपदभाजनीयवण्णना'),
        ('abh1_1_2', 668, 'रूपावचरकुसलं'),
        ('abh1_1_3', 758, 'अरूपावचरकुसलकथावण्णना'),
        ('abh1_1_4', 774, 'तेभूमककुसलवण्णना'),
        ('abh1_1_5', 780, 'लोकुत्तरकुसलवण्णना'),
        ('abh1_1_6', 840, 'अकुसलपदं'),
        ('abh1_1_7', 898, 'अब्याकतपदं'),
        ('abh1_1_8', 964, 'लोकुत्तरविपाककथावण्णना'),
        # abh1_1_9 (akusalavipāka): the anuṭīkā has no comment on it.
        ('abh1_1_10', 972, 'किरियाब्याकतकथावण्णना'),
        ('abh1_2', 988, '२. रूपकण्डं'),
        ('abh1_2_1', 990, 'उद्देसवण्णना'),
        ('abh1_2_2', 1024, 'रूपविभत्ति'),
        ('abh1_3', 1164, '३. निक्खेपकण्डं'),
        ('abh1_3_1', 1166, 'तिकनिक्खेपकथावण्णना'),
        ('abh1_3_2', 1222, 'दुकनिक्खेपकथावण्णना'),
        ('abh1_3_3', 1268, 'सुत्तन्तिकदुकनिक्खेपकथावण्णना'),
        ('abh1_4', 1364, '४. अट्ठकथाकण्डं'),
        ('abh1_4_1', 1366, 'तिकअत्थुद्धारवण्णना'),
        ('abh1_4_2', 1436, 'दुकअत्थुद्धारवण्णना'),
    ], trailer={
        'abh1_1_1': True,   # "कामावचरकुसलवण्णना निट्ठिता।"
        'abh1_1_7': False,
        'abh1_1_0': True,   # "मातिकापदवण्णना निट्ठिता।" closes the whole mātikā commentary
    }),
}


def text(lines, i):
    m = TEXT.match(lines[i])
    return plain(m.group(3)) if m else None


def names(lines, j, head):
    m = TEXT.match(lines[j])
    if not (m and (m.group(2) == 'centre' or CLOSING.search(plain(m.group(3))))): return False
    t = squash(plain(m.group(3)))
    k = key(head)
    return bool(k) and (k in t or re.sub('(कथा)$', '', k) in t)


def runs_on(lines, j):
    m = TEXT.match(lines[j])
    return bool(m) and m.group(2) == 'centre' and not re.search('[।॥]$', plain(m.group(3)))


def closing_start(lines, t):
    k = last_element(lines, t)
    while k > 0 and runs_on(lines, k):
        t = k; k = last_element(lines, k)
    return t


def build(name):
    spec = SPEC[name]
    lines = strip(read(os.path.join(MASTER, name)))
    body_end = lines.index('</body>')
    bline, btext = spec['book']
    bstart = bline - 1
    assert text(lines, bstart) == btext, (name, bline, text(lines, bstart))
    bend = last_element(lines, body_end)
    btrailer = bool(CLOSING.search(text(lines, bend)))
    bclose = closing_start(lines, bend) if btrailer else bend + 1
    if spec.get('close_line'):
        bclose = spec['close_line'] - 1
    if spec.get('close'):
        bclose = next(j for j in range(bstart, bclose) if text(lines, j) == spec['close'])
    secs = []
    heads = {}
    for sid, ln, h, *hd in spec['sections']:
        assert text(lines, ln - 1) == h, (name, sid, ln, text(lines, ln - 1))
        secs.append((sid, ln - 1, h)); heads[sid] = hd[0] if hd else True
    out = [Section('abh1', 'book', bstart, bend, trailer=btrailer)]
    report = []
    inner = {'abh1': bclose}

    def parent(sid): return sid.rsplit('_', 1)[0]
    for k, (sid, start, h) in enumerate(secs):
        p = parent(sid)
        # next sibling, or the next section at a higher level
        nxt = next((s for s2, s, _ in secs[k + 1:] if s2.count('_') <= sid.count('_') and s > start), None)
        lim = min(x for x in [nxt, inner[p]] if x is not None)
        end = spec['end'][sid] - 1 if sid in spec.get('end', {}) else last_element(lines, lim)
        auto = names(lines, end, h)
        tr = spec.get('trailer', {}).get(sid, auto)
        tr = bool(tr)
        if sid.endswith('_0') and sid.count('_') == 1 and name != 'abh01m.mul.xml':
            tr = names(lines, end, h)    # the commentaries' opening: trailer only if its last line names it
        level = sid.count('_')
        stype = 'intro' if sid == 'abh1_0' else 'kanda' if level == 1 else 'matika' if sid == 'abh1_1_0' else L2_TYPE
        out.append(Section(sid, stype, start, end, head=heads[sid], trailer=tr))
        inner[sid] = closing_start(lines, end) if tr else end + 1
        report.append((sid, h, text(lines, end), tr, auto != tr))
    result = apply(lines, out)
    if spec.get('extra_head'):
        i = result.index(lines[spec['extra_head'] - 1])
        result[i] = retag(result[i], 'head')
    ids = check(lines, result)
    return lines, result, report, ids, (text(lines, bend), btrailer)


if __name__ == '__main__':
    for name in sys.argv[1:]:
        lines, result, report, ids, (bt, btr) = build(name)
        print(f'== {name}')
        for sid, h, end, tr, overridden in report:
            print(f'{"  " * sid.count("_")}{sid:11} {h[:30]:30} → {end[:42]:42} {"[trailer]" if tr else "(none)"}{" *" if overridden else ""}')
        print(f'book trailer: {bt if btr else "(none)"}')
        types = {'abh01m.mul.xml': {'book', 'kanda', 'section'}}.get(name, {'book', 'kanda', 'section'})
        print(f'lines {len(lines)} -> {len(result)}; ids {len(ids)}; strip check OK; '
              f'chapter list (book,kanda,section): {len(chapter_list(result, types))} items, '
              f'(book,kanda): {len(chapter_list(result, {"book", "kanda"}))}\n')
