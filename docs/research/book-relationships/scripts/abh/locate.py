"""Does the Abhidhamma markup (tipitaka-xml ebe7ee7) give every commentary paragraph a unique base paragraph?

For each numbered <p n="..."> we record the ids of its enclosing divs, innermost first. A commentary
paragraph (ancestors A, number n) is located in its base by the innermost id in A that the base also has,
then by n inside that div: found once = resolved, several = ambiguous, none = missing. "Without markup" is
the same with the number alone. Also: does every restart (a number lower than the one before) fall right
after a div opens?"""
import os, re, subprocess, sys, collections
import xml.etree.ElementTree as ET
REPO = os.environ.get('TIPITAKA_XML_REPO') or os.path.expanduser('~/github/VipassanaTech/tipitaka-xml')
REV = sys.argv[1] if len(sys.argv) > 1 else 'ebe7ee7'

def load(name):
    b = subprocess.run(['git', '-C', REPO, 'show', f'{REV}:deva master/{name}'], capture_output=True, check=True).stdout
    s = re.sub(r'<\?xml[^>]*\?>', '', b.decode('utf-16').lstrip('\ufeff'))
    s = s.replace('<?xml-stylesheet type="text/xsl" href="tipitaka-deva.xsl"?>', '')
    return ET.fromstring(s)

def paras(name, book=None):
    """[(ancestor ids innermost first, n, opened_since_last)] for numbered paragraphs (optionally of one book div)."""
    out, stack, opened = [], [], [False]
    def walk(el):
        for c in el:
            if c.tag == 'div':
                stack.append(c.get('id')); opened[0] = True; walk(c); stack.pop()
            elif c.tag == 'p' and c.get('n'):
                m = re.match(r'\d+', c.get('n'))
                if m and (book is None or (stack and stack[0] == book)):
                    out.append((tuple(reversed(stack)), int(m.group()), opened[0])); opened[0] = False
            else:
                walk(c)
    walk(load(name).find('.//body'))
    return out

def restarts(ps):
    r = [(i, ps[i-1][1], ps[i][1], ps[i][2]) for i in range(1, len(ps)) if ps[i][1] < ps[i-1][1]]
    return len(r), sum(1 for x in r if not x[3]), [x for x in r if not x[3]][:5]

def locate(comm, base):
    idx = collections.defaultdict(list)            # (div id, n) -> count ; n -> count
    alln = collections.Counter(n for _, n, _ in base)
    for anc, n, _ in base:
        for a in anc: idx[(a, n)].append(1)
    baseids = {a for anc, _, _ in base for a in anc}
    res = collections.Counter(); nomark = collections.Counter(); bad = []
    for anc, n, _ in comm:
        scope = next((a for a in anc if a in baseids), None)
        k = len(idx.get((scope, n), [])) if scope else 0
        res['resolved' if k == 1 else 'ambiguous' if k > 1 else 'missing'] += 1
        if k != 1 and len(bad) < 4: bad.append((anc[0] if anc else '-', scope, n, k))
        c = alln[n]; nomark['resolved' if c == 1 else 'ambiguous' if c > 1 else 'missing'] += 1
    return res, nomark, bad

# (label, commentary file, its book filter, base files, base book filter)
PAIRS = [
    ('Dhammasaṅgaṇī  a→m', 'abh01a.att.xml', None, ['abh01m.mul.xml'], None),
    ('               t→a', 'abh01t.tik.xml', None, ['abh01a.att.xml'], None),
    ('            anuṭ→t', 'abh04t.nrf.xml', None, ['abh01t.tik.xml'], None),
    ('Vibhaṅga       a→m', 'abh02a.att.xml', None, ['abh02m.mul.xml'], None),
    ('               t→a', 'abh02t.tik.xml', 'abh2', ['abh02a.att.xml'], None),
    ('            anuṭ→a', 'abh02t.tik.xml', 'abh2a', ['abh02a.att.xml'], None),
]
for book, files in [('abh3', ['abh03m1.mul.xml']), ('abh4', ['abh03m2.mul.xml']), ('abh5', ['abh03m3.mul.xml']),
                    ('abh6', ['abh03m4.mul.xml', 'abh03m5.mul.xml', 'abh03m6.mul.xml']),
                    ('abh7', [f'abh03m{i}.mul.xml' for i in range(7, 12)])]:
    PAIRS += [(f'{book}  a→m', 'abh03a.att.xml', book, files, None),
              (f'{book}  t→a', 'abh03t.tik.xml', book, ['abh03a.att.xml'], book),
              (f'{book}  anuṭ→t', 'abh05t.nrf.xml', book, ['abh03t.tik.xml'], book)]

if __name__ == '__main__':
    print('== restarts per file: total / NOT right after a div opens')
    files = sorted({p[1] for p in PAIRS} | {f for p in PAIRS for f in p[3]})
    for f in files:
        t, bad, ex = restarts(paras(f))
        if t: print(f'  {f:18} {t:4} / {bad}' + (f'   e.g. {[(e[1], e[2]) for e in ex]}' if bad else ''))
    print('\n== commentary ¶ located in its base:  with markup  |  number alone')
    for label, c, cb, bases, bb in PAIRS:
        comm = paras(c, cb); base = [p for f in bases for p in paras(f, bb)]
        if not comm: print(f'  {label:20} (no numbered paragraphs)'); continue
        r, nm, bad = locate(comm, base)
        tot = sum(r.values())
        fmt = lambda x: f"{100*x['resolved']//tot:3}% ok {x['ambiguous']:4} amb {x['missing']:4} miss"
        print(f'  {label:20} n={tot:5}  {fmt(r)}  |  {fmt(nm)}' + (f'   e.g. {bad[:2]}' if r['resolved'] < tot else ''))
