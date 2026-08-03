namespace Lexis.Core.DTOs.DocumentProcessing
{
    public class ExtractedPage
    {
        public int PageNumber { get; set; }

        public string Text { get; set; } = string.Empty;

        public string Markdown { get; set; } = string.Empty;

        public List<DocumentElement> Elements { get; set; } = [];
    }
}
