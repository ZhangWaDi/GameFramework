using System;
using System.IO;
using GameFramework.DataTools.Editor;
using UnityEditor;
using UnityEngine;

namespace GameFramework.LocalizationData.Editor
{
    /// <summary>
    /// 本地化 XLSX、CSV 和分语言 SO 的统一编辑器入口。
    /// </summary>
    public sealed class LocalizationTableToolWindow : EditorWindow
    {
        private const float WindowWidth = 560f;
        private const float WindowHeight = 480f;
        private const float ButtonHeight = 40f;
        private const float LocateButtonWidth = 48f;
        private const float FolderFieldSpacing = 4f;

        private static readonly GUIContent LocateFolderButtonContent = new("定位", "在 Unity Project 窗口中显示该目录");
        private LocalizationTableToolSettings settings;

        [MenuItem("工具集/本地化配置表工具")]
        public static void Open()
        {
            LocalizationTableToolWindow window = GetWindow<LocalizationTableToolWindow>("本地化配置表工具");
            window.minSize = new(WindowWidth, WindowHeight);
            window.Show();
        }

        private void OnEnable()
        {
            settings = LocalizationTableToolSettings.instance;
            settings.EnsureDefaults();
        }

        private void OnDisable()
        {
            settings?.SaveSettings();
        }

        private void OnGUI()
        {
            if (settings == null)
            {
                settings = LocalizationTableToolSettings.instance;
                settings.EnsureDefaults();
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("XLSX 转 CSV", EditorStyles.boldLabel);
            EditorGUILayout.Space(5f);

            DefaultAsset inputFolder = DrawFolderField("XLSX 输入目录", settings.XLSXInputFolder);
            DrawAssetPath(inputFolder);
            DefaultAsset csvOutputFolder = DrawFolderField("CSV 输出目录", settings.CSVOutputFolder);
            DrawAssetPath(csvOutputFolder);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("CSV 生成本地化 SO", EditorStyles.boldLabel);
            EditorGUILayout.Space(5f);

            DefaultAsset scriptOutputFolder = DrawFolderField("SO 脚本输出目录", settings.SOScriptOutputFolder);
            DrawAssetPath(scriptOutputFolder);
            DefaultAsset assetOutputFolder = DrawFolderField("SO 资产输出目录", settings.SOAssetOutputFolder);
            DrawAssetPath(assetOutputFolder);

            EditorGUILayout.Space(5f);
            int dataStartRow = Mathf.Max(1, EditorGUILayout.IntField("数据开始行", settings.DataStartRow));
            int dataStartColumn = Mathf.Max(1, EditorGUILayout.IntField("数据开始列", settings.DataStartColumn));

            EditorGUILayout.HelpBox(
                "行列坐标从 1 开始，默认从第 6 行、第 2 列读取正式数据。第一导出列必须为 Key，后续每一列代表一种语言。\n" +
                "所有 CSV 的语言列必须保持相同名称和顺序；每份 CSV 会保留为独立分表，Key 只要求在当前表内唯一。\n" +
                "语言列名同时会生成为 LocalizationLanguage 枚举；SO 和语言目录仍保存稳定的字符串语言 ID。\n" +
                "工具为每种语言生成一份包含多个分表的 LocalizationDataSO，并额外生成只记录 Resources 路径字符串的语言目录。",
                MessageType.Info);

            if (inputFolder != settings.XLSXInputFolder || csvOutputFolder != settings.CSVOutputFolder || scriptOutputFolder != settings.SOScriptOutputFolder || assetOutputFolder != settings.SOAssetOutputFolder || dataStartRow != settings.DataStartRow || dataStartColumn != settings.DataStartColumn)
            {
                settings.XLSXInputFolder = inputFolder;
                settings.CSVOutputFolder = csvOutputFolder;
                settings.SOScriptOutputFolder = scriptOutputFolder;
                settings.SOAssetOutputFolder = assetOutputFolder;
                settings.DataStartRow = dataStartRow;
                settings.DataStartColumn = dataStartColumn;
                settings.SaveSettings();
            }

            GUILayout.FlexibleSpace();

            bool canExport = IsValidFolder(settings.XLSXInputFolder) && IsValidFolder(settings.CSVOutputFolder);
            using (new EditorGUI.DisabledScope(!canExport || EditorApplication.isCompiling))
            {
                if (GUILayout.Button("导出XLSX为CSV", GUILayout.Height(ButtonHeight)))
                {
                    ExportXLSXToCSV();
                }
            }

            EditorGUILayout.Space(5f);
            bool canGenerate = IsValidFolder(settings.CSVOutputFolder) && IsValidFolder(settings.SOScriptOutputFolder) && IsValidFolder(settings.SOAssetOutputFolder);
            using (new EditorGUI.DisabledScope(!canGenerate || EditorApplication.isCompiling))
            {
                if (GUILayout.Button("生成CSV本地化SO", GUILayout.Height(ButtonHeight)))
                {
                    GenerateLocalizationSO();
                }
            }

            EditorGUILayout.Space(10f);
        }

        private DefaultAsset DrawFolderField(string label, DefaultAsset currentFolder)
        {
            DefaultAsset selectedFolder;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                using (new EditorGUI.DisabledScope(!IsValidFolder(currentFolder)))
                {
                    if (GUILayout.Button(LocateFolderButtonContent, EditorStyles.miniButton, GUILayout.Width(LocateButtonWidth), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                    {
                        LocateFolder(currentFolder);
                    }
                }

                GUILayout.Space(FolderFieldSpacing);
                selectedFolder = (DefaultAsset)EditorGUILayout.ObjectField(currentFolder, typeof(DefaultAsset), allowSceneObjects: false, GUILayout.ExpandWidth(true));
            }

            if (selectedFolder == null || selectedFolder == currentFolder || IsValidFolder(selectedFolder))
            {
                return selectedFolder;
            }

            ShowNotification(new GUIContent("请选择项目内的文件夹资源"));
            return currentFolder;
        }

        private static void DrawAssetPath(DefaultAsset folder)
        {
            string path = folder == null ? "未选择" : AssetDatabase.GetAssetPath(folder);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth + LocateButtonWidth + FolderFieldSpacing);
                EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
            }
        }

        private static void LocateFolder(DefaultAsset folder)
        {
            if (!IsValidFolder(folder))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = folder;
                EditorGUIUtility.PingObject(folder);
            };
        }

        private static bool IsValidFolder(DefaultAsset folder)
        {
            return folder != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(folder));
        }

        private void ExportXLSXToCSV()
        {
            settings.SaveSettings();
            string inputAssetPath = AssetDatabase.GetAssetPath(settings.XLSXInputFolder);
            string outputAssetPath = AssetDatabase.GetAssetPath(settings.CSVOutputFolder);
            string inputDirectory = GetAbsoluteProjectPath(inputAssetPath);
            string outputDirectory = GetAbsoluteProjectPath(outputAssetPath);

            try
            {
                XlsxToCsvExportReport report = LocalizationTableExportPipeline.ExportDirectory(inputDirectory, outputDirectory, settings.DataStartRow, settings.DataStartColumn);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                string message = $"工作簿：{report.WorkbookCount}\nCSV：{report.WorksheetCount}\n输出目录：{outputAssetPath}";
                Debug.Log($"[本地化配置表导出完成] {message.Replace(Environment.NewLine, "，")}");
                EditorUtility.DisplayDialog("本地化配置表导出完成", message, "确定");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("本地化配置表导出失败", exception.Message, "确定");
            }
        }

        private void GenerateLocalizationSO()
        {
            settings.SaveSettings();
            try
            {
                LocalizationGenerationReport report = LocalizationTableGenerationPipeline.Run(settings);
                string message = report.ToDisplayMessage();
                Debug.Log($"[本地化配置表生成] {message.Replace(Environment.NewLine, "，")}");
                EditorUtility.DisplayDialog(report.AssetsBuilt ? "本地化 SO 生成完成" : "本地化生成脚本已更新", message, "确定");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("本地化 SO 生成失败", exception.Message, "确定");
            }
        }

        private static string GetAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}
