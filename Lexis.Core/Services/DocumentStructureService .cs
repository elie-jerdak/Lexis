using Lexis.Core.DTOs.DocumentProcessing;
using Lexis.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Lexis.Core.Services
{
    public class DocumentStructureService : IDocumentStructureService
    {
        private const double RepeatThreshold = 0.7;


        public HashSet<string> FindRepeatedHeaders(List<ExtractedPage> pages)
        {
            var headers = pages
                .SelectMany(page => page.Elements)
                .Where(element =>
                    element.ElementType == "section_header" &&
                    !string.IsNullOrWhiteSpace(element.Text) &&
                    element.Text.Trim().Length > 2)
                .Select(element => Normalize(element.Text!))
                .ToList();


            return headers
                .GroupBy(header => header)
                .Where(group =>
                    group.Count() >= pages.Count * RepeatThreshold)
                .Select(group => group.Key)
                .ToHashSet();
        }


        public HashSet<string> FindRepeatedFooters(List<ExtractedPage> pages)
        {
            /*
             * Footers are usually the last elements on a page.
             * We use pageOrder/order to identify them.
             */

            var footers = pages
                .SelectMany(page =>
                    page.Elements
                        .OrderByDescending(e => e.Order)
                        .Take(3))
                .Where(element =>
                    element != null &&
                    !string.IsNullOrWhiteSpace(element.Text))
                .Select(element => Normalize(element.Text!))
                .ToList();


            return footers
                .GroupBy(footer => footer)
                .Where(group =>
                    group.Count() >= pages.Count * RepeatThreshold)
                .Select(group => group.Key)
                .ToHashSet();
        }


        public bool IsPageNumber(DocumentElement element)
        {
            if (string.IsNullOrWhiteSpace(element.Text))
                return false;


            if (element.ElementType == "page_number")
                return true;


            return Regex.IsMatch(
                element.Text.Trim(),
                @"^(page[:\s]*)?[-–—]?\s*\d+\s*[-–—.]?$",
                RegexOptions.IgnoreCase);
        }

        public bool IsRepeatedHeader(DocumentElement element,  HashSet<string> repeatedHeaders)
        {
            return repeatedHeaders.Contains(Normalize(element.Text ?? ""));
        }


        public bool IsRepeatedFooter(DocumentElement element, HashSet<string> repeatedFooters)
        {
            return repeatedFooters.Contains(Normalize(element.Text ?? ""));
        }

        private static string Normalize(string text)
        {
            return Regex.Replace(text.Trim(), @"\s+", " ");
        }
    }
}
