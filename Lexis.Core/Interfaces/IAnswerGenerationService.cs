using Lexis.Core.DTOs.Generation;

namespace Lexis.Core.Interfaces
{
    public interface IAnswerGenerationService
    {
        Task<AnswerGenerationResult> GenerateAsync(AnswerGenerationRequest request, CancellationToken cancellationToken = default);
    }
}
