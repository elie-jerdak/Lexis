namespace Lexis.Core.DTOs.Generation
{
    public sealed class AnswerGenerationResult
    {
        public string Answer { get; init; } = string.Empty;

        public IReadOnlyList<AnswerCitationReference> Citations { get; init; } = Array.Empty<AnswerCitationReference>();

        public bool InsufficientEvidence { get; init; }

        // Actual token usage reported by the generation provider.
        public int InputTokens { get; init; }

        public int OutputTokens { get; init; }
    }
}
