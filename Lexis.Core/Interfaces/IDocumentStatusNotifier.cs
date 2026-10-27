using Lexis.Core.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Interfaces
{
    public interface IDocumentStatusNotifier
    {
        Task NotifyAsync(int userId, Guid documentId, string fileName, DocumentStatus status, string? error = null,
            CancellationToken cancellationToken = default);
    }
}
