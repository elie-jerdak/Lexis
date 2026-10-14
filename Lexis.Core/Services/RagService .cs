using Lexis.Core.DTOs.Generation;
using Lexis.Core.DTOs.RAG;
using Lexis.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Services
{
    public sealed class RagService : IRagService
    {
        private const string InsufficientEvidenceAnswer =
            "I don't have enough information in the retrieved documents to answer that question.";

        private readonly IDocumentSearchService _documentSearchService;
        private readonly IAnswerGenerationService _answerGenerationService;
        private readonly IConversationService _conversationService;

        public RagService(IDocumentSearchService documentSearchService, IAnswerGenerationService answerGenerationService, 
            IConversationService conversationService)
        {
            _documentSearchService = documentSearchService;
            _answerGenerationService = answerGenerationService;
            _conversationService = conversationService;
        }

        public async Task<RagQueryResult> QueryAsync(int userId, RagQueryRequest request, CancellationToken cancellationToken = default)
        {   
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.Question))
            {
                throw new ArgumentException(
                    "The question cannot be empty.",
                    nameof(request));
            }

            if (request.Limit <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    "The retrieval limit must be greater than zero.");
            }

            var documentsResult = await _conversationService.GetDocumentsAsync(userId, request.ConversationId,
                cancellationToken);

            if (!documentsResult.IsSuccess)
            {
                throw new KeyNotFoundException(
                    "The conversation was not found or does not belong to this user.");
            }

            var documentIds = documentsResult.Data!
                .Select(document => document.Id)
                .ToArray();

            if (documentIds.Length == 0)
            {
                return new RagQueryResult
                {
                    Answer = "Please select at least one indexed document before asking a question.",
                    InsufficientEvidence = true,
                    Citations = Array.Empty<RagCitation>()
                };
            }

            // Step 1: Retrieve relevant chunks from the explicitly selected documents.
            var searchResults = await _documentSearchService.SearchAsync(request.Question, documentIds, request.Limit, 
                cancellationToken);

            // Step 2: Abstain if retrieval returned no evidence.
            if (searchResults.Count == 0)
            {
                return new RagQueryResult
                {
                    Answer = InsufficientEvidenceAnswer,
                    InsufficientEvidence = true,
                    Citations = Array.Empty<RagCitation>()
                };
            }

            // Step 3: Build generation context and assign request-local references.
            var contextItems = searchResults
                .Select((result, index) => new GenerationContextItem
                {
                    ReferenceId = $"C{index + 1}",
                    ChunkId = result.Chunk.Id,
                    DocumentId = result.Chunk.DocumentId,
                    Content = result.Chunk.Content,
                    StartPage = result.Chunk.StartPage,
                    EndPage = result.Chunk.EndPage,
                    Heading = result.Chunk.Heading,
                    HeadingPath = result.Chunk.HeadingPath
                })
                .ToList();

            var contextByReference = contextItems.ToDictionary(item => item.ReferenceId, StringComparer.Ordinal);

            // Step 4: Ask the configured generation implementation for an answer.
            var generationResult =
                await _answerGenerationService.GenerateAsync(
                    new AnswerGenerationRequest
                    {
                        Question = request.Question,
                        Context = contextItems
                    },
                    cancellationToken);

            // Step 5: Handle insufficient evidence or an empty generated answer.
            if (generationResult.InsufficientEvidence ||
                string.IsNullOrWhiteSpace(generationResult.Answer))
            {
                return new RagQueryResult
                {
                    Answer = InsufficientEvidenceAnswer,
                    InsufficientEvidence = true,
                    Citations = Array.Empty<RagCitation>()
                };
            }

            // Step 6: Resolve only valid references against the context we supplied.
            // Ignore invented references and duplicate references.
            var citations = new List<RagCitation>();
            var usedReferences = new HashSet<string>(StringComparer.Ordinal);

            foreach (var citationReference in generationResult.Citations)
            {
                if (citationReference is null || string.IsNullOrWhiteSpace(citationReference.ReferenceId))
                {
                    continue;
                }

                var referenceId = citationReference.ReferenceId;

                if (!usedReferences.Add(referenceId))
                {
                    continue;
                }

                if (!contextByReference.TryGetValue(referenceId, out var contextItem))
                {
                    continue;
                }

                citations.Add(new RagCitation
                {
                    ReferenceId = contextItem.ReferenceId,
                    ChunkId = contextItem.ChunkId,
                    DocumentId = contextItem.DocumentId,
                    StartPage = contextItem.StartPage,
                    EndPage = contextItem.EndPage,
                    Heading = contextItem.Heading,
                    HeadingPath = contextItem.HeadingPath
                });
            }

            // Step 7: Return the answer and validated source metadata.
            return new RagQueryResult
            {
                Answer = generationResult.Answer,
                InsufficientEvidence = false,
                Citations = citations
            };
        }
    }
}
