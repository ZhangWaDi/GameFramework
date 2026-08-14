using System.Collections.Generic;
using System.IO;
using GameFramework.DataTools.Editor;

namespace GameFramework.LocalizationData.Editor
{
    /// <summary>
    /// 本地化配置表 XLSX 导出包装，在通用事务流程上追加本地化结构校验。
    /// </summary>
    internal static class LocalizationTableExportPipeline
    {
        private const string TemporaryRootFolderName = "GameFrameworkLocalizationTableExport";

        public static XlsxToCsvExportReport ExportDirectory(string inputDirectory, string outputDirectory, int dataStartRow, int dataStartColumn, SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            return XlsxCsvExportPipeline.ExportDirectory(
                inputDirectory,
                outputDirectory,
                TemporaryRootFolderName,
                files => ValidateFiles(files, dataStartRow, dataStartColumn),
                searchOption);
        }

        private static void ValidateFiles(IReadOnlyList<string> files, int dataStartRow, int dataStartColumn)
        {
            LocalizationTableParser.ParseFiles(files, dataStartRow, dataStartColumn, out _);
        }
    }
}
