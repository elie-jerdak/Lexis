using Lexis.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Interfaces
{
    public interface IDocumentChunkRepository
    {
        Task AddRangeAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken);

        Task<List<DocumentChunk>> GetByIdsAsync(IEnumerable<Guid> chunkIds, CancellationToken cancellationToken);

        Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken);
    }
}
