using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.DTOs.Auth
{
    public class ResendConfirmationEmailRequestDTO
    {
        public string Email { get; set; } = string.Empty;
    }
}
