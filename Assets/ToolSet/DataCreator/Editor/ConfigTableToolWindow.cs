using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameFramework.ConfigData.Editor
{
    public sealed class ConfigTableExportWindow : EditorWindow
    {
        private const float WindowWidth = 560f;
        private const float WindowHeight = 480f;
        private const float ExportButtonHeight = 40f;
        private const float OpenFolderButtonWidth = 48f;
        private const float FolderFieldSpacing = 4f;

        private static readonly GUIContent LocateFolderButtonContent = new("定位", "在 Unity Project 窗口中显示该目录");

        private ConfigTableToolSettings settings;

        [MenuItem("工具集/配置表工具")]
        public static void Open()
        {
            ConfigTableExportWindow window = GetWindow<ConfigTableExportWindow>("配置表工具");

            window.minSize = new Vector2(WindowWidth, WindowHeight);
            window.Show();
        }

        private void OnEnable()
        {
            settings = ConfigTableToolSettings.instance;
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
                settings = ConfigTableToolSettings.instance;
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
            EditorGUILayout.LabelField("CSV 生成配置 SO", EditorStyles.boldLabel);
            EditorGUILayout.Space(5f);

            DefaultAsset soScriptOutputFolder = DrawFolderField("SO 脚本输出目录", settings.SOScriptOutputFolder);
            DrawAssetPath(soScriptOutputFolder);

            DefaultAsset soAssetOutputFolder = DrawFolderField("SO 资产输出目录", settings.SOAssetOutputFolder);
            DrawAssetPath(soAssetOutputFolder);

            EditorGUILayout.Space(5f);

            int dataStartRow = Mathf.Max(1, EditorGUILayout.IntField("数据开始行", settings.DataStartRow));
            int dataStartColumn = Mathf.Max(1, EditorGUILayout.IntField("数据开始列", settings.DataStartColumn));

            EditorGUILayout.HelpBox(
                "行列坐标从 1 开始，当前默认从第 6 行、第 2 列读取正式配置数据。" +
                "第一列仅作为行描述时不会写入 SO。\n" +
                "#check 支持 NonEmpty 和 Unique，多个标签可用逗号、空格、分号或 | 分隔。\n" +
                "SO 脚本目录用于生成配置数据行类和 XXXSO.cs，不能选择 Editor 目录；" +
                "SO 资产目录用于生成 XXXSO.asset。\n" +
                "ConfigDatabase 固定生成到 Assets/Resources/ConfigData，" +
                "并引用 SO 资产目录中的配置表资产。",
                MessageType.Info);

            if (inputFolder != settings.XLSXInputFolder ||
                csvOutputFolder != settings.CSVOutputFolder ||
                soScriptOutputFolder != settings.SOScriptOutputFolder ||
                soAssetOutputFolder != settings.SOAssetOutputFolder ||
                dataStartRow != settings.DataStartRow ||
                dataStartColumn != settings.DataStartColumn)
            {
                settings.XLSXInputFolder = inputFolder;
                settings.CSVOutputFolder = csvOutputFolder;
                settings.SOScriptOutputFolder = soScriptOutputFolder;
                settings.SOAssetOutputFolder = soAssetOutputFolder;
                settings.DataStartRow = dataStartRow;
                settings.DataStartColumn = dataStartColumn;
                settings.SaveSettings();
            }

            GUILayout.FlexibleSpace();

            bool canExport = IsValidFolder(settings.XLSXInputFolder) && IsValidFolder(settings.CSVOutputFolder);

            using (new EditorGUI.DisabledScope(!canExport))
            {
                if (GUILayout.Button(
                        "导出XLSX为CSV",
                        GUILayout.Height(ExportButtonHeight)))
                {
                    ExportXLSXToCSV();
                }
            }

            EditorGUILayout.Space(5f);

            using (new EditorGUI.DisabledScope(
                       !IsValidFolder(settings.CSVOutputFolder) ||
                       !IsValidFolder(settings.SOScriptOutputFolder) ||
                       !IsValidFolder(settings.SOAssetOutputFolder) ||
                       EditorApplication.isCompiling))
            {
                if (GUILayout.Button(
                        "生成CSV配置SO",
                        GUILayout.Height(ExportButtonHeight)))
                {
                    GenerateCSVConfigSO();
                }
            }

            EditorGUILayout.Space(10f);
        }

        /// <summary>
        /// 绘制“Project 定位按钮 + Unity 文件夹选择框”。
        /// 定位按钮会聚焦 Project 窗口，并选中、闪烁提示对应目录。
        /// </summary>
        private DefaultAsset DrawFolderField(string label, DefaultAsset currentFolder)
        {
            DefaultAsset selectedFolder;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);

                using (new EditorGUI.DisabledScope(!IsValidFolder(currentFolder)))
                {
                    if (GUILayout.Button(
                            LocateFolderButtonContent,
                            EditorStyles.miniButton,
                            GUILayout.Width(OpenFolderButtonWidth),
                            GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                    {
                        LocateFolderInProjectWindow(currentFolder);
                    }
                }

                GUILayout.Space(FolderFieldSpacing);
                selectedFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    currentFolder,
                    typeof(DefaultAsset),
                    allowSceneObjects: false,
                    GUILayout.ExpandWidth(true));
            }

            if (selectedFolder == null || selectedFolder == currentFolder)
            {
                return selectedFolder;
            }

            if (IsValidFolder(selectedFolder))
            {
                return selectedFolder;
            }

            ShowNotification(new GUIContent("请选择项目内的文件夹资源"));
            return currentFolder;
        }

        /// <summary>
        /// 绘制指定文件夹的路径标签。
        /// </summary>
        private static void DrawAssetPath(DefaultAsset folder)
        {
            string path = folder == null ? "未选择" : AssetDatabase.GetAssetPath(folder);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(
                    EditorGUIUtility.labelWidth +
                    OpenFolderButtonWidth +
                    FolderFieldSpacing);
                EditorGUILayout.LabelField(
                    path,
                    EditorStyles.miniLabel);
            }
        }

        /// <summary>
        /// 在 Unity Project 窗口中定位指定文件夹资源。
        /// </summary>
        private void LocateFolderInProjectWindow(DefaultAsset folder)
        {
            if (!IsValidFolder(folder))
            {
                ShowNotification(new GUIContent("目录无效，无法定位"));
                return;
            }

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }

        /// <summary>
        /// 检查指定的文件夹是否有效。
        /// </summary>
        private static bool IsValidFolder(DefaultAsset folder)
        {
            return folder != null &&
                   AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(folder));
        }

        private void ExportXLSXToCSV()
        {
            settings.SaveSettings();

            string inputAssetPath = AssetDatabase.GetAssetPath(settings.XLSXInputFolder);
            string outputAssetPath = AssetDatabase.GetAssetPath(settings.CSVOutputFolder);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string inputDirectory = Path.GetFullPath(Path.Combine(projectRoot, inputAssetPath));
            string outputDirectory = Path.GetFullPath(Path.Combine(projectRoot, outputAssetPath));

            try
            {
                XlsxToCsvExportReport report = ConfigTableExportPipeline.ExportDirectory(inputDirectory, outputDirectory, settings.DataStartRow, settings.DataStartColumn);

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                string message = $"工作簿：{report.WorkbookCount}\n" + $"CSV：{report.WorksheetCount}\n" + $"输出目录：{outputAssetPath}";

                Debug.Log($"[配置表导出完成] {message.Replace(Environment.NewLine, "，")}");
                EditorUtility.DisplayDialog("配置表导出完成", message, "确定");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "配置表导出失败",
                    exception.Message,
                    "确定");
            }
        }

        /// <summary>
        /// 从当前 CSV 目录执行 Schema 校验、强类型代码生成和 SO 构建。
        /// 当生成代码有变化时，SO 阶段会在 Unity 编译完成后自动续接。
        /// </summary>
        private void GenerateCSVConfigSO()
        {
            settings.SaveSettings();

            try
            {
                ConfigTableGenerationReport report = ConfigTableGenerationPipeline.Run(settings);
                string message = report.ToDisplayMessage();

                Debug.Log(
                    $"[配置表生成] {message.Replace(Environment.NewLine, "，")}");
                EditorUtility.DisplayDialog(
                    report.AssetsBuilt ? "配置表生成完成" : "配置表代码已更新",
                    message,
                    "确定");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "配置表生成失败",
                    exception.Message,
                    "确定");
            }
        }
    }
}
