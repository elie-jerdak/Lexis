using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.DTOs.Auth
{
    public class RefreshTokenRequestDTO
    {
        public string RefreshToken { get; set; } = string.Empty;
    }
}
