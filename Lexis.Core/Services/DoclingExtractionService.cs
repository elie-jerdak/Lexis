using Lexis.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using Lexis.Core.DTOs.DocumentProcessing;

namespace Lexis.Core.Services
{
    public class DoclingExtractionService : IDocumentExtractionService
    {
        private readonly ILogger<DoclingExtractionService> _logger;
        private readonly HttpClient _httpClient;

        public DoclingExtractionService(ILogger<DoclingExtractionService> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<ExtractedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken)
        {
            using var form = new MultipartFormDataContent();

            await using var stream = File.OpenRead(filePath);

            form.Add(new StreamContent(stream), "file", Path.GetFileName(filePath));

            var response = await _httpClient.PostAsync("extract", form, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorDetails = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogError(
                    "PDF extraction failed. Status Code: {StatusCode}. Reason: {ReasonPhrase}. Details: {Details}",
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    string.IsNullOrWhiteSpace(errorDetails)
                        ? "No detailed message provided."
                        : errorDetails);

                throw new HttpRequestException(
                    $"Failed to extract PDF text. Status: {response.StatusCode}");
            }


            var result = await response.Content
                .ReadFromJsonAsync<ExtractedDocument>(
                    cancellationToken: cancellationToken);


            if (result is null)
                throw new Exception("Docling returned an empty response.");


            return result;
        }
    }
}
