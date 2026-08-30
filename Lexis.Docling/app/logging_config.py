import logging
import sys


def setup_logging():
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        stream=sys.stdout,
    )
    return logging.getLogger("docling-service")


# Shared logger — every module does `from app.logging_config import log`
log = setup_logging()