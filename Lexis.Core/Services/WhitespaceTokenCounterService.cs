using Lexis.Core.Interfaces;

namespace Lexis.Core.Services
{
    public class WhitespaceTokenCounterService : ITokenCounter
    {
        public int Count(string text) =>
            string.IsNullOrWhiteSpace(text)
                ? 0
                : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
