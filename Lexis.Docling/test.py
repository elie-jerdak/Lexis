import fitz  # pip install pymupdf --break-system-packages, if not already available

def dump_heading_candidates(pdf_path: str, search_snippets: list[str], page_range: tuple[int, int] | None = None):
    """
    For each page (or page_range if given), find text blocks that start
    with any of the given snippets and print their font/position info.

    search_snippets: list of short strings to match against the start
    of a block's text, e.g. ["1.3.3.11", "1.1. Description", "1.4 Abbreviations"]
    """
    pdf = fitz.open(pdf_path)

    pages = (
        range(*page_range) if page_range
        else range(len(pdf))
    )

    for page_index in pages:
        page = pdf[page_index]
        blocks = page.get_text("dict").get("blocks", [])

        for block in blocks:
            if block.get("type") != 0:
                continue

            lines = block.get("lines", [])
            if not lines:
                continue

            block_text = " ".join(
                span.get("text", "")
                for line in lines
                for span in line.get("spans", [])
            ).strip()

            if not any(block_text.startswith(s) for s in search_snippets):
                continue

            bbox = block.get("bbox")
            print(f"\n=== page {page_index + 1} | bbox_x0={bbox[0]:.1f} bbox_y0={bbox[1]:.1f} ===")
            print(f"text: {block_text[:100]!r}")

            for line in lines:
                for span in line.get("spans", []):
                    print(
                        f"    size={span['size']:.1f}  "
                        f"font={span['font']!r}  "
                        f"text={span['text'][:40]!r}"
                    )

    pdf.close()


# Usage — run once per document, listing the specific items you want compared:

dump_heading_candidates(
    "D:\\RAG\\80.pdf",
    search_snippets=["1.3.3.11", "1.3.3.4", "1.3.4", "2.2", "2.3"],
)

dump_heading_candidates(
    "D:\\RAG\\58.pdf",
    search_snippets=["1.1. Description", "1.2 Impact", "1.4 Abbreviations", "1.5 Terms"],
)