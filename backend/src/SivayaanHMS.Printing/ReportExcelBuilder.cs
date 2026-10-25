using ClosedXML.Excel;

namespace SivayaanHMS.Printing;

/// <summary>
/// An .xlsx workbook of whichever report is on screen — the desktop's
/// <c>ReportExcelBuilder</c>: the same title block, header row, number formats,
/// live TOTAL row, labelled extras underneath, autofilter and frozen header.
///
/// Cells are written <b>typed</b> — a money column is a real currency cell that
/// sums and sorts, not text that looks like one. That is the point of offering
/// Excel alongside the PDF: the PDF is for reading, the workbook is for working with.
/// </summary>
public static class ReportExcelBuilder
{
    private const string CurrencyFormat = "\"₹\"#,##0.00";
    private const string IntFormat = "#,##0";
    private const string DateFormat = "dd-mmm-yyyy";
    private const string TimeFormat = "hh:mm";
    private const string DateTimeFormat = "dd-mmm-yyyy hh:mm";
    private const string PercentFormat = "0.0\"%\"";

    private static readonly XLColor Muted = XLColor.FromHtml("#61707E");

    public static byte[] Generate(ReportTable report, string clinicName)
    {
        var table = report.ForExcel;

        using var workbook = new XLWorkbook();

        // Excel refuses a sheet name over 31 characters or containing
        // : \ / ? * [ ] — every report title here is short and plain, but
        // trimming is cheaper than a corrupt file if one ever is not.
        var sheetName = table.Title.Length > 31 ? table.Title[..31] : table.Title;
        var sheet = workbook.Worksheets.Add(sheetName);

        var columns = Math.Max(table.Columns.Count, 1);
        var headerRow = WriteTitle(sheet, table, clinicName, columns);

        for (var c = 0; c < table.Columns.Count; c++)
            sheet.Cell(headerRow, c + 1).Value = table.Columns[c].Header;

        var row = headerRow + 1;
        var firstData = row;

        foreach (var data in table.Rows)
        {
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var column = table.Columns[c];
                var cell = sheet.Cell(row, c + 1);

                if (column.Formula is not null) WriteFormula(cell, column, row);
                else Write(cell, c < data.Cells.Count ? data.Cells[c] : null, column.Format);
            }

            if (data.Emphasise)
            {
                var range = sheet.Range(row, 1, row, columns);
                range.Style.Font.FontColor = XLColor.FromHtml("#B42318");
                range.Style.Font.Bold = true;
            }

            row++;
        }

        var lastData = row - 1;

        if (lastData >= firstData && table.Columns.Any(c => c.Sum))
        {
            BoldLabel(sheet, row, 1, table.TotalLabel);

            for (var c = 0; c < table.Columns.Count; c++)
            {
                if (!table.Columns[c].Sum) continue;

                var cell = sheet.Cell(row, c + 1);
                var letter = cell.Address.ColumnLetter;
                cell.FormulaA1 = $"=SUM({letter}{firstData}:{letter}{lastData})";
                cell.Style.NumberFormat.Format = table.Columns[c].Format == ReportFormat.Money ? CurrencyFormat : IntFormat;
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }

            row++;
        }

        // The labelled figures under the table — "Cash total", "Total OPD visits" — each a label
        // in the first column and its value in the second.
        if (table.Totals.Count > 0)
        {
            row++;
            foreach (var total in table.Totals)
            {
                BoldLabel(sheet, row, 1, total.Label);
                Write(sheet.Cell(row, 2), total.Value, total.Format);
                row++;
            }
        }

        // A frozen header and an autofilter are what make this a workbook
        // somebody can actually work in rather than a picture of a table.
        var header = sheet.Range(headerRow, 1, headerRow, columns);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2F5");
        header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        if (lastData >= headerRow) sheet.Range(headerRow, 1, lastData, columns).SetAutoFilter();

        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns(1, columns).AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static int WriteTitle(IXLWorksheet sheet, ReportTable table, string clinicName, int columns)
    {
        sheet.Cell(1, 1).Value = clinicName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        sheet.Cell(2, 1).Value = "OPD & Pharmacy";
        sheet.Cell(2, 1).Style.Font.FontColor = Muted;

        sheet.Cell(3, 1).Value = table.Title;
        sheet.Cell(3, 1).Style.Font.Bold = true;
        sheet.Cell(3, 1).Style.Font.FontSize = 12;

        sheet.Cell(4, 1).Value = table.DateLabel;
        sheet.Cell(4, 1).Style.Font.FontColor = Muted;

        for (var r = 1; r <= 4; r++) sheet.Range(r, 1, r, columns).Merge();

        return 6; // the first header row
    }

    private static void BoldLabel(IXLWorksheet sheet, int row, int col, string text)
    {
        var cell = sheet.Cell(row, col);
        cell.Value = text;
        cell.Style.Font.Bold = true;
    }

    /// <summary>Live rather than pre-computed, so the workbook stays auditable if a reader edits a
    /// quantity or a rate while reviewing it.</summary>
    private static void WriteFormula(IXLCell cell, ReportColumn column, int row)
    {
        var a1 = System.Text.RegularExpressions.Regex.Replace(
            column.Formula!, @"\{(\d+)\}",
            m => XLHelper.GetColumnLetterFromNumber(int.Parse(m.Groups[1].Value)) + row);

        cell.FormulaA1 = "=" + a1;
        cell.Style.NumberFormat.Format = column.Format == ReportFormat.Money ? CurrencyFormat : IntFormat;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
    }

    private static void Write(IXLCell cell, object? value, ReportFormat format)
    {
        if (value is null)
        {
            cell.Value = "";
            return;
        }

        switch (format)
        {
            case ReportFormat.Money:
                cell.Value = ReportFormats.ToDecimal(value);
                cell.Style.NumberFormat.Format = CurrencyFormat;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                break;
            case ReportFormat.Number:
            case ReportFormat.Integer:
                cell.Value = ReportFormats.ToDecimal(value);
                cell.Style.NumberFormat.Format = IntFormat;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                break;
            case ReportFormat.Percent:
                cell.Value = ReportFormats.ToDecimal(value);
                cell.Style.NumberFormat.Format = PercentFormat;
                break;
            case ReportFormat.Date:
            case ReportFormat.Time:
            case ReportFormat.DateTime:
                if (ReportFormats.ToDate(value) is { } dt)
                {
                    cell.Value = dt;
                    cell.Style.NumberFormat.Format = format switch
                    {
                        ReportFormat.Date => DateFormat,
                        ReportFormat.Time => TimeFormat,
                        _ => DateTimeFormat
                    };
                }
                else cell.Value = value.ToString();
                break;
            default:
                cell.Value = value.ToString();
                break;
        }
    }
}
