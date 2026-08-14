using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GameFramework.DataTools.Editor
{
    /// <summary>
    /// 标准 CSV 中的一个单元格。
    /// </summary>
    internal readonly struct CsvCell
    {
        public CsvCell(string value, int sourceRow, int sourceColumn)
        {
            Value = value;
            SourceRow = sourceRow;
            SourceColumn = sourceColumn;
        }

        public string Value { get; }

        public int SourceRow { get; }

        public int SourceColumn { get; }
    }

    /// <summary>
    /// 标准 CSV 中的一条逻辑记录。
    /// 引号字段包含换行时，一条逻辑记录可能跨越多个物理文本行。
    /// </summary>
    internal sealed class CsvRecord
    {
        private readonly IReadOnlyList<CsvCell> cells;

        public CsvRecord(int recordNumber, IReadOnlyList<CsvCell> cells)
        {
            RecordNumber = recordNumber;
            this.cells = cells;
        }

        public int RecordNumber { get; }

        public int CellCount => cells.Count;

        public IReadOnlyList<CsvCell> Cells => cells;

        /// <summary>
        /// 按 1 基列号读取单元格；记录缺少该列时返回空值单元格。
        /// </summary>
        public CsvCell GetCell(int column)
        {
            if (column <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }

            return column <= cells.Count
                ? cells[column - 1]
                : new CsvCell(string.Empty, RecordNumber, column);
        }
    }

    /// <summary>
    /// 完成语法解析后的 CSV 文档。
    /// </summary>
    internal sealed class CsvDocument
    {
        public CsvDocument(string sourcePath, IReadOnlyList<CsvRecord> records)
        {
            SourcePath = sourcePath;
            Records = records;
        }

        public string SourcePath { get; }

        public IReadOnlyList<CsvRecord> Records { get; }
    }

    /// <summary>
    /// CSV 文本不符合标准引号或分隔规则时抛出的异常。
    /// </summary>
    internal sealed class CsvParseException : FormatException
    {
        public CsvParseException(
            string sourcePath,
            int row,
            int column,
            string message)
            : base($"{sourcePath}({row},{column}): {message}")
        {
            SourcePath = sourcePath;
            Row = row;
            Column = column;
        }

        public string SourcePath { get; }

        public int Row { get; }

        public int Column { get; }
    }

    /// <summary>
    /// 不依赖第三方库的标准 CSV 解析器。
    /// 支持逗号分隔、双引号字段、双引号转义、CRLF/LF 以及引号内换行。
    /// 此类型只处理 CSV 语法，不承担配置字段类型转换。
    /// </summary>
    internal static class StandardCsvParser
    {
        /// <summary>
        /// 读取 UTF-8 CSV 文件并解析为逻辑记录。
        /// StreamReader 会自动识别并移除 UTF-8 BOM。
        /// </summary>
        public static CsvDocument ParseFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("CSV 文件路径不能为空。", nameof(filePath));
            }

            using StreamReader reader = new(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            return Parse(reader.ReadToEnd(), filePath);
        }

        /// <summary>
        /// 使用状态机解析一段完整 CSV 文本。
        /// 引号内的 CRLF 和 CR 会统一为 LF，其他字段内容保持原样。
        /// </summary>
        public static CsvDocument Parse(string text, string sourcePath = "<CSV>")
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text.Substring(1);
            }

            List<CsvRecord> records = new();
            List<CsvCell> cells = new();
            StringBuilder field = new();

            int physicalRow = 1;
            int physicalColumn = 1;
            int fieldStartRow = 1;
            int fieldStartColumn = 1;

            bool inQuotes = false;
            bool closedQuotedField = false;
            bool fieldWasQuoted = false;
            bool recordWasTouched = false;

            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];

                if (inQuotes)
                {
                    if (character == '"')
                    {
                        if (index + 1 < text.Length && text[index + 1] == '"')
                        {
                            field.Append('"');
                            index++;
                            physicalColumn += 2;
                            continue;
                        }

                        inQuotes = false;
                        closedQuotedField = true;
                        physicalColumn++;
                        continue;
                    }

                    if (IsLineBreak(character))
                    {
                        ConsumeLineBreak(
                            text,
                            ref index,
                            ref physicalRow,
                            ref physicalColumn);
                        field.Append('\n');
                        continue;
                    }

                    field.Append(character);
                    physicalColumn++;
                    continue;
                }

                if (closedQuotedField)
                {
                    if (character == ',')
                    {
                        CompleteField(
                            cells,
                            field,
                            fieldStartRow,
                            fieldStartColumn);
                        closedQuotedField = false;
                        fieldWasQuoted = false;
                        recordWasTouched = true;
                        physicalColumn++;
                        fieldStartRow = physicalRow;
                        fieldStartColumn = physicalColumn;
                        continue;
                    }

                    if (IsLineBreak(character))
                    {
                        CompleteField(
                            cells,
                            field,
                            fieldStartRow,
                            fieldStartColumn);
                        CompleteRecord(records, cells);
                        closedQuotedField = false;
                        fieldWasQuoted = false;
                        recordWasTouched = false;
                        ConsumeLineBreak(
                            text,
                            ref index,
                            ref physicalRow,
                            ref physicalColumn);
                        fieldStartRow = physicalRow;
                        fieldStartColumn = physicalColumn;
                        continue;
                    }

                    throw CreateParseException(sourcePath, physicalRow, physicalColumn, "结束引号后只能出现逗号、换行或文件结尾。");
                }

                if (character == '"')
                {
                    if (field.Length > 0)
                    {
                        throw CreateParseException(sourcePath, physicalRow, physicalColumn, "未加引号字段中不能直接出现双引号。");
                    }

                    inQuotes = true;
                    fieldWasQuoted = true;
                    recordWasTouched = true;
                    physicalColumn++;
                    continue;
                }

                if (character == ',')
                {
                    CompleteField(
                        cells,
                        field,
                        fieldStartRow,
                        fieldStartColumn);
                    recordWasTouched = true;
                    physicalColumn++;
                    fieldStartRow = physicalRow;
                    fieldStartColumn = physicalColumn;
                    continue;
                }

                if (IsLineBreak(character))
                {
                    CompleteField(
                        cells,
                        field,
                        fieldStartRow,
                        fieldStartColumn);
                    CompleteRecord(records, cells);
                    recordWasTouched = false;
                    ConsumeLineBreak(
                        text,
                        ref index,
                        ref physicalRow,
                        ref physicalColumn);
                    fieldStartRow = physicalRow;
                    fieldStartColumn = physicalColumn;
                    continue;
                }

                field.Append(character);
                recordWasTouched = true;
                physicalColumn++;
            }

            if (inQuotes)
            {
                throw CreateParseException(sourcePath, fieldStartRow, fieldStartColumn, "引号字段在文件结束前没有闭合。");
            }

            if (closedQuotedField ||
                fieldWasQuoted ||
                recordWasTouched ||
                cells.Count > 0 ||
                field.Length > 0)
            {
                CompleteField(
                    cells,
                    field,
                    fieldStartRow,
                    fieldStartColumn);
                CompleteRecord(records, cells);
            }

            return new CsvDocument(sourcePath, records);
        }

        private static bool IsLineBreak(char character)
        {
            return character == '\r' || character == '\n';
        }

        /// <summary>
        /// 消费一个 CR、LF 或 CRLF，并更新后续错误诊断使用的物理坐标。
        /// </summary>
        private static void ConsumeLineBreak(
            string text,
            ref int index,
            ref int physicalRow,
            ref int physicalColumn)
        {
            if (text[index] == '\r' &&
                index + 1 < text.Length &&
                text[index + 1] == '\n')
            {
                index++;
            }

            physicalRow++;
            physicalColumn = 1;
        }

        /// <summary>
        /// 将当前字段写入记录并重置字段缓冲区。
        /// </summary>
        private static void CompleteField(
            ICollection<CsvCell> cells,
            StringBuilder field,
            int sourceRow,
            int sourceColumn)
        {
            cells.Add(new CsvCell(field.ToString(), sourceRow, sourceColumn));
            field.Clear();
        }

        /// <summary>
        /// 将当前单元格集合封装为一条 1 基编号的逻辑记录。
        /// </summary>
        private static void CompleteRecord(
            ICollection<CsvRecord> records,
            List<CsvCell> cells)
        {
            records.Add(new CsvRecord(
                records.Count + 1,
                cells.ToArray()));
            cells.Clear();
        }

        private static CsvParseException CreateParseException(
            string sourcePath,
            int row,
            int column,
            string message)
        {
            return new CsvParseException(sourcePath, row, column, message);
        }
    }
}
