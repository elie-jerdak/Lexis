using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Enums
{
    /// <summary>
    /// Distinguishes WHY a chunk is marked as a continuation, since these
    /// two situations require different downstream handling:
    ///   - DoclingSourceContinuation: the source document itself splits an
    ///     element across a page break (e.g. a table straddling pages 3-4).
    ///     Docling's own provenance data told us this; it's a property of
    ///     the source document, not a choice we made.
    ///   - ArtificialTokenSplit: WE split a chunk because it exceeded
    ///     MaxChunkTokens. This is our own budgeting decision, not a fact
    ///     about the source document's structure.
    /// Conflating these loses information a retriever/re-ranker could use
    /// (e.g. to decide whether re-fetching neighboring chunks matters).
    /// </summary>
    public enum ContinuationType
    {
        None,
        DoclingSourceContinuation,
        ArtificialTokenSplit
    }
}
