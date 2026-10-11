using Lexis.Core.DTOs.Document;
using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Services
{
    public sealed class DocumentSearchService : IDocumentSearchService
    {
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorStoreService _vectorStoreService;
        private readonly IDocumentChunkRepository _chunkRepository;
        private readonly ILogger<DocumentSearchService> _logger;

        public DocumentSearchService(IEmbeddingService embeddingService, IVectorStoreService vectorStoreService,
            IDocumentChunkRepository chunkRepository, ILogger<DocumentSearchService> logger)
        {
            _embeddingService = embeddingService;
            _vectorStoreService = vectorStoreService;
            _chunkRepository = chunkRepository;
            _logger = logger;
        }

        public async Task<IReadOnlyList<DocumentSearchResult>> SearchAsync(string query, int limit = 5, Guid? documentId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException(
                    "The search query cannot be empty.",
                    nameof(query));
            }

            if (limit <= 0 || limit > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(limit),
                    "The result limit must be between 1 and 100.");
            }

            // 1. Generate the query embedding for dense retrieval.
            var embeddings = await _embeddingService.GenerateAsync(
                new[] { query },
                cancellationToken);

            if (embeddings.Count != 1 || embeddings[0] is null ||
                embeddings[0].Length == 0)
            {
                throw new InvalidOperationException(
                    "The embedding service did not return one valid query embedding.");
            }

            _logger.LogInformation(
                "[RetrievalTrace] Query={Query}; RequestedLimit={Limit}; " +
                "DocumentId={DocumentId}; EmbeddingDimensions={Dimensions}",
                query,
                limit,
                documentId,
                embeddings[0].Length);

            // 2. Retrieve a larger candidate pool for fusion.
            var searchLimit = Math.Min(
                100,
                Math.Max(limit * 5, 25));

            // 3. Run dense and lexical retrieval.
            var vectorResults = await _vectorStoreService.SearchAsync(embeddings[0], searchLimit, cancellationToken);

            var lexicalResults = await _chunkRepository.SearchLexicallyAsync(query, searchLimit, documentId, cancellationToken);

            _logger.LogInformation(
                "[HybridTrace] DenseCandidateCount={DenseCount}; " +
                "LexicalCandidateCount={LexicalCount}; SearchLimit={SearchLimit}",
                vectorResults.Count,
                lexicalResults.Count,
                searchLimit);

            // 4. Load authoritative chunks for the dense results.
            var denseChunkIds = vectorResults
                .Select(result => result.ChunkId)
                .Distinct()
                .ToList();

            var denseChunks = denseChunkIds.Count > 0
                ? await _chunkRepository.GetByIdsAsync(
                    denseChunkIds,
                    cancellationToken)
                : new List<DocumentChunk>();

            // Store each eligible chunk once.
            var chunksById = denseChunks
                .Where(chunk => !documentId.HasValue || chunk.DocumentId == documentId.Value)
                .GroupBy(chunk => chunk.Id)
                .ToDictionary(group => group.Key, group => group.First());

            // 5. Preserve the original Qdrant order when assigning dense ranks.
            var denseRankById = new Dictionary<Guid, int>();
            var denseScoreById = new Dictionary<Guid, double>();

            foreach (var vectorResult in vectorResults)
            {
                if (!chunksById.ContainsKey(vectorResult.ChunkId))
                    continue;

                if (denseRankById.ContainsKey(vectorResult.ChunkId))
                    continue;

                denseRankById[vectorResult.ChunkId] = denseRankById.Count + 1;

                denseScoreById[vectorResult.ChunkId] = vectorResult.Score;
            }

            // 6. Add eligible lexical candidates and assign lexical ranks.
            var lexicalRankById = new Dictionary<Guid, int>();

            foreach (var chunk in lexicalResults)
            {
                if (documentId.HasValue &&
                    chunk.DocumentId != documentId.Value)
                {
                    continue;
                }

                if (!chunksById.ContainsKey(chunk.Id))
                {
                    chunksById[chunk.Id] = chunk;
                }

                if (!lexicalRankById.ContainsKey(chunk.Id))
                {
                    lexicalRankById[chunk.Id] =
                        lexicalRankById.Count + 1;
                }
            }

            // 7. Fuse ranks using Reciprocal Rank Fusion.
            // Definition evidence is only used for definition-seeking queries.
            var definitionTerm = DefinitionQueryAnalyzer.ExtractTerm(query);

            const int rrfK = 60;

            var fusedResults = chunksById.Values
                .Select(chunk =>
                {
                    var hasDenseRank = denseRankById.TryGetValue(
                        chunk.Id,
                        out var denseRank);

                    var hasLexicalRank = lexicalRankById.TryGetValue(
                        chunk.Id,
                        out var lexicalRank);

                    var denseContribution = hasDenseRank
                        ? 1.0 / (rrfK + denseRank)
                        : 0.0;

                    var lexicalContribution = hasLexicalRank
                        ? 1.0 / (rrfK + lexicalRank)
                        : 0.0;

                    var rrfScore = denseContribution + lexicalContribution;

                    denseScoreById.TryGetValue(chunk.Id, out var denseScore);

                    var definitionEvidence = definitionTerm is not null
                        ? DefinitionQueryAnalyzer.GetEvidenceLevel(chunk, definitionTerm) : 0;

                    return new
                    {
                        Chunk = chunk,
                        DenseRank = hasDenseRank
                            ? (int?)denseRank
                            : null,
                        LexicalRank = hasLexicalRank
                            ? (int?)lexicalRank
                            : null,
                        DenseScore = denseScore,
                        RrfScore = rrfScore,
                        DefinitionEvidence = definitionEvidence
                    };
                })
                .OrderByDescending(result => result.DefinitionEvidence)
                .ThenByDescending(result => result.RrfScore)
                .ThenByDescending(result => result.DenseScore)
                .Take(limit)
                .ToList();

            // 8. Log the fused ranking for diagnosing retrieval quality.
            for (var i = 0; i < fusedResults.Count; i++)
            {
                var result = fusedResults[i];

                _logger.LogInformation(
                    "[HybridTrace] FinalRank={Rank}; ChunkId={ChunkId}; " +
                    "DenseRank={DenseRank}; LexicalRank={LexicalRank}; " +
                    "DenseScore={DenseScore}; RrfScore={RrfScore}; " +
                    "DefinitionTerm={DefinitionTerm}; " +
                    "DefinitionEvidence={DefinitionEvidence}; " +
                    "DocumentId={DocumentId}",
                    i + 1,
                    result.Chunk.Id,
                    result.DenseRank,
                    result.LexicalRank,
                    result.DenseScore,
                    result.RrfScore,
                    definitionTerm,
                    result.DefinitionEvidence,
                    result.Chunk.DocumentId);
            }

            _logger.LogInformation(
                "[HybridTrace] FinalResultCount={Count}; RequestedLimit={Limit}",
                fusedResults.Count,
                limit);

            // Score now represents the fused RRF ranking score, not cosine similarity.
            return fusedResults
                .Select(result => new DocumentSearchResult
                {
                    Chunk = result.Chunk,
                    Score = (float)result.RrfScore
                })
                .ToList();
        }
    
    }
}
