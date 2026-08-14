using System;
using System.Collections.Generic;

namespace GameFramework.ConfigData.Editor
{
    /// <summary>
    /// Unique：该列的全部非空值在当前配置表中必须唯一。
    /// 比较时忽略值首尾的空白字符，但保留大小写差异。
    /// </summary>
    internal sealed class UniqueColumnValidator : IConfigTableColumnValidator
    {
        public string Tag => "Unique";

        public void Validate(
            ConfigTableColumnValidationContext context,
            ICollection<ConfigTableDiagnostic> diagnostics)
        {
            Dictionary<string, int> firstRowByValue = new(StringComparer.Ordinal);

            foreach (ConfigTableColumnValue value in context.Values)
            {
                string normalizedValue = value.Value.Trim();
                if (normalizedValue.Length == 0)
                {
                    // 空值是否允许由 NonEmpty 独立决定，避免规则之间互相耦合。
                    continue;
                }

                if (firstRowByValue.TryGetValue(
                        normalizedValue,
                        out int firstSourceRow))
                {
                    diagnostics.Add(new ConfigTableDiagnostic(
                        ConfigDiagnosticSeverity.Error,
                        context.SourcePath,
                        value.SourceRow,
                        context.SourceColumn,
                        $"字段“{context.FieldName}”标记了 Unique，值“{value.Value}”重复；" +
                        $"首次出现在第 {firstSourceRow} 行。"));
                    continue;
                }

                firstRowByValue.Add(normalizedValue, value.SourceRow);
            }
        }
    }
}
