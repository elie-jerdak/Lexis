from docling_core.types.doc.document import SectionHeaderItem

from app.logging_config import log
from app.hybrid_heading_inference import HybridHeadingInferenceService   

_heading_service = HybridHeadingInferenceService()

import re

ROMAN_NUMERAL_RE = re.compile(
    r"^M{0,4}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$"
)

def _is_valid_roman(token: str) -> bool:
    token = token.upper()
    return bool(token) and bool(ROMAN_NUMERAL_RE.match(token))

# Token is the ONLY capturing group -- keyword name itself is
# non-capturing via (?i:...), scoping case-insensitivity to just the
# keyword. combined_numbering_level reads group(1) below.
KEYWORD_PREFIX_RE = re.compile(
    r"^\s*(?i:chapter|article|section|part|appendix|annex)\s+([0-9]+|[IVXLCDM]+|[A-Z]{1,2})\b"
)

LEGAL_SECTION_SYMBOL_RE = re.compile(r"^\s*§\s*(\d+(?:\.\d+)*)")

# Uppercase only, deliberately -- your own data shows lowercase "a.",
# "b.", "i.", "ii." used for plain reference-list items (not headings),
# while appendix/division markers conventionally use uppercase ("A.
# Scope"). This is a real typographic convention, not a guess, but it's
# also the one part of this fix with the least confirmed evidence behind
# it -- watch for it specifically on the next new PDF.
BARE_LETTER_HEADING_RE = re.compile(r"^\s*([A-Z]{1,2})[.\-–—]\s+\S")

# Bare roman numeral headings with 3+ letters (III., VIII., XII., XIV.).
# 1-2 letter romans (I., IV., IX.) are already covered by
# BARE_LETTER_HEADING_RE and don't need a separate path here.
# Deliberately uppercase-only, same rationale as BARE_LETTER_HEADING_RE:
# lowercase "iii." / "viii." are classic list-item markers, not headings.
BARE_ROMAN_HEADING_RE = re.compile(r"^\s*([IVXLCDM]{3,})[.\-–—]\s+\S")

# Letter-prefixed compound numbering, any separator style:
#   "A.2 Scope"    (dot)
#   "A2 Scope"     (fused, no separator)
#   "A-2 Scope"    (hyphen)
#   "D2.6 Handling..." (fused letter+first digit, dot before sub-levels)
# Separator is optional and, when present, matches the same class as
# BARE_LETTER_HEADING_RE's -- keeps the two regexes consistent so a
# document that uses "-" for both "A-Scope" and "A-2" behaves the same
# way in each case.
LETTER_NUMBER_COMPOUND_RE = re.compile(
    r"^\s*(?P<letter>[A-Z]{1,2})[.\-–—]?(?P<number>\d+(?:\.\d+)*)\.?\s+\S"
)

ANY_HEADING_PREFIX_RE = re.compile(
    r"^\s*(?:(?i:chapter|article|section|part|appendix|annex)\s+(?:[0-9]+|[IVXLCDM]+|[A-Z]{1,2})\b"
    r"|§\s*\d+(?:\.\d+)*"
    r"|\d+(?:\.\d+)*\.?"
    r"|[A-Z]{1,2}[.\-–—]?\d+(?:\.\d+)*\.?"
    r"|[IVXLCDM]{3,}"
    r"|[A-Z]{1,2}(?![a-zA-Z]))"  # negative lookahead: stops "An" matching inside "Annex"
)


def combined_numbering_level(text: str) -> tuple[int | None, bool, str | None]:
    """
    Returns (level, is_alternate_scheme, scheme). `scheme` names which
    alternate path matched: "keyword-roman", "bare-roman",
    "keyword-decimal", "keyword-letter", "letter-compound",
    "section-symbol", "bare-letter", or None (pure decimal, or no match).
    `scheme` is not currently consumed by build_structured_elements --
    it's returned for callers that want to distinguish numbering styles
    (e.g. future roman-root nesting) without needing another regex pass.
    """
    text = text.strip()

    decimal_level = numbering_depth_for_hierarchy(text)
    if decimal_level is not None:
        return decimal_level, False, None

    compound_match = LETTER_NUMBER_COMPOUND_RE.match(text)
    if compound_match:
        segments = compound_match.group("number").split(".")
        return 1 + len(segments), True, "letter-compound"

    keyword_match = KEYWORD_PREFIX_RE.match(text)
    if keyword_match:
        token = keyword_match.group(1)
        if token.isdigit():
            scheme = "keyword-decimal"
        elif _is_valid_roman(token):
            scheme = "keyword-roman"
        else:
            scheme = "keyword-letter"
        remainder = text[keyword_match.end():].lstrip(" :.-–—")
        remainder_level = numbering_depth_for_hierarchy(remainder)
        return (remainder_level, True, scheme) if remainder_level is not None else (1, True, scheme)

    section_symbol_match = LEGAL_SECTION_SYMBOL_RE.match(text)
    if section_symbol_match:
        remainder_level = numbering_depth_for_hierarchy(section_symbol_match.group(1))
        return (remainder_level, True, "section-symbol") if remainder_level is not None else (1, True, "section-symbol")

    roman_match = BARE_ROMAN_HEADING_RE.match(text)
    if roman_match and _is_valid_roman(roman_match.group(1)):
        return 1, True, "bare-roman"

    if BARE_LETTER_HEADING_RE.match(text):
        return 1, True, "bare-letter"

    return None, False, None


LIST_TYPE_SECTION_RE = re.compile(
    r"\b(references?|bibliography|citations?|works cited|"
    r"terms and definitions|definitions|glossary|"
    r"abbreviations|acronyms)\b",
    re.IGNORECASE,
)

def _is_list_type_context(current_hierarchy_context: dict[int, str], candidate_level: int) -> bool:
    """
    True only if a list-type heading (References, Definitions, Glossary,
    Abbreviations, ...) is open at a level SHALLOWER than candidate_level
    -- meaning candidate would nest inside it as a list entry. A candidate
    at the same or shallower level than the list heading is a sibling
    section, not a member of the list, and should not be suppressed.
    """
    for level, heading_text in current_hierarchy_context.items():
        if level < candidate_level and LIST_TYPE_SECTION_RE.search(heading_text):
            return True
    return False

HEADING_NUMBER_PREFIX_RE = re.compile(r"^\s*\d+(?:\.\d+)*\.?\s*")
HEADING_TERM_SEPARATOR_RE = re.compile(r"(?<=\s)[-–—]|[-–—](?=\s)|:\s")
BARE_TOP_LEVEL_NUMBER_RE = re.compile(r"^\s*\d+\.?\s+\S")
# A period counts as a sentence end only if preceded by 2+ word chars
# (skips single-letter abbreviations like "U.S.", "e.g.") and followed
# by whitespace or end of string.
HEADING_SENTENCE_END_RE = re.compile(r"(?<=\w{2})\.(?=\s|$)")

import fitz
from docling_core.types.doc import BoundingBox, CoordOrigin

def derive_heading_label(text: str, max_length: int = 160) -> str:
    text = text.strip()

    prefix_match = ANY_HEADING_PREFIX_RE.match(text)
    prefix_end = prefix_match.end() if prefix_match else 0

    sep_skip_match = re.match(r"[:.\-–—]\s+", text[prefix_end:])
    if sep_skip_match:
        prefix_end += sep_skip_match.end()

    body = text[prefix_end:]

    term_match = HEADING_TERM_SEPARATOR_RE.search(body)
    sentence_match = HEADING_SENTENCE_END_RE.search(body)

    cut_candidates = [m.start() for m in (term_match, sentence_match) if m]

    if cut_candidates:
        label = text[: prefix_end + min(cut_candidates)].strip()
    else:
        label = text

    if len(label) > max_length:
        label = label[:max_length].rsplit(" ", 1)[0] + "…"

    return label

def numbering_depth_for_hierarchy(text: str) -> int | None:
    """
    Like HybridHeadingInferenceService.numbering_depth, but a trailing
    ".0" segment (e.g. "2.0. Terms and Definitions") is one level
    shallower than its own children ("2.1", "2.2", ...) instead of
    colliding with them at the same depth.
    """
    depth = _heading_service.numbering_depth(text)
    if depth is None:
        return None

    match = _heading_service.NUMBERED_HEADING_RE.match(text.strip())
    if match and match.group("number").split(".")[-1] == "0":
        depth -= 1

    return max(depth, 1)

def _normalize_for_dedup(text: str) -> str:
    return re.sub(r"\s+", " ", text or "").strip().casefold()

# Matches dashes with optional space, but NOT when trapped between two letters
BOLD_LABEL_TERMINATOR_RE = re.compile(r"(?<=\w{2})[.:;,](?=\s|$)|\s*[-–—]\s*(?!\w)|(?<!\w)\s*[-–—]\s*")


def _leading_bold_run_has_label(pdf_doc, page_no: int, bbox, page_height: float) -> bool:
    """
    Does the leading bold run inside this item's own bbox extend past
    its numbering into real label text?

    Uses PyMuPDF's `clip` to restrict extraction to the item's exact
    bbox, rather than matching against whole-page blocks/lines. This
    makes the check independent of how fitz happens to group text on
    a given page -- correct for any PDF regardless of layout density,
    since it never depends on fitz's block boundaries lining up with
    Docling's.
    """
    if bbox is None:
        return False

    if bbox.coord_origin == CoordOrigin.BOTTOMLEFT:
        bbox = bbox.to_top_left_origin(page_height)  # VERIFY method name

    target_rect = fitz.Rect(bbox.l, bbox.t, bbox.r, bbox.b)
    if target_rect.is_empty:
        return False

    page = pdf_doc[page_no - 1]
    clipped = page.get_text("dict", clip=target_rect)

    spans_in_order = []
    for block in clipped.get("blocks", []):
        if block.get("type") != 0:
            continue
        for line in block.get("lines", []):
            spans_in_order.extend(line.get("spans", []))

    bold_prefix_text = ""
    for span in spans_in_order:
        span_text = span.get("text", "")
        font_name = span.get("font", "").lower()
        is_bold_span = "bold" in font_name or "black" in font_name

        if not is_bold_span:
            # A whitespace-only span reporting non-bold is almost always
            # a PyMuPDF font-metadata artifact on the gap between two
            # real bold runs (e.g. between "3.1" and "Annual"), not an
            # actual style break -- don't let it terminate the scan.
            # Only a non-bold span with real (non-whitespace) content
            # means the bold run has genuinely ended.
            if span_text.strip() == "":
                bold_prefix_text += span_text
                continue
            break

        bold_prefix_text += span_text

        # Stop at the label's own terminating punctuation, even if the
        # font is still reporting bold past it (trailing bold space, or
        # a PDF that bolds the whole first sentence/clause). Without
        # this, a heading like "3.1 Annual Recognition Solicitation: The
        # recognition process..." or "3.1 Scope, Purpose, and Field..."
        # -- where bold styling runs well past the actual label -- would
        # swallow everything up to the next non-bold span instead of
        # stopping at the label's own punctuation.
        terminator_match = BOLD_LABEL_TERMINATOR_RE.search(bold_prefix_text)
        if terminator_match:
            bold_prefix_text = bold_prefix_text[: terminator_match.end()]
            break

    remainder = ANY_HEADING_PREFIX_RE.sub("", bold_prefix_text, count=1).lstrip(".:-–— ,;").strip()
    return len(remainder) > 0

def build_structured_elements(document, pdf_path: str):
    """
    Walk docling's structured document tree, in document order, and group
    elements by page -- preserving type info, heading hierarchy, and
    cross-page continuation for tables/figures.

    Heading hierarchy is read directly from `SectionHeaderItem.level`.
    That level is set by docling-hierarchical-pdf's ResultPostprocessor
    (see main.py, called right after converter.convert()), which corrects
    it in place on docling's own document tree using PDF bookmarks first,
    falling back to numbering/font-style rules.

    This replaces the old approach of building a separate heading list
    from raw PyMuPDF text (HeadingInferenceService) and then matching it
    back against docling's item stream by text/prefix comparison. That
    approach broke whenever docling's own line/paragraph grouping
    disagreed with PyMuPDF's -- e.g. a clause number and its title split
    across two lines in the PDF, which PyMuPDF and docling would
    sometimes chunk differently from each other. Reading the level
    straight off docling's own (corrected) items sidesteps that class of
    bug entirely, since there's no second, independently-built heading
    list to go out of sync with.
    """
    elements_by_page = {}
    table_ids = {}
    figure_ids = {}
    order_counter = 0
    page_order_counters = {}

    # level -> heading text, representing the currently "open" hierarchy path
    current_hierarchy_context = {} 
    table_artifact_pool = ""
    
    pdf_doc = fitz.open(pdf_path)
    
    for item, _docling_level in document.iterate_items():
        prov_entries = getattr(item, "prov", None)
        if not prov_entries:
            continue  # structural items (groups) carry no page provenance

        label = str(getattr(item, "label", item.__class__.__name__))
        item_page = prov_entries[0].page_no
        item_text = getattr(item, "text", "") or ""
         

        is_heading = isinstance(item, SectionHeaderItem)
        # combined_numbering_level now returns a 3-tuple (level,
        # is_alternate_scheme, scheme); scheme is unused here since
        # roman-root parent nesting is intentionally left out of this
        # version -- kept simple, matching the original behavior.
        numbering_level, is_alternate_scheme, _numbering_scheme = combined_numbering_level(item_text)

        if is_heading:
            normalized_item_text = _normalize_for_dedup(item_text)
            if table_artifact_pool and normalized_item_text and normalized_item_text in table_artifact_pool:
                is_heading = False
                label = "text"
            elif numbering_level is not None:
                # Docling sometimes classifies "bold numbering + long
                # regular-text sentence" as a genuine SectionHeaderItem
                # (see D2.8.2 above) -- it isn't a heading, just a
                # numbered clause whose leading digits happen to render
                # bold. Run the same bold-run check used to PROMOTE plain
                # text into a heading, here as a VETO on docling's own
                # classification: if the bold styling doesn't extend past
                # the numbering into real label text, this isn't a
                # heading either, regardless of what docling called it.
                bbox = prov_entries[0].bbox if prov_entries else None
                page_obj = document.pages.get(item_page)
                page_height = page_obj.size.height if page_obj and page_obj.size else None
                if page_height and not _leading_bold_run_has_label(pdf_doc, item_page, bbox, page_height):
                    is_heading = False
                    label = "text"
                    log.info(
                        f"DOCLING HEADING VETOED page={item_page} "
                        f"text='{item_text[:60]}' -> demoted, bold run "
                        f"doesn't extend past numbering"
                    ) 
        elif label in ("list_item", "paragraph", "text"):
            if numbering_level is not None:
                bbox = prov_entries[0].bbox if prov_entries else None
                page_obj = document.pages.get(item_page)
                page_height = page_obj.size.height if page_obj and page_obj.size else None
                if page_height and _leading_bold_run_has_label(pdf_doc, item_page, bbox, page_height):
                    is_heading = True
                elif numbering_level is not None:
                    log.info(
                        f"BOLD-RUN CHECK page={item_page} "
                        f"page_height={page_height} bbox={bbox} "
                        f"text='{item_text[:60]}' -> not promoted"
                    )

        if is_heading:
            # Numbering depth is the one scale that's consistent across
            # the whole document -- Docling's own item.level isn't
            # reliable enough to key the hierarchy dict on (it has
            # assigned the same raw level to a chapter and one of that
            # chapter's own numbered subsections, silently overwriting
            # the parent in current_hierarchy_context).
            if numbering_level is not None:
                level = numbering_level
            elif isinstance(item, SectionHeaderItem):
                # Bare top-level titles like "1. Program Summary" have no
                # decimal, so numbering_depth_for_hierarchy can't score
                # them -- without this they fall back to Docling's own
                # item.level, which can coincide with a numbered child's
                # level (e.g. both land on 2) and get silently overwritten.
                level = 1 if BARE_TOP_LEVEL_NUMBER_RE.match(item_text.strip()) else item.level
            else:
                level = 1

            # Always derive a short label -- Docling itself sometimes
            # classifies a bold lead-in + long trailing paragraph as a
            # genuine SectionHeaderItem (see 2.9, 2.12), not just our
            # own numbering-fallback promotions.
            heading_label = derive_heading_label(item_text)

            current_hierarchy_context[level] = heading_label
            for k in [k for k in list(current_hierarchy_context) if k > level]:
                del current_hierarchy_context[k]

        active_headings = [
            current_hierarchy_context[k] for k in sorted(current_hierarchy_context.keys())
        ]

        element = {
            "elementType": "section_header" if is_heading else label,
            "order": order_counter,
            "headingPath": active_headings,
        }
        
        order_counter += 1

        if is_heading:
            element["headingLevel"] = level
            element["headingText"] = heading_label
            element["headingSource"] = (
                "docling-hierarchical-pdf" if isinstance(item, SectionHeaderItem)
                else "numbering-fallback"
            )

        if label == "table":
            key = id(item)
            if key not in table_ids:
                table_ids[key] = len(table_ids) + 1
            element["tableId"] = table_ids[key]
            try:
                element["content"] = item.export_to_markdown(document)
            except Exception:
                element["content"] = None

            if element["content"]:
                for md_line in element["content"].strip().splitlines():
                    stripped = md_line.strip()
                    if stripped.startswith("|") and not set(stripped) <= set("|-: "):
                        table_artifact_pool = (
                            table_artifact_pool + " " +
                            _normalize_for_dedup(stripped.strip("|").replace("|", " "))
                        ).strip()
                        break

        elif label == "picture":
            key = id(item)
            if key not in figure_ids:
                figure_ids[key] = len(figure_ids) + 1
            element["figureId"] = figure_ids[key]
            try:
                element["caption"] = item.caption_text(document)
            except Exception:
                element["caption"] = None

        else:
            element["text"] = item_text or None
            if label == "caption":
                table_artifact_pool = (table_artifact_pool + " " + _normalize_for_dedup(item_text)).strip()

        num_pages_spanned = len(prov_entries)
        for idx, prov in enumerate(prov_entries):
            page_element = dict(element)
            page_element["continuedFromPreviousPage"] = idx > 0
            page_element["continuesOnNextPage"] = idx < num_pages_spanned - 1

            local_order = page_order_counters.get(prov.page_no, 0)
            page_element["pageOrder"] = local_order
            page_order_counters[prov.page_no] = local_order + 1

            elements_by_page.setdefault(prov.page_no, []).append(page_element)
            
    pdf_doc.close()
    return elements_by_page