import re

PDF_BOLD_FLAG = 1 << 4  # PyMuPDF span flags bit for bold


def normalize_text(text):
    """Collapse whitespace and casefold so headings extracted by PyMuPDF
    can be matched against Docling's own text items, which frequently
    differ in whitespace/hyphenation even for the exact same content."""
    if not text:
        return ""
    return " ".join(text.split()).strip().casefold()


def extract_clause_number(text):
    match = re.match(r'^(\d+(?:\.\d+)*)', text)

    if not match:
        return None

    return match.group(1)