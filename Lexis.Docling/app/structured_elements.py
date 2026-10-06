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


TOC_DOT_LEADER_RE = re.compile(r"\.{4,}\s*\d+\s*$")


def _is_toc_line(text: str) -> bool:
    """
    Table-of-contents dot-leader lines ("APPENDIX III.......android 25")
    are a near-universal PDF convention -- a heading-like label, a run
    of leader dots, and a trailing page number -- independent of any
    one document's wording. Docling sometimes classifies these as real
    headings (they're often bold/large-font, same as a real heading),
    which then permanently pollutes every subsequent item's headingPath
    with a page-number-suffixed ghost ancestor for the rest of the
    document -- there's nothing later that looks "shallower" than a
    fake root like this to ever close it.
    """
    return bool(TOC_DOT_LEADER_RE.search(text.strip()))


def _looks_like_sentence_not_heading(text: str) -> bool:
    """
    A heading title is short and doesn't read as a grammatically
    complete sentence/clause. Numbered/lettered enumerated LIST ITEMS
    ("c) the composition of the strengths are quantitatively
    proportional, i.e. the ratio between...") share the surface
    features that make something look like a heading -- a short
    alphanumeric prefix, sometimes bold styling across the whole run --
    but are body content, not structural headings, and must never
    become part of the persistent hierarchy.

    This check runs independently of has_own_numbering/numbering
    recognition below: a lowercase list marker like "c)" isn't matched
    by any numbering regex at all (deliberately -- see
    _lowercase_roman_prefix_end), which otherwise leaves it with zero
    scrutiny if Docling happens to classify it as a heading. Sentence
    shape is a general enough signal to catch that regardless of
    whether the prefix itself was ever recognized as numbering.

    General, not document-specific:
    - length: real heading titles are almost always well under ~20
      words / ~150 characters. Longer than that is prose.
    - terminal punctuation on an already-substantial clause: most real
      headings carry no trailing period/semicolon at all. A period only
      counts once the text is long enough that a real title wouldn't
      normally end that way (a short "1.2 Overview." keeping its
      trailing dot is still a fine heading).
    """
    text = text.strip()
    word_count = len(text.split())
    if len(text) > 150 or word_count > 20:
        return True
    if len(text) > 50 and word_count > 8 and re.search(r"[.;]\s*$", text):
        return True
    return False


PAGE_NUMBER_ONLY_RE = re.compile(r"^\s*(?:page\s+)?[-–—]?\s*\d+\s*[-–—]?\s*$", re.IGNORECASE)


def _is_bare_page_number(text: str) -> bool:
    """
    A heading whose ENTIRE text is nothing but a bare integer
    (optionally decorated -- "45", "Page 45", "- 45 -") is virtually
    never a genuine heading. Real numbering-only headings always carry
    either an explicit keyword ("ARTICLE V") or a title in the SAME or
    an adjacent block; a lone bare number with nothing else at all is a
    page-footer/header artifact that Docling's font/layout clustering
    occasionally mis-promotes to a SectionHeaderItem -- it's often
    isolated on its own line, which looks structurally similar to a
    real heading even though the content itself says nothing.

    This needs its own check because a bare number FAILS has_own_
    numbering entirely (BARE_TOP_LEVEL_NUMBER_RE requires trailing text
    after the digits, which a page number by definition doesn't have)
    and is far too short to trip the sentence-shape or TOC-line checks
    -- without this, it sails through every existing safety net with
    zero scrutiny, and Docling calling it a heading is the only signal
    anywhere in the pipeline.
    """
    return bool(PAGE_NUMBER_ONLY_RE.match(text.strip()))


def _is_strong_compound_numbering(numbering_scheme: str | None, numbering_level: int | None) -> bool:
    """
    Multi-SEGMENT compound numbering ("IV.1.1", "4.1.2") is specific,
    structural evidence of a real heading on its own -- flat enumerated
    lists overwhelmingly use a single numbering TOKEN for a bullet
    ("1.", "a)", "i.", "IV."), not a multi-level compound one. A PDF
    generator that renders a deep heading tier with no distinguishing
    style at all (confirmed: Docling's own layout model didn't tag it
    as a heading either, so this isn't a font-detection miss -- there's
    genuinely nothing to detect) still leaves this pattern as strong
    enough evidence to promote without requiring bold/italic
    confirmation. Single-token numbering ("1. Confirm the invoice")
    stays gated behind the styling check below -- that ambiguity is
    real and common, this one isn't.
    A letter+number combination is, by construction, always at least a
    two-part compound reference ("A.1", "III.1") -- flat list markers
    never combine a letter and a number like that, they use a single
    token ("1.", "a)", "i."). So any letter-compound match qualifies,
    without a depth threshold; only the decimal ("None"-scheme) path
    needs one, since its minimum achievable match is already a
    multi-segment decimal (a bare "1." doesn't reach this scheme at
    all -- see numbering_depth_for_hierarchy), so >=2 there is really
    just "any match of this scheme" too, stated for clarity.
    """
    if numbering_scheme == "letter-compound":
        return True
    if numbering_scheme is None and numbering_level is not None and numbering_level >= 2:
        return True
    return False

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
    r"^\s*(?P<letter>[A-Z]{1,4})[.\-–—]?(?P<number>\d+(?:\.\d+)*)\.?\s+\S"
)

ANY_HEADING_PREFIX_RE = re.compile(
    r"^\s*(?:(?i:chapter|article|section|part|appendix|annex)\s+(?:[0-9]+|[IVXLCDM]+|[A-Z]{1,2})\b"
    r"|§\s*\d+(?:\.\d+)*"
    r"|\d+(?:\.\d+)*\.?"
    r"|[A-Z]{1,4}[.\-–—]?\d+(?:\.\d+)*\.?"
    r"|[IVXLCDM]{3,}"
    r"|[A-Z]{1,2}(?![a-zA-Z]))"  # negative lookahead: stops "An" matching inside "Annex"
)


def _lowercase_roman_prefix_end(text: str) -> int:
    """
    Recognizes lowercase roman-numeral list markers ("i - Testing on
    product quality", "ii - Bioequivalence surrogate inference") that
    ANY_HEADING_PREFIX_RE's roman/letter alternatives can't see, since
    those are deliberately uppercase-only. Loosening them to be
    case-insensitive would make ordinary lowercase words made up of
    roman-numeral letters ("civil", "mix", "vivid", "mild"...) match as
    a heading prefix anywhere they open a text block, not just at
    actual list markers.

    Scoped narrowly to avoid that: only matches a short run of roman-
    numeral characters immediately followed by a "- "/": " label
    separator (the actual convention seen in practice), AND only if
    that run is itself syntactically a valid roman numeral via
    _is_valid_roman -- "civil - Some heading" matches the character
    class but fails roman validation, so it's correctly rejected.
    """
    match = re.match(r"[ivxlcdm]{1,4}(?=\s*[-–—:]\s)", text)
    if match and _is_valid_roman(match.group(0).upper()):
        return match.end()
    return 0


def _heading_prefix_end(text: str) -> int:
    """
    Single source of truth for "how much of this text is the heading's
    own numbering/keyword prefix" -- used at every call site that needs
    to separate a heading's numbering from its title/label, so the
    uppercase and lowercase-roman-list-marker rules can't drift apart
    between them.
    """
    match = ANY_HEADING_PREFIX_RE.match(text)
    if match:
        return match.end()
    return _lowercase_roman_prefix_end(text)


def classify_keyword_root_schemes(document) -> dict[str, str]:
    """
    Classify each keyword's (chapter/article/section/part/appendix/annex)
    labeling scheme as "roman" or "letter", using ALL of that keyword's
    occurrences across the whole document together -- not one heading
    guessed in isolation.

    A single-letter label like "Annex D" is structurally ambiguous by
    itself: D, I, V, X, L, C, M are all simultaneously valid Roman
    numeral characters AND valid plain letters. A per-heading check
    (_is_valid_roman on that one token) has no way to tell them apart,
    and picks wrong exactly when a document's own alphabetic sequence
    (Annex A, B, C, D, E, F...) happens to pass through one of those
    overloaded letters -- "Annex D" scores as Roman while its very next
    sibling "Annex E" scores as a plain letter, even though they're the
    same kind of label.

    The document itself resolves the ambiguity: if the same keyword's
    family contains ANY multi-character Roman numeral ("Chapter II",
    "Chapter III"), that's unambiguous evidence of a genuine Roman
    sequence. Otherwise -- every occurrence is a single character --
    the family is treated as plain alphabetic labeling, which is by far
    the more common convention for Annex/Appendix/Part lettering and
    means every member of the family (including ones that happen to
    look like valid Roman characters) is scored consistently.

    A keyword with only one occurrence and no siblings to disambiguate
    against defaults to "letter" for the same reason: a genuinely
    Roman, non-sequential single letter ("Part D" as literal numeral
    500, with no "Part I"/"Part II" anywhere else) is real but rare
    compared to a lone alphabetic label.
    """
    tokens_by_keyword: dict[str, list[str]] = {}

    for item, _ in document.iterate_items():
        text = getattr(item, "text", "") or ""
        match = KEYWORD_PREFIX_RE.match(text)
        if not match:
            continue
        keyword = match.group(0).split()[0].lower()
        token = match.group(1)
        if token.isdigit():
            continue
        tokens_by_keyword.setdefault(keyword, []).append(token)

    schemes = {}
    for keyword, tokens in tokens_by_keyword.items():
        if any(len(t) > 1 and _is_valid_roman(t) for t in tokens):
            schemes[keyword] = "roman"
        else:
            schemes[keyword] = "letter"

    return schemes


def combined_numbering_level(
    text: str,
    keyword_schemes: dict[str, str] | None = None,
) -> tuple[int | None, bool, str | None]:
    """
    Returns (level, is_alternate_scheme, scheme). `scheme` names which
    alternate path matched: "keyword-roman", "bare-roman",
    "keyword-decimal", "keyword-letter", "letter-compound",
    "section-symbol", "bare-letter", or None (pure decimal, or no match).

    `keyword_schemes` is the output of classify_keyword_root_schemes --
    a document-level roman/letter classification per keyword, used to
    resolve the "Annex D" vs "Annex E" ambiguity described there. When
    not supplied (e.g. a caller scoring a single heading with no
    document context available), falls back to the old per-token
    Roman-character check.
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
        keyword_name = keyword_match.group(0).split()[0].lower()
        if token.isdigit():
            scheme = "keyword-decimal"
        else:
            family_scheme = (keyword_schemes or {}).get(keyword_name)
            is_roman = (
                family_scheme == "roman"
                if family_scheme is not None
                else _is_valid_roman(token)
            )
            scheme = "keyword-roman" if is_roman else "keyword-letter"
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

    bare_letter_match = BARE_LETTER_HEADING_RE.match(text)
    if bare_letter_match:
        # 1-2 letter tokens are ambiguous: I, V, X, L, C, D, M (and valid
        # combos like II, IV, IX, XI, ...) are simultaneously valid
        # roman numerals AND valid annex-style bare letters.
        # BARE_ROMAN_HEADING_RE alone can't catch these (3+ letters
        # only), so without this check "I. Main Heading" resolved to the
        # same scheme as "A. Subheading" -- nothing told open-root
        # tracking that "I." was a roman root. Letters with no valid
        # roman reading (A, B, E, F, G, H, J, K, N, ...) are unaffected.
        token = bare_letter_match.group(1)
        if _is_valid_roman(token):
            return 1, True, "bare-roman"
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

    prefix_end = _heading_prefix_end(text)

    # Tolerate a space between the prefix and its separator ("i - Title",
    # not just "i- Title") -- needed for lowercase-roman list markers,
    # where the prefix match itself can't consume the separator (it's
    # only a lookahead, so as not to falsely swallow "- " that belongs
    # to unrelated text after a non-numbering word).
    sep_skip_match = re.match(r"\s*[:.\-–—]\s+", text[prefix_end:])
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


def _compute_baseline_font_size(pdf_doc) -> float | None:
    """
    Most heading conventions signal a heading via a LARGER font size --
    at least as common as, and often used instead of, bold or italic.
    _leading_bold_run_has_label previously had no visibility into this
    at all, which silently failed every numbered heading in a document
    whose only heading signal is size (e.g. an academic paper's
    "1 Introduction", "2 Overview" -- Docling's own layout model
    doesn't classify them as SectionHeaderItem either, per elementType
    "text" in the actual output, so this isn't a detection gap on our
    side alone; there's a real size difference in the PDF that nothing
    was looking at).

    Computes the single most common font size across the WHOLE document
    (weighted by character count) as a stand-in for "body text size".
    Body text dominates by volume in virtually any real document, so
    the mode is a robust, self-calibrating baseline that needs no
    per-document tuning -- unlike a fixed pt threshold, which would be
    wrong for a document set in 9pt vs one set in 12pt.
    """
    from collections import Counter

    sizes: Counter = Counter()
    for page in pdf_doc:
        page_dict = page.get_text("dict")
        for block in page_dict.get("blocks", []):
            if block.get("type") != 0:
                continue
            for line in block.get("lines", []):
                for span in line.get("spans", []):
                    text = span.get("text", "")
                    if text.strip():
                        # Round to the nearest 0.5pt to absorb tiny
                        # rendering jitter within an otherwise-uniform
                        # body size (fractional-pt noise would otherwise
                        # split one true body size into many near-
                        # duplicate buckets and undercount its mode).
                        rounded_size = round(span.get("size", 0) * 2) / 2
                        sizes[rounded_size] += len(text)

    if not sizes:
        return None
    return sizes.most_common(1)[0][0]


def _leading_bold_run_has_label(
    pdf_doc, page_no: int, bbox, page_height: float, *,
    require_label_text: bool = True,
    baseline_font_size: float | None = None,
) -> bool:
    """
    Does the leading distinctively-styled run inside this item's own
    bbox extend past its numbering into real label text?

    "Distinctive style" means bold, italic, OR a font size notably
    larger than the document's body-text baseline (see
    is_distinctive_span below and _compute_baseline_font_size) -- not
    every template marks headings with weight or slant at all. A
    generated template observed in practice styles its top two heading
    tiers bold and its third tier in italics only; treating bold as the
    only valid signal made every one of those italic headings read as
    "no distinguishing style at all" and get silently dropped. Font
    size is the same story for documents whose ONLY heading signal is
    size (common in academic/journal layouts).

    Uses PyMuPDF's `clip` to restrict extraction to the item's exact
    bbox, rather than matching against whole-page blocks/lines. This
    makes the check independent of how fitz happens to group text on
    a given page -- correct for any PDF regardless of layout density,
    since it never depends on fitz's block boundaries lining up with
    Docling's.

    require_label_text controls what counts as evidence:
    - True (default): the distinctive run must extend past the
      numbering into real trailing text WITHIN THIS BLOCK. Used when
      item_text itself already contains that trailing text (e.g.
      "D2.8.2 In addition to the general report requirements..." --
      number + a full sentence bundled in one block). If the styling
      stops right after the number, this is a numbered clause/list
      item wrongly classified as a heading, not a real one.
    - False: the numbering itself being styled is enough. Used when
      item_text is JUST the number/keyword with no trailing content at
      all (e.g. "ARTICLE V", title "MEETINGS AND PROCEDURES" given as a
      separate following heading block) -- there is no label to check
      styling on within this block, so requiring one would always fail
      and permanently block a legitimate numbering-only heading.
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
        flags = span.get("flags", 0)
        span_size = span.get("size", 0)
        # A heading's distinguishing style isn't always bold -- some
        # templates mark lower tiers with italics instead (or in
        # addition), and some use neither, relying only on a larger
        # font size (common in academic/journal layouts). flags is
        # PyMuPDF's bitfield (bit 4 / value 16 = bold, bit 1 / value 2 =
        # italic); the font-name substring check is kept as a fallback
        # for fonts that don't set flags reliably. The size comparison
        # requires a real baseline and a size that's NOTABLY larger --
        # both an absolute (>=1pt) and a relative (>=10%) margin, so
        # ordinary rendering jitter in body text doesn't get read as a
        # heading signal.
        is_larger_than_body = (
            baseline_font_size is not None
            and span_size > 0
            and span_size - baseline_font_size >= 1.0
            and span_size >= baseline_font_size * 1.1
        )
        is_distinctive_span = (
            "bold" in font_name
            or "black" in font_name
            or "italic" in font_name
            or "oblique" in font_name
            or bool(flags & 16)
            or bool(flags & 2)
            or is_larger_than_body
        )

        if not is_distinctive_span:
            if span_text.strip() == "":
                bold_prefix_text += span_text
                continue
            break

        bold_prefix_text += span_text

    # Only search for a terminator INSIDE the label text -- never inside
    # the heading's own numbering/keyword prefix. A period ending a
    # 2+ character numbering token ("II.", "10.") is not evidence that
    # the label ends there; it's just what numbering periods look like,
    # and treating it as a terminator was truncating the label away
    # entirely for any numbering 2+ characters long (silently passing
    # for single-character numerals like "I." or "V." only by the
    # accident of the regex's own \w{2} lookbehind). Matches starting
    # AT the prefix boundary itself (the numbering's trailing period)
    # are excluded too, not just ones before it.
    prefix_end = _heading_prefix_end(bold_prefix_text)
    terminator_match = next(
        (m for m in BOLD_LABEL_TERMINATOR_RE.finditer(bold_prefix_text) if m.start() > prefix_end),
        None,
    )
    if terminator_match:
        bold_prefix_text = bold_prefix_text[: terminator_match.end()]

    if not require_label_text:
        return len(bold_prefix_text.strip()) > 0

    remainder = bold_prefix_text[_heading_prefix_end(bold_prefix_text):].lstrip(".:-–— ,;").strip()
    return len(remainder) > 0


def _table_column_count(markdown_content: str | None) -> int | None:
    """
    Counts columns from the first real (non-separator) row of a table's
    exported markdown. Used as a corroborating signal for cross-page
    table continuation: two page-split fragments of the same logical
    table almost always have the same column count, WHETHER OR NOT the
    header row happens to repeat on the second page -- column count is
    invariant either way, so this works regardless of which convention
    a given table follows.
    """
    if not markdown_content:
        return None
    for md_line in markdown_content.strip().splitlines():
        stripped = md_line.strip()
        if stripped.startswith("|") and not set(stripped) <= set("|-: "):
            return stripped.strip("|").count("|") + 1
    return None


def _breaks_table_continuation(label: str, is_heading: bool, item_text: str) -> bool:
    """
    Decides whether an element sitting between two tables rules out
    treating them as one logical table split across a page break.

    Tolerated (does NOT break continuation): captions, and short
    annotation-like text -- a "(continued)" marker, a repeated header
    fragment that leaked out as its own text element, a table number/
    title line. These are exactly the kind of thing that legitimately
    sits between two fragments of the SAME table.

    Breaks continuation: a real heading, or a substantial paragraph of
    body text -- the kind of content that would only appear between two
    genuinely SEPARATE tables (a new section starting, an explanatory
    paragraph), not between two fragments of one table.
    """
    if label == "caption":
        return False
    if is_heading:
        return True
    return len(item_text.split()) > 12

# Lowercase-only, deliberately -- matches this file's existing convention
# (see BARE_LETTER_HEADING_RE / _lowercase_roman_prefix_end): lowercase
# roman characters conventionally mark list items and pagination, while
# genuine structural roman headings ("III. Drug Substance") are
# uppercase and already require trailing content via BARE_ROMAN_HEADING_RE,
# so restricting this check to lowercase avoids colliding with those.
ROMAN_PAGE_NUMBER_ONLY_RE = re.compile(
    r"^\s*(?:(?i:page)\s+)?[-–—]?\s*([ivxlcdm]+)\s*[-–—]?\s*$"
)

def _is_bare_roman_page_number(text: str) -> bool:
    match = ROMAN_PAGE_NUMBER_ONLY_RE.match(text.strip())
    if not match:
        return False
    return _is_valid_roman(match.group(1))


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
    next_table_id = 1
    figure_ids = {}
    order_counter = 0
    page_order_counters = {}
    # Tracks the most recently emitted table for cross-page continuation
    # detection (see _table_column_count / _breaks_table_continuation).
    # None whenever something non-trivial has intervened since the last
    # table, so a later table on a much-later page can't be wrongly
    # linked to a stale one.
    last_table_state = None

    # level -> heading text, representing the currently "open" hierarchy path
    current_hierarchy_context = {} 
    table_artifact_pool = ""
    # Level of the currently-open roman-numeral root (e.g. "I. Main
    # Heading"), or None. Bare-letter/decimal children have no lexical
    # tie back to their roman parent, so left alone they score their own
    # hardcoded/absolute level and collide with the root, evicting it
    # from current_hierarchy_context. See usage below.
    open_roman_root_level = None
    open_root_scheme = None
    
    pdf_doc = fitz.open(pdf_path)

    # Document-level roman-vs-letter classification per keyword (see
    # classify_keyword_root_schemes docstring) -- computed once up
    # front so every heading of the same keyword (e.g. every "Annex X")
    # is scored consistently, instead of each one guessing in isolation
    # from its own single letter.
    keyword_schemes = classify_keyword_root_schemes(document)

    # Document-wide body-text font size (see _compute_baseline_font_size
    # docstring) -- computed once up front so every heading candidate is
    # compared against the same baseline, and documents whose only
    # heading signal is size (no bold/italic at all) aren't silently
    # dropped in their entirety.
    baseline_font_size = _compute_baseline_font_size(pdf_doc)

    for item, _docling_level in document.iterate_items():
        prov_entries = getattr(item, "prov", None)
        if not prov_entries:
            continue  # structural items (groups) carry no page provenance

        label = str(getattr(item, "label", item.__class__.__name__))
        item_page = prov_entries[0].page_no
        item_text = getattr(item, "text", "") or ""

        is_heading = isinstance(item, SectionHeaderItem)
        # combined_numbering_level returns a 3-tuple (level,
        # is_alternate_scheme, scheme). scheme drives the Roman-root
        # nesting logic below (open_roman_root_level / roman_root_offset),
        # and is now resolved using the document-level keyword_schemes
        # classification rather than a per-heading isolated guess.
        numbering_level, is_alternate_scheme, numbering_scheme = combined_numbering_level(
            item_text, keyword_schemes
        )
        
        # Does item_text itself carry label content beyond its own
        # numbering? A block that is JUST a number/keyword ("ARTICLE V",
        # "1.") -- title given as a SEPARATE following heading -- has no
        # remainder of its own. A block bundling numbering AND a full
        # sentence in ONE block ("D2.8.2 In addition to...") does. Both
        # the promotion and veto checks below key off this distinction,
        # via require_label_text on _leading_bold_run_has_label.
        stripped_item_text = item_text.strip()
        own_prefix_end = _heading_prefix_end(stripped_item_text)
        own_remainder = stripped_item_text[own_prefix_end:].lstrip(" :.-–—").strip()
        # Bare top-level numbering ("1. Maintain...") has no dot, so
        # combined_numbering_level can't score it -- without also
        # checking BARE_TOP_LEVEL_NUMBER_RE here, numbered list items
        # like this were invisible to the veto below entirely and
        # passed through unchecked whenever docling misclassified them.
        has_own_numbering = numbering_level is not None or bool(BARE_TOP_LEVEL_NUMBER_RE.match(stripped_item_text))

        if _is_toc_line(item_text):
            if is_heading:
                is_heading = False
                label = "text"
                log.info(
                    f"TOC LINE VETOED page={item_page} "
                    f"text='{item_text[:60]}' -> demoted, dot-leader + "
                    f"trailing page number"
                )
        elif is_heading and (_is_bare_page_number(item_text) or _is_bare_roman_page_number(item_text)):
            # Independent of has_own_numbering below, same reason as the
            # sentence-shape check right after this: a bare page number
            # ("45") has no trailing text for BARE_TOP_LEVEL_NUMBER_RE to
            # match, so has_own_numbering is False and the veto further
            # down never runs at all -- Docling's own classification
            # would otherwise be the only word on this. Also matters for
            # cross-page table continuation: an unvetoed page-number
            # "heading" sitting between two fragments of the same table
            # was being read by _breaks_table_continuation as real
            # intervening content, wrongly splitting the table in two.
            is_heading = False
            label = "text"
            log.info(
                f"PAGE-NUMBER VETO page={item_page} "
                f"text='{item_text[:60]}' -> demoted, bare page number, "
                f"not a heading"
            )
        elif is_heading and _looks_like_sentence_not_heading(item_text):
            # Independent of has_own_numbering below -- this is exactly
            # what catches a lowercase list marker like "c) the
            # composition of the strengths are quantitatively
            # proportional..." that no numbering regex recognizes at
            # all (deliberately -- see _lowercase_roman_prefix_end), and
            # which the has_own_numbering-gated veto right below would
            # therefore never even get a chance to look at.
            is_heading = False
            label = "text"
            log.info(
                f"SENTENCE-SHAPE VETO page={item_page} "
                f"text='{item_text[:60]}' -> demoted, reads as a full "
                f"sentence/clause, not a title"
            )
        elif is_heading:
            normalized_item_text = _normalize_for_dedup(item_text)
            if table_artifact_pool and normalized_item_text and normalized_item_text in table_artifact_pool:
                is_heading = False
                label = "text"
            elif not _is_strong_compound_numbering(numbering_scheme, numbering_level):
                # Docling sometimes classifies "bold numbering + long
                # regular-text sentence" as a genuine SectionHeaderItem
                # (see D2.8.2 above) -- it isn't a heading, just a
                # numbered clause whose leading digits happen to render
                # bold. Run the same bold-run check used to PROMOTE plain
                # text into a heading, here as a VETO on docling's own
                # classification: if the bold styling doesn't cover the
                # numbering/label as expected, this isn't a heading
                # either, regardless of what docling called it.
                #
                # Skipped for strong compound numbering ("IV.1.1") for
                # the same reason it bypasses the promotion check below
                # -- that pattern is specific enough evidence on its own
                # that a failed style check shouldn't override it.
                bbox = prov_entries[0].bbox if prov_entries else None
                page_obj = document.pages.get(item_page)
                page_height = page_obj.size.height if page_obj and page_obj.size else None
                if page_height and not _leading_bold_run_has_label(
                    pdf_doc, item_page, bbox, page_height,
                    require_label_text=bool(own_remainder),
                    baseline_font_size=baseline_font_size,
                ):
                    is_heading = False
                    label = "text"
                    log.info(
                        f"DOCLING HEADING VETOED page={item_page} "
                        f"text='{item_text[:60]}' -> demoted, bold run "
                        f"doesn't cover the numbering/label as expected"
                    )
        elif label in ("list_item", "paragraph", "text"):
            if has_own_numbering and not _looks_like_sentence_not_heading(item_text):
                if _is_strong_compound_numbering(numbering_scheme, numbering_level):
                    # Multi-segment compound numbering ("IV.1.1") is
                    # strong enough evidence on its own -- promote
                    # without requiring bold/italic confirmation. Some
                    # PDF generators give a deep heading tier no
                    # distinguishing style at all (styling isn't just
                    # inconsistently detected here -- Docling's own
                    # layout model saw nothing either), and gating
                    # promotion on a style signal that genuinely doesn't
                    # exist in the source silently drops the entire
                    # tier no matter how the bold/italic check itself is
                    # tuned.
                    is_heading = True
                else:
                    bbox = prov_entries[0].bbox if prov_entries else None
                    page_obj = document.pages.get(item_page)
                    page_height = page_obj.size.height if page_obj and page_obj.size else None
                    if page_height and _leading_bold_run_has_label(
                        pdf_doc, item_page, bbox, page_height,
                        require_label_text=bool(own_remainder),
                        baseline_font_size=baseline_font_size,
                    ):
                        is_heading = True
                    else:
                        log.info(
                            f"BOLD-RUN CHECK page={item_page} "
                            f"page_height={page_height} bbox={bbox} "
                            f"text='{item_text[:60]}' -> not promoted"
                        )

        if is_heading:
            # A roman root only counts as "open" if it hasn't already
            # been evicted from current_hierarchy_context by something
            # else taking its slot.
            roman_root_offset = (
                open_roman_root_level
                if open_roman_root_level is not None
                and open_roman_root_level in current_hierarchy_context
                else 0
            )
            # A KEYWORD-opened root ("Appendix III", "Annex D") is a
            # genuine container: bare numbering inside it (restarting
            # its own local "I.", "II.", ... or "A.", "B.", ...
            # sequence) is meant to nest one level under it. A
            # BARE-opened root (plain "I. Primary heading" with no
            # keyword) is NOT a container in the same sense -- a
            # following bare-roman heading ("II. Second primary
            # heading") is its SIBLING, not its child, which is why
            # bare-roman is excluded below unless the open root is
            # keyword-based.
            root_is_keyword = open_root_scheme in ("keyword-roman", "keyword-letter")
            # Tracks whether THIS heading was scored by nesting under an
            # existing root, as opposed to opening a root of its own.
            # Matters below: a bare-roman heading that got nested inside
            # a keyword container must NOT then overwrite open_root_scheme
            # with its own "bare-roman" -- doing so would make the
            # container's root vanish the moment its first internal "I."
            # is seen, kicking the very next sibling ("II.") back out to
            # top level instead of staying nested beside "I.".
            nested_under_root = False

            if numbering_level is not None and not (
                isinstance(item, SectionHeaderItem)
                and numbering_scheme == "letter-compound"
                and (lc_match := LETTER_NUMBER_COMPOUND_RE.match(item_text.strip()))
                and len(lc_match.group("letter")) >= 3
            ):
                # The "and not (...)" guard: LETTER_NUMBER_COMPOUND_RE's
                # letter group was widened from {1,2} to {1,4} chars to
                # catch multi-letter roman prefixes ("VIII.2 Something")
                # on headings Docling does NOT classify as headings at
                # all (e.g. "IV.1.1 General aspects" -- confirmed via
                # elementType "text" -- Docling's own layout model saw
                # no distinguishing style for that tier either, so this
                # is the ONLY signal available). But when the item IS
                # already a genuine SectionHeaderItem Docling classified
                # AND leveled correctly on its own ("III.1 Solubility"),
                # our simpler letter+number heuristic isn't more
                # trustworthy than Docling's own established level --
                # overriding it here silently evicted "III. Drug
                # Substance" from "III.1 Solubility"'s headingPath the
                # moment the regex widened to recognize "III" as a
                # letter group. Only 3+ character letter groups trigger
                # this guard; 1-2 character matches ("E.1 Scope") are
                # the original, already-validated behavior.
                if numbering_scheme is None:
                    # Standard legal-outline convention: Roman > Letter >
                    # Number (I. > A. > 1.). Bare-decimal under an open
                    # roman root nests one level DEEPER than bare-letter
                    # under the same root -- e.g. "1. Tertiary heading"
                    # sits under "B. Another secondary heading", which
                    # sits under "I. Primary heading". Confirmed
                    # structure, not a per-document guess.
                    level = (
                        numbering_level + roman_root_offset + 1
                        if roman_root_offset
                        else numbering_level
                    )
                elif numbering_scheme == "bare-letter" and roman_root_offset:
                    # Only a BARE letter ("A. Secondary heading", no
                    # keyword) is eligible for this offset. A
                    # keyword-prefixed letter heading ("Annex E",
                    # "Appendix F") is self-declaring its own position
                    # via the keyword itself and must never silently
                    # inherit an offset from an unrelated open Roman
                    # root -- whether that root is a genuine bare "I."
                    # or a same-keyword sibling that was itself
                    # misread as Roman (e.g. "Annex D", where D is also
                    # a valid Roman numeral character). Excluding
                    # keyword-letter here is what stops "Annex E" (and
                    # everything nested under it) from being folded
                    # into "Annex D"'s subtree.
                    level = roman_root_offset + 1
                    nested_under_root = True
                elif (
                    numbering_scheme == "bare-roman"
                    and roman_root_offset
                    and root_is_keyword
                ):
                    # A bare roman-numeral heading ("I. Introduction")
                    # immediately following a KEYWORD container
                    # ("APPENDIX III") is that container's own internal
                    # subsection sequence, restarting from I regardless
                    # of the appendix's own number -- a very common
                    # convention. Nest it one level under the container
                    # instead of letting it replace the container as a
                    # same-scheme "sibling" (which is right when the open
                    # root is itself a bare "I." -- see root_is_keyword
                    # above -- but wrong here).
                    level = roman_root_offset + 1
                    nested_under_root = True
                else:
                    level = numbering_level
            elif BARE_TOP_LEVEL_NUMBER_RE.match(item_text.strip()):
                # Same Roman > Letter > Number convention as above, for
                # the bare-top-level-number path (no dot, no separator
                # at all -- "1 Introduction", not "1. Introduction").
                # Applies whether this heading was already a
                # SectionHeaderItem from Docling, or was PROMOTED from
                # plain text/list_item via the numbering-fallback tier
                # above: has_own_numbering already recognizes this
                # pattern for promotion eligibility (BARE_TOP_LEVEL_
                # NUMBER_RE's trailing dot is optional), but this check
                # used to only be reachable via the isinstance branch
                # below -- a promoted bare-number heading isn't really a
                # SectionHeaderItem instance, so it fell through to a
                # hardcoded level=1 regardless of nesting context.
                level = (roman_root_offset + 2) if roman_root_offset else 1
            elif isinstance(item, SectionHeaderItem):
                level = item.level
            else:
                level = 1

            heading_label = derive_heading_label(item_text)
            current_hierarchy_context[level] = heading_label
            for k in [k for k in list(current_hierarchy_context) if k > level]:
                del current_hierarchy_context[k]

            if numbering_scheme in ("keyword-roman", "bare-roman", "keyword-letter") and not nested_under_root:
                open_roman_root_level = level
                open_root_scheme = numbering_scheme
            elif open_roman_root_level is not None and level <= open_roman_root_level:
                open_roman_root_level = None
                open_root_scheme = None

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

        is_table_continuation = False

        if label == "table":
            key = id(item)
            try:
                table_markdown = item.export_to_markdown(document)
            except Exception:
                table_markdown = None
            column_count = _table_column_count(table_markdown)

            # Docling gives a page-split table as TWO SEPARATE TableItem
            # objects (one per page fragment), each with its own
            # single-page prov entry -- there is no per-item signal at
            # all that links them, which is why continuedFromPreviousPage
            # /continuesOnNextPage below were always False and table_ids
            # always incremented. Detected here instead, from page
            # adjacency plus "nothing substantial intervened" (tracked
            # across every element in the loop, not just tables --
            # see _breaks_table_continuation below), corroborated by
            # column count where available. Column count is checked
            # rather than header content specifically because it holds
            # regardless of whether a given table repeats its header row
            # on the continuation page or not.
            is_table_continuation = (
                last_table_state is not None
                and item_page == last_table_state["last_page"] + 1
                and (
                    column_count is None
                    or last_table_state["column_count"] is None
                    or column_count == last_table_state["column_count"]
                )
            )

            if is_table_continuation:
                table_ids[key] = last_table_state["table_id"]
                # The previous table's last page-element was already
                # emitted (in an earlier loop iteration) with
                # continuesOnNextPage left at its per-item-provenance
                # default of False -- patch it now that the continuation
                # is known. Safe because page_element dicts are appended
                # by reference into elements_by_page, not copied.
                last_table_state["last_page_element"]["continuesOnNextPage"] = True
            elif key not in table_ids:
                # A dedicated counter, NOT len(table_ids): every
                # continuation fragment ALSO adds an entry to table_ids
                # (same value, reused id, but still a new dict key), so
                # len(table_ids) counts raw fragments processed so far,
                # not distinct logical tables assigned an id. A table
                # spanning 3 fragments would inflate the next genuinely
                # new table's id by 2 (e.g. table 1 -> id 4 instead of
                # id 2) if derived from len(table_ids) instead.
                table_ids[key] = next_table_id
                next_table_id += 1

            element["tableId"] = table_ids[key]
            element["content"] = table_markdown
            element["continuationOfPreviousTable"] = is_table_continuation

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
        emitted_page_elements = []
        for idx, prov in enumerate(prov_entries):
            page_element = dict(element)
            page_element["continuedFromPreviousPage"] = idx > 0
            page_element["continuesOnNextPage"] = idx < num_pages_spanned - 1

            local_order = page_order_counters.get(prov.page_no, 0)
            page_element["pageOrder"] = local_order
            page_order_counters[prov.page_no] = local_order + 1

            elements_by_page.setdefault(prov.page_no, []).append(page_element)
            emitted_page_elements.append(page_element)

        if label == "table":
            if is_table_continuation:
                # idx==0 by construction (a table fragment is always a
                # single-provenance item -- see above), so this is the
                # one and only page_element just emitted for it.
                emitted_page_elements[0]["continuedFromPreviousPage"] = True
            last_table_state = {
                "table_id": table_ids[key],
                "last_page": prov_entries[-1].page_no,
                "column_count": _table_column_count(element.get("content")),
                "last_page_element": emitted_page_elements[-1],
            }
        elif _breaks_table_continuation(label, is_heading, item_text):
            last_table_state = None

    pdf_doc.close()
    return elements_by_page