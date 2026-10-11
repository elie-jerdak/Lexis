using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Helpers
{
    public sealed class OllamaEmbeddingOptions
    {
        public const string SectionName = "OllamaEmbedding";

        public string BaseUrl { get; set; } = "http://host.docker.internal:11434/";

        public string Model { get; set; } = "nomic-embed-text";
    }
}
