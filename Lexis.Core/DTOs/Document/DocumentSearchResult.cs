using Lexis.Core.Models; 

namespace Lexis.Core.DTOs.Document
{
    public sealed class DocumentSearchResult
    {
        public required DocumentChunk Chunk { get; init; }

        public double Score { get; init; }
    }
}
