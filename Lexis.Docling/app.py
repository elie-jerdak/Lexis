import os

# --- Must be set BEFORE torch / docling are imported ---
# Fixes a common Windows/conda deadlock where two OpenMP runtimes
# (one from PyTorch, one from numpy/scipy) collide and hang silently
# right after model weights finish loading.
os.environ.setdefault("KMP_DUPLICATE_LIB_OK", "TRUE")
os.environ.setdefault("OMP_NUM_THREADS", "1")

# Hugging Face Hub has no download timeout by default, so a stalled
# connection (e.g. CDN host blocked/throttled by a firewall or proxy)
# hangs forever instead of failing. These force it to error out instead.
os.environ.setdefault("HF_HUB_ETAG_TIMEOUT", "15")
os.environ.setdefault("HF_HUB_DOWNLOAD_TIMEOUT", "30")

import sys
import time
import threading
import tempfile
import logging
import traceback

from fastapi import FastAPI, UploadFile, File
from fastapi.responses import JSONResponse

# --- Logging setup: prints go to stdout with timestamps ---
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    stream=sys.stdout,
)
log = logging.getLogger("docling-service")

log.info("Starting up: importing docling (this can take a while on first run)...")
_t0 = time.time()

from docling.document_converter import DocumentConverter, PdfFormatOption
from docling.datamodel.base_models import InputFormat
from docling.datamodel.pipeline_options import PdfPipelineOptions

log.info(f"docling imported in {time.time() - _t0:.2f}s")

app = FastAPI()

# --- Build pipeline options explicitly. Passing do_ocr=False directly to
# DocumentConverter() does NOT work — it's silently ignored. It must go
# through PdfPipelineOptions + format_options. ---
log.info("Building PdfPipelineOptions (OCR enabled, table structure enabled)...")
pipeline_options = PdfPipelineOptions()
pipeline_options.do_ocr = True
pipeline_options.do_table_structure = True
# TableFormerMode.ACCURATE gives better table results than the default
# FAST mode, at the cost of more compute time. Drop this line if you
# want to keep the faster default.
pipeline_options.table_structure_options.mode = pipeline_options.table_structure_options.mode.ACCURATE

log.info("Instantiating DocumentConverter (this loads OCR + table structure "
         "models, can take longer on first run while weights are read)...")
_t0 = time.time()
converter = DocumentConverter(
    format_options={
        InputFormat.PDF: PdfFormatOption(pipeline_options=pipeline_options)
    }
)
log.info(f"DocumentConverter ready in {time.time() - _t0:.2f}s")

# --- Pre-warm the pipeline NOW, at startup, instead of on the first
# request. Docling initializes/downloads its models lazily on the first
# convert() call, not at DocumentConverter() construction time. Doing
# it here means:
#   1. You see model-loading/download cost in startup logs, not hidden
#      inside your first API request.
#   2. If it's a network stall (e.g. Hugging Face CDN blocked/slow),
#      you'll see it fail here with a timeout instead of hanging.
_MIN_VALID_PDF = (
    b"%PDF-1.1\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n"
    b"2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n"
    b"3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\n"
    b"trailer<</Root 1 0 R>>"
)

def _warm_up():
    log.info("Warm-up: converting a throwaway 1-page PDF to force model "
              "loading/downloads now, at startup...")
    stop_heartbeat = threading.Event()

    def _heartbeat():
        start = time.time()
        while not stop_heartbeat.wait(15):
            log.info(f"Warm-up: still working... ({time.time() - start:.0f}s elapsed)")

    hb_thread = threading.Thread(target=_heartbeat, daemon=True)
    hb_thread.start()

    tmp_path = None
    try:
        with tempfile.NamedTemporaryFile(delete=False, suffix=".pdf") as tmp:
            tmp.write(_MIN_VALID_PDF)
            tmp_path = tmp.name
        _t0 = time.time()
        converter.convert(tmp_path)
        log.info(f"Warm-up: complete in {time.time() - _t0:.2f}s. "
                  f"Models are loaded and cached — real requests should now be fast.")
    except Exception:
        log.error("Warm-up FAILED. This usually means a model download stalled "
                   "or timed out (network/proxy/firewall issue reaching "
                   "huggingface.co or its CDN). See traceback below:")
        log.error(traceback.format_exc())
    finally:
        stop_heartbeat.set()
        if tmp_path and os.path.exists(tmp_path):
            os.remove(tmp_path)

_warm_up()


def build_structured_elements(document):
    """
    Walk docling's structured document tree (not just the flattened
    markdown/text export) and group elements by page, preserving type
    info and cross-page continuation for tables/figures.

    Docling gives every item (TextItem, SectionHeaderItem, TableItem,
    PictureItem, ListItem, ...) a `.prov` list of ProvenanceItem entries
    — one per page the item actually appears on. When a table or figure
    spans a page break, that SAME item has multiple prov entries (one
    per page), which is what lets us flag continuation directly instead
    of guessing from position/text.
    """
    elements_by_page = {}
    table_ids = {}   # id(item) -> sequential table number
    figure_ids = {}  # id(item) -> sequential figure number
    order_counter = 0
    page_order_counters = {}  # page_no -> next local order index on that page

    for item, _level in document.iterate_items():
        prov_entries = getattr(item, "prov", None)
        if not prov_entries:
            continue  # some structural items (groups) carry no page provenance

        label = str(getattr(item, "label", item.__class__.__name__))

        element = {"elementType": label, "order": order_counter}
        order_counter += 1

        if label == "table":
            key = id(item)
            if key not in table_ids:
                table_ids[key] = len(table_ids) + 1
            element["tableId"] = table_ids[key]
            try:
                element["content"] = item.export_to_markdown(document)
            except Exception:
                element["content"] = None

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
            element["text"] = getattr(item, "text", None)

        num_pages_spanned = len(prov_entries)
        for idx, prov in enumerate(prov_entries):
            page_element = dict(element)
            page_element["continuedFromPreviousPage"] = idx > 0
            page_element["continuesOnNextPage"] = idx < num_pages_spanned - 1

            local_order = page_order_counters.get(prov.page_no, 0)
            page_element["pageOrder"] = local_order
            page_order_counters[prov.page_no] = local_order + 1

            elements_by_page.setdefault(prov.page_no, []).append(page_element)

    return elements_by_page



    log.info("Health check hit")
    return {"status": "Docling service running"}


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

        document = result.document

        page_numbers = sorted(document.pages.keys())
        log.info(f"[{file.filename}] Document has {len(page_numbers)} page(s), "
                  f"exporting per-page markdown/text/elements...")

        _t0 = time.time()
        elements_by_page = build_structured_elements(document)
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