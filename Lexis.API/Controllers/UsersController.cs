using Lexis.API.Common;
using Lexis.Core.DTOs.Users;
using Lexis.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lexis.API.Controllers
{
    [Route("api/[controller]")] 
    public class UsersController : BaseApiController
    {
        private readonly IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }
         
        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = GetUserId();

            if (userId == null)
            {
                return Unauthorized(new
                {
                    message = "Invalid user identity.",
                    code = "INVALID_USER"
                });
            }

            var result = await _userService.GetProfileAsync(userId.Value);

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

        [Authorize]
        [HttpPut("me")]
        public async Task<IActionResult> UpdateProfile(UpdateProfileRequestDTO request)
        {
            var userId = GetUserId();

            if (userId == null)
            {
                return Unauthorized(new
                {
                    message = "Invalid user identity.",
                    code = "INVALID_USER"
                });
            }

            var result = await _userService.UpdateProfileAsync(userId.Value, request);

            if (!result.IsSuccess)
            {
                return BadRequest(new
                {
                    message = result.ErrorMessage,
                    code = result.ErrorCode
                });
            }

            return Ok(new
            {
                message = "Profile updated successfully."
            });
        }

        [Authorize]
        [HttpDelete("me")]
        public async Task<IActionResult> DeactivateAccount()
        {
            var userId = GetUserId();

            if (userId == null)
            {
                return Unauthorized(new
                {
                    message = "Invalid user identity.",
                    code = "INVALID_USER"
                });
            }

            var result = await _userService.DeactivateAccountAsync(userId.Value);


            if (!result.IsSuccess)
            {
                return BadRequest(new
                {
                    message = result.ErrorMessage,
                    code = result.ErrorCode
                });
            }


            return Ok(new
            {
                message = "Account deactivated successfully."
            });
        }

        [Authorize]
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var userId = GetUserId();

            if (userId == null)
            {
                return Unauthorized(new
                {
                    message = "Invalid user identity.",
                    code = "INVALID_USER"
                });
            }

            var result = await _userService.GetDashboardAsync(userId.Value);

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
    }
}
