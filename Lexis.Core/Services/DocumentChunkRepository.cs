using Lexis.API.Data;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Lexis.Core.Services
{
    public class DocumentChunkRepository : IDocumentChunkRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DocumentChunkRepository> _logger;

        public DocumentChunkRepository(ApplicationDbContext context, ILogger<DocumentChunkRepository> logger)
        {
            _context = context;
            _logger = logger;
        }


        public async Task AddRangeAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken)
        {
            if (chunks == null)
                throw new ArgumentNullException(nameof(chunks));

            var chunkList = chunks.ToList();

            if (chunkList.Count == 0)
                return;

            await _context.DocumentChunks.AddRangeAsync(chunkList, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<List<DocumentChunk>> GetByIdsAsync(IEnumerable<Guid> chunkIds, CancellationToken cancellationToken)
        {
            var idList = chunkIds.ToList();

            if (idList.Count == 0)
                return [];

            var chunks = await _context.DocumentChunks
                .Where(c => idList.Contains(c.Id))
                .ToListAsync(cancellationToken);

            return idList
                .Join(chunks, id => id, chunk => chunk.Id,(_, chunk) => chunk)
                .ToList();
        }

        public async Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken)
        {
            await _context.DocumentChunks
                .Where(c => c.DocumentId == documentId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        private static readonly HashSet<string> LexicalStopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "what", "does", "do", "is", "are", "the", "a", "an", "of", "for", "to", "in", "on",
            "about", "me", "please", "tell", "explain", "define", "mean"
        };

        public async Task<List<DocumentChunk>> SearchLexicallyAsync(string query, int limit, Guid? documentId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query) || limit <= 0)
                return new List<DocumentChunk>();

            var allTerms = Regex.Matches(query, @"[A-Za-z0-9]+")
                .Select(match => match.Value.ToLowerInvariant())
                .Distinct()
                .ToList();

            var terms = allTerms
                .Where(term => !LexicalStopWords.Contains(term))
                .ToList();

            // If the query contains only stop words, fall back to its original terms.
            if (terms.Count == 0)
                terms = allTerms;

            if (terms.Count == 0)
                return new List<DocumentChunk>();

            // Tokens contain only letters and digits, so they cannot inject tsquery syntax.
            var tsQuery = string.Join(" | ", terms);

            // Populated only when the query explicitly asks for a definition.
            var definitionTerm = ExtractDefinitionTerm(query);

            return await _context.DocumentChunks
                .FromSqlInterpolated($"""
                    SELECT d.*
                    FROM "DocumentChunks" AS d
                    CROSS JOIN LATERAL (
                        SELECT to_tsvector(
                            'simple',
                            concat_ws(
                                ' ',
                                d."Content",
                                d."Heading",
                                d."HeadingPath"
                            )
                        ) AS search_vector
                    ) AS v
                    CROSS JOIN LATERAL (
                        SELECT to_tsquery('simple', {tsQuery}) AS search_query
                    ) AS q
                    WHERE ({documentId} IS NULL OR d."DocumentId" = {documentId})
                      AND v.search_vector @@ q.search_query
                    ORDER BY
                        CASE
                            -- Strongest signal:
                            -- the requested term is on its own line,
                            -- followed by the definition on the next line.
                            WHEN {definitionTerm}::text IS NOT NULL
                             AND d."Content" ~* (
                                '(^|[\r\n])[[:blank:]]*'
                                || {definitionTerm}::text
                                || '[[:blank:]]*[\r\n]+[[:blank:]]*[[:alpha:]]'
                             )
                            THEN 3

                            -- The requested term is explicitly separated
                            -- from its definition by punctuation.
                            WHEN {definitionTerm}::text IS NOT NULL
                             AND d."Content" ~* (
                                '(^|[^[:alnum:]])'
                                || {definitionTerm}::text
                                || '[[:blank:]]*([:：—–-]|=)[[:blank:]]*'
                             )
                            THEN 2

                            -- The term is explicitly connected to a definition
                            -- in ordinary prose.
                            WHEN {definitionTerm}::text IS NOT NULL
                             AND d."Content" ~* (
                                '(^|[^[:alnum:]])'
                                || {definitionTerm}::text
                                || '([[:space:]]+stands[[:space:]]+for[[:space:]]+'
                                || '|[[:space:]]+is[[:space:]]+(an?|the)[[:space:]]+'
                                || '|[[:space:]]+refers[[:space:]]+to[[:space:]]+'
                                || '|[[:space:]]+means[[:space:]]+)'
                             )
                            THEN 1

                            ELSE 0
                        END DESC,
                        ts_rank_cd(v.search_vector, q.search_query) DESC,
                        d."ChunkIndex" ASC
                    LIMIT {limit}
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }
        private static string? ExtractDefinitionTerm(string query)
        {
            var patterns = new[]
            {
                // "What does OWM stand for?"
                @"\bwhat\s+does\s+([A-Za-z0-9]{2,})\s+stand\s+for\b",

                // "What does OWM mean?"
                @"\bwhat\s+does\s+([A-Za-z0-9]{2,})\s+mean\b",

                // "What is the meaning of OWM?"
                // "Meaning of OWM", "definition of OWM"
                // "Full form of OWM", "expand OWM"
                @"\b(?:what\s+is\s+the\s+meaning\s+of|meaning\s+of|definition\s+of|full\s+form\s+of|expand)\s+([A-Za-z0-9]{2,})\b",

                // "Define OWM"
                @"\bdefine\s+([A-Za-z0-9]{2,})\b"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(
                    query,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                if (match.Success)
                    return match.Groups[1].Value;
            }

            return null;
        }
    }
}
