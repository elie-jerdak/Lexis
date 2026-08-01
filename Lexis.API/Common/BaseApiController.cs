using Lexis.Core.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace Lexis.API.Common
{
    [ApiController]
    public abstract class BaseApiController : ControllerBase
    {
        protected int? GetUserId()
        {
            return User.GetUserId();
        }
    }
}
