namespace Lexis.Core.Enums
{
    public sealed record DocumentClaim(Guid DocumentId, Guid LeaseId, int Attempt);
}
