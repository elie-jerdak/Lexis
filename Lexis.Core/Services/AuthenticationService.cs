using Lexis.API.Data;
using Lexis.Core.DTOs.Auth;
using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lexis.Core.Services
{
    public class AuthenticationService : IAuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole<int>> _roleManager;
        private readonly IEmailService _emailService;
        private readonly IJwtService _jwtService;
        private readonly ApplicationDbContext _context;


        public AuthenticationService(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<int>> roleManager,
            IJwtService jwtService, ApplicationDbContext context, IEmailService emailService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _jwtService = jwtService;
            _context = context;
            _emailService = emailService;
        }


        public async Task<ServiceResult<AuthResponseDTO>> RegisterAsync(RegisterRequestDTO request)
        {
            var existingUser = await _userManager.FindByEmailAsync(request.Email);

            if (existingUser != null)
            {
                return ServiceResult<AuthResponseDTO>.Failure(
                    "Email already exists.",
                    "EMAIL_EXISTS");
            }

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                PhoneNumber = request.PhoneNumber,

                // Change this later when adding email verification
                EmailConfirmed = false
            };

            var createResult = await _userManager.CreateAsync(user, request.Password);

            if (!createResult.Succeeded)
            {
                return ServiceResult<AuthResponseDTO>.Failure(
                    string.Join(", ", createResult.Errors.Select(e => e.Description)),
                    "REGISTRATION_FAILED");
            }

            const string role = "User";

            if (!await _roleManager.RoleExistsAsync(role))
            {
                await _roleManager.CreateAsync(new IdentityRole<int>(role));
            }

            var roleResult = await _userManager.AddToRoleAsync(user, role);

            if (!roleResult.Succeeded)
            {
                return ServiceResult<AuthResponseDTO>.Failure(
                    string.Join(", ", roleResult.Errors.Select(e => e.Description)),
                    "ROLE_ASSIGNMENT_FAILED");
            }

            var emailConfirmationToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);

            //this should point to the frontend
            //var confirmationLink = $"http://localhost:3000/confirm-email?userId={user.Id}&token={Uri.EscapeDataString(emailConfirmationToken)}";

            var confirmationLink = $" https://localhost:32769/api/Auth/confirm-email?userId={user.Id}&token={Uri.EscapeDataString(emailConfirmationToken)}";


            await _emailService.SendEmailAsync(
                "jardakelie@gmail.com"/*user.Email!*/,
                "Confirm your Lexis account",
                "ConfirmEmail",
                new Dictionary<string, string>
                {
                    {
                        "ConfirmationLink",
                        confirmationLink
                    }
                });

            return ServiceResult<AuthResponseDTO>.Success(
                new AuthResponseDTO
                {
                    Message = "Registration successful. Please verify your email."
                });
        }

        public async Task<ServiceResult<AuthResponseDTO>> LoginAsync(LoginRequestDTO request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);

            if (user == null)
            {
                return ServiceResult<AuthResponseDTO>.Failure(
                    "Invalid email or password.",
                    "INVALID_CREDENTIALS");
            }

            if (!user.EmailConfirmed)
            {
                return ServiceResult<AuthResponseDTO>.Failure(
                    "Please verify your email before logging in.",
                    "EMAIL_NOT_CONFIRMED");
            }

            var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);

            if (!passwordValid)
            {
                return ServiceResult<AuthResponseDTO>.Failure(
                    "Invalid email or password.",
                    "INVALID_CREDENTIALS");
            }

            var authResponse = await GenerateTokens(user);

            authResponse.Message = "Login successful.";

            user.LastLoginAt = DateTime.UtcNow;

            var updateResult = await _userManager.UpdateAsync(user);

            if (!updateResult.Succeeded)
            {
                return ServiceResult<AuthResponseDTO>.Failure(
                    "Could not update login information.",
                    "UPDATE_FAILED");
            }

            return ServiceResult<AuthResponseDTO>.Success(authResponse);
        }

        public async Task<ServiceResult<AuthResponseDTO>> RefreshTokenAsync(RefreshTokenRequestDTO request)
        {
            var storedToken = await _context.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

            if (storedToken == null)
            {
                return ServiceResult<AuthResponseDTO>.Failure(
                    "Invalid refresh token.",
                    "INVALID_REFRESH_TOKEN");
            }

            if (!storedToken.IsActive)
            {
                return ServiceResult<AuthResponseDTO>.Failure(
                    "Refresh token has expired or has been revoked.",
                    "INVALID_REFRESH_TOKEN");
            }

            // Revoke the old refresh token
            storedToken.Revoke();

            // Generate a new access token and refresh token
            var roles = await _userManager.GetRolesAsync(storedToken.User);

            var accessToken = _jwtService.GenerateAccessToken(storedToken.User, roles);

            var newRefreshToken = _jwtService.GenerateRefreshToken(storedToken.UserId);

            _context.RefreshTokens.Add(newRefreshToken);

            await _context.SaveChangesAsync();

            return ServiceResult<AuthResponseDTO>.Success(
                new AuthResponseDTO
                {
                    Message = "Token refreshed successfully.",
                    AccessToken = accessToken,
                    RefreshToken = newRefreshToken.Token,
                    AccessTokenExpiration = DateTime.UtcNow.AddMinutes(15)
                });
        }

        public async Task<ServiceResult<bool>> LogoutAsync(LogoutRequestDTO request)
        {
            var refreshToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

            if (refreshToken == null)
            {
                return ServiceResult<bool>.Failure(
                    "Refresh token not found.",
                    "TOKEN_NOT_FOUND");
            }

            if (refreshToken.IsRevoked)
            {
                return ServiceResult<bool>.Failure(
                    "Refresh token already revoked.",
                    "TOKEN_ALREADY_REVOKED");
            }

            refreshToken.Revoke();

            await _context.SaveChangesAsync();

            return ServiceResult<bool>.Success(true);
        }

        public async Task<ServiceResult<bool>> ConfirmEmailAsync(ConfirmEmailRequestDTO request)
        {
            var user = await _userManager.FindByIdAsync(request.UserId.ToString());

            if (user == null)
            {
                return ServiceResult<bool>.Failure(
                    "User not found.",
                    "USER_NOT_FOUND");
            }

            if (user.EmailConfirmed)
            {
                return ServiceResult<bool>.Failure(
                    "Email already confirmed.",
                    "EMAIL_ALREADY_CONFIRMED");
            }

            var result = await _userManager.ConfirmEmailAsync(user, request.Token);

            if (!result.Succeeded)
            {
                return ServiceResult<bool>.Failure(
                    "Invalid email confirmation token.",
                    "INVALID_TOKEN");
            }

            return ServiceResult<bool>.Success(true);
        }

        public async Task<ServiceResult<bool>> ResendConfirmationEmailAsync(ResendConfirmationEmailRequestDTO request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);

            if (user == null)
            {
                // Don't reveal whether an email exists
                return ServiceResult<bool>.Success(true);
            }

            if (user.EmailConfirmed)
            {
                return ServiceResult<bool>.Failure(
                    "Email is already confirmed.",
                    "EMAIL_ALREADY_CONFIRMED");
            }

            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

            var confirmationLink =
                $"https://localhost:32769/api/auth/confirm-email" +
                $"?userId={user.Id}" +
                $"&token={Uri.EscapeDataString(token)}";

            await _emailService.SendEmailAsync(
                user.Email!,
                "Confirm your Lexis account",
                "ConfirmEmail",
                new Dictionary<string, string>
                {
                    {
                        "ConfirmationLink",
                        confirmationLink
                    }
                });

            return ServiceResult<bool>.Success(true);
        }

        public async Task<ServiceResult<bool>> ForgotPasswordAsync(ForgotPasswordRequestDTO request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);


            // Do not reveal whether the email exists
            if (user == null)
            {
                return ServiceResult<bool>.Success(true);
            }


            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            //later send the info to the frontend
            var resetLink =
                $"https://localhost:32769/api/auth/reset-password-page" +
                $"?email={Uri.EscapeDataString(user.Email!)}" +
                $"&token={Uri.EscapeDataString(token)}"; ;


            await _emailService.SendEmailAsync(
                user.Email!,
                "Reset your Lexis password",
                "ForgotPassword",
                new Dictionary<string, string>
                {
            {
                "ResetLink",
                resetLink
            },
            {
                "FirstName",
                user.FirstName
            }
                });


            return ServiceResult<bool>.Success(true);
        }

        public async Task<ServiceResult<bool>> ResetPasswordAsync(ResetPasswordRequestDTO request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);

            if (user == null)
            {
                return ServiceResult<bool>.Failure(
                    "Invalid request.",
                    "INVALID_REQUEST");
            }


            var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);


            if (!result.Succeeded)
            {
                return ServiceResult<bool>.Failure(
                    string.Join(", ",
                        result.Errors.Select(e => e.Description)),
                    "RESET_FAILED");
            }

            // Revoke all refresh tokens after password change
            await RevokeAllRefreshTokensAsync(user.Id);

            return ServiceResult<bool>.Success(true);
        }

        public async Task<ServiceResult<bool>> ChangePasswordAsync(int userId, ChangePasswordRequestDTO request)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());

            if (user == null)
            {
                return ServiceResult<bool>.Failure(
                    "User not found.",
                    "USER_NOT_FOUND");
            }

            var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

            if (!result.Succeeded)
            {
                return ServiceResult<bool>.Failure(
                    string.Join(", ",
                        result.Errors.Select(e => e.Description)),
                    "PASSWORD_CHANGE_FAILED");
            }

            // Revoke existing refresh tokens
            await RevokeAllRefreshTokensAsync(user.Id);

            return ServiceResult<bool>.Success(true);
        }

        // Private Helper Functions
        private async Task<AuthResponseDTO> GenerateTokens(ApplicationUser user)
        {
            await RevokeAllRefreshTokensAsync(user.Id);

            var roles = await _userManager.GetRolesAsync(user);

            var accessToken = _jwtService.GenerateAccessToken(user, roles);
            var refreshToken = _jwtService.GenerateRefreshToken(user.Id);

            _context.RefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync();

            return new AuthResponseDTO
            {
                Message = "Login successful.",
                AccessToken = accessToken,
                RefreshToken = refreshToken.Token,
                AccessTokenExpiration = DateTime.UtcNow.AddMinutes(15)
            };
        }
        private async Task RevokeAllRefreshTokensAsync(int userId)
        {
            var activeTokens = await _context.RefreshTokens
                .Where(rt => rt.UserId == userId &&
                             rt.RevokedAt == null &&
                             rt.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();

            foreach (var token in activeTokens)
            {
                token.Revoke();
            }

            await _context.SaveChangesAsync();
        }
    }
}
