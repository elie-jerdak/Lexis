using Lexis.Core.DTOs.User;
using Lexis.Core.DTOs.Users;
using Lexis.Core.Helpers;

namespace Lexis.Core.Interfaces
{
    public interface IUserService
    {
        Task<ServiceResult<UserProfileDTO>> GetProfileAsync(int userId);

        Task<ServiceResult<bool>> UpdateProfileAsync(int userId, UpdateProfileRequestDTO request);

        Task<ServiceResult<bool>> DeactivateAccountAsync(int userId);

        Task<ServiceResult<UserDashboardDTO>> GetDashboardAsync(int userId);
         
    }
}
