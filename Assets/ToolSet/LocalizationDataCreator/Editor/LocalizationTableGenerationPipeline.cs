using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameFramework.LocalizationData.Editor
{
    /// <summary>
    /// 串联本地化 CSV 解析、SO 类型生成和按语言资产构建。
    /// </summary>
    internal static class LocalizationTableGenerationPipeline
    {
        internal const string PendingBuildSessionKey = "GameFramework.LocalizationData.PendingAssetBuild";

        public static LocalizationGenerationReport Run(LocalizationTableToolSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.EnsureDefaults();
            string csvAssetFolder = GetRequiredAssetFolder(settings.CSVOutputFolder, "CSV 输出目录");
            string scriptAssetFolder = GetRequiredAssetFolder(settings.SOScriptOutputFolder, "SO 脚本输出目录");
            string soAssetFolder = GetRequiredAssetFolder(settings.SOAssetOutputFolder, "SO 资产输出目录");
            if (ContainsEditorFolder(scriptAssetFolder))
            {
                throw new InvalidOperationException("SO 脚本输出目录不能位于 Editor 文件夹中。");
            }
            if (!ContainsResourcesFolder(soAssetFolder))
            {
                throw new InvalidOperationException("SO 资产输出目录必须位于 Resources 文件夹中，语言目录需要记录可供运行时加载的 Resources 相对路径。");
            }

            string csvDirectory = GetAbsoluteProjectPath(csvAssetFolder);
            string[] csvFiles = Directory.EnumerateFiles(csvDirectory, "*", SearchOption.TopDirectoryOnly).Where(path => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

            LocalizationDataSet dataSet;
            IReadOnlyList<LocalizationDiagnostic> diagnostics;
            try
            {
                dataSet = LocalizationTableParser.ParseFiles(csvFiles, settings.DataStartRow, settings.DataStartColumn, out diagnostics);
            }
            catch (LocalizationTableValidationException exception)
            {
                SessionState.EraseBool(PendingBuildSessionKey);
                LogDiagnostics(exception.Diagnostics);
                throw;
            }

            LogDiagnostics(diagnostics);
            bool scriptChanged = LocalizationDataCodeGenerator.Generate(scriptAssetFolder, settings.LastGeneratedScriptOutputFolderPath, dataSet.Languages, out _);
            settings.LastGeneratedScriptOutputFolderPath = scriptAssetFolder;
            settings.SaveSettings();

            LocalizationGenerationReport report = new() { TableCount = dataSet.TableCount, LanguageCount = dataSet.Languages.Count, KeyCount = dataSet.EntryCount, WarningCount = diagnostics.Count(item => item.Severity == LocalizationDiagnosticSeverity.Warning), ScriptChanged = scriptChanged, ScriptOutputFolder = scriptAssetFolder, SOAssetOutputFolder = soAssetFolder, CatalogAssetPath = soAssetFolder.TrimEnd('/') + "/" + LocalizationGenerationPaths.CatalogFileName };

            if (scriptChanged)
            {
                SessionState.SetBool(PendingBuildSessionKey, true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                return report;
            }

            report.AssetPaths = LocalizationDataAssetBuilder.Build(dataSet, soAssetFolder);
            report.AssetsBuilt = true;
            SessionState.EraseBool(PendingBuildSessionKey);
            RevealOutputFolder(soAssetFolder);
            return report;
        }

        public static bool TryRunPendingBuild(out LocalizationGenerationReport report)
        {
            if (!SessionState.GetBool(PendingBuildSessionKey, false))
            {
                report = null;
                return false;
            }

            try
            {
                report = Run(LocalizationTableToolSettings.instance);
                return true;
            }
            catch
            {
                SessionState.EraseBool(PendingBuildSessionKey);
                throw;
            }
        }

        private static string GetRequiredAssetFolder(DefaultAsset folder, string displayName)
        {
            string assetFolder = folder == null ? string.Empty : AssetDatabase.GetAssetPath(folder);
            if (string.IsNullOrEmpty(assetFolder) || !AssetDatabase.IsValidFolder(assetFolder))
            {
                throw new InvalidOperationException($"{displayName}无效，请在本地化配置表工具窗口中重新选择。");
            }

            if (assetFolder != "Assets" && !assetFolder.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{displayName}必须位于项目 Assets 目录下：{assetFolder}");
            }

            return assetFolder;
        }

        private static bool ContainsEditorFolder(string assetFolder)
        {
            return assetFolder.Split('/').Any(segment => string.Equals(segment, "Editor", StringComparison.OrdinalIgnoreCase));
        }

        private static bool ContainsResourcesFolder(string assetFolder)
        {
            return assetFolder.Split('/').Any(segment => string.Equals(segment, "Resources", StringComparison.OrdinalIgnoreCase));
        }

        private static void LogDiagnostics(IEnumerable<LocalizationDiagnostic> diagnostics)
        {
            foreach (LocalizationDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == LocalizationDiagnosticSeverity.Warning)
                {
                    Debug.LogWarning($"[本地化配置表] {diagnostic}");
                }
                else
                {
                    Debug.LogError($"[本地化配置表] {diagnostic}");
                }
            }
        }

        private static void RevealOutputFolder(string assetFolder)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            DefaultAsset folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetFolder);
            if (folder == null)
            {
                Debug.LogWarning($"[本地化配置表] SO 已构建，但无法定位目录：{assetFolder}");
                return;
            }

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }

        private static string GetAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }

    [InitializeOnLoad]
    internal static class LocalizationTableGenerationContinuation
    {
        static LocalizationTableGenerationContinuation()
        {
            if (SessionState.GetBool(LocalizationTableGenerationPipeline.PendingBuildSessionKey, false))
            {
                EditorApplication.delayCall += TryResume;
            }
        }

        private static void TryResume()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryResume;
                return;
            }

            try
            {
                if (LocalizationTableGenerationPipeline.TryRunPendingBuild(out LocalizationGenerationReport report))
                {
                    Debug.Log("[本地化配置表自动续建完成] " + report.ToDisplayMessage().Replace(Environment.NewLine, "，"));
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("本地化配置表自动续建失败", exception.Message, "确定");
            }
        }
    }
}
