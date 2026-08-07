using System;
using System.Collections.Generic;
using System.IO;

namespace GameFramework.ConfigData.Editor
{
    /// <summary>
    /// XLSX 导出 CSV 的事务式流程。
    /// 转换和校验均在临时目录完成，全部通过后才更新正式目录。
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
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("CSV 输出目录不能为空。", nameof(outputDirectory));
            }

            string fullOutputDirectory = Path.GetFullPath(outputDirectory);
            string temporaryDirectory = CreateTemporaryDirectory();

            try
            {
                XlsxToCsvExportReport temporaryReport = XlsxToCsvConverter.ConvertDirectory(inputDirectory, temporaryDirectory, searchOption);

                ConfigTableExportValidator.ValidateFiles(
                    temporaryReport.OutputFiles,
                    dataStartRow,
                    dataStartColumn);

                return PublishFiles(
                    temporaryReport,
                    fullOutputDirectory,
                    temporaryDirectory);
            }
            finally
            {
                TryDeleteTemporaryDirectory(temporaryDirectory);
            }
        }

        private static XlsxToCsvExportReport PublishFiles(
            XlsxToCsvExportReport temporaryReport,
            string outputDirectory,
            string temporaryDirectory)
        {
            Directory.CreateDirectory(outputDirectory);

            string backupDirectory = Path.Combine(temporaryDirectory, "PublishBackup");
            Directory.CreateDirectory(backupDirectory);

            List<PublishEntry> entries = new();
            foreach (string temporaryFile in temporaryReport.OutputFiles)
            {
                string fileName = Path.GetFileName(temporaryFile);
                string destinationPath = Path.GetFullPath(Path.Combine(outputDirectory, fileName));
                EnsureChildPath(outputDirectory, destinationPath);

                bool hadOriginalFile = File.Exists(destinationPath);
                string backupPath = Path.Combine(backupDirectory, fileName);
                if (hadOriginalFile)
                {
                    File.Copy(destinationPath, backupPath, overwrite: true);
                }

                entries.Add(new PublishEntry(
                    temporaryFile,
                    destinationPath,
                    backupPath,
                    hadOriginalFile));
            }

            try
            {
                foreach (PublishEntry entry in entries)
                {
                    if (FilesHaveEqualContent(
                            entry.TemporaryPath,
                            entry.DestinationPath))
                    {
                        continue;
                    }

                    // 复制过程中也可能发生部分写入，因此必须在写入前标记以便回滚。
                    entry.WasUpdated = true;
                    File.Copy(
                        entry.TemporaryPath,
                        entry.DestinationPath,
                        overwrite: true);
                }
            }
            catch (Exception publishException)
            {
                Exception rollbackException = TryRollback(entries);
                if (rollbackException != null)
                {
                    throw new IOException("更新正式 CSV 目录失败，并且回滚旧文件时发生错误。" + $"更新错误：{publishException.Message}；" + $"回滚错误：{rollbackException.Message}", publishException);
                }

                throw new IOException("更新正式 CSV 目录失败，原有 CSV 已回滚。", publishException);
            }

            XlsxToCsvExportReport publishedReport = new() { WorkbookCount = temporaryReport.WorkbookCount, WorksheetCount = temporaryReport.WorksheetCount };

            foreach (PublishEntry entry in entries)
            {
                publishedReport.AddOutputFile(entry.DestinationPath);
            }

            return publishedReport;
        }

        private static Exception TryRollback(
            IReadOnlyList<PublishEntry> entries)
        {
            Exception firstException = null;

            for (int index = entries.Count - 1; index >= 0; index--)
            {
                PublishEntry entry = entries[index];
                if (!entry.WasUpdated)
                {
                    continue;
                }

                try
                {
                    if (entry.HadOriginalFile)
                    {
                        File.Copy(
                            entry.BackupPath,
                            entry.DestinationPath,
                            overwrite: true);
                    }
                    else if (File.Exists(entry.DestinationPath))
                    {
                        File.Delete(entry.DestinationPath);
                    }
                }
                catch (Exception exception)
                {
                    firstException ??= exception;
                }
            }

            return firstException;
        }

        private static bool FilesHaveEqualContent(
            string firstPath,
            string secondPath)
        {
            if (!File.Exists(secondPath))
            {
                return false;
            }

            FileInfo firstInfo = new(firstPath);
            FileInfo secondInfo = new(secondPath);
            if (firstInfo.Length != secondInfo.Length)
            {
                return false;
            }

            const int bufferSize = 81920;
            byte[] firstBuffer = new byte[bufferSize];
            byte[] secondBuffer = new byte[bufferSize];

            using FileStream firstStream = File.OpenRead(firstPath);
            using FileStream secondStream = File.OpenRead(secondPath);

            while (true)
            {
                int firstRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length);
                int secondRead = secondStream.Read(secondBuffer, 0, secondBuffer.Length);

                if (firstRead != secondRead)
                {
                    return false;
                }

                if (firstRead == 0)
                {
                    return true;
                }

                for (int index = 0; index < firstRead; index++)
                {
                    if (firstBuffer[index] != secondBuffer[index])
                    {
                        return false;
                    }
                }
            }
        }

        private static string CreateTemporaryDirectory()
        {
            string temporaryRoot = GetTemporaryRoot();
            Directory.CreateDirectory(temporaryRoot);

            string temporaryDirectory = Path.GetFullPath(Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N")));
            EnsureChildPath(temporaryRoot, temporaryDirectory);
            Directory.CreateDirectory(temporaryDirectory);
            return temporaryDirectory;
        }

        private static void TryDeleteTemporaryDirectory(
            string temporaryDirectory)
        {
            try
            {
                string temporaryRoot = GetTemporaryRoot();
                string fullTemporaryDirectory = Path.GetFullPath(temporaryDirectory);
                EnsureChildPath(temporaryRoot, fullTemporaryDirectory);

                if (Directory.Exists(fullTemporaryDirectory))
                {
                    Directory.Delete(fullTemporaryDirectory, recursive: true);
                }
            }
            catch
            {
                // 临时目录清理失败不应覆盖原始的转换或校验异常。
            }
        }

        private static string GetTemporaryRoot()
        {
            return Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                TemporaryRootFolderName));
        }

        private static void EnsureChildPath(
            string parentDirectory,
            string childPath)
        {
            string fullParentDirectory = Path.GetFullPath(parentDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullChildPath = Path.GetFullPath(childPath);

            if (!fullChildPath.StartsWith(
                    fullParentDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"路径“{fullChildPath}”不在预期目录“{fullParentDirectory}”中。");
            }
        }

        private sealed class PublishEntry
        {
            public PublishEntry(
                string temporaryPath,
                string destinationPath,
                string backupPath,
                bool hadOriginalFile)
            {
                TemporaryPath = temporaryPath;
                DestinationPath = destinationPath;
                BackupPath = backupPath;
                HadOriginalFile = hadOriginalFile;
            }

            public string TemporaryPath { get; }

            public string DestinationPath { get; }

            public string BackupPath { get; }

            public bool HadOriginalFile { get; }

            public bool WasUpdated { get; set; }
        }
    }
}
