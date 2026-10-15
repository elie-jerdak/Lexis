using Lexis.API.Data;
using Lexis.Core.Helpers;
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
            "about", "me", "please", "tell", "explain", "define", "mean",
            "stand", "meaning", "definition"
        };

        public async Task<List<DocumentChunk>> SearchLexicallyAsync(string query, int limit, 
            IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query) || limit <= 0)
                return new List<DocumentChunk>();

            if (documentIds is null || documentIds.Count == 0)
                return new List<DocumentChunk>();

            var allTerms = Regex.Matches(query, @"[A-Za-z0-9]+")
                .Select(m => m.Value.ToLowerInvariant())
                .Distinct()
                .ToList();

            var terms = allTerms.Where(t => !LexicalStopWords.Contains(t)).ToList();
            if (terms.Count == 0)
                terms = allTerms;
            if (terms.Count == 0)
                return new List<DocumentChunk>();

            var orTsQuery = string.Join(" | ", terms);

            var definitionTerm = DefinitionQueryAnalyzer.ExtractTerm(query);
            if (definitionTerm is null)
                return await RunLexicalQueryAsync(orTsQuery, null, limit, documentIds, cancellationToken);

            var pgTerm = DefinitionQueryAnalyzer.ToPostgresPattern(definitionTerm);
            var termTokens = DefinitionQueryAnalyzer.Tokenize(definitionTerm);

            if (termTokens.Count > 0)
            {
                var strict = await RunLexicalQueryAsync(string.Join(" & ", termTokens), pgTerm, limit, documentIds, cancellationToken);

                if (strict.Count > 0)
                    return strict;
            }

            return await RunLexicalQueryAsync(orTsQuery, pgTerm, limit, documentIds, cancellationToken);
        }

        private async Task<List<DocumentChunk>> RunLexicalQueryAsync(string tsQuery, string? pgTerm, int limit,
            IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken)
        {
            var documentIdArray = documentIds.ToArray();

            return await _context.DocumentChunks
                .FromSqlInterpolated($"""
                    SELECT d.*
                    FROM "DocumentChunks" AS d
                    CROSS JOIN LATERAL (
                        SELECT to_tsquery('simple', {tsQuery}) AS search_query
                    ) AS q
                    WHERE d."DocumentId" = ANY({documentIdArray})
                      AND d."SearchVector" @@ q.search_query
                    ORDER BY
                        CASE
                            WHEN {pgTerm}::text IS NOT NULL
                             AND d."Content" ~* (
                                '(^|[\r\n])[[:blank:]]*'
                                || {pgTerm}::text
                                || '[[:blank:]]*[\r\n]+[[:blank:]]*[[:alpha:]]'
                             )
                            THEN 3

                            WHEN {pgTerm}::text IS NOT NULL
                             AND d."Content" ~* (
                                '(^|[^[:alnum:]])'
                                || {pgTerm}::text
                                || '[[:blank:]]*(:|：|=|—|–|[[:blank:]]-[[:blank:]])[[:blank:]]*[^[:space:]]'
                             )
                            THEN 2

                            WHEN {pgTerm}::text IS NOT NULL
                             AND d."Content" ~* (
                                '(^|[^[:alnum:]])'
                                || {pgTerm}::text
                                || '([[:space:]]+stands[[:space:]]+for[[:space:]]+'
                                || '|[[:space:]]+is[[:space:]]+(an?|the)[[:space:]]+'
                                || '|[[:space:]]+refers[[:space:]]+to[[:space:]]+'
                                || '|[[:space:]]+means[[:space:]]+)'
                             )
                            THEN 1

                            ELSE 0
                        END DESC,
                        ts_rank_cd(d."SearchVector", q.search_query, 1) DESC,
                        d."ChunkIndex" ASC
                    LIMIT {limit}
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }
    }
}
