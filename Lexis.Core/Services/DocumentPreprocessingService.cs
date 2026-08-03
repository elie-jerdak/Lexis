using Lexis.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Lexis.Core.DTOs.DocumentProcessing;

namespace Lexis.Core.Services
{
    public class DocumentPreprocessingService : IDocumentPreprocessingService
    {
        private readonly ITextCleaningService _textCleaningService; 
        private readonly ILogger<DocumentProcessingService> _logger;

        public DocumentPreprocessingService(ITextCleaningService textCleaningService,
            ILogger<DocumentProcessingService> logger)
        {
            _textCleaningService = textCleaningService; 
            _logger = logger;
        }


        public async Task<List<ProcessedPage>> ProcessAsync(List<ExtractedPage> pages, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return pages
                .Select(page => new ProcessedPage
                {
                    PageNumber = page.PageNumber,
                    Text = _textCleaningService.Clean(page.Text),
                    Markdown = page.Markdown
                })
                .ToList();
        }
    }
}
