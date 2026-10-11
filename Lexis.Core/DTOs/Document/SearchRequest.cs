using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.DTOs.Document
{
    public sealed class SearchRequest
    {
        public string Query { get; set; } = string.Empty;

        public int Limit { get; set; } = 5;

        public Guid? DocumentId { get; set; }
    }
}
