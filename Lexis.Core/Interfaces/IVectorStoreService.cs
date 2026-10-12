using Lexis.Core.DTOs.VectorSearch;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Interfaces
{
    public interface IVectorStoreService
    {
        Task EnsureCollectionExistsAsync(
            CancellationToken cancellationToken = default);
        Task StoreAsync(IReadOnlyList<Models.DocumentChunk> chunks, IReadOnlyList<float[]> embeddings,
            CancellationToken cancellationToken = default);
        Task<IReadOnlyList<VectorSearchResult>> SearchAsync(IReadOnlyList<float> embedding, int limit, Guid? documentId = null,
            CancellationToken cancellationToken = default);
    }
}
