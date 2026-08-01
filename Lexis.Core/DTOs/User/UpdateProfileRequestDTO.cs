namespace Lexis.Core.DTOs.Users
{
    public class UpdateProfileRequestDTO
    {
        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }
    }
}
