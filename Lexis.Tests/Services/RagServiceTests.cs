
using Lexis.Core.DTOs.Conversation;
using Lexis.Core.DTOs.Document;
using Lexis.Core.DTOs.Generation;
using Lexis.Core.DTOs.RAG;
using Lexis.Core.Enums;
using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Lexis.Core.Services;

namespace Lexis.Tests.Services
{
    public class RagServiceTests
    {
        private const int TestUserId = 1;

        [Fact]
        public async Task QueryAsync_ValidCitation_ReturnsResolvedSourceMetadata()
        {
            // Arrange
            var conversationId = Guid.NewGuid();
            var chunkId = Guid.NewGuid();
            var documentId = Guid.NewGuid();

            var chunk = CreateChunk(
                chunkId,
                documentId,
                "OWM — Office of Weights and Measures.",
                15,
                17,
                "1.4 Abbreviations and Acronyms");

            var search = new FakeDocumentSearchService(
                CreateSearchResult(chunk));

            var generator = new FakeAnswerGenerationService
            {
                Result = new AnswerGenerationResult
                {
                    Answer = "OWM stands for Office of Weights and Measures.",
                    Citations =
                    [
                        new AnswerCitationReference
                        {
                            ReferenceId = "C1"
                        }
                    ]
                }
            };

            var sut = CreateSut(search, generator, documentId);

            var request = new RagQueryRequest
            {
                ConversationId = conversationId,
                Question = "What does OWM stand for?",
                Limit = 5
            };

            // Act
            var result = await sut.QueryAsync(TestUserId, request);

            // Assert
            Assert.False(result.InsufficientEvidence);
            Assert.Equal(
                "OWM stands for Office of Weights and Measures.",
                result.Answer);

            var citation = Assert.Single(result.Citations);

            Assert.Equal("C1", citation.ReferenceId);
            Assert.Equal(chunkId, citation.ChunkId);
            Assert.Equal(documentId, citation.DocumentId);
            Assert.Equal(15, citation.StartPage);
            Assert.Equal(17, citation.EndPage);
            Assert.Equal(
                "1.4 Abbreviations and Acronyms",
                citation.Heading);

            Assert.NotNull(generator.LastRequest);
            Assert.Equal(
                request.Question,
                generator.LastRequest.Question);

            Assert.Equal(
                "C1",
                Assert.Single(generator.LastRequest.Context).ReferenceId);

            Assert.Equal(1, search.CallCount);
            Assert.Equal(
                new[] { documentId },
                search.LastDocumentIds);
        }

        [Fact]
        public async Task QueryAsync_NoSearchResults_ReturnsInsufficientEvidence()
        {
            // Arrange
            var conversationId = Guid.NewGuid();
            var documentId = Guid.NewGuid();

            var search = new FakeDocumentSearchService(
                Array.Empty<DocumentSearchResult>());

            var generator = new FakeAnswerGenerationService();

            var sut = CreateSut(search, generator, documentId);

            // Act
            var result = await sut.QueryAsync(
                TestUserId,
                new RagQueryRequest
                {
                    ConversationId = conversationId,
                    Question = "What does OWM stand for?"
                });

            // Assert
            Assert.True(result.InsufficientEvidence);
            Assert.NotEmpty(result.Answer);
            Assert.Empty(result.Citations);

            // Generation must not run when retrieval returns nothing.
            Assert.Equal(0, generator.CallCount);
            Assert.Equal(1, search.CallCount);
        }

        [Fact]
        public async Task QueryAsync_InvalidAndDuplicateReferences_AreDiscarded()
        {
            // Arrange
            var conversationId = Guid.NewGuid();

            var chunk1 = CreateChunk(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "OWM means Office of Weights and Measures.",
                15,
                15,
                "Abbreviations");

            var chunk2 = CreateChunk(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Additional supporting information.",
                16,
                16,
                "Program Summary");

            var search = new FakeDocumentSearchService(
                CreateSearchResult(chunk1),
                CreateSearchResult(chunk2));

            var generator = new FakeAnswerGenerationService
            {
                Result = new AnswerGenerationResult
                {
                    Answer = "OWM means Office of Weights and Measures.",
                    Citations =
                    [
                        new AnswerCitationReference
                        {
                            ReferenceId = "C1"
                        },
                        new AnswerCitationReference
                        {
                            ReferenceId = "C1"
                        },
                        new AnswerCitationReference
                        {
                            ReferenceId = "C999"
                        },
                        new AnswerCitationReference
                        {
                            ReferenceId = "C2"
                        }
                    ]
                }
            };

            var sut = CreateSut(
                search,
                generator,
                chunk1.DocumentId,
                chunk2.DocumentId);

            // Act
            var result = await sut.QueryAsync(
                TestUserId,
                new RagQueryRequest
                {
                    ConversationId = conversationId,
                    Question = "What does OWM stand for?",
                    Limit = 5
                });

            // Assert
            Assert.False(result.InsufficientEvidence);

            Assert.Equal(2, result.Citations.Count);
            Assert.Equal("C1", result.Citations[0].ReferenceId);
            Assert.Equal(chunk1.Id, result.Citations[0].ChunkId);
            Assert.Equal("C2", result.Citations[1].ReferenceId);
            Assert.Equal(chunk2.Id, result.Citations[1].ChunkId);

            Assert.DoesNotContain(
                result.Citations,
                citation => citation.ReferenceId == "C999");
        }

        [Fact]
        public async Task QueryAsync_GeneratorReportsInsufficientEvidence_ReturnsAbstention()
        {
            // Arrange
            var conversationId = Guid.NewGuid();

            var chunk = CreateChunk(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Some unrelated document content.",
                10,
                10,
                "Introduction");

            var search = new FakeDocumentSearchService(
                CreateSearchResult(chunk));

            var generator = new FakeAnswerGenerationService
            {
                Result = new AnswerGenerationResult
                {
                    Answer = "I cannot answer from the available evidence.",
                    InsufficientEvidence = true,
                    Citations =
                    [
                        new AnswerCitationReference
                        {
                            ReferenceId = "C1"
                        }
                    ]
                }
            };

            var sut = CreateSut(search, generator, chunk.DocumentId);

            // Act
            var result = await sut.QueryAsync(
                TestUserId,
                new RagQueryRequest
                {
                    ConversationId = conversationId,
                    Question = "What is the answer?"
                });

            // Assert
            Assert.True(result.InsufficientEvidence);
            Assert.NotEmpty(result.Answer);
            Assert.Empty(result.Citations);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task QueryAsync_EmptyQuestion_ThrowsArgumentException(
            string question)
        {
            // Arrange
            var search = new FakeDocumentSearchService(
                Array.Empty<DocumentSearchResult>());

            var generator = new FakeAnswerGenerationService();

            var sut = CreateSut(
                search,
                generator,
                Guid.NewGuid());

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                () => sut.QueryAsync(
                    TestUserId,
                    new RagQueryRequest
                    {
                        ConversationId = Guid.NewGuid(),
                        Question = question
                    }));

            Assert.Equal(0, search.CallCount);
            Assert.Equal(0, generator.CallCount);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task QueryAsync_InvalidLimit_ThrowsArgumentOutOfRangeException(
            int limit)
        {
            // Arrange
            var search = new FakeDocumentSearchService(
                Array.Empty<DocumentSearchResult>());

            var generator = new FakeAnswerGenerationService();

            var sut = CreateSut(
                search,
                generator,
                Guid.NewGuid());

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => sut.QueryAsync(
                    TestUserId,
                    new RagQueryRequest
                    {
                        ConversationId = Guid.NewGuid(),
                        Question = "What does OWM stand for?",
                        Limit = limit
                    }));

            Assert.Equal(0, search.CallCount);
            Assert.Equal(0, generator.CallCount);
        }

        [Fact]
        public async Task QueryAsync_NoSelectedDocuments_ReturnsInsufficientEvidence()
        {
            // Arrange
            var search = new FakeDocumentSearchService(
                CreateSearchResult(
                    CreateChunk(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "OWM means Office of Weights and Measures.",
                        15,
                        15,
                        "Abbreviations")));

            var generator = new FakeAnswerGenerationService();

            var sut = CreateSut(
                search,
                generator);

            // Act
            var result = await sut.QueryAsync(
                TestUserId,
                new RagQueryRequest
                {
                    ConversationId = Guid.NewGuid(),
                    Question = "What does OWM stand for?"
                });

            // Assert
            Assert.True(result.InsufficientEvidence);
            Assert.Empty(result.Citations);

            // An empty selection must stop before retrieval or generation.
            Assert.Equal(0, search.CallCount);
            Assert.Equal(0, generator.CallCount);
        }

        [Fact]
        public async Task QueryAsync_PassesRetrievedChunksToGeneratorWithReferences()
        {
            // Arrange
            var conversationId = Guid.NewGuid();

            var chunk1 = CreateChunk(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "OWM means Office of Weights and Measures.",
                15,
                17,
                "Abbreviations");

            var chunk2 = CreateChunk(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Additional information about OWM.",
                18,
                18,
                "Program Summary");

            var search = new FakeDocumentSearchService(
                CreateSearchResult(chunk1),
                CreateSearchResult(chunk2));

            var generator = new FakeAnswerGenerationService
            {
                Result = new AnswerGenerationResult
                {
                    Answer = "OWM means Office of Weights and Measures.",
                    Citations =
                    [
                        new AnswerCitationReference
                        {
                            ReferenceId = "C1"
                        }
                    ]
                }
            };

            var sut = CreateSut(
                search,
                generator,
                chunk1.DocumentId,
                chunk2.DocumentId);

            // Act
            var result = await sut.QueryAsync(
                TestUserId,
                new RagQueryRequest
                {
                    ConversationId = conversationId,
                    Question = "What does OWM stand for?",
                    Limit = 5
                });

            // Assert
            Assert.Equal(1, generator.CallCount);
            Assert.NotNull(generator.LastRequest);

            Assert.Equal(
                "What does OWM stand for?",
                generator.LastRequest.Question);

            Assert.Equal(2, generator.LastRequest.Context.Count);

            var first = generator.LastRequest.Context[0];
            var second = generator.LastRequest.Context[1];

            Assert.Equal("C1", first.ReferenceId);
            Assert.Equal(chunk1.Id, first.ChunkId);
            Assert.Equal(chunk1.DocumentId, first.DocumentId);
            Assert.Equal(chunk1.Content, first.Content);
            Assert.Equal(15, first.StartPage);
            Assert.Equal(17, first.EndPage);

            Assert.Equal("C2", second.ReferenceId);
            Assert.Equal(chunk2.Id, second.ChunkId);

            Assert.Single(result.Citations);
            Assert.Equal(chunk1.Id, result.Citations[0].ChunkId);
        }

        private static RagService CreateSut(
            FakeDocumentSearchService search,
            FakeAnswerGenerationService generator,
            params Guid[] documentIds)
        {
            return new RagService(
                search,
                generator,
                new FakeConversationService(documentIds));
        }

        private static DocumentChunk CreateChunk(
            Guid chunkId,
            Guid documentId,
            string content,
            int startPage,
            int endPage,
            string heading)
        {
            return new DocumentChunk
            {
                Id = chunkId,
                DocumentId = documentId,
                Content = content,
                StartPage = startPage,
                EndPage = endPage,
                Heading = heading,
                HeadingPath = heading
            };
        }

        private static DocumentSearchResult CreateSearchResult(
            DocumentChunk chunk)
        {
            return new DocumentSearchResult
            {
                Chunk = chunk,
                Score = 1
            };
        }

        private sealed class FakeDocumentSearchService
            : IDocumentSearchService
        {
            private readonly IReadOnlyList<DocumentSearchResult> _results;

            public int CallCount { get; private set; }

            public string? LastQuery { get; private set; }

            public int LastLimit { get; private set; }

            public IReadOnlyCollection<Guid>? LastDocumentIds { get; private set; }

            public FakeDocumentSearchService(
                params DocumentSearchResult[] results)
            {
                _results = results;
            }

            public Task<IReadOnlyList<DocumentSearchResult>> SearchAsync(
                string query,
                IReadOnlyCollection<Guid> documentIds,
                int limit = 5,
                CancellationToken cancellationToken = default)
            {
                CallCount++;
                LastQuery = query;
                LastLimit = limit;
                LastDocumentIds = documentIds.ToArray();

                return Task.FromResult(_results);
            }
        }

        private sealed class FakeAnswerGenerationService
            : IAnswerGenerationService
        {
            public AnswerGenerationResult Result { get; set; } =
                new()
                {
                    Answer = "Test answer."
                };

            public int CallCount { get; private set; }

            public AnswerGenerationRequest? LastRequest { get; private set; }

            public Task<AnswerGenerationResult> GenerateAsync(
                AnswerGenerationRequest request,
                CancellationToken cancellationToken = default)
            {
                CallCount++;
                LastRequest = request;

                return Task.FromResult(Result);
            }
        }

        private sealed class FakeConversationService
            : IConversationService
        {
            private readonly IReadOnlyList<ConversationDocumentDTO> _documents;

            public FakeConversationService(params Guid[] documentIds)
            {
                var now = DateTime.UtcNow;

                _documents = documentIds
                    .Distinct()
                    .Select(id => new ConversationDocumentDTO
                    {
                        Id = id,
                        FileName = "test.pdf",
                        Status = DocumentStatus.Indexed,
                        UploadedAt = now,
                        IsActive = true,
                        AppliedAt = now
                    })
                    .ToList();
            }

            public Task<ServiceResult<IReadOnlyList<ConversationDocumentDTO>>>
                GetDocumentsAsync(
                    int userId,
                    Guid conversationId,
                    CancellationToken cancellationToken = default)
            {
                return Task.FromResult(
                    ServiceResult<IReadOnlyList<ConversationDocumentDTO>>
                        .Success(_documents));
            }

            public Task<ServiceResult<ConversationDTO>> CreateAsync(
                int userId,
                CreateConversationRequestDTO request,
                CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public Task<ServiceResult<IReadOnlyList<ConversationDTO>>>
                GetMyConversationsAsync(
                    int userId,
                    CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public Task<ServiceResult<IReadOnlyList<ConversationDocumentDTO>>>
                UpdateDocumentsAsync(
                    int userId,
                    Guid conversationId,
                    UpdateConversationDocumentsRequestDTO request,
                    CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
        }
    }
}
