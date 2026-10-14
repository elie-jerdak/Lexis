
using Lexis.Core.DTOs.RAG;
using Lexis.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Lexis.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public sealed class RagController : ControllerBase
    {
        private readonly IRagService _ragService;

        public RagController(IRagService ragService)
        {
            _ragService = ragService;
        }

        [HttpPost("query")]
        public async Task<IActionResult> Query([FromBody] RagQueryRequest request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                return BadRequest(new
                {
                    error = "A request body is required."
                });
            }

            if (request.ConversationId == Guid.Empty)
            {
                return BadRequest(new
                {
                    error = "A valid ConversationId is required."
                });
            }

            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return BadRequest(new
                {
                    error = "A question is required."
                });
            }

            if (request.Limit is < 1 or > 100)
            {
                return BadRequest(new
                {
                    error = "Limit must be between 1 and 100."
                });
            }

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized();
            }

            try
            {
                var result = await _ragService.QueryAsync(userId, request, cancellationToken);

                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new
                {
                    error = "The conversation was not found."
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    error = ex.Message
                });
            }
        }
    }
}
