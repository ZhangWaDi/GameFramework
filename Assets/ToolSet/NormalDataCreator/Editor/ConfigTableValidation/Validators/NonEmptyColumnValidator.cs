using System;
using System.Collections.Generic;

namespace GameFramework.ConfigData.Editor
{
    /// <summary>
    /// NonEmpty：数据行中的该列不允许为空或只包含空白字符。
    /// </summary>
    internal sealed class NonEmptyColumnValidator : IConfigTableColumnValidator
    {
        public string Tag => "NonEmpty";

        public void Validate(
            ConfigTableColumnValidationContext context,
            ICollection<ConfigTableDiagnostic> diagnostics)
        {
            foreach (ConfigTableColumnValue value in context.Values)
            {
                if (!string.IsNullOrWhiteSpace(value.Value))
                {
                    continue;
                }

                diagnostics.Add(new ConfigTableDiagnostic(
                    ConfigDiagnosticSeverity.Error,
                    context.SourcePath,
                    value.SourceRow,
                    context.SourceColumn,
                    $"字段“{context.FieldName}”标记了 NonEmpty，值不能为空。"));
            }
        }
    }
}
