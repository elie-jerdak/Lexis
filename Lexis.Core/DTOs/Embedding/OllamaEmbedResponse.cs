using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.DTOs.Embedding
{
    public sealed class OllamaEmbedResponse
    {
        public float[][] Embeddings { get; set; } = [];
    }
}
