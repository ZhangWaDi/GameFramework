using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameFramework.ConfigData.Editor
{
    /// <summary>
    /// 配置字段类型注册与值转换入口。
    /// 所有数值均使用固定文化格式，避免导表结果受操作系统区域设置影响。
    /// </summary>
    internal static class ConfigValueConverter
    {
        private static readonly IReadOnlyDictionary<string, ConfigFieldKind> KindByToken = new Dictionary<string, ConfigFieldKind>(StringComparer.OrdinalIgnoreCase) { ["int"] = ConfigFieldKind.Int, ["float"] = ConfigFieldKind.Float, ["bool"] = ConfigFieldKind.Bool, ["string"] = ConfigFieldKind.String, ["List<int>"] = ConfigFieldKind.IntList, ["List<float>"] = ConfigFieldKind.FloatList, ["List<bool>"] = ConfigFieldKind.BoolList, ["List<string>"] = ConfigFieldKind.StringList };

        /// <summary>
        /// 将 #type 单元格转换为受支持的八种字段类型之一。
        /// </summary>
        public static bool TryParseKind(string token, out ConfigFieldKind kind)
        {
            return KindByToken.TryGetValue(token?.Trim() ?? string.Empty, out kind);
        }

        /// <summary>
        /// 返回生成 C# 字段时使用的规范类型名称。
        /// </summary>
        public static string GetCSharpTypeName(ConfigFieldKind kind)
        {
            return kind switch
            {
                ConfigFieldKind.Int => "int",
                ConfigFieldKind.Float => "float",
                ConfigFieldKind.Bool => "bool",
                ConfigFieldKind.String => "string",
                ConfigFieldKind.IntList => "List<int>",
                ConfigFieldKind.FloatList => "List<float>",
                ConfigFieldKind.BoolList => "List<bool>",
                ConfigFieldKind.StringList => "List<string>",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        /// <summary>
        /// 将一个非空单元格文本转换为字段目标类型。
        /// 转换失败时返回错误原因；兼容性处理会通过 warning 返回提示。
        /// </summary>
        public static bool TryConvert(
            string rawValue,
            ConfigFieldKind kind,
            out object value,
            out string error,
            out string warning)
        {
            error = null;
            warning = null;

            switch (kind)
            {
                case ConfigFieldKind.Int:
                    return TryConvertInt(rawValue, out value, out error);
                case ConfigFieldKind.Float:
                    return TryConvertFloat(
                        rawValue,
                        out value,
                        out error,
                        out warning);
                case ConfigFieldKind.Bool:
                    return TryConvertBool(rawValue, out value, out error);
                case ConfigFieldKind.String:
                    value = rawValue;
                    return true;
                case ConfigFieldKind.IntList:
                    return TryConvertList(
                        rawValue,
                        ConfigFieldKind.Int,
                        out value,
                        out error,
                        out warning);
                case ConfigFieldKind.FloatList:
                    return TryConvertList(
                        rawValue,
                        ConfigFieldKind.Float,
                        out value,
                        out error,
                        out warning);
                case ConfigFieldKind.BoolList:
                    return TryConvertList(
                        rawValue,
                        ConfigFieldKind.Bool,
                        out value,
                        out error,
                        out warning);
                case ConfigFieldKind.StringList:
                    return TryConvertList(
                        rawValue,
                        ConfigFieldKind.String,
                        out value,
                        out error,
                        out warning);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>
        /// 返回字段在数据值和 #default 都为空时使用的类型默认值。
        /// 列表始终返回新的空列表，避免多行数据共享同一个可变对象。
        /// </summary>
        public static object CreateTypeDefault(ConfigFieldKind kind)
        {
            return kind switch
            {
                ConfigFieldKind.Int => 0,
                ConfigFieldKind.Float => 0f,
                ConfigFieldKind.Bool => false,
                ConfigFieldKind.String => string.Empty,
                ConfigFieldKind.IntList => new List<int>(),
                ConfigFieldKind.FloatList => new List<float>(),
                ConfigFieldKind.BoolList => new List<bool>(),
                ConfigFieldKind.StringList => new List<string>(),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        /// <summary>
        /// 为某一行复制已解析的列默认值。
        /// 标量可直接复用，列表必须复制以避免不同配置行互相修改。
        /// </summary>
        public static object CloneValue(object value, ConfigFieldKind kind)
        {
            return kind switch
            {
                ConfigFieldKind.IntList => new List<int>((List<int>)value),
                ConfigFieldKind.FloatList => new List<float>((List<float>)value),
                ConfigFieldKind.BoolList => new List<bool>((List<bool>)value),
                ConfigFieldKind.StringList => new List<string>((List<string>)value),
                _ => value
            };
        }

        private static bool TryConvertInt(
            string rawValue,
            out object value,
            out string error)
        {
            if (int.TryParse(
                    rawValue.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsed))
            {
                value = parsed;
                error = null;
                return true;
            }

            value = 0;
            error = $"“{rawValue}”不是有效的 int。";
            return false;
        }

        private static bool TryConvertFloat(
            string rawValue,
            out object value,
            out string error,
            out string warning)
        {
            string normalized = rawValue.Trim();
            warning = null;

            if (normalized.Length > 1 && normalized.EndsWith("f", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
                warning =
                    $"浮点值“{rawValue}”使用了 C# 后缀 f；建议在配置表中改写为“{normalized}”。";
            }

            if (float.TryParse(
                    normalized,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float parsed) &&
                !float.IsNaN(parsed) &&
                !float.IsInfinity(parsed))
            {
                value = parsed;
                error = null;
                return true;
            }

            value = 0f;
            error = $"“{rawValue}”不是有效的有限 float。";
            return false;
        }

        private static bool TryConvertBool(
            string rawValue,
            out object value,
            out string error)
        {
            if (bool.TryParse(rawValue.Trim(), out bool parsed))
            {
                value = parsed;
                error = null;
                return true;
            }

            value = false;
            error = $"“{rawValue}”不是有效的 bool，只允许 TRUE 或 FALSE。";
            return false;
        }

        /// <summary>
        /// 解析一个以英文逗号分隔的列表单元格。
        /// 外层 CSV 引号已由 StandardCsvParser 移除，因此此处只处理列表内部语法。
        /// </summary>
        private static bool TryConvertList(
            string rawValue,
            ConfigFieldKind elementKind,
            out object value,
            out string error,
            out string warning)
        {
            string[] tokens = rawValue.Split(new[] { ',' }, StringSplitOptions.None);
            warning = null;

            switch (elementKind)
            {
                case ConfigFieldKind.Int:
                    {
                        List<int> values = new(tokens.Length);
                        if (!TryFillList(tokens, elementKind, values, out error, out warning))
                        {
                            value = values;
                            return false;
                        }

                        value = values;
                        return true;
                    }
                case ConfigFieldKind.Float:
                    {
                        List<float> values = new(tokens.Length);
                        if (!TryFillList(tokens, elementKind, values, out error, out warning))
                        {
                            value = values;
                            return false;
                        }

                        value = values;
                        return true;
                    }
                case ConfigFieldKind.Bool:
                    {
                        List<bool> values = new(tokens.Length);
                        if (!TryFillList(tokens, elementKind, values, out error, out warning))
                        {
                            value = values;
                            return false;
                        }

                        value = values;
                        return true;
                    }
                case ConfigFieldKind.String:
                    {
                        List<string> values = new(tokens.Length);
                        if (!TryFillList(tokens, elementKind, values, out error, out warning))
                        {
                            value = values;
                            return false;
                        }

                        value = values;
                        return true;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(elementKind), elementKind, null);
            }
        }

        private static bool TryFillList<T>(
            IReadOnlyList<string> tokens,
            ConfigFieldKind elementKind,
            ICollection<T> output,
            out string error,
            out string warning)
        {
            warning = null;

            for (int index = 0; index < tokens.Count; index++)
            {
                string token = tokens[index];
                if (string.IsNullOrEmpty(token))
                {
                    error = $"列表第 {index + 1} 个元素为空。";
                    return false;
                }

                if (!TryConvert(
                        token,
                        elementKind,
                        out object converted,
                        out string itemError,
                        out string itemWarning))
                {
                    error = $"列表第 {index + 1} 个元素解析失败：{itemError}";
                    return false;
                }

                if (!string.IsNullOrEmpty(itemWarning))
                {
                    warning = string.IsNullOrEmpty(warning)
                        ? itemWarning
                        : $"{warning} {itemWarning}";
                }

                output.Add((T)converted);
            }

            error = null;
            return true;
        }
    }
}
