
using Lexis.API.Data;
using Lexis.Core.DTOs.Document;
using Lexis.Core.DTOs.Generation;
using Lexis.Core.DTOs.RAG;
using Lexis.Core.Enums;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Lexis.Core.Services
{
    public sealed class RagService : IRagService
    {
        private const string InsufficientEvidenceAnswer =
            "I don't have enough information in the retrieved documents to answer that question.";

        private const string RequestFailedAnswer =
            "I couldn't complete this request due to an internal error.";

        private readonly IDocumentSearchService _documentSearchService;
        private readonly IAnswerGenerationService _answerGenerationService;
        private readonly IConversationService _conversationService;
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<RagService> _logger;

        public RagService(IDocumentSearchService documentSearchService, IAnswerGenerationService answerGenerationService,
            IConversationService conversationService, ApplicationDbContext dbContext, ILogger<RagService> logger)
        {
            _documentSearchService = documentSearchService;
            _answerGenerationService = answerGenerationService;
            _conversationService = conversationService;
            _dbContext = dbContext;
            _logger = logger;
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

            if (request.Limit <= 0 || request.Limit > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    "The retrieval limit must be between 1 and 100.");
            }

            var requestTime = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();

            // Validate that the conversation belongs to this user.
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

            // Persist the user's message before retrieval or generation.
            var userMessage = new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = request.ConversationId,
                Content = request.Question,
                Role = MessageRole.User,
                InputTokens = 0,
                OutputTokens = 0,
                IsError = false,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Messages.Add(userMessage);

            await _dbContext.SaveChangesAsync(cancellationToken);

            string answer;
            bool insufficientEvidence;
            int inputTokens = 0;
            int outputTokens = 0;

            var citations = new List<RagCitation>();

            // Maps generation references (C1, C2, ...) to their original
            // search results so persistence uses the real retrieval score
            // and source content.
            var sourceByReference =
                new Dictionary<string, DocumentSearchResult>(
                    StringComparer.Ordinal);

            try
            {
                // No documents selected: persist an explanatory response.
                if (documentIds.Length == 0)
                {
                    answer = "Please select at least one indexed document before asking a question.";
                    insufficientEvidence = true;
                }
                else
                {
                    // Step 1: retrieve chunks from selected documents.
                    var searchResults = await _documentSearchService.SearchAsync(request.Question, documentIds, request.Limit,
                            cancellationToken);

                    // Step 2: abstain if retrieval found no evidence.
                    if (searchResults.Count == 0)
                    {
                        answer = InsufficientEvidenceAnswer;
                        insufficientEvidence = true;
                    }
                    else
                    {
                        // Step 3: build generation context and assign
                        // request-local citation references.
                        var contextItems = searchResults
                            .Select((result, index) =>
                            {
                                var item = new GenerationContextItem
                                {
                                    ReferenceId = $"C{index + 1}",
                                    ChunkId = result.Chunk.Id,
                                    DocumentId = result.Chunk.DocumentId,
                                    Content = result.Chunk.Content,
                                    StartPage = result.Chunk.StartPage,
                                    EndPage = result.Chunk.EndPage,
                                    Heading = result.Chunk.Heading,
                                    HeadingPath = result.Chunk.HeadingPath
                                };

                                sourceByReference[item.ReferenceId] = result;

                                return item;
                            })
                            .ToList();

                        var contextByReference = contextItems
                            .ToDictionary(
                                item => item.ReferenceId,
                                StringComparer.Ordinal);

                        // Step 4: generate an answer.
                        var generationResult =
                            await _answerGenerationService.GenerateAsync(
                                new AnswerGenerationRequest
                                {
                                    Question = request.Question,
                                    Context = contextItems
                                },
                                cancellationToken);

                        inputTokens = generationResult.InputTokens;
                        outputTokens = generationResult.OutputTokens;

                        _logger.LogInformation(
                            "RAG generated answer. InsufficientEvidence={InsufficientEvidence}; " +
                            "InputTokens={InputTokens}; OutputTokens={OutputTokens}",
                            generationResult.InsufficientEvidence,
                            inputTokens,
                            outputTokens);

                        // Step 5: normalize abstentions and empty answers.
                        if (generationResult.InsufficientEvidence ||
                            string.IsNullOrWhiteSpace(generationResult.Answer))
                        {
                            answer = InsufficientEvidenceAnswer;
                            insufficientEvidence = true;
                        }
                        else
                        {
                            answer = generationResult.Answer;
                            insufficientEvidence = false;

                            // Step 6: retain only valid, unique references.
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
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Best-effort persistence of the failed request. Do not let
                // a secondary persistence failure hide the original error.
                try
                {
                    await PersistFailedRequestAsync(
                        userId,
                        request.ConversationId,
                        requestTime,
                        stopwatch.ElapsedMilliseconds,
                        inputTokens,
                        outputTokens);
                }
                catch (Exception persistenceException)
                {
                    _logger.LogError(
                        persistenceException,
                        "Could not persist the failure record for RAG request. " +
                        "ConversationId={ConversationId}; UserId={UserId}",
                        request.ConversationId,
                        userId);
                }

                _logger.LogError(
                    ex,
                    "RAG request failed. ConversationId={ConversationId}; UserId={UserId}",
                    request.ConversationId,
                    userId);

                throw;
            }

            stopwatch.Stop();

            // Step 7: persist the assistant response, validated citations,
            // and usage log.
            await PersistCompletedRequestAsync(
                userId,
                request.ConversationId,
                requestTime,
                stopwatch.ElapsedMilliseconds,
                answer,
                insufficientEvidence,
                citations,
                sourceByReference,
                inputTokens,
                outputTokens,
                cancellationToken);

            return new RagQueryResult
            {
                Answer = answer,
                InsufficientEvidence = insufficientEvidence,
                Citations = citations
            };
        }

        private async Task PersistCompletedRequestAsync(int userId, Guid conversationId, DateTime requestTime,
            long elapsedMilliseconds, string answer, bool insufficientEvidence, IReadOnlyList<RagCitation> citations,
            IReadOnlyDictionary<string, DocumentSearchResult> sourceByReference, int inputTokens, int outputTokens,
            CancellationToken cancellationToken)
        {
            var assistantMessage = new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                Content = answer,
                Role = MessageRole.Assistant,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                IsError = false,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Messages.Add(assistantMessage);

            // Abstentions intentionally have no citations.
            if (!insufficientEvidence)
            {
                foreach (var citation in citations)
                {
                    if (!sourceByReference.TryGetValue(
                            citation.ReferenceId,
                            out var source))
                    {
                        continue;
                    }

                    _dbContext.MessageCitations.Add(new MessageCitation
                    {
                        Id = Guid.NewGuid(),
                        MessageId = assistantMessage.Id,
                        DocumentChunkId = citation.ChunkId,

                        // This is the hybrid RRF score, not cosine similarity.
                        SimilarityScore = (float)source.Score,

                        // Preserve the cited source text at response time.
                        TextContextSnapshot = source.Chunk.Content,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            _dbContext.UsageLogs.Add(new UsageLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                MessageId = assistantMessage.Id,
                RequestTime = requestTime,
                ResponseTimeMs = ToResponseTimeMilliseconds(
                    elapsedMilliseconds),
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CacheHit = false
            });

            // The assistant message, its citations, and usage log are saved
            // together in one SaveChanges operation.
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Persisted RAG response. ConversationId={ConversationId}; " +
                "AssistantMessageId={MessageId}; CitationCount={CitationCount}; " +
                "InputTokens={InputTokens}; OutputTokens={OutputTokens}",
                conversationId,
                assistantMessage.Id,
                insufficientEvidence ? 0 : citations.Count,
                inputTokens,
                outputTokens);
        }

        private async Task PersistFailedRequestAsync(int userId, Guid conversationId, DateTime requestTime, long elapsedMilliseconds,
            int inputTokens, int outputTokens)
        {
            var assistantMessage = new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                Content = RequestFailedAnswer,
                Role = MessageRole.Assistant,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                IsError = true,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Messages.Add(assistantMessage);

            _dbContext.UsageLogs.Add(new UsageLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                MessageId = assistantMessage.Id,
                RequestTime = requestTime,
                ResponseTimeMs = ToResponseTimeMilliseconds(
                    elapsedMilliseconds),
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CacheHit = false
            });

            // Use an independent token so request cancellation doesn't
            // automatically prevent best-effort failure logging.
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }

        private static int ToResponseTimeMilliseconds(long elapsedMilliseconds)
        {
            return (int)Math.Clamp(
                elapsedMilliseconds,
                0L,
                int.MaxValue);
        }
    }
}
