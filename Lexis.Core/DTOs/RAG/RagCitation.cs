namespace Lexis.Core.DTOs.RAG
{
    public sealed class RagCitation
    {
        // Temporary reference used by the generation model, e.g. C1.
        public string ReferenceId { get; init; } = string.Empty;

        // Actual identifiers from Lexis.
        public Guid ChunkId { get; init; }
        public Guid DocumentId { get; init; }

        public int StartPage { get; init; }
        public int EndPage { get; init; }

        public string? Heading { get; init; }
        public string? HeadingPath { get; init; }
    }
}
