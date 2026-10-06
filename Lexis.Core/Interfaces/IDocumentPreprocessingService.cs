using Lexis.Core.DTOs.DocumentProcessing;

namespace Lexis.Core.Interfaces
{
    public interface IDocumentPreprocessingService
    {
        Task<ProcessedDocument> ProcessAsync(ExtractedDocument document, CancellationToken cancellationToken);
    }
}
