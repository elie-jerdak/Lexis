using Microsoft.AspNetCore.Http;

namespace Lexis.Core.Interfaces
{
    public interface IFileHashService
    {
        Task<string> ComputeSha256Async(IFormFile file);
    }
}
