import os

from app.logging_config import log

# Off by default. Turn on in PowerShell with: $env:LEXIS_FAST_EXPORT="1"
FAST_EXPORT = os.environ.get("LEXIS_FAST_EXPORT", "0") == "1"
SEP = "@@LEXISPAGEBREAK@@"


def _pages_with_own_items(document):
    """Pages that have at least one item starting on them (first provenance page),
    in order; None if the document order isn't monotonic by page."""
    seq = [it.prov[0].page_no for it, _ in document.iterate_items() if getattr(it, "prov", None)]
    if any(a > b for a, b in zip(seq, seq[1:])):
        return None
    return sorted(set(seq))


def _split_pages(document, export):
    pages_with_items = _pages_with_own_items(document)
    if pages_with_items is None:
        return None
    parts = export(page_break_placeholder=SEP).split(SEP)
    if len(parts) != len(pages_with_items):
        return None
    last = len(parts) - 1
    cleaned = []
    for i, part in enumerate(parts):
        if i > 0 and part.startswith("\n\n"):
            part = part[2:]
        if i < last and part.endswith("\n\n"):
            part = part[:-2]
        cleaned.append(part)
    by_page = dict(zip(pages_with_items, cleaned))
    return {p: by_page.get(p, "") for p in sorted(document.pages.keys())}


def _export_per_page(document, export):
    return {p: export(page_no=p) for p in sorted(document.pages.keys())}


def export_markdown_pages(document, fast: bool = False) -> dict[int, str]:
    if fast:
        res = _split_pages(document, document.export_to_markdown)
        if res is not None:
            return res
        log.warning("FALLBACK: markdown page split not safe for this document; using slow per-page export")
    return _export_per_page(document, document.export_to_markdown)


def export_text_pages(document, fast: bool = False) -> dict[int, str]:
    if fast:
        res = _split_pages(document, document.export_to_text)
        if res is not None:
            return res
        log.warning("FALLBACK: text page split not safe for this document; using slow per-page export")
    return _export_per_page(document, document.export_to_text)