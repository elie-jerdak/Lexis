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
from app.postprocess import build_pages

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

        # ResultPostprocessor modifies result.document IN PLACE (heading
        # hierarchy from bookmarks, then numbering/font-style rules).
        # `source=temp_path` is required so it can re-read the PDF.
        _t0 = time.time()
        ResultPostprocessor(result, source=temp_path).process()
        log.info(f"[{file.filename}] Heading hierarchy postprocessed in "
                 f"{time.time() - _t0:.2f}s")

        # Hybrid heading correction, structured elements, per-page exports.
        pages = build_pages(result.document, temp_path, label=file.filename)

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