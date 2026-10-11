using Lexis.Core.DTOs.DocumentProcessing;
using Lexis.Core.Enums;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Microsoft.Extensions.Logging;

namespace Lexis.Core.Services
{
    public class DocumentChunkingService : IDocumentChunkingService
    {
        private const int MaxChunkTokens = 800;

        private readonly ITokenCounter _tokenCounter;
        private readonly ILogger<DocumentChunkingService> _logger;
        private readonly HashSet<string> _ignoredTypes;
        private readonly HashSet<string> _unmappedTypesWarned = new(StringComparer.OrdinalIgnoreCase);

        public DocumentChunkingService(ILogger<DocumentChunkingService> logger, ITokenCounter? tokenCounter = null,
            HashSet<string>? ignoredTypes = null)
        {
            _logger = logger;
            _tokenCounter = tokenCounter ?? new WhitespaceTokenCounterService();
            _ignoredTypes = ignoredTypes ?? DefaultIgnoredTypes;
        }

        public List<DocumentChunk> CreateChunks(Guid documentId, ProcessedDocument document)
        {
            var chunks = new List<DocumentChunk>();
            var state = new ChunkBuilderState();

            foreach (var page in document.Pages)
            {
                foreach (var element in page.Elements.OrderBy(e => e.Order))
                {
                    if (_ignoredTypes.Contains(element.ElementType))
                        continue;

                    var elementContent =
                        !string.IsNullOrWhiteSpace(element.Markdown) ? element.Markdown :
                        !string.IsNullOrWhiteSpace(element.Text) ? element.Text :
                        !string.IsNullOrWhiteSpace(element.Content) ? element.Content :
                        !string.IsNullOrWhiteSpace(element.Caption) ? element.Caption :
                        string.Empty;

                    if (string.IsNullOrWhiteSpace(elementContent))
                    {
                        _logger.LogDebug(
                            "Skipping element with empty content. " +
                            "ElementType={ElementType}, Page={PageNumber}, Order={Order}",
                            element.ElementType,
                            page.PageNumber,
                            element.Order);

                        continue;
                    }

                    var chunkType = MapChunkType(element.ElementType);

                    if (chunkType == ChunkType.Heading)
                    {
                        FlushText(chunks, documentId, state);
                        FlushList(chunks, documentId, state);
                        PushHeading(state, element);
                        continue;
                    }

                    if (HasHeadingPathChanged(state, element))
                    {
                        FlushText(chunks, documentId, state);
                        FlushList(chunks, documentId, state);

                        UpdateHeadingContext(state, element);
                    }

                    switch (chunkType)
                    {
                        case ChunkType.Text:
                            FlushList(chunks, documentId, state);

                            var incomingTokens = _tokenCounter.Count(elementContent);

                            /*
                             * Only start a new chunk at a paragraph boundary.
                             *
                             * If this element continues a paragraph from a previous page,
                             * it must remain with that paragraph even if the token limit
                             * is exceeded.
                             */
                            if (!element.ContinuedFromPreviousPage && state.TextBuffer.Length > 0 &&
                                state.TextBufferTokens + incomingTokens > MaxChunkTokens)
                            {
                                FlushText(chunks, documentId, state, ContinuationType.ArtificialTokenSplit);
                            }

                            AppendText(state, element, page.PageNumber);

                            break;

                        case ChunkType.List:
                            FlushText(chunks, documentId, state);
                            AppendListItem(state, element, page.PageNumber);
                            if (state.ListBufferTokens >= MaxChunkTokens)
                                FlushList(chunks, documentId, state, ContinuationType.ArtificialTokenSplit);
                            break;

                        case ChunkType.Table:
                        case ChunkType.ImageCaption:
                            FlushText(chunks, documentId, state);
                            FlushList(chunks, documentId, state);
                            HandleContinuableElement(chunks, documentId, state, element, page.PageNumber, chunkType);
                            break;

                        default:
                            FlushText(chunks, documentId, state);
                            FlushList(chunks, documentId, state);
                            Console.WriteLine($"FlushAllPendingElements in switch defaults: {string.Join(", ", state.PendingElements.Keys)}");
                            FlushAllPendingElements(chunks, documentId, state);
                            chunks.Add(CreateSimpleElementChunk(documentId, state, element, page.PageNumber, chunkType));
                            break;
                    }
                }
            }

            FlushText(chunks, documentId, state);
            FlushList(chunks, documentId, state);

            if (state.PendingElements.Any(p => p.Value.IsPending))
            {
                _logger.LogWarning(
                    "Document ended while continuable elements were still open. Flushing remaining elements.");
            }

            foreach (var pending in state.PendingElements.Values)
            {
                chunks.Add(FinalizeElementChunk(state, pending));
            }

            state.PendingElements.Clear();

            return chunks;
        }

        // ---- Text buffering -------------------------------------------------

        private void AppendText(ChunkBuilderState state, DocumentElement element, int pageNumber)
        {
            // Markdown-aware: prefer markdown so formatting (bold, inline
            // code, etc.) survives into the embedded chunk rather than
            // being silently flattened to plain text.
            var content = element.Markdown ?? element.Text ?? element.Content ?? element.Caption ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
                return;

            if (state.TextStartPage == 0)
                state.TextStartPage = pageNumber;
            state.TextEndPage = pageNumber;

            state.TextBuffer.AppendLine(content);
            state.TextBufferTokens += _tokenCounter.Count(content); // incremental, not O(n) per append
        }

        private void FlushText(List<DocumentChunk> chunks, Guid documentId, ChunkBuilderState state,
            ContinuationType continuationType = ContinuationType.None)
        {
            if (state.TextBuffer.Length == 0)
                return;

            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                Content = state.TextBuffer.ToString().Trim(),
                ChunkIndex = state.ChunkIndex++,
                StartPage = state.TextStartPage,
                EndPage = state.TextEndPage,
                Heading = state.CurrentHeading,
                HeadingPath = state.HeadingPath,
                ChunkType = ChunkType.Text,
                ContinuationType = continuationType,
                TokenCount = state.TextBufferTokens,
                CreatedAt = DateTime.UtcNow,

            });

            state.TextBuffer.Clear();
            state.TextBufferTokens = 0;
            state.TextStartPage = 0;
            state.TextEndPage = 0;
        }

        // ---- List buffering --------------------------------------------------
        // Consecutive list_item elements are grouped into one chunk instead
        // of one chunk per bullet — a single list item rarely stands alone
        // semantically, and 10 near-empty chunks for a 10-item list hurts
        // retrieval more than it helps.

        private void AppendListItem(ChunkBuilderState state, DocumentElement element, int pageNumber)
        {
            var content = element.Markdown ?? element.Text ?? element.Content ?? element.Caption ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
                return;

            if (state.ListStartPage == 0)
                state.ListStartPage = pageNumber;
            state.ListEndPage = pageNumber;

            // Normalize to a bullet if the source markdown doesn't already
            // carry one — Docling usually does, but don't assume.
            var line = content.TrimStart().StartsWith("-") || content.TrimStart().StartsWith("*")
                ? content
                : $"- {content}";

            state.ListBuffer.AppendLine(line);
            state.ListBufferTokens += _tokenCounter.Count(content);
        }

        private void FlushList(List<DocumentChunk> chunks, Guid documentId, ChunkBuilderState state,
            ContinuationType continuationType = ContinuationType.None)
        {
            if (state.ListBuffer.Length == 0)
                return;

            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                Content = state.ListBuffer.ToString().Trim(),
                ChunkIndex = state.ChunkIndex++,
                StartPage = state.ListStartPage,
                EndPage = state.ListEndPage,
                Heading = state.CurrentHeading,
                HeadingPath = state.HeadingPath,
                ChunkType = ChunkType.List,
                ContinuationType = continuationType,
                TokenCount = state.ListBufferTokens,
                CreatedAt = DateTime.UtcNow
            });

            state.ListBuffer.Clear();
            state.ListBufferTokens = 0;
            state.ListStartPage = 0;
            state.ListEndPage = 0;
        }

        // ---- Table / figure continuation merging ------------------------------
        // This is the piece that actually uses the ContinuedFromPreviousPage /
        // ContinuesOnNextPage flags and TableId/FigureId the extraction
        // service provides, instead of just storing them as unused metadata.
        //
        // v2 TODO: for very large tables, consider splitting by row group
        // instead of emitting one giant chunk — mid-row splits would corrupt
        // the table, so this needs real row-boundary awareness, not a naive
        // token-count cut. Not implemented here.

        private void HandleContinuableElement(List<DocumentChunk> chunks, Guid documentId, ChunkBuilderState state,
            DocumentElement element, int pageNumber, ChunkType chunkType)
        {
            var elementId = chunkType == ChunkType.Table ? element.TableId : element.FigureId;
            var content = element.Markdown ?? element.Text ?? element.Content ?? element.Caption ?? string.Empty;

            // Don't embed empty image chunks. A picture with no markdown,
            // text, or caption (no OCR/description available) produces an
            // empty chunk that adds noise without any retrievable signal.
            // Revisit once OCR/image-description output is wired in.

            Console.WriteLine($"Pending count = {state.PendingElements.Count}");

            if (chunkType == ChunkType.ImageCaption && string.IsNullOrWhiteSpace(content))
            {
                _logger.LogDebug(
                    "Skipping empty picture element (figureId={FigureId}) on page {Page} — " +
                    "no markdown/text/caption available.", elementId, pageNumber);
                return;
            }

            var key = BuildPendingKey(chunkType, elementId);

            Console.WriteLine(
                $"ElementType={element.ElementType}, " +
                $"Page={pageNumber}, " +
                $"TableId={element.TableId}, " +
                $"Prev={element.ContinuedFromPreviousPage}, " +
                $"Next={element.ContinuesOnNextPage}, " +
                $"Key={key}");

            if (chunkType == ChunkType.Table && elementId == null)
            {
                _logger.LogWarning(
                    "Skipping table without TableId on page {Page}",
                    pageNumber);

                return;
            }

            Console.WriteLine(
                $"Table chunk: Page={pageNumber}, TableId={element.TableId}, " +
                $"ContinuedFromPrevious={element.ContinuedFromPreviousPage}, " +
                $"ContinuedFromPrevious={element.ContinuedFromPreviousPage}, " +
                $"ContinuesNext={element.ContinuesOnNextPage}, Key={key}");

            if (element.ContinuedFromPreviousPage && key != null && state.PendingElements.TryGetValue(key, out var pending))
            {
                pending.Content = pending.Content + "\n" + content;
                Console.WriteLine($"Appending to pending: {key}, NewEndPage={pageNumber}");
                
                pending.IsPending = element.ContinuesOnNextPage;
                pending.EndPage = pageNumber;
                
                if (element.ContinuedFromPreviousPage)
                {
                    pending.ContinuationType = ContinuationType.DoclingSourceContinuation;
                }

                pending.TokenCount = _tokenCounter.Count(pending.Content);

                if (pending.TokenCount > MaxChunkTokens)
                {
                    _logger.LogWarning(
                        "Table {TableId} contains {TokenCount} tokens.",
                        pending.TableId,
                        pending.TokenCount);
                }

                pending.ContinuationType = ContinuationType.DoclingSourceContinuation;
            }
            else
            {
                var newChunk = new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    Content = content,
                    ChunkIndex = -1, // set on flush; -1 makes an unflushed chunk obvious in a debugger
                    StartPage = pageNumber,
                    EndPage = pageNumber,
                    Heading = state.CurrentHeading,
                    HeadingPath = state.HeadingPath,
                    ChunkType = chunkType,
                    TableId = chunkType == ChunkType.Table ? elementId : null,
                    FigureId = chunkType == ChunkType.ImageCaption ? elementId : null,
                    ContinuationType = ContinuationType.None,
                    TokenCount = _tokenCounter.Count(content),
                    IsPending = element.ContinuesOnNextPage,
                    CreatedAt = DateTime.UtcNow
                };

                if (key != null)
                {
                    if (state.PendingElements.ContainsKey(key))
                    {
                        _logger.LogWarning(
                            "Overwriting existing pending element {Key}",
                            key);
                    }

                    state.PendingElements[key] = newChunk;
                }
                else
                    // No stable ID to key on (elementId is null) — can't
                    // safely track this as a multi-page continuation, so
                    // treat it as a one-off chunk instead of risking merging
                    // it with an unrelated element later.
                    chunks.Add(FinalizeElementChunk(state, newChunk));
            }

            if (!element.ContinuesOnNextPage && key != null)
            {
                Console.WriteLine($"FlushPendingElement: {key}");
                FlushPendingElement(chunks, documentId, state, key);
            }
        }

        private static string? BuildPendingKey(ChunkType chunkType, int? elementId)
        {
            if (elementId == null)
                return null;

            return chunkType == ChunkType.Table
                ? $"table:{elementId}"
                : $"figure:{elementId}";
        }

        private void FlushPendingElement(List<DocumentChunk> chunks, Guid documentId, ChunkBuilderState state, string key)
        {
            if (!state.PendingElements.TryGetValue(key, out var pending))
                return;

            state.PendingElements.Remove(key);
            chunks.Add(FinalizeElementChunk(state, pending));
        }

        private void FlushAllPendingElements(List<DocumentChunk> chunks, Guid documentId, ChunkBuilderState state)
        {
            if (state.PendingElements.Count == 0)
                return;

            var completed = state.PendingElements
                                    .Where(x => !x.Value.IsPending)
                                    .ToList();

            foreach (var item in completed)
            {
                chunks.Add(FinalizeElementChunk(state, item.Value));
                state.PendingElements.Remove(item.Key);
            }

            state.PendingElements.Clear();
        }

        private DocumentChunk FinalizeElementChunk(ChunkBuilderState state, DocumentChunk chunk)
        {
            chunk.ChunkIndex = state.ChunkIndex++;

            // We don't split tables/figures on token overflow (that's the
            // v2 row-splitting TODO below), so at minimum, log which
            // documents produce oversized chunks — otherwise you won't
            // know which ones are quietly exceeding your embedding
            // model's effective context until retrieval quality suffers.
            if (chunk.TokenCount > MaxChunkTokens)
            {
                _logger.LogWarning(
                    "{ChunkType} chunk (tableId={TableId}, figureId={FigureId}, pages {StartPage}-{EndPage}) " +
                    "is {TokenCount} tokens, exceeding MaxChunkTokens ({MaxChunkTokens}). " +
                    "Not auto-split — review this document.",
                    chunk.ChunkType, chunk.TableId, chunk.FigureId, chunk.StartPage, chunk.EndPage,
                    chunk.TokenCount, MaxChunkTokens);
            }

            return chunk;
        }

        // ---- Simple (non-continuable, non-buffered) elements: code, formula, reference ----

        private DocumentChunk CreateSimpleElementChunk(Guid documentId, ChunkBuilderState state, DocumentElement element, 
            int pageNumber, ChunkType chunkType)
        {
            var content = element.Markdown ?? element.Text ?? element.Content ?? element.Caption ?? string.Empty;

            return new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                Content = content,
                ChunkIndex = state.ChunkIndex++,
                StartPage = pageNumber,
                EndPage = pageNumber,
                Heading = state.CurrentHeading,
                HeadingPath = state.HeadingPath,
                ChunkType = chunkType,
                ContinuationType = element.ContinuedFromPreviousPage
                    ? ContinuationType.DoclingSourceContinuation
                    : ContinuationType.None,
                TokenCount = _tokenCounter.Count(content),
                CreatedAt = DateTime.UtcNow
            };
        }

        // ---- Heading hierarchy -------------------------------------------------

        private static void PushHeading(ChunkBuilderState state, DocumentElement element)
        {
            state.HeadingPath = element.HeadingPath is { Count: > 0 } ? string.Join(" > ", element.HeadingPath) : null;

            state.CurrentHeading =
                element.HeadingText ??
                element.HeadingPath?.LastOrDefault() ??
                element.Text;
        }

        private bool HasHeadingPathChanged(ChunkBuilderState state, DocumentElement element)
        {
            var elementHeadingPath = element.HeadingPath is { Count: > 0 } ? string.Join(" > ", element.HeadingPath): null;

            return !string.Equals(
                state.HeadingPath,
                elementHeadingPath,
                StringComparison.Ordinal);
        }

        private static void UpdateHeadingContext(ChunkBuilderState state, DocumentElement element)
        {
            state.HeadingPath = element.HeadingPath is { Count: > 0 } ? string.Join(" > ", element.HeadingPath) : null;

            state.CurrentHeading = element.HeadingText
                ?? element.HeadingPath?.LastOrDefault()
                ?? element.Text;
        }

        // ---- Element type mapping -------------------------------------------------

        private ChunkType MapChunkType(string elementType)
        {
            if (ElementTypeMap.TryGetValue(elementType, out var mapped))
                return mapped;

            if (_unmappedTypesWarned.Add(elementType))
            {
                _logger.LogWarning(
                    "Unmapped Docling element type '{ElementType}' — routing to Unknown, " +
                    "NOT Text. Add an explicit mapping once you've confirmed how this type " +
                    "should be chunked; leaving it Unknown until then avoids silently " +
                    "embedding it as ordinary prose.",
                    elementType);
            }

            return ChunkType.Unknown;
        }

        // Explicit mapping instead of a catch-all switch default. Anything
        // NOT in this dictionary is logged as unmapped rather than silently
        // treated as Text — so a new Docling label showing up in a future
        // document version gets noticed instead of silently mis-chunked.
        // Confirm this list against your own corpus by checking the
        // "Element types encountered" log line from the extraction service.
        private static readonly Dictionary<string, ChunkType> ElementTypeMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["text"] = ChunkType.Text,
                ["paragraph"] = ChunkType.Text,
                ["title"] = ChunkType.Heading,
                ["section_header"] = ChunkType.Heading,
                ["caption"] = ChunkType.ImageCaption,
                ["table"] = ChunkType.Table,
                ["picture"] = ChunkType.ImageCaption,
                ["list_item"] = ChunkType.List,
                ["code"] = ChunkType.Code,
                ["formula"] = ChunkType.Formula,
                ["reference"] = ChunkType.Reference,
                ["document_index"] = ChunkType.DocumentIndex
            };

        private static readonly HashSet<string> DefaultIgnoredTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "page_header", "page_footer", "footnote", "marker",
                "empty_value", "key_value_region"
            };
    }

}