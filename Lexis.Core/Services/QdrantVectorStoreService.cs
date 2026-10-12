using Lexis.Core.DTOs.VectorSearch;
using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;


namespace Lexis.Core.Services
{
    public sealed class QdrantVectorStoreService : IVectorStoreService
    {
        private readonly HttpClient _httpClient;
        private readonly QdrantOptions _options;

        public QdrantVectorStoreService(HttpClient httpClient, IOptions<QdrantOptions> options)
        {
            _httpClient = httpClient;
            _options = options.Value;

            if (string.IsNullOrWhiteSpace(_options.BaseUrl))
                throw new InvalidOperationException(
                    "Qdrant BaseUrl is not configured.");

            if (string.IsNullOrWhiteSpace(_options.CollectionName))
                throw new InvalidOperationException(
                    "Qdrant CollectionName is not configured.");

            if (_options.VectorSize <= 0)
                throw new InvalidOperationException(
                    "Qdrant VectorSize must be positive.");

            _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }

        public async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
        {
            var collectionPath = $"collections/{Uri.EscapeDataString(_options.CollectionName)}";

            using var getResponse = await _httpClient.GetAsync(collectionPath, cancellationToken);

            if (getResponse.IsSuccessStatusCode)
                return;

            if (getResponse.StatusCode != HttpStatusCode.NotFound)
            {
                var error = await getResponse.Content.ReadAsStringAsync(
                    cancellationToken);

                throw new HttpRequestException(
                    $"Failed to check Qdrant collection. " +
                    $"Status: {(int)getResponse.StatusCode}. {error}");
            }

            var request = new
            {
                vectors = new
                {
                    size = _options.VectorSize,
                    distance = "Cosine"
                }
            };

            using var createResponse = await _httpClient.PutAsJsonAsync(collectionPath, request, cancellationToken);

            if (createResponse.IsSuccessStatusCode)
                return;

            // Another worker may have created the collection
            // between our GET and PUT requests.
            if (createResponse.StatusCode == HttpStatusCode.Conflict)
            {
                using var verifyResponse = await _httpClient.GetAsync(collectionPath, cancellationToken);

                if (verifyResponse.IsSuccessStatusCode)
                    return;
            }

            var createError = await createResponse.Content.ReadAsStringAsync(cancellationToken);

            throw new HttpRequestException(
                $"Failed to create Qdrant collection. " +
                $"Status: {(int)createResponse.StatusCode}. {createError}");
        }

        public async Task StoreAsync(IReadOnlyList<DocumentChunk> chunks, IReadOnlyList<float[]> embeddings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(chunks);
            ArgumentNullException.ThrowIfNull(embeddings);

            if (chunks.Count != embeddings.Count)
            {
                throw new ArgumentException(
                    "The number of chunks must match the number of embeddings.");
            }

            if (chunks.Count == 0)
                return;

            if (embeddings.Any(e =>
                    e is null ||
                    e.Length != _options.VectorSize))
            {
                throw new ArgumentException(
                    $"Every embedding must contain exactly " +
                    $"{_options.VectorSize} dimensions.",
                    nameof(embeddings));
            }

            if (chunks.Any(c => c is null || c.Id == Guid.Empty))
            {
                throw new ArgumentException(
                    "Every chunk must have a valid ID.",
                    nameof(chunks));
            }

            if (chunks.Select(c => c.Id).Distinct().Count() != chunks.Count)
            {
                throw new ArgumentException(
                    "Chunk IDs must be unique within a batch.",
                    nameof(chunks));
            }

            var points = chunks.Select((chunk, index) => new
            {
                id = chunk.Id.ToString("D"),
                vector = embeddings[index],
                payload = new Dictionary<string, object?>
                {
                    ["document_id"] = chunk.DocumentId.ToString("D"),
                    ["chunk_id"] = chunk.Id.ToString("D"),
                    ["chunk_index"] = chunk.ChunkIndex,
                    ["start_page"] = chunk.StartPage,
                    ["end_page"] = chunk.EndPage,
                    ["heading"] = chunk.Heading,
                    ["heading_path"] = chunk.HeadingPath,
                    ["chunk_type"] = chunk.ChunkType.ToString(),
                    ["continuation_type"] = chunk.ContinuationType.ToString(),
                    ["table_id"] = chunk.TableId,
                    ["figure_id"] = chunk.FigureId
                }
            }).ToArray();

            var collectionPath =
                $"collections/{Uri.EscapeDataString(_options.CollectionName)}" +
                "/points?wait=true";

            // One request for this batch. Qdrant point IDs are stable,
            // making the upsert itself safe to repeat.
            using var response = await _httpClient.PutAsJsonAsync(
                collectionPath,
                new { points },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(
                    cancellationToken);

                throw new HttpRequestException(
                    $"Qdrant upsert failed. " +
                    $"Status: {(int)response.StatusCode}. {error}");
            }
        }

        public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(IReadOnlyList<float> embedding, int limit,
            Guid? documentId = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(embedding);

            if (embedding.Count != _options.VectorSize)
            {
                throw new ArgumentException(
                    $"The query embedding must contain exactly " +
                    $"{_options.VectorSize} dimensions.",
                    nameof(embedding));
            }

            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(limit),
                    "The result limit must be greater than zero.");
            }

            var collectionPath = $"collections/{Uri.EscapeDataString(_options.CollectionName)}" + "/points/query";

            var request = new
            {
                query = embedding,
                limit,
                filter = documentId.HasValue
                    ? new
                    {
                        must = new[]
                        {
                            new
                            {
                                key = "document_id",
                                match = new
                                {
                                    value = documentId.Value.ToString()
                                }
                            }
                        }
                    }
                    : null,
                with_payload = false,
                with_vector = false
            };

            using var response = await _httpClient.PostAsJsonAsync(collectionPath, request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);

                throw new HttpRequestException(
                    $"Qdrant vector search failed. " +
                    $"Status: {(int)response.StatusCode}. {error}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var points = json.RootElement
                .GetProperty("result")
                .GetProperty("points");

            var results = new List<VectorSearchResult>();

            var rawRank = 0;

            foreach (var point in points.EnumerateArray())
            {
                rawRank++;

                var idText = point.GetProperty("id").GetString();

                if (!Guid.TryParse(idText, out var chunkId))
                {
                    throw new InvalidOperationException(
                        $"Qdrant returned an invalid chunk ID: {idText}");
                }

                var score = point.GetProperty("score").GetDouble();

                if (chunkId == Guid.Parse(
                        "388c1a31-c158-48fa-8967-53d7ee0939ea"))
                {
                    Console.WriteLine(
                        $"[QdrantRawTrace] Rank={rawRank}; " +
                        $"ChunkId={chunkId}; Score={score}; Limit={limit}");
                }

                results.Add(new VectorSearchResult
                {
                    ChunkId = chunkId,
                    Score = score
                });
            }

            return results;
        }

    }
}
