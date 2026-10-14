using Lexis.Core.Models;
using System.Text.RegularExpressions;

namespace Lexis.Core.Helpers
{
    public static class DefinitionQueryAnalyzer
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
        private const int MaxTermWords = 6;

        private const RegexOptions Opts =
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly Regex[] TermPatterns = new[]
        {
        @"\bwhat\s+does\s+(.+?)\s+stand\s+for\b",
        @"\bwhat\s+does\s+(.+?)\s+mean\b",
        @"\bwhat\s+is\s+meant\s+by\s+(.+?)(?:[?.!]|$)",
        @"\bwhat\s+is\s+the\s+meaning\s+of\s+(.+?)(?:[?.!]|$)",
        @"\b(?:meaning\s+of|definition\s+of|full\s+form\s+of|expand\s+(?:the\s+)?(?:acronym|abbreviation))\s+(.+?)(?:[?.!]|$)",
        @"\bdefine\s+(.+?)(?:[?.!]|$)",
        // Last and most general; the word cap in CleanTerm keeps it from
        // matching long questions like "what is the best way to ...".
        @"^\s*what\s+(?:is|are)\s+(.+?)\s*[?.!]?\s*$"
    }
        .Select(p => new Regex(p, Opts, RegexTimeout))
        .ToArray();

        private static readonly Regex LeadingArticle =
            new(@"^(?:the|a|an)\s+", Opts, RegexTimeout);

        private static readonly Regex ParenGroup =
            new(@"\(([^)]*)\)", RegexOptions.CultureInvariant, RegexTimeout);

        public static string? ExtractTerm(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            try
            {
                foreach (var pattern in TermPatterns)
                {
                    var match = pattern.Match(query);
                    if (!match.Success)
                        continue;

                    var term = CleanTerm(match.Groups[1].Value);
                    if (term is not null)
                        return term;
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }

            return null;
        }

        private static string? CleanTerm(string raw)
        {
            var term = raw.Trim(' ', '\t', '\r', '\n', '?', '!', '.', ':', ';', '"', '\'');

            // Only strip brackets when they are unbalanced, so
            // "Target of Evaluation (TOE)" keeps its closing paren.
            if (term.Count(c => c == '(') != term.Count(c => c == ')'))
                term = term.Trim('(', ')');
            if (term.Count(c => c == '[') != term.Count(c => c == ']'))
                term = term.Trim('[', ']');

            term = LeadingArticle.Replace(term, string.Empty).Trim();

            if (term.Length == 0)
                return null;

            var wordCount = term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            return wordCount > MaxTermWords ? null : term;
        }

        /// <summary>Lowercased alphanumeric tokens, for building a tsquery.</summary>
        public static List<string> Tokenize(string term) =>
            Regex.Matches(term, @"[A-Za-z0-9]+")
                .Select(m => m.Value.ToLowerInvariant())
                .Distinct()
                .ToList();

        /// <summary>
        /// Escapes the term for use inside a Postgres regex (~*), with flexible
        /// whitespace between words.
        /// </summary>
        public static string ToPostgresPattern(string term) =>
            string.Join(
                "[[:space:]]+",
                term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => Regex.Replace(w, @"[\\.^$|?*+()\[\]{}]", @"\$0")));

        public static int GetEvidenceLevel(DocumentChunk chunk, string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return 0;

            var content = chunk.Content ?? string.Empty;
            var heading = chunk.Heading ?? string.Empty;
            var headingPath = chunk.HeadingPath ?? string.Empty;

            var termPattern = string.Join(
                @"\s+",
                term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Regex.Escape));

            if (string.IsNullOrWhiteSpace(termPattern))
                return 0;

            try
            {
                var normalizedTerm = NormalizeHeading(term);

                var pathSegments = headingPath
                    .Split('>')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToArray();

                var headingMatchesTerm = HeadingMatches(heading, normalizedTerm);
                var finalPathSegmentMatchesTerm =
                    pathSegments.Length > 0 &&
                    HeadingMatches(pathSegments[^1], normalizedTerm);

                var hasDefinitionSectionContext = pathSegments
                    .Select(NormalizeHeading)
                    .Any(segment =>
                        segment.Contains("terms and definitions") ||
                        segment.Contains("term and definition") ||
                        segment.Contains("definitions") ||
                        segment == "glossary" ||
                        segment.Contains("abbreviations and acronyms") ||
                        segment == "terminology");

                var termIsHeading = headingMatchesTerm || finalPathSegmentMatchesTerm;

                // Level 3: term heading inside a definitions/glossary section.
                if (!string.IsNullOrWhiteSpace(content) &&
                    termIsHeading &&
                    hasDefinitionSectionContext)
                    return 3;

                // Level 3: glossary layout, term alone on a line, definition below.
                if (Regex.IsMatch(
                        content,
                        $@"^[ \t]*{termPattern}[ \t]*\r?\n(?:[ \t]*\r?\n)*[ \t]*[A-Za-z]",
                        Opts | RegexOptions.Multiline,
                        RegexTimeout))
                    return 3;

                // Level 2: the term is the heading of a chunk that has content.
                if (!string.IsNullOrWhiteSpace(content) && termIsHeading)
                    return 2;

                // Level 2: "TERM: ...", "TERM = ...", "TERM - ..." (dash needs spaces
                // around it, so SHA-256 does not count as a definition of SHA).
                if (Regex.IsMatch(
                        content,
                        $@"(?<![A-Za-z0-9]){termPattern}" +
                        @"[ \t]*(?:[:：=]|[ \t][—–-][ \t]|[—–])[ \t]*\S",
                        Opts,
                        RegexTimeout))
                    return 2;

                // Level 1: explicit definition language in prose.
                if (Regex.IsMatch(
                        content,
                        $@"(?<![A-Za-z0-9]){termPattern}" +
                        @"(?:\s+stands\s+for\s+" +
                        @"|\s+is\s+(?:an?|the)\s+" +
                        @"|\s+refers\s+to\s+" +
                        @"|\s+means\s+)",
                        Opts,
                        RegexTimeout))
                    return 1;
            }
            catch (RegexMatchTimeoutException)
            {
            }

            return 0;
        }

        // Matches "TOE", "Target of Evaluation (TOE)" and "TOE (Target of Evaluation)".
        private static bool HeadingMatches(string rawHeading, string normalizedTerm)
        {
            if (string.IsNullOrWhiteSpace(rawHeading) || normalizedTerm.Length == 0)
                return false;

            if (NormalizeHeading(rawHeading) == normalizedTerm)
                return true;

            foreach (Match m in ParenGroup.Matches(rawHeading))
                if (NormalizeHeading(m.Groups[1].Value) == normalizedTerm)
                    return true;

            return NormalizeHeading(ParenGroup.Replace(rawHeading, " ")) == normalizedTerm;
        }

        private static string NormalizeHeading(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var withoutSectionNumber = Regex.Replace(
                value.Trim(), @"^\s*\d+(?:\.\d+)*\.?\s+", string.Empty);

            return Regex.Replace(
                    withoutSectionNumber.ToLowerInvariant(), @"[^a-z0-9]+", " ")
                .Trim();
        }
    }
}