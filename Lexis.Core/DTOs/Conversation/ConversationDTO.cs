namespace Lexis.Core.DTOs.Conversation;

public sealed class ConversationDTO
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }
}