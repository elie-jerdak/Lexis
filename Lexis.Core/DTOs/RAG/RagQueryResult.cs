namespace Lexis.Core.DTOs.RAG
{
    public sealed class RagQueryResult
    {
        public string Answer { get; init; } = string.Empty;

        public bool InsufficientEvidence { get; init; }

        public IReadOnlyList<RagCitation> Citations { get; init; }
            = Array.Empty<RagCitation>();
    }
}
