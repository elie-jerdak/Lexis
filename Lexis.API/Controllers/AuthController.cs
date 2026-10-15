using Lexis.API.Common;
using Lexis.Core.DTOs.Auth;
using Lexis.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Lexis.API.Controllers
{
    [Route("api/[controller]")] 
    public class AuthController : BaseApiController
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }


        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequestDTO request)
        {
            var result = await _authService.RegisterAsync(request);

            if (!result.IsSuccess)
            {
                return BadRequest(new
                {
                    message = result.ErrorMessage,
                    code = result.ErrorCode
                });
            }

            return Created("", result.Data);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequestDTO request)
        {
            var result = await _authService.LoginAsync(request);

            if (!result.IsSuccess)
            {
                return Unauthorized(new
                {
                    message = result.ErrorMessage,
                    code = result.ErrorCode
                });
            }

            return Ok(result.Data);
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(RefreshTokenRequestDTO request)
        {
            var result = await _authService.RefreshTokenAsync(request);

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

        [HttpPost("logout")]
        public async Task<IActionResult> Logout(LogoutRequestDTO request)
        {
            var result = await _authService.LogoutAsync(request);

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
                message = "Logout successful."
            });
        }

        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail([FromQuery] int userId, [FromQuery] string token)
        {
            var result = await _authService.ConfirmEmailAsync(
                new ConfirmEmailRequestDTO
                {
                    UserId = userId,
                    Token = token
                });

            if (!result.IsSuccess)
            {
                return Content(
                    $"""
            <html>
                <body style="font-family:Arial;text-align:center;margin-top:100px;">
                    <h2>❌ Email Verification Failed</h2>
                    <p>{result.ErrorMessage}</p>
                </body>
            </html>
            """,
                    "text/html");
            }

            return Content(
                """
        <html>
            <body style="font-family:Arial;text-align:center;margin-top:100px;">
                <h2> Email Verified!</h2>
                <p>Your email has been confirmed successfully.</p>
                <p>You may now close this page and log in.</p>
            </body>
        </html>
        """,
                "text/html");
        }

        [HttpPost("resend-confirmation-email")]
        public async Task<IActionResult> ResendConfirmationEmail(ResendConfirmationEmailRequestDTO request)
        {
            var result = await _authService.ResendConfirmationEmailAsync(request);

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
                message = "If the email exists, a confirmation link has been sent."
            });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordRequestDTO request)
        {
            var result = await _authService.ForgotPasswordAsync(request);

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
                message = "If the email exists, a password reset link has been sent."
            });
        }

        //delete this entire endpoint once the frontend is finished
        [HttpGet("reset-password-page")]
        public IActionResult ResetPasswordPage(string email, string token)
        {
            return Content(
            $"""
    <html>
    <body style="font-family:Arial;text-align:center">

    <h2>Reset Password</h2>

    <form method="post" action="/api/auth/reset-password">

        <input 
            name="Email"
            value="{email}"
            hidden />

        <textarea 
            name="Token"
            hidden>{token}</textarea>


        <input
            name="NewPassword"
            type="password"
            placeholder="New Password"
            required />

        <br/><br/>

        <button type="submit">
            Reset Password
        </button>

    </form>

    </body>
    </html>
    """,
            "text/html");
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromForm] ResetPasswordRequestDTO request)
        {
            var result = await _authService.ResetPasswordAsync(request);


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
                message = "Password reset successfully."
            });
        }

        [Authorize]
        [HttpPut("change-password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordRequestDTO request)
        {
            var userIdClaim =
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;


            if (userIdClaim == null)
            {
                return Unauthorized(new
                {
                    message = "Invalid token.",
                    code = "INVALID_TOKEN"
                });
            }

            var userId = int.Parse(userIdClaim);
            
            var result = await _authService.ChangePasswordAsync(userId, request);

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
                message = "Password changed successfully. Please login again."
            });
        }
    }
}
