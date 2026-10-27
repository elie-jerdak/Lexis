using Lexis.API.DTOs.Documents;
using Lexis.API.Hubs;
using Lexis.Core.Enums;
using Lexis.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Lexis.API.Services;

public sealed class DocumentStatusNotifier : IDocumentStatusNotifier
{
    private readonly IHubContext<DocumentStatusHub> _hubContext;
    private readonly ILogger<DocumentStatusNotifier> _logger;

    public DocumentStatusNotifier(IHubContext<DocumentStatusHub> hubContext, ILogger<DocumentStatusNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyAsync(int userId, Guid documentId, string fileName, DocumentStatus status,
        string? error = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = new DocumentStatusChangedEvent(documentId, fileName, status, DateTime.UtcNow, error);

            await _hubContext.Clients
                .Group(DocumentStatusHub.UserGroup(userId))
                .SendAsync(
                    "DocumentStatusChanged",
                    message,
                    cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Notification failures must not turn a successfully
            // indexed document into a failed processing job.
            _logger.LogWarning(
                ex,
                "Failed to notify user {UserId} about document {DocumentId} status {Status}.",
                userId,
                documentId,
                status);
        }
    }
}