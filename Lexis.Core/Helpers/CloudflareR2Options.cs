namespace Lexis.Core.Helpers
{
    public sealed class CloudflareR2Options
    {
        public const string SectionName = "CloudflareR2";

        public string ServiceUrl { get; set; } = string.Empty;

        public string BucketName { get; set; } = string.Empty;

        public string AccessKeyId { get; set; } = string.Empty;

        public string SecretAccessKey { get; set; } = string.Empty;
    }
}
