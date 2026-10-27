using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Helpers
{
    public sealed class DocumentProcessingOptions
    {
        public const string SectionName = "DocumentProcessing";

        public int MaxConcurrency { get; set; } = 2;

        public int MaxAttempts { get; set; } = 3;

        public int LeaseDurationSeconds { get; set; } = 300;

        public int LeaseRenewalSeconds { get; set; } = 60;

        public int IdleDelaySeconds { get; set; } = 5;
    }
}
