namespace Lexis.Core.Interfaces
{
    public interface IEmailService
    {
        Task SendEmailAsync(string recipient, string subject, string templateName, Dictionary<string, string> placeholders);
    }
}
