using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Interfaces
{
    /// <summary>
    /// Swap-in point for a real tokenizer. The default implementation below
    /// keeps the old word-split placeholder so the project compiles without
    /// a new dependency, but it undercounts real BPE tokens by roughly
    /// 25-30% versus something like cl100k_base — replace before relying on
    /// MaxChunkTokens as an actual safety margin against a model's context
    /// window.
    ///
    /// To use a real tokenizer, add the SharpToken NuGet package and swap in:
    ///
    ///   public class TiktokenCounter : ITokenCounter
    ///   {
    ///       private readonly SharpToken.GptEncoding _encoding =
    ///           SharpToken.GptEncoding.GetEncoding("cl100k_base");
    ///       public int Count(string text) =>
    ///           string.IsNullOrEmpty(text) ? 0 : _encoding.Encode(text).Count;
    ///   }
    /// </summary>
    public interface ITokenCounter
    {
        int Count(string text);
    }
}
