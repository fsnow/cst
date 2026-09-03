# Page numbering by book

Which print editions each corpus book carries page breaks for, and therefore which numbering
systems **Go To** can offer (#844), which the status bar can show (#541), and which page a
passage can be cited by.

> **Generated, not maintained.** `python3 docs/reference/page_numbering_by_book.py` scans
> `<pb ed="…"/>` across the corpus. Re-run it after a corpus update rather than editing below.
> Figures here are from **2026-09-03**, over **217** XML files. (#845)

## The five editions

| code | edition | books carrying it | share |
|---|---|---:|---:|
| `V` | VRI | 153 | 70% |
| `M` | Myanmar | 197 | 90% |
| `P` | PTS | 101 | 46% |
| `T` | Thai | 58 | 26% |
| `O` | Other | 15 | 6% |

## Which combinations occur

| systems present | books |
|---|---:|
| V · M · P | 54 |
| V · M · P · T | 47 |
| M | 44 |
| V · M | 41 |
| O | 15 |
| V · M · T | 11 |
| none at all | 5 |

### What "Other" is

**`O` is one edition's pagination, not a miscellany.** All 15 books using it use
*nothing else*, and no book combines it with another system. They are a contiguous block,
`e1201n.nrf.xml`–`e1215n.nrf.xml` — the **Sihaḷa-Gantha-Saṅgaho** collection: Sinhalese texts paginated to a Sri Lankan
printing with no counterpart among the VRI, Myanmar, PTS or Thai editions.

That is why a reader never meets `O` in ordinary use: it appears only inside that collection,
and never as an alternative to a system they already had.

All of them sit under the same top-level nav node: **Añña**.

### Books with no page markers at all

5 books carry no `<pb>` element of any edition. **Go To can offer only
Paragraph for these, and a passage in them has no page reference to cite.**

| file | nav path |
|---|---|
| `e0605n.nrf.xml` | Añña/Buddha-Vandanā Gantha-Saṅgaho/Jinālaṅkāra |
| `e0606n.nrf.xml` | Añña/Buddha-Vandanā Gantha-Saṅgaho/Kamalāñjali |
| `e0607n.nrf.xml` | Añña/Buddha-Vandanā Gantha-Saṅgaho/Pajjamadhu |
| `e0608n.nrf.xml` | Añña/Buddha-Vandanā Gantha-Saṅgaho/Buddhaguṇagāthāvalī |
| `e0701n.nrf.xml` | Añña/Vaṃsa-Gantha-Saṅgaho/Cūḷaganthavaṃsa |

**Expected, not a gap in the XML.** These are `e*` texts, and the VRI printed set does not
extend to them — so there is no printed pagination for them to carry, in any edition. The same
reason accounts for the thin coverage across Añña generally. **[fsnow]**

## By commentary level

The four levels the tree groups by, and how the editions fall across them. The pattern is not
uniform, which is the point: a system's overall share says little about the books a given
reader actually opens.

| level | books | `V` | `M` | `P` | `T` | `O` |
|---|---:|---:|---:|---:|---:|---:|
| Mūla | 61 | 61 (100%) | 61 (100%) | 50 (81%) | 58 (95%) | · |
| Aṭṭhakathā | 47 | 47 (100%) | 47 (100%) | 46 (97%) | · | · |
| Ṭīkā | 41 | 41 (100%) | 41 (100%) | 5 (12%) | · | · |
| Añña (other) | 68 | 4 (5%) | 48 (70%) | · | · | 15 (22%) |

And what a book of each level opens on by default, walking the same precedence `DefaultType`
does:

| level | VRI | Myanmar | PTS | Thai | Other | Paragraph |
|---|---:|---:|---:|---:|---:|---:|
| Mūla | 61 | · | · | · | · | · |
| Aṭṭhakathā | 47 | · | · | · | · | · |
| Ṭīkā | 41 | · | · | · | · | · |
| Añña (other) | 4 | 44 | · | · | 15 | 5 |

## What this means for the app

### How often a preferred system is unavailable (#844)

Go To remembers the system the reader last navigated with and falls back per book when that
book does not carry it. This is how often each preference falls back:

| a reader who prefers | falls back in | of 217 |
|---|---:|---:|
| VRI (`V`) | 64 books | 29% |
| Myanmar (`M`) | 20 books | 9% |
| PTS (`P`) | 116 books | 53% |
| Thai (`T`) | 159 books | 73% |
| Other (`O`) | 202 books | 93% |

So the fallback is not an edge case for anyone but a Myanmar reader, and it is the majority
case for PTS and Thai. A preference that were overwritten by its own fallback would be
destroyed within a few books — which is why #844 records the preference only when the reader
navigates, never when a fallback is merely displayed.

### What the default precedence lands on (#541)

`PageNumbering.Order` is VRI (`V`) → Myanmar (`M`) → PTS (`P`) → Thai (`T`) → Other (`O`), and
`DefaultType` returns the first of those a book carries. Across the corpus that resolves to:

| default for a book with no preference | books |
|---|---:|
| VRI (`V`) | 153 |
| Myanmar (`M`) | 44 |
| Other (`O`) | 15 |
| Paragraph — no page system at all | 5 |

VRI first means most books open on VRI numbering even though Myanmar is the more widely
carried system; the books that fall through to Myanmar are the ones VRI did not paginate.

Every book in `Books.cs` has an XML file, every XML file is declared, and no `<pb>` carries
an `ed=` value outside the five above.

## Every book

Book order is `Books.cs` order — the order the tree presents them. A `·` means the book carries
no page breaks for that edition; the number is how many it carries.

| # | file | level | `V` | `M` | `P` | `T` | `O` | nav path |
|---:|---|---|---:|---:|---:|---:|---:|---|
| 0 | `s0101m.mul.xml` | Mūla | 227 | 236 | 253 | 310 | · | Su. Pi./Dī. Ni./Sīlakkhandhavaggapāḷi |
| 1 | `s0102m.mul.xml` | Mūla | 263 | 283 | 357 | 396 | · | Su. Pi./Dī. Ni./Mahāvaggapāḷi |
| 2 | `s0103m.mul.xml` | Mūla | 252 | 260 | 293 | 343 | · | Su. Pi./Dī. Ni./Pāthikavaggapāḷi |
| 3 | `s0201m.mul.xml` | Mūla | 423 | 414 | 338 | 609 | · | Su. Pi./Ma. Ni./Mūlapaṇṇāsapāḷi |
| 4 | `s0202m.mul.xml` | Mūla | 445 | 439 | 399 | 684 | · | Su. Pi./Ma. Ni./Majjhimapaṇṇāsapāḷi |
| 5 | `s0203m.mul.xml` | Mūla | 365 | 352 | 355 | 547 | · | Su. Pi./Ma. Ni./Uparipaṇṇāsapāḷi |
| 6 | `s0301m.mul.xml` | Mūla | 278 | 242 | 240 | 352 | · | Su. Pi./Saṃ. Ni./Sagāthāvaggapāḷi |
| 7 | `s0302m.mul.xml` | Mūla | 254 | 230 | 286 | 332 | · | Su. Pi./Saṃ. Ni./Nidānavaggapāḷi |
| 8 | `s0303m.mul.xml` | Mūla | 273 | 235 | 279 | 338 | · | Su. Pi./Saṃ. Ni./Khandhavaggapāḷi |
| 9 | `s0304m.mul.xml` | Mūla | 367 | 332 | 402 | 489 | · | Su. Pi./Saṃ. Ni./Saḷāyatanavaggapāḷi |
| 10 | `s0305m.mul.xml` | Mūla | 488 | 415 | 476 | 583 | · | Su. Pi./Saṃ. Ni./Mahāvaggapāḷi |
| 11 | `s0401m.mul.xml` | Mūla | 62 | 48 | 46 | 60 | · | Su. Pi./A. Ni./Ekakanipātapāḷi |
| 12 | `s0402m1.mul.xml` | Mūla | 60 | 50 | 54 | 65 | · | Su. Pi./A. Ni./Dukanipātapāḷi |
| 13 | `s0402m2.mul.xml` | Mūla | 218 | 207 | 199 | 259 | · | Su. Pi./A. Ni./Tikanipātapāḷi |
| 14 | `s0402m3.mul.xml` | Mūla | 295 | 275 | 257 | 347 | · | Su. Pi./A. Ni./Catukkanipātapāḷi |
| 15 | `s0403m1.mul.xml` | Mūla | 263 | 246 | 278 | 289 | · | Su. Pi./A. Ni./Pañcakanipātapāḷi |
| 16 | `s0403m2.mul.xml` | Mūla | 151 | 147 | 174 | 193 | · | Su. Pi./A. Ni./Chakkanipātapāḷi |
| 17 | `s0403m3.mul.xml` | Mūla | 126 | 119 | 149 | 149 | · | Su. Pi./A. Ni./Sattakanipātapāḷi |
| 18 | `s0404m1.mul.xml` | Mūla | 169 | 162 | 201 | 210 | · | Su. Pi./A. Ni./Aṭṭhakanipātapāḷi |
| 19 | `s0404m2.mul.xml` | Mūla | 101 | 94 | 116 | 125 | · | Su. Pi./A. Ni./Navakanipātapāḷi |
| 20 | `s0404m3.mul.xml` | Mūla | 277 | 257 | 310 | 334 | · | Su. Pi./A. Ni./Dasakanipātapāḷi |
| 21 | `s0404m4.mul.xml` | Mūla | 47 | 44 | 51 | 59 | · | Su. Pi./A. Ni./Ekādasakanipātapāḷi |
| 22 | `s0501m.mul.xml` | Mūla | 12 | 11 | 9 | 14 | · | Su. Pi./Khu. Ni./Khuddakapāṭhapāḷi |
| 23 | `s0502m.mul.xml` | Mūla | 55 | 64 | 60 | 58 | · | Su. Pi./Khu. Ni./Dhammapadapāḷi |
| 24 | `s0503m.mul.xml` | Mūla | 111 | 117 | 94 | 155 | · | Su. Pi./Khu. Ni./Udānapāḷi |
| 25 | `s0504m.mul.xml` | Mūla | 86 | 83 | 124 | 95 | · | Su. Pi./Khu. Ni./Itivuttakapāḷi |
| 26 | `s0505m.mul.xml` | Mūla | 165 | 177 | 223 | 235 | · | Su. Pi./Khu. Ni./Suttanipātapāḷi |
| 27 | `s0506m.mul.xml` | Mūla | 89 | 125 | 127 | 157 | · | Su. Pi./Khu. Ni./Vimānavatthupāḷi |
| 28 | `s0507m.mul.xml` | Mūla | 70 | 92 | 86 | 103 | · | Su. Pi./Khu. Ni./Petavatthupāḷi |
| 29 | `s0508m.mul.xml` | Mūla | 141 | 157 | 115 | 182 | · | Su. Pi./Khu. Ni./Theragāthāpāḷi |
| 30 | `s0509m.mul.xml` | Mūla | 52 | 59 | 52 | 66 | · | Su. Pi./Khu. Ni./Therīgāthāpāḷi |
| 31 | `s0510m1.mul.xml` | Mūla | 415 | 445 | 378 | 597 | · | Su. Pi./Khu. Ni./Apadānapāḷi-1 |
| 32 | `s0510m2.mul.xml` | Mūla | 286 | 297 | 237 | 378 | · | Su. Pi./Khu. Ni./Apadānapāḷi-2 |
| 33 | `s0511m.mul.xml` | Mūla | 83 | 86 | 69 | 137 | · | Su. Pi./Khu. Ni./Buddhavaṃsapāḷi |
| 34 | `s0512m.mul.xml` | Mūla | 37 | 36 | 31 | 45 | · | Su. Pi./Khu. Ni./Cariyāpiṭakapāḷi |
| 35 | `s0513m.mul.xml` | Mūla | 293 | 400 | · | 529 | · | Su. Pi./Khu. Ni./Jātakapāḷi-1 |
| 36 | `s0514m.mul.xml` | Mūla | 282 | 378 | · | 494 | · | Su. Pi./Khu. Ni./Jātakapāḷi-2 |
| 37 | `s0515m.mul.xml` | Mūla | 387 | 410 | 510 | 630 | · | Su. Pi./Khu. Ni./Mahāniddesapāḷi |
| 38 | `s0516m.mul.xml` | Mūla | 276 | 307 | · | 429 | · | Su. Pi./Khu. Ni./Cūḷaniddesapāḷi |
| 39 | `s0517m.mul.xml` | Mūla | 414 | 419 | 442 | 642 | · | Su. Pi./Khu. Ni./Paṭisambhidāmaggapāḷi |
| 40 | `s0519m.mul.xml` | Mūla | 164 | 166 | 193 | · | · | Su. Pi./Khu. Ni./Nettippakaraṇapāḷi |
| 41 | `s0518m.nrf.xml` | Mūla | 394 | 408 | 420 | · | · | Su. Pi./Khu. Ni./Milindapañhapāḷi |
| 42 | `s0520m.nrf.xml` | Mūla | 171 | 175 | 258 | · | · | Su. Pi./Khu. Ni./Peṭakopadesapāḷi |
| 43 | `vin01m.mul.xml` | Mūla | 393 | 381 | 266 | 592 | · | Vi. Pi./Pārājikapāḷi |
| 44 | `vin02m1.mul.xml` | Mūla | 488 | 470 | 348 | 684 | · | Vi. Pi./Pācittiyapāḷi |
| 45 | `vin02m2.mul.xml` | Mūla | 485 | 511 | 360 | 718 | · | Vi. Pi./Mahāvaggapāḷi |
| 46 | `vin02m3.mul.xml` | Mūla | 478 | 508 | 308 | 796 | · | Vi. Pi./Cūḷavaggapāḷi |
| 47 | `vin02m4.mul.xml` | Mūla | 414 | 390 | 226 | 549 | · | Vi. Pi./Parivārapāḷi |
| 48 | `abh01m.mul.xml` | Mūla | 327 | 298 | 264 | 381 | · | Abhi. Pi./Dhammasaṅgaṇīpāḷi |
| 49 | `abh02m.mul.xml` | Mūla | 510 | 453 | 436 | 583 | · | Abhi. Pi./Vibhaṅgapāḷi |
| 50 | `abh03m1.mul.xml` | Mūla | 101 | 100 | 113 | 126 | · | Abhi. Pi./Dhātukathāpāḷi |
| 51 | `abh03m2.mul.xml` | Mūla | 85 | 85 | 74 | 107 | · | Abhi. Pi./Puggalapaññattipāḷi |
| 52 | `abh03m3.mul.xml` | Mūla | 503 | 454 | 628 | 655 | · | Abhi. Pi./Kathāvatthupāḷi |
| 53 | `abh03m4.mul.xml` | Mūla | 385 | 264 | · | 357 | · | Abhi. Pi./Yamakapāḷi-1 |
| 54 | `abh03m5.mul.xml` | Mūla | 411 | 316 | · | 392 | · | Abhi. Pi./Yamakapāḷi-2 |
| 55 | `abh03m6.mul.xml` | Mūla | 486 | 330 | · | 521 | · | Abhi. Pi./Yamakapāḷi-3 |
| 56 | `abh03m7.mul.xml` | Mūla | 528 | 464 | · | 577 | · | Abhi. Pi./Paṭṭhānapāḷi-1 |
| 57 | `abh03m8.mul.xml` | Mūla | 575 | 493 | · | 647 | · | Abhi. Pi./Paṭṭhānapāḷi-2 |
| 58 | `abh03m9.mul.xml` | Mūla | 742 | 605 | · | 705 | · | Abhi. Pi./Paṭṭhānapāḷi-3 |
| 59 | `abh03m10.mul.xml` | Mūla | 787 | 635 | · | 764 | · | Abhi. Pi./Paṭṭhānapāḷi-4 |
| 60 | `abh03m11.mul.xml` | Mūla | 497 | 431 | · | 609 | · | Abhi. Pi./Paṭṭhānapāḷi-5 |
| 61 | `s0101a.att.xml` | Aṭṭhakathā | 306 | 338 | 378 | · | · | Su. Pi./Dī. Ni./Sīlakkhandhavagga-Aṭṭhakathā |
| 62 | `s0102a.att.xml` | Aṭṭhakathā | 364 | 403 | 409 | · | · | Su. Pi./Dī. Ni./Mahāvagga-Aṭṭhakathā |
| 63 | `s0103a.att.xml` | Aṭṭhakathā | 231 | 251 | 249 | · | · | Su. Pi./Dī. Ni./Pāthikavagga-Aṭṭhakathā |
| 64 | `s0201a.att.xml` | Aṭṭhakathā | 724 | 718 | 725 | · | · | Su. Pi./Ma. Ni./Mūlapaṇṇāsa-Aṭṭhakathā |
| 65 | `s0202a.att.xml` | Aṭṭhakathā | 319 | 309 | 454 | · | · | Su. Pi./Ma. Ni./Majjhimapaṇṇāsa-Aṭṭhakathā |
| 66 | `s0203a.att.xml` | Aṭṭhakathā | 267 | 254 | 349 | · | · | Su. Pi./Ma. Ni./Uparipaṇṇāsa-Aṭṭhakathā |
| 67 | `s0301a.att.xml` | Aṭṭhakathā | 310 | 325 | 356 | · | · | Su. Pi./Saṃ. Ni./Sagāthāvagga-Aṭṭhakathā |
| 68 | `s0302a.att.xml` | Aṭṭhakathā | 218 | 227 | 248 | · | · | Su. Pi./Saṃ. Ni./Nidānavagga-Aṭṭhakathā |
| 69 | `s0303a.att.xml` | Aṭṭhakathā | 101 | 96 | 105 | · | · | Su. Pi./Saṃ. Ni./Khandhavagga-Aṭṭhakathā |
| 70 | `s0304a.att.xml` | Aṭṭhakathā | 152 | 152 | 166 | · | · | Su. Pi./Saṃ. Ni./Saḷāyatanavagga-Aṭṭhakathā |
| 71 | `s0305a.att.xml` | Aṭṭhakathā | 189 | 189 | 194 | · | · | Su. Pi./Saṃ. Ni./Mahāvagga-Aṭṭhakathā |
| 72 | `s0401a.att.xml` | Aṭṭhakathā | 404 | 416 | 545 | · | · | Su. Pi./A. Ni./Ekakanipāta-Aṭṭhakathā |
| 73 | `s0402a.att.xml` | Aṭṭhakathā | 396 | 395 | 520 | · | · | Su. Pi./A. Ni./Duka-Tika-Catukkanipāta-Aṭṭhakathā |
| 74 | `s0403a.att.xml` | Aṭṭhakathā | 190 | 189 | 262 | · | · | Su. Pi./A. Ni./Pañcaka-Chakka-Sattakanipāta-Aṭṭhakathā |
| 75 | `s0404a.att.xml` | Aṭṭhakathā | 162 | 162 | 240 | · | · | Su. Pi./A. Ni./Aṭṭhakādinipāta-Aṭṭhakathā |
| 76 | `s0501a.att.xml` | Aṭṭhakathā | 205 | 216 | 243 | · | · | Su. Pi./Khu. Ni./Khuddakapāṭha-Aṭṭhakathā |
| 77 | `s0502a.att.xml` | Aṭṭhakathā | 819 | 903 | 1454 | · | · | Su. Pi./Khu. Ni./Dhammapada-Aṭṭhakathā |
| 78 | `s0503a.att.xml` | Aṭṭhakathā | 354 | 393 | 436 | · | · | Su. Pi./Khu. Ni./Udāna-Aṭṭhakathā |
| 79 | `s0504a.att.xml` | Aṭṭhakathā | 324 | 355 | 374 | · | · | Su. Pi./Khu. Ni./Itivuttaka-Aṭṭhakathā |
| 80 | `s0505a.att.xml` | Aṭṭhakathā | 573 | 638 | 607 | · | · | Su. Pi./Khu. Ni./Suttanipāta-Aṭṭhakathā |
| 81 | `s0506a.att.xml` | Aṭṭhakathā | 302 | 335 | 355 | · | · | Su. Pi./Khu. Ni./Vimānavatthu-Aṭṭhakathā |
| 82 | `s0507a.att.xml` | Aṭṭhakathā | 252 | 270 | 287 | · | · | Su. Pi./Khu. Ni./Petavatthu-Aṭṭhakathā |
| 83 | `s0508a1.att.xml` | Aṭṭhakathā | 412 | 485 | 358 | · | · | Su. Pi./Khu. Ni./Theragāthā-Aṭṭhakathā-1 |
| 84 | `s0508a2.att.xml` | Aṭṭhakathā | 459 | 545 | 380 | · | · | Su. Pi./Khu. Ni./Theragāthā-Aṭṭhakathā-2 |
| 85 | `s0509a.att.xml` | Aṭṭhakathā | 324 | 305 | 301 | · | · | Su. Pi./Khu. Ni./Therīgāthā-Aṭṭhakathā |
| 86 | `s0510a.att.xml` | Aṭṭhakathā | 616 | 655 | 572 | · | · | Su. Pi./Khu. Ni./Apadāna-Aṭṭhakathā |
| 87 | `s0511a.att.xml` | Aṭṭhakathā | 346 | 354 | 300 | · | · | Su. Pi./Khu. Ni./Buddhavaṃsa-Aṭṭhakathā |
| 88 | `s0512a.att.xml` | Aṭṭhakathā | 305 | 328 | 335 | · | · | Su. Pi./Khu. Ni./Cariyāpiṭaka-Aṭṭhakathā |
| 89 | `s0513a1.att.xml` | Aṭṭhakathā | 487 | 538 | 511 | · | · | Su. Pi./Khu. Ni./Jātaka-Aṭṭhakathā-1 |
| 90 | `s0513a2.att.xml` | Aṭṭhakathā | 374 | 408 | 450 | · | · | Su. Pi./Khu. Ni./Jātaka-Aṭṭhakathā-2 |
| 91 | `s0513a3.att.xml` | Aṭṭhakathā | 478 | 517 | 543 | · | · | Su. Pi./Khu. Ni./Jātaka-Aṭṭhakathā-3 |
| 92 | `s0513a4.att.xml` | Aṭṭhakathā | 555 | 617 | 607 | · | · | Su. Pi./Khu. Ni./Jātaka-Aṭṭhakathā-4 |
| 93 | `s0514a1.att.xml` | Aṭṭhakathā | 400 | 440 | 403 | · | · | Su. Pi./Khu. Ni./Jātaka-Aṭṭhakathā-5 |
| 94 | `s0514a2.att.xml` | Aṭṭhakathā | 311 | 330 | 278 | · | · | Su. Pi./Khu. Ni./Jātaka-Aṭṭhakathā-6 |
| 95 | `s0514a3.att.xml` | Aṭṭhakathā | 381 | 386 | 288 | · | · | Su. Pi./Khu. Ni./Jātaka-Aṭṭhakathā-7 |
| 96 | `s0515a.att.xml` | Aṭṭhakathā | 386 | 419 | 470 | · | · | Su. Pi./Khu. Ni./Mahāniddesa-Aṭṭhakathā |
| 97 | `s0516a.att.xml` | Aṭṭhakathā | 131 | 140 | 152 | · | · | Su. Pi./Khu. Ni./Cūḷaniddesa-Aṭṭhakathā |
| 98 | `s0517a.att.xml` | Aṭṭhakathā | 610 | 668 | 704 | · | · | Su. Pi./Khu. Ni./Paṭisambhidāmagga-Aṭṭhakathā |
| 99 | `s0519a.att.xml` | Aṭṭhakathā | 259 | 276 | · | · | · | Su. Pi./Khu. Ni./Nettippakaraṇa-Aṭṭhakathā |
| 100 | `vin01a.att.xml` | Aṭṭhakathā | 598 | 655 | 734 | · | · | Vi. Pi./Pārājikakaṇḍa-Aṭṭhakathā |
| 101 | `vin02a1.att.xml` | Aṭṭhakathā | 223 | 231 | 215 | · | · | Vi. Pi./Pācittiya-Aṭṭhakathā |
| 102 | `vin02a2.att.xml` | Aṭṭhakathā | 186 | 205 | 204 | · | · | Vi. Pi./Mahāvagga-Aṭṭhakathā |
| 103 | `vin02a3.att.xml` | Aṭṭhakathā | 136 | 136 | 146 | · | · | Vi. Pi./Cūḷavagga-Aṭṭhakathā |
| 104 | `vin02a4.att.xml` | Aṭṭhakathā | 132 | 129 | 116 | · | · | Vi. Pi./Parivāra-Aṭṭhakathā |
| 105 | `abh01a.att.xml` | Aṭṭhakathā | 444 | 454 | 430 | · | · | Abhi. Pi./Dhammasaṅgaṇi-Aṭṭhakathā |
| 106 | `abh02a.att.xml` | Aṭṭhakathā | 497 | 508 | 524 | · | · | Abhi. Pi./Sammohavinodanī-Aṭṭhakathā |
| 107 | `abh03a.att.xml` | Aṭṭhakathā | 491 | 498 | 367 | · | · | Abhi. Pi./Pañcapakaraṇa-Aṭṭhakathā |
| 108 | `s0101t.tik.xml` | Ṭīkā | 360 | 405 | 526 | · | · | Su. Pi./Dī. Ni./Sīlakkhandhavagga-Ṭīkā |
| 109 | `s0102t.tik.xml` | Ṭīkā | 330 | 358 | 452 | · | · | Su. Pi./Dī. Ni./Mahāvagga-Ṭīkā |
| 110 | `s0103t.tik.xml` | Ṭīkā | 263 | 292 | 372 | · | · | Su. Pi./Dī. Ni./Pāthikavagga-Ṭīkā |
| 111 | `s0104t.nrf.xml` | Ṭīkā | 439 | 500 | · | · | · | Su. Pi./Dī. Ni./Sīlakkhandhavagga-Abhinavaṭīkā-1 |
| 112 | `s0105t.nrf.xml` | Ṭīkā | 386 | 437 | · | · | · | Su. Pi./Dī. Ni./Sīlakkhandhavagga-Abhinavaṭīkā-2 |
| 113 | `s0201t.tik.xml` | Ṭīkā | 652 | 718 | · | · | · | Su. Pi./Ma. Ni./Mūlapaṇṇāsa-Ṭīkā |
| 114 | `s0202t.tik.xml` | Ṭīkā | 210 | 209 | · | · | · | Su. Pi./Ma. Ni./Majjhimapaṇṇāsa-Ṭīkā |
| 115 | `s0203t.tik.xml` | Ṭīkā | 223 | 232 | · | · | · | Su. Pi./Ma. Ni./Uparipaṇṇāsa-Ṭīkā |
| 116 | `s0301t.tik.xml` | Ṭīkā | 302 | 345 | · | · | · | Su. Pi./Saṃ. Ni./Sagāthāvagga-Ṭīkā |
| 117 | `s0302t.tik.xml` | Ṭīkā | 178 | 200 | · | · | · | Su. Pi./Saṃ. Ni./Nidānavagga-Ṭīkā |
| 118 | `s0303t.tik.xml` | Ṭīkā | 77 | 79 | · | · | · | Su. Pi./Saṃ. Ni./Khandhavagga-Ṭīkā |
| 119 | `s0304t.tik.xml` | Ṭīkā | 102 | 111 | · | · | · | Su. Pi./Saṃ. Ni./Saḷāyatanavagga-Ṭīkā |
| 120 | `s0305t.tik.xml` | Ṭīkā | 151 | 159 | · | · | · | Su. Pi./Saṃ. Ni./Mahāvagga-Ṭīkā |
| 121 | `s0401t.tik.xml` | Ṭīkā | 270 | 288 | · | · | · | Su. Pi./A. Ni./Ekakanipāta-Ṭīkā |
| 122 | `s0402t.tik.xml` | Ṭīkā | 360 | 396 | · | · | · | Su. Pi./A. Ni./Duka-Tika-Catukkanipāta-Ṭīkā |
| 123 | `s0403t.tik.xml` | Ṭīkā | 187 | 201 | · | · | · | Su. Pi./A. Ni./Pañcaka-Chakka-Sattakanipāta-Ṭīkā |
| 124 | `s0404t.tik.xml` | Ṭīkā | 156 | 168 | · | · | · | Su. Pi./A. Ni./Aṭṭhakādinipāta-Ṭīkā |
| 125 | `s0519t.tik.xml` | Ṭīkā | 143 | 151 | · | · | · | Su. Pi./Khu. Ni./Nettippakaraṇa-Ṭīkā |
| 126 | `s0501t.nrf.xml` | Ṭīkā | 328 | 356 | · | · | · | Su. Pi./Khu. Ni./Nettivibhāvinī |
| 127 | `vin01t1.tik.xml` | Ṭīkā | 402 | 460 | · | · | · | Vi. Pi./Sāratthadīpanī-Ṭīkā-1 |
| 128 | `vin01t2.tik.xml` | Ṭīkā | 397 | 448 | · | · | · | Vi. Pi./Sāratthadīpanī-Ṭīkā-2 |
| 129 | `vin02t.tik.xml` | Ṭīkā | 455 | 494 | · | · | · | Vi. Pi./Sāratthadīpanī-Ṭīkā-3 |
| 130 | `vin04t.nrf.xml` | Ṭīkā | 345 | 356 | 208 | · | · | Vi. Pi./Dvemātikāpāḷi |
| 131 | `vin05t.nrf.xml` | Ṭīkā | 436 | 468 | · | · | · | Vi. Pi./Vinayasaṅgaha-Aṭṭhakathā |
| 132 | `vin06t.nrf.xml` | Ṭīkā | 533 | 583 | · | · | · | Vi. Pi./Vajirabuddhi-Ṭīkā |
| 133 | `vin07t.nrf.xml` | Ṭīkā | 616 | 681 | · | · | · | Vi. Pi./Vimativinodanī-Ṭīkā |
| 134 | `vin08t.nrf.xml` | Ṭīkā | 638 | 858 | · | · | · | Vi. Pi./Vinayālaṅkāra-Ṭīkā |
| 135 | `vin09t.nrf.xml` | Ṭīkā | 457 | 487 | · | · | · | Vi. Pi./Kaṅkhāvitaraṇīpurāṇa-Ṭīkā |
| 136 | `vin10t.nrf.xml` | Ṭīkā | 326 | 395 | · | · | · | Vi. Pi./Vinayavinicchaya-Uttaravinicchaya |
| 137 | `vin11t.nrf.xml` | Ṭīkā | 856 | 1099 | · | · | · | Vi. Pi./Vinayavinicchaya-Ṭīkā |
| 138 | `vin12t.nrf.xml` | Ṭīkā | 546 | 653 | · | · | · | Vi. Pi./Pācityādiyojanāpāḷi |
| 139 | `vin13t.nrf.xml` | Ṭīkā | 417 | 495 | · | · | · | Vi. Pi./Khuddasikkhā-Mūlasikkhā |
| 140 | `abh01t.tik.xml` | Ṭīkā | 195 | 203 | · | · | · | Abhi. Pi./Dhammasaṅgaṇī-Mūlaṭīkā |
| 141 | `abh02t.tik.xml` | Ṭīkā | 465 | 464 | · | · | · | Abhi. Pi./Vibhaṅga-Mūlaṭīkā |
| 142 | `abh03t.tik.xml` | Ṭīkā | 256 | 247 | · | · | · | Abhi. Pi./Pañcapakaraṇa-Mūlaṭīkā |
| 143 | `abh04t.nrf.xml` | Ṭīkā | 206 | 220 | · | · | · | Abhi. Pi./Dhammasaṅgaṇī-Anuṭīkā |
| 144 | `abh05t.nrf.xml` | Ṭīkā | 322 | 320 | · | · | · | Abhi. Pi./Pañcapakaraṇa-Anuṭīkā |
| 145 | `abh06t.nrf.xml` | Ṭīkā | 497 | 480 | 138 | · | · | Abhi. Pi./Abhidhammāvatāro-Nāmarūpaparicchedo |
| 146 | `abh07t.nrf.xml` | Ṭīkā | 241 | 279 | · | · | · | Abhi. Pi./Abhidhammatthasaṅgaho |
| 147 | `abh08t.nrf.xml` | Ṭīkā | 562 | 738 | · | · | · | Abhi. Pi./Abhidhammāvatāra-Purāṇaṭīkā |
| 148 | `abh09t.nrf.xml` | Ṭīkā | 472 | 583 | · | · | · | Abhi. Pi./Abhidhammamātikāpāḷi |
| 149 | `e0101n.mul.xml` | Añña (other) | 364 | 370 | · | · | · | Añña/Visuddhimagga/Visuddhimagga-1 |
| 150 | `e0102n.mul.xml` | Añña (other) | 354 | 356 | · | · | · | Añña/Visuddhimagga/Visuddhimagga-2 |
| 151 | `e0103n.att.xml` | Añña (other) | 429 | 461 | · | · | · | Añña/Visuddhimagga/Visuddhimagga-Mahāṭīkā-1 |
| 152 | `e0104n.att.xml` | Añña (other) | 503 | 535 | · | · | · | Añña/Visuddhimagga/Visuddhimagga-Mahāṭīkā-2 |
| 153 | `e0105n.nrf.xml` | Añña (other) | · | 73 | · | · | · | Añña/Visuddhimagga/Visuddhimagga-Nidānakathā |
| 154 | `e0901n.nrf.xml` | Añña (other) | · | 148 | · | · | · | Añña/Saṃgāyana-Pucchā Vissajjanā/Dīghanikāya (Pu-Vi) |
| 155 | `e0902n.nrf.xml` | Añña (other) | · | 278 | · | · | · | Añña/Saṃgāyana-Pucchā Vissajjanā/Majjhimanikāya (Pu-Vi) |
| 156 | `e0903n.nrf.xml` | Añña (other) | · | 363 | · | · | · | Añña/Saṃgāyana-Pucchā Vissajjanā/Saṃyuttanikāya (Pu-Vi) |
| 157 | `e0904n.nrf.xml` | Añña (other) | · | 317 | · | · | · | Añña/Saṃgāyana-Pucchā Vissajjanā/Aṅguttaranikāya (Pu-Vi) |
| 158 | `e0905n.nrf.xml` | Añña (other) | · | 455 | · | · | · | Añña/Saṃgāyana-Pucchā Vissajjanā/Vinayapiṭaka (Pu-Vi) |
| 159 | `e0906n.nrf.xml` | Añña (other) | · | 126 | · | · | · | Añña/Saṃgāyana-Pucchā Vissajjanā/Abhidhammapiṭaka (Pu-Vi) |
| 160 | `e0907n.nrf.xml` | Añña (other) | · | 262 | · | · | · | Añña/Saṃgāyana-Pucchā Vissajjanā/Aṭṭhakathā (Pu-Vi) |
| 161 | `e0201n.nrf.xml` | Añña (other) | · | 560 | · | · | · | Añña/Leḍī Sayāḍo Gantha-Saṅgaho/Niruttidīpanī |
| 162 | `e0301n.nrf.xml` | Añña (other) | · | 442 | · | · | · | Añña/Leḍī Sayāḍo Gantha-Saṅgaho/Paramatthadīpanī Saṅgahamahāṭīkāpāṭha |
| 163 | `e0401n.nrf.xml` | Añña (other) | · | 324 | · | · | · | Añña/Leḍī Sayāḍo Gantha-Saṅgaho/Anudīpanīpāṭha |
| 164 | `e0501n.nrf.xml` | Añña (other) | · | 42 | · | · | · | Añña/Leḍī Sayāḍo Gantha-Saṅgaho/Paṭṭhānuddesadīpanīpāṭha |
| 165 | `e0601n.nrf.xml` | Añña (other) | · | 226 | · | · | · | Añña/Buddha-Vandanā Gantha-Saṅgaho/Namakkāraṭīkā |
| 166 | `e0602n.nrf.xml` | Añña (other) | · | 52 | · | · | · | Añña/Buddha-Vandanā Gantha-Saṅgaho/Mahāpaṇāmapāṭha |
| 167 | `e0603n.nrf.xml` | Añña (other) | · | 45 | · | · | · | Añña/Buddha-Vandanā Gantha-Saṅgaho/Lakkhaṇāto Buddhathomanāgāthā |
| 168 | `e0604n.nrf.xml` | Añña (other) | · | 74 | · | · | · | Añña/Buddha-Vandanā Gantha-Saṅgaho/Sutavandanā |
| 169 | `e0605n.nrf.xml` | Añña (other) | · | · | · | · | · | Añña/Buddha-Vandanā Gantha-Saṅgaho/Jinālaṅkāra |
| 170 | `e0606n.nrf.xml` | Añña (other) | · | · | · | · | · | Añña/Buddha-Vandanā Gantha-Saṅgaho/Kamalāñjali |
| 171 | `e0607n.nrf.xml` | Añña (other) | · | · | · | · | · | Añña/Buddha-Vandanā Gantha-Saṅgaho/Pajjamadhu |
| 172 | `e0608n.nrf.xml` | Añña (other) | · | · | · | · | · | Añña/Buddha-Vandanā Gantha-Saṅgaho/Buddhaguṇagāthāvalī |
| 173 | `e0701n.nrf.xml` | Añña (other) | · | · | · | · | · | Añña/Vaṃsa-Gantha-Saṅgaho/Cūḷaganthavaṃsa |
| 174 | `e0702n.nrf.xml` | Añña (other) | · | 182 | · | · | · | Añña/Vaṃsa-Gantha-Saṅgaho/Sāsanavaṃsa |
| 175 | `e0703n.nrf.xml` | Añña (other) | · | 395 | · | · | · | Añña/Vaṃsa-Gantha-Saṅgaho/Mahāvaṃsa |
| 176 | `e0801n.nrf.xml` | Añña (other) | · | 293 | · | · | · | Añña/Byākaraṇa Gantha-Saṅgaho/Moggallānabyākaraṇaṃ |
| 177 | `e0802n.nrf.xml` | Añña (other) | · | 314 | · | · | · | Añña/Byākaraṇa Gantha-Saṅgaho/Kaccāyanabyākaraṇaṃ |
| 178 | `e0803n.nrf.xml` | Añña (other) | · | 417 | · | · | · | Añña/Byākaraṇa Gantha-Saṅgaho/Saddanītippakaraṇaṃ (Padamālā) |
| 179 | `e0804n.nrf.xml` | Añña (other) | · | 388 | · | · | · | Añña/Byākaraṇa Gantha-Saṅgaho/Saddanītippakaraṇaṃ (Dhātumālā) |
| 180 | `e0805n.nrf.xml` | Añña (other) | · | 413 | · | · | · | Añña/Byākaraṇa Gantha-Saṅgaho/Padarūpasiddhi |
| 181 | `e0806n.nrf.xml` | Añña (other) | · | 285 | · | · | · | Añña/Byākaraṇa Gantha-Saṅgaho/Mogallānapañcikā |
| 182 | `e0807n.nrf.xml` | Añña (other) | · | 304 | · | · | · | Añña/Byākaraṇa Gantha-Saṅgaho/Payogasiddhipāṭha |
| 183 | `e0808n.nrf.xml` | Añña (other) | · | 10 | · | · | · | Añña/Byākaraṇa Gantha-Saṅgaho/Vuttodayapāṭha |
| 184 | `e0809n.nrf.xml` | Añña (other) | · | 99 | · | · | · | Añña/Byākaraṇa Gantha-Saṅgaho/Abhidhānappadāpikāpāṭha |
| 185 | `e0810n.nrf.xml` | Añña (other) | · | 618 | · | · | · | Añña/Byākaraṇa Gantha-Saṅgaho/Abhidhānappadāpikāṭīkā |
| 186 | `e0811n.nrf.xml` | Añña (other) | · | 34 | · | · | · | Añña/Byākaraṇa Gantha-Saṅgaho/Subodhālaṅkārapāṭha |
| 187 | `e0812n.nrf.xml` | Añña (other) | · | 355 | · | · | · | Añña/Byākaraṇa Gantha-Saṅgaho/Subodhālaṅkāraṭīkā |
| 188 | `e0813n.nrf.xml` | Añña (other) | · | 187 | · | · | · | Añña/Byākaraṇa Gantha-Saṅgaho/Bālāvatāra Gaṇṭhipadatthavinicchayasāra |
| 189 | `e1001n.nrf.xml` | Añña (other) | · | 208 | · | · | · | Añña/Nīti-Gantha-Saṅgaho/Kavidappaṇanīti |
| 190 | `e1002n.nrf.xml` | Añña (other) | · | 50 | · | · | · | Añña/Nīti-Gantha-Saṅgaho/Nītimañjarī |
| 191 | `e1003n.nrf.xml` | Añña (other) | · | 146 | · | · | · | Añña/Nīti-Gantha-Saṅgaho/Dhammanīti |
| 192 | `e1004n.nrf.xml` | Añña (other) | · | 108 | · | · | · | Añña/Nīti-Gantha-Saṅgaho/Mahārahanīti |
| 193 | `e1005n.nrf.xml` | Añña (other) | · | 36 | · | · | · | Añña/Nīti-Gantha-Saṅgaho/Lokanīti |
| 194 | `e1006n.nrf.xml` | Añña (other) | · | 50 | · | · | · | Añña/Nīti-Gantha-Saṅgaho/Suttantanīti |
| 195 | `e1007n.nrf.xml` | Añña (other) | · | 102 | · | · | · | Añña/Nīti-Gantha-Saṅgaho/Sūrassatinīti |
| 196 | `e1008n.nrf.xml` | Añña (other) | · | 77 | · | · | · | Añña/Nīti-Gantha-Saṅgaho/Cāṇakyanīti |
| 197 | `e1009n.nrf.xml` | Añña (other) | · | 353 | · | · | · | Añña/Nīti-Gantha-Saṅgaho/Naradakkhadīpanī |
| 198 | `e1010n.nrf.xml` | Añña (other) | · | 331 | · | · | · | Añña/Nīti-Gantha-Saṅgaho/Caturārakkhadīpanī |
| 199 | `e1101n.nrf.xml` | Añña (other) | · | 166 | · | · | · | Añña/Pakiṇṇaka-Gantha-Saṅgaho/Rasavāhinī |
| 200 | `e1102n.nrf.xml` | Añña (other) | · | 110 | · | · | · | Añña/Pakiṇṇaka-Gantha-Saṅgaho/Sīmavisodhanīpāṭha |
| 201 | `e1103n.nrf.xml` | Añña (other) | · | 216 | · | · | · | Añña/Pakiṇṇaka-Gantha-Saṅgaho/Vessantaragīti |
| 202 | `e1201n.nrf.xml` | Añña (other) | · | · | · | · | 345 | Añña/Sihaḷa-Gantha-Saṅgaho/Moggallāna Vuttivivaraṇapañcikā |
| 203 | `e1202n.nrf.xml` | Añña (other) | · | · | · | · | 89 | Añña/Sihaḷa-Gantha-Saṅgaho/Thūpavaṃsa |
| 204 | `e1203n.nrf.xml` | Añña (other) | · | · | · | · | 111 | Añña/Sihaḷa-Gantha-Saṅgaho/Dāṭhavaṃsa |
| 205 | `e1204n.nrf.xml` | Añña (other) | · | · | · | · | 15 | Añña/Sihaḷa-Gantha-Saṅgaho/Dhātupāṭhavilāsiniyā |
| 206 | `e1205n.nrf.xml` | Añña (other) | · | · | · | · | 71 | Añña/Sihaḷa-Gantha-Saṅgaho/Dhātuvaṃsa |
| 207 | `e1206n.nrf.xml` | Añña (other) | · | · | · | · | 27 | Añña/Sihaḷa-Gantha-Saṅgaho/Hatthavanagallavihāravaṃsa |
| 208 | `e1207n.nrf.xml` | Añña (other) | · | · | · | · | 46 | Añña/Sihaḷa-Gantha-Saṅgaho/Jinacaritaya |
| 209 | `e1208n.nrf.xml` | Añña (other) | · | · | · | · | 693 | Añña/Sihaḷa-Gantha-Saṅgaho/Jinavaṃsadīpaṃ |
| 210 | `e1209n.nrf.xml` | Añña (other) | · | · | · | · | 32 | Añña/Sihaḷa-Gantha-Saṅgaho/Telakaṭāhagāthā |
| 211 | `e1210n.nrf.xml` | Añña (other) | · | · | · | · | 75 | Añña/Sihaḷa-Gantha-Saṅgaho/Milidaṭīkā |
| 212 | `e1211n.nrf.xml` | Añña (other) | · | · | · | · | 47 | Añña/Sihaḷa-Gantha-Saṅgaho/Padamañjarī |
| 213 | `e1212n.nrf.xml` | Añña (other) | · | · | · | · | 56 | Añña/Sihaḷa-Gantha-Saṅgaho/Padasādhanaṃ |
| 214 | `e1213n.nrf.xml` | Añña (other) | · | · | · | · | 12 | Añña/Sihaḷa-Gantha-Saṅgaho/Saddabindupakaraṇaṃ |
| 215 | `e1214n.nrf.xml` | Añña (other) | · | · | · | · | 36 | Añña/Sihaḷa-Gantha-Saṅgaho/Kaccāyanadhātumañjusā |
| 216 | `e1215n.nrf.xml` | Añña (other) | · | · | · | · | 84 | Añña/Sihaḷa-Gantha-Saṅgaho/Sāmantakūṭavaṇṇanā |

