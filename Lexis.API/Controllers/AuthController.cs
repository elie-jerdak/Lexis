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
        private readonly IWebHostEnvironment _environment;
        private const string AccessCookie = "lexis_access_token";
        private const string RefreshCookie = "lexis_refresh_token";

        public AuthController(IAuthService authService, IWebHostEnvironment environment)
        {
            _authService = authService;
            _environment = environment;
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

            if (!result.IsSuccess || result.Data is null)
            {
                return Unauthorized(new
                {
                    message = result.ErrorMessage,
                    code = result.ErrorCode
                });
            }

            SetAuthCookies(result.Data);

            Response.Headers.CacheControl = "no-store";

            return Ok(new { message = result.Data.Message });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh()
        {
            var refreshToken = Request.Cookies[RefreshCookie];

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                ClearAuthCookies();

                return Unauthorized(new
                {
                    message = "Refresh token is missing.",
                    code = "INVALID_REFRESH_TOKEN"
                });
            }

            var result = await _authService.RefreshTokenAsync(
                new RefreshTokenRequestDTO
                {
                    RefreshToken = refreshToken
                });

            if (!result.IsSuccess || result.Data is null)
            {
                ClearAuthCookies();

                return Unauthorized(new
                {
                    message = result.ErrorMessage,
                    code = result.ErrorCode
                });
            }

            SetAuthCookies(result.Data);

            Response.Headers.CacheControl = "no-store";

            return Ok(new { message = result.Data.Message });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var refreshToken = Request.Cookies[RefreshCookie];

            try
            {
                if (!string.IsNullOrWhiteSpace(refreshToken))
                {
                    await _authService.LogoutAsync(
                        new LogoutRequestDTO
                        {
                            RefreshToken = refreshToken
                        });
                }
            }
            finally
            {
                ClearAuthCookies();
            }

            Response.Headers.CacheControl = "no-store";

            return Ok(new { message = "Logout successful." });
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

        [Authorize]
        [HttpGet("session")]
        public IActionResult Session()
        {
            Response.Headers.CacheControl = "no-store";

            var isAdmin = User.IsInRole("Admin");

            return Ok(new
            {
                authenticated = true,
                isAdmin
            });
        }

        private CookieOptions CreateCookieOptions()
        {
            return new CookieOptions
            {
                HttpOnly = true,
                Secure = !_environment.IsDevelopment() || Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                IsEssential = true
            };
        }

        private void SetAuthCookies(AuthResponseDTO auth)
        {
            var accessOptions = CreateCookieOptions();
            accessOptions.Expires = auth.AccessTokenExpiration;

            Response.Cookies.Append(AccessCookie, auth.AccessToken!, accessOptions);

            // Session cookie: the server remains responsible for checking the refresh token's real expiration and revocation.
            Response.Cookies.Append(RefreshCookie, auth.RefreshToken!, CreateCookieOptions());
        }

        private void ClearAuthCookies()
        {
            var options = CreateCookieOptions();

            Response.Cookies.Delete(AccessCookie, options);
            Response.Cookies.Delete(RefreshCookie, options);
        }

    }
}
