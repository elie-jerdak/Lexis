"""
Environment configuration that MUST be set before torch / docling are
imported anywhere in the process.

Import this module FIRST, at the very top of main.py, before any other
project module (converter.py, heading_inference.py, etc.) — those import
docling/torch transitively, and by the time Python gets to them it's too
late to set these vars.
"""
import os


def configure_environment():
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

    # This means PyTorch 2.x is trying to use TorchInductor (torch.compile),
    # which on Windows wants the Microsoft C++ compiler (cl.exe).
    # Docling itself is fine.
    os.environ["TORCH_COMPILE_DISABLE"] = "1"
    os.environ["TORCHDYNAMO_DISABLE"] = "1"


# Run on import so `from app import config` at the top of main.py is
# enough — no extra call needed.
configure_environment()