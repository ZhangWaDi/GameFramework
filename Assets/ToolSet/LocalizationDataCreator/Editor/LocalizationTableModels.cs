using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GameFramework.LocalizationData.Editor
{
    internal enum LocalizationDiagnosticSeverity
    {
        Warning,
        Error
    }

    internal sealed class LocalizationDiagnostic
    {
        public LocalizationDiagnostic(LocalizationDiagnosticSeverity severity, string sourcePath, int row, int column, string message)
        {
            Severity = severity;
            SourcePath = sourcePath ?? string.Empty;
            Row = row;
            Column = column;
            Message = message ?? string.Empty;
        }

        public LocalizationDiagnosticSeverity Severity { get; }
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

    internal sealed class LocalizationEntryDefinition
    {
        public LocalizationEntryDefinition(string sourcePath, int sourceRow, string key, IReadOnlyDictionary<string, string> values)
        {
            SourcePath = sourcePath;
            SourceRow = sourceRow;
            Key = key;
            Values = values;
        }

        public string SourcePath { get; }
        public int SourceRow { get; }
        public string Key { get; }
        public IReadOnlyDictionary<string, string> Values { get; }
    }

    internal sealed class LocalizationTableDefinition
    {
        public LocalizationTableDefinition(string tableId, string sourcePath, IReadOnlyList<LocalizationEntryDefinition> entries)
        {
            TableId = tableId;
            SourcePath = sourcePath;
            Entries = entries;
        }

        public string TableId { get; }
        public string SourcePath { get; }
        public IReadOnlyList<LocalizationEntryDefinition> Entries { get; }
    }

    internal sealed class LocalizationDataSet
    {
        public LocalizationDataSet(IReadOnlyList<string> languages, IReadOnlyList<LocalizationTableDefinition> tables)
        {
            Languages = languages;
            Tables = tables;
        }

        public IReadOnlyList<string> Languages { get; }
        public IReadOnlyList<LocalizationTableDefinition> Tables { get; }
        public int TableCount => Tables.Count;
        public int EntryCount => Tables.Sum(table => table.Entries.Count);
    }

    internal sealed class LocalizationGenerationReport
    {
        public int TableCount { get; set; }
        public int LanguageCount { get; set; }
        public int KeyCount { get; set; }
        public int WarningCount { get; set; }
        public bool ScriptChanged { get; set; }
        public bool AssetsBuilt { get; set; }
        public string ScriptOutputFolder { get; set; }
        public string SOAssetOutputFolder { get; set; }
        public string CatalogAssetPath { get; set; }
        public IReadOnlyList<string> AssetPaths { get; set; } = Array.Empty<string>();

        public string ToDisplayMessage()
        {
            StringBuilder message = new();
            message.AppendLine($"本地化配置表：{TableCount}");
            message.AppendLine($"语言：{LanguageCount}");
            message.AppendLine($"Key：{KeyCount}");
            message.AppendLine($"警告：{WarningCount}");
            message.AppendLine($"SO 脚本输出目录：{ScriptOutputFolder}");
            message.AppendLine($"SO 资产输出目录：{SOAssetOutputFolder}");
            if (!string.IsNullOrEmpty(CatalogAssetPath))
            {
                message.AppendLine($"语言目录：{CatalogAssetPath}");
            }

            if (ScriptChanged && !AssetsBuilt)
            {
                message.Append("本地化生成脚本已更新，Unity 编译完成后会自动续建语言 SO。");
            }
            else
            {
                message.Append($"SO 构建：{(AssetsBuilt ? "完成" : "未执行")}，资产数量：{AssetPaths.Count}");
            }

            return message.ToString();
        }
    }

    internal sealed class LocalizationTableValidationException : Exception
    {
        public LocalizationTableValidationException(string summary, IReadOnlyList<LocalizationDiagnostic> diagnostics) : base(BuildMessage(summary, diagnostics))
        {
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<LocalizationDiagnostic> Diagnostics { get; }

        private static string BuildMessage(string summary, IReadOnlyList<LocalizationDiagnostic> diagnostics)
        {
            LocalizationDiagnostic[] errors = diagnostics.Where(item => item.Severity == LocalizationDiagnosticSeverity.Error).ToArray();
            StringBuilder message = new(summary);
            foreach (LocalizationDiagnostic error in errors.Take(20))
            {
                message.AppendLine();
                message.Append(error);
            }

            if (errors.Length > 20)
            {
                message.AppendLine();
                message.Append($"其余 {errors.Length - 20} 条错误请查看 Console。");
            }

            return message.ToString();
        }
    }

    internal static class LocalizationGenerationPaths
    {
        public const string GeneratedNamespace = "GameFramework.LocalizationSystem.Generated";
        public const string GeneratedTypeName = "LocalizationDataSO";
        public const string GeneratedEntryTypeName = "LocalizationDataEntry";
        public const string GeneratedSectionTypeName = "LocalizationTableSection";
        public const string GeneratedPackageReferenceTypeName = "LocalizationPackageReference";
        public const string GeneratedCatalogTypeName = "LocalizationCatalogSO";
        public const string GeneratedLanguageTypeName = "LocalizationLanguage";
        public const string GeneratedTypeFullName = GeneratedNamespace + "." + GeneratedTypeName;
        public const string GeneratedEntryTypeFullName = GeneratedNamespace + "." + GeneratedEntryTypeName;
        public const string GeneratedSectionTypeFullName = GeneratedNamespace + "." + GeneratedSectionTypeName;
        public const string GeneratedPackageReferenceTypeFullName = GeneratedNamespace + "." + GeneratedPackageReferenceTypeName;
        public const string GeneratedCatalogTypeFullName = GeneratedNamespace + "." + GeneratedCatalogTypeName;
        public const string GeneratedFileName = GeneratedTypeName + ".cs";
        public const string GeneratedCatalogFileName = GeneratedCatalogTypeName + ".cs";
        public const string GeneratedLanguageFileName = GeneratedLanguageTypeName + ".cs";
        public const string CatalogFileName = "LocalizationCatalog.asset";
        public const string PreferredDefaultLanguage = "English";
        public const string GeneratorId = "GameFramework.LocalizationDataCreator";
        public const string DefaultGeneratedScriptFolder = "Assets/C#Scripts/LocalizationDataSOScript";
        public const string DefaultAssetFolder = "Assets/Resources/LocalizationDataSOAssets";
    }
}
