using Lexis.Core.DTOs.DocumentProcessing;
using Lexis.Core.DTOs.TableContinuation;
using Lexis.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Lexis.Core.Services
{
    public sealed class TableContinuationService : ITableContinuationService
    {
        private readonly TableContinuationAnalyzer _analyzer;
        private readonly ILogger<TableContinuationService> _logger;
        private readonly TableContinuationHelpers _helpers;

        public TableContinuationService(TableContinuationAnalyzer analyzer, ILogger<TableContinuationService> logger,
            TableContinuationHelpers helpers)
        {
            _analyzer = analyzer;
            _logger = logger;
            _helpers = helpers;
        } 

        public ProcessedDocument ProcessTableContinuations(ProcessedDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            var orderedTables = GetOrderedTables(document);

            if (orderedTables.Count < 2)
            {
                _logger.LogDebug(
                    "Table continuation processing skipped because the document contains fewer than two tables.");

                return document;
            }

            /*
             * Phase 1: decide every pair BEFORE mutating anything.
             *
             * A table in the middle of a chain plays two roles across the loop:
             * it is `current` for the pair before it, and `previous` for the
             * pair after it. If we strip its repeated header/caption as soon as
             * it's confirmed as a continuation (its `current` role), the very
             * same element is then evaluated again, corrupted, in its `previous`
             * role for the next pair -- e.g. its header row is gone, so header-
             * subset/overlap evidence for the NEXT pair silently disappears.
             * Deciding every pair first, against untouched original content,
             * makes every decision independent of processing order and of any
             * other pair's mutation.
             */
            var decisions = new List<(TableReference Previous, TableReference Current, TableContinuationDecision Decision)>();

            for (int i = 0; i < orderedTables.Count - 1; i++)
            {
                var previous = orderedTables[i];
                var current = orderedTables[i + 1];

                var intervening = GetInterveningElements(document, previous, current);

                _logger.LogInformation(
                    "INTERVENING Page {P} -> {C}:\n{Elements}",
                    previous.PageNumber,
                    current.PageNumber,
                    string.Join(
                        "\n",
                        intervening.Select(e =>
                            $"  Order={e.Order}, Type={e.ElementType}, " +
                            $"Text='{e.Text}', Markdown='{e.Markdown}', " +
                            $"HeadingPath='{string.Join(" > ", e.HeadingPath ?? new List<string>())}', " +
                            $"HeadingLevel={e.HeadingLevel}")));

                var previousGrid = _helpers.GetTableGrid(previous.Element);
                var currentGrid = _helpers.GetTableGrid(current.Element);

                _logger.LogInformation(
                    "TABLE DEBUG Page {P} TableId {T}:\nHeader: {Header}\nRows:\n{Rows}",
                    previous.PageNumber,
                    previous.Element.TableId,
                    previousGrid?.Header == null
                        ? "<none>"
                        : string.Join(" | ", previousGrid.Header),
                    previousGrid == null
                        ? "<null>"
                        : string.Join(
                            "\n",
                            previousGrid.Rows.Select(
                                (row, index) =>
                                    $"  Row {index}: {string.Join(" | ", row)}")));

                _logger.LogInformation(
                    "TABLE DEBUG Page {P} TableId {T}:\nHeader: {Header}\nRows:\n{Rows}",
                    current.PageNumber,
                    current.Element.TableId,
                    currentGrid?.Header == null
                        ? "<none>"
                        : string.Join(" | ", currentGrid.Header),
                    currentGrid == null
                        ? "<null>"
                        : string.Join(
                            "\n",
                            currentGrid.Rows.Select(
                                (row, index) =>
                                    $"  Row {index}: {string.Join(" | ", row)}")));

                var decision = _analyzer.Evaluate(previous.Element, previous.PageNumber, 
                    current.Element, current.PageNumber, intervening);

                LogDecision(previous, current, decision);

                decisions.Add((previous, current, decision));
            }

            // TableContinuationService.cs -- phase 2
            foreach (var (previous, current, decision) in decisions)
            {
                if (decision.Verdict == TableContinuationVerdict.Continue)
                {
                    current.Element.ContinuationOfPreviousTable = true;
                    current.Element.TableId = previous.Element.TableId;
                    current.Element.ContinuedFromPreviousPage = true;
                    previous.Element.ContinuesOnNextPage = true;

                    StripRepeatedHeaderIfPresent(previous.Element, current.Element);
                    StripLeadingCaptionIfPresent(document, current);
                    continue;
                }

                /*
                 * The analyzer did not confirm continuation. If extraction's own
                 * raw flags already mark this pair as continuous (matching
                 * TableId with both continuation flags set -- typically from
                 * Docling's own page-adjacency heuristic, upstream of this
                 * service), and the analyzer found at least one real piece of
                 * evidence AGAINST continuation, that detected contradiction has
                 * to actually take effect -- otherwise this service's evidence-
                 * weighing is purely advisory, and the chunker merges the two
                 * fragments anyway using extraction's raw, unreviewed flags
                 * regardless of what was just concluded here.
                 *
                 * A verdict reached with NO veto at all (evidence was simply
                 * sparse, not contradictory) is left alone -- there's nothing
                 * detected to act on, so extraction's original guess stands.
                 */
                bool hasAnyVeto = decision.Evidence.Any(e => e.Kind == EvidenceKind.Veto);
                bool doclingAlreadyLinksThem =
                    current.Element.TableId == previous.Element.TableId &&
                    current.Element.ContinuedFromPreviousPage &&
                    previous.Element.ContinuesOnNextPage;

                if (hasAnyVeto && doclingAlreadyLinksThem)
                {
                    current.Element.ContinuedFromPreviousPage = false;
                    previous.Element.ContinuesOnNextPage = false;
                    current.Element.ContinuationOfPreviousTable = false;

                    /*
                     * Give the current fragment its own identity instead of
                     * continuing to share previous's TableId -- otherwise the
                     * chunker's pending-table lookup (keyed by TableId) would
                     * still treat them as the same logical table even with the
                     * continuation flags cleared. Offset far above any realistic
                     * extraction-assigned id so it can never collide with a real
                     * table elsewhere in the document.
                     */
                    current.Element.TableId = 1_000_000 + current.Element.Order;

                    _logger.LogInformation(
                        "Breaking extraction-provided continuation link between Page {PreviousPage} " +
                        "TableId {PreviousTableId} and Page {CurrentPage} (new TableId {NewTableId}): " +
                        "analyzer verdict was {Verdict} with contradicting evidence present.",
                        previous.PageNumber, previous.Element.TableId,
                        current.PageNumber, current.Element.TableId, decision.Verdict);
                }
            }

            return document;
        }

        /// <summary>
        /// Removes the current table fragment's own repeated caption/title text
        /// (e.g. "Table 1 (continued).") from its Content/Markdown. Prefers a
        /// sibling `caption` element the extractor tagged explicitly; if none is
        /// found or matches, falls back to a `text` element that precedes the
        /// table and whose content matches the table's own leading line(s)
        /// verbatim. Neither path guesses at wording -- both rely on comparing
        /// actual extracted text, not a pattern for what a caption "looks like".
        /// </summary>
        private void StripLeadingCaptionIfPresent(ProcessedDocument document, TableReference current)
        {
            var page = document.Pages.FirstOrDefault(p => p.PageNumber == current.PageNumber);
            if (page == null)
                return;

            var captionElement = FindAssociatedElement(page, current.Element, "caption");
            if (captionElement != null && !string.IsNullOrWhiteSpace(captionElement.Text))
            {
                if (TryStrip(current.Element, captionElement.Text))
                    return;
            }

            var textElement = FindAssociatedElement(page, current.Element, "text");
            if (textElement != null && !string.IsNullOrWhiteSpace(textElement.Text))
            {
                TryStrip(current.Element, textElement.Text);
            }
        }

        /// <summary>
        /// Finds the element of elementType on the same page most likely to be
        /// this table's caption/label. Does NOT assume a fixed before/after
        /// ordering relative to the table -- extractors vary (some tag the
        /// caption element with an Order *after* the table it visually precedes,
        /// as in Docling-style output). Instead: prefer same HeadingPath, then
        /// pick the closest by Order distance in either direction, requiring no
        /// other meaningful element sits strictly between the two.
        /// </summary>
        private DocumentElement? FindAssociatedElement(ProcessedPage page, DocumentElement target, string elementType)
        {
            var candidates = page.Elements
                .Where(e =>
                    string.Equals(e.ElementType, elementType, StringComparison.OrdinalIgnoreCase) &&
                    e.Order != target.Order &&
                    (e.HeadingPath ?? new List<string>()).SequenceEqual(
                        target.HeadingPath ?? new List<string>(),
                        StringComparer.OrdinalIgnoreCase))
                .OrderBy(e => Math.Abs(e.Order - target.Order))
                .ToList();

            foreach (var candidate in candidates)
            {
                int lowOrder = Math.Min(candidate.Order, target.Order);
                int highOrder = Math.Max(candidate.Order, target.Order);

                bool anythingBetween = page.Elements.Any(e =>
                    e.Order > lowOrder &&
                    e.Order < highOrder &&
                    _helpers.IsMeaningfulElement(e));

                if (!anythingBetween)
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// Finds the nearest element of elementType that sits directly before
        /// target on the same page, with nothing else meaningful in between.
        /// </summary>
        private DocumentElement? FindPrecedingElement(ProcessedPage page, DocumentElement target, string elementType)
        {
            var candidate = page.Elements
                .Where(e =>
                    string.Equals(e.ElementType, elementType, StringComparison.OrdinalIgnoreCase) &&
                    e.Order < target.Order)
                .OrderByDescending(e => e.Order)
                .FirstOrDefault();

            if (candidate == null)
                return null;

            bool anythingBetween = page.Elements.Any(e =>
                e.Order > candidate.Order &&
                e.Order < target.Order &&
                _helpers.IsMeaningfulElement(e));

            return anythingBetween ? null : candidate;
        }

        /// <summary>
        /// Attempts to strip candidateText from the start of the table element's
        /// Markdown/Content. Returns true if a strip actually happened on either
        /// field, so the caller can decide whether the fallback is still needed.
        /// </summary>
        private bool TryStrip(DocumentElement tableElement, string candidateText)
        {
            var newMarkdown = RemoveLeadingText(tableElement.Markdown, candidateText);
            var newContent = RemoveLeadingText(tableElement.Content, candidateText);

            bool changed =
                !string.Equals(newMarkdown, tableElement.Markdown, StringComparison.Ordinal) ||
                !string.Equals(newContent, tableElement.Content, StringComparison.Ordinal);

            tableElement.Markdown = newMarkdown;
            tableElement.Content = newContent;

            return changed;
        }

        /// <summary>
        /// Removes candidateText from the start of source, if present as one or
        /// more leading standalone lines, along with any surrounding blank lines.
        /// Comparison is whitespace/formatting-tolerant since markdown spacing can
        /// differ slightly from plain extracted text. Stops as soon as a pipe-table
        /// row is reached without a match, or a non-matching, non-blank line is
        /// hit -- so unrelated leading prose is never touched.
        /// </summary>
        private string? RemoveLeadingText(string? source, string candidateText)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(candidateText))
                return source;

            // Support multi-line candidates (e.g. a two-line caption) by comparing
            // against the normalized, whitespace-joined form as well as line-by-line.
            var normalizedCandidate = _helpers.NormalizeCell(candidateText);

            var sourceLines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

            int stripUpTo = 0;
            var accumulated = new System.Text.StringBuilder();

            for (int i = 0; i < sourceLines.Count; i++)
            {
                var trimmed = sourceLines[i].Trim();

                if (trimmed.Length == 0)
                {
                    if (accumulated.Length == 0)
                    {
                        // Leading blank lines before any candidate match starts.
                        stripUpTo = i + 1;
                    }
                    continue;
                }

                if (MarkdownRowIsTableRow(trimmed))
                {
                    // Reached the table without a confirmed match.
                    break;
                }

                accumulated.Append(accumulated.Length > 0 ? " " : "").Append(trimmed);
                var normalizedAccumulated = _helpers.NormalizeCell(accumulated.ToString());

                if (string.Equals(normalizedAccumulated, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                {
                    stripUpTo = i + 1;
                    break;
                }

                if (!normalizedCandidate.StartsWith(normalizedAccumulated, StringComparison.OrdinalIgnoreCase))
                {
                    // Accumulated text has diverged from the candidate -- not a match.
                    break;
                }
            }

            if (stripUpTo == 0)
                return source;

            sourceLines.RemoveRange(0, stripUpTo);

            return string.Join("\n", sourceLines).Trim('\n');
        }

        private static readonly System.Text.RegularExpressions.Regex TableRowRegex =
            new System.Text.RegularExpressions.Regex(@"^\s*\|.*\|\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private bool MarkdownRowIsTableRow(string trimmedLine) => TableRowRegex.IsMatch(trimmedLine);
        private List<TableReference> GetOrderedTables(ProcessedDocument document)
        {
            return document.Pages
                .OrderBy(page => page.PageNumber)
                .SelectMany(
                    page => page.Elements
                        .Where(IsTable)
                        .OrderBy(element => element.Order)
                        .Select(
                            element => new TableReference(
                                page.PageNumber,
                                element)))
                .ToList();
        }

        private List<DocumentElement> GetInterveningElements(ProcessedDocument document, TableReference previous, TableReference current)
        {
            var result = new List<DocumentElement>();

            foreach (var page in document.Pages
                         .OrderBy(page => page.PageNumber))
            {
                if (page.PageNumber < previous.PageNumber ||
                    page.PageNumber > current.PageNumber)
                {
                    continue;
                }

                foreach (var element in page.Elements.OrderBy(e => e.Order))
                {
                    if (page.PageNumber == previous.PageNumber &&
                        element.Order <= previous.Element.Order)
                    {
                        continue;
                    }

                    if (page.PageNumber == current.PageNumber &&
                        element.Order >= current.Element.Order)
                    {
                        continue;
                    }

                    result.Add(element);
                }
            }

            return result;
        }

        private static bool IsTable(DocumentElement element)
        {
            return string.Equals(
                element.ElementType,
                "table",
                StringComparison.OrdinalIgnoreCase);
        }

        private void LogDecision(TableReference previous, TableReference current, TableContinuationDecision decision)
        {

            _logger.LogInformation(
                "EVIDENCE Page {P} -> {C}:\n{Evidence}",
                previous.PageNumber, current.PageNumber,
                string.Join("\n", decision.Evidence.Select(e =>
                    $"  [{e.Kind}{(e.Kind == EvidenceKind.Veto ? "/" + e.VetoStrength : "")}] {e.Source}: {e.Reason}")));

            switch (decision.Verdict)
            {
                case TableContinuationVerdict.Continue:
                    _logger.LogInformation(
                        "Table continuation confirmed: " +
                        "Page {PreviousPage}, TableId {PreviousTableId} -> " +
                        "Page {CurrentPage}, TableId {CurrentTableId}. " +
                        "Reason: {Reason}",
                        previous.PageNumber,
                        previous.Element.TableId,
                        current.PageNumber,
                        current.Element.TableId,
                        decision.Reason);
                    break;

                case TableContinuationVerdict.Reject:
                    _logger.LogInformation(
                        "Table continuation rejected: " +
                        "Page {PreviousPage}, TableId {PreviousTableId} -> " +
                        "Page {CurrentPage}, TableId {CurrentTableId}. " +
                        "Reason: {Reason}",
                        previous.PageNumber,
                        previous.Element.TableId,
                        current.PageNumber,
                        current.Element.TableId,
                        decision.Reason);
                    break;

                case TableContinuationVerdict.Unknown:
                    _logger.LogInformation(
                        "Table continuation unknown: " +
                        "Page {PreviousPage}, TableId {PreviousTableId} -> " +
                        "Page {CurrentPage}, TableId {CurrentTableId}. " +
                        "Reason: {Reason}",
                        previous.PageNumber,
                        previous.Element.TableId,
                        current.PageNumber,
                        current.Element.TableId,
                        decision.Reason);
                    break;
            }
        }

        private void StripRepeatedHeaderIfPresent(DocumentElement previous, DocumentElement current)
        {
            var previousGrid = _helpers.GetTableGrid(previous);
            var currentGrid = _helpers.GetTableGrid(current);

            if (previousGrid?.Header == null || currentGrid?.Header == null)
                return;

            var overlap = _helpers.HeaderTokenOverlap(previousGrid.Header, currentGrid.Header);

            if (overlap < 0.60)
                return; // current's header isn't actually a repeat -- leave it alone

            current.Markdown = _helpers.RemoveLeadingTableRow(current.Markdown);
            current.Content = _helpers.RemoveLeadingTableRow(current.Content);
        }

        private sealed record TableReference(int PageNumber, DocumentElement Element);
    }
}