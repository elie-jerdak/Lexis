using NpgsqlTypes;

namespace Lexis.Core.Enums
{
    public enum MessageRole
    {
        [PgName("User")]
        User,
        [PgName("Assistant")]
        Assistant
    }
}
