namespace Lexis.Core.DTOs.Auth
{
    public class ResetPasswordRequestDTO
    {
        public string Email { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;

        public string NewPassword { get; set; } = string.Empty;
    }
}
