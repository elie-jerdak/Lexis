using Lexis.Core.DTOs.DocumentProcessing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lexis.Core.DTOs.TableContinuation
{
    public sealed class TableContinuationHelpers
    {
        private static readonly Regex MarkdownRowRegex =
            new Regex(
                @"^\s*\|.*\|\s*$",
                RegexOptions.Compiled);

        private static readonly Regex MarkdownSeparatorRegex =
            new Regex(
                @"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$",
                RegexOptions.Compiled);

        private static readonly Regex TableNumberRegex =
            new Regex(
                @"\btable\s+([A-Za-z]?\d+(?:[.-]\d+)*)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HeadingMarkerRegex =
            new Regex(
                @"^\s{0,3}(#{1,6})\s+",
                RegexOptions.Compiled);

        private static readonly Regex NumberRegex =
            new Regex(
                @"^\s*\d+\s*$",
                RegexOptions.Compiled);

        private static readonly Regex IdentifierRegex =
            new Regex(
                @"^\s*([A-Za-z]+[-/]?\d+|[A-Za-z]+\s+\d+)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SentenceEndRegex =
            new Regex(
                @"[.!?:;)\]}]$",
                RegexOptions.Compiled);

        private static readonly Regex HeaderLabelSentenceEndRegex =
            new Regex(@"[.!?;]$", RegexOptions.Compiled);

        private static readonly Regex ContinuationMarkerRegex =
            new Regex(
                @"\b(continued|continuation|cont['’]?d|continued\s+from|continued\s+on)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public TableGrid? GetTableGrid(DocumentElement element)
        {
            if (element == null)
                return null;

            var markdown = GetBestTableText(element);

            if (string.IsNullOrWhiteSpace(markdown))
                return null;

            return ParseMarkdownTable(markdown);
        }

        public string? ExtractTableTitle(DocumentElement element)
        {
            if (element == null)
                return null;

            var candidates = new[]
            {
                element.Caption,
                element.Markdown,
                element.Content,
                element.Text
            };

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                var title = ExtractTableTitleFromText(candidate);

                if (!string.IsNullOrWhiteSpace(title))
                    return title;
            }

            return null;
        }
        
        public string? ExtractTableNumber(DocumentElement element)
        {
            if (element == null)
                return null;

            var title = ExtractTableTitle(element);

            if (string.IsNullOrWhiteSpace(title))
                return null;

            var match = TableNumberRegex.Match(title);

            if (!match.Success)
                return null;

            return match.Groups[1].Value.Trim();
        }

        public bool LooksLikeExplicitTableLabel(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var normalized = NormalizeWhitespace(text);

            return Regex.IsMatch(
                normalized,
                @"^table\s+[A-Za-z]?\d+(?:\.\d+)*\b",
                RegexOptions.IgnoreCase);
        }
         
        public bool LooksLikeContinuationMarkerOrTableNumber(DocumentElement element)
        {
            if (element == null)
                return false;

            var values = new[] { element.Caption, element.Text };

            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                /*
                 * Only an explicit continuation CUE ("continued", "cont'd",
                 * "continued from") is benign intervening content. A bare
                 * "Table N. <Title>" with no continuation wording is a NEW
                 * table's own title being announced -- exactly the signal
                 * that the fragments on either side are NOT the same table,
                 * not something to treat as harmless. The previous version
                 * exempted any "Table N" match on its own (via
                 * LooksLikeExplicitTableLabel), which let a genuine new-table
                 * title slip past unflagged as intervening content.
                 */
                if (ContinuationMarkerRegex.IsMatch(value))
                    return true;
            }

            return false;
        }

        public bool LooksLikeDuplicatedCaptionRow(IReadOnlyList<string> row)
        {
            if (row == null)
                return false;

            var nonEmpty = row.Select(NormalizeCell).Where(x => x.Length > 0).ToList();

            /*
             * Need at least two cells to call anything "duplicated," and the
             * shared text must be substantial. A genuinely duplicated table
             * caption is always a full descriptive phrase (every real example
             * seen is 20+ characters); a short value repeating by coincidence
             * ("-", "N/A", "0") is a legitimate, if degenerate, data row, not
             * a caption artifact -- this length floor keeps the check from
             * misfiring on that case.
             */
            if (nonEmpty.Count < 2)
                return false;

            var first = nonEmpty[0];
            if (first.Length < MinDuplicatedCaptionLength)
                return false;

            return nonEmpty.All(x => string.Equals(x, first, StringComparison.OrdinalIgnoreCase));
        }

        private const int MinDuplicatedCaptionLength = 20;

        public bool LooksLikeHeaderRow(IReadOnlyList<string> row)
        {
            if (row == null || row.Count == 0)
                return false;

            var nonEmpty = row
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (nonEmpty.Count == 0)
                return false;

            int labelLike = 0;

            foreach (var cell in nonEmpty)
            {
                if (LooksLikeHeaderLabel(cell))
                    labelLike++;
            }

            return (double)labelLike / nonEmpty.Count >= 0.60;
        }

        public bool LooksLikeHeaderLabel(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = NormalizeCell(value);

            if (normalized.Length == 0)
                return false;

            if (NumberRegex.IsMatch(normalized))
                return false;

            if (TryParseDate(normalized, out _))
                return false;

            /*
             * Header labels tend to be short descriptive phrases rather
             * than long sentences.
             */
            if (normalized.Length > 80)
                return false;

            if (HeaderLabelSentenceEndRegex.IsMatch(normalized))
                return false;

            return true;
        }

        public double HeaderTokenOverlap(IReadOnlyList<string> previousHeader, IReadOnlyList<string> currentHeader)
        {
            var previousTokens = TokenizeHeader(previousHeader);
            var currentTokens = TokenizeHeader(currentHeader);

            if (previousTokens.Count == 0 ||
                currentTokens.Count == 0)
            {
                return 0;
            }

            var intersection = previousTokens
                .Intersect(currentTokens, StringComparer.OrdinalIgnoreCase)
                .Count();

            var union = previousTokens
                .Union(currentTokens, StringComparer.OrdinalIgnoreCase)
                .Count();

            if (union == 0)
                return 0;

            return (double)intersection / union;
        }

        public string DominantCellType(IReadOnlyList<string> row, int columnIndex)
        {
            if (row == null || columnIndex < 0 || columnIndex >= row.Count)
            {
                return "Unknown";
            }

            var value = NormalizeCell(row[columnIndex]);

            if (value.Length == 0)
                return "Unknown";

            if (int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                return "Integer";
            }

            if (TryParseDate(value, out _))
                return "Date";

            if (decimal.TryParse(
                    value,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                return "Number";
            }

            if (LooksLikeIdentifier(value))
                return "Identifier";

            if (LooksLikePercentage(value))
                return "Percentage";

            return "Text";
        }

        public bool TryParseInteger(string? value, out int result)
        {
            result = 0;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            return int.TryParse(
                NormalizeCell(value),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result);
        }

        public bool TryParseDate(string? value, out DateTime result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = NormalizeCell(value);

            string[] formats =
            {
                "yyyy-MM-dd",
                "dd/MM/yyyy",
                "MM/dd/yyyy",
                "dd-MM-yyyy",
                "MM-dd-yyyy",
                "yyyy/MM/dd",
                "d/M/yyyy",
                "M/d/yyyy",
                "d-M-yyyy",
                "M-d-yyyy"
            };

            if (DateTime.TryParseExact(
                    normalized,
                    formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out result))
            {
                return true;
            }

            return DateTime.TryParse(
                normalized,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out result);
        }

        public bool LooksLikeIdentifier(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return IdentifierRegex.IsMatch(
                NormalizeCell(value));
        }

        public bool LooksLikePercentage(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = NormalizeCell(value);

            if (!normalized.EndsWith("%", StringComparison.Ordinal))
                return false;

            var number = normalized[..^1].Trim();

            return decimal.TryParse(
                number,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out _);
        }

        public bool IsMeaningfulElement(DocumentElement element)
        {
            if (element == null)
                return false;

            var type = NormalizeElementType(element.ElementType);

            /*
             * These do not represent meaningful intervening document
             * content for table continuation purposes.
             */
            if (type == "caption" ||
                type == "page_number" ||
                type == "document_index")
            {
                return false;
            }

            if (type == "section_header" ||
                type == "heading" ||
                type == "title")
            {
                return true;
            }

            if (type == "text" ||
                type == "paragraph" ||
                type == "list_item")
            {
                return !string.IsNullOrWhiteSpace(
                    element.Text ?? element.Markdown);
            }

            if (type == "table")
                return true;

            if (type == "picture" ||
                type == "figure")
                return true;

            return !string.IsNullOrWhiteSpace(
                element.Text ??
                element.Markdown ??
                element.Content);
        }

        public bool IsGenuineHeading(DocumentElement element)
        {
            if (element == null)
                return false;

            if (element.HeadingLevel.HasValue)
                return true;

            var type = NormalizeElementType(element.ElementType);

            if (type == "section_header" ||
                type == "heading" ||
                type == "title")
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(element.HeadingText);
        }

        public string NormalizeElementType(string? elementType)
        {
            if (string.IsNullOrWhiteSpace(elementType))
                return string.Empty;

            return elementType
                .Trim()
                .ToLowerInvariant()
                .Replace("-", "_")
                .Replace(" ", "_");
        }

        public string NormalizeCell(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value.Trim();

            /*
             * Remove common markdown emphasis markers.
             */
            text = text
                .Replace("**", string.Empty)
                .Replace("__", string.Empty)
                .Replace("`", string.Empty)
                .Trim();

            return NormalizeWhitespace(text);
        }

        public string GetBestTableText(DocumentElement element)
        {
            /*
             * Markdown is preferred because it preserves table rows.
             */
            if (!string.IsNullOrWhiteSpace(element.Markdown) &&
                element.Markdown.Contains("|", StringComparison.Ordinal))
            {
                return element.Markdown;
            }

            if (!string.IsNullOrWhiteSpace(element.Content) &&
                element.Content.Contains("|", StringComparison.Ordinal))
            {
                return element.Content;
            }

            if (!string.IsNullOrWhiteSpace(element.Text) &&
                element.Text.Contains("|", StringComparison.Ordinal))
            {
                return element.Text;
            }

            return element.Markdown ??
                   element.Content ??
                   element.Text ??
                   string.Empty;
        }

        private TableGrid? ParseMarkdownTable(string markdown)
        {
            var lines = markdown
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => MarkdownRowRegex.IsMatch(x))
                .ToList();

            if (lines.Count == 0)
                return null;

            var rows = new List<List<string>>();

            foreach (var line in lines)
            {
                if (MarkdownSeparatorRegex.IsMatch(line))
                    continue;

                var cells = ParseMarkdownRow(line);

                if (cells.Count == 0)
                    continue;

                rows.Add(cells);
            }

            if (rows.Count == 0)
                return null;

            string? capturedCaptionText = null;

            while (rows.Count > 0 && !LooksLikeHeaderRow(rows[0]) && LooksLikeDuplicatedCaptionRow(rows[0]))
            {
                if (capturedCaptionText == null)
                {
                    capturedCaptionText = rows[0].Select(NormalizeCell).FirstOrDefault(x => x.Length > 0);
                }
                rows.RemoveAt(0);
            }

            if (rows.Count == 0)
                return null;

            var columnCount = rows.Max(x => x.Count); 

            if (columnCount == 0)
                return null;

            /*
             * Normalize rows so later column comparisons never throw.
             */
            foreach (var row in rows)
            {
                while (row.Count < columnCount)
                    row.Add(string.Empty);
            }

            /*
             * The first parsed row may be the table header.
             *
             * Keep the header separately for structural/header comparison,
             * but do not leave it inside Rows. Rows must represent data rows
             * only so sequential/date/identifier/text-continuity checks do
             * not accidentally inspect column titles as data.
             */
            IReadOnlyList<string>? header = null;

            if (LooksLikeHeaderRow(rows[0]))
            {
                header = rows[0];
                rows.RemoveAt(0);
            }

            return new TableGrid(rows, columnCount, header, capturedCaptionText);
        }

        private List<string> ParseMarkdownRow(string line)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("|", StringComparison.Ordinal))
                trimmed = trimmed[1..];

            if (trimmed.EndsWith("|", StringComparison.Ordinal))
                trimmed = trimmed[..^1];

            var cells = new List<string>();
            var current = new StringBuilder();
            bool escaped = false;

            foreach (var character in trimmed)
            {
                if (escaped)
                {
                    current.Append(character);
                    escaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (character == '|')
                {
                    cells.Add(NormalizeCell(current.ToString()));
                    current.Clear();
                    continue;
                }

                current.Append(character);
            }

            cells.Add(NormalizeCell(current.ToString()));

            return cells;
        }

        private string ExtractTableTitleFromText(string text)
        {
            var lines = text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));

            foreach (var line in lines)
            {
                var withoutHeadingMarkers =
                    HeadingMarkerRegex.Replace(line, string.Empty).Trim();

                if (LooksLikeExplicitTableLabel(withoutHeadingMarkers))
                    return withoutHeadingMarkers;

                if (line.StartsWith("Table ",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return line;
                }
            }

            return string.Empty;
        }

        private string ExtractLeadingInteger(string value)
        {
            var match = Regex.Match(
                value,
                @"\d+");

            return match.Success
                ? match.Value
                : string.Empty;
        }

        private HashSet<string> TokenizeHeader(IReadOnlyList<string> header)
        {
            var tokens = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var cell in header)
            {
                var normalized = NormalizeCell(cell);

                foreach (var token in Regex.Split(
                             normalized,
                             @"[^A-Za-z0-9]+"))
                {
                    if (token.Length >= 2)
                        tokens.Add(token);
                }
            }

            return tokens;
        }

        private bool LooksLikeCutOffCell(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = NormalizeCell(value);

            if (normalized.EndsWith(
                    "-",
                    StringComparison.Ordinal))
            {
                return true;
            }

            /*
             * Avoid considering every ordinary short table cell as
             * unfinished.
             */
            if (normalized.Length < 15)
                return false;

            return !SentenceEndRegex.IsMatch(normalized);
        }

        private bool StartsLikeContinuation(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = NormalizeCell(value);

            if (normalized.Length == 0)
                return false;

            return char.IsLower(normalized[0]);
        }

        public bool LooksLikeTextContinuation(string? previous, string? current)
        {
            if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(current))
            {
                return false;
            }

            var previousNormalized = NormalizeCell(previous);
            var currentNormalized = NormalizeCell(current);

            if (previousNormalized.EndsWith(
                    "-",
                    StringComparison.Ordinal) &&
                currentNormalized.Length > 0 &&
                char.IsLetter(currentNormalized[0]))
            {
                return true;
            }

            return LooksLikeCutOffCell(previousNormalized) &&
                   StartsLikeContinuation(currentNormalized);
        }

        public IReadOnlyList<string> GetFirstRow(TableGrid? grid)
        {
            if (grid == null ||
                grid.Rows.Count == 0)
            {
                return Array.Empty<string>();
            }

            return grid.Rows[0];
        }

        public IReadOnlyList<string> GetLastRow(TableGrid? grid)
        {
            if (grid == null || grid.Rows.Count == 0)
            {
                return Array.Empty<string>();
            }

            return grid.Rows[^1];
        }

        private string NormalizeWhitespace(string value)
        {
            return Regex.Replace(
                value,
                @"\s+",
                " ").Trim();
        }

        public IReadOnlyList<string> GetTableGridLines(DocumentElement element)
        {
            var text =
                !string.IsNullOrWhiteSpace(element.Markdown)
                    ? element.Markdown
                    : !string.IsNullOrWhiteSpace(element.Content)
                        ? element.Content
                        : element.Text;

            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            return text
                .Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Contains('|'))
                .ToList();
        }

        public IReadOnlyList<string> GetTableGridLines(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return Array.Empty<string>();

            return content
                .Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Contains('|'))
                .ToList();
        }

        public string? RemoveLeadingTableRow(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return markdown;

            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

            // Strip leading caption/continuation-marker lines (e.g. "Table 1 (continued).")
            // and blank lines that sit before the table itself. Stop as soon as we hit
            // a line that isn't blank and doesn't look like a table label/marker, so we
            // never eat genuine surrounding prose.
            int stripUpTo = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();

                if (trimmed.Length == 0)
                {
                    stripUpTo = i + 1;
                    continue;
                }

                if (MarkdownRowRegex.IsMatch(trimmed))
                    break; // reached the table itself

                if (LooksLikeExplicitTableLabel(trimmed) || ContinuationMarkerRegex.IsMatch(trimmed))
                {
                    stripUpTo = i + 1;
                    continue;
                }

                // Real, non-caption content before the table -- leave everything alone.
                stripUpTo = 0;
                break;
            }

            lines.RemoveRange(0, stripUpTo);

            int firstRowIndex = lines.FindIndex(l => MarkdownRowRegex.IsMatch(l.Trim()));
            if (firstRowIndex < 0)
                return string.Join("\n", lines).Trim('\n');

            lines.RemoveAt(firstRowIndex);

            if (firstRowIndex < lines.Count && MarkdownSeparatorRegex.IsMatch(lines[firstRowIndex].Trim()))
            {
                lines.RemoveAt(firstRowIndex);
            }

            return string.Join("\n", lines).Trim('\n');
        }

        public bool HeaderIsExplainableSubset(TableGrid previousGrid, TableGrid currentGrid)
        {
            if (previousGrid?.Header == null || currentGrid?.Header == null)
                return false;

            var previousKeys = ExtractNormalizedHeaderKeys(previousGrid.Header);
            var currentKeys = ExtractNormalizedHeaderKeys(currentGrid.Header);

            if (previousKeys.Count == 0 || currentKeys.Count == 0)
                return false;

            var smaller = previousKeys.Count <= currentKeys.Count ? previousKeys : currentKeys;
            var larger = previousKeys.Count <= currentKeys.Count ? currentKeys : previousKeys;

            if (!smaller.IsSubsetOf(larger))
                return false;

            /*
             * Guard against trivial coincidence by SPECIFICITY of what's
             * shared, not the count of shared keys -- a single long, specific
             * label (e.g. a 30+ character phrase) is far stronger evidence
             * than several short generic ones, so counting keys either over-
             * or under-trusts depending on what the keys actually say.
             */
            var sharedSpecificity = smaller.Sum(k => k.Length);
            return sharedSpecificity >= MinSharedHeaderSpecificity;
        }

        private const int MinSharedHeaderSpecificity = 10;

        private HashSet<string> ExtractNormalizedHeaderKeys(IReadOnlyList<string> header)
        {
            return header
                .Select(NormalizeCell)
                .Where(x => x.Length > 0)
                .Select(x => x.ToLowerInvariant())
                .ToHashSet();
        }

        private static readonly Regex WordTokenRegex = new Regex(@"[A-Za-z0-9]+", RegexOptions.Compiled);

        /// <summary>
        /// Tokenizes to lowercase alphanumeric words only. Punctuation, quote
        /// glyphs (of any Unicode variant), pipes, dashes, and whitespace all
        /// disappear equally -- this is what makes containment checks below
        /// immune to formatting/delimiter differences between two independent
        /// extractions of the same underlying content, rather than requiring
        /// us to enumerate every punctuation variant we might encounter.
        /// </summary>
        public List<string> Tokenize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            return WordTokenRegex.Matches(text.ToLowerInvariant())
                .Select(m => m.Value)
                .ToList();
        }

        private bool ContainsTokenSubsequence(IReadOnlyList<string> haystack, IReadOnlyList<string> needle)
        {
            if (needle.Count == 0 || haystack.Count < needle.Count)
                return false;

            for (int i = 0; i <= haystack.Count - needle.Count; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Count; j++)
                {
                    if (!string.Equals(haystack[i + j], needle[j], StringComparison.Ordinal))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// True if candidateText's content already appears, as a contiguous run
        /// of words, somewhere inside the table's own parsed cells (header or
        /// data rows) -- meaning the element is a floating duplicate/artifact
        /// of the table's own content, not genuine separating content.
        ///
        /// Deliberately works against PARSED cells (via GetTableGrid), joined
        /// with plain spaces, rather than the raw pipe-delimited markdown
        /// string. Two cells that are adjacent in the raw markdown are only
        /// contiguous there because of a "|" delimiter Docling's plain-text
        /// extraction of the same content never includes -- comparing against
        /// the parsed, re-joined cell text sidesteps that structural mismatch
        /// entirely, and tokenizing sidesteps any punctuation/quote-glyph
        /// differences between the two extraction paths for the same visual
        /// content (e.g. an inch mark rendered as ' in one path and as ” in
        /// the other).
        /// </summary>

        // AppearsInsideTable -- check the preserved caption text too, same token-subsequence mechanism already used for Header/Rows,
        // so a fragment is still correctly recognized even when it's only a substring of the full caption (Docling split it into pieces)
        public bool AppearsInsideTable(string? candidateText, TableGrid? grid)
        {
            if (grid == null || string.IsNullOrWhiteSpace(candidateText))
                return false;

            var candidateNormalized = NormalizeCell(candidateText);
            if (candidateNormalized.Length == 0)
                return false;

            if (grid.Header != null &&
                grid.Header.Any(cell => string.Equals(NormalizeCell(cell), candidateNormalized, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            foreach (var row in grid.Rows)
            {
                if (row.Any(cell => string.Equals(NormalizeCell(cell), candidateNormalized, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            var candidateTokens = Tokenize(candidateText);
            if (candidateTokens.Count < 3)
                return false;

            /*
             * A duplicated-caption row is intentionally excluded from Header/
             * Rows so it doesn't pollute structural evidence (see
             * LooksLikeDuplicatedCaptionRow) -- but a floating text fragment
             * elsewhere in the document that repeats that same caption is
             * still a genuine extraction duplicate, not independent content,
             * and must still be recognized as such.
             */
            if (grid.CaptionText != null &&
                ContainsTokenSubsequence(Tokenize(grid.CaptionText), candidateTokens))
            {
                return true;
            }

            if (grid.Header != null &&
                ContainsTokenSubsequence(Tokenize(string.Join(" ", grid.Header)), candidateTokens))
            {
                return true;
            }

            foreach (var row in grid.Rows)
            {
                if (ContainsTokenSubsequence(Tokenize(string.Join(" ", row)), candidateTokens))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// True if candidateText's content appears, as a contiguous run of words, inside either table fragment's own HeadingPath. 
        /// A title that wraps across multiple lines in the source PDF is sometimes split by extraction into several 
        /// separate fragments, even though it was already correctly consolidated into ONE HeadingPath entry on the table elements themselves 
        /// -- e.g. "Table 2-6. ... for Packages Labeled by Liquid and Dry Volume" as one entry, while "Table 2-6. ... for Packages" and
        /// "Liquid and Dry Volume" each separately show up as their own intervening elements (a prefix and a suffix of that same text). 
        /// Recognizing these as the fragment's own already-known title -- not new, separating content -- needs to compare against the 
        /// fragment's recorded title, not just the raw table grid.
        /// </summary>
        public bool AppearsInHeadingPath(string? candidateText, DocumentElement? previous, DocumentElement? current)
        {
            var candidateTokens = Tokenize(candidateText);
            if (candidateTokens.Count == 0)
                return false;

            foreach (var element in new[] { previous, current })
            {
                if (element?.HeadingPath == null)
                    continue;

                foreach (var pathEntry in element.HeadingPath)
                {
                    if (ContainsTokenSubsequence(Tokenize(pathEntry), candidateTokens))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true when an intervening element looks like repeated boilerplate
        /// belonging to the same table rather than independent document content.
        ///
        /// The element is considered table-associated when:
        /// 1. Its heading path matches the heading context of either table fragment.
        /// 2. Its text is short enough to be a table note/boilerplate fragment.
        /// 3. It contains table-specific wording or explicitly refers to the table.
        /// </summary>
        public bool IsTableAssociatedBoilerplate(DocumentElement element, DocumentElement previous, DocumentElement current)
        {
            if (element == null)
                return false;

            var text = NormalizeCell(element.Text);

            if (text.Length == 0)
                return false;

            var type = NormalizeElementType(element.ElementType);

            if (type != "text" &&  type != "paragraph" &&  type != "section_header" &&
                type != "heading" && type != "title")
            {
                return false;
            }

            /*
             * If the element already belongs to the same heading context as either
             * table fragment, it is very likely part of that table's extracted
             * representation rather than independent content.
             */
            if (HeadingPathMatchesTable(element, previous) || HeadingPathMatchesTable(element, current))
            {
                return true;
            }

            /*
             * Explicit table-specific wording is a strong signal that this is
             * table-associated boilerplate rather than a new document paragraph.
             *
             * Keep this deliberately narrow. We do NOT classify arbitrary text
             * between tables as harmless.
             */
            if (Regex.IsMatch(
                    text,
                    @"\b(do not use this table|for .*?, see table\s+\d|use table\s+\d.*for)\b",
                    RegexOptions.IgnoreCase))
            {
                return true;
            }

            return false;
        }

        private bool HeadingPathMatchesTable(DocumentElement element, DocumentElement table)
        {
            if (element?.HeadingPath == null || table?.HeadingPath == null ||
                element.HeadingPath.Count == 0 || table.HeadingPath.Count == 0)
            {
                return false;
            }

            return element.HeadingPath.SequenceEqual(table.HeadingPath, StringComparer.OrdinalIgnoreCase);
        }

        public bool IsFlattenedTableContent(DocumentElement element, TableGrid? previousGrid, TableGrid? currentGrid)
        {
            if (element == null)
                return false;

            var candidateText =
                !string.IsNullOrWhiteSpace(element.Text)
                    ? element.Text
                    : !string.IsNullOrWhiteSpace(element.Markdown)
                        ? element.Markdown
                        : element.Content;

            if (string.IsNullOrWhiteSpace(candidateText))
                return false;

            return AppearsInsideTable(candidateText, previousGrid) ||
                   AppearsInsideTable(candidateText, currentGrid);
        }

        public int WordCount(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        public int GetColumnCountDifference(TableGrid? previousGrid, TableGrid? currentGrid)
        {
            if (previousGrid == null || currentGrid == null)
                return 0;

            return Math.Abs(previousGrid.ColumnCount - currentGrid.ColumnCount);
        }

        public bool HasSingleColumnCountDifference(TableGrid? previousGrid, TableGrid? currentGrid)
        {
            return GetColumnCountDifference(previousGrid, currentGrid) == 1;
        }

        public bool HasDuplicatedColumn(TableGrid? grid)
        {
            if (grid == null || grid.ColumnCount < 2)
                return false;

            var rows = new List<IReadOnlyList<string>>();

            if (grid.Header != null)
                rows.Add(grid.Header);

            rows.AddRange(grid.Rows);

            if (rows.Count == 0)
                return false;

            for (int columnA = 0; columnA < grid.ColumnCount; columnA++)
            {
                for (int columnB = columnA + 1; columnB < grid.ColumnCount; columnB++)
                {
                    int comparable = 0;
                    int identical = 0;

                    foreach (var row in rows)
                    {
                        if (columnA >= row.Count || columnB >= row.Count)
                            continue;

                        var a = NormalizeCell(row[columnA]);
                        var b = NormalizeCell(row[columnB]);

                        if (a.Length == 0 && b.Length == 0)
                            continue;

                        comparable++;

                        if (string.Equals(
                                a,
                                b,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            identical++;
                        }
                    }

                    if (comparable == 0)
                        continue;

                    /*
                     * Require every comparable row to have identical values.
                     *
                     * This deliberately avoids treating merely similar columns
                     * as duplicated extraction artifacts.
                     */
                    if (identical == comparable)
                        return true;
                }
            }

            return false;
        }

    }
}

public sealed class TableGrid
{
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }
    public int ColumnCount { get; }
    public IReadOnlyList<string>? Header { get; }
    public string? CaptionText { get; }

    public TableGrid(IReadOnlyList<List<string>> rows, int columnCount, IReadOnlyList<string>? header, string? captionText = null)
    {
        Rows = rows.Select(x => (IReadOnlyList<string>)x).ToList();
        ColumnCount = columnCount;
        Header = header;
        CaptionText = captionText;
    }
}