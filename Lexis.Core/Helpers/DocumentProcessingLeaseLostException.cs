namespace Lexis.Core.Helpers;

public sealed class DocumentProcessingLeaseLostException : Exception
{
    public Guid DocumentId { get; }

    public Guid LeaseId { get; }

    public DocumentProcessingLeaseLostException(Guid documentId, Guid leaseId) : base($"Processing lease lost for document {documentId}.")
    {
        DocumentId = documentId;
        LeaseId = leaseId;
    }
}