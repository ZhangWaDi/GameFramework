using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameFramework.ConfigData.Editor
{
    /// <summary>
    /// 将标准 CSV 文档解释为配置表 Schema 和强类型数据行。
    /// 此类型负责元数据定位、字段校验、默认值回退与 ID 唯一性检查。
    /// </summary>
    internal static class ConfigTableSchemaParser
    {
        private const string VarMarker = "#var";
        private const string TypeMarker = "#type";
        private const string DescriptionMarker = "#desc";
        private const string CheckMarker = "#check";
        private const string DefaultMarker = "#default";

        private static readonly string[] RequiredMarkers =
        {
            VarMarker,
            TypeMarker,
            DescriptionMarker,
            CheckMarker,
            DefaultMarker
        };

        private static readonly Regex IdentifierRegex =
            new Regex(
                "^[A-Za-z_][A-Za-z0-9_]*$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> CSharpKeywords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "abstract", "as", "base", "bool", "break", "byte", "case",
                "catch", "char", "checked", "class", "const", "continue",
                "decimal", "default", "delegate", "do", "double", "else",
                "enum", "event", "explicit", "extern", "false", "finally",
                "fixed", "float", "for", "foreach", "goto", "if", "implicit",
                "in", "int", "interface", "internal", "is", "lock", "long",
                "namespace", "new", "null", "object", "operator", "out",
                "override", "params", "private", "protected", "public",
                "readonly", "ref", "return", "sbyte", "sealed", "short",
                "sizeof", "stackalloc", "static", "string", "struct",
                "switch", "this", "throw", "true", "try", "typeof", "uint",
                "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
                "void", "volatile", "while"
            };

        /// <summary>
        /// 解析单张 CSV 配置表。
        /// dataStartRow 和 dataStartColumn 使用 1 基坐标，并分别决定数据记录起点和字段起点。
        /// 所有可发现的问题会追加到 diagnostics，便于一次修正多处错误。
        /// </summary>
        public static ConfigTableDefinition Parse(
            CsvDocument document,
            int dataStartRow,
            int dataStartColumn,
            ICollection<ConfigTableDiagnostic> diagnostics)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            string tableName = Path.GetFileNameWithoutExtension(document.SourcePath);
            bool hasStructuralError = false;

            if (!IsValidIdentifier(tableName))
            {
                AddError(
                    diagnostics,
                    document.SourcePath,
                    0,
                    0,
                    $"CSV 文件名“{tableName}”不是有效的 C# 类型名。" +
                    "请使用英文字母、数字和下划线，且不能以数字开头。");
                hasStructuralError = true;
            }

            if (dataStartRow < 1)
            {
                AddError(
                    diagnostics,
                    document.SourcePath,
                    0,
                    0,
                    "数据开始行必须大于或等于 1。");
                hasStructuralError = true;
            }

            if (dataStartColumn < 1)
            {
                AddError(
                    diagnostics,
                    document.SourcePath,
                    0,
                    0,
                    "数据开始列必须大于或等于 1。");
                hasStructuralError = true;
            }

            Dictionary<string, CsvRecord> markerRecords = FindMarkerRecords(
                document,
                diagnostics,
                ref hasStructuralError);

            if (hasStructuralError)
            {
                return null;
            }

            int lastMetadataRecord = markerRecords.Values
                .Max(record => record.RecordNumber);
            if (dataStartRow <= lastMetadataRecord)
            {
                AddError(
                    diagnostics,
                    document.SourcePath,
                    dataStartRow,
                    dataStartColumn,
                    $"数据开始行必须位于全部元数据行之后；当前最后一个元数据标记位于第 " +
                    $"{lastMetadataRecord} 行。");
                return null;
            }

            List<ConfigFieldSchema> fields = ParseFields(
                document.SourcePath,
                markerRecords,
                dataStartColumn,
                diagnostics);

            if (fields.Count == 0 ||
                diagnostics.Any(item =>
                    item.Severity == ConfigDiagnosticSeverity.Error &&
                    string.Equals(
                        item.SourcePath,
                        document.SourcePath,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            ConfigTableSchema schema = new ConfigTableSchema
            {
                SourcePath = document.SourcePath,
                TableName = tableName,
                DataStartRow = dataStartRow,
                DataStartColumn = dataStartColumn,
                Fields = fields
            };

            List<ConfigTableDataRow> rows = ParseDataRows(
                document,
                schema,
                diagnostics);

            return new ConfigTableDefinition(schema, rows);
        }

        /// <summary>
        /// 在第一列中按标记名称定位元数据行。
        /// 标记顺序可以扩展，但每个必需标记必须且只能出现一次。
        /// </summary>
        private static Dictionary<string, CsvRecord> FindMarkerRecords(
            CsvDocument document,
            ICollection<ConfigTableDiagnostic> diagnostics,
            ref bool hasStructuralError)
        {
            Dictionary<string, CsvRecord> result =
                new Dictionary<string, CsvRecord>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> requiredMarkers =
                new HashSet<string>(RequiredMarkers, StringComparer.OrdinalIgnoreCase);

            foreach (CsvRecord record in document.Records)
            {
                string marker = record.GetCell(1).Value.Trim();
                if (!requiredMarkers.Contains(marker))
                {
                    continue;
                }

                if (result.ContainsKey(marker))
                {
                    AddError(
                        diagnostics,
                        document.SourcePath,
                        record.RecordNumber,
                        1,
                        $"元数据标记“{marker}”重复。");
                    hasStructuralError = true;
                    continue;
                }

                result.Add(marker, record);
            }

            foreach (string marker in RequiredMarkers)
            {
                if (result.ContainsKey(marker))
                {
                    continue;
                }

                AddError(
                    diagnostics,
                    document.SourcePath,
                    0,
                    1,
                    $"缺少必需的元数据标记“{marker}”。");
                hasStructuralError = true;
            }

            return result;
        }

        /// <summary>
        /// 根据 #var 的有效列范围建立字段 Schema，并预解析 #default。
        /// </summary>
        private static List<ConfigFieldSchema> ParseFields(
            string sourcePath,
            IReadOnlyDictionary<string, CsvRecord> markerRecords,
            int dataStartColumn,
            ICollection<ConfigTableDiagnostic> diagnostics)
        {
            CsvRecord varRecord = markerRecords[VarMarker];
            CsvRecord typeRecord = markerRecords[TypeMarker];
            CsvRecord descriptionRecord = markerRecords[DescriptionMarker];
            CsvRecord checkRecord = markerRecords[CheckMarker];
            CsvRecord defaultRecord = markerRecords[DefaultMarker];

            int lastFieldColumn = FindLastNonEmptyColumn(
                varRecord,
                dataStartColumn);
            if (lastFieldColumn < dataStartColumn)
            {
                AddError(
                    diagnostics,
                    sourcePath,
                    varRecord.RecordNumber,
                    dataStartColumn,
                    "#var 行没有定义任何可导出的字段。");
                return new List<ConfigFieldSchema>();
            }

            int metadataColumnCount = new[]
                {
                    varRecord.CellCount,
                    typeRecord.CellCount,
                    descriptionRecord.CellCount,
                    checkRecord.CellCount,
                    defaultRecord.CellCount
                }
                .Max();

            ValidateNoMetadataBeyondFields(
                sourcePath,
                markerRecords,
                lastFieldColumn,
                metadataColumnCount,
                diagnostics);

            List<ConfigFieldSchema> fields = new List<ConfigFieldSchema>();
            HashSet<string> fieldNames = new HashSet<string>(StringComparer.Ordinal);

            for (int column = dataStartColumn; column <= lastFieldColumn; column++)
            {
                string fieldName = varRecord.GetCell(column).Value.Trim();
                string typeToken = typeRecord.GetCell(column).Value.Trim();

                if (string.IsNullOrEmpty(fieldName))
                {
                    AddError(
                        diagnostics,
                        sourcePath,
                        varRecord.RecordNumber,
                        column,
                        "字段范围中不允许出现空的 #var；请删除中间空列或补充字段名。");
                    continue;
                }

                if (!IsValidIdentifier(fieldName))
                {
                    AddError(
                        diagnostics,
                        sourcePath,
                        varRecord.RecordNumber,
                        column,
                        $"字段名“{fieldName}”不是有效的 C# 标识符。");
                    continue;
                }

                if (!fieldNames.Add(fieldName))
                {
                    AddError(
                        diagnostics,
                        sourcePath,
                        varRecord.RecordNumber,
                        column,
                        $"字段名“{fieldName}”重复。");
                    continue;
                }

                if (!ConfigValueConverter.TryParseKind(
                        typeToken,
                        out ConfigFieldKind kind))
                {
                    AddError(
                        diagnostics,
                        sourcePath,
                        typeRecord.RecordNumber,
                        column,
                        $"不支持字段类型“{typeToken}”。当前只支持 int、float、bool、string " +
                        "及其 List<T>。");
                    continue;
                }

                string defaultRawValue = defaultRecord.GetCell(column).Value;
                ConfigFieldSchema field = new ConfigFieldSchema
                {
                    SourceColumn = column,
                    Name = fieldName,
                    Kind = kind,
                    Description = descriptionRecord.GetCell(column).Value,
                    CheckExpression = checkRecord.GetCell(column).Value,
                    DefaultRawValue = defaultRawValue,
                    HasExplicitDefault = !string.IsNullOrEmpty(defaultRawValue)
                };

                if (field.HasExplicitDefault)
                {
                    if (ConfigValueConverter.TryConvert(
                            defaultRawValue,
                            kind,
                            out object defaultValue,
                            out string error,
                            out string warning))
                    {
                        field.ParsedDefaultValue = defaultValue;
                        AddWarningIfNeeded(
                            diagnostics,
                            sourcePath,
                            defaultRecord.RecordNumber,
                            column,
                            warning);
                    }
                    else
                    {
                        AddError(
                            diagnostics,
                            sourcePath,
                            defaultRecord.RecordNumber,
                            column,
                            $"字段“{fieldName}”的默认值无效：{error}");
                    }
                }

                fields.Add(field);
            }

            ValidateIdField(sourcePath, fields, markerRecords, diagnostics);
            return fields;
        }

        private static void ValidateNoMetadataBeyondFields(
            string sourcePath,
            IReadOnlyDictionary<string, CsvRecord> markerRecords,
            int lastFieldColumn,
            int metadataColumnCount,
            ICollection<ConfigTableDiagnostic> diagnostics)
        {
            for (int column = lastFieldColumn + 1;
                 column <= metadataColumnCount;
                 column++)
            {
                foreach (KeyValuePair<string, CsvRecord> pair in markerRecords)
                {
                    if (string.IsNullOrEmpty(pair.Value.GetCell(column).Value))
                    {
                        continue;
                    }

                    AddError(
                        diagnostics,
                        sourcePath,
                        pair.Value.RecordNumber,
                        column,
                        $"“{pair.Key}”在最后一个 #var 字段之后仍包含内容，无法确定列归属。");
                }
            }
        }

        private static void ValidateIdField(
            string sourcePath,
            IReadOnlyList<ConfigFieldSchema> fields,
            IReadOnlyDictionary<string, CsvRecord> markerRecords,
            ICollection<ConfigTableDiagnostic> diagnostics)
        {
            ConfigFieldSchema idField = fields.FirstOrDefault(field =>
                string.Equals(field.Name, "ID", StringComparison.Ordinal));

            if (idField == null)
            {
                AddError(
                    diagnostics,
                    sourcePath,
                    markerRecords[VarMarker].RecordNumber,
                    1,
                    "配置表必须包含名称严格为“ID”的 int 字段，供运行时建立索引。");
                return;
            }

            if (idField.Kind != ConfigFieldKind.Int)
            {
                AddError(
                    diagnostics,
                    sourcePath,
                    markerRecords[TypeMarker].RecordNumber,
                    idField.SourceColumn,
                    "ID 字段的类型必须为 int。");
            }
        }

        /// <summary>
        /// 从配置的数据开始行读取数据。
        /// 完全空的数据区记录会被跳过；每个非空记录都会执行默认值回退和强类型转换。
        /// </summary>
        private static List<ConfigTableDataRow> ParseDataRows(
            CsvDocument document,
            ConfigTableSchema schema,
            ICollection<ConfigTableDiagnostic> diagnostics)
        {
            List<ConfigTableDataRow> rows = new List<ConfigTableDataRow>();
            HashSet<int> ids = new HashSet<int>();
            int idIndex = schema.Fields
                .Select((field, index) => new { field, index })
                .First(item => string.Equals(
                    item.field.Name,
                    "ID",
                    StringComparison.Ordinal))
                .index;

            for (int recordIndex = schema.DataStartRow - 1;
                 recordIndex < document.Records.Count;
                 recordIndex++)
            {
                CsvRecord record = document.Records[recordIndex];
                bool hasRawData = schema.Fields.Any(field =>
                    !string.IsNullOrEmpty(
                        record.GetCell(field.SourceColumn).Value));

                if (!hasRawData)
                {
                    continue;
                }

                object[] values = new object[schema.Fields.Count];
                bool rowIsValid = true;

                for (int fieldIndex = 0;
                     fieldIndex < schema.Fields.Count;
                     fieldIndex++)
                {
                    ConfigFieldSchema field = schema.Fields[fieldIndex];
                    string rawValue = record.GetCell(field.SourceColumn).Value;

                    if (string.Equals(
                            field.Name,
                            "ID",
                            StringComparison.Ordinal) &&
                        string.IsNullOrEmpty(rawValue))
                    {
                        AddError(
                            diagnostics,
                            document.SourcePath,
                            record.RecordNumber,
                            field.SourceColumn,
                            "ID 不能为空；ID 不参与默认值回退。");
                        values[fieldIndex] = 0;
                        rowIsValid = false;
                        continue;
                    }

                    if (string.IsNullOrEmpty(rawValue))
                    {
                        values[fieldIndex] = field.HasExplicitDefault
                            ? ConfigValueConverter.CloneValue(
                                field.ParsedDefaultValue,
                                field.Kind)
                            : ConfigValueConverter.CreateTypeDefault(field.Kind);
                        continue;
                    }

                    if (ConfigValueConverter.TryConvert(
                            rawValue,
                            field.Kind,
                            out object converted,
                            out string error,
                            out string warning))
                    {
                        values[fieldIndex] = converted;
                        AddWarningIfNeeded(
                            diagnostics,
                            document.SourcePath,
                            record.RecordNumber,
                            field.SourceColumn,
                            warning);
                    }
                    else
                    {
                        AddError(
                            diagnostics,
                            document.SourcePath,
                            record.RecordNumber,
                            field.SourceColumn,
                            $"字段“{field.Name}”解析失败：{error}");
                        values[fieldIndex] =
                            ConfigValueConverter.CreateTypeDefault(field.Kind);
                        rowIsValid = false;
                    }
                }

                if (rowIsValid)
                {
                    int id = (int)values[idIndex];
                    if (!ids.Add(id))
                    {
                        AddError(
                            diagnostics,
                            document.SourcePath,
                            record.RecordNumber,
                            schema.IdField.SourceColumn,
                            $"ID {id} 在当前配置表中重复。");
                        rowIsValid = false;
                    }
                }

                if (rowIsValid)
                {
                    rows.Add(new ConfigTableDataRow(
                        record.RecordNumber,
                        values));
                }
            }

            return rows;
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

        private static bool IsValidIdentifier(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   IdentifierRegex.IsMatch(value) &&
                   !CSharpKeywords.Contains(value);
        }

        private static void AddWarningIfNeeded(
            ICollection<ConfigTableDiagnostic> diagnostics,
            string sourcePath,
            int row,
            int column,
            string warning)
        {
            if (string.IsNullOrEmpty(warning))
            {
                return;
            }

            diagnostics.Add(new ConfigTableDiagnostic(
                ConfigDiagnosticSeverity.Warning,
                sourcePath,
                row,
                column,
                warning));
        }

        private static void AddError(
            ICollection<ConfigTableDiagnostic> diagnostics,
            string sourcePath,
            int row,
            int column,
            string message)
        {
            diagnostics.Add(new ConfigTableDiagnostic(
                ConfigDiagnosticSeverity.Error,
                sourcePath,
                row,
                column,
                message));
        }
    }
}
