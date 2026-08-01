using Lexis.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Interfaces
{
    public interface IJwtService
    {
        string GenerateAccessToken(ApplicationUser user, IList<string> roles);

        RefreshToken GenerateRefreshToken(int userId);
    }
}
