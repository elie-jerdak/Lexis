using System.ComponentModel.DataAnnotations;

namespace Lexis.Core.DTOs.Auth
{
    public class LoginRequestDTO
    {
        [Required(ErrorMessage = "Email address is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address format.")]
        public string Email { get; set; } = null!;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = null!;
    }
}
