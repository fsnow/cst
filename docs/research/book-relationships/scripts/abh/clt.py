"""ChapterListTypes for the Abhidhamma files at a tipitaka-xml rev, checked the way ChapterListsService
builds the list (type in the set, label from .//head)."""
import sys, collections, locate as L
L.REV = sys.argv[1] if len(sys.argv) > 1 else '656fe59'
M1 = 'book,kanda,section'
CLT = {
    'abh01m.mul.xml': M1, 'abh01a.att.xml': 'book,intro,kanda,section', 'abh01t.tik.xml': 'book,intro,kanda,section',
    'abh04t.nrf.xml': 'book,intro,kanda,section',
    'abh02m.mul.xml': 'book,vibhanga', 'abh02a.att.xml': 'book,vibhanga', 'abh02t.tik.xml': 'book,vibhanga',
    'abh03m1.mul.xml': 'book,matika,naya', 'abh03m2.mul.xml': 'book,matika,uddesa,niddesa,pannatti',
    'abh03m3.mul.xml': 'book,vagga',
    **{f'abh03m{i}.mul.xml': 'book,yamaka,vara' for i in (4, 5, 6)},
    **{f'abh03m{i}.mul.xml': 'book,patthana,tika,gocchaka,chapter' for i in range(7, 12)},
    **{f: 'book,intro,matika,naya,niddesa,pannatti,vagga,yamaka,vara,tika,gocchaka,chapter'
       for f in ('abh03a.att.xml', 'abh03t.tik.xml', 'abh05t.nrf.xml')},
}
ACCEPTED = {('abh03m3.mul.xml', 'abh5_1')}     # [fsnow] accepted: head-less vagga 1 listed under its first kathā
ok = True
for f, types in CLT.items():
    root = L.load(f); ts = set(types.split(','))
    present = collections.Counter(d.get('type') for d in root.iter('div'))
    items, problems, seen = [], [], collections.Counter()
    for d in root.iter('div'):
        if d.get('type') not in ts: continue
        own = d.find('head'); anyh = d.find('.//head')
        if anyh is None: continue
        if own is None and (f, d.get('id')) not in ACCEPTED: problems.append(f'{d.get("id")} borrows "{"".join(anyh.itertext()).strip()[:20]}"')
        book = d.get('id').split('_')[0]
        lab = (book, ''.join(anyh.itertext()).strip()); seen[lab] += 1
        items.append(lab)
    dups = [l for l, c in seen.items() if c > 1]
    unused = sorted(ts - set(present))
    unlisted = sorted(t for t in present if t not in ts)
    if problems or dups or unused: ok = False
    print(f'{f:18} {len(items):3} items  types={types}' + (f'\n     not listed: {unlisted}' if unlisted else '') +
          (f'\n     !! borrowed: {problems}' if problems else '') + (f'\n     !! duplicate labels: {dups[:4]}' if dups else '') +
          (f'\n     !! types not present: {unused}' if unused else ''))
print('ALL OK' if ok else 'PROBLEMS')
