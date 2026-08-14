using System.IO;
using GameFramework.DataTools.Editor;

namespace GameFramework.ConfigData.Editor
{
    /// <summary>
    /// 普通配置表 XLSX 导出包装，在通用事务流程上追加配置表校验。
    /// </summary>
    internal static class ConfigTableExportPipeline
    {
        private const string TemporaryRootFolderName = "GameFrameworkConfigTableExport";

        public static XlsxToCsvExportReport ExportDirectory(
            string inputDirectory,
            string outputDirectory,
            int dataStartRow,
            int dataStartColumn,
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            return XlsxCsvExportPipeline.ExportDirectory(
                inputDirectory,
                outputDirectory,
                TemporaryRootFolderName,
                files => ConfigTableExportValidator.ValidateFiles(files, dataStartRow, dataStartColumn),
                searchOption);
        }
    }
}
