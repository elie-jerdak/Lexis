using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.DTOs.Auth
{
    public class ConfirmEmailRequestDTO
    {
        public int UserId { get; set; }

        public string Token { get; set; } = string.Empty;
    }
}
