using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameFramework.LocalizationData.Editor
{
    /// <summary>
    /// 将本地化数据按语言拆分为独立 SO，并生成不直接引用语言资产的轻量目录。
    /// </summary>
    internal static class LocalizationDataAssetBuilder
    {
        private const BindingFlags InstanceFieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        public static IReadOnlyList<string> Build(LocalizationDataSet dataSet, string assetFolder)
        {
            if (dataSet == null)
            {
                throw new ArgumentNullException(nameof(dataSet));
            }

            if (string.IsNullOrWhiteSpace(assetFolder) || !AssetDatabase.IsValidFolder(assetFolder))
            {
                throw new ArgumentException("本地化 SO 资产输出目录无效。", nameof(assetFolder));
            }

            Type dataType = GetRequiredLoadedType(LocalizationGenerationPaths.GeneratedTypeFullName);
            Type sectionType = GetRequiredLoadedType(LocalizationGenerationPaths.GeneratedSectionTypeFullName);
            Type entryType = GetRequiredLoadedType(LocalizationGenerationPaths.GeneratedEntryTypeFullName);
            Type catalogType = GetRequiredLoadedType(LocalizationGenerationPaths.GeneratedCatalogTypeFullName);
            Type packageReferenceType = GetRequiredLoadedType(LocalizationGenerationPaths.GeneratedPackageReferenceTypeFullName);
            ValidateScriptableObjectType(dataType);
            ValidateScriptableObjectType(catalogType);

            FieldInfo dataGeneratorIdField = GetRequiredField(dataType, "generatorId");
            FieldInfo languageField = GetRequiredField(dataType, "language");
            FieldInfo sectionsField = GetRequiredField(dataType, "sections");
            FieldInfo tableIdField = GetRequiredField(sectionType, "tableId");
            FieldInfo sectionEntriesField = GetRequiredField(sectionType, "entries");
            FieldInfo keyField = GetRequiredField(entryType, "key");
            FieldInfo valueField = GetRequiredField(entryType, "value");
            FieldInfo catalogGeneratorIdField = GetRequiredField(catalogType, "generatorId");
            FieldInfo defaultLanguageField = GetRequiredField(catalogType, "defaultLanguage");
            FieldInfo packagesField = GetRequiredField(catalogType, "packages");
            FieldInfo packageLanguageField = GetRequiredField(packageReferenceType, "language");
            FieldInfo resourcesPathField = GetRequiredField(packageReferenceType, "resourcesPath");

            List<PreparedLanguageAsset> preparedLanguages = PrepareLanguageAssets(dataSet, assetFolder, sectionType, entryType, tableIdField, sectionEntriesField, keyField, valueField);
            PreparedCatalogAsset preparedCatalog = PrepareCatalogAsset(dataSet, assetFolder, packageReferenceType, preparedLanguages, packageLanguageField, resourcesPathField);
            List<LanguageAssetBackup> languageBackups = new();
            CatalogAssetBackup catalogBackup = null;
            List<string> createdPaths = new();

            try
            {
                foreach (PreparedLanguageAsset prepared in preparedLanguages)
                {
                    ScriptableObject asset = GetOrCreateAsset(prepared.AssetPath, dataType, prepared.Language + "LocalizationData", createdPaths);
                    if (!createdPaths.Contains(prepared.AssetPath, StringComparer.OrdinalIgnoreCase))
                    {
                        languageBackups.Add(new(asset, languageField.GetValue(asset), sectionsField.GetValue(asset), dataGeneratorIdField.GetValue(asset)));
                    }

                    dataGeneratorIdField.SetValue(asset, LocalizationGenerationPaths.GeneratorId);
                    languageField.SetValue(asset, prepared.Language);
                    sectionsField.SetValue(asset, prepared.Sections);
                    EditorUtility.SetDirty(asset);
                }

                ScriptableObject catalog = GetOrCreateAsset(preparedCatalog.AssetPath, catalogType, "LocalizationCatalog", createdPaths);
                if (!createdPaths.Contains(preparedCatalog.AssetPath, StringComparer.OrdinalIgnoreCase))
                {
                    catalogBackup = new(catalog, defaultLanguageField.GetValue(catalog), packagesField.GetValue(catalog), catalogGeneratorIdField.GetValue(catalog));
                }

                catalogGeneratorIdField.SetValue(catalog, LocalizationGenerationPaths.GeneratorId);
                defaultLanguageField.SetValue(catalog, preparedCatalog.DefaultLanguage);
                packagesField.SetValue(catalog, preparedCatalog.Packages);
                EditorUtility.SetDirty(catalog);

                AssetDatabase.SaveAssets();
                DeleteStaleOwnedLanguageAssets(assetFolder, dataType, dataGeneratorIdField, preparedLanguages.Select(item => item.AssetPath));
                AssetDatabase.SaveAssets();
            }
            catch
            {
                foreach (LanguageAssetBackup backup in languageBackups)
                {
                    languageField.SetValue(backup.Asset, backup.Language);
                    sectionsField.SetValue(backup.Asset, backup.Sections);
                    dataGeneratorIdField.SetValue(backup.Asset, backup.GeneratorId);
                    EditorUtility.SetDirty(backup.Asset);
                }

                if (catalogBackup != null)
                {
                    defaultLanguageField.SetValue(catalogBackup.Asset, catalogBackup.DefaultLanguage);
                    packagesField.SetValue(catalogBackup.Asset, catalogBackup.Packages);
                    catalogGeneratorIdField.SetValue(catalogBackup.Asset, catalogBackup.GeneratorId);
                    EditorUtility.SetDirty(catalogBackup.Asset);
                }

                for (int index = createdPaths.Count - 1; index >= 0; index--)
                {
                    AssetDatabase.DeleteAsset(createdPaths[index]);
                }

                AssetDatabase.SaveAssets();
                throw;
            }

            return preparedLanguages.Select(item => item.AssetPath).Append(preparedCatalog.AssetPath).ToArray();
        }

        private static List<PreparedLanguageAsset> PrepareLanguageAssets(LocalizationDataSet dataSet, string assetFolder, Type sectionType, Type entryType, FieldInfo tableIdField, FieldInfo sectionEntriesField, FieldInfo keyField, FieldInfo valueField)
        {
            Type sectionListType = typeof(List<>).MakeGenericType(sectionType);
            Type entryListType = typeof(List<>).MakeGenericType(entryType);
            List<PreparedLanguageAsset> result = new();
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

            foreach (string language in dataSet.Languages)
            {
                IList sections = (IList)Activator.CreateInstance(sectionListType);
                foreach (LocalizationTableDefinition table in dataSet.Tables)
                {
                    IList entries = (IList)Activator.CreateInstance(entryListType);
                    foreach (LocalizationEntryDefinition definition in table.Entries)
                    {
                        object entry = Activator.CreateInstance(entryType);
                        keyField.SetValue(entry, definition.Key);
                        valueField.SetValue(entry, definition.Values[language]);
                        entries.Add(entry);
                    }

                    object section = Activator.CreateInstance(sectionType);
                    tableIdField.SetValue(section, table.TableId);
                    sectionEntriesField.SetValue(section, entries);
                    sections.Add(section);
                }

                string assetPath = assetFolder.TrimEnd('/') + "/" + language + "LocalizationDataSO.asset";
                if (!paths.Add(assetPath))
                {
                    throw new InvalidOperationException($"语言“{language}”生成了重复的资产路径：{assetPath}");
                }

                result.Add(new(language, sections, assetPath));
            }

            return result;
        }

        private static PreparedCatalogAsset PrepareCatalogAsset(LocalizationDataSet dataSet, string assetFolder, Type packageReferenceType, IReadOnlyList<PreparedLanguageAsset> preparedLanguages, FieldInfo packageLanguageField, FieldInfo resourcesPathField)
        {
            Type packageListType = typeof(List<>).MakeGenericType(packageReferenceType);
            IList packages = (IList)Activator.CreateInstance(packageListType);
            foreach (PreparedLanguageAsset prepared in preparedLanguages)
            {
                object package = Activator.CreateInstance(packageReferenceType);
                packageLanguageField.SetValue(package, prepared.Language);
                resourcesPathField.SetValue(package, GetResourcesLoadPath(prepared.AssetPath));
                packages.Add(package);
            }

            string defaultLanguage = dataSet.Languages.FirstOrDefault(language => string.Equals(language, LocalizationGenerationPaths.PreferredDefaultLanguage, StringComparison.OrdinalIgnoreCase)) ?? dataSet.Languages.FirstOrDefault();
            if (string.IsNullOrEmpty(defaultLanguage))
            {
                throw new InvalidOperationException("无法生成语言目录：本地化数据中没有任何语言。");
            }

            string assetPath = assetFolder.TrimEnd('/') + "/" + LocalizationGenerationPaths.CatalogFileName;
            GetResourcesLoadPath(assetPath);
            return new(defaultLanguage, packages, assetPath);
        }

        private static ScriptableObject GetOrCreateAsset(string assetPath, Type expectedType, string assetName, ICollection<string> createdPaths)
        {
            Object existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existing != null)
            {
                if (existing.GetType() != expectedType)
                {
                    throw new InvalidOperationException($"路径“{assetPath}”已存在类型为“{existing.GetType().FullName}”的资产。");
                }

                return (ScriptableObject)existing;
            }

            ScriptableObject asset = ScriptableObject.CreateInstance(expectedType);
            asset.name = assetName;
            AssetDatabase.CreateAsset(asset, assetPath);
            createdPaths.Add(assetPath);
            return asset;
        }

        private static string GetResourcesLoadPath(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/');
            const string resourcesSegment = "/Resources/";
            int resourcesIndex = normalized.LastIndexOf(resourcesSegment, StringComparison.OrdinalIgnoreCase);
            if (resourcesIndex < 0)
            {
                throw new InvalidOperationException($"SO 资产输出目录必须位于 Resources 目录下，当前资产路径：{assetPath}");
            }

            string relativePath = normalized.Substring(resourcesIndex + resourcesSegment.Length);
            return relativePath.Substring(0, relativePath.Length - Path.GetExtension(relativePath).Length);
        }

        private static void DeleteStaleOwnedLanguageAssets(string assetFolder, Type dataType, FieldInfo generatorIdField, IEnumerable<string> expectedPaths)
        {
            HashSet<string> expected = new(expectedPaths, StringComparer.OrdinalIgnoreCase);
            string[] assetGuids = AssetDatabase.FindAssets("t:" + dataType.Name, new[] { assetFolder });
            foreach (string assetGuid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (expected.Contains(assetPath) || !string.Equals(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'), assetFolder.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (asset != null && asset.GetType() == dataType && string.Equals(generatorIdField.GetValue(asset) as string, LocalizationGenerationPaths.GeneratorId, StringComparison.Ordinal))
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }
            }
        }

        private static void ValidateScriptableObjectType(Type type)
        {
            if (!typeof(ScriptableObject).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"生成类型“{type.FullName}”没有继承 ScriptableObject。");
            }
        }

        private static FieldInfo GetRequiredField(Type type, string fieldName)
        {
            return type.GetField(fieldName, InstanceFieldFlags) ?? throw new MissingFieldException(type.FullName, fieldName);
        }

        private static Type GetRequiredLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }

            throw new InvalidOperationException($"尚未找到生成类型“{fullName}”，请等待 Unity 完成脚本编译后重试。");
        }

        private sealed class PreparedLanguageAsset
        {
            public PreparedLanguageAsset(string language, IList sections, string assetPath)
            {
                Language = language;
                Sections = sections;
                AssetPath = assetPath;
            }

            public string Language { get; }
            public IList Sections { get; }
            public string AssetPath { get; }
        }

        private sealed class PreparedCatalogAsset
        {
            public PreparedCatalogAsset(string defaultLanguage, IList packages, string assetPath)
            {
                DefaultLanguage = defaultLanguage;
                Packages = packages;
                AssetPath = assetPath;
            }

            public string DefaultLanguage { get; }
            public IList Packages { get; }
            public string AssetPath { get; }
        }

        private sealed class LanguageAssetBackup
        {
            public LanguageAssetBackup(ScriptableObject asset, object language, object sections, object generatorId)
            {
                Asset = asset;
                Language = language;
                Sections = sections;
                GeneratorId = generatorId;
            }

            public ScriptableObject Asset { get; }
            public object Language { get; }
            public object Sections { get; }
            public object GeneratorId { get; }
        }

        private sealed class CatalogAssetBackup
        {
            public CatalogAssetBackup(ScriptableObject asset, object defaultLanguage, object packages, object generatorId)
            {
                Asset = asset;
                DefaultLanguage = defaultLanguage;
                Packages = packages;
                GeneratorId = generatorId;
            }

            public ScriptableObject Asset { get; }
            public object DefaultLanguage { get; }
            public object Packages { get; }
            public object GeneratorId { get; }
        }
    }
}
