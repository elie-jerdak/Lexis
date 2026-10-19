
using Lexis.Core.DTOs.Generation;
using Lexis.Core.Services;
using Microsoft.Extensions.Configuration;

namespace Lexis.Tests.Generation;

public sealed class OpenAiAnswerGenerationServiceTests
{
    private const string ApiUserSecretsId =
        "10ec0cec-3253-4b5f-a4eb-9804ddd0ca6d";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateAsync_WithValidContext_ReturnsAnswer()
    {
        // Load the same User Secrets used by Lexis.API.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Generation:OpenRouter:BaseUrl"] =
                        "https://openrouter.ai/api/v1/",
                    ["Generation:OpenRouter:Model"] =
                        "nvidia/nemotron-3.5-lightning:free"
                })
            .AddUserSecrets(ApiUserSecretsId)
            .Build();

        Assert.False(
            string.IsNullOrWhiteSpace(
                configuration["Generation:OpenRouter:ApiKey"]),
            "OpenRouter API key was not found in the API project's User Secrets.");

        var httpClient = new HttpClient();

        try
        {
            var service = new OpenAiAnswerGenerationService(httpClient, configuration);

            var request = new AnswerGenerationRequest
            {
                Question = "According to the supplied excerpt, what is the capital of France?",
                Context = new[]
                {
                    new GenerationContextItem
                    {
                        ReferenceId = "C1",
                        ChunkId = Guid.NewGuid(),
                        DocumentId = Guid.NewGuid(),
                        Content = "The capital of France is Paris.",
                        StartPage = 1,
                        EndPage = 1,
                        Heading = "Example",
                        HeadingPath = "Example"
                    }
                }
            };

            var result = await service.GenerateAsync(request);

            Assert.False(
                string.IsNullOrWhiteSpace(result.Answer),
                "The model returned an empty answer.");

            Assert.Contains(
                "Paris",
                result.Answer,
                StringComparison.OrdinalIgnoreCase);

            Assert.False(
                result.InsufficientEvidence,
                "The model unexpectedly reported insufficient evidence.");

            Assert.All(
                result.Citations,
                citation => Assert.Equal("C1", citation.ReferenceId));
        }
        finally
        {
            httpClient.Dispose();
        }
    }
}
