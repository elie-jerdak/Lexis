using Lexis.Core.Interfaces;
using Lexis.Core.Helpers;
using Microsoft.Extensions.Options;
using Resend;

namespace Lexis.Core.Services
{
    public class EmailService : IEmailService
    {
        private readonly IResend _resend;
        private readonly ResendSettings _settings;
        private readonly IWebHostEnvironment _environment;

        public EmailService(ResendSettings settings, IResend resend,  IWebHostEnvironment environment)
        {
            _settings = settings;
            _resend = resend;
            _environment = environment;
        }


        public async Task SendEmailAsync(string recipient, string subject, string templateName, Dictionary<string, string> placeholders)
        {
            var templatePath = Path.Combine(
                _environment.ContentRootPath,
                "Email Templates",
                $"{templateName}.html");


            if (!File.Exists(templatePath))
            {
                throw new FileNotFoundException(
                    $"Email template '{templateName}' not found.");
            }


            var htmlContent = await File.ReadAllTextAsync(templatePath);


            foreach (var placeholder in placeholders)
            {
                htmlContent = htmlContent.Replace(
                    $"{{{{{placeholder.Key}}}}}",
                    placeholder.Value);
            }

            var message = new EmailMessage
            {
                From = string.IsNullOrWhiteSpace(_settings.FromName)
                ? _settings.FromEmail
                : $"{_settings.FromName} <{_settings.FromEmail}>",

                Subject = subject,
                HtmlBody = htmlContent
            };


            message.To.Add(recipient);


            await _resend.EmailSendAsync(message);
        }
    }
}
