using Lexis.Core.DTOs.DocumentProcessing;

namespace Lexis.Core.DTOs.TableContinuation
{
    public sealed class TableContinuationAnalyzer
    {
        private readonly TableContinuationHelpers _helpers;

        /*
         * Optional geometry provider.
         *
         * Returns:
         *   top
         *   bottom
         *   pageHeight
         *
         * for a table element.
         */
        private readonly Func<DocumentElement, (double top, double bottom, double pageHeight)?>?  _getGeometry;

        public TableContinuationAnalyzer(TableContinuationHelpers? helpers = null,
            Func<DocumentElement, (double top, double bottom, double pageHeight)?>? getGeometry = null)
        {
            _helpers = helpers ?? new TableContinuationHelpers();
            _getGeometry = getGeometry;
        }

        public TableContinuationDecision Evaluate(DocumentElement previous, int previousPageNumber,
            DocumentElement current, int currentPageNumber, IReadOnlyList<DocumentElement>? intervening = null)
        {
            if (previous == null)
            {
                throw new ArgumentNullException(nameof(previous));
            }

            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            intervening ??= Array.Empty<DocumentElement>();

            var evidence = new List<TableContinuationEvidence>();

            CollectDoclingEvidence(previous, current, evidence);
            CollectTableIdentityEvidence(previous, current, evidence);
            CollectElementOrderEvidence(previous, current, previousPageNumber, currentPageNumber, evidence);
            CollectSemanticContextEvidence(previous, current, evidence);
            CollectStructureEvidence(previous, current, evidence);
            CollectColumnDataEvidence(previous, current, evidence);
            CollectSequentialDataEvidence(previous, current, evidence);
            CollectTextContinuityEvidence(previous, current, evidence);
            CollectTableTerminationEvidence(previous, evidence);
            CollectPhysicalLayoutEvidence(previous, current, previousPageNumber, currentPageNumber, evidence);
            CollectInterveningContentEvidence(previous, current, intervening, previousPageNumber, currentPageNumber, evidence);
            CollectKeyValueEvidence(previous, current, evidence);

            return Decide(evidence);
        }

        private void CollectDoclingEvidence(DocumentElement previous, DocumentElement current, List<TableContinuationEvidence> evidence)
        {
            /*
             * -------------------------------------------------------------
             * Semantic continuation flag
             *
             * This explicitly says that the current table is a continuation
             * of a previous table.
             *
             * It is stronger than a page-boundary flag because it expresses
             * the semantic relationship directly.
             * -------------------------------------------------------------
             */
            if (current.ContinuationOfPreviousTable)
            {
                evidence.Add(
                    TableContinuationEvidence.Confirmation(
                        "Current table is explicitly marked as a continuation of the previous table.",
                        "DoclingFlags.SemanticContinuation"));
            }

            /*
             * -------------------------------------------------------------
             * Page-boundary flags
             *
             * Both flags agreeing is strong extraction evidence.
             * -------------------------------------------------------------
             */
            if (previous.ContinuesOnNextPage &&
                current.ContinuedFromPreviousPage)
            {
                evidence.Add(
                    TableContinuationEvidence.Confirmation(
                        "Previous table explicitly continues and current table explicitly continues from the previous page.",
                        "DoclingFlags.Both"));
            }

            /*
             * Only the previous element says that it continues.
             */
            if (previous.ContinuesOnNextPage &&
                !current.ContinuedFromPreviousPage)
            {
                evidence.Add(
                    TableContinuationEvidence.Confirmation(
                        "Previous table explicitly indicates continuation onto the next page.",
                        "DoclingFlags.PreviousOnly"));

                evidence.Add(
                    TableContinuationEvidence.Veto(
                        "Current table does not explicitly indicate that it continues from the previous page.",
                        "DoclingFlags.Disagreement",
                        VetoStrength.Soft));
            }

            /*
             * Only the current element says it continues from the previous page.
             */
            if (!previous.ContinuesOnNextPage &&
                current.ContinuedFromPreviousPage)
            {
                evidence.Add(
                    TableContinuationEvidence.Confirmation(
                        "Current table explicitly indicates that it continues from the previous page.",
                        "DoclingFlags.CurrentOnly"));

                evidence.Add(
                    TableContinuationEvidence.Veto(
                        "Previous table does not explicitly indicate continuation onto the next page.",
                        "DoclingFlags.Disagreement",
                        VetoStrength.Soft));
            }

            /*
             * Same TableId is an extraction identity signal.
             *
             * This is intentionally treated as strong identity evidence.
             */
            if (previous.TableId.HasValue &&
                current.TableId.HasValue &&
                previous.TableId.Value == current.TableId.Value)
            {
                evidence.Add(
                    TableContinuationEvidence.Confirmation(
                        $"Both elements have the same TableId ({previous.TableId.Value}).",
                        "TableId"));
            }
        }

        private void CollectTableIdentityEvidence(DocumentElement previous, DocumentElement current, List<TableContinuationEvidence> evidence)
        {
            var previousTableNumber = _helpers.ExtractTableNumber(previous);
            var currentTableNumber = _helpers.ExtractTableNumber(current);

            /*
             * If both fragments explicitly identify themselves as tables
             * and the table numbers differ, they are different tables.
             *
             * This is a hard boundary and overrides extraction identity
             * such as a shared TableId or continuation flags.
             */
            if (!string.IsNullOrWhiteSpace(previousTableNumber) && !string.IsNullOrWhiteSpace(currentTableNumber) &&
                !string.Equals(previousTableNumber, currentTableNumber, StringComparison.OrdinalIgnoreCase))
            {
                evidence.Add(
                    TableContinuationEvidence.Veto(
                        $"Previous table is explicitly identified as Table {previousTableNumber}, " +
                        $"while current table is explicitly identified as Table {currentTableNumber}. " +
                        "The current fragment is therefore a new table, not a continuation.",
                        "TableIdentity.NewTableNumber",
                        VetoStrength.Hard));

                return;
            }

            /*
             * Same explicit table number is useful confirmation that the
             * fragments refer to the same table, but it is not required
             * for continuation and is not sufficient on its own.
             */
            if (!string.IsNullOrWhiteSpace(previousTableNumber) && !string.IsNullOrWhiteSpace(currentTableNumber) &&
                string.Equals(previousTableNumber, currentTableNumber, StringComparison.OrdinalIgnoreCase))
            {
                evidence.Add(
                    TableContinuationEvidence.Compatibility(
                        $"Both fragments are explicitly identified as Table {previousTableNumber}.",
                        "TableIdentity.SameTableNumber"));
            }
        }

        private void CollectElementOrderEvidence(DocumentElement previous, DocumentElement current, int previousPageNumber,
            int currentPageNumber, List<TableContinuationEvidence> evidence)
        {
            if (currentPageNumber < previousPageNumber)
            {
                evidence.Add(
                    TableContinuationEvidence.Veto(
                        "Current table occurs before the previous table in page order.",
                        "ElementOrder",
                        VetoStrength.Hard));

                return;
            }

            if (currentPageNumber == previousPageNumber)
            {
                if (current.Order > previous.Order)
                {
                    evidence.Add(
                        TableContinuationEvidence.Compatibility(
                            "Current table occurs after previous table in element order on the same page.",
                            "ElementOrder"));
                }

                return;
            }

            if (currentPageNumber == previousPageNumber + 1)
            {
                evidence.Add(
                    TableContinuationEvidence.Compatibility(
                        "Current table is on the immediately following page.",
                        "ElementOrder"));
            }
            else
            {
                evidence.Add(
                    TableContinuationEvidence.Context(
                        "Current table is separated from previous table by one or more pages.",
                        "ElementOrder"));
            }
        }

        private void CollectSemanticContextEvidence(DocumentElement previous, DocumentElement current, List<TableContinuationEvidence> evidence)
        {
            var previousPath = previous.HeadingPath ?? new List<string>();
            var currentPath = current.HeadingPath ?? new List<string>();

            if (previousPath.Count == 0 && currentPath.Count == 0)
            {
                evidence.Add(
                    TableContinuationEvidence.Context(
                        "Neither table has heading-path context.",
                        "HeadingPath"));

                return;
            }

            if (previousPath.SequenceEqual(
                    currentPath,
                    StringComparer.OrdinalIgnoreCase))
            {
                evidence.Add(
                    TableContinuationEvidence.Compatibility(
                        "Both tables belong to the same extraction-provided heading path.",
                        "HeadingPath"));

                return;
            }

            /*
             * Different heading paths do not automatically mean rejection.
             * A continuation can legitimately cross a page boundary where
             * extraction context changes.
             */
            evidence.Add(
                TableContinuationEvidence.Context(
                    "The tables have different extraction-provided heading paths.",
                    "HeadingPath"));
        }

        private void CollectStructureEvidence(DocumentElement previous, DocumentElement current, List<TableContinuationEvidence> evidence)
        {
            var previousGrid = _helpers.GetTableGrid(previous);
            var currentGrid = _helpers.GetTableGrid(current);

            if (previousGrid == null || currentGrid == null)
            {
                evidence.Add(
                    TableContinuationEvidence.Context(
                        "A reliable markdown table grid could not be extracted from one or both tables.",
                        "Structure"));
                return;
            }

            int columnDifference = Math.Abs(previousGrid.ColumnCount - currentGrid.ColumnCount);

            if (columnDifference == 0)
            {
                evidence.Add(
                    TableContinuationEvidence.Compatibility(
                        $"Both tables have {previousGrid.ColumnCount} columns.",
                        "Structure.ColumnCount"));
            }
            else if (_helpers.HeaderIsExplainableSubset(previousGrid, currentGrid))
            {
                /*
                 * A column-count mismatch can be explained when the smaller
                 * header schema is completely represented inside the larger one.
                 */
                evidence.Add(
                    TableContinuationEvidence.Compatibility(
                        $"Column count differs ({previousGrid.ColumnCount} vs {currentGrid.ColumnCount}), " +
                        "but every header label in the smaller fragment also appears in the larger " +
                        "fragment's header, consistent with a column collapsing during extraction " +
                        "(e.g. an empty leading cell) rather than a genuinely different table.",
                        "Structure.ColumnCount.ExplainedMismatch"));
            }
            else if (columnDifference == 1)
            {
                /*
                 * A one-column difference is suspicious, but not strong enough by
                 * itself to declare that these are different tables.
                 *
                 * Other independent continuation evidence may still establish
                 * that this is the same logical table.
                 */
                evidence.Add(
                    TableContinuationEvidence.Veto(
                        $"Column count differs by exactly one ({previousGrid.ColumnCount} vs {currentGrid.ColumnCount}). " +
                        "This is treated as a soft structural contradiction because the difference " +
                        "may be caused by extraction.",
                        "Structure.ColumnCount",
                        VetoStrength.Soft));
            }
            else
            {
                /*
                 * A difference of two or more columns remains a hard structural
                 * contradiction.
                 */
                evidence.Add(
                    TableContinuationEvidence.Veto(
                        $"Column count differs by {columnDifference} " +
                        $"({previousGrid.ColumnCount} vs {currentGrid.ColumnCount}).",
                        "Structure.ColumnCount",
                        VetoStrength.Hard));

                return;
            }

            var previousHeader = previousGrid.Header;
            var currentHeader = currentGrid.Header;

            if (previousHeader != null && currentHeader != null)
            {
                var overlap = _helpers.HeaderTokenOverlap(previousHeader, currentHeader);

                if (overlap >= 0.60)
                {
                    evidence.Add(
                        TableContinuationEvidence.Confirmation(
                            $"Current table repeats the previous table header (token overlap {overlap:P0}).",
                            "Structure.RepeatedHeader"));
                }
                else if (overlap < 0.30)
                {
                    evidence.Add(
                        TableContinuationEvidence.Veto(
                            $"Table headers have very low token overlap ({overlap:P0}).",
                            "Structure.HeaderMismatch",
                            VetoStrength.Soft));
                }
                else
                {
                    evidence.Add(
                        TableContinuationEvidence.Compatibility(
                            $"Table headers have partial token overlap ({overlap:P0}).",
                            "Structure.HeaderOverlap"));
                }
            }
            else
            {
                evidence.Add(
                    TableContinuationEvidence.Context(
                        "One or both table fragments do not expose a reliable header row.",
                        "Structure.Header"));
            }
        }

        private void CollectColumnDataEvidence(DocumentElement previous, DocumentElement current, List<TableContinuationEvidence> evidence)
        {
            var previousGrid = _helpers.GetTableGrid(previous);
            var currentGrid = _helpers.GetTableGrid(current);

            if (previousGrid == null || currentGrid == null)
            {
                return;
            }

            if (previousGrid.ColumnCount != currentGrid.ColumnCount)
            {
                return;
            }

            bool foundMeaningfulCompatibility = false;

            for (int column = 0;
                 column < previousGrid.ColumnCount;
                 column++)
            {
                var previousTypes =
                    previousGrid.Rows
                        .Select(row =>
                            _helpers.DominantCellType(
                                row,
                                column))
                        .Where(type =>
                            !string.Equals(
                                type,
                                "Unknown",
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                var currentTypes =
                    currentGrid.Rows
                        .Select(row =>
                            _helpers.DominantCellType(
                                row,
                                column))
                        .Where(type =>
                            !string.Equals(
                                type,
                                "Unknown",
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (previousTypes.Count == 0 || currentTypes.Count == 0)
                {
                    continue;
                }

                var previousDominant = DominantType(previousTypes);
                var currentDominant = DominantType(currentTypes);

                if (AreCompatibleTypes(previousDominant, currentDominant))
                {
                    /*
                     * Generic Text/Text is not counted as strong evidence.
                     * Almost every table can produce that combination.
                     */
                    if (!(previousDominant == "Text" && currentDominant == "Text"))
                    {
                        foundMeaningfulCompatibility = true;
                    }

                    continue;
                }

                evidence.Add(
                    TableContinuationEvidence.Veto(
                        $"Column {column + 1} has incompatible data types ({previousDominant} vs {currentDominant}).",
                        "ColumnTypes",
                        VetoStrength.Soft));
            }

            if (foundMeaningfulCompatibility)
            {
                evidence.Add(
                    TableContinuationEvidence.Compatibility(
                        "One or more table columns have compatible data types across the two fragments.",
                        "ColumnTypes"));
            }
        }

        private void CollectSequentialDataEvidence(DocumentElement previous, DocumentElement current, List<TableContinuationEvidence> evidence)
        {
            var previousGrid = _helpers.GetTableGrid(previous);
            var currentGrid = _helpers.GetTableGrid(current);

            if (previousGrid == null || currentGrid == null || previousGrid.ColumnCount == 0 || currentGrid.ColumnCount == 0)
            {
                return;
            }

            int columns =
                Math.Min(
                    previousGrid.ColumnCount,
                    currentGrid.ColumnCount);

            for (int column = 0;
                 column < columns;
                 column++)
            {
                var previousCell =
                    previousGrid.Rows
                        .Select(row =>
                            column < row.Count
                                ? row[column]
                                : string.Empty)
                        .LastOrDefault(
                            value =>
                                !string.IsNullOrWhiteSpace(value));

                var currentCell =
                    currentGrid.Rows
                        .Select(row =>
                            column < row.Count
                                ? row[column]
                                : string.Empty)
                        .FirstOrDefault(
                            value =>
                                !string.IsNullOrWhiteSpace(value));

                if (string.IsNullOrWhiteSpace(previousCell) ||
                    string.IsNullOrWhiteSpace(currentCell))
                {
                    continue;
                }

                /*
                 * Integer sequence.
                 *
                 * Deliberately require the whole cell to be an integer.
                 * We do NOT extract arbitrary numbers from strings such
                 * as "Invoice 100".
                 */
                if (_helpers.TryParseInteger(
                        previousCell,
                        out var previousInteger) &&
                    _helpers.TryParseInteger(
                        currentCell,
                        out var currentInteger))
                {
                    if (currentInteger == previousInteger + 1)
                    {
                        evidence.Add(
                            TableContinuationEvidence.Confirmation(
                                $"Column {column + 1} contains sequential integer values ({previousInteger} → {currentInteger}).",
                                "SequentialData.Integer"));
                    }

                    continue;
                }

                /*
                 * Date sequence.
                 */
                if (_helpers.TryParseDate(
                        previousCell,
                        out var previousDate) &&
                    _helpers.TryParseDate(
                        currentCell,
                        out var currentDate))
                {
                    var days =
                        (currentDate - previousDate).TotalDays;

                    if (days > 0 && days <= 31)
                    {
                        evidence.Add(
                            TableContinuationEvidence.Confirmation(
                                $"Column {column + 1} contains sequential dates ({previousDate:yyyy-MM-dd} → {currentDate:yyyy-MM-dd}).",
                                "SequentialData.Date"));
                    }

                    continue;
                }

                /*
                 * Structured identifier sequence.
                 *
                 * Example:
                 * A100 → A101
                 */
                if (_helpers.LooksLikeIdentifier(previousCell) &&
                    _helpers.LooksLikeIdentifier(currentCell) &&
                    TryIncrementIdentifier(
                        previousCell,
                        currentCell))
                {
                    evidence.Add(
                        TableContinuationEvidence.Confirmation(
                            $"Column {column + 1} contains sequential identifiers ({previousCell} → {currentCell}).",
                            "SequentialData.Identifier"));
                }
            }
        }

        private void CollectTextContinuityEvidence(DocumentElement previous, DocumentElement current, List<TableContinuationEvidence> evidence)
        {
            var previousGrid = _helpers.GetTableGrid(previous);
            var currentGrid = _helpers.GetTableGrid(current);

            if (previousGrid == null || currentGrid == null)
            {
                return;
            }

            int columns =
                Math.Min(
                    previousGrid.ColumnCount,
                    currentGrid.ColumnCount);

            for (int column = 0;
                 column < columns;
                 column++)
            {
                var previousCell =
                    previousGrid.Rows
                        .Select(row =>
                            column < row.Count
                                ? row[column]
                                : string.Empty)
                        .LastOrDefault(
                            value =>
                                !string.IsNullOrWhiteSpace(value));

                var currentCell =
                    currentGrid.Rows
                        .Select(row =>
                            column < row.Count
                                ? row[column]
                                : string.Empty)
                        .FirstOrDefault(
                            value =>
                                !string.IsNullOrWhiteSpace(value));

                if (_helpers.LooksLikeTextContinuation(
                        previousCell,
                        currentCell))
                {
                    evidence.Add(
                        TableContinuationEvidence.Confirmation(
                            $"Column {column + 1} appears to continue unfinished text from the previous table fragment.",
                            "TextContinuity"));

                    /*
                     * One clear continuation cell is enough to establish
                     * the signal; don't generate duplicate evidence for
                     * every possible column.
                     */
                    return;
                }
            }
        }
        
        private void CollectTableTerminationEvidence(DocumentElement previous, List<TableContinuationEvidence> evidence)
        {
            var grid = _helpers.GetTableGrid(previous);

            if (grid == null || grid.Rows.Count == 0)
            {
                return;
            }

            var lastRow = grid.Rows[^1];

            var rowText =
                string.Join(
                    " ",
                    lastRow
                        .Select(_helpers.NormalizeCell)
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x)));

            if (string.IsNullOrWhiteSpace(rowText))
                return;

            /*
             * Grand Total is deliberately treated as a hard termination.
             * A table that has explicitly reached its grand total should
             * not normally continue as another table fragment.
             */
            if (rowText.IndexOf(
                    "grand total",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                evidence.Add(
                    TableContinuationEvidence.Veto(
                        "Previous table ends with a Grand Total row.",
                        "TableTermination.GrandTotal",
                        VetoStrength.Hard));

                return;
            }

            /*
             * Ordinary total/subtotal rows are softer because a table can
             * contain subtotals before another page fragment.
             */
            if (rowText.IndexOf(
                    "subtotal",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                rowText.IndexOf(
                    "total",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                rowText.IndexOf(
                    "summary",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                evidence.Add(
                    TableContinuationEvidence.Veto(
                        "Previous table appears to end with a total/subtotal/summary row.",
                        "TableTermination",
                        VetoStrength.Soft));
            }
        }

        private void CollectPhysicalLayoutEvidence(DocumentElement previous, DocumentElement current, int previousPageNumber,
            int currentPageNumber, List<TableContinuationEvidence> evidence)
        {
            /*
             * Physical continuation is meaningful only across adjacent
             * pages.
             */
            if (currentPageNumber != previousPageNumber + 1)
            {
                return;
            }

            if (_getGeometry == null)
            {
                evidence.Add(
                    TableContinuationEvidence.Context(
                        "Tables are on adjacent pages, but physical geometry is unavailable.",
                        "PhysicalLayout"));

                return;
            }

            var previousGeometry =
                _getGeometry(previous);

            var currentGeometry =
                _getGeometry(current);

            if (!previousGeometry.HasValue ||
                !currentGeometry.HasValue)
            {
                evidence.Add(
                    TableContinuationEvidence.Context(
                        "Tables are on adjacent pages, but geometry could not be resolved.",
                        "PhysicalLayout"));

                return;
            }

            var previousData =
                previousGeometry.Value;

            var currentData =
                currentGeometry.Value;

            if (previousData.pageHeight <= 0 ||
                currentData.pageHeight <= 0)
            {
                return;
            }

            var previousBottomRatio =
                previousData.bottom /
                previousData.pageHeight;

            var currentTopRatio =
                currentData.top /
                currentData.pageHeight;

            /*
             * A table ending near the bottom of page A and another starting
             * near the top of page B is physically compatible with
             * continuation.
             */
            if (previousBottomRatio >= 0.70 &&
                currentTopRatio <= 0.30)
            {
                evidence.Add(
                    TableContinuationEvidence.Confirmation(
                        "Previous table ends near the bottom of its page and current table starts near the top of the next page.",
                        "PhysicalLayout"));
            }
            else if (previousBottomRatio <= 0.45 &&
                     currentTopRatio >= 0.50)
            {
                evidence.Add(
                    TableContinuationEvidence.Context(
                        "Physical positions provide weak evidence against direct page continuation.",
                        "PhysicalLayout"));
            }
        }

        private void CollectInterveningContentEvidence(DocumentElement previous, DocumentElement current,
            IReadOnlyList<DocumentElement> intervening, int previousPageNumber, int currentPageNumber,
            List<TableContinuationEvidence> evidence)
        {
            if (intervening.Count == 0)
                return;

            var previousGrid = _helpers.GetTableGrid(previous);
            var currentGrid = _helpers.GetTableGrid(current);

            /*
             * Count normalized text repeated across THIS pair's own
             * intervening elements. A table's title/subtitle or a disclaimer
             * paragraph is sometimes reprinted once per continuation page of
             * a multi-page table -- appearing more than once within one
             * pair's intervening span is itself evidence the text is
             * boilerplate specific to the table, not a one-off separator
             * between two different tables (a genuine new section heading
             * appears exactly once, at the boundary, not duplicated on both
             * sides of it).
             */
            var textOccurrences = intervening
                .Select(e => _helpers.NormalizeCell(e.Text))
                .Where(t => t.Length > 0)
                .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var element in intervening)
            {
                if (!_helpers.IsMeaningfulElement(element))
                    continue;

                if (_helpers.LooksLikeContinuationMarkerOrTableNumber(element))
                    continue;

                if (_helpers.IsFlattenedTableContent(element, previousGrid, currentGrid))
                {
                    evidence.Add(
                        TableContinuationEvidence.Context(
                            $"Intervening element Order={element.Order} is also represented " +
                            "inside one of the adjacent table fragments and is therefore " +
                            "treated as flattened table content rather than independent " +
                            "document content.",
                            "InterveningContent.TableArtifact"));
                    continue;
                }

                if (_helpers.AppearsInHeadingPath(element.Text, previous, current))
                {
                    evidence.Add(
                        TableContinuationEvidence.Context(
                            $"Intervening element Order={element.Order} matches part of the " +
                            "table's own already-recorded heading path -- likely a fragment " +
                            "of a title that wraps across multiple lines in the source PDF, " +
                            "not new separating content.",
                            "InterveningContent.HeadingPathArtifact"));
                    continue;
                }

                if (_helpers.IsTableAssociatedBoilerplate(element, previous, current))
                {
                    evidence.Add(
                        TableContinuationEvidence.Context(
                            $"Intervening element Order={element.Order} contains table-specific " +
                            "boilerplate associated with the same logical table and is therefore " +
                            "treated as table representation rather than independent document content.",
                            "InterveningContent.TableBoilerplate"));
                    continue;
                }

                var normalizedText = _helpers.NormalizeCell(element.Text);
                
                if (normalizedText.Length > 0 && textOccurrences.TryGetValue(normalizedText, out var occurrenceCount) &&
                    occurrenceCount > 1)
                {
                    evidence.Add(
                        TableContinuationEvidence.Context(
                            $"Intervening element Order={element.Order} repeats verbatim " +
                            "elsewhere within this pair's own intervening content -- " +
                            "consistent with boilerplate (a running title, subtitle, or " +
                            "disclaimer) reprinted once per continuation page, not a " +
                            "one-off separating heading.",
                            "InterveningContent.RepeatedArtifact"));
                    continue;
                }

                if (_helpers.IsGenuineHeading(element))
                {
                    evidence.Add(
                        TableContinuationEvidence.Veto(
                            "A genuine heading occurs between the two table fragments.",
                            "InterveningContent.Heading",
                            VetoStrength.Soft));
                    continue;
                }

                if (_helpers.WordCount(element.Text) <= SubstantialContentWordThreshold)
                    continue;

                if (previousPageNumber == currentPageNumber)
                {
                    evidence.Add(
                        TableContinuationEvidence.Veto(
                            "Meaningful body content occurs between the two tables on the same page.",
                            "InterveningContent.Body",
                            VetoStrength.Hard));
                }
                else
                {
                    evidence.Add(
                        TableContinuationEvidence.Veto(
                            "Meaningful content occurs between the two table fragments.",
                            "InterveningContent.Body",
                            VetoStrength.Soft));
                }
            }
        }

        private const int SubstantialContentWordThreshold = 12;

        private void CollectKeyValueEvidence(DocumentElement previous, DocumentElement current, List<TableContinuationEvidence> evidence)
        {
            var previousGrid = _helpers.GetTableGrid(previous);
            var currentGrid = _helpers.GetTableGrid(current);

            if (previousGrid == null || currentGrid == null)
            {
                return;
            }

            if (previousGrid.Header == null ||
                currentGrid.Header == null)
            {
                return;
            }

            var previousKeys =
                ExtractHeaderKeys(previousGrid.Header);

            var currentKeys =
                ExtractHeaderKeys(currentGrid.Header);

            if (previousKeys.Count == 0 ||
                currentKeys.Count == 0)
            {
                return;
            }

            if (previousKeys.SetEquals(currentKeys))
            {
                /*
                 * Same schema is compatibility, not confirmation.
                 */
                evidence.Add(
                    TableContinuationEvidence.Compatibility(
                        "Both fragments expose the same key/header schema.",
                        "KeyValueSchema"));
            }
            else if (currentKeys.IsSubsetOf(previousKeys) ||
                     previousKeys.IsSubsetOf(currentKeys))
            {
                /*
                 * A subset schema is deliberately NOT confirmation.
                 * It may represent a different table.
                 */
                evidence.Add(
                    TableContinuationEvidence.Compatibility(
                        "The table header schemas partially overlap.",
                        "KeyValueSchema"));
            }
            else
            {
                evidence.Add(
                    TableContinuationEvidence.Veto(
                        "The table header schemas are structurally disjoint.",
                        "KeyValueSchema",
                        VetoStrength.Soft));
            }
        }

        private TableContinuationDecision Decide(IReadOnlyList<TableContinuationEvidence> evidence)
        {
            /*
             * -------------------------------------------------------------
             * STEP 1: Strong extraction identity.
             *
             * A matching TableId is very strong continuation evidence,
             * but it is not an unconditional guarantee.
             * -------------------------------------------------------------
             */
            bool sameTableId =
                evidence.Any(
                    x =>
                        x.Kind == EvidenceKind.Confirmation &&
                        x.Source == "TableId");

            /*
             * -------------------------------------------------------------
             * STEP 2: Veto state.
             * -------------------------------------------------------------
             */
            var hardVeto =
                evidence.FirstOrDefault(
                    x =>
                        x.Kind == EvidenceKind.Veto &&
                        x.VetoStrength == VetoStrength.Hard);

            bool hasSoftVeto =
                evidence.Any(
                    x =>
                        x.Kind == EvidenceKind.Veto &&
                        x.VetoStrength == VetoStrength.Soft);

            /*
             * Explicit table numbering is a direct table-boundary signal.
             *
             * If the previous fragment is Table 2-6 and the current fragment
             * is Table 2-7, reject continuation even when Docling assigned
             * the same TableId or continuation flags.
             */
            var explicitNewTable =
                evidence.FirstOrDefault(
                    x =>
                        x.Kind == EvidenceKind.Veto &&
                        x.Source == "TableIdentity.NewTableNumber");

            if (explicitNewTable != null)
            {
                return TableContinuationDecision.Reject(
                    explicitNewTable.Reason,
                    evidence);
            }

            /*
             * -------------------------------------------------------------
             * STEP 3: Same TableId + hard contradiction.
             *
             * Same TableId is strong evidence that these fragments belong
             * to the same logical table. However, a hard contradiction
             * indicates that something does not agree structurally or
             * contextually.
             *
             * We therefore do NOT automatically continue and we do NOT
             * automatically reject.
             *
             * Unknown means this case requires investigation.
             * -------------------------------------------------------------
             */
            if (sameTableId && hardVeto != null)
            {
                return TableContinuationDecision.Unknown(
                    "Both fragments have the same extraction TableId, " +
                    "which strongly indicates continuation, but hard contradiction " +
                    "evidence was also detected. Further investigation is required. " +
                    $"Hard veto: {hardVeto.Reason}",
                    evidence);
            }

            /*
             * STEP 4: Same TableId without a hard contradiction.
             *
             * Matching TableId is strong evidence, but it is not infallible --
             * extraction can assign the same identifier to two genuinely
             * different tables (e.g. when a heading between them was itself
             * missed or mis-exempted upstream). A soft veto -- a genuine
             * intervening heading, a structural mismatch -- is real contradicting
             * evidence and is no longer overridden silently. This mirrors Step 3's
             * treatment of sameTableId + a hard veto: send it to Unknown for
             * review rather than trusting the identity claim unconditionally.
             */
            if (sameTableId)
            {
                if (hasSoftVeto)
                {
                    var softVetoReasons =
                        evidence
                            .Where(x => x.Kind == EvidenceKind.Veto && x.VetoStrength == VetoStrength.Soft)
                            .Select(x => $"{x.Source}: {x.Reason}")
                            .ToList();

                    return TableContinuationDecision.Unknown(
                        "Both fragments have the same extraction TableId, which normally " +
                        "indicates continuation, but soft veto evidence was also detected. " +
                        "Matching TableId alone is not treated as conclusive in that case, " +
                        "since extraction can assign the same identifier to two genuinely " +
                        "different tables. Further investigation is required. " +
                        $"Soft vetoes: {string.Join(" | ", softVetoReasons)}",
                        evidence);
                }

                return TableContinuationDecision.Continue(
                    "Both fragments have the same extraction TableId and no hard or soft " +
                    "contradiction was detected.",
                    evidence);
            }

            /*
             * -------------------------------------------------------------
             * STEP 5: Hard veto without matching TableId.
             *
             * Without the strong TableId identity signal, a hard
             * contradiction rejects the continuation.
             * -------------------------------------------------------------
             */
            if (hardVeto != null)
            {
                return TableContinuationDecision.Reject(
                    hardVeto.Reason,
                    evidence);
            }

            /*
             * STEP 6/7: Identify strong structural/content/extraction-flag
             * confirmations, all gated the same way below.
             */

            bool bothDoclingFlags =
                evidence.Any(x => x.Kind == EvidenceKind.Confirmation && x.Source == "DoclingFlags.Both");

            bool semanticContinuation =
                evidence.Any(x => x.Kind == EvidenceKind.Confirmation && x.Source == "DoclingFlags.SemanticContinuation");

            bool repeatedHeader =
                evidence.Any(x => x.Kind == EvidenceKind.Confirmation && x.Source == "Structure.RepeatedHeader");

            bool sequentialData =
                evidence.Any(x => x.Kind == EvidenceKind.Confirmation && x.Source.StartsWith("SequentialData", StringComparison.Ordinal));

            bool textContinuity =
                evidence.Any(x => x.Kind == EvidenceKind.Confirmation && x.Source == "TextContinuity");

            bool physicalContinuation =
                evidence.Any(x => x.Kind == EvidenceKind.Confirmation && x.Source == "PhysicalLayout");

            bool previousFlagOnly =
                evidence.Any(x => x.Kind == EvidenceKind.Confirmation && x.Source == "DoclingFlags.PreviousOnly");

            bool currentFlagOnly =
                evidence.Any(x => x.Kind == EvidenceKind.Confirmation && x.Source == "DoclingFlags.CurrentOnly");

            /*
             * STEP 8: Strong Docling continuation signals.
             *
             * These are derived from extraction's own flags (two independently-set
             * page-adjacency booleans agreeing, or a per-element continuation
             * flag) rather than a direct same-object identity claim like TableId.
             * They are strong, but -- as with every other confirmation below --
             * only unconditional in the absence of a soft veto; with one present,
             * independent corroboration is required.
             */
            if (semanticContinuation || bothDoclingFlags)
            {
                if (!hasSoftVeto)
                {
                    return TableContinuationDecision.Continue(
                        semanticContinuation
                            ? "The current fragment is explicitly marked as a continuation of the previous table."
                            : "Both extraction continuation flags explicitly agree.",
                        evidence);
                }

                if (repeatedHeader || sequentialData || textContinuity || physicalContinuation)
                {
                    return TableContinuationDecision.Continue(
                        "Extraction continuation flags are supported by independent structural " +
                        "or content evidence, outweighing the soft contextual contradiction.",
                        evidence);
                }
            }

            bool sameTableNumber =
                evidence.Any(
                    x =>
                        x.Kind == EvidenceKind.Compatibility &&
                        x.Source == "TableIdentity.SameTableNumber");

            /*
             * STEP 8.5: Same explicit table identity + repeated header.
             *
             * A one-column mismatch is allowed to be overcome when both fragments
             * explicitly identify the same table and the current fragment repeats
             * the previous header.
             *
             * The column mismatch itself remains a soft veto; it is not being
             * ignored. It is being outweighed by two independent continuation
             * signals.
             */
            if (sameTableNumber && repeatedHeader && hasSoftVeto)
            {
                bool hasOnlyRecoverableColumnMismatch =
                    evidence
                        .Where(
                            x =>
                                x.Kind == EvidenceKind.Veto &&
                                x.VetoStrength == VetoStrength.Soft)
                        .All(
                            x =>
                                x.Source == "Structure.ColumnCount");

                if (hasOnlyRecoverableColumnMismatch)
                {
                    return TableContinuationDecision.Continue(
                        "Both fragments explicitly identify the same table and the current " +
                        "fragment repeats the previous table header. The one-column structural " +
                        "mismatch is therefore treated as an extraction artifact.",
                        evidence);
                }
            }

            /*
             * STEP 9: Repeated header.
             */

            if (repeatedHeader)
            {
                if (!hasSoftVeto)
                {
                    return TableContinuationDecision.Continue(
                        "The current fragment repeats the previous table header with matching column structure.",
                        evidence);
                }

                if (physicalContinuation || sequentialData || textContinuity || semanticContinuation || bothDoclingFlags)
                {
                    return TableContinuationDecision.Continue(
                        "Repeated header plus independent continuation evidence outweighs the soft contextual contradiction.",
                        evidence);
                }
            }

            /*
             * STEP 10: Sequential data.
             */

            if (sequentialData)
            {
                if (!hasSoftVeto)
                {
                    return TableContinuationDecision.Continue(
                        "Sequential table data provides direct continuation evidence.",
                        evidence);
                }

                if (physicalContinuation || textContinuity || previousFlagOnly || currentFlagOnly || semanticContinuation || bothDoclingFlags)
                {
                    return TableContinuationDecision.Continue(
                        "Sequential data plus independent continuation evidence outweighs the soft contextual contradiction.",
                        evidence);
                }
            }

            /*
             * STEP 11: Text continuity.
             */

            if (textContinuity)
            {
                if (!hasSoftVeto)
                {
                    return TableContinuationDecision.Continue(
                        "Text in the current fragment appears to continue unfinished text from the previous fragment.",
                        evidence);
                }

                if (physicalContinuation || previousFlagOnly || currentFlagOnly || semanticContinuation || bothDoclingFlags)
                {
                    return TableContinuationDecision.Continue(
                        "Text continuation plus independent extraction/layout evidence supports continuation.",
                        evidence);
                }
            }

            /*
             * STEP 12: One-sided extraction flag.
             */

            if (previousFlagOnly || currentFlagOnly)
            {
                if (!hasSoftVeto)
                {
                    return TableContinuationDecision.Continue(
                        "The extraction service explicitly identifies the table boundary as a continuation.",
                        evidence);
                }

                if (physicalContinuation || repeatedHeader || sequentialData || textContinuity)
                {
                    return TableContinuationDecision.Continue(
                        "Extraction continuation evidence is supported by independent structural/content evidence.",
                        evidence);
                }
            }

            /*
             * STEP 13: Physical adjacency alone is NOT enough.
             */

            if (physicalContinuation)
            {
                return TableContinuationDecision.Unknown(
                    "The tables are physically positioned like a continuation, but there is no independent confirmation.",
                    evidence);
            }


            /*
             * -------------------------------------------------------------
             * STEP 13: Same heading path + same structure is compatibility only.
             * -------------------------------------------------------------
             */
            bool sameHeadingPath =
                evidence.Any(
                    x =>
                        x.Kind == EvidenceKind.Compatibility &&
                        x.Source == "HeadingPath");

            bool sameColumnCount =
                evidence.Any(
                    x =>
                        x.Kind == EvidenceKind.Compatibility &&
                        x.Source == "Structure.ColumnCount");

            bool explainedColumnMismatch =
                evidence.Any(
                    x =>
                        x.Kind == EvidenceKind.Compatibility &&
                        x.Source == "Structure.ColumnCount.ExplainedMismatch");

            if (sameHeadingPath && sameColumnCount)
            {
                return hasSoftVeto
                    ? TableContinuationDecision.Reject(
                        "The fragments share heading context and structure, but there is contextual evidence that they are separate tables.",
                        evidence)
                    : TableContinuationDecision.Unknown(
                        "The fragments share heading context and structure, but there is no actual continuation confirmation.",
                        evidence);
            }

            /*
             * -------------------------------------------------------------
             * STEP 14: Soft veto without confirmation.
             * -------------------------------------------------------------
             */
            if (hasSoftVeto)
            {
                var softVetoReasons =
                    evidence
                        .Where(
                            x =>
                                x.Kind == EvidenceKind.Veto &&
                                x.VetoStrength == VetoStrength.Soft)
                        .Select(
                            x => $"{x.Source}: {x.Reason}")
                        .ToList();

                return TableContinuationDecision.Reject(
                    "Continuation was rejected because soft veto evidence was " +
                    "present and no sufficiently strong continuation confirmation " +
                    $"was found. Soft vetoes: {string.Join(" | ", softVetoReasons)}",
                    evidence);
            }

            /*
             * -------------------------------------------------------------
             * STEP 15: No contradiction and no confirmation.
             * -------------------------------------------------------------
             */
            return TableContinuationDecision.Unknown(
                "Insufficient evidence to determine whether the two table fragments are continuations.",
                evidence);
        }

        private HashSet<string> ExtractHeaderKeys(IReadOnlyList<string> header)
        {
            var keys =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var cell in header)
            {
                var normalized =
                    _helpers.NormalizeCell(cell);

                if (string.IsNullOrWhiteSpace(normalized))
                    continue;

                var key =
                    normalized
                        .ToLowerInvariant()
                        .Replace(" ", "_");

                keys.Add(key);
            }

            return keys;
        }

        private string DominantType(IReadOnlyList<string> types)
        {
            if (types.Count == 0)
                return "Unknown";

            return types
                .GroupBy(
                    x => x,
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(
                    group => group.Count())
                .ThenBy(
                    group => group.Key,
                    StringComparer.OrdinalIgnoreCase)
                .First()
                .Key;
        }

        private bool AreCompatibleTypes(string previous, string current)
        {
            if (string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            /*
             * Unknown does not create a contradiction.
             */
            if (previous == "Unknown" || current == "Unknown")
            {
                return true;
            }

            /*
             * Number and Integer are compatible.
             */
            if ((previous == "Integer" && current == "Number") ||
                (previous == "Number" && current == "Integer"))
            {
                return true;
            }

            /*
             * Text can legitimately contain values that extraction
             * classified differently, so Text is not automatically
             * considered a structural contradiction.
             */
            if (previous == "Text" || current == "Text")
            {
                return true;
            }

            return false;
        }

        private bool TryIncrementIdentifier(string previous, string current)
        {
            var previousNormalized = _helpers.NormalizeCell(previous);
            var currentNormalized = _helpers.NormalizeCell(current);

            var previousMatch =
                System.Text.RegularExpressions.Regex.Match(
                    previousNormalized,
                    @"^(?<prefix>[A-Za-z]+[-/]?)(?<number>\d+)$");

            var currentMatch =
                System.Text.RegularExpressions.Regex.Match(
                    currentNormalized,
                    @"^(?<prefix>[A-Za-z]+[-/]?)(?<number>\d+)$");

            if (!previousMatch.Success ||
                !currentMatch.Success)
            {
                return false;
            }

            var previousPrefix =
                previousMatch.Groups["prefix"].Value;

            var currentPrefix =
                currentMatch.Groups["prefix"].Value;

            if (!string.Equals(
                    previousPrefix,
                    currentPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!int.TryParse(
                    previousMatch.Groups["number"].Value,
                    out var previousNumber))
            {
                return false;
            }

            if (!int.TryParse(
                    currentMatch.Groups["number"].Value,
                    out var currentNumber))
            {
                return false;
            }

            return currentNumber == previousNumber + 1;
        }
    }
}