using Lexis.Core.DTOs.DocumentProcessing;
using Lexis.Core.Models;

namespace Lexis.Core.Interfaces
{
    public interface IDocumentChunkingService
    {
        List<DocumentChunk> CreateChunks(Guid documentId, ProcessedDocument document);
    }
}
