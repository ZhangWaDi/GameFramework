using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace GameFramework.ConfigData.Editor
{
    /// <summary>
    /// XLSX 转 CSV 的执行结果。
    /// </summary>
    public sealed class XlsxToCsvExportReport
    {
        private readonly List<string> outputFiles = new List<string>();

        public int WorkbookCount { get; internal set; }

        public int WorksheetCount { get; internal set; }

        public IReadOnlyList<string> OutputFiles => outputFiles;

        internal void AddOutputFile(string path)
        {
            outputFiles.Add(path);
        }
    }

    /// <summary>
    /// XLSX 转 CSV 工具。
    /// </summary>
    public static class XlsxToCsvConverter
    {
        private const string WorkbookPartPath = "xl/workbook.xml";
        private const string WorkbookRelationshipsPartPath = "xl/_rels/workbook.xml.rels";
        private const string SharedStringsPartPath = "xl/sharedStrings.xml";
        private const string StylesPartPath = "xl/styles.xml";

        private static readonly XNamespace SpreadsheetNamespace =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        private static readonly XNamespace OfficeDocumentRelationshipsNamespace =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        private static readonly XNamespace PackageRelationshipsNamespace =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>
        /// 转换输入目录中的全部 XLSX 文件。路径既可以是绝对路径，也可以是相对路径。
        /// </summary>
        public static XlsxToCsvExportReport ConvertDirectory(
            string inputDirectory,
            string outputDirectory,
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            string fullInputDirectory = GetRequiredDirectoryPath(inputDirectory, nameof(inputDirectory));
            string fullOutputDirectory = GetRequiredPath(outputDirectory, nameof(outputDirectory));

            Directory.CreateDirectory(fullOutputDirectory);

            string[] workbookPaths = Directory
                .EnumerateFiles(fullInputDirectory, "*", searchOption)
                .Where(path =>
                    string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            XlsxToCsvExportReport report = new XlsxToCsvExportReport();
            Dictionary<string, string> outputOwners =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string workbookPath in workbookPaths)
            {
                ConvertWorkbook(workbookPath, fullOutputDirectory, outputOwners, report);
                report.WorkbookCount++;
            }

            return report;
        }

        /// <summary>
        /// 转换单个 XLSX 文件。路径既可以是绝对路径，也可以是相对路径。
        /// </summary>
        public static XlsxToCsvExportReport ConvertFile(string xlsxPath, string outputDirectory)
        {
            string fullWorkbookPath = GetRequiredFilePath(xlsxPath, nameof(xlsxPath));
            string fullOutputDirectory = GetRequiredPath(outputDirectory, nameof(outputDirectory));

            if (!string.Equals(
                    Path.GetExtension(fullWorkbookPath),
                    ".xlsx",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("输入文件必须是 .xlsx 文件。", nameof(xlsxPath));
            }

            Directory.CreateDirectory(fullOutputDirectory);

            XlsxToCsvExportReport report = new XlsxToCsvExportReport();
            Dictionary<string, string> outputOwners =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            ConvertWorkbook(fullWorkbookPath, fullOutputDirectory, outputOwners, report);
            report.WorkbookCount = 1;
            return report;
        }

        private static void ConvertWorkbook(
            string workbookPath,
            string outputDirectory,
            IDictionary<string, string> outputOwners,
            XlsxToCsvExportReport report)
        {
            using (FileStream fileStream = new FileStream(
                       workbookPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite))
            using (ZipArchive archive = new ZipArchive(fileStream, ZipArchiveMode.Read))
            {
                XDocument workbookDocument = LoadRequiredXml(archive, WorkbookPartPath);
                XDocument relationshipsDocument =
                    LoadRequiredXml(archive, WorkbookRelationshipsPartPath);

                Dictionary<string, string> worksheetTargets = relationshipsDocument
                    .Root?
                    .Elements(PackageRelationshipsNamespace + "Relationship")
                    .Where(element =>
                        element.Attribute("Id") != null &&
                        element.Attribute("Target") != null)
                    .ToDictionary(
                        element => element.Attribute("Id")!.Value,
                        element => element.Attribute("Target")!.Value,
                        StringComparer.Ordinal)
                    ?? new Dictionary<string, string>(StringComparer.Ordinal);

                List<string> sharedStrings = LoadSharedStrings(archive);
                List<CellStyle> cellStyles = LoadCellStyles(archive);
                bool usesDate1904 = UsesDate1904System(workbookDocument);

                IEnumerable<XElement> sheets = workbookDocument
                    .Root?
                    .Element(SpreadsheetNamespace + "sheets")?
                    .Elements(SpreadsheetNamespace + "sheet")
                    ?? Enumerable.Empty<XElement>();

                foreach (XElement sheet in sheets)
                {
                    string sheetName = sheet.Attribute("name")?.Value;
                    string relationshipId =
                        sheet.Attribute(OfficeDocumentRelationshipsNamespace + "id")?.Value;

                    if (string.IsNullOrWhiteSpace(sheetName) ||
                        string.IsNullOrWhiteSpace(relationshipId))
                    {
                        throw new InvalidDataException(
                            $"工作簿“{Path.GetFileName(workbookPath)}”包含无效的工作表定义。");
                    }

                    if (!worksheetTargets.TryGetValue(relationshipId, out string worksheetTarget))
                    {
                        throw new InvalidDataException(
                            $"找不到工作表“{sheetName}”对应的 XML 文件。");
                    }

                    string worksheetPartPath = ResolvePartPath("xl", worksheetTarget);
                    XDocument worksheetDocument = LoadRequiredXml(archive, worksheetPartPath);

                    string csvFileName = SanitizeFileName(sheetName) + ".csv";
                    string csvPath = Path.GetFullPath(Path.Combine(outputDirectory, csvFileName));

                    if (outputOwners.TryGetValue(csvPath, out string existingOwner))
                    {
                        throw new InvalidDataException(
                            $"CSV 输出名称冲突：“{sheetName}”与“{existingOwner}”都会输出到“{csvPath}”。");
                    }

                    outputOwners.Add(
                        csvPath,
                        $"{Path.GetFileName(workbookPath)}/{sheetName}");

                    WriteWorksheetAsCsv(
                        worksheetDocument,
                        sharedStrings,
                        cellStyles,
                        usesDate1904,
                        csvPath);

                    report.WorksheetCount++;
                    report.AddOutputFile(csvPath);
                }
            }
        }

        private static void WriteWorksheetAsCsv(
            XDocument worksheetDocument,
            IReadOnlyList<string> sharedStrings,
            IReadOnlyList<CellStyle> cellStyles,
            bool usesDate1904,
            string csvPath)
        {
            SortedDictionary<int, Dictionary<int, string>> rows =
                new SortedDictionary<int, Dictionary<int, string>>();

            int maximumRowIndex = 0;
            int maximumColumnIndex = 0;
            int previousRowIndex = 0;

            IEnumerable<XElement> rowElements = worksheetDocument
                .Root?
                .Element(SpreadsheetNamespace + "sheetData")?
                .Elements(SpreadsheetNamespace + "row")
                ?? Enumerable.Empty<XElement>();

            foreach (XElement rowElement in rowElements)
            {
                int rowIndex = ParsePositiveIndex(rowElement.Attribute("r")?.Value)
                               ?? previousRowIndex + 1;

                previousRowIndex = rowIndex;

                Dictionary<int, string> cells = new Dictionary<int, string>();
                int previousColumnIndex = 0;
                bool rowHasValue = false;

                foreach (XElement cellElement in rowElement.Elements(SpreadsheetNamespace + "c"))
                {
                    string cellReference = cellElement.Attribute("r")?.Value;
                    int columnIndex = GetColumnIndex(cellReference) ?? previousColumnIndex + 1;

                    previousColumnIndex = columnIndex;

                    string value = GetCellValue(
                        cellElement,
                        sharedStrings,
                        cellStyles,
                        usesDate1904);

                    cells[columnIndex] = value;
                    if (!string.IsNullOrEmpty(value))
                    {
                        rowHasValue = true;
                        maximumColumnIndex = Math.Max(maximumColumnIndex, columnIndex);
                    }
                }

                if (rowHasValue)
                {
                    maximumRowIndex = Math.Max(maximumRowIndex, rowIndex);
                }

                rows[rowIndex] = cells;
            }

            string parentDirectory = Path.GetDirectoryName(csvPath);
            if (string.IsNullOrEmpty(parentDirectory))
            {
                throw new InvalidOperationException($"无法确定 CSV 输出目录：{csvPath}");
            }

            Directory.CreateDirectory(parentDirectory);

            using (FileStream fileStream = new FileStream(
                       csvPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.Read))
            using (StreamWriter writer = new StreamWriter(
                       fileStream,
                       new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                writer.NewLine = "\r\n";

                for (int rowIndex = 1; rowIndex <= maximumRowIndex; rowIndex++)
                {
                    rows.TryGetValue(rowIndex, out Dictionary<int, string> cells);

                    StringBuilder line = new StringBuilder();
                    for (int columnIndex = 1;
                         columnIndex <= maximumColumnIndex;
                         columnIndex++)
                    {
                        if (columnIndex > 1)
                        {
                            line.Append(',');
                        }

                        if (cells != null && cells.TryGetValue(columnIndex, out string value))
                        {
                            line.Append(EscapeCsvValue(value));
                        }
                    }

                    writer.WriteLine(line.ToString());
                }
            }
        }

        private static string GetCellValue(
            XElement cellElement,
            IReadOnlyList<string> sharedStrings,
            IReadOnlyList<CellStyle> cellStyles,
            bool usesDate1904)
        {
            string cellType = cellElement.Attribute("t")?.Value;

            if (string.Equals(cellType, "inlineStr", StringComparison.Ordinal))
            {
                return string.Concat(
                    cellElement
                        .Descendants(SpreadsheetNamespace + "t")
                        .Select(element => element.Value));
            }

            string rawValue =
                cellElement.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty;

            switch (cellType)
            {
                case "s":
                    if (!int.TryParse(
                            rawValue,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int sharedStringIndex) ||
                        sharedStringIndex < 0 ||
                        sharedStringIndex >= sharedStrings.Count)
                    {
                        throw new InvalidDataException(
                            $"共享字符串索引无效：{rawValue}");
                    }

                    return sharedStrings[sharedStringIndex];

                case "b":
                    return rawValue == "1" ? "TRUE" : "FALSE";

                case "str":
                case "e":
                case "d":
                    return rawValue;
            }

            int? styleIndex = ParseNonNegativeIndex(cellElement.Attribute("s")?.Value);
            if (styleIndex.HasValue &&
                styleIndex.Value < cellStyles.Count &&
                cellStyles[styleIndex.Value].DateKind != DateCellKind.None &&
                double.TryParse(
                    rawValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double serialDate))
            {
                if (usesDate1904)
                {
                    serialDate += 1462d;
                }

                DateTime dateTime = DateTime.FromOADate(serialDate);
                switch (cellStyles[styleIndex.Value].DateKind)
                {
                    case DateCellKind.Date:
                        return dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    case DateCellKind.Time:
                        return dateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                    case DateCellKind.DateTime:
                        return dateTime.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture);
                }
            }

            return rawValue;
        }

        private static List<string> LoadSharedStrings(ZipArchive archive)
        {
            XDocument document = LoadOptionalXml(archive, SharedStringsPartPath);
            if (document?.Root == null)
            {
                return new List<string>();
            }

            return document
                .Root
                .Elements(SpreadsheetNamespace + "si")
                .Select(item =>
                    string.Concat(
                        item
                            .Descendants(SpreadsheetNamespace + "t")
                            .Select(text => text.Value)))
                .ToList();
        }

        private static List<CellStyle> LoadCellStyles(ZipArchive archive)
        {
            XDocument document = LoadOptionalXml(archive, StylesPartPath);
            if (document?.Root == null)
            {
                return new List<CellStyle>();
            }

            Dictionary<int, string> customNumberFormats = document
                .Root
                .Element(SpreadsheetNamespace + "numFmts")?
                .Elements(SpreadsheetNamespace + "numFmt")
                .Where(element =>
                    ParseNonNegativeIndex(element.Attribute("numFmtId")?.Value).HasValue &&
                    element.Attribute("formatCode") != null)
                .ToDictionary(
                    element =>
                        ParseNonNegativeIndex(element.Attribute("numFmtId")!.Value)!.Value,
                    element => element.Attribute("formatCode")!.Value)
                ?? new Dictionary<int, string>();

            IEnumerable<XElement> formatElements = document
                .Root
                .Element(SpreadsheetNamespace + "cellXfs")?
                .Elements(SpreadsheetNamespace + "xf")
                ?? Enumerable.Empty<XElement>();

            List<CellStyle> styles = new List<CellStyle>();
            foreach (XElement formatElement in formatElements)
            {
                int numberFormatId =
                    ParseNonNegativeIndex(formatElement.Attribute("numFmtId")?.Value) ?? 0;

                customNumberFormats.TryGetValue(numberFormatId, out string formatCode);
                styles.Add(new CellStyle(GetDateCellKind(numberFormatId, formatCode)));
            }

            return styles;
        }

        private static DateCellKind GetDateCellKind(int numberFormatId, string formatCode)
        {
            if (numberFormatId >= 14 && numberFormatId <= 17)
            {
                return DateCellKind.Date;
            }

            if ((numberFormatId >= 18 && numberFormatId <= 21) ||
                (numberFormatId >= 45 && numberFormatId <= 47))
            {
                return DateCellKind.Time;
            }

            if (numberFormatId == 22)
            {
                return DateCellKind.DateTime;
            }

            if (string.IsNullOrEmpty(formatCode))
            {
                return DateCellKind.None;
            }

            string normalizedFormat = RemoveNumberFormatLiterals(formatCode).ToLowerInvariant();
            bool hasDate = normalizedFormat.IndexOf('y') >= 0 ||
                           normalizedFormat.IndexOf('d') >= 0;
            bool hasTime = normalizedFormat.IndexOf('h') >= 0 ||
                           normalizedFormat.IndexOf('s') >= 0;

            if (hasDate && hasTime)
            {
                return DateCellKind.DateTime;
            }

            if (hasDate)
            {
                return DateCellKind.Date;
            }

            return hasTime ? DateCellKind.Time : DateCellKind.None;
        }

        private static string RemoveNumberFormatLiterals(string formatCode)
        {
            StringBuilder result = new StringBuilder(formatCode.Length);
            bool insideQuotes = false;

            for (int index = 0; index < formatCode.Length; index++)
            {
                char current = formatCode[index];

                if (current == '"')
                {
                    insideQuotes = !insideQuotes;
                    continue;
                }

                if (insideQuotes)
                {
                    continue;
                }

                if ((current == '\\' || current == '_' || current == '*') &&
                    index + 1 < formatCode.Length)
                {
                    index++;
                    continue;
                }

                if (current == '[')
                {
                    int closingBracketIndex = formatCode.IndexOf(']', index + 1);
                    if (closingBracketIndex >= 0)
                    {
                        string bracketContent = formatCode
                            .Substring(index + 1, closingBracketIndex - index - 1)
                            .ToLowerInvariant();

                        if (bracketContent == "h" ||
                            bracketContent == "hh" ||
                            bracketContent == "m" ||
                            bracketContent == "mm" ||
                            bracketContent == "s" ||
                            bracketContent == "ss")
                        {
                            result.Append(bracketContent);
                        }

                        index = closingBracketIndex;
                        continue;
                    }
                }

                result.Append(current);
            }

            return result.ToString();
        }

        private static bool UsesDate1904System(XDocument workbookDocument)
        {
            string value = workbookDocument
                .Root?
                .Element(SpreadsheetNamespace + "workbookPr")?
                .Attribute("date1904")?
                .Value;

            return value == "1" ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            bool requiresQuotes =
                value.IndexOf(',') >= 0 ||
                value.IndexOf('"') >= 0 ||
                value.IndexOf('\r') >= 0 ||
                value.IndexOf('\n') >= 0;

            if (!requiresQuotes)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string SanitizeFileName(string fileName)
        {
            HashSet<char> invalidCharacters =
                new HashSet<char>(Path.GetInvalidFileNameChars());

            StringBuilder sanitized = new StringBuilder(fileName.Length);
            foreach (char character in fileName)
            {
                sanitized.Append(invalidCharacters.Contains(character) ? '_' : character);
            }

            string result = sanitized.ToString().Trim();
            if (string.IsNullOrEmpty(result))
            {
                throw new InvalidDataException($"工作表名称“{fileName}”无法生成有效文件名。");
            }

            return result;
        }

        private static int? GetColumnIndex(string cellReference)
        {
            if (string.IsNullOrEmpty(cellReference))
            {
                return null;
            }

            int columnIndex = 0;
            bool foundLetter = false;

            foreach (char character in cellReference)
            {
                if (!char.IsLetter(character))
                {
                    break;
                }

                foundLetter = true;
                columnIndex =
                    checked(columnIndex * 26 + char.ToUpperInvariant(character) - 'A' + 1);
            }

            return foundLetter ? columnIndex : null;
        }

        private static int? ParsePositiveIndex(string value)
        {
            return int.TryParse(
                       value,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out int result) &&
                   result > 0
                ? result
                : (int?)null;
        }

        private static int? ParseNonNegativeIndex(string value)
        {
            return int.TryParse(
                       value,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out int result) &&
                   result >= 0
                ? result
                : (int?)null;
        }

        private static XDocument LoadRequiredXml(ZipArchive archive, string partPath)
        {
            XDocument document = LoadOptionalXml(archive, partPath);
            if (document == null)
            {
                throw new InvalidDataException($"XLSX 中缺少必要文件：{partPath}");
            }

            return document;
        }

        private static XDocument LoadOptionalXml(ZipArchive archive, string partPath)
        {
            string normalizedPartPath = partPath.Replace('\\', '/').TrimStart('/');
            ZipArchiveEntry entry = archive.Entries.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.FullName.Replace('\\', '/'),
                    normalizedPartPath,
                    StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                return null;
            }

            using (Stream stream = entry.Open())
            {
                return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            }
        }

        private static string ResolvePartPath(string basePartDirectory, string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                throw new InvalidDataException("XLSX 关系目标路径为空。");
            }

            string normalizedTarget = target.Replace('\\', '/');
            string combined = normalizedTarget.StartsWith("/", StringComparison.Ordinal)
                ? normalizedTarget.TrimStart('/')
                : basePartDirectory.TrimEnd('/') + "/" + normalizedTarget;

            Stack<string> segments = new Stack<string>();
            foreach (string segment in combined.Split('/'))
            {
                if (string.IsNullOrEmpty(segment) || segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (segments.Count == 0)
                    {
                        throw new InvalidDataException(
                            $"XLSX 关系目标路径越界：{target}");
                    }

                    segments.Pop();
                    continue;
                }

                segments.Push(segment);
            }

            return string.Join("/", segments.Reverse());
        }

        private static string GetRequiredDirectoryPath(string path, string parameterName)
        {
            string fullPath = GetRequiredPath(path, parameterName);
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException($"输入目录不存在：{fullPath}");
            }

            return fullPath;
        }

        private static string GetRequiredFilePath(string path, string parameterName)
        {
            string fullPath = GetRequiredPath(path, parameterName);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("输入文件不存在。", fullPath);
            }

            return fullPath;
        }

        private static string GetRequiredPath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("路径不能为空。", parameterName);
            }

            return Path.GetFullPath(path);
        }

        private readonly struct CellStyle
        {
            public CellStyle(DateCellKind dateKind)
            {
                DateKind = dateKind;
            }

            public DateCellKind DateKind { get; }
        }

        private enum DateCellKind
        {
            None,
            Date,
            Time,
            DateTime
        }
    }
}
