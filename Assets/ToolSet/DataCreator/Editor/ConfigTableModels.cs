using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GameFramework.ConfigData.Editor
{
    internal enum ConfigFieldKind
    {
        Int,
        Float,
        Bool,
        String,
        IntList,
        FloatList,
        BoolList,
        StringList
    }

    internal enum ConfigDiagnosticSeverity
    {
        Warning,
        Error
    }

    /// <summary>
    /// 表示配置表处理过程中的一条定位信息。
    /// 行、列均采用与 Excel 一致的 1 基坐标，便于开发者回到源表修正。
    /// </summary>
    internal sealed class ConfigTableDiagnostic
    {
        public ConfigTableDiagnostic(
            ConfigDiagnosticSeverity severity,
            string sourcePath,
            int row,
            int column,
            string message)
        {
            Severity = severity;
            SourcePath = sourcePath ?? string.Empty;
            Row = row;
            Column = column;
            Message = message ?? string.Empty;
        }

        public ConfigDiagnosticSeverity Severity { get; }

        public string SourcePath { get; }

        public int Row { get; }

        public int Column { get; }

        public string Message { get; }

        public override string ToString()
        {
            string location = Row > 0 ? $"{SourcePath}({Row},{Math.Max(1, Column)})" : SourcePath;

            return $"[{Severity}] {location}: {Message}";
        }
    }

    /// <summary>
    /// CSV 中一列配置字段的完整 Schema。
    /// </summary>
    internal sealed class ConfigFieldSchema
    {
        public int SourceColumn { get; set; }

        public string Name { get; set; }

        public ConfigFieldKind Kind { get; set; }

        public string Description { get; set; }

        public string CheckExpression { get; set; }

        public string DefaultRawValue { get; set; }

        public bool HasExplicitDefault { get; set; }

        public object ParsedDefaultValue { get; set; }
    }

    /// <summary>
    /// 单张配置表的结构定义。
    /// </summary>
    internal sealed class ConfigTableSchema
    {
        public string SourcePath { get; set; }

        public string TableName { get; set; }

        public int DataStartRow { get; set; }

        public int DataStartColumn { get; set; }

        public IReadOnlyList<ConfigFieldSchema> Fields { get; set; }

        public ConfigFieldSchema IdField =>
            Fields.First(field => string.Equals(
                field.Name,
                "ID",
                StringComparison.Ordinal));

        public string DataTypeFullName =>
            $"{ConfigTableGenerationPaths.GeneratedNamespace}.{TableName}";

        public string TableTypeFullName =>
            $"{ConfigTableGenerationPaths.GeneratedNamespace}.{TableName}SO";
    }

    /// <summary>
    /// 已完成默认值回退和类型转换的一行配置数据。
    /// </summary>
    internal sealed class ConfigTableDataRow
    {
        public ConfigTableDataRow(int sourceRow, IReadOnlyList<object> values)
        {
            SourceRow = sourceRow;
            Values = values;
        }

        public int SourceRow { get; }

        public IReadOnlyList<object> Values { get; }
    }

    /// <summary>
    /// 一张可用于代码生成和 SO 构建的完整配置表。
    /// </summary>
    internal sealed class ConfigTableDefinition
    {
        public ConfigTableDefinition(
            ConfigTableSchema schema,
            IReadOnlyList<ConfigTableDataRow> rows)
        {
            Schema = schema;
            Rows = rows;
        }

        public ConfigTableSchema Schema { get; }

        public IReadOnlyList<ConfigTableDataRow> Rows { get; }
    }

    /// <summary>
    /// 配置表生成流程的结果摘要。
    /// </summary>
    internal sealed class ConfigTableGenerationReport
    {
        public int TableCount { get; set; }

        public int DataRowCount { get; set; }

        public int WarningCount { get; set; }

        public int GeneratedScriptCount { get; set; }

        public bool ScriptsChanged { get; set; }

        public bool AssetsBuilt { get; set; }

        public string ScriptOutputFolder { get; set; }

        public string SOAssetOutputFolder { get; set; }

        public IReadOnlyList<string> AssetPaths { get; set; } = Array.Empty<string>();

        public string ToDisplayMessage()
        {
            StringBuilder message = new();
            message.AppendLine($"配置表：{TableCount}");
            message.AppendLine($"数据行：{DataRowCount}");
            message.AppendLine($"警告：{WarningCount}");
            message.AppendLine($"脚本输出目录：{ScriptOutputFolder}");
            message.AppendLine($"SO 资产输出目录：{SOAssetOutputFolder}");

            if (ScriptsChanged && !AssetsBuilt)
            {
                message.Append("强类型代码已更新，Unity 编译完成后会自动续建 SO。");
            }
            else
            {
                message.AppendLine($"生成脚本：{GeneratedScriptCount}");
                message.Append($"SO 构建：{(AssetsBuilt ? "完成" : "未执行")}");
            }

            return message.ToString();
        }
    }

    /// <summary>
    /// 当配置表包含阻断生成的错误时抛出的聚合异常。
    /// </summary>
    internal sealed class ConfigTableValidationException : Exception
    {
        public ConfigTableValidationException(
            string summary,
            IReadOnlyList<ConfigTableDiagnostic> diagnostics)
            : base(BuildMessage(summary, diagnostics))
        {
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<ConfigTableDiagnostic> Diagnostics { get; }

        private static string BuildMessage(
            string summary,
            IReadOnlyList<ConfigTableDiagnostic> diagnostics)
        {
            IEnumerable<ConfigTableDiagnostic> errors = diagnostics.Where(item => item.Severity == ConfigDiagnosticSeverity.Error);

            StringBuilder message = new(summary);
            foreach (ConfigTableDiagnostic error in errors.Take(20))
            {
                message.AppendLine();
                message.Append(error);
            }

            int errorCount = errors.Count();
            if (errorCount > 20)
            {
                message.AppendLine();
                message.Append($"其余 {errorCount - 20} 条错误请查看 Console。");
            }

            return message.ToString();
        }
    }

    /// <summary>
    /// 集中定义生成物位置和命名空间，避免路径字符串散落在各处理阶段。
    /// </summary>
    internal static class ConfigTableGenerationPaths
    {
        public const string GeneratedNamespace = "GameFramework.ConfigData.Generated";
        public const string DefaultGeneratedScriptFolder = "Assets/C#Scripts/Generated/ConfigData";
        public const string DefaultTableAssetFolder = "Assets/Resources/ConfigData/Tables";
        public const string DatabaseAssetPath = "Assets/Resources/ConfigData/ConfigDatabase.asset";
    }
}
