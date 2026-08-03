using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.DTOs.DocumentProcessing
{
    public class DocumentElement
    {
        public string ElementType { get; set; } = string.Empty;

        public int Order { get; set; }

        public string? Text { get; set; }

        public int? TableId { get; set; }

        public int? FigureId { get; set; }

        public string? Caption { get; set; }

        public bool ContinuedFromPreviousPage { get; set; }

        public bool ContinuesOnNextPage { get; set; }

        public int PageOrder { get; set; }
    }
}
