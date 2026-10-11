using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Helpers
{
    public sealed class QdrantOptions
    {
        public const string SectionName = "Qdrant";
        public string BaseUrl { get; set; } = "http://host.docker.internal:6333/";
        public string CollectionName { get; set; } = "lexis_chunks";
        public int VectorSize { get; set; } = 768;
    }
}
