import time

from app.logging_config import log
from app.page_export import FAST_EXPORT, export_markdown_pages, export_text_pages
from app.structured_elements import build_structured_elements
from app.hybrid_heading_inference import HybridHeadingInferenceService


def build_pages(document, pdf_path: str, label: str = "doc"):
    """Everything that happens after converter.convert() + ResultPostprocessor."""
    _t0 = time.time()
    heading_result = HybridHeadingInferenceService().infer_and_correct(document, pdf_path)
    log.info(
        f"[{label}] Hybrid heading analysis completed in {time.time() - _t0:.2f}s "
        f"(existing={len(heading_result['existing'])}, "
        f"markdown={len(heading_result['markdown'])}, "
        f"matched={len(heading_result['matched'])}, "
        f"unmatched={len(heading_result['unmatched_markdown'])}, "
        f"corrected={heading_result['corrected_count']})"
    )

    page_numbers = sorted(document.pages.keys())
    log.info(f"[{label}] Document has {len(page_numbers)} page(s), building elements and exports...")

    _t0 = time.time()
    elements_by_page = build_structured_elements(document, pdf_path=pdf_path)
    log.info(f"[{label}] Structured elements built in {time.time() - _t0:.2f}s")

    _t0 = time.time()
    md_pages = export_markdown_pages(document)
    tx_pages = export_text_pages(document)
    log.info(f"[{label}] Per-page markdown/text exported in {time.time() - _t0:.2f}s "
             f"({'fast' if FAST_EXPORT else 'per-page'} mode)")

    return [
        {
            "pageNumber": p,
            "text": tx_pages[p],
            "markdown": md_pages[p],
            "elements": elements_by_page.get(p, []),
        }
        for p in page_numbers
    ]