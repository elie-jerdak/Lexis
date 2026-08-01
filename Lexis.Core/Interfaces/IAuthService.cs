using Lexis.Core.DTOs.Auth;
using Lexis.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Lexis.Core.Interfaces
{
    public interface IAuthService
    {
        Task<ServiceResult<AuthResponseDTO>> RegisterAsync(RegisterRequestDTO request);
        Task<ServiceResult<AuthResponseDTO>> LoginAsync(LoginRequestDTO request);
        Task<ServiceResult<AuthResponseDTO>> RefreshTokenAsync(RefreshTokenRequestDTO request);
        Task<ServiceResult<bool>> LogoutAsync(LogoutRequestDTO request);
        Task<ServiceResult<bool>> ConfirmEmailAsync(ConfirmEmailRequestDTO request);
        Task<ServiceResult<bool>> ResendConfirmationEmailAsync(ResendConfirmationEmailRequestDTO request);
        Task<ServiceResult<bool>> ForgotPasswordAsync(ForgotPasswordRequestDTO request);

        //ResetPassword → user is not authenticated, proves ownership through email token.
        //ChangePassword → user is already logged in, proves ownership by providing the current password.

        Task<ServiceResult<bool>> ResetPasswordAsync(ResetPasswordRequestDTO request);
        Task<ServiceResult<bool>> ChangePasswordAsync(int userId, ChangePasswordRequestDTO request);


    }
}
