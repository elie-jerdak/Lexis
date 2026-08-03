using Lexis.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Lexis.Core.DTOs.DocumentProcessing;

namespace Lexis.Core.Services
{
    public class DocumentPreprocessingService : IDocumentPreprocessingService
    {
        private readonly ITextCleaningService _textCleaningService; 
        private readonly ILogger<DocumentProcessingService> _logger;
        private readonly IDocumentStructureService _documentStructureService;
        public DocumentPreprocessingService(ITextCleaningService textCleaningService,
            ILogger<DocumentProcessingService> logger, IDocumentStructureService documentStructureService)
        {
            _textCleaningService = textCleaningService; 
            _logger = logger;
            _documentStructureService = documentStructureService;
        }


        public async Task<List<ProcessedPage>> ProcessAsync(List<ExtractedPage> pages, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var repeatedHeaders = _documentStructureService.FindRepeatedHeaders(pages);

            var repeatedFooters = _documentStructureService.FindRepeatedFooters(pages);

            return pages
                .Select(page =>
                {
                    var filteredElements = page.Elements
                        .Where(element =>!IsNoise(element, repeatedHeaders, repeatedFooters))
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
