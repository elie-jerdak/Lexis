using Lexis.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Lexis.Core.Helpers
{
    public static class DefinitionQueryAnalyzer
    {
        public static string? ExtractTerm(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            // Capture the complete term, not just a single acronym or word.
            // Order the more specific patterns before the general ones.
            var patterns = new[]
            {
                @"\bwhat\s+does\s+(.+?)\s+stand\s+for\b",
                @"\bwhat\s+does\s+(.+?)\s+mean\b",
                @"\bwhat\s+is\s+meant\s+by\s+(.+?)(?:[?.!]|$)",
                @"\bwhat\s+is\s+the\s+meaning\s+of\s+(.+?)(?:[?.!]|$)",
                @"\b(?:meaning\s+of|definition\s+of|full\s+form\s+of|expand)\s+(.+?)(?:[?.!]|$)",
                @"\bdefine\s+(.+?)(?:[?.!]|$)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(
                    query,
                    pattern,
                    RegexOptions.IgnoreCase |
                    RegexOptions.CultureInvariant);

                if (!match.Success)
                    continue;

                var term = match.Groups[1].Value.Trim();

                // Remove surrounding punctuation while preserving punctuation
                // inside multiword terms.
                term = term.Trim(
                    ' ', '\t', '\r', '\n',
                    '?', '!', '.', ':', ';',
                    '"', '\'', '(', ')', '[', ']');

                if (!string.IsNullOrWhiteSpace(term))
                    return term;
            }

            return null;
        }

        public static int GetEvidenceLevel(
            DocumentChunk chunk,
            string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return 0;

            var content = chunk.Content ?? string.Empty;
            var heading = chunk.Heading ?? string.Empty;
            var headingPath = chunk.HeadingPath ?? string.Empty;

            // Allow any number of spaces between words in a multiword term.
            var termPattern = string.Join(
                @"\s+",
                term.Split(
                        (char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries)
                    .Select(Regex.Escape));

            if (string.IsNullOrWhiteSpace(termPattern))
                return 0;

            // Level 3: A matching term heading within a definition-oriented
            // section, with actual explanatory content in the chunk.
            //
            // Example:
            // Heading:     1.5.1 Common Criteria certificate
            // HeadingPath: ... > 1.5 Terms and definitions > ...
            // Content:     Formal recognition by the NIAP validation body...
            var normalizedTerm = NormalizeHeading(term);
            var normalizedHeading = NormalizeHeading(heading);

            var headingMatchesTerm =
                normalizedHeading == normalizedTerm;

            var pathSegments = headingPath
                .Split('>')
                .Select(NormalizeHeading)
                .Where(segment => segment.Length > 0)
                .ToArray();

            var finalPathSegmentMatchesTerm =
                pathSegments.Length > 0 &&
                pathSegments[^1] == normalizedTerm;

            var hasDefinitionSectionContext = pathSegments.Any(
                segment =>
                    segment.Contains("terms and definitions") ||
                    segment.Contains("term and definition") ||
                    segment.Contains("definitions") ||
                    segment == "glossary" ||
                    segment.Contains("abbreviations and acronyms") ||
                    segment == "terminology");

            if (!string.IsNullOrWhiteSpace(content) &&
                (headingMatchesTerm || finalPathSegmentMatchesTerm) &&
                hasDefinitionSectionContext)
            {
                return 3;
            }

            // Level 3: Glossary-style entry where the term occupies its own
            // line and the next non-empty line starts the definition.
            if (Regex.IsMatch(
                content,
                $@"(?im)^[ \t]*{termPattern}[ \t]*" +
                @"(?:\r?\n[ \t]*)+\r?\n?[ \t]*[A-Za-z]",
                RegexOptions.CultureInvariant))
            {
                return 3;
            }

            // Level 2: The term is the heading of a chunk containing content.
            // This also handles definition headings outside explicitly named
            // glossary/definition sections.
            if (!string.IsNullOrWhiteSpace(content) &&
                (headingMatchesTerm || finalPathSegmentMatchesTerm))
            {
                return 2;
            }

            // Level 2: TERM: definition, TERM = definition, and similar forms.
            if (Regex.IsMatch(
                content,
                $@"(?<![A-Za-z0-9]){termPattern}" +
                @"[ \t]*(?:[:：—–-]|=)[ \t]*\S",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant))
            {
                return 2;
            }

            // Level 1: Explicit definition language in prose.
            if (Regex.IsMatch(
                content,
                $@"(?<![A-Za-z0-9]){termPattern}" +
                @"(?:\s+stands\s+for\s+" +
                @"|\s+is\s+(?:an?|the)\s+" +
                @"|\s+refers\s+to\s+" +
                @"|\s+means\s+)",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant))
            {
                return 1;
            }

            return 0;
        }

        private static string NormalizeHeading(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            // Remove a leading section number, e.g. "1.5.1 ".
            var withoutSectionNumber = Regex.Replace(
                value.Trim(),
                @"^\s*\d+(?:\.\d+)*\.?\s+",
                string.Empty);

            // Make heading comparison insensitive to spacing and punctuation.
            return Regex.Replace(
                    withoutSectionNumber.ToLowerInvariant(),
                    @"[^a-z0-9]+",
                    " ")
                .Trim();
        }
    }
}