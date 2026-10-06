using Lexis.Core.DTOs.DocumentProcessing;

namespace Lexis.Core.Interfaces
{
    public interface ITableContinuationService
    {
        ProcessedDocument ProcessTableContinuations(ProcessedDocument document);
    }
}
