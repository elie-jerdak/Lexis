"""
Docling DocumentConverter setup and warm-up.

Importing this module triggers the (slow) docling import and pipeline
construction, so it must only be imported AFTER app.config has already
run (see the import order note at the top of app.config).
"""
import os
import tempfile
import threading
import time
import traceback

from app.logging_config import log

log.info("Starting up: importing docling (this can take a while on first run)...")
_t0 = time.time()

from docling.document_converter import DocumentConverter, PdfFormatOption
from docling.datamodel.base_models import InputFormat
from docling.datamodel.pipeline_options import PdfPipelineOptions

log.info(f"docling imported in {time.time() - _t0:.2f}s")

# --- Build pipeline options explicitly. Passing do_ocr=False directly to
# DocumentConverter() does NOT work — it's silently ignored. It must go
# through PdfPipelineOptions + format_options. --- 
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

_MIN_VALID_PDF = (
    b"%PDF-1.1\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n"
    b"2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n"
    b"3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\n"
    b"trailer<</Root 1 0 R>>"
)


def warm_up():
    """
    Pre-warm the pipeline NOW, at startup, instead of on the first
    request. Docling initializes/downloads its models lazily on the first
    convert() call, not at DocumentConverter() construction time. Doing
    it here means:
      1. You see model-loading/download cost in startup logs, not hidden
         inside your first API request.
      2. If it's a network stall (e.g. Hugging Face CDN blocked/slow),
         you'll see it fail here with a timeout instead of hanging.
    """
    log.info("Warm-up: converting a throwaway 1-page PDF to force model "
              "loading/downloads now, at startup...")
    stop_heartbeat = threading.Event()

    def _heartbeat():
        start = time.time()
        while not stop_heartbeat.wait(30):
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