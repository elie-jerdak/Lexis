using Lexis.API.Common;
using Lexis.Core.DTOs.Document;
using Lexis.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Lexis.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SearchController : BaseApiController
    {
        private readonly IDocumentSearchService _documentSearchService;

        public SearchController(IDocumentSearchService documentSearchService)
        {
            _documentSearchService = documentSearchService;
        }

        [HttpPost]
        public async Task<IActionResult> Search([FromBody] SearchRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BadRequest(new
                {
                    error = "A search query is required."
                });
            }

            if (request.Limit is < 1 or > 100)
            {
                return BadRequest(new
                {
                    error = "Limit must be between 1 and 100."
                });
            }

            if (request.DocumentId == Guid.Empty)
            {
                return BadRequest(new
                {
                    error = "DocumentId must be a valid GUID when supplied."
                });
            }

            var results = await _documentSearchService.SearchAsync(request.Query, request.Limit, request.DocumentId,
                cancellationToken);

            return Ok(new
            {
                query = request.Query,
                resultCount = results.Count,
                results = results.Select(result => new
                {
                    chunkId = result.Chunk.Id,
                    documentId = result.Chunk.DocumentId,
                    chunkIndex = result.Chunk.ChunkIndex,
                    content = result.Chunk.Content,
                    startPage = result.Chunk.StartPage,
                    endPage = result.Chunk.EndPage,
                    heading = result.Chunk.Heading,
                    headingPath = result.Chunk.HeadingPath,
                    chunkType = result.Chunk.ChunkType.ToString(),
                    continuationType = result.Chunk.ContinuationType.ToString(),
                    tableId = result.Chunk.TableId,
                    figureId = result.Chunk.FigureId,
                    score = result.Score
                })
            });
        }
    }
}
