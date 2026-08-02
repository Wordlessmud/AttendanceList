using System.IO.Compression;
using System.Text;
using System.Xml;

namespace AttendanceList.Services;

internal sealed record XlsxCell(object? Value, int Style = 0);

internal sealed class XlsxSheet
{
    public required string Name { get; init; }
    public List<List<XlsxCell>> Rows { get; } = [];
    public List<string> Merges { get; } = [];

    public void AddRow(IEnumerable<object?> values, int style = 0) =>
        Rows.Add(values.Select(value => new XlsxCell(value, style)).ToList());

    public void AddRow(params object?[] values) => AddRow(values.AsEnumerable());
}

internal static class SimpleXlsxWriter
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void Write(string path, IReadOnlyList<XlsxSheet> sheets)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", writer => WriteContentTypes(writer, sheets.Count));
        WriteEntry(archive, "_rels/.rels", WriteRootRelationships);
        WriteEntry(archive, "xl/workbook.xml", writer => WriteWorkbook(writer, sheets));
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", writer => WriteWorkbookRelationships(writer, sheets.Count));
        WriteEntry(archive, "xl/styles.xml", WriteStyles);
        for (var index = 0; index < sheets.Count; index++)
        {
            var sheet = sheets[index];
            WriteEntry(archive, $"xl/worksheets/sheet{index + 1}.xml", writer => WriteSheet(writer, sheet));
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, Action<XmlWriter> write)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            CloseOutput = false
        });
        write(writer);
    }

    private static void WriteContentTypes(XmlWriter writer, int sheetCount)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
        writer.WriteStartElement("Default");
        writer.WriteAttributeString("Extension", "rels");
        writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
        writer.WriteEndElement();
        writer.WriteStartElement("Default");
        writer.WriteAttributeString("Extension", "xml");
        writer.WriteAttributeString("ContentType", "application/xml");
        writer.WriteEndElement();
        WriteOverride(writer, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        WriteOverride(writer, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        for (var index = 1; index <= sheetCount; index++)
        {
            WriteOverride(writer, $"/xl/worksheets/sheet{index}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        }
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteOverride(XmlWriter writer, string partName, string contentType)
    {
        writer.WriteStartElement("Override");
        writer.WriteAttributeString("PartName", partName);
        writer.WriteAttributeString("ContentType", contentType);
        writer.WriteEndElement();
    }

    private static void WriteRootRelationships(XmlWriter writer)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
        writer.WriteStartElement("Relationship");
        writer.WriteAttributeString("Id", "rId1");
        writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
        writer.WriteAttributeString("Target", "xl/workbook.xml");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteWorkbook(XmlWriter writer, IReadOnlyList<XlsxSheet> sheets)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("workbook", SpreadsheetNamespace);
        writer.WriteAttributeString("xmlns", "r", null, RelationshipNamespace);
        writer.WriteStartElement("sheets");
        for (var index = 0; index < sheets.Count; index++)
        {
            writer.WriteStartElement("sheet");
            writer.WriteAttributeString("name", SafeSheetName(sheets[index].Name, index));
            writer.WriteAttributeString("sheetId", (index + 1).ToString());
            writer.WriteAttributeString("r", "id", RelationshipNamespace, $"rId{index + 1}");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteWorkbookRelationships(XmlWriter writer, int sheetCount)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
        for (var index = 1; index <= sheetCount; index++)
        {
            writer.WriteStartElement("Relationship");
            writer.WriteAttributeString("Id", $"rId{index}");
            writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet");
            writer.WriteAttributeString("Target", $"worksheets/sheet{index}.xml");
            writer.WriteEndElement();
        }
        writer.WriteStartElement("Relationship");
        writer.WriteAttributeString("Id", $"rId{sheetCount + 1}");
        writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles");
        writer.WriteAttributeString("Target", "styles.xml");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteStyles(XmlWriter writer)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("styleSheet", SpreadsheetNamespace);
        writer.WriteStartElement("numFmts");
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("numFmt");
        writer.WriteAttributeString("numFmtId", "164");
        writer.WriteAttributeString("formatCode", "yyyy-mm-dd");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("fonts");
        writer.WriteAttributeString("count", "2");
        WriteFont(writer, bold: false);
        WriteFont(writer, bold: true);
        writer.WriteEndElement();
        writer.WriteStartElement("fills");
        writer.WriteAttributeString("count", "5");
        WriteFill(writer, "none", null);
        WriteFill(writer, "gray125", null);
        WriteFill(writer, "solid", "FFF4CCCC");
        WriteFill(writer, "solid", "FFFFE599");
        WriteFill(writer, "solid", "FFD9EAD3");
        writer.WriteEndElement();
        writer.WriteStartElement("borders");
        writer.WriteAttributeString("count", "2");
        WriteBorder(writer, false);
        WriteBorder(writer, true);
        writer.WriteEndElement();
        writer.WriteStartElement("cellStyleXfs");
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("xf");
        writer.WriteAttributeString("numFmtId", "0");
        writer.WriteAttributeString("fontId", "0");
        writer.WriteAttributeString("fillId", "0");
        writer.WriteAttributeString("borderId", "0");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("cellXfs");
        writer.WriteAttributeString("count", "6");
        WriteXf(writer, 0, 0, 0, false, 0);
        WriteXf(writer, 1, 2, 1, true, 0);
        WriteXf(writer, 1, 3, 1, true, 0);
        WriteXf(writer, 1, 4, 1, true, 0);
        WriteXf(writer, 1, 2, 1, false, 0);
        WriteXf(writer, 0, 0, 1, false, 164);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteFont(XmlWriter writer, bool bold)
    {
        writer.WriteStartElement("font");
        if (bold)
        {
            writer.WriteElementString("b", string.Empty);
        }
        writer.WriteStartElement("sz");
        writer.WriteAttributeString("val", "11");
        writer.WriteEndElement();
        writer.WriteStartElement("name");
        writer.WriteAttributeString("val", "Calibri");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteFill(XmlWriter writer, string pattern, string? rgb)
    {
        writer.WriteStartElement("fill");
        writer.WriteStartElement("patternFill");
        writer.WriteAttributeString("patternType", pattern);
        if (rgb is not null)
        {
            writer.WriteStartElement("fgColor");
            writer.WriteAttributeString("rgb", rgb);
            writer.WriteEndElement();
            writer.WriteStartElement("bgColor");
            writer.WriteAttributeString("indexed", "64");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteBorder(XmlWriter writer, bool thin)
    {
        writer.WriteStartElement("border");
        foreach (var edge in new[] { "left", "right", "top", "bottom" })
        {
            writer.WriteStartElement(edge);
            if (thin)
            {
                writer.WriteAttributeString("style", "thin");
                writer.WriteStartElement("color");
                writer.WriteAttributeString("rgb", "FFD9D9D9");
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteElementString("diagonal", string.Empty);
        writer.WriteEndElement();
    }

    private static void WriteXf(XmlWriter writer, int fontId, int fillId, int borderId, bool centered, int numFmtId)
    {
        writer.WriteStartElement("xf");
        writer.WriteAttributeString("numFmtId", numFmtId.ToString());
        writer.WriteAttributeString("fontId", fontId.ToString());
        writer.WriteAttributeString("fillId", fillId.ToString());
        writer.WriteAttributeString("borderId", borderId.ToString());
        writer.WriteAttributeString("xfId", "0");
        writer.WriteAttributeString("applyFill", "1");
        writer.WriteAttributeString("applyBorder", "1");
        if (numFmtId != 0)
        {
            writer.WriteAttributeString("applyNumberFormat", "1");
        }
        if (centered)
        {
            writer.WriteAttributeString("applyAlignment", "1");
            writer.WriteStartElement("alignment");
            writer.WriteAttributeString("horizontal", "center");
            writer.WriteAttributeString("vertical", "center");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteSheet(XmlWriter writer, XlsxSheet sheet)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", SpreadsheetNamespace);
        var columnCount = Math.Max(1, sheet.Rows.Select(r => r.Count).DefaultIfEmpty(1).Max());
        writer.WriteStartElement("cols");
        for (var index = 1; index <= columnCount; index++)
        {
            var width = sheet.Rows
                .Where(r => r.Count >= index)
                .Select(r => Math.Min(40, Math.Max(10, CellDisplayText(r[index - 1]).Length + 2)))
                .DefaultIfEmpty(12)
                .Max();
            writer.WriteStartElement("col");
            writer.WriteAttributeString("min", index.ToString());
            writer.WriteAttributeString("max", index.ToString());
            writer.WriteAttributeString("width", width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteAttributeString("customWidth", "1");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteStartElement("sheetData");
        for (var rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
        {
            writer.WriteStartElement("row");
            writer.WriteAttributeString("r", (rowIndex + 1).ToString());
            for (var columnIndex = 0; columnIndex < sheet.Rows[rowIndex].Count; columnIndex++)
            {
                var cell = sheet.Rows[rowIndex][columnIndex];
                writer.WriteStartElement("c");
                writer.WriteAttributeString("r", $"{ColumnName(columnIndex + 1)}{rowIndex + 1}");
                var style = cell.Value is DateTime && cell.Style == 0 ? 5 : cell.Style;
                if (style > 0)
                {
                    writer.WriteAttributeString("s", style.ToString());
                }
                WriteCellValue(writer, cell.Value);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        if (sheet.Merges.Count > 0)
        {
            writer.WriteStartElement("mergeCells");
            writer.WriteAttributeString("count", sheet.Merges.Count.ToString());
            foreach (var merge in sheet.Merges)
            {
                writer.WriteStartElement("mergeCell");
                writer.WriteAttributeString("ref", merge);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteCellValue(XmlWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteAttributeString("t", "inlineStr");
                writer.WriteStartElement("is");
                writer.WriteElementString("t", string.Empty);
                writer.WriteEndElement();
                break;
            case DateTime date:
                writer.WriteAttributeString("t", "n");
                writer.WriteElementString("v", date.ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                writer.WriteAttributeString("t", "n");
                writer.WriteElementString("v", Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case bool boolean:
                writer.WriteAttributeString("t", "b");
                writer.WriteElementString("v", boolean ? "1" : "0");
                break;
            default:
                writer.WriteAttributeString("t", "inlineStr");
                writer.WriteStartElement("is");
                writer.WriteStartElement("t");
                writer.WriteAttributeString("xml", "space", null, "preserve");
                writer.WriteString(value.ToString());
                writer.WriteEndElement();
                writer.WriteEndElement();
                break;
        }
    }

    private static string CellDisplayText(XlsxCell cell) => cell.Value switch
    {
        null => string.Empty,
        DateTime date => date.ToString("yyyy-MM-dd"),
        _ => cell.Value.ToString() ?? string.Empty
    };

    private static string ColumnName(int number)
    {
        var value = string.Empty;
        while (number > 0)
        {
            number--;
            value = (char)('A' + number % 26) + value;
            number /= 26;
        }
        return value;
    }

    private static string SafeSheetName(string value, int index)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var clean = string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch)).Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = $"Sheet {index + 1}";
        }
        return clean.Length > 31 ? clean[..31] : clean;
    }
}
