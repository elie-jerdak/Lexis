namespace Lexis.Core.DTOs.DocumentProcessing
{
    public class ProcessedPage
    {
        public int PageNumber { get; set; }

        public string Text { get; set; } = string.Empty;

        public string Markdown { get; set; } = string.Empty;
    }
}
