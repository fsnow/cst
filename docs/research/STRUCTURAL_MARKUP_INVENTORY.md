# Structural markup inventory — corpus `<div>` coverage

**Status:** research (ground truth for the book-mapping / navigation cluster: #24 Go To, #76 source-PDF cardinality, #174 book linking, #314 nav resolver, #187 agent navigation, and the #266 `bookCodes`). **Generated:** 2026-07-20 by `structural_markup_inventory.py`.

## Background

The corpus TEI XML carries structural `<div type="…">` markup (`book` / `vagga` / `samyutta` / …) in SOME books but not others. These divisions were added **by hand in 2007** to power the chapter-list feature; that work **stopped mid-corpus in 2008** (unfinished) and was never resumed. `bookCodes` (`Books.cs` + `MultiBookCodes`, #266) and the chapter list derive DIRECTLY from this markup, so their coverage IS this markup's coverage. Nobody had an inventory — this is it.

## Method

`structural_markup_inventory.py` scans all corpus XML files (UTF-16-LE) for `<div type>` elements and cross-references each book's *declared* `ChapterListTypes` in `src/CST.Core/Books.cs`. "Structured" = has at least one `<div type="book">`. Re-run the script to refresh.

## Coverage summary

**78 of 217 books are structured; 139 are unstructured.** The gaps are systematic:


| Collection | structured | unstructured |
|---|---:|---:|
| Sutta | 73 | 28 |
| Vinaya | 5 | 18 |
| Abhidhamma | 0 | 25 |
| Anya/other | 0 | 40 |
| other | 0 | 28 |

By layer, it is a clean front-to-back trail that stops mid-canon:

- **Sutta Mūla 41/41** and **Sutta Ṭīkā 15/16** — fully done.
- **Sutta Aṭṭhakathā ~15/39** — about half, then stopped.
- **Vinaya** — only its 5 Mūla volumes; none of its commentary.
- **Abhidhamma 0/25, Anya/other 0/40, `.nrf` 0/28** — never reached.

This matches the account: the Sutta canon was marked first, the commentaries partway, and Abhidhamma + the extra texts not at all before the 2008 halt.

## Structural vocabulary present

`book`(78)  `vagga`(32)  `samyutta`(15)  `pannasaka`(12)  `peyyala`(10)  `sutta`(9)  `chapter`(8)  `nipata`(5)  `kanda`(2)  `khandaka`(2)  `intro`(1)  `vimana`(1)  `subbook`(1)  — (count = #books containing that div type)

## Inconsistencies (declared a chapter type its XML lacks)

Only two; everything else is cleanly all-or-nothing:

- **`abh01m.mul.xml`** — declares `book,chapter`, XML has `(none)` → missing `book,chapter`.
- **`s0404a.att.xml`** — declares `book,pannasaka,peyyala,vagga`, XML has `book,pannasaka,vagga` → missing `peyyala`.

## Implication for the book-mapping cluster

`bookCodes`, the chapter list, and the "Go To by chapter" hard cases all rest on this markup, so they are **~36% complete**, with the missing 64% falling entirely on **Abhidhamma, the Anya/other texts, all Vinaya commentary, and half the Sutta commentary**. Any relationship model built on `<div>` structure inherits those holes; "Go To" degrades to paragraph/page-only exactly there. Completing the 2007 markup (or deriving structure another way for the unmarked books) is the prerequisite for uniform chapter-level navigation across the corpus.

## Full per-book inventory

| Collection | Layer | File | Structured | `<div type>` present | Declared `ChapterListTypes` | Note |
|---|---|---|:--:|---|---|---|
| Vinaya | Atthakatha | `vin01a.att.xml` | N | — | — |  |
| Vinaya | Mula | `vin01m.mul.xml` | Y | kanda×5 book×1 | book,kanda |  |
| Vinaya | Tika | `vin01t1.tik.xml` | N | — | — |  |
| Vinaya | Tika | `vin01t2.tik.xml` | N | — | — |  |
| Vinaya | Atthakatha | `vin02a1.att.xml` | N | — | — |  |
| Vinaya | Atthakatha | `vin02a2.att.xml` | N | — | — |  |
| Vinaya | Atthakatha | `vin02a3.att.xml` | N | — | — |  |
| Vinaya | Atthakatha | `vin02a4.att.xml` | N | — | — |  |
| Vinaya | Mula | `vin02m1.mul.xml` | Y | kanda×11 book×1 subbook×1 | book,subbook,kanda |  |
| Vinaya | Mula | `vin02m2.mul.xml` | Y | khandaka×10 book×1 | book,khandaka |  |
| Vinaya | Mula | `vin02m3.mul.xml` | Y | khandaka×12 book×1 | book,khandaka |  |
| Vinaya | Mula | `vin02m4.mul.xml` | Y | chapter×18 book×1 | book,chapter |  |
| Vinaya | Tika | `vin02t.tik.xml` | N | — | — |  |
| Vinaya | nrf | `vin04t.nrf.xml` | N | — | — |  |
| Vinaya | nrf | `vin05t.nrf.xml` | N | — | — |  |
| Vinaya | nrf | `vin06t.nrf.xml` | N | — | — |  |
| Vinaya | nrf | `vin07t.nrf.xml` | N | — | — |  |
| Vinaya | nrf | `vin08t.nrf.xml` | N | — | — |  |
| Vinaya | nrf | `vin09t.nrf.xml` | N | — | — |  |
| Vinaya | nrf | `vin10t.nrf.xml` | N | — | — |  |
| Vinaya | nrf | `vin11t.nrf.xml` | N | — | — |  |
| Vinaya | nrf | `vin12t.nrf.xml` | N | — | — |  |
| Vinaya | nrf | `vin13t.nrf.xml` | N | — | — |  |
| Sutta | Atthakatha | `s0101a.att.xml` | Y | sutta×14 book×1 | book,sutta |  |
| Sutta | Mula | `s0101m.mul.xml` | Y | sutta×13 book×1 | book,sutta |  |
| Sutta | Tika | `s0101t.tik.xml` | Y | sutta×14 book×1 | book,sutta |  |
| Sutta | Atthakatha | `s0102a.att.xml` | Y | sutta×10 book×1 | book,sutta |  |
| Sutta | Mula | `s0102m.mul.xml` | Y | sutta×10 book×1 | book,sutta |  |
| Sutta | Tika | `s0102t.tik.xml` | Y | sutta×10 book×1 | book,sutta |  |
| Sutta | Atthakatha | `s0103a.att.xml` | Y | sutta×11 book×1 | book,sutta |  |
| Sutta | Mula | `s0103m.mul.xml` | Y | sutta×11 book×1 | book,sutta |  |
| Sutta | Tika | `s0103t.tik.xml` | Y | sutta×11 book×1 | book,sutta |  |
| Sutta | nrf | `s0104t.nrf.xml` | N | — | — |  |
| Sutta | nrf | `s0105t.nrf.xml` | N | — | — |  |
| Sutta | Atthakatha | `s0201a.att.xml` | Y | vagga×6 book×1 | book,vagga |  |
| Sutta | Mula | `s0201m.mul.xml` | Y | vagga×5 book×1 | book,vagga |  |
| Sutta | Tika | `s0201t.tik.xml` | Y | vagga×6 book×1 | book,vagga |  |
| Sutta | Atthakatha | `s0202a.att.xml` | Y | vagga×5 book×1 | book,vagga |  |
| Sutta | Mula | `s0202m.mul.xml` | Y | vagga×5 book×1 | book,vagga |  |
| Sutta | Tika | `s0202t.tik.xml` | Y | vagga×5 book×1 | book,vagga |  |
| Sutta | Atthakatha | `s0203a.att.xml` | Y | vagga×5 book×1 | book,vagga |  |
| Sutta | Mula | `s0203m.mul.xml` | Y | vagga×5 book×1 | book,vagga |  |
| Sutta | Tika | `s0203t.tik.xml` | Y | vagga×5 book×1 | book,vagga |  |
| Sutta | Atthakatha | `s0301a.att.xml` | Y | samyutta×12 book×1 | book,samyutta |  |
| Sutta | Mula | `s0301m.mul.xml` | Y | samyutta×11 book×1 | book,samyutta |  |
| Sutta | Tika | `s0301t.tik.xml` | Y | samyutta×12 book×1 | book,samyutta |  |
| Sutta | Atthakatha | `s0302a.att.xml` | Y | samyutta×10 book×1 | book,samyutta |  |
| Sutta | Mula | `s0302m.mul.xml` | Y | samyutta×10 book×1 | book,samyutta |  |
| Sutta | Tika | `s0302t.tik.xml` | Y | samyutta×10 book×1 | book,samyutta |  |
| Sutta | Atthakatha | `s0303a.att.xml` | Y | samyutta×13 book×1 | book,samyutta |  |
| Sutta | Mula | `s0303m.mul.xml` | Y | samyutta×13 book×1 | book,samyutta |  |
| Sutta | Tika | `s0303t.tik.xml` | Y | samyutta×13 book×1 | book,samyutta |  |
| Sutta | Atthakatha | `s0304a.att.xml` | Y | samyutta×10 book×1 | book,samyutta |  |
| Sutta | Mula | `s0304m.mul.xml` | Y | samyutta×10 book×1 | book,samyutta |  |
| Sutta | Tika | `s0304t.tik.xml` | Y | samyutta×10 book×1 | book,samyutta |  |
| Sutta | Atthakatha | `s0305a.att.xml` | Y | samyutta×12 book×1 | book,samyutta |  |
| Sutta | Mula | `s0305m.mul.xml` | Y | samyutta×12 book×1 | book,samyutta |  |
| Sutta | Tika | `s0305t.tik.xml` | Y | samyutta×12 book×1 | book,samyutta |  |
| Sutta | Atthakatha | `s0401a.att.xml` | Y | vagga×20 book×1 intro×1 | book,intro,vagga |  |
| Sutta | Mula | `s0401m.mul.xml` | Y | vagga×20 book×1 | book,vagga |  |
| Sutta | Tika | `s0401t.tik.xml` | Y | book×1 | — |  |
| Sutta | Atthakatha | `s0402a.att.xml` | Y | vagga×60 pannasaka×11 peyyala×4 book×3 | book,pannasaka,vagga |  |
| Sutta | Mula | `s0402m1.mul.xml` | Y | vagga×15 peyyala×4 pannasaka×3 book×1 | book,pannasaka,vagga,peyyala |  |
| Sutta | Mula | `s0402m2.mul.xml` | Y | vagga×18 pannasaka×3 book×1 | book,pannasaka,vagga |  |
| Sutta | Mula | `s0402m3.mul.xml` | Y | vagga×28 pannasaka×5 book×1 | book,pannasaka,vagga |  |
| Sutta | Tika | `s0402t.tik.xml` | Y | book×3 | — |  |
| Sutta | Atthakatha | `s0403a.att.xml` | Y | vagga×46 pannasaka×8 book×3 peyyala×1 | book,pannasaka,vagga,peyyala |  |
| Sutta | Mula | `s0403m1.mul.xml` | Y | vagga×26 pannasaka×5 peyyala×3 book×1 | book,pannasaka,vagga,peyyala |  |
| Sutta | Mula | `s0403m2.mul.xml` | Y | vagga×12 pannasaka×2 book×1 peyyala×1 | book,pannasaka,vagga,peyyala |  |
| Sutta | Mula | `s0403m3.mul.xml` | Y | vagga×10 book×1 pannasaka×1 peyyala×1 | book,pannasaka,vagga,peyyala |  |
| Sutta | Tika | `s0403t.tik.xml` | Y | book×3 | — |  |
| Sutta | Atthakatha | `s0404a.att.xml` | Y | vagga×10 book×4 pannasaka×2 | book,pannasaka,vagga,peyyala | declares peyyala ABSENT in XML |
| Sutta | Mula | `s0404m1.mul.xml` | Y | vagga×10 pannasaka×2 book×1 peyyala×1 | book,pannasaka,vagga,peyyala |  |
| Sutta | Mula | `s0404m2.mul.xml` | Y | vagga×9 pannasaka×2 book×1 peyyala×1 | book,pannasaka,vagga,peyyala |  |
| Sutta | Mula | `s0404m3.mul.xml` | Y | vagga×22 pannasaka×4 book×1 peyyala×1 | book,pannasaka,vagga,peyyala |  |
| Sutta | Mula | `s0404m4.mul.xml` | Y | vagga×3 book×1 peyyala×1 | book,vagga,peyyala |  |
| Sutta | Tika | `s0404t.tik.xml` | Y | book×4 | — |  |
| Sutta | Atthakatha | `s0501a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0501m.mul.xml` | Y | chapter×9 book×1 | book,chapter |  |
| Sutta | nrf | `s0501t.nrf.xml` | N | — | — |  |
| Sutta | Atthakatha | `s0502a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0502m.mul.xml` | Y | vagga×26 book×1 | book,vagga |  |
| Sutta | Atthakatha | `s0503a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0503m.mul.xml` | Y | vagga×8 book×1 | book,vagga |  |
| Sutta | Atthakatha | `s0504a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0504m.mul.xml` | Y | nipata×4 book×1 | book,nipata |  |
| Sutta | Atthakatha | `s0505a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0505m.mul.xml` | Y | vagga×5 book×1 | book,vagga |  |
| Sutta | Atthakatha | `s0506a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0506m.mul.xml` | Y | vimana×2 book×1 | book,vimana |  |
| Sutta | Atthakatha | `s0507a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0507m.mul.xml` | Y | vagga×4 book×1 | book,vagga |  |
| Sutta | Atthakatha | `s0508a1.att.xml` | N | — | — |  |
| Sutta | Atthakatha | `s0508a2.att.xml` | N | — | — |  |
| Sutta | Mula | `s0508m.mul.xml` | Y | nipata×21 book×1 | book,nipata |  |
| Sutta | Atthakatha | `s0509a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0509m.mul.xml` | Y | nipata×16 book×1 | book,nipata |  |
| Sutta | Atthakatha | `s0510a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0510m1.mul.xml` | Y | vagga×42 book×1 | book,vagga |  |
| Sutta | Mula | `s0510m2.mul.xml` | Y | vagga×18 book×2 | book,vagga |  |
| Sutta | Atthakatha | `s0511a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0511m.mul.xml` | Y | chapter×29 book×1 | book,chapter |  |
| Sutta | Atthakatha | `s0512a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0512m.mul.xml` | Y | vagga×3 book×1 | book,vagga |  |
| Sutta | Atthakatha | `s0513a1.att.xml` | N | — | — |  |
| Sutta | Atthakatha | `s0513a2.att.xml` | N | — | — |  |
| Sutta | Atthakatha | `s0513a3.att.xml` | N | — | — |  |
| Sutta | Atthakatha | `s0513a4.att.xml` | N | — | — |  |
| Sutta | Mula | `s0513m.mul.xml` | Y | nipata×16 book×1 | book,nipata |  |
| Sutta | Atthakatha | `s0514a1.att.xml` | N | — | — |  |
| Sutta | Atthakatha | `s0514a2.att.xml` | N | — | — |  |
| Sutta | Atthakatha | `s0514a3.att.xml` | N | — | — |  |
| Sutta | Mula | `s0514m.mul.xml` | Y | nipata×6 book×1 | book,nipata |  |
| Sutta | Atthakatha | `s0515a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0515m.mul.xml` | Y | chapter×16 book×1 | book,chapter |  |
| Sutta | Atthakatha | `s0516a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0516m.mul.xml` | Y | chapter×2 book×1 | book,chapter |  |
| Sutta | Atthakatha | `s0517a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0517m.mul.xml` | Y | vagga×3 book×1 | book,vagga |  |
| Sutta | nrf | `s0518m.nrf.xml` | Y | chapter×5 book×1 | book,chapter |  |
| Sutta | Atthakatha | `s0519a.att.xml` | N | — | — |  |
| Sutta | Mula | `s0519m.mul.xml` | Y | chapter×6 book×1 | book,chapter |  |
| Sutta | Tika | `s0519t.tik.xml` | N | — | — |  |
| Sutta | nrf | `s0520m.nrf.xml` | Y | chapter×8 book×1 | book,chapter |  |
| Abhidhamma | Atthakatha | `abh01a.att.xml` | N | — | — |  |
| Abhidhamma | Mula | `abh01m.mul.xml` | N | — | book,chapter | declares book,chapter ABSENT in XML |
| Abhidhamma | Tika | `abh01t.tik.xml` | N | — | — |  |
| Abhidhamma | Atthakatha | `abh02a.att.xml` | N | — | — |  |
| Abhidhamma | Mula | `abh02m.mul.xml` | N | — | — |  |
| Abhidhamma | Tika | `abh02t.tik.xml` | N | — | — |  |
| Abhidhamma | Atthakatha | `abh03a.att.xml` | N | — | — |  |
| Abhidhamma | Mula | `abh03m1.mul.xml` | N | — | — |  |
| Abhidhamma | Mula | `abh03m10.mul.xml` | N | — | — |  |
| Abhidhamma | Mula | `abh03m11.mul.xml` | N | — | — |  |
| Abhidhamma | Mula | `abh03m2.mul.xml` | N | — | — |  |
| Abhidhamma | Mula | `abh03m3.mul.xml` | N | — | — |  |
| Abhidhamma | Mula | `abh03m4.mul.xml` | N | — | — |  |
| Abhidhamma | Mula | `abh03m5.mul.xml` | N | — | — |  |
| Abhidhamma | Mula | `abh03m6.mul.xml` | N | — | — |  |
| Abhidhamma | Mula | `abh03m7.mul.xml` | N | — | — |  |
| Abhidhamma | Mula | `abh03m8.mul.xml` | N | — | — |  |
| Abhidhamma | Mula | `abh03m9.mul.xml` | N | — | — |  |
| Abhidhamma | Tika | `abh03t.tik.xml` | N | — | — |  |
| Abhidhamma | nrf | `abh04t.nrf.xml` | N | — | — |  |
| Abhidhamma | nrf | `abh05t.nrf.xml` | N | — | — |  |
| Abhidhamma | nrf | `abh06t.nrf.xml` | N | — | — |  |
| Abhidhamma | nrf | `abh07t.nrf.xml` | N | — | — |  |
| Abhidhamma | nrf | `abh08t.nrf.xml` | N | — | — |  |
| Abhidhamma | nrf | `abh09t.nrf.xml` | N | — | — |  |
| Anya/other | Mula | `e0101n.mul.xml` | N | — | — |  |
| Anya/other | Mula | `e0102n.mul.xml` | N | — | — |  |
| Anya/other | Atthakatha | `e0103n.att.xml` | N | — | — |  |
| Anya/other | Atthakatha | `e0104n.att.xml` | N | — | — |  |
| Anya/other | nrf | `e0105n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0201n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0301n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0401n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0501n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0601n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0602n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0603n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0604n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0605n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0606n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0607n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0608n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0701n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0702n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0703n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0801n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0802n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0803n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0804n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0805n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0806n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0807n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0808n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0809n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0810n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0811n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0812n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0813n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0901n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0902n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0903n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0904n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0905n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0906n.nrf.xml` | N | — | — |  |
| Anya/other | nrf | `e0907n.nrf.xml` | N | — | — |  |
| other | nrf | `e1001n.nrf.xml` | N | — | — |  |
| other | nrf | `e1002n.nrf.xml` | N | — | — |  |
| other | nrf | `e1003n.nrf.xml` | N | — | — |  |
| other | nrf | `e1004n.nrf.xml` | N | — | — |  |
| other | nrf | `e1005n.nrf.xml` | N | — | — |  |
| other | nrf | `e1006n.nrf.xml` | N | — | — |  |
| other | nrf | `e1007n.nrf.xml` | N | — | — |  |
| other | nrf | `e1008n.nrf.xml` | N | — | — |  |
| other | nrf | `e1009n.nrf.xml` | N | — | — |  |
| other | nrf | `e1010n.nrf.xml` | N | — | — |  |
| other | nrf | `e1101n.nrf.xml` | N | — | — |  |
| other | nrf | `e1102n.nrf.xml` | N | — | — |  |
| other | nrf | `e1103n.nrf.xml` | N | — | — |  |
| other | nrf | `e1201n.nrf.xml` | N | — | — |  |
| other | nrf | `e1202n.nrf.xml` | N | — | — |  |
| other | nrf | `e1203n.nrf.xml` | N | — | — |  |
| other | nrf | `e1204n.nrf.xml` | N | — | — |  |
| other | nrf | `e1205n.nrf.xml` | N | — | — |  |
| other | nrf | `e1206n.nrf.xml` | N | — | — |  |
| other | nrf | `e1207n.nrf.xml` | N | — | — |  |
| other | nrf | `e1208n.nrf.xml` | N | — | — |  |
| other | nrf | `e1209n.nrf.xml` | N | — | — |  |
| other | nrf | `e1210n.nrf.xml` | N | — | — |  |
| other | nrf | `e1211n.nrf.xml` | N | — | — |  |
| other | nrf | `e1212n.nrf.xml` | N | — | — |  |
| other | nrf | `e1213n.nrf.xml` | N | — | — |  |
| other | nrf | `e1214n.nrf.xml` | N | — | — |  |
| other | nrf | `e1215n.nrf.xml` | N | — | — |  |
