
using System.Security.Claims;
using Lexis.Core.DTOs.UsageLogs;
using Lexis.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lexis.API.Controllers;

[ApiController]
[Route("api/usage-logs")]
[Authorize]
public sealed class UsageLogsController : ControllerBase
{
    private readonly IUsageLogService _usageLogService;

    public UsageLogsController(IUsageLogService usageLogService)
    {
        _usageLogService = usageLogService;
    }

    [HttpGet]
    public async Task<ActionResult<UsageLogPageDTO>> GetUsageLogs([FromQuery] UsageLogQueryDTO query,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdClaim, out var authenticatedUserId))
        {
            return Unauthorized();
        }

        var isAdmin = User.IsInRole("Admin");

        if (query.From.HasValue &&
            query.To.HasValue &&
            query.From.Value > query.To.Value)
        {
            return BadRequest(
                "The 'From' date must be earlier than or equal to 'To'.");
        }

        var result = await _usageLogService.GetUsageLogsAsync(
            authenticatedUserId,
            isAdmin,
            query,
            cancellationToken);

        return Ok(result);
    }
}
