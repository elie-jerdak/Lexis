namespace Lexis.Core.DTOs.VectorSearch
{
    public sealed class VectorSearchResult
    {
        public Guid ChunkId { get; init; }

        public double Score { get; init; }
    }
}
