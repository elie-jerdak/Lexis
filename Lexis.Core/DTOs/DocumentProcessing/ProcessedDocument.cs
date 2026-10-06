using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.DTOs.DocumentProcessing
{
    public class ProcessedDocument
    {
        public List<ProcessedPage> Pages { get; set; } = [];

        public DocumentMetadata Metadata { get; set; } = new();
    }
}
