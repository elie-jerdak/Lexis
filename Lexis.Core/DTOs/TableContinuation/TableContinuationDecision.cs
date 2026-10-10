using Lexis.Core.Enums;

namespace Lexis.Core.DTOs.TableContinuation
{
    public sealed class TableContinuationDecision
    {
        public TableContinuationVerdict Verdict { get; }

        public string Reason { get; }

        public IReadOnlyList<TableContinuationEvidence> Evidence { get; }

        private TableContinuationDecision(TableContinuationVerdict verdict, string reason, 
            IReadOnlyList<TableContinuationEvidence> evidence)
        {
            Verdict = verdict;
            Reason = reason;
            Evidence = evidence;
        }

        public static TableContinuationDecision Continue(string reason, IEnumerable<TableContinuationEvidence> evidence)
        {
            return new TableContinuationDecision(TableContinuationVerdict.Continue, reason, evidence.ToList());
        }

        public static TableContinuationDecision Reject(string reason, IEnumerable<TableContinuationEvidence> evidence)
        {
            return new TableContinuationDecision(TableContinuationVerdict.Reject, reason, evidence.ToList());
        }

        public static TableContinuationDecision Unknown(string reason, IEnumerable<TableContinuationEvidence> evidence)
        {
            return new TableContinuationDecision(TableContinuationVerdict.Unknown, reason, evidence.ToList());
        }
    }
}