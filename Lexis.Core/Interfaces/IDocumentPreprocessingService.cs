using Lexis.Core.DTOs.DocumentProcessing;

namespace Lexis.Core.Interfaces
{
    public interface IDocumentPreprocessingService
    {
        Task<List<ProcessedPage>> ProcessAsync(List<ExtractedPage> pages, CancellationToken cancellationToken);
    }
}
