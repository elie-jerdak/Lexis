using Lexis.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.DTOs.DocumentProcessing
{
    public class ChunkBuilderState
    {
        public readonly StringBuilder TextBuffer = new();
        public int TextBufferTokens;
        public int TextStartPage;
        public int TextEndPage;

        public readonly StringBuilder ListBuffer = new();
        public int ListBufferTokens;
        public int ListStartPage;
        public int ListEndPage;

        // Heading context comes directly from Docling's extracted
        // headingPath. The chunker does not infer or reconstruct
        // heading hierarchy.
        public string? HeadingPath { get; set; }

        // Tables/figures currently being assembled across a page break.
        public readonly Dictionary<string, DocumentChunk> PendingElements = new();

        public int ChunkIndex;

        public string? CurrentHeading {  get; set; }
    }
}
