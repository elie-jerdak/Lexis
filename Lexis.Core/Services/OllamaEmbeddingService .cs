using Lexis.Core.DTOs.Embedding;
using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;

namespace Lexis.Core.Services
{
    public sealed class OllamaEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly OllamaEmbeddingOptions _options;

        public OllamaEmbeddingService(HttpClient httpClient, IOptions<OllamaEmbeddingOptions> options)
        {
            _httpClient = httpClient;
            _options = options.Value;

            _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }

        public async Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(texts);

            if (texts.Count == 0)
                return Array.Empty<float[]>();

            if (texts.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(
                    "Embedding inputs cannot contain null, empty, or whitespace-only text.",
                    nameof(texts));
            }

            var request = new
            {
                model = _options.Model,
                input = texts
            };

            using var response = await _httpClient.PostAsJsonAsync("api/embed", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);

                throw new HttpRequestException(
                    $"Ollama embedding request failed " +
                    $"({(int)response.StatusCode}): {error}");
            }

            var result =
                await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken: cancellationToken);

            if (result?.Embeddings is null || result.Embeddings.Length != texts.Count ||
                result.Embeddings.Any(embedding => embedding is null || embedding.Length == 0))
            {
                throw new InvalidOperationException(
                    "Ollama returned an invalid embedding response.");
            }

            var dimensions = result.Embeddings[0].Length;

            if (result.Embeddings.Any(embedding => embedding.Length != dimensions))
            {
                throw new InvalidOperationException("Ollama returned embeddings with inconsistent dimensions.");
            }

            return result.Embeddings;
        }
    }
}
