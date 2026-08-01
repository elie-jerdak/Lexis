namespace Lexis.Core.Helpers
{
    public class ResendSettings
    {
        public const string SectionName = "Resend";

        public string ApiKey { get; set; } = string.Empty;

        public string FromEmail { get; set; } = string.Empty;

        public string FromName { get; set; } = string.Empty;
    }
}
