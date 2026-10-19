"""Outline a book: divs (with head/trailer) for a marked file; heading-like and centre paragraphs for an
unmarked one. Usage: outline.py FILE [rends...]"""
import re, sys, os
import xml.etree.ElementTree as ET

import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from markup import MASTER
HEADING = {'book', 'chapter', 'title', 'subhead', 'subsubhead', 'nikaya'}

def load(name):
    s = open(os.path.join(MASTER, name), 'rb').read().decode('utf-16')
    s = re.sub(r'<\?xml[^>]*\?>', '', s, count=1)
    return ET.fromstring(s)

def txt(e):
    t = ''.join(x for x in e.itertext())
    return re.sub(r'\s+', ' ', t).strip()

def outline(name, rends):
    root = load(name)
    body = next(e for e in root.iter() if e.tag == 'body')
    def rec(el, depth):
        for c in el:
            if c.tag == 'div':
                print('  ' * depth + f'[div {c.get("id")} type={c.get("type")}]')
                rec(c, depth + 1)
            elif c.tag in ('head', 'trailer') or (c.tag == 'p' and c.get('rend') in rends):
                n = c.get('n')
                print('  ' * depth + f'<{c.tag} {c.get("rend")}{" n="+n if n else ""}> {txt(c)[:70]}')
    rec(body, 0)

if __name__ == '__main__':
    rends = set(sys.argv[2:]) or HEADING | {'centre'}
    outline(sys.argv[1], rends)
