using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.DTOs.RAG
{
    public sealed class RagQueryRequest
    {
        public Guid ConversationId { get; set; }

        public string Question { get; set; } = string.Empty;

        public int Limit { get; set; } = 5;
    }
}
