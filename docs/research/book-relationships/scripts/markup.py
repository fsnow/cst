"""Add structural <div> markup to a deva master book, changing nothing else.

A section is given by the line of its heading (which becomes <head>) and the line of its last element
(which becomes <trailer> when asked). The tool inserts the <div>/</div> lines the way the 2007-08 markup
lays them out: the opening line directly above the heading, </div> directly below the last element.

Every existing "rend" value is kept exactly as it is: [fsnow, 2026-10-19] "always preserve the "rend"
values that are there". A <p rend="X"> becomes <head rend="X"> or <trailer rend="X">, never another rend.

Every change is checked before the file is written:
  - strip check: drop all <div>/</div> lines and turn <head>/<trailer> back into <p>, in both the original
    and the result; the two must be identical, line for line. So the text, the attributes and every other
    element are untouched.
  - the BOM, UTF-16 LE and CRLF line ends are kept;
  - the result is well-formed, ids are unique, n == id, every id extends its parent's.
"""
import re, sys, os
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field

BOM = '\ufeff'

# The tipitaka-xml clone's "deva master" folder: the files these tools read and, when asked, rewrite.
# Never the app's own xml folder. Override with TIPITAKA_XML_MASTER.
MASTER = os.environ.get('TIPITAKA_XML_MASTER') or os.path.expanduser('~/github/VipassanaTech/tipitaka-xml/deva master')


@dataclass
class Section:
    id: str
    type: str
    start: int            # line index of the heading element
    end: int              # line index of the last element inside the section
    head: bool = True     # convert the start line's <p> to <head>
    trailer: bool = False # convert the end line's <p> to <trailer>


def read(path):
    b = open(path, 'rb').read()
    assert b[:2] == b'\xff\xfe', f'{path}: expected a UTF-16 LE BOM'
    s = b.decode('utf-16-le')
    assert s.startswith(BOM)
    assert '\r\n' in s and '\n' not in s.replace('\r\n', ''), f'{path}: expected CRLF line ends only'
    return s[1:].split('\r\n')


def write(path, lines):
    open(path, 'wb').write((BOM + '\r\n'.join(lines)).encode('utf-16-le'))


P_LINE = re.compile(r'^<p rend="([^"]+)"([^>]*)>(.*)</p>$')


def retag(line, tag):
    m = P_LINE.match(line)
    assert m, f'not a single-line <p>: {line[:80]}'
    return f'<{tag} rend="{m.group(1)}"{m.group(2)}>{m.group(3)}</{tag}>'


def apply(lines, sections):
    new = list(lines)
    for s in sections:
        if s.head: new[s.start] = retag(new[s.start], 'head')
        if s.trailer: new[s.end] = retag(new[s.end], 'trailer')
    opens, closes = {}, {}
    # outer sections open first and close last at the same line
    for s in sorted(sections, key=lambda s: (s.start, -s.end)):
        opens.setdefault(s.start, []).append(f'<div id="{s.id}" n="{s.id}" type="{s.type}">')
    for s in sorted(sections, key=lambda s: (s.end, -s.start), reverse=True):
        closes.setdefault(s.end, []).append('</div>')
    out = []
    for i, l in enumerate(new):
        out.extend(opens.get(i, []))
        out.append(l)
        out.extend(closes.get(i, []))
    return out


DIV_LINE = re.compile(r'^(<div [^>]*>|</div>)$')


def strip(lines):
    res = []
    for l in lines:
        if DIV_LINE.match(l): continue
        l = re.sub(r'^<(head|trailer) ', '<p ', l)
        l = re.sub(r'</(head|trailer)>$', '</p>', l)
        res.append(l)
    return res


def check(original, result):
    a, b = strip(original), strip(result)
    assert a == b, 'strip check failed: ' + next(f'line {i}: {x[:60]!r} != {y[:60]!r}'
                                                for i, (x, y) in enumerate(zip(a, b)) if x != y)
    root = ET.fromstring('\n'.join(result[1:]) if result[0].startswith('<?xml') else '\n'.join(result))
    ids = {}
    def walk(el, parent_id):
        for c in el:
            if c.tag == 'div':
                i = c.get('id')
                assert i not in ids, f'duplicate id {i}'
                assert c.get('n') == i, f'n != id on {i}'
                if parent_id: assert i.startswith(parent_id + '_'), f'{i} does not extend {parent_id}'
                ids[i] = c
                walk(c, i)
            else:
                walk(c, parent_id)
    walk(root, None)
    return ids


def find(lines, text, rend=None, start=0, nth=1):
    """Line index of the nth single-line <p>/<head>/<trailer> whose text is exactly `text`."""
    for i in range(start, len(lines)):
        m = re.match(r'^<(p|head|trailer) rend="([^"]+)"[^>]*>(.*)</\1>$', lines[i])
        if m and m.group(3) == text and (rend is None or m.group(2) == rend):
            nth -= 1
            if nth == 0: return i
    raise KeyError(f'not found: {text!r} rend={rend}')


def last_element(lines, before):
    """The last element line before line `before`, skipping blank lines."""
    i = before - 1
    while i >= 0 and lines[i] == '': i -= 1
    return i


def chapter_list(result, types):
    root = ET.fromstring('\n'.join(result[1:]))
    out = []
    for d in root.iter('div'):
        if d.get('type') in types:
            h = d.find('.//head')
            if h is not None:
                out.append('   ' * d.get('id').count('_') + ''.join(h.itertext()).strip())
    return out
