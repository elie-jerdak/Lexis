using Lexis.API.Common;
using Lexis.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Lexis.API.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    public class DocumentsController : BaseApiController
    {
        private readonly IDocumentService _documentService;
        private readonly IFileStorageService _fileStorageService;

        public DocumentsController(IDocumentService documentService, IFileStorageService fileStorageService)
        {
            _documentService = documentService;
            _fileStorageService = fileStorageService;
        }


        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            var userId = GetUserId();

            if (userId == null)
            {
                return Unauthorized(new
                {
                    message = "User authentication information is missing.",
                    code = "USER_ID_NOT_FOUND"
                });
            }

            var result = await _documentService.UploadDocumentAsync(userId.Value, file);


            if (!result.IsSuccess)
            {
                return BadRequest(new
                {
                    message = result.ErrorMessage,
                    code = result.ErrorCode
                });
            }


            return Ok(result.Data);
        }

        [HttpGet]
        public async Task<IActionResult> GetMyDocuments()
        {
            var userId = GetUserId();

            if (userId == null)
            {
                return Unauthorized(new
                {
                    message = "User information could not be retrieved.",
                    code = "USER_ID_NOT_FOUND"
                });
            }


            var result = await _documentService.GetUserDocumentsAsync(userId.Value);


            if (!result.IsSuccess)
            {
                return BadRequest(new
                {
                    message = result.ErrorMessage,
                    code = result.ErrorCode
                });
            }


            return Ok(result.Data);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetDocument(Guid id)
        {
            var userId = GetUserId();

            if (userId == null)
                return Unauthorized();


            var result = await _documentService
                .GetDocumentByIdAsync(userId.Value, id);


            if (!result.IsSuccess)
                return NotFound(result);


            return Ok(result.Data);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDocument(Guid id)
        {
            var userId = GetUserId();

            if (userId == null)
                return Unauthorized();


            var result = await _documentService
                .DeleteDocumentAsync(userId.Value, id);


            if (!result.IsSuccess)
                return NotFound(result);


            return Ok(new
            {
                message = "Document deleted successfully."
            });
        }

        [HttpGet("{id}/download")]
        public async Task<IActionResult> Download(Guid id)
        {
            var userId = GetUserId();

            if (userId == null)
                return Unauthorized();

            var result = await _documentService
                .GetDocumentForDownloadAsync(userId.Value, id);

            if (!result.IsSuccess)
                return NotFound(result);

            var document = result.Data;

            var fileStream = await _fileStorageService.GetFileAsync(document.FilePath);

            return File(fileStream, document.MimeType, document.FileName);
        }
    }
}
