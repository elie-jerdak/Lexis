using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.DTOs.MessageCitation
{
    public sealed class MessageCitationDTO
    {
        public Guid ChunkId { get; set; }
        public string ReferenceId { get; set; } = string.Empty;
        public int StartPage { get; set; }
        public int EndPage { get; set; }
        public string? Heading { get; set; }
        public string? HeadingPath { get; set; }
    }
}
