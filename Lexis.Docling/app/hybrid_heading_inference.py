import re
from dataclasses import dataclass
from typing import Any

from docling_core.types.doc.document import SectionHeaderItem
from docling_core.types.doc import BoundingBox, CoordOrigin, ProvenanceItem
from docling_core.types.doc.document import TableItem

from app.logging_config import log
#from app.page_export import export_markdown_pages, FAST_EXPORT
from app import page_export

@dataclass
class HeadingCandidate:
    text: str
    page_no: int | None
    item: Any | None
    source: str

    # Final / Docling level
    level: int | None = None

    # Semantic depth from numbering
    numbering_depth: int | None = None

    # Markdown ## / ### / #### depth
    markdown_level: int | None = None

    # Inline heading evidence
    is_inline_candidate: bool = False
    font_size: float | None = None
    is_bold: bool | None = None

    bbox: tuple[float, float, float, float] | None = None  # (x0, y0, x1, y1), top-left origin (fitz)
    
class HybridHeadingInferenceService:

    MARKDOWN_HEADING_RE = re.compile(
        r"^\s*(#{1,6})\s+(.+?)\s*$"
    )

    NUMBERED_HEADING_RE = re.compile(
        r"^\s*"
        r"(?P<number>\d+(?:\.\d+)+)\.?"
        r"\s+"
        r"(?P<title>.+?)"
        r"\s*$"
    )

    # ------------------------------------------------------------------
    # 1. Existing Docling headings
    # ------------------------------------------------------------------

    def find_existing_headings(self, document):
        """
        Find headings already recognized by Docling /
        docling-hierarchical-pdf.
        """

        headings = []

        for item, _ in document.iterate_items():

            if not isinstance(item, SectionHeaderItem):
                continue

            prov = getattr(item, "prov", None)

            if not prov:
                continue

            page_no = prov[0].page_no

            candidate = HeadingCandidate(
                text=item.text,
                page_no=page_no,
                item=item,
                source="docling-hierarchical-pdf",
                level=item.level,
                numbering_depth=self.numbering_depth(item.text),
            )

            headings.append(candidate)
  
        return headings

    # ------------------------------------------------------------------
    # 2. Markdown headings
    # ------------------------------------------------------------------

    def find_markdown_headings(self, document):
        """
        Find headings from Docling's exported Markdown.

        Markdown heading depth is treated as a structural signal,
        not automatically as the final semantic level.
        """

        headings = []

        #for page_no, markdown in export_markdown_pages(document, fast=FAST_EXPORT).items():
        for page_no, markdown in page_export.export_markdown_pages(document, fast=page_export.FAST_EXPORT).items():
            for line in markdown.splitlines():
                match = self.MARKDOWN_HEADING_RE.match(line)

                if not match:
                    continue

                hashes = match.group(1)
                text = match.group(2).strip()

                level = len(hashes)

                candidate = HeadingCandidate(
                    text=text,
                    page_no=page_no,
                    item=None,
                    source="markdown",
                    markdown_level=level,
                    numbering_depth=self.numbering_depth(text),
                )

                headings.append(candidate)

        return headings

    # ------------------------------------------------------------------
    # 3. Text normalization
    # ------------------------------------------------------------------

    @staticmethod
    def normalize_heading_text(text: str) -> str:
        """
        Normalize heading text for matching between Docling and Markdown.
        """

        if not text:
            return ""

        text = text.strip()

        # Normalize whitespace.
        text = re.sub(r"\s+", " ", text)

        # Remove trailing Markdown punctuation.
        text = text.rstrip("#").strip()

        return text.casefold()

    # ------------------------------------------------------------------
    # 4. Numbering depth
    # ------------------------------------------------------------------

    @staticmethod
    def numbering_depth(text: str) -> int | None:
        """
        Return semantic numbering depth.

        Examples:

            1.2             -> 2
            1.2.6           -> 3
            1.2.6.1         -> 4
            3.1             -> 2
            3.1.7.1         -> 4
        """

        if not text:
            return None

        match = HybridHeadingInferenceService.NUMBERED_HEADING_RE.match(
            text.strip()
        )

        if not match:
            return None

        number = match.group("number")

        return len(number.split("."))

    # ------------------------------------------------------------------
    # 5. Numbered heading candidate
    # ------------------------------------------------------------------

    @staticmethod
    def is_numbered_heading_candidate(text: str) -> bool:
        return (
            HybridHeadingInferenceService.numbering_depth(text)
            is not None
        )

    # ------------------------------------------------------------------
    # 6. Match Markdown headings against Docling headings
    # ------------------------------------------------------------------

    def compare_heading_sources(
        self,
        existing_headings,
        markdown_headings,
    ):
        """
        Match Markdown headings to Docling SectionHeaderItems and
        determine the corrected heading level.

        Markdown provides structural hierarchy.
        Numbering provides semantic hierarchy.
        Docling provides the actual document item that we mutate.

        The SectionHeaderItem itself is updated in place.
        """

        matched = []
        unmatched_markdown = []
        corrected_count = 0

        existing_by_page = {}

        for heading in existing_headings:
            existing_by_page.setdefault(
                heading.page_no,
                []
            ).append(heading)

        # ---------------------------------------------------------
        # Match Markdown headings to existing Docling headings
        # ---------------------------------------------------------

        for markdown_heading in markdown_headings:

            page_headings = existing_by_page.get(
                markdown_heading.page_no,
                []
            )

            normalized_markdown = self.normalize_heading_text(
                markdown_heading.text
            )

            match = None

            for existing in page_headings:

                normalized_existing = self.normalize_heading_text(
                    existing.text
                )

                if normalized_existing == normalized_markdown:
                    match = existing
                    break

            # -----------------------------------------------------
            # No Docling heading matched this Markdown heading
            # -----------------------------------------------------

            if match is None:

                unmatched_markdown.append(
                    markdown_heading
                )

                log.warning(
                    f"HYBRID UNMATCHED MARKDOWN "
                    f"page={markdown_heading.page_no} "
                    f"markdownLevel={markdown_heading.markdown_level} "
                    f"numberingDepth={markdown_heading.numbering_depth} "
                    f"text='{markdown_heading.text}'"
                )

                continue

            # -----------------------------------------------------
            # Match successful
            # -----------------------------------------------------

            markdown_heading.item = match.item
            markdown_heading.level = match.level
            markdown_heading.source = (
                "docling-hierarchical-pdf+markdown"
            )

            matched.append(markdown_heading)

            # -----------------------------------------------------
            # Determine corrected level
            # -----------------------------------------------------

            old_level = match.item.level

            corrected_level = self.determine_corrected_level(
                match,
                markdown_heading,
            )

            # -----------------------------------------------------
            # Apply correction directly to Docling item
            # -----------------------------------------------------

            if old_level != corrected_level:

                log.warning(
                    f"HYBRID LEVEL CORRECTION "
                    f"page={match.page_no} "
                    f"oldLevel={old_level} "
                    f"newLevel={corrected_level} "
                    f"markdownLevel={markdown_heading.markdown_level} "
                    f"numberingDepth={match.numbering_depth} "
                    f"text='{match.text}'"
                )

                match.item.level = corrected_level

                # Keep candidate synchronized.
                match.level = corrected_level

                corrected_count += 1
 
 
        return matched, unmatched_markdown, corrected_count
    
    # ------------------------------------------------------------------
    # 7. Infer Markdown offset
    # ------------------------------------------------------------------

    def infer_markdown_offset(self, matched_headings):
        """
        Infer the relationship between Markdown levels and semantic
        numbering depth.

        Example:

            Markdown ###  -> numbering depth 3

        gives:

            offset = 0

        If:

            Markdown ##   -> numbering depth 3

        gives:

            offset = +1

        We use only headings where both Markdown level and numbering
        depth are available.
        """

        offsets = []

        for heading in matched_headings:

            if heading.markdown_level is None:
                continue

            if heading.numbering_depth is None:
                continue

            offset = (
                heading.numbering_depth
                - heading.markdown_level
            )

            offsets.append(offset)
 

        if not offsets:
            log.warning(
                "HYBRID: could not infer Markdown offset; "
                "no matched headings had both Markdown level "
                "and numbering depth"
            )

            return 0

        # Count frequency of each offset.
        frequencies = {}

        for offset in offsets:
            frequencies[offset] = (
                frequencies.get(offset, 0) + 1
            )

        # Most common offset wins.
        best_offset = max(
            frequencies,
            key=frequencies.get
        )
 

        return best_offset

    # ------------------------------------------------------------------
    # 8. Infer final heading levels
    # ------------------------------------------------------------------

    def infer_and_apply_levels(
        self,
        existing_headings,
        markdown_headings,
        matched_headings,
        unmatched_headings,
    ):
        """
        Infer final heading levels and apply them directly to the
        original Docling SectionHeaderItem objects.

        Current priority:

            1. Markdown structural level
            2. Numbering depth as validation
            3. Existing Docling level as fallback

        Unmatched Markdown headings are NOT inserted into Docling yet.
        That is a separate problem we will handle after level inference
        is stable.
        """

        if not matched_headings:
            log.warning(
                "HYBRID: no matched Markdown headings; "
                "leaving Docling heading levels unchanged"
            )
            return

        markdown_offset = self.infer_markdown_offset(
            matched_headings
        )

        corrected = 0
        unchanged = 0

        for heading in matched_headings:

            old_level = heading.item.level

            markdown_level = heading.markdown_level
            numbering_depth = heading.numbering_depth

            final_level = None
            source = None

            # ----------------------------------------------------------
            # Primary signal: Markdown
            # ----------------------------------------------------------

            if markdown_level is not None:

                final_level = (
                    markdown_level
                    + markdown_offset
                )

                source = "markdown"

            # ----------------------------------------------------------
            # Fallback: numbering
            # ----------------------------------------------------------

            elif numbering_depth is not None:

                final_level = numbering_depth
                source = "numbering"

            # ----------------------------------------------------------
            # Fallback: existing Docling level
            # ----------------------------------------------------------

            else:

                final_level = old_level
                source = "docling"

            # Keep levels within Docling's normal positive range.
            final_level = max(1, min(final_level, 6))

            # ----------------------------------------------------------
            # Numbering validation
            # ----------------------------------------------------------

            if (
                numbering_depth is not None
                and markdown_level is not None
            ):
                expected_from_numbering = (
                    numbering_depth
                    - markdown_offset
                )
 

            # ----------------------------------------------------------
            # Apply
            # ----------------------------------------------------------

            heading.item.level = final_level
            heading.level = final_level

            if old_level != final_level:

                corrected += 1

                log.warning(
                    "HYBRID LEVEL CORRECTION "
                    f"page={heading.page_no} "
                    f"oldLevel={old_level} "
                    f"newLevel={final_level} "
                    f"markdownLevel={markdown_level} "
                    f"numberingDepth={numbering_depth} "
                    f"source={source} "
                    f"text='{heading.text}'"
                )

            else: 
                unchanged += 1
 
    def determine_corrected_level(
        self,
        existing: HeadingCandidate,
        markdown: HeadingCandidate | None,
    ) -> int:
        """
        Determine the best semantic heading level.

        Priority:

        1. Numbering depth when the heading has explicit hierarchical numbering.
        2. Markdown heading level when available.
        3. Existing Docling level as fallback.

        Docling levels are normalized to the same 1-based hierarchy
        used by our application.
        """

        # ---------------------------------------------------------
        # 1. Explicit numbering is the strongest signal
        # ---------------------------------------------------------

        if existing.numbering_depth is not None:
            return existing.numbering_depth

        # ---------------------------------------------------------
        # 2. Markdown hierarchy is the next strongest signal
        # ---------------------------------------------------------

        if markdown is not None and markdown.markdown_level is not None:
            return markdown.markdown_level

        # ---------------------------------------------------------
        # 3. Fall back to whatever Docling gave us
        # ---------------------------------------------------------

        if existing.level is not None:
            return existing.level

        return 1
    
    def infer_and_correct(self, document, pdf_path: str,): 

        existing_headings = self.find_existing_headings(document)

        markdown_headings = self.find_markdown_headings(document)

        inline_headings = self.extract_inline_heading_candidates(pdf_path, document=document)

        (
            matched,
            unmatched_markdown,
            corrected_count,
        ) = self.compare_heading_sources(
            existing_headings,
            markdown_headings,
        )

        (
            matched_inline,
            unmatched_inline,
        ) = self.match_inline_headings(
            existing_headings,
            inline_headings,
        )
        
        inserted_inline = self.infer_and_insert_inline_headings(
            document,
            unmatched_inline,
            existing_headings,
        )
        return {
            "existing": existing_headings,
            "markdown": markdown_headings,
            "inline": inline_headings,
            "matched": matched,
            "matched_inline": matched_inline,
            "inserted_inline": inserted_inline,
            "unmatched_markdown": unmatched_markdown,
            "unmatched_inline": unmatched_inline,
            "corrected_count": corrected_count,
        }
    
    @staticmethod
    def is_inline_heading_candidate(
        text: str,
        *,
        font_size: float | None = None,
        body_font_size: float | None = None,
        is_bold: bool | None = None,
    ) -> bool:
        """
        Determine whether a text block is likely to be an inline /
        unnumbered heading.

        This is intentionally conservative.

        Numbered headings are handled separately and should never
        depend on this heuristic.
        """

        if not text:
            return False

        text = text.strip()

        # ---------------------------------------------------------
        # Basic rejection rules
        # ---------------------------------------------------------

        if len(text) < 2:
            return False

        # Avoid treating long paragraphs as headings.
        if len(text) > 150:
            return False

        # A heading should generally be a single logical line.
        if "\n" in text:
            return False

        # Avoid obvious sentence-like text.
        if text.endswith((".", ",", ";", ":", "?")):
            return False

        # Avoid list-like content.
        if re.match(
            r"^(?:[-•*]|\(?\d+[.)]|\(?[a-zA-Z][.)])\s+",
            text
        ):
            return False

        # ---------------------------------------------------------
        # Font evidence
        # ---------------------------------------------------------

        font_signal = False

        if (
            font_size is not None
            and body_font_size is not None
            and body_font_size > 0
        ):
            # Require a meaningful size increase.
            if font_size >= body_font_size * 1.08:
                font_signal = True

        # Bold is another strong signal.
        if is_bold:
            font_signal = True

        # Without any visual evidence, do not classify it as inline.
        if not font_signal:
            return False

        return True

    def extract_inline_heading_candidates(self, pdf_path: str, document=None,):
        """
        Detect unnumbered headings directly from PDF text blocks.

        This is an additional signal and does not replace Docling,
        Markdown, or numbered-heading detection.

        When `document` is provided, candidates whose bounding box falls
        inside a Docling-recognized table on the same page are excluded --
        see _collect_table_bboxes for why.
        """

        import fitz

        candidates = []

        table_bboxes_by_page = (
            self._collect_table_bboxes(document) if document is not None else {}
        )

        pdf = fitz.open(pdf_path)

        for page_index, page in enumerate(pdf):

            page_no = page_index + 1
            page_table_bboxes = table_bboxes_by_page.get(page_no, [])

            blocks = page.get_text("dict").get("blocks", [])

            font_sizes = []

            for block in blocks:
                if block.get("type") != 0:
                    continue
                for line in block.get("lines", []):
                    for span in line.get("spans", []):
                        size = span.get("size")
                        if size:
                            font_sizes.append(size)

            if not font_sizes:
                continue

            body_font_size = self.most_common_font_size(font_sizes)

            for block in blocks:

                if block.get("type") != 0:
                    continue

                lines = block.get("lines", [])

                if not lines:
                    continue

                block_bbox = block.get("bbox")

                block_text_parts = []
                block_font_sizes = []
                block_is_bold = False

                for line in lines:
                    for span in line.get("spans", []):
                        text = span.get("text", "")
                        if not text.strip():
                            continue
                        block_text_parts.append(text)
                        if span.get("size") is not None:
                            block_font_sizes.append(span["size"])
                        font_name = span.get("font", "").lower()
                        if "bold" in font_name or "black" in font_name:
                            block_is_bold = True

                text = " ".join(block_text_parts).strip()

                if not text:
                    continue

                if self.is_numbered_heading_candidate(text):
                    continue

                avg_font_size = (
                    sum(block_font_sizes) / len(block_font_sizes)
                    if block_font_sizes
                    else None
                )

                if not self.is_inline_heading_candidate(
                    text,
                    font_size=avg_font_size,
                    body_font_size=body_font_size,
                    is_bold=block_is_bold,
                ):
                    continue

                if block_bbox and self._bbox_inside_any(block_bbox, page_table_bboxes):
                    continue

                candidate = HeadingCandidate(
                    text=text,
                    page_no=page_no,
                    item=None,
                    source="inline-pdf",
                    numbering_depth=None,
                    is_inline_candidate=True,
                    font_size=avg_font_size,
                    is_bold=block_is_bold,
                    bbox=tuple(block_bbox) if block_bbox else None,
                )

                candidates.append(candidate)

        pdf.close()

        return candidates

    @staticmethod
    def most_common_font_size(
        font_sizes: list[float],
    ) -> float | None:

        if not font_sizes:
            return None

        frequencies = {}

        for size in font_sizes:

            # Bucket tiny PDF rendering differences.
            normalized = round(size, 1)

            frequencies[normalized] = (
                frequencies.get(normalized, 0) + 1
            )

        return max(
            frequencies,
            key=frequencies.get,
        )
        
    def match_inline_headings(
        self,
        existing_headings,
        inline_headings,
    ):
        """
        Match PDF inline heading candidates against headings
        already recognized by Docling.

        This does NOT create new Docling headings.
        It only attaches inline evidence to existing
        SectionHeaderItems.
        """

        matched = []
        unmatched = []

        existing_by_page = {}

        for heading in existing_headings:
            existing_by_page.setdefault(
                heading.page_no,
                []
            ).append(heading)

        for inline_heading in inline_headings:

            page_headings = existing_by_page.get(
                inline_heading.page_no,
                []
            )

            normalized_inline = (
                self.normalize_heading_text(
                    inline_heading.text
                )
            )

            match = None

            for existing in page_headings:

                normalized_existing = (
                    self.normalize_heading_text(
                        existing.text
                    )
                )

                if normalized_existing == normalized_inline:
                    match = existing
                    break

            if match is None:

                unmatched.append(
                    inline_heading
                )

                log.warning(
                    "HYBRID INLINE UNMATCHED "
                    f"page={inline_heading.page_no} "
                    f"fontSize={inline_heading.font_size} "
                    f"bold={inline_heading.is_bold} "
                    f"text='{inline_heading.text}'"
                )

                continue

            # -----------------------------------------------------
            # Attach inline evidence to the existing heading
            # -----------------------------------------------------

            inline_heading.item = match.item
            inline_heading.level = match.level
            inline_heading.source = (
                "docling-hierarchical-pdf+inline"
            )

            matched.append(
                inline_heading
            )  
            
        return matched, unmatched
    
    def _heading_top_y(self, item, document) -> float:
        """
        Top y-coordinate of a Docling heading, normalized to top-left
        origin, so it's directly comparable to fitz block bboxes.
        """
        prov = item.prov[0]
        bbox = prov.bbox
        page = document.pages.get(prov.page_no)
        page_height = page.size.height if page and page.size else None

        if bbox.coord_origin == CoordOrigin.BOTTOMLEFT and page_height:
            bbox = bbox.to_top_left_origin(page_height)  # VERIFY method name on your version

        return bbox.t

    def build_heading_anchors(self, existing_headings, document):
        """
        Ordered (page_no, top_y, level, item) anchors from headings that
        already have a confirmed numbered/markdown level. Read-only —
        never mutates numbered/markdown heading state.
        """
        anchors = []

        for heading in existing_headings:
            if heading.item is None or heading.level is None:
                continue
            top_y = self._heading_top_y(heading.item, document)
            anchors.append((heading.page_no, top_y, heading.level, heading.item))

        anchors.sort(key=lambda a: (a[0], a[1]))
        return anchors

    def nearest_preceding_anchor(self, anchors, page_no, top_y):
        preceding = None
        for anchor_page, anchor_y, anchor_level, anchor_item in anchors:
            if (anchor_page, anchor_y) >= (page_no, top_y):
                break
            preceding = (anchor_page, anchor_y, anchor_level, anchor_item)
        return preceding
    
    def _collect_table_bboxes(self, document) -> dict[int, list[tuple[float, float, float, float]]]:
        """
        Page -> list of (x0, top, x1, bottom) bounding boxes, top-left
        origin, for every TableItem Docling recognized in the document.
        Used to exclude inline heading candidates that actually live
        inside a table's own content -- see extract_inline_heading_candidates.
        A bold, short, run-in label organizing prose WITHIN a table cell
        (e.g. "Thickness" / "Weight" as sub-topic markers inside a value
        column) can satisfy every font/length/punctuation heuristic this
        class uses just as well as a genuine document heading does --
        geometry like font size or line spacing doesn't reliably tell them
        apart. What does is structural: it's inside a table, which is
        never part of the document's own heading hierarchy, regardless of
        how the table happens to style its cell content.
        """
        bboxes_by_page: dict[int, list[tuple[float, float, float, float]]] = {}

        for item, _ in document.iterate_items():
            if not isinstance(item, TableItem):
                continue

            for prov in getattr(item, "prov", None) or []:
                bbox = prov.bbox
                page = document.pages.get(prov.page_no)
                page_height = page.size.height if page and page.size else None

                if bbox.coord_origin == CoordOrigin.BOTTOMLEFT and page_height:
                    bbox = bbox.to_top_left_origin(page_height)

                bboxes_by_page.setdefault(prov.page_no, []).append(
                    (bbox.l, bbox.t, bbox.r, bbox.b)
                )

        return bboxes_by_page

    @staticmethod
    def _bbox_inside_any(
        candidate_bbox: tuple[float, float, float, float],
        container_bboxes: list[tuple[float, float, float, float]],
        *,
        min_overlap_ratio: float = 0.6,
    ) -> bool:
        """
        True if candidate_bbox overlaps any container bbox by at least
        min_overlap_ratio of its own area. A ratio, not exact containment,
        tolerates minor coordinate differences between Docling's table
        bbox and PyMuPDF's own block bbox for the same visual content.
        """
        cx0, cy0, cx1, cy1 = candidate_bbox
        candidate_area = max(0.0, cx1 - cx0) * max(0.0, cy1 - cy0)

        if candidate_area <= 0:
            return False

        for bx0, by0, bx1, by1 in container_bboxes:
            ix0, iy0 = max(cx0, bx0), max(cy0, by0)
            ix1, iy1 = min(cx1, bx1), min(cy1, by1)
            overlap_area = max(0.0, ix1 - ix0) * max(0.0, iy1 - iy0)

            if overlap_area / candidate_area >= min_overlap_ratio:
                return True

        return False

    def infer_and_insert_inline_headings(self, document, unmatched_inline, existing_headings,):
        """
        Inline headings Docling never recognized get level =
        nearest preceding numbered/markdown heading's level + 1
        (default level 1 if nothing precedes them), then get inserted
        as real SectionHeaderItem nodes.
        """

        anchors = self.build_heading_anchors(existing_headings, document,)

        # Chain insertions so multiple inline headings under the same
        # anchor stay in their own relative order.
        last_inserted_after = {}

        inserted = []

        ordered_candidates = sorted(
            unmatched_inline,
            key=lambda c: (
                c.page_no,
                c.bbox[1] if c.bbox else 0,
            ),
        )

        for candidate in ordered_candidates:

            top_y = (
                candidate.bbox[1]
                if candidate.bbox
                else 0
            )

            preceding = self.nearest_preceding_anchor(
                anchors,
                candidate.page_no,
                top_y,
            )

            # -----------------------------------------------------
            # Determine level
            # -----------------------------------------------------

            if preceding is None:

                level = 1
                sibling_item = None

            else:

                _, _, anchor_level, anchor_item = preceding

                level = min(
                    anchor_level + 1,
                    6,
                )

                sibling_item = (
                    last_inserted_after.get(
                        id(anchor_item),
                        anchor_item,
                    )
                )

            # -----------------------------------------------------
            # Provenance
            # -----------------------------------------------------

            prov = ProvenanceItem(
                page_no=candidate.page_no,
                bbox=(
                    BoundingBox(
                        l=candidate.bbox[0],
                        t=candidate.bbox[1],
                        r=candidate.bbox[2],
                        b=candidate.bbox[3],
                        coord_origin=CoordOrigin.TOPLEFT,
                    )
                    if candidate.bbox
                    else None
                ),
                charspan=(
                    0,
                    len(candidate.text),
                ),
            )

            # -----------------------------------------------------
            # Insert heading
            # -----------------------------------------------------

            if sibling_item is not None:

                new_item = document.insert_heading(
                    sibling=sibling_item,
                    text=candidate.text,
                    level=level,
                    prov=prov,
                    after=True,
                )

                if preceding is not None:
                    last_inserted_after[
                        id(preceding[3])
                    ] = new_item

            else:

                # No preceding anchor.
                #
                # add_heading() is appropriate here because there
                # is no sibling position to insert after.
                new_item = document.add_heading(
                    text=candidate.text,
                    level=level,
                    prov=prov,
                )

            # -----------------------------------------------------
            # Synchronize candidate
            # -----------------------------------------------------

            candidate.item = new_item
            candidate.level = level
            candidate.source = "inline-pdf+inherited"

            inserted.append(candidate)

            log.warning(
                "HYBRID INLINE INSERTED "
                f"page={candidate.page_no} "
                f"level={level} "
                f"text='{candidate.text}'"
            ) 

        return inserted