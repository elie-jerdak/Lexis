# IMPORTANT — import order:
# app.config must be imported FIRST, before anything else, because it sets
# env vars (via os.environ.setdefault) that must be in place before torch
# or docling get imported anywhere. app.converter imports docling, so it —
# and everything that imports it — must come after this line.
from app import config  # noqa: F401  (imported for its side effects)

import os
import tempfile
import time
import traceback

from fastapi import FastAPI, UploadFile, File
from fastapi.responses import JSONResponse
from hierarchical.postprocessor import ResultPostprocessor

from app.logging_config import log
from app.converter import converter, warm_up
from app.structured_elements import build_structured_elements
from app.hybrid_heading_inference import HybridHeadingInferenceService

app = FastAPI()

warm_up()


@app.post("/extract")
def extract(file: UploadFile = File(...)):
    # NOTE: sync `def`, not `async def`. FastAPI runs sync routes in a
    # threadpool automatically, so the blocking converter.convert() call
    # won't freeze the whole server's event loop while it runs.

    log.info(f"[{file.filename}] Request received")

    suffix = os.path.splitext(file.filename)[1]
    temp_path = None

    try:
        _t0 = time.time()
        with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as temp:
            content = file.file.read()
            temp.write(content)
            temp_path = temp.name
        log.info(f"[{file.filename}] Saved to temp file {temp_path} "
                  f"({len(content)} bytes) in {time.time() - _t0:.2f}s")

        log.info(f"[{file.filename}] Starting converter.convert() ...")
        _t0 = time.time()
        result = converter.convert(temp_path)
        log.info(f"[{file.filename}] converter.convert() finished in "
                  f"{time.time() - _t0:.2f}s")

        # ResultPostprocessor modifies result.document IN PLACE: it infers
        # the heading hierarchy (from PDF bookmarks first, falling back to
        # numbering/font-style rules) and corrects SectionHeaderItem.level
        # + reparents items under their governing heading directly on
        # docling's own document tree. `source=temp_path` is required here
        # -- per the library's docs, converter.convert(str_path) doesn't
        # leave result.document holding a usable reference back to the
        # source file, so the postprocessor needs it passed explicitly to
        # re-read the PDF (for font-style signals and bookmark/TOC
        # extraction via pymupdf).
        _t0 = time.time()
        ResultPostprocessor(result, source=temp_path).process()
        log.info(f"[{file.filename}] Heading hierarchy postprocessed in "
                  f"{time.time() - _t0:.2f}s")

        _t0 = time.time()

        heading_service = HybridHeadingInferenceService()

        heading_result = heading_service.infer_and_correct(result.document, temp_path)

        log.info(
            f"[{file.filename}] Hybrid heading analysis completed "
            f"in {time.time() - _t0:.2f}s "
            f"(existing={len(heading_result['existing'])}, "
            f"markdown={len(heading_result['markdown'])}, "
            f"matched={len(heading_result['matched'])}, "
            f"unmatched={len(heading_result['unmatched_markdown'])}, "
            f"corrected={heading_result['corrected_count']})"
        )   
             
        document = result.document

        page_numbers = sorted(document.pages.keys())
        log.info(f"[{file.filename}] Document has {len(page_numbers)} page(s), "
                  f"exporting per-page markdown/text/elements...")

        _t0 = time.time()
        elements_by_page = build_structured_elements(document, pdf_path=temp_path)
        log.info(f"[{file.filename}] Structured elements built in "
                  f"{time.time() - _t0:.2f}s")

        pages = []
        for page_no in page_numbers:
            _t0 = time.time()
            page_markdown = document.export_to_markdown(page_no=page_no)
            page_text = document.export_to_text(page_no=page_no)
            page_elements = elements_by_page.get(page_no, [])
            log.info(f"[{file.filename}] Page {page_no} exported in "
                      f"{time.time() - _t0:.2f}s "
                      f"(markdown: {len(page_markdown)} chars, text: {len(page_text)} chars, "
                      f"elements: {len(page_elements)})")
            pages.append({
                "pageNumber": page_no,
                "text": page_text,
                "markdown": page_markdown,
                "elements": page_elements,
            })

        log.info(f"[{file.filename}] Done, returning response")
        return {
            "pages": pages,
            "metadata": {"filename": file.filename},
        }

    except Exception as e:
        log.error(f"[{file.filename}] FAILED: {e}")
        log.error(traceback.format_exc())
        return JSONResponse(
            status_code=500,
            content={"error": str(e), "filename": file.filename},
        )

    finally:
        if temp_path and os.path.exists(temp_path):
            os.remove(temp_path)
            log.info(f"[{file.filename}] Cleaned up temp file")


if __name__ == "__main__":
    import uvicorn
    log.info("Launching uvicorn...")
    uvicorn.run(app, host="0.0.0.0", port=8000, log_level="info")
