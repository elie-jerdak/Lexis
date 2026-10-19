using Lexis.API.Data;
using Lexis.Core.DTOs.Conversation;
using Lexis.Core.DTOs.MessageCitation;
using Lexis.Core.Enums;
using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Lexis.Core.Services
{
    public sealed class ConversationService : IConversationService
    {
        private const string DefaultTitle = "New conversation";

        private readonly ApplicationDbContext _context;

        public ConversationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ServiceResult<ConversationDTO>> CreateAsync(int userId, CreateConversationRequestDTO request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var title = string.IsNullOrWhiteSpace(request.Title)
                ? DefaultTitle
                : request.Title.Trim();

            if (title.Length > 255)
            {
                return ServiceResult<ConversationDTO>.Failure(
                    "The conversation title cannot exceed 255 characters.",
                    "TITLE_TOO_LONG");
            }

            var now = DateTime.UtcNow;

            var conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = title,
                CreatedAt = now,
                UpdatedAt = now,
                IsDeleted = false
            };

            _context.Conversations.Add(conversation);

            await _context.SaveChangesAsync(cancellationToken);

            var response = new ConversationDTO
            {
                Id = conversation.Id,
                Title = conversation.Title,
                CreatedAt = conversation.CreatedAt,
                UpdatedAt = conversation.UpdatedAt
            };

            return ServiceResult<ConversationDTO>.Success(response);
        }

        public async Task<ServiceResult<IReadOnlyList<ConversationDTO>>>GetMyConversationsAsync(int userId,
                CancellationToken cancellationToken = default)
        {
            var conversations = await _context.Conversations
                .AsNoTracking()
                .Where(c =>
                    c.UserId == userId &&
                    !c.IsDeleted)
                .OrderByDescending(c => c.UpdatedAt)
                .Select(c => new ConversationDTO
                {
                    Id = c.Id,
                    Title = c.Title,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            return ServiceResult<IReadOnlyList<ConversationDTO>>
                .Success(conversations);
        }

        public async Task<ServiceResult<IReadOnlyList<ConversationDocumentDTO>>>GetDocumentsAsync(int userId, 
            Guid conversationId, CancellationToken cancellationToken = default)
        {
            var conversationExists = await _context.Conversations
                .AnyAsync(
                    c => c.Id == conversationId
                         && c.UserId == userId
                         && !c.IsDeleted,
                    cancellationToken);

            if (!conversationExists)
            {
                return ServiceResult<IReadOnlyList<ConversationDocumentDTO>>.Failure(
                    "Conversation not found.",
                    "CONVERSATION_NOT_FOUND");
            }

            var documents = await _context.ConversationDocumentFilters
                .AsNoTracking()
                .Where(f => f.ConversationId == conversationId && f.IsActive)
                .OrderBy(f => f.Document.FileName)
                .Select(f => new ConversationDocumentDTO
                {
                    Id = f.Document.Id,
                    FileName = f.Document.FileName,
                    Status = f.Document.Status,
                    UploadedAt = f.Document.UploadedAt,
                    IsActive = f.IsActive,
                    AppliedAt = f.AppliedAt
                })
                .ToListAsync(cancellationToken);

            return ServiceResult<IReadOnlyList<ConversationDocumentDTO>>.Success(documents);
        }

        public async Task<ServiceResult<IReadOnlyList<ConversationDocumentDTO>>>UpdateDocumentsAsync(int userId,
                Guid conversationId, UpdateConversationDocumentsRequestDTO request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(
                    c => c.Id == conversationId
                         && c.UserId == userId
                         && !c.IsDeleted,
                    cancellationToken);

            if (conversation is null)
            {
                return ServiceResult<IReadOnlyList<ConversationDocumentDTO>>.Failure(
                    "Conversation not found.",
                    "CONVERSATION_NOT_FOUND");
            }

            var requestedIds = request.DocumentIds ?? new List<Guid>();

            if (requestedIds.Any(id => id == Guid.Empty))
            {
                return ServiceResult<IReadOnlyList<ConversationDocumentDTO>>.Failure(
                    "Document IDs must be valid.",
                    "INVALID_DOCUMENT_ID");
            }

            var distinctIds = requestedIds.Distinct().ToList();

            // Only documents owned by this user can be selected.
            var ownedDocuments = await _context.Documents
                .Where(d => distinctIds.Contains(d.Id) && d.UploadedBy == userId)
                .ToListAsync(cancellationToken);

            if (ownedDocuments.Count != distinctIds.Count)
            {
                return ServiceResult<IReadOnlyList<ConversationDocumentDTO>>.Failure(
                    "One or more documents were not found or do not belong to you.",
                    "DOCUMENT_NOT_FOUND");
            }

            // Only fully indexed documents can be selected.
            var unindexedDocument = ownedDocuments.FirstOrDefault(d => d.Status != DocumentStatus.Indexed);

            if (unindexedDocument is not null)
            {
                return ServiceResult<IReadOnlyList<ConversationDocumentDTO>>.Failure(
                    $"Document '{unindexedDocument.FileName}' is not ready for retrieval.",
                    "DOCUMENT_NOT_INDEXED");
            }

            var existingFilters = await _context.ConversationDocumentFilters
                .Where(f => f.ConversationId == conversationId)
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;

            // Deactivate current filters instead of deleting them.
            foreach (var filter in existingFilters)
            {
                filter.IsActive = false;
            }

            // Reactivate existing selections or add new ones.
            foreach (var documentId in distinctIds)
            {
                var existingFilter = existingFilters.FirstOrDefault(
                    f => f.DocumentId == documentId);

                if (existingFilter is not null)
                {
                    existingFilter.IsActive = true;
                    existingFilter.AppliedAt = now;
                }
                else
                {
                    _context.ConversationDocumentFilters.Add(
                        new ConversationDocumentFilter
                        {
                            Id = Guid.NewGuid(),
                            ConversationId = conversationId,
                            DocumentId = documentId,
                            IsActive = true,
                            AppliedAt = now
                        });
                }
            }

            conversation.UpdatedAt = now;

            await _context.SaveChangesAsync(cancellationToken);

            var selectedDocuments = await _context.ConversationDocumentFilters
                .AsNoTracking()
                .Where(f => f.ConversationId == conversationId && f.IsActive)
                .OrderBy(f => f.Document.FileName)
                .Select(f => new ConversationDocumentDTO
                {
                    Id = f.Document.Id,
                    FileName = f.Document.FileName,
                    Status = f.Document.Status,
                    UploadedAt = f.Document.UploadedAt,
                    IsActive = f.IsActive,
                    AppliedAt = f.AppliedAt
                })
                .ToListAsync(cancellationToken);

            return ServiceResult<IReadOnlyList<ConversationDocumentDTO>>
                .Success(selectedDocuments);
        }

        public async Task<ServiceResult<IReadOnlyList<ConversationMessageDTO>>> GetMessagesAsync(int userId, 
            Guid conversationId, CancellationToken cancellationToken = default)
        {
            var conversationExists = await _context.Conversations
                .AnyAsync(c => c.Id == conversationId && c.UserId == userId && !c.IsDeleted, cancellationToken);

            if (!conversationExists)
            {
                return ServiceResult<IReadOnlyList<ConversationMessageDTO>>.Failure(
                    "Conversation not found.", "CONVERSATION_NOT_FOUND");
            }

            var rows = await _context.Messages
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId
                            && (m.Role == MessageRole.User || m.Role == MessageRole.Assistant))
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    m.Id,
                    m.Role,
                    m.Content,
                    m.CreatedAt,
                    Citations = m.MessageCitations
                        .OrderByDescending(c => c.SimilarityScore)
                        .Select(c => new
                        {
                            c.DocumentChunkId,
                            c.DocumentChunk.StartPage,   // adjust to your DocumentChunk property names
                            c.DocumentChunk.EndPage,
                            c.DocumentChunk.Heading,
                            c.DocumentChunk.HeadingPath
                        })
                        .ToList()
                })
                .ToListAsync(cancellationToken);

            var messages = rows
                .Select(m => new ConversationMessageDTO
                {
                    Id = m.Id,
                    Role = m.Role == MessageRole.User ? "user" : "assistant",
                    Content = m.Content,
                    CreatedAt = m.CreatedAt,
                    Citations = m.Citations
                        .Select((c, i) => new MessageCitationDTO
                        {
                            ChunkId = c.DocumentChunkId,
                            ReferenceId = $"S{i + 1}",
                            StartPage = c.StartPage,
                            EndPage = c.EndPage,
                            Heading = c.Heading,
                            HeadingPath = c.HeadingPath
                        })
                        .ToList()
                })
                .ToList();

            return ServiceResult<IReadOnlyList<ConversationMessageDTO>>.Success(messages);
        }

        public async Task<ServiceResult<ConversationDTO>> RenameAsync(int userId, Guid conversationId, 
            RenameConversationRequestDTO request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var title = request.Title?.Trim();

            if (string.IsNullOrEmpty(title))
            {
                return ServiceResult<ConversationDTO>.Failure(
                    "The conversation title is required.", "TITLE_REQUIRED");
            }

            if (title.Length > 255)
            {
                return ServiceResult<ConversationDTO>.Failure(
                    "The conversation title cannot exceed 255 characters.", "TITLE_TOO_LONG");
            }

            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId && !c.IsDeleted,
                    cancellationToken);

            if (conversation is null)
            {
                return ServiceResult<ConversationDTO>.Failure(
                    "Conversation not found.", "CONVERSATION_NOT_FOUND");
            }

            conversation.Title = title;
            conversation.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return ServiceResult<ConversationDTO>.Success(new ConversationDTO
            {
                Id = conversation.Id,
                Title = conversation.Title,
                CreatedAt = conversation.CreatedAt,
                UpdatedAt = conversation.UpdatedAt
            });
        }

        public async Task<ServiceResult<bool>> DeleteAsync(int userId, Guid conversationId, 
            CancellationToken cancellationToken = default)
        {
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId && !c.IsDeleted,
                    cancellationToken);

            if (conversation is null)
            {
                return ServiceResult<bool>.Failure("Conversation not found.", "CONVERSATION_NOT_FOUND");
            }

            conversation.IsDeleted = true;   // soft delete, consistent with the rest of the service
            conversation.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return ServiceResult<bool>.Success(true);
        }
    }
}
