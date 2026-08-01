using Lexis.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;

namespace Lexis.Core.Services
{
    public class FileHashService : IFileHashService
    {
        public async Task<string> ComputeSha256Async(IFormFile file)
        {
            using var sha256 = SHA256.Create();

            await using var stream = file.OpenReadStream();

            var hash = await sha256.ComputeHashAsync(stream);

            return Convert.ToHexString(hash);
        }
    }
}
