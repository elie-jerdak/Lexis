using Microsoft.AspNetCore.Http;

namespace Lexis.Core.Interfaces
{
    public interface IFileStorageService
    {
        Task<string> SaveFileAsync(IFormFile file, int userId);
        Task DeleteFileAsync(string filePath);
        Task<Stream> GetFileAsync(string filePath);
    }
}
