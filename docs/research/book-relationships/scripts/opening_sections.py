"""Study the structural markup of DN/MN/SN (and AN) in deva master.
Prints, per file: ChapterListTypes, div types by depth, chapter-list entries (head-bearing listed divs),
and for every opening section (_0 / intro): what precedes it, its head, and how far it is from the top."""
import re, sys, glob, os
import xml.etree.ElementTree as ET

import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from markup import MASTER
BOOKS = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', '..', 'src', 'CST.Core', 'Books.cs')

clt = {}
cur = None
for line in open(BOOKS, encoding='utf-8'):
    m = re.search(r'book\.FileName = "([^"]+)"', line)
    if m: cur = m.group(1)
    m = re.search(r'book\.ChapterListTypes = "([^"]*)"', line)
    if m and cur: clt[cur] = m.group(1)

def load(path):
    s = open(path, 'rb').read().decode('utf-16')
    s = re.sub(r'<\?xml[^>]*\?>', '', s, count=1)
    return ET.fromstring(s)

def text(e):
    return ''.join(e.itertext()).strip()

def body_of(root):
    for e in root.iter():
        if e.tag == 'body': return e
    return root

def walk(path, pattern):
    for f in sorted(glob.glob(os.path.join(MASTER, pattern))):
        if f.endswith('.toc.xml'): continue
        yield f

def depth(i): return i.count('_')

def report(f):
    name = os.path.basename(f)
    root = load(f)
    body = body_of(root)
    types = clt.get(name, '')
    listed = set(t for t in types.split(',') if t)
    divs = [d for d in body.iter('div')]
    bydepth = {}
    for d in divs:
        bydepth.setdefault(depth(d.get('id','')), {}).setdefault(d.get('type'), 0)
        bydepth[depth(d.get('id',''))][d.get('type')] += 1
    print(f'== {name}  ChapterListTypes="{types}"  divs={len(divs)}')
    print('   types by depth:', {k: v for k, v in sorted(bydepth.items())})
    # chapter list as ChapterListsService builds it
    entries = []
    for d in divs:
        if d.get('type') in listed:
            h = d.find('.//head')
            if h is not None:
                entries.append((d.get('id'), d.get('type'), text(h)[:40]))
    print('   chapter list: %d entries; first: %s' % (len(entries), entries[:3]))
    # opening sections
    for d in divs:
        i = d.get('id', '')
        if i.endswith('_0') or d.get('type') == 'intro':
            parent = None
            for p in divs:
                if d in list(p): parent = p
            sibs = list(parent) if parent is not None else []
            before = sibs[:sibs.index(d)] if d in sibs else []
            own_head = d.find('head')
            first = list(d)[0] if len(d) else None
            paras = len(list(d.iter('p')))
            chars = len(text(d))
            print(f'   OPENING {i} type={d.get("type")} own<head>={own_head is not None}'
                  f' first child=<{first.tag} rend={first.get("rend")}> "{text(first)[:30]}"'
                  f' | before it in parent: {[(b.tag, b.get("rend"), text(b)[:25]) for b in before]}'
                  f' | {paras} <p>, {chars} chars | listed type: {d.get("type") in listed}')
    # what precedes the first section inside each book div
    for d in divs:
        if d.get('type') == 'book':
            kids = list(d)
            firstdiv = next((k for k in kids if k.tag == 'div'), None)
            pre = kids[:kids.index(firstdiv)] if firstdiv is not None else kids
            print(f'   book {d.get("id")}: before first section -> {[(k.tag, k.get("rend"), text(k)[:25]) for k in pre][:6]}'
                  + (f' (+{len(pre)-6} more)' if len(pre) > 6 else ''))

if __name__ == '__main__':
    for pat in sys.argv[1:]:
        for f in walk(MASTER, pat):
            report(f)
