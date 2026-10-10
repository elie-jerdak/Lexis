namespace Lexis.Core.DTOs.TableContinuation
{
    public sealed class TableGrid
    {
        public IReadOnlyList<IReadOnlyList<string>> Rows { get; }
        public int ColumnCount { get; }
        public IReadOnlyList<string>? Header { get; }
        public string? CaptionText { get; }

        public TableGrid(IReadOnlyList<List<string>> rows, int columnCount, IReadOnlyList<string>? header, string? captionText = null)
        {
            Rows = rows.Select(x => (IReadOnlyList<string>)x).ToList();
            ColumnCount = columnCount;
            Header = header;
            CaptionText = captionText;
        }
    }
}
