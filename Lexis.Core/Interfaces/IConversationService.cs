using Lexis.Core.DTOs.Conversation;
using Lexis.Core.Helpers;

namespace Lexis.Core.Interfaces;

public interface IConversationService
{
    Task<ServiceResult<ConversationDTO>> CreateAsync(int userId,CreateConversationRequestDTO request,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<ConversationDTO>>> GetMyConversationsAsync(int userId,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<ConversationDocumentDTO>>> GetDocumentsAsync(int userId, Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<ConversationDocumentDTO>>> UpdateDocumentsAsync(int userId, Guid conversationId,
        UpdateConversationDocumentsRequestDTO request, CancellationToken cancellationToken = default);
}