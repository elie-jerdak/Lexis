using Lexis.API.Common;
using Lexis.Core.DTOs.Conversation;
using Lexis.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lexis.API.Controllers;

[Authorize]
[Route("api/[controller]")]
public class ConversationsController : BaseApiController
{
    private readonly IConversationService _conversationService;

    public ConversationsController(IConversationService conversationService)
    {
        _conversationService = conversationService;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateConversationRequestDTO request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId == null)
        {
            return Unauthorized(new
            {
                message = "User authentication information is missing.",
                code = "USER_ID_NOT_FOUND"
            });
        }

        var result = await _conversationService.CreateAsync(userId.Value, request, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new
            {
                message = result.ErrorMessage,
                code = result.ErrorCode
            });
        }

        return CreatedAtAction(nameof(GetMyConversations), null, result.Data);
    }

    [HttpGet]
    public async Task<IActionResult> GetMyConversations(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId == null)
        {
            return Unauthorized(new
            {
                message = "User authentication information is missing.",
                code = "USER_ID_NOT_FOUND"
            });
        }

        var result = await _conversationService.GetMyConversationsAsync(userId.Value, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new
            {
                message = result.ErrorMessage,
                code = result.ErrorCode
            });
        }

        return Ok(result.Data);
    }

    [HttpGet("{id:guid}/documents")]
    public async Task<IActionResult> GetDocuments(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return Unauthorized(new
            {
                message = "User authentication information is missing.",
                code = "USER_ID_NOT_FOUND"
            });
        }

        var result = await _conversationService.GetDocumentsAsync(
            userId.Value,
            id,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return NotFound(new
            {
                message = result.ErrorMessage,
                code = result.ErrorCode
            });
        }

        return Ok(result.Data);
    }

    [HttpPut("{id:guid}/documents")]
    public async Task<IActionResult> UpdateDocuments(Guid id, [FromBody] UpdateConversationDocumentsRequestDTO request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return Unauthorized(new
            {
                message = "User authentication information is missing.",
                code = "USER_ID_NOT_FOUND"
            });
        }

        var result = await _conversationService.UpdateDocumentsAsync(
            userId.Value,
            id,
            request,
            cancellationToken);

        if (!result.IsSuccess)
        {
            var statusCode = result.ErrorCode switch
            {
                "CONVERSATION_NOT_FOUND" => StatusCodes.Status404NotFound,
                "DOCUMENT_NOT_FOUND" => StatusCodes.Status400BadRequest,
                "INVALID_DOCUMENT_ID" => StatusCodes.Status400BadRequest,
                "DOCUMENT_NOT_INDEXED" => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status400BadRequest
            };

            return StatusCode(statusCode, new
            {
                message = result.ErrorMessage,
                code = result.ErrorCode
            });
        }

        return Ok(result.Data);
    }

    [HttpGet("{id:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized(new { message = "User authentication information is missing.", code = "USER_ID_NOT_FOUND" });
        }

        var result = await _conversationService.GetMessagesAsync(userId.Value, id, cancellationToken);

        if (!result.IsSuccess)
        {
            return NotFound(new { message = result.ErrorMessage, code = result.ErrorCode });
        }

        return Ok(result.Data);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameConversationRequestDTO request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized(new { message = "User authentication information is missing.", code = "USER_ID_NOT_FOUND" });
        }

        var result = await _conversationService.RenameAsync(userId.Value, id, request, cancellationToken);

        if (!result.IsSuccess)
        {
            var statusCode = result.ErrorCode == "CONVERSATION_NOT_FOUND"
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            return StatusCode(statusCode, new { message = result.ErrorMessage, code = result.ErrorCode });
        }

        return Ok(result.Data);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized(new { message = "User authentication information is missing.", code = "USER_ID_NOT_FOUND" });
        }

        var result = await _conversationService.DeleteAsync(userId.Value, id, cancellationToken);

        if (!result.IsSuccess)
        {
            return NotFound(new { message = result.ErrorMessage, code = result.ErrorCode });
        }

        return Ok(new { message = "Conversation deleted." });   // body, so the frontend's apiRequest can parse it
    }
}