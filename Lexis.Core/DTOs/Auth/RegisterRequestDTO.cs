using System.ComponentModel.DataAnnotations;

namespace Lexis.Core.DTOs.Auth
{
    public class RegisterRequestDTO
    {
        [Required(ErrorMessage = "First name is required.")]
        [StringLength(50, ErrorMessage = "First name cannot exceed {1} characters.")]
        public string FirstName { get; set; } = null!;

        [Required(ErrorMessage = "Last name is required.")]
        [StringLength(50, ErrorMessage = "Last name cannot exceed {1} characters.")]
        public string LastName { get; set; } = null!;

        [Required(ErrorMessage = "Email address is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address format.")]
        [StringLength(100, ErrorMessage = "Email cannot exceed {1} characters.")]
        public string Email { get; set; } = null!;

        [Phone(ErrorMessage = "Invalid phone number format.")]
        public string? PhoneNumber { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(13, MinimumLength = 8, ErrorMessage = "Password must be between {2} and {1} characters long.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = null!;
    }
}
