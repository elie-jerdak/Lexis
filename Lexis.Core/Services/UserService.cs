using Lexis.API.Data;
using Lexis.Core.DTOs.User;
using Lexis.Core.DTOs.Users;
using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lexis.Core.Services
{
    public class UserService : IUserService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly ApplicationDbContext _context;

        public UserService(UserManager<ApplicationUser> userManager, ApplicationDbContext context, IRefreshTokenService refreshTokenService)
        {
            _userManager = userManager;
            _context = context;
            _refreshTokenService = refreshTokenService;
            _context = context;
        }

        public async Task<ServiceResult<UserProfileDTO>> GetProfileAsync(int userId)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());

            if (user == null)
            {
                return ServiceResult<UserProfileDTO>.Failure(
                    "User not found.",
                    "USER_NOT_FOUND");
            }

            var profile = new UserProfileDTO
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email!,
                PhoneNumber = user.PhoneNumber,
                EmailConfirmed = user.EmailConfirmed,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                MonthlyTokenAllowance = user.MonthlyTokenAllowance,
                TokensConsumedCurrentMonth = user.TokensConsumedCurrentMonth
            };

            return ServiceResult<UserProfileDTO>.Success(profile);
        }

        public async Task<ServiceResult<bool>> DeactivateAccountAsync(int userId)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());

            if (user == null)
            {
                return ServiceResult<bool>.Failure(
                    "User not found.",
                    "USER_NOT_FOUND");
            }

            if (!user.IsActive)
            {
                return ServiceResult<bool>.Failure(
                    "Account is already deactivated.",
                    "ACCOUNT_ALREADY_DEACTIVATED");
            }

            user.IsActive = false;

            // Revoke all refresh tokens
            await _refreshTokenService.RevokeUserRefreshTokensAsync(userId);

            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
            {
                return ServiceResult<bool>.Failure(
                    string.Join(", ",
                        result.Errors.Select(e => e.Description)),
                    "ACCOUNT_DEACTIVATION_FAILED");
            }

            await _context.SaveChangesAsync();

            return ServiceResult<bool>.Success(true);
        }

        public async Task<ServiceResult<bool>> UpdateProfileAsync(int userId, UpdateProfileRequestDTO request)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());

            if (user == null)
            {
                return ServiceResult<bool>.Failure(
                    "User not found.",
                    "USER_NOT_FOUND");
            }

            user.FirstName = request.FirstName;
            user.LastName = request.LastName;
            user.PhoneNumber = request.PhoneNumber;

            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
            {
                return ServiceResult<bool>.Failure(
                    string.Join(", ", result.Errors.Select(e => e.Description)),
                    "UPDATE_FAILED");
            }

            return ServiceResult<bool>.Success(true);
        }

        public async Task<ServiceResult<UserDashboardDTO>> GetDashboardAsync(int userId)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());

            if (user == null)
            {
                return ServiceResult<UserDashboardDTO>.Failure(
                    "User not found.",
                    "USER_NOT_FOUND");
            }

            var dashboard = new UserDashboardDTO
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email!,
                PhoneNumber = user.PhoneNumber,
                EmailConfirmed = user.EmailConfirmed,
                CreatedAt = user.CreatedAt,

                // TODO:
                // Replace with:
                // await _documentService.GetDocumentCountAsync(userId)
                TotalDocuments = 0,


                // TODO:
                // Replace with:
                // await _conversationService.GetConversationCountAsync(userId)
                TotalConversations = 0,


                // TODO:
                // Replace with:
                // await _messageService.GetMessageCountAsync(userId)
                TotalMessages = 0,


                TokensUsed = user.TokensConsumedCurrentMonth,
                TokenLimit = user.MonthlyTokenAllowance
            };

            return ServiceResult<UserDashboardDTO>.Success(dashboard);
        }
    }
}
