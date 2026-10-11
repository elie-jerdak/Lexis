using Lexis.API.Common;
using Lexis.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Lexis.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestController : BaseApiController
    {
        private readonly IEmailService _emailService;
        private readonly IEmbeddingService _embeddingService;

        public TestController(IEmailService emailService, IEmbeddingService embeddingService)
        {
            _emailService = emailService;
            _embeddingService = embeddingService;
        }

        [HttpGet("public")]
        public IActionResult Public()
        {
            return Ok(new
            {
                message = "This endpoint is public."
            });
        }


        [Authorize]
        [HttpGet("protected")]
        public IActionResult Protected()
        {
            return Ok(new
            {
                message = "You are authenticated.",
                user = User.Identity?.Name
            });
        }


        [Authorize(Roles = "User")]
        [HttpGet("user-only")]
        public IActionResult UserOnly()
        {
            return Ok(new
            {
                message = "You have the User role."
            });
        }

        [HttpGet("test-email")]
        public async Task<IActionResult> TestEmail()
        {
            //await _emailService.SendEmailAsync(
            //    "jardakelie@gmail.com",
            //    "Lexi Test Email",
            //    "<h1>Hello from Lexi 🚀</h1>"
            //);

            return Ok("Email sent");
        }

        [Authorize]
        [HttpPost("test-upload")]
        public async Task<IActionResult> TestUpload(IFormFile file, [FromServices] IFileStorageService fileStorageService)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new
                {
                    message = "No file was uploaded."
                });
            }

            if (file.ContentType != "application/pdf")
            {
                return BadRequest(new
                {
                    message = "Only PDF files are allowed."
                });
            }

            //var filePath = await fileStorageService.SaveFileAsync(file, GetUserId());

            return Ok(new
            {
                message = "File uploaded successfully.",
                //path = filePath
            });
        }

        [HttpGet("test-embedding")]
        public async Task TestEmbeddingService()
        {
            var vectors = await _embeddingService.GenerateAsync(
                new[]
                {
            "A contract may be terminated after a material breach.",
            "PostgreSQL is a relational database."
                });

            Console.WriteLine($"Vectors returned: {vectors.Count}");

            if (vectors.Count > 0)
            {
                Console.WriteLine($"Dimensions: {vectors[0].Length}");
            }
        }
    }
}
