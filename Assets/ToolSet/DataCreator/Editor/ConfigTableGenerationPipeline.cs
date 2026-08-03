using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameFramework.ConfigData.Editor
{
    /// <summary>
    /// 串联 CSV 解析、Schema 校验、代码生成和 SO 构建的编辑器流程。
    /// 当强类型代码发生变化时，会记录待续建状态并等待 Unity 完成编译。
    /// </summary>
    internal static class ConfigTableGenerationPipeline
    {
        internal const string PendingBuildSessionKey = "GameFramework.ConfigData.PendingAssetBuild";

        /// <summary>
        /// 使用当前项目设置处理 CSV 输出目录中的全部配置表。
        /// 代码发生变化时，本次调用只触发编译；编译后的域重载会自动再次调用并构建 SO。
        /// </summary>
        public static ConfigTableGenerationReport Run(
            ConfigTableToolSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.EnsureDefaults();

            string csvAssetFolder = AssetDatabase.GetAssetPath(
                settings.CSVOutputFolder);
            if (string.IsNullOrEmpty(csvAssetFolder) ||
                !AssetDatabase.IsValidFolder(csvAssetFolder))
            {
                throw new InvalidOperationException(
                    "CSV 输出目录无效，请在配置表工具窗口中重新选择。");
            }

            string csvDirectory = GetAbsoluteProjectPath(csvAssetFolder);
            string scriptAssetFolder = AssetDatabase.GetAssetPath(
                settings.SOScriptOutputFolder);
            ValidateAssetFolder(
                scriptAssetFolder,
                "SO 脚本输出目录");
            if (ContainsEditorFolder(scriptAssetFolder))
            {
                throw new InvalidOperationException(
                    "SO 脚本输出目录不能位于 Editor 文件夹中。" +
                    "生成的数据类和 SO 类型需要编译到运行时程序集。");
            }

            string soAssetFolder = AssetDatabase.GetAssetPath(
                settings.SOAssetOutputFolder);
            ValidateAssetFolder(
                soAssetFolder,
                "SO 资产输出目录");

            string[] csvFiles = Directory
                .EnumerateFiles(csvDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(
                    Path.GetExtension(path),
                    ".csv",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (csvFiles.Length == 0)
            {
                throw new InvalidOperationException(
                    $"CSV 目录“{csvAssetFolder}”中没有可处理的 .csv 文件。");
            }

            List<ConfigTableDiagnostic> diagnostics =
                new List<ConfigTableDiagnostic>();
            List<ConfigTableDefinition> definitions =
                ParseDefinitions(
                    csvFiles,
                    settings.DataStartRow,
                    settings.DataStartColumn,
                    diagnostics);

            ValidateUniqueTableNames(definitions, diagnostics);
            LogDiagnostics(diagnostics);

            if (diagnostics.Any(item =>
                    item.Severity == ConfigDiagnosticSeverity.Error))
            {
                SessionState.EraseBool(PendingBuildSessionKey);
                throw new ConfigTableValidationException(
                    "配置表校验失败，未生成代码或 SO。",
                    diagnostics);
            }

            bool scriptsChanged = ConfigTableCodeGenerator.Generate(
                definitions,
                scriptAssetFolder,
                settings.LastGeneratedScriptOutputFolderPath,
                out int scriptCount);
            settings.LastGeneratedScriptOutputFolderPath = scriptAssetFolder;
            settings.SaveSettings();

            ConfigTableGenerationReport report =
                new ConfigTableGenerationReport
                {
                    TableCount = definitions.Count,
                    DataRowCount = definitions.Sum(item => item.Rows.Count),
                    WarningCount = diagnostics.Count(item =>
                        item.Severity == ConfigDiagnosticSeverity.Warning),
                    GeneratedScriptCount = scriptCount,
                    ScriptsChanged = scriptsChanged,
                    ScriptOutputFolder = scriptAssetFolder,
                    SOAssetOutputFolder = soAssetFolder
                };

            if (scriptsChanged)
            {
                SessionState.SetBool(PendingBuildSessionKey, true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                return report;
            }

            report.AssetPaths = ConfigTableAssetBuilder.Build(
                definitions,
                soAssetFolder);
            report.AssetsBuilt = true;
            SessionState.EraseBool(PendingBuildSessionKey);
            RevealOutputFolder(soAssetFolder);
            return report;
        }

        /// <summary>
        /// 检查当前域重载是否需要自动续建 SO。
        /// 返回 false 表示没有待处理任务，调用方无需执行任何操作。
        /// </summary>
        public static bool TryRunPendingBuild(
            out ConfigTableGenerationReport report)
        {
            if (!SessionState.GetBool(PendingBuildSessionKey, false))
            {
                report = null;
                return false;
            }

            try
            {
                report = Run(ConfigTableToolSettings.instance);
                return true;
            }
            catch
            {
                SessionState.EraseBool(PendingBuildSessionKey);
                throw;
            }
        }

        private static List<ConfigTableDefinition> ParseDefinitions(
            IEnumerable<string> csvFiles,
            int dataStartRow,
            int dataStartColumn,
            ICollection<ConfigTableDiagnostic> diagnostics)
        {
            List<ConfigTableDefinition> definitions =
                new List<ConfigTableDefinition>();

            foreach (string csvFile in csvFiles)
            {
                try
                {
                    CsvDocument document = StandardCsvParser.ParseFile(csvFile);
                    ConfigTableDefinition definition =
                        ConfigTableSchemaParser.Parse(
                            document,
                            dataStartRow,
                            dataStartColumn,
                            diagnostics);

                    if (definition != null)
                    {
                        definitions.Add(definition);
                    }
                }
                catch (CsvParseException exception)
                {
                    diagnostics.Add(new ConfigTableDiagnostic(
                        ConfigDiagnosticSeverity.Error,
                        exception.SourcePath,
                        exception.Row,
                        exception.Column,
                        exception.Message));
                }
                catch (IOException exception)
                {
                    diagnostics.Add(new ConfigTableDiagnostic(
                        ConfigDiagnosticSeverity.Error,
                        csvFile,
                        0,
                        0,
                        $"读取 CSV 失败：{exception.Message}"));
                }
            }

            return definitions;
        }

        private static void ValidateUniqueTableNames(
            IReadOnlyList<ConfigTableDefinition> definitions,
            ICollection<ConfigTableDiagnostic> diagnostics)
        {
            foreach (IGrouping<string, ConfigTableDefinition> group in definitions
                         .GroupBy(
                             item => item.Schema.TableName,
                             StringComparer.OrdinalIgnoreCase)
                         .Where(item => item.Count() > 1))
            {
                foreach (ConfigTableDefinition definition in group)
                {
                    diagnostics.Add(new ConfigTableDiagnostic(
                        ConfigDiagnosticSeverity.Error,
                        definition.Schema.SourcePath,
                        0,
                        0,
                        $"配置表类型名“{group.Key}”与另一个 CSV 冲突。"));
                }
            }
        }

        /// <summary>
        /// 将全部诊断输出到 Unity Console。
        /// 警告不会阻断生成，错误会在聚合后统一终止流程。
        /// </summary>
        private static void LogDiagnostics(
            IEnumerable<ConfigTableDiagnostic> diagnostics)
        {
            foreach (ConfigTableDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == ConfigDiagnosticSeverity.Warning)
                {
                    Debug.LogWarning($"[配置表] {diagnostic}");
                }
                else
                {
                    Debug.LogError($"[配置表] {diagnostic}");
                }
            }
        }

        /// <summary>
        /// 校验窗口中配置的 Unity 文件夹，保证生成物只能写入 Assets。
        /// </summary>
        private static void ValidateAssetFolder(
            string assetFolder,
            string displayName)
        {
            if (string.IsNullOrEmpty(assetFolder) ||
                !AssetDatabase.IsValidFolder(assetFolder))
            {
                throw new InvalidOperationException(
                    $"{displayName}无效，请在配置表工具窗口中重新选择。");
            }

            if (!string.Equals(
                    assetFolder,
                    "Assets",
                    StringComparison.Ordinal) &&
                !assetFolder.StartsWith(
                    "Assets/",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{displayName}必须位于项目 Assets 目录下：{assetFolder}");
            }
        }

        /// <summary>
        /// 判断脚本目录是否包含 Unity 约定的 Editor 路径段。
        /// 放在该目录中的类型只能供编辑器使用，无法被运行时配置系统引用。
        /// </summary>
        private static bool ContainsEditorFolder(string assetFolder)
        {
            return assetFolder
                .Split('/')
                .Any(segment => string.Equals(
                    segment,
                    "Editor",
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 在配置表 SO 构建完成后聚焦 Project 窗口并定位实际输出目录。
        /// 这使即时构建和编译后自动续建都能给开发者明确的可见反馈。
        /// </summary>
        private static void RevealOutputFolder(string assetFolder)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            DefaultAsset folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(
                assetFolder);
            if (folder == null)
            {
                Debug.LogWarning(
                    $"[配置表] SO 已构建，但无法在 Project 窗口定位目录：{assetFolder}");
                return;
            }

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }

        private static string GetAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }

    /// <summary>
    /// Unity 完成脚本域重载后自动续接待处理的配置表 SO 构建。
    /// </summary>
    [InitializeOnLoad]
    internal static class ConfigTableGenerationContinuation
    {
        static ConfigTableGenerationContinuation()
        {
            if (SessionState.GetBool(ConfigTableGenerationPipeline.PendingBuildSessionKey, false))
            {
                EditorApplication.delayCall += TryResume;
            }
        }

        /// <summary>
        /// 等待 Unity 完成编译和资源刷新，再运行不改变代码的第二阶段资产构建。
        /// </summary>
        private static void TryResume()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryResume;
                return;
            }

            try
            {
                if (!ConfigTableGenerationPipeline.TryRunPendingBuild(
                        out ConfigTableGenerationReport report))
                {
                    return;
                }

                Debug.Log(
                    "[配置表自动续建完成] " +
                    report.ToDisplayMessage().Replace(
                        Environment.NewLine,
                        "，"));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "配置表自动续建失败",
                    exception.Message,
                    "确定");
            }
        }
    }
}
