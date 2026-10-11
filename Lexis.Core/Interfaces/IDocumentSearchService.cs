using Lexis.Core.DTOs.Document;

namespace Lexis.Core.Interfaces
{
    public interface IDocumentSearchService
    {
        Task<IReadOnlyList<DocumentSearchResult>> SearchAsync(string query, int limit = 5, Guid? documentId = null,
            CancellationToken cancellationToken = default);
    }
}
