using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace GameFramework.ConfigData.Editor
{
    /// <summary>
    /// 配置表工具的项目级配置。
    /// </summary>
    [FilePath("ProjectSettings/ConfigTableToolSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class ConfigTableToolSettings : ScriptableSingleton<ConfigTableToolSettings>
    {
        public const string DefaultXLSXInputFolderPath = "Assets/ConfigTables/XLSX";
        public const string DefaultCSVOutputFolderPath = "Assets/ConfigTables/CSV";
        public const string DefaultSOScriptOutputFolderPath = ConfigTableGenerationPaths.DefaultGeneratedScriptFolder;
        public const string DefaultSOAssetOutputFolderPath = ConfigTableGenerationPaths.DefaultTableAssetFolder;
        public const int DefaultDataStartRow = 6;
        public const int DefaultDataStartColumn = 2;

        [SerializeField]
        private DefaultAsset xlsxInputFolder;

        [SerializeField]
        private DefaultAsset csvOutputFolder;

        [SerializeField]
        private DefaultAsset soScriptOutputFolder;

        [FormerlySerializedAs("soOutputFolder")]
        [SerializeField]
        private DefaultAsset soAssetOutputFolder;

        [SerializeField]
        private string lastGeneratedScriptOutputFolderPath = DefaultSOScriptOutputFolderPath;

        [SerializeField]
        private int dataStartRow = DefaultDataStartRow;

        [SerializeField]
        private int dataStartColumn = DefaultDataStartColumn;

        public DefaultAsset XLSXInputFolder
        {
            get => xlsxInputFolder;
            set => xlsxInputFolder = value;
        }

        public DefaultAsset CSVOutputFolder
        {
            get => csvOutputFolder;
            set => csvOutputFolder = value;
        }

        /// <summary>
        /// 获取或设置配置表数据类及具体 SO 类型脚本的输出目录。
        /// 生成脚本属于运行时代码，因此不能放入 Editor 目录。
        /// </summary>
        public DefaultAsset SOScriptOutputFolder
        {
            get => soScriptOutputFolder;
            set => soScriptOutputFolder = value;
        }

        /// <summary>
        /// 获取或设置生成的配置表 SO 资产输出目录。
        /// ConfigDatabase 仍保存在固定 Resources 路径，运行时通过它引用这些表资产。
        /// </summary>
        public DefaultAsset SOAssetOutputFolder
        {
            get => soAssetOutputFolder;
            set => soAssetOutputFolder = value;
        }

        /// <summary>
        /// 记录上一次实际生成脚本的目录。
        /// 当开发者切换输出位置时，生成器据此迁移旧脚本并保留 Unity GUID。
        /// </summary>
        internal string LastGeneratedScriptOutputFolderPath
        {
            get => lastGeneratedScriptOutputFolderPath;
            set => lastGeneratedScriptOutputFolderPath = value;
        }

        /// <summary>
        /// 获取或设置配置数据开始读取的逻辑记录号，使用 1 基坐标。
        /// </summary>
        public int DataStartRow
        {
            get => dataStartRow;
            set => dataStartRow = Mathf.Max(1, value);
        }

        /// <summary>
        /// 获取或设置配置字段开始读取的列号，使用 1 基坐标。
        /// </summary>
        public int DataStartColumn
        {
            get => dataStartColumn;
            set => dataStartColumn = Mathf.Max(1, value);
        }

        /// <summary>
        /// 补齐首次创建或版本升级后缺失的项目级默认设置。
        /// </summary>
        public void EnsureDefaults()
        {
            bool changed = false;

            if (xlsxInputFolder == null)
            {
                xlsxInputFolder =
                    AssetDatabase.LoadAssetAtPath<DefaultAsset>(DefaultXLSXInputFolderPath);
                changed = xlsxInputFolder != null;
            }

            if (csvOutputFolder == null)
            {
                csvOutputFolder =
                    AssetDatabase.LoadAssetAtPath<DefaultAsset>(DefaultCSVOutputFolderPath);
                changed |= csvOutputFolder != null;
            }

            if (soScriptOutputFolder == null)
            {
                ConfigTableAssetBuilder.EnsureAssetFolder(
                    DefaultSOScriptOutputFolderPath);
                soScriptOutputFolder =
                    AssetDatabase.LoadAssetAtPath<DefaultAsset>(
                        DefaultSOScriptOutputFolderPath);
                changed |= soScriptOutputFolder != null;
            }

            if (soAssetOutputFolder == null)
            {
                ConfigTableAssetBuilder.EnsureAssetFolder(
                    DefaultSOAssetOutputFolderPath);
                soAssetOutputFolder =
                    AssetDatabase.LoadAssetAtPath<DefaultAsset>(
                        DefaultSOAssetOutputFolderPath);
                changed |= soAssetOutputFolder != null;
            }

            if (string.IsNullOrWhiteSpace(lastGeneratedScriptOutputFolderPath))
            {
                lastGeneratedScriptOutputFolderPath =
                    DefaultSOScriptOutputFolderPath;
                changed = true;
            }

            if (dataStartRow < 1)
            {
                dataStartRow = DefaultDataStartRow;
                changed = true;
            }

            if (dataStartColumn < 1)
            {
                dataStartColumn = DefaultDataStartColumn;
                changed = true;
            }

            if (changed)
            {
                SaveSettings();
            }
        }

        /// <summary>
        /// 将当前工具设置保存到 ProjectSettings，供团队共享。
        /// </summary>
        public void SaveSettings()
        {
            Save(saveAsText: true);
        }
    }
}
