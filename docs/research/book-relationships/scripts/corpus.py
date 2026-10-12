"""Shared loading for the book-relationships measurement scripts.

The corpus is read from $CST_XML_DIR, else the CST Reader download on macOS. The app's downloaded copy
is the master (fsnow, 2026-10-09), so measurements are taken against it, never against a checked-in copy.
Book metadata comes from src/CST.Core/Books.cs, parsed as text so no build is needed.
"""
import os, re

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
BOOKS_CS = os.path.join(REPO, "src", "CST.Core", "Books.cs")
XML_DIR = os.environ.get("CST_XML_DIR") or os.path.expanduser(
    "~/Library/Application Support/CSTReader/xml")


def read_xml(file_name):
    """Decode one corpus file. The corpus is UTF-16-LE with a BOM; byte-level tools are unreliable on it."""
    with open(os.path.join(XML_DIR, file_name), encoding="utf-16") as f:
        return f.read()


def books():
    """Books.cs entries in index order, as dicts.

    `canonical` is Matn != Other. That is the canonical / añña division, and it is NOT the file extension:
    21 canonical files are .nrf (s0104t/s0105t are DN ṭīkās, s0518m/s0520m are mūla).
    """
    src = open(BOOKS_CS, encoding="utf-8").read()
    out = []
    for block in re.split(r"book = new Book\(\);", src)[1:]:
        def field(name, pat=r"(\S+?)"):
            m = re.search(rf"\bbook\.{name} = {pat};", block)
            return m.group(1) if m else None
        matn = field("Matn", r"CommentaryLevel\.(\w+)")
        out.append(dict(
            index=int(field("Index")),
            file=field("FileName", r'"([^"]+)"'),
            matn=matn,
            canonical=matn != "Other",
            pitaka=field("Pitaka", r"Pitaka\.(\w+)"),
            book_type=field("BookType", r"BookType\.(\w+)"),
            mula=_int(field("MulaIndex")),
            atthakatha=_int(field("AtthakathaIndex")),
            tika=_int(field("TikaIndex")),
            chapter_list_types=field("ChapterListTypes", r'"([^"]*)"') or "",
            unlinked="// unlinked" in block,
        ))
    return out


def _int(s):
    return int(s) if s is not None else None
