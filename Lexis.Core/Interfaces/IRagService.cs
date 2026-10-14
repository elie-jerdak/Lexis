using Lexis.Core.DTOs.RAG;

namespace Lexis.Core.Interfaces
{
    public interface IRagService
    {
        Task<RagQueryResult> QueryAsync(int userId, RagQueryRequest request, CancellationToken cancellationToken = default);
    }
}
