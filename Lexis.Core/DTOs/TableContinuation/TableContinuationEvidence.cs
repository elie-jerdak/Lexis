namespace Lexis.Core.DTOs.TableContinuation
{
    public sealed class TableContinuationEvidence
    {
        // ------------------------------------------------------------------
        // Evidence classification
        // ------------------------------------------------------------------

        public EvidenceKind Kind { get; set; }

        // Legacy textual category used by the existing analyzer.
        public string Category { get; set; } = string.Empty;

        // ------------------------------------------------------------------
        // Evidence details
        // ------------------------------------------------------------------

        public string Name { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;

        // New terminology aliases.
        public string Reason
        {
            get => Detail;
            set => Detail = value;
        }

        public string Source
        {
            get => Name;
            set => Name = value;
        }

        // ------------------------------------------------------------------
        // Veto information
        // ------------------------------------------------------------------

        public VetoStrength? VetoStrength { get; set; }

        // ------------------------------------------------------------------
        // Constructors
        // ------------------------------------------------------------------

        // Required for object initializers.
        public TableContinuationEvidence()
        {
        }

        // Constructor for the newer API.
        public TableContinuationEvidence(EvidenceKind kind, string reason, string source = "",
            VetoStrength? vetoStrength = null)
        {
            Kind = kind;
            Category = kind.ToString();
            Detail = reason;
            Name = source;
            VetoStrength = vetoStrength;
        }

        // ------------------------------------------------------------------
        // Factory methods
        // ------------------------------------------------------------------

        public static TableContinuationEvidence Confirmation(string reason, string source = "")
        {
            return new TableContinuationEvidence
            {
                Kind = EvidenceKind.Confirmation,
                Category = nameof(EvidenceKind.Confirmation),
                Detail = reason,
                Name = source
            };
        }

        public static TableContinuationEvidence Compatibility(string reason, string source = "")
        {
            return new TableContinuationEvidence
            {
                Kind = EvidenceKind.Compatibility,
                Category = nameof(EvidenceKind.Compatibility),
                Detail = reason,
                Name = source
            };
        }

        public static TableContinuationEvidence Context(string reason, string source = "")
        {
            return new TableContinuationEvidence
            {
                Kind = EvidenceKind.Context,
                Category = nameof(EvidenceKind.Context),
                Detail = reason,
                Name = source
            };
        }

        public static TableContinuationEvidence Veto(string reason, string source, VetoStrength strength)
        {
            return new TableContinuationEvidence
            {
                Kind = EvidenceKind.Veto,
                Category = nameof(EvidenceKind.Veto),
                Detail = reason,
                Name = source,
                VetoStrength = strength
            };
        }
    }
}