using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameFramework.ConfigData.Editor
{
    /// <summary>
    /// 单元格级导表校验规则的通用接口。
    /// 新增规则时只需实现当前接口，并注册到 ConfigTableExportValidator。yiji
    /// </summary>
    internal interface IConfigTableColumnValidator
    {
        string Tag { get; }

        void Validate(
            ConfigTableColumnValidationContext context,
            ICollection<ConfigTableDiagnostic> diagnostics);
    }

    /// <summary>
    /// 一列配置数据执行校验时所需的上下文。
    /// </summary>
    internal sealed class ConfigTableColumnValidationContext
    {
        public ConfigTableColumnValidationContext(
            string sourcePath,
            string fieldName,
            int sourceColumn,
            IReadOnlyList<ConfigTableColumnValue> values)
        {
            SourcePath = sourcePath;
            FieldName = fieldName;
            SourceColumn = sourceColumn;
            Values = values;
        }

        public string SourcePath { get; }

        public string FieldName { get; }

        public int SourceColumn { get; }

        public IReadOnlyList<ConfigTableColumnValue> Values { get; }
    }

    /// <summary>
    /// 一列中的一个数据单元格及其源行号。
    /// </summary>
    internal readonly struct ConfigTableColumnValue
    {
        public ConfigTableColumnValue(int sourceRow, string value)
        {
            SourceRow = sourceRow;
            Value = value ?? string.Empty;
        }

        public int SourceRow { get; }

        public string Value { get; }
    }

    /// <summary>
    /// 读取 CSV 的 #check 行，并按列执行已注册的通用校验规则。
    /// </summary>
    internal static class ConfigTableExportValidator
    {
        private const string VarMarker = "#var";
        private const string CheckMarker = "#check";

        private static readonly char[] TagSeparators = { ' ', '\t', '\r', '\n', ',', ';', '|', '，', '；' };

        private static readonly IReadOnlyDictionary<string, IConfigTableColumnValidator> Validators = CreateValidators();

        /// <summary>
        /// 校验一次导出生成的全部临时 CSV。
        /// 任意文件存在错误时会聚合所有错误并中断导表流程。
        /// </summary>
        public static void ValidateFiles(
            IReadOnlyList<string> csvFiles,
            int dataStartRow,
            int dataStartColumn)
        {
            if (csvFiles == null)
            {
                throw new ArgumentNullException(nameof(csvFiles));
            }

            if (dataStartRow < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(dataStartRow));
            }

            if (dataStartColumn < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(dataStartColumn));
            }

            List<ConfigTableDiagnostic> diagnostics = new();

            foreach (string csvFile in csvFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                ValidateFile(csvFile, dataStartRow, dataStartColumn, diagnostics);
            }

            if (diagnostics.Any(item =>
                    item.Severity == ConfigDiagnosticSeverity.Error))
            {
                throw new ConfigTableValidationException("CSV 导出校验失败，正式 CSV 目录未更新。", diagnostics);
            }
        }

        private static void ValidateFile(
            string csvFile,
            int dataStartRow,
            int dataStartColumn,
            ICollection<ConfigTableDiagnostic> diagnostics)
        {
            CsvDocument document;
            try
            {
                document = StandardCsvParser.ParseFile(csvFile);
            }
            catch (CsvParseException exception)
            {
                diagnostics.Add(new ConfigTableDiagnostic(
                    ConfigDiagnosticSeverity.Error,
                    Path.GetFileName(csvFile),
                    exception.Row,
                    exception.Column,
                    exception.Message));
                return;
            }

            string sourceName = Path.GetFileName(csvFile);
            CsvRecord varRecord = FindMarkerRecord(document, sourceName, VarMarker, diagnostics);
            CsvRecord checkRecord = FindMarkerRecord(document, sourceName, CheckMarker, diagnostics);

            if (varRecord == null || checkRecord == null)
            {
                return;
            }

            int lastFieldColumn = FindLastNonEmptyColumn(varRecord, dataStartColumn);
            if (lastFieldColumn < dataStartColumn)
            {
                diagnostics.Add(new ConfigTableDiagnostic(
                    ConfigDiagnosticSeverity.Error,
                    sourceName,
                    varRecord.RecordNumber,
                    dataStartColumn,
                    "#var 行没有定义任何可校验字段。"));
                return;
            }

            if (dataStartRow <= Math.Max(
                    varRecord.RecordNumber,
                    checkRecord.RecordNumber))
            {
                diagnostics.Add(new ConfigTableDiagnostic(
                    ConfigDiagnosticSeverity.Error,
                    sourceName,
                    dataStartRow,
                    dataStartColumn,
                    "数据开始行必须位于 #var 和 #check 元数据行之后。"));
                return;
            }

            ValidateCheckCellsBeyondFields(
                sourceName,
                checkRecord,
                lastFieldColumn,
                diagnostics);

            IReadOnlyList<CsvRecord> dataRecords = document.Records.Skip(dataStartRow - 1).Where(record => HasData(record, dataStartColumn, lastFieldColumn)).ToArray();

            for (int column = dataStartColumn; column <= lastFieldColumn; column++)
            {
                string checkExpression = checkRecord.GetCell(column).Value;
                if (string.IsNullOrWhiteSpace(checkExpression))
                {
                    continue;
                }

                IReadOnlyList<IConfigTableColumnValidator> validators = ResolveValidators(sourceName, checkRecord.RecordNumber, column, checkExpression, diagnostics);

                if (validators.Count == 0)
                {
                    continue;
                }

                string fieldName = varRecord.GetCell(column).Value.Trim();
                if (string.IsNullOrEmpty(fieldName))
                {
                    fieldName = $"第 {column} 列";
                }

                ConfigTableColumnValidationContext context = new(sourceName, fieldName, column, dataRecords.Select(record => new ConfigTableColumnValue(record.RecordNumber, record.GetCell(column).Value)).ToArray());

                foreach (IConfigTableColumnValidator validator in validators)
                {
                    validator.Validate(context, diagnostics);
                }
            }
        }

        private static CsvRecord FindMarkerRecord(
            CsvDocument document,
            string sourceName,
            string marker,
            ICollection<ConfigTableDiagnostic> diagnostics)
        {
            CsvRecord[] matches = document.Records.Where(record => string.Equals(record.GetCell(1).Value.Trim(), marker, StringComparison.OrdinalIgnoreCase)).ToArray();

            if (matches.Length == 0)
            {
                diagnostics.Add(new ConfigTableDiagnostic(
                    ConfigDiagnosticSeverity.Error,
                    sourceName,
                    0,
                    1,
                    $"缺少必需的元数据标记“{marker}”。"));
                return null;
            }

            for (int index = 1; index < matches.Length; index++)
            {
                diagnostics.Add(new ConfigTableDiagnostic(
                    ConfigDiagnosticSeverity.Error,
                    sourceName,
                    matches[index].RecordNumber,
                    1,
                    $"元数据标记“{marker}”重复。"));
            }

            return matches[0];
        }

        private static IReadOnlyList<IConfigTableColumnValidator> ResolveValidators(
            string sourceName,
            int checkRow,
            int column,
            string expression,
            ICollection<ConfigTableDiagnostic> diagnostics)
        {
            string[] tags = expression.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries);
            List<IConfigTableColumnValidator> result = new();
            HashSet<string> resolvedTags = new(StringComparer.OrdinalIgnoreCase);

            foreach (string rawTag in tags)
            {
                string tag = rawTag.Trim();
                if (!resolvedTags.Add(tag))
                {
                    continue;
                }

                if (Validators.TryGetValue(
                        tag,
                        out IConfigTableColumnValidator validator))
                {
                    result.Add(validator);
                    continue;
                }

                diagnostics.Add(new ConfigTableDiagnostic(
                    ConfigDiagnosticSeverity.Error,
                    sourceName,
                    checkRow,
                    column,
                    $"未知的 #check 标签“{tag}”。当前支持：" +
                    string.Join("、", Validators.Keys.OrderBy(value => value)) + "。"));
            }

            return result;
        }

        private static void ValidateCheckCellsBeyondFields(
            string sourceName,
            CsvRecord checkRecord,
            int lastFieldColumn,
            ICollection<ConfigTableDiagnostic> diagnostics)
        {
            for (int column = lastFieldColumn + 1; column <= checkRecord.CellCount; column++)
            {
                if (string.IsNullOrWhiteSpace(
                        checkRecord.GetCell(column).Value))
                {
                    continue;
                }

                diagnostics.Add(new ConfigTableDiagnostic(
                    ConfigDiagnosticSeverity.Error,
                    sourceName,
                    checkRecord.RecordNumber,
                    column,
                    "#check 在最后一个 #var 字段之后仍包含内容，无法确定校验列。"));
            }
        }

        private static bool HasData(
            CsvRecord record,
            int firstColumn,
            int lastColumn)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                if (!string.IsNullOrWhiteSpace(record.GetCell(column).Value))
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindLastNonEmptyColumn(
            CsvRecord record,
            int startColumn)
        {
            for (int column = record.CellCount; column >= startColumn; column--)
            {
                if (!string.IsNullOrWhiteSpace(record.GetCell(column).Value))
                {
                    return column;
                }
            }

            return startColumn - 1;
        }

        private static IReadOnlyDictionary<string, IConfigTableColumnValidator> CreateValidators()
        {
            IConfigTableColumnValidator[] validators = { new NonEmptyColumnValidator(), new UniqueColumnValidator() };

            return validators.ToDictionary(
                validator => validator.Tag,
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
