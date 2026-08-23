using ClosedXML.Excel;

namespace SivayaanHMS.Printing;

/// <summary>
/// An .xlsx workbook of whichever report is on screen, from the same
/// <see cref="ReportTable"/> the screen and the PDF were given.
///
/// Cells are written **typed** — a money column is a real currency cell that
/// sums and sorts, not text that looks like one. That is the whole point of
/// offering Excel alongside the PDF: the PDF is for reading, the workbook is
/// for working with.
/// </summary>
public static class ReportExcelBuilder
{
    private const string CurrencyFormat = "\"₹\"#,##0.00";
    private const string NumberFormat = "#,##0.00";
    private const string IntFormat = "#,##0";
    private const string DateFormat = "dd-mmm-yyyy";
    private const string TimeFormat = "hh:mm";
    private const string DateTimeFormat = "dd-mmm-yyyy hh:mm";

    public static byte[] Generate(ReportTable table, string clinicName)
    {
        using var workbook = new XLWorkbook();

        // Excel refuses a sheet name over 31 characters or containing
        // : \ / ? * [ ] — every report title here is short and plain, but
        // trimming is cheaper than a corrupt file if one ever is not.
        var sheetName = table.Title.Length > 31 ? table.Title[..31] : table.Title;
        var sheet = workbook.Worksheets.Add(sheetName);

        var columnCount = Math.Max(table.Columns.Count, 1);
        var row = 1;

        sheet.Cell(row, 1).Value = clinicName;
        sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Range(row, 1, row, columnCount).Merge();
        row++;

        sheet.Cell(row, 1).Value = table.Title;
        sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(12);
        sheet.Range(row, 1, row, columnCount).Merge();
        row++;

        sheet.Cell(row, 1).Value = table.DateLabel;
        sheet.Cell(row, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
        sheet.Range(row, 1, row, columnCount).Merge();
        row += 2;

        var headerRow = row;
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = table.Columns[c].Header;
            cell.Style.Font.SetBold();
            cell.Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xEE, 0xF2, 0xF4));
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cell.Style.Alignment.Horizontal = Horizontal(table.Columns[c].Align);
        }
        row++;

        foreach (var dataRow in table.Rows)
        {
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var column = table.Columns[c];
                var value = c < dataRow.Cells.Count ? dataRow.Cells[c] : null;
                var cell = sheet.Cell(row, c + 1);

                Write(cell, value, column.Format);
                cell.Style.Alignment.Horizontal = Horizontal(column.Align);
                if (dataRow.Emphasise) cell.Style.Font.SetBold();
            }
            row++;
        }

        if (table.Totals.Count > 0)
        {
            row++;
            foreach (var total in table.Totals)
            {
                // Label in the second-to-last column, value in the last, so
                // the totals line up under the figures they total.
                var labelCol = Math.Max(1, columnCount - 1);
                sheet.Cell(row, labelCol).Value = total.Label;
                sheet.Cell(row, labelCol).Style.Font.SetBold();
                sheet.Cell(row, labelCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                var valueCell = sheet.Cell(row, columnCount);
                Write(valueCell, total.Value, total.Format);
                valueCell.Style.Font.SetBold();
                valueCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                row++;
            }
        }

        // A frozen header and an autofilter are what make this a workbook
        // somebody can actually work in rather than a picture of a table.
        if (table.Rows.Count > 0)
        {
            sheet.Range(headerRow, 1, headerRow + table.Rows.Count, columnCount).SetAutoFilter();
        }
        sheet.SheetView.FreezeRows(headerRow);

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
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
                cell.Value = ReportPdfBuilder.ToDecimal(value);
                cell.Style.NumberFormat.Format = CurrencyFormat;
                break;
            case ReportFormat.Number:
                cell.Value = ReportPdfBuilder.ToDecimal(value);
                cell.Style.NumberFormat.Format = NumberFormat;
                break;
            case ReportFormat.Integer:
                cell.Value = ReportPdfBuilder.ToDecimal(value);
                cell.Style.NumberFormat.Format = IntFormat;
                break;
            case ReportFormat.Date:
            case ReportFormat.Time:
            case ReportFormat.DateTime:
                if (ReportPdfBuilder.ToDate(value) is { } dt)
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

    private static XLAlignmentHorizontalValues Horizontal(ReportAlign align) => align switch
    {
        ReportAlign.Right => XLAlignmentHorizontalValues.Right,
        ReportAlign.Center => XLAlignmentHorizontalValues.Center,
        _ => XLAlignmentHorizontalValues.Left
    };
}
