using Lexis.Core.DTOs.Document;

namespace Lexis.Core.Interfaces
{
    public interface IDocumentSearchService
    {
        Task<IReadOnlyList<DocumentSearchResult>> SearchAsync(string query, IReadOnlyCollection<Guid> documentIds, 
            int limit = 5, CancellationToken cancellationToken = default);

    }
}
