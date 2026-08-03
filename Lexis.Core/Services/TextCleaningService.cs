using Lexis.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Lexis.Core.Services
{
    public class TextCleaningService : ITextCleaningService
    {
        public string Clean(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Unicode normalization
            text = text.Normalize(NormalizationForm.FormKC);

            // Replace strange whitespace characters
            text = text
                .Replace("\u00A0", " ")
                .Replace("\t", " ");

            // Normalize line endings
            text = text.Replace("\r\n", "\n");

            // Remove excessive spaces
            text = Regex.Replace(text, @"[ ]{2,}", " ");

            // Remove excessive empty lines
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            // Remove lines made only of punctuation/symbols
            text = Regex.Replace(text, @"^[^\p{L}\p{N}]+$", "", RegexOptions.Multiline);

            return text.Trim();
        }
    }
}