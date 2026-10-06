using Lexis.Core.DTOs.DocumentProcessing;
using Lexis.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Lexis.Core.Services
{
    public class DocumentPreprocessingService : IDocumentPreprocessingService
    {
        private readonly ITextCleaningService _textCleaningService; 
        private readonly ILogger<DocumentProcessingService> _logger;
        private readonly IDocumentStructureService _documentStructureService;
        private readonly ITableContinuationService _tableContinuationService;
        public DocumentPreprocessingService(ITextCleaningService textCleaningService,
            ILogger<DocumentProcessingService> logger, IDocumentStructureService documentStructureService,
            ITableContinuationService tableContinuationService)
        {
            _textCleaningService = textCleaningService; 
            _logger = logger;
            _documentStructureService = documentStructureService;
            _tableContinuationService = tableContinuationService;
        }


        public async Task<ProcessedDocument> ProcessAsync(ExtractedDocument document, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var repeatedHeaders = _documentStructureService.FindRepeatedHeaders(document.Pages);

            var repeatedFooters = _documentStructureService.FindRepeatedFooters(document.Pages);

            var processedPages = document.Pages
                    .Select(page =>
                    {
                        var filteredElements = page.Elements
                            .Where(element =>
                                !IsNoise(
                                    element,
                                    repeatedHeaders,
                                    repeatedFooters))
                            .ToList();


                        return new ProcessedPage
                        {
                            PageNumber = page.PageNumber,

                            Text = _textCleaningService.Clean(
                                string.Join(
                                    "\n",
                                    filteredElements
                                        .Where(e => !string.IsNullOrWhiteSpace(e.Text))
                                        .Select(e => e.Text))),

                            Markdown = page.Markdown,

                            Elements = filteredElements
                        };
                    })
                    .ToList();
            var processedDocument = new ProcessedDocument
            {
                Pages = processedPages,
                Metadata = document.Metadata
            };

            processedDocument = _tableContinuationService.ProcessTableContinuations(processedDocument);

            return processedDocument;
        }

        private bool IsNoise(DocumentElement element, HashSet<string> repeatedHeaders, HashSet<string> repeatedFooters)
        {
            if (_documentStructureService.IsPageNumber(element))
                return true;


            if (!string.IsNullOrWhiteSpace(element.Text))
            {
                var text = element.Text.Trim();

                if (repeatedHeaders.Contains(text))
                    return true;


                if (repeatedFooters.Contains(text))
                    return true;
            }


            return false;
        }
    }
}
