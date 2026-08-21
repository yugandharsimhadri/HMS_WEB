using System.Text;

namespace SivayaanHMS.Data.Import;

/// <summary>
/// A minimal CSV reader that handles quoted fields, doubled quotes and embedded
/// commas or newlines. Vendor exports are not always well behaved, and pulling in
/// a parsing library for one file format is not worth the dependency.
/// </summary>
public sealed class CsvFile
{
    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<CsvRow> Rows { get; }

    private CsvFile(List<string> headers, List<CsvRow> rows)
    {
        Headers = headers;
        Rows = rows;
    }

    public static CsvFile Load(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    public static CsvFile Parse(string content)
    {
        var records = SplitRecords(content);

        if (records.Count == 0)
            return new CsvFile([], []);

        var headers = records[0].Select(h => h.Trim()).ToList();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < headers.Count; i++)
            index.TryAdd(headers[i], i);

        var rows = new List<CsvRow>();

        for (var i = 1; i < records.Count; i++)
        {
            var fields = records[i];

            // Trailing blank lines are normal in vendor exports.
            if (fields.All(string.IsNullOrWhiteSpace)) continue;

            // +2: header is record 0, and spreadsheets number from 1.
            rows.Add(new CsvRow(index, fields, i + 1));
        }

        return new CsvFile(headers, rows);
    }

    public bool HasColumn(string name) => Headers.Any(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));

    private static List<List<string>> SplitRecords(string content)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;

                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;

                case '\r':
                    break;   // handled by the \n that follows

                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add(fields);
                    fields = [];
                    break;

                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields);
        }

        return records;
    }
}

/// <summary>One CSV record, addressable by column name.</summary>
public sealed class CsvRow(Dictionary<string, int> index, List<string> fields, int lineNumber)
{
    public int LineNumber { get; } = lineNumber;

    /// <summary>Raw text of a column, or null when the column is absent or blank.</summary>
    public string? this[string column]
    {
        get
        {
            if (!index.TryGetValue(column, out var i) || i >= fields.Count) return null;

            var value = fields[i].Trim();
            return value.Length == 0 ? null : value;
        }
    }
}
