using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Interfaces
{
    public interface IEmbeddingService
    {
        Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
    }
}
