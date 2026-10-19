"""Where do closing lines (निट्ठित / समत्त) sit relative to the div they close?
For every div in every marked file: is its last element a closing line (inside), and is a closing line
found among the siblings right after it, before the next div (outside)?"""
import re, os, glob, collections
import xml.etree.ElementTree as ET

import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from markup import MASTER as M
CLOSE = re.compile('निट्ठित|निट्ठिता|निट्ठितो|निट्ठितं|समत्त')
VOL = re.compile('भागो')

def load(p):
    b = open(p, 'rb').read()
    s = b.decode('utf-16') if b[:2] in (b'\xff\xfe', b'\xfe\xff') else b.decode('utf-8-sig')
    s = re.sub(r'<\?xml[^>]*\?>', '', s, count=1)
    return ET.fromstring(s)

def t(e): return re.sub(r'\s+', ' ', ''.join(e.itertext())).strip()

stats = collections.Counter()
outside_examples = collections.defaultdict(list)
vol_lines = []
for f in sorted(glob.glob(os.path.join(M, '*.xml'))):
    if f.endswith('.toc.xml') or os.path.basename(f).startswith('e1216n'): continue
    name = os.path.basename(f)
    root = load(f)
    if not any(True for _ in root.iter('div')): continue
    parent_of = {c: p for p in root.iter() for c in p}
    for d in root.iter('div'):
        kids = [k for k in d if k.tag != 'div' or True]
        last = kids[-1] if kids else None
        inside = last is not None and last.tag != 'div' and bool(CLOSE.search(t(last)))
        # siblings after the div, up to the next div
        p = parent_of.get(d)
        after = []
        if p is not None:
            sibs = list(p)
            for s in sibs[sibs.index(d) + 1:]:
                if s.tag == 'div': break
                after.append(s)
        out = [s for s in after if CLOSE.search(t(s))]
        level = d.get('type')
        key = ('book' if level == 'book' else 'section')
        if inside and not out: stats[(key, 'inside only')] += 1
        elif out and not inside: stats[(key, 'outside only')] += 1; outside_examples[key].append((name, d.get('id'), [t(s)[:45] for s in out]))
        elif out and inside: stats[(key, 'both')] += 1; outside_examples[key + '+both'].append((name, d.get('id'), [t(s)[:45] for s in out]))
        else: stats[(key, 'neither')] += 1
    for e in root.iter():
        if e.tag in ('p', 'trailer', 'head') and VOL.search(t(e)) and CLOSE.search(t(e)):
            # where is it: inside which div (innermost)?
            anc = e; inner = None
            while anc in parent_of:
                anc = parent_of[anc]
                if anc.tag == 'div': inner = anc.get('id'); break
            vol_lines.append((name, e.tag, t(e)[:40], inner))

for k in sorted(stats): print(k, stats[k])
for k, v in outside_examples.items():
    print(f'\n-- {k}: {len(v)}')
    for x in v[:40]: print('  ', x)
print('\n-- volume colophons (…भागो निट्ठितो): file, element, text, innermost div')
for x in vol_lines: print('  ', x)
