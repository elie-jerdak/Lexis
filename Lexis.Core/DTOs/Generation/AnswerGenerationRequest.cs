namespace Lexis.Core.DTOs.Generation
{
    public sealed class AnswerGenerationRequest
    {
        public string Question { get; init; } = string.Empty;

        public IReadOnlyList<GenerationContextItem> Context { get; init; } = Array.Empty<GenerationContextItem>();
    }
}
