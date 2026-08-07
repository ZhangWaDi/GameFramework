using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using UnityEngine;

namespace GameFramework.ConfigData.Editor
{
    /// <summary>
    /// 根据配置表 Schema 生成强类型数据类和薄 SO 类型。
    /// 生成代码只描述数据结构，不包含 CSV 解析或字段转换逻辑。
    /// </summary>
    internal static class ConfigTableCodeGenerator
    {
        /// <summary>
        /// 为全部配置表生成确定性的 C# 文件。
        /// 只有内容实际变化时才写盘，从而避免无意义的 Unity 重编译。
        /// 当输出目录发生变化时，会迁移本工具拥有的旧脚本及其 .meta，
        /// 从而避免重复类型并保留已有 SO 资产引用的脚本 GUID。
        /// </summary>
        public static bool Generate(
            IReadOnlyList<ConfigTableDefinition> definitions,
            string scriptAssetFolder,
            string previousScriptAssetFolder,
            out int generatedScriptCount)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            string normalizedScriptAssetFolder = NormalizeAssetFolderPath(scriptAssetFolder, nameof(scriptAssetFolder));
            string outputDirectory = GetAbsoluteProjectPath(normalizedScriptAssetFolder);
            Directory.CreateDirectory(outputDirectory);

            string normalizedPreviousFolder = NormalizeOptionalAssetFolderPath(previousScriptAssetFolder);
            bool outputFolderChanged = !string.IsNullOrEmpty(normalizedPreviousFolder) && !string.Equals(normalizedPreviousFolder, normalizedScriptAssetFolder, StringComparison.OrdinalIgnoreCase);
            string previousDirectory = outputFolderChanged ? GetAbsoluteProjectPath(normalizedPreviousFolder) : null;

            ValidateOwnedGeneratedFiles(
                definitions,
                outputDirectory,
                previousDirectory);

            bool anyChanged = false;
            generatedScriptCount = 0;

            foreach (ConfigTableDefinition definition in definitions)
            {
                string tableName = definition.Schema.TableName;
                if (previousDirectory != null)
                {
                    anyChanged |= MoveOwnedGeneratedFile(
                        previousDirectory,
                        outputDirectory,
                        $"{tableName}.cs");
                    anyChanged |= MoveOwnedGeneratedFile(
                        previousDirectory,
                        outputDirectory,
                        $"{tableName}SO.cs");
                    anyChanged |= DeleteOwnedGeneratedFile(
                        Path.Combine(
                            previousDirectory,
                            $"{tableName}.Generated.cs"));
                }

                string dataFilePath = Path.Combine(outputDirectory, $"{tableName}.cs");
                string tableFilePath = Path.Combine(outputDirectory, $"{tableName}SO.cs");

                anyChanged |= WriteIfChanged(
                    dataFilePath,
                    BuildDataSource(definition.Schema));
                anyChanged |= WriteIfChanged(
                    tableFilePath,
                    BuildTableSource(definition.Schema));
                anyChanged |= DeleteOwnedGeneratedFile(
                    Path.Combine(
                        outputDirectory,
                        $"{tableName}.Generated.cs"));

                generatedScriptCount += 2;
            }

            return anyChanged;
        }

        /// <summary>
        /// 构建单张表的数据类源码。
        /// #desc 会转换为 XML 注释；ID 使用可序列化属性实现基类索引契约。
        /// </summary>
        private static string BuildDataSource(ConfigTableSchema schema)
        {
            StringBuilder source = new();
            AppendGeneratedHeader(source);
            source.AppendLine("using System;");
            source.AppendLine("using System.Collections.Generic;");
            source.AppendLine("using UnityEngine;");
            source.AppendLine("using GameFramework.ConfigSystem;");
            source.AppendLine();
            source.AppendLine($"namespace {ConfigTableGenerationPaths.GeneratedNamespace}");
            source.AppendLine("{");
            source.AppendLine("    [Serializable]");
            source.AppendLine($"    public sealed class {schema.TableName} : ConfigDataBase");
            source.AppendLine("    {");

            for (int index = 0; index < schema.Fields.Count; index++)
            {
                ConfigFieldSchema field = schema.Fields[index];

                if (string.Equals(field.Name, "ID", StringComparison.Ordinal))
                {
                    source.AppendLine("        [SerializeField]");
                    source.AppendLine("        private int id;");
                    source.AppendLine();
                    AppendXmlSummary(
                        source,
                        field.Description,
                        field.Name,
                        indentation: "        ");
                    source.AppendLine("        public override int ID");
                    source.AppendLine("        {");
                    source.AppendLine("            get => id;");
                    source.AppendLine("            set => id = value;");
                    source.AppendLine("        }");
                }
                else
                {
                    AppendXmlSummary(
                        source,
                        field.Description,
                        field.Name,
                        indentation: "        ");
                    string typeName = ConfigValueConverter.GetCSharpTypeName(field.Kind);
                    string initializer = GetInitializer(field.Kind);
                    source.AppendLine(
                        $"        public {typeName} {field.Name}{initializer};");
                }

                if (index < schema.Fields.Count - 1)
                {
                    source.AppendLine();
                }
            }

            source.AppendLine("    }");
            source.AppendLine("}");

            return source.ToString();
        }

        /// <summary>
        /// 构建具体 SO 类型源码。
        /// 文件名必须与 ScriptableObject 类名完全一致，否则 Unity 无法为资产写入有效 m_Script。
        /// </summary>
        private static string BuildTableSource(ConfigTableSchema schema)
        {
            StringBuilder source = new();
            AppendGeneratedHeader(source);
            source.AppendLine("using GameFramework.ConfigSystem;");
            source.AppendLine();
            source.AppendLine($"namespace {ConfigTableGenerationPaths.GeneratedNamespace}");
            source.AppendLine("{");
            source.AppendLine("    /// <summary>");
            source.AppendLine($"    /// {EscapeXml(schema.TableName)} 配置表资产类型。");
            source.AppendLine("    /// </summary>");
            source.AppendLine(
                $"    public sealed class {schema.TableName}SO : " +
                $"ConfigTableSO<{schema.TableName}>");
            source.AppendLine("    {");
            source.AppendLine("    }");
            source.AppendLine("}");

            return source.ToString();
        }

        private static void AppendGeneratedHeader(StringBuilder source)
        {
            source.AppendLine("// <auto-generated>");
            source.AppendLine("// 此文件由配置表工具生成，请勿手动修改。");
            source.AppendLine("// </auto-generated>");
            source.AppendLine();
        }

        /// <summary>
        /// 仅在源文件内容变化时写盘，避免触发无意义的 Unity 脚本编译。
        /// </summary>
        private static bool WriteIfChanged(string filePath, string content)
        {
            if (File.Exists(filePath))
            {
                EnsureOwnedGeneratedFile(filePath);
                if (string.Equals(
                        File.ReadAllText(filePath, Encoding.UTF8),
                        content,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            File.WriteAllText(
                filePath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }

        /// <summary>
        /// 在写盘前检查新旧目录中的精确生成文件名。
        /// 任何缺少自动生成标记的同名文件都会阻止流程，避免覆盖开发者代码。
        /// </summary>
        private static void ValidateOwnedGeneratedFiles(
            IReadOnlyList<ConfigTableDefinition> definitions,
            string outputDirectory,
            string previousDirectory)
        {
            foreach (ConfigTableDefinition definition in definitions)
            {
                string tableName = definition.Schema.TableName;
                EnsureOwnedGeneratedFileIfExists(
                    Path.Combine(outputDirectory, $"{tableName}.cs"));
                EnsureOwnedGeneratedFileIfExists(
                    Path.Combine(outputDirectory, $"{tableName}SO.cs"));
                EnsureOwnedGeneratedFileIfExists(
                    Path.Combine(
                        outputDirectory,
                        $"{tableName}.Generated.cs"));

                if (previousDirectory == null ||
                    !Directory.Exists(previousDirectory))
                {
                    continue;
                }

                EnsureOwnedGeneratedFileIfExists(
                    Path.Combine(previousDirectory, $"{tableName}.cs"));
                EnsureOwnedGeneratedFileIfExists(
                    Path.Combine(previousDirectory, $"{tableName}SO.cs"));
                EnsureOwnedGeneratedFileIfExists(
                    Path.Combine(
                        previousDirectory,
                        $"{tableName}.Generated.cs"));
            }
        }

        /// <summary>
        /// 把上一输出目录中的生成脚本迁移到新目录，并同步迁移 .meta。
        /// 若新目录中存在本工具此前生成的同名文件，以上一目录的 GUID 为准。
        /// </summary>
        private static bool MoveOwnedGeneratedFile(
            string sourceDirectory,
            string targetDirectory,
            string fileName)
        {
            string sourcePath = Path.Combine(sourceDirectory, fileName);
            if (!File.Exists(sourcePath))
            {
                return false;
            }

            EnsureOwnedGeneratedFile(sourcePath);
            string targetPath = Path.Combine(targetDirectory, fileName);
            EnsureOwnedGeneratedFileIfExists(targetPath);

            string sourceMetaPath = $"{sourcePath}.meta";
            string targetMetaPath = $"{targetPath}.meta";

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(sourcePath, targetPath);

            if (File.Exists(sourceMetaPath))
            {
                if (File.Exists(targetMetaPath))
                {
                    File.Delete(targetMetaPath);
                }

                File.Move(sourceMetaPath, targetMetaPath);
            }

            return true;
        }

        /// <summary>
        /// 删除本工具拥有的生成文件及其 Unity 元数据。
        /// 精确文件名和自动生成头会在删除前共同确认所有权。
        /// </summary>
        private static bool DeleteOwnedGeneratedFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            EnsureOwnedGeneratedFile(filePath);
            File.Delete(filePath);

            string metaPath = $"{filePath}.meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }

            return true;
        }

        private static void EnsureOwnedGeneratedFileIfExists(string filePath)
        {
            if (File.Exists(filePath))
            {
                EnsureOwnedGeneratedFile(filePath);
            }
        }

        private static void EnsureOwnedGeneratedFile(string filePath)
        {
            string content = File.ReadAllText(filePath, Encoding.UTF8);
            if (!content.StartsWith(
                    "// <auto-generated>",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"文件“{filePath}”不包含自动生成标记，已停止覆盖或删除。" + "请修改输出目录，或手动确认该同名文件的所有权。");
            }
        }

        private static string NormalizeAssetFolderPath(
            string assetFolderPath,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(assetFolderPath))
            {
                throw new ArgumentException("SO 脚本输出目录不能为空。", parameterName);
            }

            string normalized = assetFolderPath.Replace('\\', '/').TrimEnd('/');
            if (!string.Equals(
                    normalized,
                    "Assets",
                    StringComparison.Ordinal) &&
                !normalized.StartsWith(
                    "Assets/",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException($"SO 脚本输出目录必须位于 Assets 下：{assetFolderPath}", parameterName);
            }

            foreach (string segment in normalized.Split('/'))
            {
                if (string.Equals(segment, ".", StringComparison.Ordinal) ||
                    string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"SO 脚本输出目录不能包含相对路径段：{assetFolderPath}", parameterName);
                }
            }

            return normalized;
        }

        private static string NormalizeOptionalAssetFolderPath(
            string assetFolderPath)
        {
            return string.IsNullOrWhiteSpace(assetFolderPath)
                ? null
                : NormalizeAssetFolderPath(
                    assetFolderPath,
                    nameof(assetFolderPath));
        }

        private static string GetInitializer(ConfigFieldKind kind)
        {
            return kind switch
            {
                ConfigFieldKind.String => " = string.Empty",
                ConfigFieldKind.IntList => " = new()",
                ConfigFieldKind.FloatList => " = new()",
                ConfigFieldKind.BoolList => " = new()",
                ConfigFieldKind.StringList => " = new()",
                _ => string.Empty
            };
        }

        /// <summary>
        /// 将字段描述安全写入多行 XML summary。
        /// 空描述会回退为稳定的字段说明，避免生成空注释。
        /// </summary>
        private static void AppendXmlSummary(
            StringBuilder source,
            string description,
            string fieldName,
            string indentation)
        {
            string effectiveDescription = string.IsNullOrWhiteSpace(description) ? $"配置字段 {fieldName}。" : description;

            source.AppendLine($"{indentation}/// <summary>");
            foreach (string line in NormalizeLineBreaks(effectiveDescription).Split('\n'))
            {
                source.AppendLine(
                    $"{indentation}/// {EscapeXml(line)}");
            }

            source.AppendLine($"{indentation}/// </summary>");
        }

        private static string NormalizeLineBreaks(string value)
        {
            return value
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
        }

        private static string EscapeXml(string value)
        {
            return SecurityElement.Escape(value) ?? string.Empty;
        }

        private static string GetAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}
