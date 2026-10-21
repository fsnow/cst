# Structural markup inventory: corpus `<div>` coverage

**Status:** research, part of the book-relationships epic (#422). **Generated:** 2026-10-21 by `structural_markup_inventory.py`, from tipitaka-xml `deva master` at `3879295` and `Books.cs` at `afb9a98`.

Related: [book-relationships/markup-conventions.md](book-relationships/markup-conventions.md) describes the markup and records the decisions for adding it; [book-relationships/README.md](book-relationships/README.md) is the epic's working doc.

## Background

[fsnow, 2026-10-09] "The structural markup that is there was done by me in 2007 - 2008, manually, and stopped arbitrarily when I ran out of time to develop CST4." The work resumed in October 2026, starting with the Aṅguttara ṭīkās (VipassanaTech/tipitaka-xml#2729).

[observed] The chapter list (`ChapterListsService`) shows the divs whose type is in the book's `ChapterListTypes`, and `MultiBookCodes` (#266) reads a Multi book's sub-book codes from its book divs. Both have only as much to work with as the markup provides.

## Method

Each of the 217 books in `Books.cs` is read and given one of four states, by `markup_profile.profile` (book-relationships/scripts):

| State | Meaning |
|---|---|
| marked | inner section divs, and every `<trailer>` closes a div |
| partial | inner divs, but some `<trailer>`s close nothing: headings and colophons were converted without all their divs |
| book-only | the only divs are `type="book"` |
| unmarked | no divs |

Books are grouped by `Matn` in `Books.cs`, never by file extension: 21 canonical files are `.nrf`. Canonical books are split by piṭaka, and the Sutta piṭaka by nikāya; `Matn = Other` is añña.

## Summary

**78 of 217 books are marked**, 139 unmarked.

| Group | Layer | marked | partial | book-only | unmarked | total |
|---|---|---:|---:|---:|---:|---:|
| DN | mūla | 3 | 0 | 0 | 0 | 3 |
| DN | aṭṭhakathā | 3 | 0 | 0 | 0 | 3 |
| DN | ṭīkā | 3 | 0 | 0 | 2 | 5 |
| MN | mūla | 3 | 0 | 0 | 0 | 3 |
| MN | aṭṭhakathā | 3 | 0 | 0 | 0 | 3 |
| MN | ṭīkā | 3 | 0 | 0 | 0 | 3 |
| SN | mūla | 5 | 0 | 0 | 0 | 5 |
| SN | aṭṭhakathā | 5 | 0 | 0 | 0 | 5 |
| SN | ṭīkā | 5 | 0 | 0 | 0 | 5 |
| AN | mūla | 11 | 0 | 0 | 0 | 11 |
| AN | aṭṭhakathā | 4 | 0 | 0 | 0 | 4 |
| AN | ṭīkā | 4 | 0 | 0 | 0 | 4 |
| KN | mūla | 21 | 0 | 0 | 0 | 21 |
| KN | aṭṭhakathā | 0 | 0 | 0 | 24 | 24 |
| KN | ṭīkā | 0 | 0 | 0 | 2 | 2 |
| Vinaya | mūla | 5 | 0 | 0 | 0 | 5 |
| Vinaya | aṭṭhakathā | 0 | 0 | 0 | 5 | 5 |
| Vinaya | ṭīkā | 0 | 0 | 0 | 13 | 13 |
| Abhidhamma | mūla | 0 | 0 | 0 | 13 | 13 |
| Abhidhamma | aṭṭhakathā | 0 | 0 | 0 | 3 | 3 |
| Abhidhamma | ṭīkā | 0 | 0 | 0 | 9 | 9 |
| añña | | 0 | 0 | 0 | 68 | 68 |

## Div types present

`book` (78)  `vagga` (36)  `samyutta` (15)  `pannasaka` (15)  `peyyala` (11)  `sutta` (9)  `chapter` (8)  `nipata` (5)  `kanda` (2)  `khandaka` (2)  `intro` (2)  `vimana` (1)  `subbook` (1) (number of books with that type)

## Mismatches with `ChapterListTypes`

**Declared but absent:** the book's `ChapterListTypes` names a type its XML has no div of. Harmless, but the declaration promises something the book does not have.

- `abh01m.mul.xml`: `book,chapter`
- `s0404a.att.xml`: `peyyala`
- `s0403t.tik.xml`: `peyyala`
- `s0404t.tik.xml`: `peyyala`

**Present but not listed:** the XML has sections of a type its `ChapterListTypes` leaves out, so the chapter list skips them.

- none

## Priorities

Not every unmarked book is meant to be marked. The epic's goal 6 (#422, 2026-07-31) is chapter lists "wherever that is correct and relevant", and ranks the añña books "likely never, and not merely 'later'"; the remaining canonical commentaries are "to be judged on how much they are actually cited". The order of work is in [markup-conventions.md](book-relationships/markup-conventions.md#adding-markup).

## Per-book inventory

| Group | Layer | File | State | `<div>` types | `ChapterListTypes` |
|---|---|---|---|---|---|
| DN | mūla | `s0101m.mul.xml` | marked | sutta×13 book×1 | book,sutta |
| DN | mūla | `s0102m.mul.xml` | marked | sutta×10 book×1 | book,sutta |
| DN | mūla | `s0103m.mul.xml` | marked | sutta×11 book×1 | book,sutta |
| DN | aṭṭhakathā | `s0101a.att.xml` | marked | sutta×14 book×1 | book,sutta |
| DN | aṭṭhakathā | `s0102a.att.xml` | marked | sutta×10 book×1 | book,sutta |
| DN | aṭṭhakathā | `s0103a.att.xml` | marked | sutta×11 book×1 | book,sutta |
| DN | ṭīkā | `s0101t.tik.xml` | marked | sutta×14 book×1 | book,sutta |
| DN | ṭīkā | `s0102t.tik.xml` | marked | sutta×10 book×1 | book,sutta |
| DN | ṭīkā | `s0103t.tik.xml` | marked | sutta×11 book×1 | book,sutta |
| DN | ṭīkā | `s0104t.nrf.xml` | unmarked | — | — |
| DN | ṭīkā | `s0105t.nrf.xml` | unmarked | — | — |
| MN | mūla | `s0201m.mul.xml` | marked | vagga×5 book×1 | book,vagga |
| MN | mūla | `s0202m.mul.xml` | marked | vagga×5 book×1 | book,vagga |
| MN | mūla | `s0203m.mul.xml` | marked | vagga×5 book×1 | book,vagga |
| MN | aṭṭhakathā | `s0201a.att.xml` | marked | vagga×6 book×1 | book,vagga |
| MN | aṭṭhakathā | `s0202a.att.xml` | marked | vagga×5 book×1 | book,vagga |
| MN | aṭṭhakathā | `s0203a.att.xml` | marked | vagga×5 book×1 | book,vagga |
| MN | ṭīkā | `s0201t.tik.xml` | marked | vagga×6 book×1 | book,vagga |
| MN | ṭīkā | `s0202t.tik.xml` | marked | vagga×5 book×1 | book,vagga |
| MN | ṭīkā | `s0203t.tik.xml` | marked | vagga×5 book×1 | book,vagga |
| SN | mūla | `s0301m.mul.xml` | marked | samyutta×11 book×1 | book,samyutta |
| SN | mūla | `s0302m.mul.xml` | marked | samyutta×10 book×1 | book,samyutta |
| SN | mūla | `s0303m.mul.xml` | marked | samyutta×13 book×1 | book,samyutta |
| SN | mūla | `s0304m.mul.xml` | marked | samyutta×10 book×1 | book,samyutta |
| SN | mūla | `s0305m.mul.xml` | marked | samyutta×12 book×1 | book,samyutta |
| SN | aṭṭhakathā | `s0301a.att.xml` | marked | samyutta×12 book×1 | book,samyutta |
| SN | aṭṭhakathā | `s0302a.att.xml` | marked | samyutta×10 book×1 | book,samyutta |
| SN | aṭṭhakathā | `s0303a.att.xml` | marked | samyutta×13 book×1 | book,samyutta |
| SN | aṭṭhakathā | `s0304a.att.xml` | marked | samyutta×10 book×1 | book,samyutta |
| SN | aṭṭhakathā | `s0305a.att.xml` | marked | samyutta×12 book×1 | book,samyutta |
| SN | ṭīkā | `s0301t.tik.xml` | marked | samyutta×12 book×1 | book,samyutta |
| SN | ṭīkā | `s0302t.tik.xml` | marked | samyutta×10 book×1 | book,samyutta |
| SN | ṭīkā | `s0303t.tik.xml` | marked | samyutta×13 book×1 | book,samyutta |
| SN | ṭīkā | `s0304t.tik.xml` | marked | samyutta×10 book×1 | book,samyutta |
| SN | ṭīkā | `s0305t.tik.xml` | marked | samyutta×12 book×1 | book,samyutta |
| AN | mūla | `s0401m.mul.xml` | marked | vagga×20 book×1 | book,vagga |
| AN | mūla | `s0402m1.mul.xml` | marked | vagga×15 peyyala×4 pannasaka×3 book×1 | book,pannasaka,vagga,peyyala |
| AN | mūla | `s0402m2.mul.xml` | marked | vagga×18 pannasaka×3 book×1 | book,pannasaka,vagga |
| AN | mūla | `s0402m3.mul.xml` | marked | vagga×28 pannasaka×5 book×1 | book,pannasaka,vagga |
| AN | mūla | `s0403m1.mul.xml` | marked | vagga×26 pannasaka×5 peyyala×3 book×1 | book,pannasaka,vagga,peyyala |
| AN | mūla | `s0403m2.mul.xml` | marked | vagga×12 pannasaka×2 book×1 peyyala×1 | book,pannasaka,vagga,peyyala |
| AN | mūla | `s0403m3.mul.xml` | marked | vagga×10 book×1 pannasaka×1 peyyala×1 | book,pannasaka,vagga,peyyala |
| AN | mūla | `s0404m1.mul.xml` | marked | vagga×10 pannasaka×2 book×1 peyyala×1 | book,pannasaka,vagga,peyyala |
| AN | mūla | `s0404m2.mul.xml` | marked | vagga×9 pannasaka×2 book×1 peyyala×1 | book,pannasaka,vagga,peyyala |
| AN | mūla | `s0404m3.mul.xml` | marked | vagga×22 pannasaka×4 book×1 peyyala×1 | book,pannasaka,vagga,peyyala |
| AN | mūla | `s0404m4.mul.xml` | marked | vagga×3 book×1 peyyala×1 | book,vagga,peyyala |
| AN | aṭṭhakathā | `s0401a.att.xml` | marked | vagga×20 book×1 intro×1 | book,intro,vagga |
| AN | aṭṭhakathā | `s0402a.att.xml` | marked | vagga×60 pannasaka×11 peyyala×4 book×3 | book,pannasaka,vagga,peyyala |
| AN | aṭṭhakathā | `s0403a.att.xml` | marked | vagga×46 pannasaka×8 book×3 peyyala×1 | book,pannasaka,vagga,peyyala |
| AN | aṭṭhakathā | `s0404a.att.xml` | marked | vagga×33 pannasaka×8 book×4 | book,pannasaka,vagga,peyyala |
| AN | ṭīkā | `s0401t.tik.xml` | marked | vagga×19 book×1 intro×1 | book,intro,vagga |
| AN | ṭīkā | `s0402t.tik.xml` | marked | vagga×58 pannasaka×11 book×3 peyyala×3 | book,pannasaka,vagga,peyyala |
| AN | ṭīkā | `s0403t.tik.xml` | marked | vagga×42 pannasaka×8 book×3 | book,pannasaka,vagga,peyyala |
| AN | ṭīkā | `s0404t.tik.xml` | marked | vagga×28 pannasaka×7 book×4 | book,pannasaka,vagga,peyyala |
| KN | mūla | `s0501m.mul.xml` | marked | chapter×9 book×1 | book,chapter |
| KN | mūla | `s0502m.mul.xml` | marked | vagga×26 book×1 | book,vagga |
| KN | mūla | `s0503m.mul.xml` | marked | vagga×8 book×1 | book,vagga |
| KN | mūla | `s0504m.mul.xml` | marked | nipata×4 book×1 | book,nipata |
| KN | mūla | `s0505m.mul.xml` | marked | vagga×5 book×1 | book,vagga |
| KN | mūla | `s0506m.mul.xml` | marked | vimana×2 book×1 | book,vimana |
| KN | mūla | `s0507m.mul.xml` | marked | vagga×4 book×1 | book,vagga |
| KN | mūla | `s0508m.mul.xml` | marked | nipata×21 book×1 | book,nipata |
| KN | mūla | `s0509m.mul.xml` | marked | nipata×16 book×1 | book,nipata |
| KN | mūla | `s0510m1.mul.xml` | marked | vagga×42 book×1 | book,vagga |
| KN | mūla | `s0510m2.mul.xml` | marked | vagga×18 book×2 | book,vagga |
| KN | mūla | `s0511m.mul.xml` | marked | chapter×29 book×1 | book,chapter |
| KN | mūla | `s0512m.mul.xml` | marked | vagga×3 book×1 | book,vagga |
| KN | mūla | `s0513m.mul.xml` | marked | nipata×16 book×1 | book,nipata |
| KN | mūla | `s0514m.mul.xml` | marked | nipata×6 book×1 | book,nipata |
| KN | mūla | `s0515m.mul.xml` | marked | chapter×16 book×1 | book,chapter |
| KN | mūla | `s0516m.mul.xml` | marked | chapter×2 book×1 | book,chapter |
| KN | mūla | `s0517m.mul.xml` | marked | vagga×3 book×1 | book,vagga |
| KN | mūla | `s0519m.mul.xml` | marked | chapter×6 book×1 | book,chapter |
| KN | mūla | `s0518m.nrf.xml` | marked | chapter×5 book×1 | book,chapter |
| KN | mūla | `s0520m.nrf.xml` | marked | chapter×8 book×1 | book,chapter |
| KN | aṭṭhakathā | `s0501a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0502a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0503a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0504a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0505a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0506a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0507a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0508a1.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0508a2.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0509a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0510a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0511a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0512a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0513a1.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0513a2.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0513a3.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0513a4.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0514a1.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0514a2.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0514a3.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0515a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0516a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0517a.att.xml` | unmarked | — | — |
| KN | aṭṭhakathā | `s0519a.att.xml` | unmarked | — | — |
| KN | ṭīkā | `s0519t.tik.xml` | unmarked | — | — |
| KN | ṭīkā | `s0501t.nrf.xml` | unmarked | — | — |
| Vinaya | mūla | `vin01m.mul.xml` | marked | kanda×5 book×1 | book,kanda |
| Vinaya | mūla | `vin02m1.mul.xml` | marked | kanda×11 book×1 subbook×1 | book,subbook,kanda |
| Vinaya | mūla | `vin02m2.mul.xml` | marked | khandaka×10 book×1 | book,khandaka |
| Vinaya | mūla | `vin02m3.mul.xml` | marked | khandaka×12 book×1 | book,khandaka |
| Vinaya | mūla | `vin02m4.mul.xml` | marked | chapter×18 book×1 | book,chapter |
| Vinaya | aṭṭhakathā | `vin01a.att.xml` | unmarked | — | — |
| Vinaya | aṭṭhakathā | `vin02a1.att.xml` | unmarked | — | — |
| Vinaya | aṭṭhakathā | `vin02a2.att.xml` | unmarked | — | — |
| Vinaya | aṭṭhakathā | `vin02a3.att.xml` | unmarked | — | — |
| Vinaya | aṭṭhakathā | `vin02a4.att.xml` | unmarked | — | — |
| Vinaya | ṭīkā | `vin01t1.tik.xml` | unmarked | — | — |
| Vinaya | ṭīkā | `vin01t2.tik.xml` | unmarked | — | — |
| Vinaya | ṭīkā | `vin02t.tik.xml` | unmarked | — | — |
| Vinaya | ṭīkā | `vin04t.nrf.xml` | unmarked | — | — |
| Vinaya | ṭīkā | `vin05t.nrf.xml` | unmarked | — | — |
| Vinaya | ṭīkā | `vin06t.nrf.xml` | unmarked | — | — |
| Vinaya | ṭīkā | `vin07t.nrf.xml` | unmarked | — | — |
| Vinaya | ṭīkā | `vin08t.nrf.xml` | unmarked | — | — |
| Vinaya | ṭīkā | `vin09t.nrf.xml` | unmarked | — | — |
| Vinaya | ṭīkā | `vin10t.nrf.xml` | unmarked | — | — |
| Vinaya | ṭīkā | `vin11t.nrf.xml` | unmarked | — | — |
| Vinaya | ṭīkā | `vin12t.nrf.xml` | unmarked | — | — |
| Vinaya | ṭīkā | `vin13t.nrf.xml` | unmarked | — | — |
| Abhidhamma | mūla | `abh01m.mul.xml` | unmarked | — | book,chapter |
| Abhidhamma | mūla | `abh02m.mul.xml` | unmarked | — | — |
| Abhidhamma | mūla | `abh03m1.mul.xml` | unmarked | — | — |
| Abhidhamma | mūla | `abh03m2.mul.xml` | unmarked | — | — |
| Abhidhamma | mūla | `abh03m3.mul.xml` | unmarked | — | — |
| Abhidhamma | mūla | `abh03m4.mul.xml` | unmarked | — | — |
| Abhidhamma | mūla | `abh03m5.mul.xml` | unmarked | — | — |
| Abhidhamma | mūla | `abh03m6.mul.xml` | unmarked | — | — |
| Abhidhamma | mūla | `abh03m7.mul.xml` | unmarked | — | — |
| Abhidhamma | mūla | `abh03m8.mul.xml` | unmarked | — | — |
| Abhidhamma | mūla | `abh03m9.mul.xml` | unmarked | — | — |
| Abhidhamma | mūla | `abh03m10.mul.xml` | unmarked | — | — |
| Abhidhamma | mūla | `abh03m11.mul.xml` | unmarked | — | — |
| Abhidhamma | aṭṭhakathā | `abh01a.att.xml` | unmarked | — | — |
| Abhidhamma | aṭṭhakathā | `abh02a.att.xml` | unmarked | — | — |
| Abhidhamma | aṭṭhakathā | `abh03a.att.xml` | unmarked | — | — |
| Abhidhamma | ṭīkā | `abh01t.tik.xml` | unmarked | — | — |
| Abhidhamma | ṭīkā | `abh02t.tik.xml` | unmarked | — | — |
| Abhidhamma | ṭīkā | `abh03t.tik.xml` | unmarked | — | — |
| Abhidhamma | ṭīkā | `abh04t.nrf.xml` | unmarked | — | — |
| Abhidhamma | ṭīkā | `abh05t.nrf.xml` | unmarked | — | — |
| Abhidhamma | ṭīkā | `abh06t.nrf.xml` | unmarked | — | — |
| Abhidhamma | ṭīkā | `abh07t.nrf.xml` | unmarked | — | — |
| Abhidhamma | ṭīkā | `abh08t.nrf.xml` | unmarked | — | — |
| Abhidhamma | ṭīkā | `abh09t.nrf.xml` | unmarked | — | — |
| añña |  | `e0101n.mul.xml` | unmarked | — | — |
| añña |  | `e0102n.mul.xml` | unmarked | — | — |
| añña |  | `e0103n.att.xml` | unmarked | — | — |
| añña |  | `e0104n.att.xml` | unmarked | — | — |
| añña |  | `e0105n.nrf.xml` | unmarked | — | — |
| añña |  | `e0901n.nrf.xml` | unmarked | — | — |
| añña |  | `e0902n.nrf.xml` | unmarked | — | — |
| añña |  | `e0903n.nrf.xml` | unmarked | — | — |
| añña |  | `e0904n.nrf.xml` | unmarked | — | — |
| añña |  | `e0905n.nrf.xml` | unmarked | — | — |
| añña |  | `e0906n.nrf.xml` | unmarked | — | — |
| añña |  | `e0907n.nrf.xml` | unmarked | — | — |
| añña |  | `e0201n.nrf.xml` | unmarked | — | — |
| añña |  | `e0301n.nrf.xml` | unmarked | — | — |
| añña |  | `e0401n.nrf.xml` | unmarked | — | — |
| añña |  | `e0501n.nrf.xml` | unmarked | — | — |
| añña |  | `e0601n.nrf.xml` | unmarked | — | — |
| añña |  | `e0602n.nrf.xml` | unmarked | — | — |
| añña |  | `e0603n.nrf.xml` | unmarked | — | — |
| añña |  | `e0604n.nrf.xml` | unmarked | — | — |
| añña |  | `e0605n.nrf.xml` | unmarked | — | — |
| añña |  | `e0606n.nrf.xml` | unmarked | — | — |
| añña |  | `e0607n.nrf.xml` | unmarked | — | — |
| añña |  | `e0608n.nrf.xml` | unmarked | — | — |
| añña |  | `e0701n.nrf.xml` | unmarked | — | — |
| añña |  | `e0702n.nrf.xml` | unmarked | — | — |
| añña |  | `e0703n.nrf.xml` | unmarked | — | — |
| añña |  | `e0801n.nrf.xml` | unmarked | — | — |
| añña |  | `e0802n.nrf.xml` | unmarked | — | — |
| añña |  | `e0803n.nrf.xml` | unmarked | — | — |
| añña |  | `e0804n.nrf.xml` | unmarked | — | — |
| añña |  | `e0805n.nrf.xml` | unmarked | — | — |
| añña |  | `e0806n.nrf.xml` | unmarked | — | — |
| añña |  | `e0807n.nrf.xml` | unmarked | — | — |
| añña |  | `e0808n.nrf.xml` | unmarked | — | — |
| añña |  | `e0809n.nrf.xml` | unmarked | — | — |
| añña |  | `e0810n.nrf.xml` | unmarked | — | — |
| añña |  | `e0811n.nrf.xml` | unmarked | — | — |
| añña |  | `e0812n.nrf.xml` | unmarked | — | — |
| añña |  | `e0813n.nrf.xml` | unmarked | — | — |
| añña |  | `e1001n.nrf.xml` | unmarked | — | — |
| añña |  | `e1002n.nrf.xml` | unmarked | — | — |
| añña |  | `e1003n.nrf.xml` | unmarked | — | — |
| añña |  | `e1004n.nrf.xml` | unmarked | — | — |
| añña |  | `e1005n.nrf.xml` | unmarked | — | — |
| añña |  | `e1006n.nrf.xml` | unmarked | — | — |
| añña |  | `e1007n.nrf.xml` | unmarked | — | — |
| añña |  | `e1008n.nrf.xml` | unmarked | — | — |
| añña |  | `e1009n.nrf.xml` | unmarked | — | — |
| añña |  | `e1010n.nrf.xml` | unmarked | — | — |
| añña |  | `e1101n.nrf.xml` | unmarked | — | — |
| añña |  | `e1102n.nrf.xml` | unmarked | — | — |
| añña |  | `e1103n.nrf.xml` | unmarked | — | — |
| añña |  | `e1201n.nrf.xml` | unmarked | — | — |
| añña |  | `e1202n.nrf.xml` | unmarked | — | — |
| añña |  | `e1203n.nrf.xml` | unmarked | — | — |
| añña |  | `e1204n.nrf.xml` | unmarked | — | — |
| añña |  | `e1205n.nrf.xml` | unmarked | — | — |
| añña |  | `e1206n.nrf.xml` | unmarked | — | — |
| añña |  | `e1207n.nrf.xml` | unmarked | — | — |
| añña |  | `e1208n.nrf.xml` | unmarked | — | — |
| añña |  | `e1209n.nrf.xml` | unmarked | — | — |
| añña |  | `e1210n.nrf.xml` | unmarked | — | — |
| añña |  | `e1211n.nrf.xml` | unmarked | — | — |
| añña |  | `e1212n.nrf.xml` | unmarked | — | — |
| añña |  | `e1213n.nrf.xml` | unmarked | — | — |
| añña |  | `e1214n.nrf.xml` | unmarked | — | — |
| añña |  | `e1215n.nrf.xml` | unmarked | — | — |
