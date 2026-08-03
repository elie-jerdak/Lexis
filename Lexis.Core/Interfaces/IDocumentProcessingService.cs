using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Interfaces
{
    public interface IDocumentProcessingService
    {
        Task ProcessPendingDocumentsAsync(CancellationToken cancellationToken);
        Task ProcessDocumentAsync(Guid documentId, CancellationToken cancellationToken);
    }
}
