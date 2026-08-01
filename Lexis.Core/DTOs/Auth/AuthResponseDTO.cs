using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.DTOs.Auth
{
    public class AuthResponseDTO
    {
        public string AccessToken { get; set; } = null!;

        public string RefreshToken { get; set; } = null!;

        public DateTime AccessTokenExpiration { get; set; }

        public string Message { get; set; }
    }
}
