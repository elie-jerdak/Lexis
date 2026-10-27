using Lexis.Core.Enums;

namespace Lexis.API.DTOs.Documents;

public sealed record DocumentStatusChangedEvent(Guid DocumentId, string FileName, DocumentStatus Status,
    DateTime ChangedAtUtc,string? Error);