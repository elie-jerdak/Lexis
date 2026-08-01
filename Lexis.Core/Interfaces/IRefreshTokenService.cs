using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Interfaces
{
    public interface IRefreshTokenService
    {
        Task RevokeUserRefreshTokensAsync(int userId);
    }
}
