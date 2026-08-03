using Lexis.Core.DTOs.DocumentProcessing;

namespace Lexis.Core.Interfaces
{
    public interface IDocumentExtractionService
    {
        Task<ExtractedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken);
    }
}
