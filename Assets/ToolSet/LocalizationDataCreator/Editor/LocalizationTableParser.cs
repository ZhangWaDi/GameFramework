using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GameFramework.DataTools.Editor;

namespace GameFramework.LocalizationData.Editor
{
    /// <summary>
    /// 解析语言列式 CSV，并保留各 CSV 对应的本地化表边界。
    /// </summary>
    internal static class LocalizationTableParser
    {
        private const string VarMarker = "#var";
        private const string TypeMarker = "#type";
        private const string DescriptionMarker = "#desc";
        private const string CheckMarker = "#check";
        private const string DefaultMarker = "#default";
        private const string KeyFieldName = "Key";
        private const string StringTypeName = "string";

        private static readonly string[] RequiredMarkers = { VarMarker, TypeMarker, DescriptionMarker, CheckMarker, DefaultMarker };
        private static readonly Regex LanguageNameRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
        };
        private static readonly HashSet<string> ForbiddenLanguageNames = new(StringComparer.Ordinal) { LocalizationGenerationPaths.GeneratedLanguageTypeName, "value__" };
        private static readonly char[] CheckTagSeparators = { ' ', '\t', '\r', '\n', ',', ';', '|', '，', '；' };

        public static LocalizationDataSet ParseFiles(IEnumerable<string> csvFiles, int dataStartRow, int dataStartColumn, out IReadOnlyList<LocalizationDiagnostic> resultDiagnostics)
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

            string[] orderedFiles = csvFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            List<LocalizationDiagnostic> diagnostics = new();
            List<ParsedTable> tables = new();

            foreach (string csvFile in orderedFiles)
            {
                try
                {
                    CsvDocument document = StandardCsvParser.ParseFile(csvFile);
                    ParsedTable table = ParseDocument(document, dataStartRow, dataStartColumn, diagnostics);
                    if (table != null)
                    {
                        tables.Add(table);
                    }
                }
                catch (CsvParseException exception)
                {
                    diagnostics.Add(new(LocalizationDiagnosticSeverity.Error, exception.SourcePath, exception.Row, exception.Column, exception.Message));
                }
                catch (IOException exception)
                {
                    diagnostics.Add(new(LocalizationDiagnosticSeverity.Error, csvFile, 0, 0, $"读取 CSV 失败：{exception.Message}"));
                }
            }

            if (orderedFiles.Length == 0)
            {
                diagnostics.Add(new(LocalizationDiagnosticSeverity.Error, string.Empty, 0, 0, "CSV 目录中没有可处理的 .csv 文件。"));
            }

            IReadOnlyList<string> languages = ValidateLanguageColumns(tables, diagnostics);
            ValidateTableIds(tables, diagnostics);
            resultDiagnostics = diagnostics;

            if (diagnostics.Any(item => item.Severity == LocalizationDiagnosticSeverity.Error))
            {
                throw new LocalizationTableValidationException("本地化配置表校验失败。", diagnostics);
            }

            LocalizationTableDefinition[] tableDefinitions = tables.Where(table => languages.SequenceEqual(table.Languages, StringComparer.Ordinal)).OrderBy(table => table.TableId, StringComparer.Ordinal).Select(table => new LocalizationTableDefinition(table.TableId, table.SourcePath, table.Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray())).ToArray();
            return new(languages, tableDefinitions);
        }

        private static ParsedTable ParseDocument(CsvDocument document, int dataStartRow, int dataStartColumn, ICollection<LocalizationDiagnostic> diagnostics)
        {
            bool hasStructuralError = false;
            Dictionary<string, CsvRecord> markers = FindMarkers(document, diagnostics, ref hasStructuralError);
            if (hasStructuralError)
            {
                return null;
            }

            int lastMetadataRecord = markers.Values.Max(record => record.RecordNumber);
            if (dataStartRow <= lastMetadataRecord)
            {
                AddError(diagnostics, document.SourcePath, dataStartRow, dataStartColumn, $"数据开始行必须位于全部元数据行之后；最后一个元数据标记位于第 {lastMetadataRecord} 行。");
                return null;
            }

            CsvRecord varRecord = markers[VarMarker];
            CsvRecord typeRecord = markers[TypeMarker];
            CsvRecord checkRecord = markers[CheckMarker];
            CsvRecord defaultRecord = markers[DefaultMarker];
            int lastColumn = FindLastNonEmptyColumn(varRecord, dataStartColumn);
            if (lastColumn <= dataStartColumn)
            {
                AddError(diagnostics, document.SourcePath, varRecord.RecordNumber, dataStartColumn, "#var 必须包含 Key 和至少一种语言列。");
                return null;
            }

            ValidateMetadataBounds(document.SourcePath, markers, lastColumn, diagnostics);

            string keyField = varRecord.GetCell(dataStartColumn).Value.Trim();
            if (!string.Equals(keyField, KeyFieldName, StringComparison.Ordinal))
            {
                AddError(diagnostics, document.SourcePath, varRecord.RecordNumber, dataStartColumn, $"第一导出列必须严格命名为“{KeyFieldName}”。");
            }

            ValidateStringType(document.SourcePath, typeRecord, dataStartColumn, KeyFieldName, diagnostics);
            ValidateKeyChecks(document.SourcePath, checkRecord, dataStartColumn, diagnostics);

            List<LanguageColumn> languageColumns = new();
            HashSet<string> languageNames = new(StringComparer.OrdinalIgnoreCase);
            for (int column = dataStartColumn + 1; column <= lastColumn; column++)
            {
                string language = varRecord.GetCell(column).Value.Trim();
                if (!LanguageNameRegex.IsMatch(language) || CSharpKeywords.Contains(language) || ForbiddenLanguageNames.Contains(language))
                {
                    AddError(diagnostics, document.SourcePath, varRecord.RecordNumber, column, $"语言列名“{language}”不能生成合法的 C# 枚举成员；只允许字母或下划线开头，后续使用字母、数字、下划线，并且不能使用 C# 关键字或生成器保留名称。");
                    continue;
                }

                if (!languageNames.Add(language))
                {
                    AddError(diagnostics, document.SourcePath, varRecord.RecordNumber, column, $"语言列“{language}”重复。");
                    continue;
                }

                ValidateStringType(document.SourcePath, typeRecord, column, language, diagnostics);
                ValidateCheckExpression(document.SourcePath, checkRecord, column, diagnostics);
                languageColumns.Add(new(language, column, defaultRecord.GetCell(column).Value));
            }

            List<LocalizationEntryDefinition> entries = ParseEntries(document, dataStartRow, dataStartColumn, lastColumn, languageColumns, diagnostics);
            string tableId = Path.GetFileNameWithoutExtension(document.SourcePath);
            return new(tableId, document.SourcePath, languageColumns.Select(item => item.Name).ToArray(), entries);
        }

        private static Dictionary<string, CsvRecord> FindMarkers(CsvDocument document, ICollection<LocalizationDiagnostic> diagnostics, ref bool hasStructuralError)
        {
            Dictionary<string, CsvRecord> result = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> required = new(RequiredMarkers, StringComparer.OrdinalIgnoreCase);
            foreach (CsvRecord record in document.Records)
            {
                string marker = record.GetCell(1).Value.Trim();
                if (!required.Contains(marker))
                {
                    continue;
                }

                if (!result.TryAdd(marker, record))
                {
                    AddError(diagnostics, document.SourcePath, record.RecordNumber, 1, $"元数据标记“{marker}”重复。");
                    hasStructuralError = true;
                }
            }

            foreach (string marker in RequiredMarkers)
            {
                if (result.ContainsKey(marker))
                {
                    continue;
                }

                AddError(diagnostics, document.SourcePath, 0, 1, $"缺少必需的元数据标记“{marker}”。");
                hasStructuralError = true;
            }

            return result;
        }

        private static List<LocalizationEntryDefinition> ParseEntries(CsvDocument document, int dataStartRow, int keyColumn, int lastColumn, IReadOnlyList<LanguageColumn> languageColumns, ICollection<LocalizationDiagnostic> diagnostics)
        {
            List<LocalizationEntryDefinition> entries = new();
            HashSet<string> localKeys = new(StringComparer.Ordinal);

            for (int recordIndex = dataStartRow - 1; recordIndex < document.Records.Count; recordIndex++)
            {
                CsvRecord record = document.Records[recordIndex];
                if (!HasData(record, keyColumn, lastColumn))
                {
                    continue;
                }

                string key = record.GetCell(keyColumn).Value.Trim();
                bool rowIsValid = true;
                if (string.IsNullOrWhiteSpace(key))
                {
                    AddError(diagnostics, document.SourcePath, record.RecordNumber, keyColumn, "本地化 Key 不能为空。");
                    rowIsValid = false;
                }
                else if (!localKeys.Add(key))
                {
                    AddError(diagnostics, document.SourcePath, record.RecordNumber, keyColumn, $"本地化 Key“{key}”在当前表中重复。");
                    rowIsValid = false;
                }

                Dictionary<string, string> values = new(StringComparer.Ordinal);
                foreach (LanguageColumn languageColumn in languageColumns)
                {
                    string rawValue = record.GetCell(languageColumn.SourceColumn).Value;
                    string value = string.IsNullOrEmpty(rawValue) ? languageColumn.DefaultValue : rawValue;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        AddError(diagnostics, document.SourcePath, record.RecordNumber, languageColumn.SourceColumn, $"Key“{key}”在语言“{languageColumn.Name}”中没有文本，且该列未提供默认值。");
                        rowIsValid = false;
                    }

                    values[languageColumn.Name] = value ?? string.Empty;
                }

                if (rowIsValid)
                {
                    entries.Add(new(document.SourcePath, record.RecordNumber, key, values));
                }
            }

            return entries;
        }

        private static IReadOnlyList<string> ValidateLanguageColumns(IReadOnlyList<ParsedTable> tables, ICollection<LocalizationDiagnostic> diagnostics)
        {
            if (tables.Count == 0)
            {
                return Array.Empty<string>();
            }

            IReadOnlyList<string> expected = tables[0].Languages;
            for (int index = 1; index < tables.Count; index++)
            {
                ParsedTable table = tables[index];
                if (!expected.SequenceEqual(table.Languages, StringComparer.Ordinal))
                {
                    AddError(diagnostics, table.SourcePath, 0, 0, "语言列必须与其他本地化表保持完全相同的名称和顺序。" + $"预期：{string.Join(", ", expected)}；实际：{string.Join(", ", table.Languages)}。");
                }
            }

            return expected;
        }

        private static void ValidateTableIds(IReadOnlyList<ParsedTable> tables, ICollection<LocalizationDiagnostic> diagnostics)
        {
            Dictionary<string, ParsedTable> tableById = new(StringComparer.OrdinalIgnoreCase);
            foreach (ParsedTable table in tables)
            {
                if (string.IsNullOrWhiteSpace(table.TableId))
                {
                    AddError(diagnostics, table.SourcePath, 0, 0, "CSV 文件名不能为空，本地化表 ID 由 CSV 文件名生成。");
                    continue;
                }

                if (tableById.TryGetValue(table.TableId, out ParsedTable existing))
                {
                    AddError(diagnostics, table.SourcePath, 0, 0, $"本地化表 ID“{table.TableId}”与“{existing.SourcePath}”重复。");
                    continue;
                }

                tableById.Add(table.TableId, table);
            }
        }

        private static void ValidateStringType(string sourcePath, CsvRecord typeRecord, int column, string fieldName, ICollection<LocalizationDiagnostic> diagnostics)
        {
            string typeName = typeRecord.GetCell(column).Value.Trim();
            if (!string.Equals(typeName, StringTypeName, StringComparison.OrdinalIgnoreCase))
            {
                AddError(diagnostics, sourcePath, typeRecord.RecordNumber, column, $"字段“{fieldName}”的类型必须为 string。");
            }
        }

        private static void ValidateKeyChecks(string sourcePath, CsvRecord checkRecord, int keyColumn, ICollection<LocalizationDiagnostic> diagnostics)
        {
            HashSet<string> tags = ParseCheckTags(sourcePath, checkRecord, keyColumn, diagnostics);
            if (!tags.Contains("NonEmpty") || !tags.Contains("Unique"))
            {
                AddError(diagnostics, sourcePath, checkRecord.RecordNumber, keyColumn, "Key 列的 #check 必须同时包含 NonEmpty 和 Unique。");
            }
        }

        private static void ValidateCheckExpression(string sourcePath, CsvRecord checkRecord, int column, ICollection<LocalizationDiagnostic> diagnostics)
        {
            ParseCheckTags(sourcePath, checkRecord, column, diagnostics);
        }

        private static HashSet<string> ParseCheckTags(string sourcePath, CsvRecord checkRecord, int column, ICollection<LocalizationDiagnostic> diagnostics)
        {
            HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
            string expression = checkRecord.GetCell(column).Value;
            foreach (string rawTag in expression.Split(CheckTagSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                string tag = rawTag.Trim();
                if (!tags.Add(tag))
                {
                    continue;
                }

                if (!string.Equals(tag, "NonEmpty", StringComparison.OrdinalIgnoreCase) && !string.Equals(tag, "Unique", StringComparison.OrdinalIgnoreCase))
                {
                    AddError(diagnostics, sourcePath, checkRecord.RecordNumber, column, $"未知的 #check 标签“{tag}”，当前只支持 NonEmpty 和 Unique。");
                }
            }

            return tags;
        }

        private static void ValidateMetadataBounds(string sourcePath, IReadOnlyDictionary<string, CsvRecord> markers, int lastColumn, ICollection<LocalizationDiagnostic> diagnostics)
        {
            int maxColumn = markers.Values.Max(record => record.CellCount);
            for (int column = lastColumn + 1; column <= maxColumn; column++)
            {
                foreach (KeyValuePair<string, CsvRecord> marker in markers)
                {
                    if (!string.IsNullOrWhiteSpace(marker.Value.GetCell(column).Value))
                    {
                        AddError(diagnostics, sourcePath, marker.Value.RecordNumber, column, $"“{marker.Key}”在最后一个 #var 字段之后仍包含内容。");
                    }
                }
            }
        }

        private static bool HasData(CsvRecord record, int firstColumn, int lastColumn)
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

        private static int FindLastNonEmptyColumn(CsvRecord record, int startColumn)
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

        private static void AddError(ICollection<LocalizationDiagnostic> diagnostics, string sourcePath, int row, int column, string message)
        {
            diagnostics.Add(new(LocalizationDiagnosticSeverity.Error, sourcePath, row, column, message));
        }

        private sealed class ParsedTable
        {
            public ParsedTable(string tableId, string sourcePath, IReadOnlyList<string> languages, IReadOnlyList<LocalizationEntryDefinition> entries)
            {
                TableId = tableId;
                SourcePath = sourcePath;
                Languages = languages;
                Entries = entries;
            }

            public string TableId { get; }
            public string SourcePath { get; }
            public IReadOnlyList<string> Languages { get; }
            public IReadOnlyList<LocalizationEntryDefinition> Entries { get; }
        }

        private readonly struct LanguageColumn
        {
            public LanguageColumn(string name, int sourceColumn, string defaultValue)
            {
                Name = name;
                SourceColumn = sourceColumn;
                DefaultValue = defaultValue;
            }

            public string Name { get; }
            public int SourceColumn { get; }
            public string DefaultValue { get; }
        }
    }
}
