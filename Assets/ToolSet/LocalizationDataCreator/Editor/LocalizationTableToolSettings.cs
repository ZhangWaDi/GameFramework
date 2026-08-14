using UnityEditor;
using UnityEngine;

namespace GameFramework.LocalizationData.Editor
{
    /// <summary>
    /// 本地化配置表工具的项目级设置。
    /// </summary>
    [FilePath("ProjectSettings/LocalizationTableToolSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class LocalizationTableToolSettings : ScriptableSingleton<LocalizationTableToolSettings>
    {
        public const string DefaultXLSXInputFolderPath = "Assets/ConfigTables/LocalizationXLSX";
        public const string DefaultCSVOutputFolderPath = "Assets/ConfigTables/LocalizationCSV";
        public const int DefaultDataStartRow = 6;
        public const int DefaultDataStartColumn = 2;

        [SerializeField] private DefaultAsset xlsxInputFolder;
        [SerializeField] private DefaultAsset csvOutputFolder;
        [SerializeField] private DefaultAsset soScriptOutputFolder;
        [SerializeField] private DefaultAsset soAssetOutputFolder;
        [SerializeField] private string lastGeneratedScriptOutputFolderPath = LocalizationGenerationPaths.DefaultGeneratedScriptFolder;
        [SerializeField] private int dataStartRow = DefaultDataStartRow;
        [SerializeField] private int dataStartColumn = DefaultDataStartColumn;

        public DefaultAsset XLSXInputFolder { get => xlsxInputFolder; set => xlsxInputFolder = value; }
        public DefaultAsset CSVOutputFolder { get => csvOutputFolder; set => csvOutputFolder = value; }
        public DefaultAsset SOScriptOutputFolder { get => soScriptOutputFolder; set => soScriptOutputFolder = value; }
        public DefaultAsset SOAssetOutputFolder { get => soAssetOutputFolder; set => soAssetOutputFolder = value; }
        public int DataStartRow { get => dataStartRow; set => dataStartRow = Mathf.Max(1, value); }
        public int DataStartColumn { get => dataStartColumn; set => dataStartColumn = Mathf.Max(1, value); }

        internal string LastGeneratedScriptOutputFolderPath
        {
            get => lastGeneratedScriptOutputFolderPath;
            set => lastGeneratedScriptOutputFolderPath = value;
        }

        public void EnsureDefaults()
        {
            bool changed = false;
            changed |= EnsureFolderReference(ref xlsxInputFolder, DefaultXLSXInputFolderPath);
            changed |= EnsureFolderReference(ref csvOutputFolder, DefaultCSVOutputFolderPath);
            changed |= EnsureFolderReference(ref soScriptOutputFolder, LocalizationGenerationPaths.DefaultGeneratedScriptFolder);
            changed |= EnsureFolderReference(ref soAssetOutputFolder, LocalizationGenerationPaths.DefaultAssetFolder);

            if (string.IsNullOrWhiteSpace(lastGeneratedScriptOutputFolderPath))
            {
                lastGeneratedScriptOutputFolderPath = LocalizationGenerationPaths.DefaultGeneratedScriptFolder;
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

        public void SaveSettings()
        {
            Save(saveAsText: true);
        }

        private static bool EnsureFolderReference(ref DefaultAsset folder, string assetPath)
        {
            if (folder != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(folder)))
            {
                return false;
            }

            EnsureAssetFolder(assetPath);
            folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetPath);
            return folder != null;
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            string[] segments = assetPath.Split('/');
            if (segments.Length == 0 || segments[0] != "Assets")
            {
                throw new System.ArgumentException($"目录必须位于 Assets 下：{assetPath}", nameof(assetPath));
            }

            string currentPath = "Assets";
            for (int index = 1; index < segments.Length; index++)
            {
                string nextPath = currentPath + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, segments[index]);
                }

                currentPath = nextPath;
            }
        }
    }
}
