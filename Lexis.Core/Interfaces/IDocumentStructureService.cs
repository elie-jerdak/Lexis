using Lexis.Core.DTOs.DocumentProcessing;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Interfaces
{
    public interface IDocumentStructureService
    {
        HashSet<string> FindRepeatedHeaders(List<ExtractedPage> pages);
        HashSet<string> FindRepeatedFooters(List<ExtractedPage> pages);
        bool IsPageNumber(DocumentElement element);
    }
}
