namespace Lexis.Core.DTOs.Generation
{
    public sealed class AnswerGenerationResult
    {
        public string Answer { get; init; } = string.Empty;

        public IReadOnlyList<AnswerCitationReference> Citations { get; init; } = Array.Empty<AnswerCitationReference>();

        public bool InsufficientEvidence { get; init; }
    }
}
