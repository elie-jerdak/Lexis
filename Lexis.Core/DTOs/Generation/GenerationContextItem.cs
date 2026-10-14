namespace Lexis.Core.DTOs.Generation
{
    public sealed class GenerationContextItem
    {
        // Temporary reference used by the model, such as C1 or C2.
        public string ReferenceId { get; init; } = string.Empty;

        // Actual source identifiers owned by Lexis.
        public Guid ChunkId { get; init; }
        public Guid DocumentId { get; init; }

        public string Content { get; init; } = string.Empty;

        public int StartPage { get; init; }
        public int EndPage { get; init; }

        public string? Heading { get; init; }
        public string? HeadingPath { get; init; }
    }
}
