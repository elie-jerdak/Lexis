using System.Text.Json.Serialization;

namespace Lexis.Core.DTOs.DocumentProcessing
{
    public class DocumentElement
    {
        public string ElementType { get; set; } = string.Empty;
        public int Order { get; set; }
        public string? Text { get; set; }
        public string? Markdown { get; set; }
        public int? TableId { get; set; }
        
        [JsonPropertyName("content")]
        public string? Content { get; set; }
        public int? FigureId { get; set; }
        public string? Caption { get; set; }
        public bool ContinuationOfPreviousTable { get; set; }
        public bool ContinuedFromPreviousPage { get; set; }
        public bool ContinuesOnNextPage { get; set; }
        public int PageOrder { get; set; }

        // Heading metadata from the extraction service
        public int? HeadingLevel { get; set; }
        public string? HeadingText { get; set; }
        public List<string> HeadingPath { get; set; } = new();
    }
}
