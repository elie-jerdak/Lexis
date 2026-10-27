using Lexis.Core.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Lexis.Core.Services
{
    public class LocalFileStorageService : IFileStorageService
    {
        private readonly IWebHostEnvironment _environment;

        public LocalFileStorageService(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public async Task<string> SaveFileAsync(IFormFile file, int userId)
        {
            var uploadsFolder = Path.Combine(
                _environment.ContentRootPath,
                "Storage",
                "Documents",
                userId.ToString());

            Directory.CreateDirectory(uploadsFolder);

            var extension = Path.GetExtension(file.FileName);

            var storedFileName = $"{Guid.NewGuid()}{extension}";

            var fullPath = Path.Combine(uploadsFolder, storedFileName);

            await using var stream = new FileStream(fullPath, FileMode.Create);

            await file.CopyToAsync(stream);

            return Path.Combine(
                "Storage",
                "Documents",
                userId.ToString(),
                storedFileName)
                .Replace("\\", "/");
        }

        public Task<Stream> GetFileAsync(string filePath)
        {
            var fullPath = Path.Combine(_environment.ContentRootPath, filePath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "Document not found.");
            }

            Stream stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            return Task.FromResult(stream);
        }

        public Task DeleteFileAsync(string filePath)
        {
            var fullPath = Path.Combine(_environment.ContentRootPath, filePath);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            return Task.CompletedTask;
        }
    }

}
