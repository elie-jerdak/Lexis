using Lexis.Core.DTOs.Generation;
using Lexis.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Tests.Services
{
    public sealed class FakeAnswerGenerationService : IAnswerGenerationService
    {
        public AnswerGenerationResult Result { get; set; } = new()
        {
            Answer = "This is a test answer.",
            Citations = Array.Empty<AnswerCitationReference>()
        };

        public AnswerGenerationRequest? LastRequest { get; private set; }

        public int CallCount { get; private set; }

        public Task<AnswerGenerationResult> GenerateAsync(
            AnswerGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            LastRequest = request;

            return Task.FromResult(Result);
        }
    }
}
