namespace Lexis.Core.DTOs.DocumentProcessing
{
    public class ExtractedDocument
    {
        public List<ExtractedPage> Pages { get; set; } = [];

        public DocumentMetadata Metadata { get; set; } = new();
    }
}
