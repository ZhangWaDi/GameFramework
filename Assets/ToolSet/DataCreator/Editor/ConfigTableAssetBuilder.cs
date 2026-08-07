using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameFramework.ConfigSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameFramework.ConfigData.Editor
{
    /// <summary>
    /// 将已完成类型转换的配置行写入强类型 ScriptableObject，
    /// 并维护运行时使用的 ConfigDatabase 资产。
    /// </summary>
    internal static class ConfigTableAssetBuilder
    {
        private const BindingFlags DeclaredInstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>
        /// 原子化构建全部表资产。
        /// 方法会先完成类型发现和行对象准备，确认无误后才修改 AssetDatabase。
        /// 若写入阶段失败，会恢复既有资产并删除本轮新建的资产。
        /// </summary>
        public static IReadOnlyList<string> Build(
            IReadOnlyList<ConfigTableDefinition> definitions,
            string tableAssetFolder)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            string normalizedTableAssetFolder = NormalizeAssetFolderPath(tableAssetFolder);
            EnsureAssetFolder(normalizedTableAssetFolder);
            EnsureAssetFolder(GetParentAssetPath(
                ConfigTableGenerationPaths.DatabaseAssetPath));

            List<PreparedTable> preparedTables = definitions.Select(definition => PrepareTable(definition, normalizedTableAssetFolder)).ToList();

            List<string> createdAssetPaths = new();
            List<AssetBackup> backups = new();
            ConfigDatabaseSO database = null;
            List<ConfigTableSOBase> databaseBackup = null;
            bool databaseWasCreated = false;

            try
            {
                foreach (PreparedTable prepared in preparedTables)
                {
                    ConfigTableSOBase tableAsset = prepared.ExistingAsset;
                    if (tableAsset == null)
                    {
                        tableAsset = (ConfigTableSOBase)
                            ScriptableObject.CreateInstance(prepared.TableType);
                        tableAsset.name = $"{prepared.Definition.Schema.TableName}SO";
                        AssetDatabase.CreateAsset(
                            tableAsset,
                            prepared.AssetPath);
                        createdAssetPaths.Add(prepared.AssetPath);
                    }
                    else
                    {
                        backups.Add(new AssetBackup(
                            tableAsset,
                            prepared.DataListField,
                            prepared.DataListField.GetValue(tableAsset)));
                    }

                    tableAsset.Release();
                    prepared.DataListField.SetValue(
                        tableAsset,
                        prepared.TypedRows);

                    // 立即建立一次索引，用运行时同一套规则再次验证 ID 和空行。
                    tableAsset.Initialize();
                    tableAsset.Release();
                    EditorUtility.SetDirty(tableAsset);
                    prepared.ResultAsset = tableAsset;
                }

                database = AssetDatabase.LoadAssetAtPath<ConfigDatabaseSO>(
                    ConfigTableGenerationPaths.DatabaseAssetPath);
                if (database == null)
                {
                    Object existing = AssetDatabase.LoadMainAssetAtPath(ConfigTableGenerationPaths.DatabaseAssetPath);
                    if (existing != null)
                    {
                        throw new InvalidOperationException($"路径“{ConfigTableGenerationPaths.DatabaseAssetPath}”" + $"已存在类型为“{existing.GetType().FullName}”的非配置数据库资产。");
                    }

                    database = ScriptableObject.CreateInstance<ConfigDatabaseSO>();
                    database.name = "ConfigDatabase";
                    AssetDatabase.CreateAsset(
                        database,
                        ConfigTableGenerationPaths.DatabaseAssetPath);
                    createdAssetPaths.Add(
                        ConfigTableGenerationPaths.DatabaseAssetPath);
                    databaseWasCreated = true;
                }

                FieldInfo tablesField = FindField(typeof(ConfigDatabaseSO), "tables");
                if (!databaseWasCreated)
                {
                    databaseBackup = new List<ConfigTableSOBase>(
                        (List<ConfigTableSOBase>)tablesField.GetValue(database));
                }

                database.Release();
                List<ConfigTableSOBase> tableAssets = preparedTables.Select(item => item.ResultAsset).ToList();
                tablesField.SetValue(database, tableAssets);

                // 数据库初始化可验证表类型和数据类型是否重复。
                database.Initialize();
                database.Release();
                EditorUtility.SetDirty(database);

                AssetDatabase.SaveAssets();

                List<string> result = preparedTables.Select(item => item.AssetPath).ToList();
                result.Add(ConfigTableGenerationPaths.DatabaseAssetPath);
                return result;
            }
            catch
            {
                Rollback(
                    backups,
                    database,
                    databaseBackup,
                    createdAssetPaths);
                throw;
            }
        }

        /// <summary>
        /// 发现生成类型、创建强类型 List&lt;T&gt;，并把中间数据行映射到生成字段。
        /// 此阶段不会修改 Unity 资产。
        /// </summary>
        private static PreparedTable PrepareTable(
            ConfigTableDefinition definition,
            string tableAssetFolder)
        {
            ConfigTableSchema schema = definition.Schema;
            Type dataType = FindLoadedType(schema.DataTypeFullName);
            Type tableType = FindLoadedType(schema.TableTypeFullName);

            if (dataType == null || tableType == null)
            {
                throw new InvalidOperationException($"尚未加载配置表“{schema.TableName}”的生成类型。" + "请确认 Unity 脚本编译已成功完成。");
            }

            if (!typeof(ConfigDataBase).IsAssignableFrom(dataType))
            {
                throw new InvalidOperationException($"生成数据类型“{dataType.FullName}”没有继承 ConfigDataBase。");
            }

            if (!typeof(ConfigTableSOBase).IsAssignableFrom(tableType))
            {
                throw new InvalidOperationException($"生成表类型“{tableType.FullName}”没有继承 ConfigTableSOBase。");
            }

            FieldInfo dataListField = FindField(tableType, "dataList");
            Type expectedListType = typeof(List<>).MakeGenericType(dataType);
            if (dataListField.FieldType != expectedListType)
            {
                throw new InvalidOperationException($"配置表“{schema.TableName}”的序列化列表类型不匹配：" + $"期望“{expectedListType.FullName}”，实际“{dataListField.FieldType.FullName}”。");
            }

            IList typedRows = (IList)Activator.CreateInstance(expectedListType);
            foreach (ConfigTableDataRow sourceRow in definition.Rows)
            {
                object targetRow = Activator.CreateInstance(dataType);
                for (int fieldIndex = 0; fieldIndex < schema.Fields.Count; fieldIndex++)
                {
                    ConfigFieldSchema field = schema.Fields[fieldIndex];
                    object value = sourceRow.Values[fieldIndex];
                    SetGeneratedMember(
                        targetRow,
                        dataType,
                        field,
                        value,
                        sourceRow.SourceRow);
                }

                typedRows.Add(targetRow);
            }

            string assetPath = $"{tableAssetFolder}/" + $"{schema.TableName}SO.asset";
            ConfigTableSOBase existingAsset = AssetDatabase.LoadAssetAtPath<ConfigTableSOBase>(assetPath);

            if (existingAsset == null)
            {
                Object existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (existing != null)
                {
                    throw new InvalidOperationException($"路径“{assetPath}”已存在类型为" + $"“{existing.GetType().FullName}”的非配置表资产。");
                }
            }
            else if (existingAsset.GetType() != tableType)
            {
                throw new InvalidOperationException($"既有资产“{assetPath}”的类型为" + $"“{existingAsset.GetType().FullName}”，与当前生成类型" + $"“{tableType.FullName}”不一致。请手动确认旧资产后再处理。");
            }

            return new PreparedTable(
                definition,
                tableType,
                dataListField,
                typedRows,
                assetPath,
                existingAsset);
        }

        private static void SetGeneratedMember(
            object target,
            Type dataType,
            ConfigFieldSchema field,
            object value,
            int sourceRow)
        {
            if (string.Equals(field.Name, "ID", StringComparison.Ordinal))
            {
                PropertyInfo idProperty = dataType.GetProperty("ID", BindingFlags.Instance | BindingFlags.Public);
                if (idProperty == null || !idProperty.CanWrite)
                {
                    throw new MissingMemberException(dataType.FullName, "ID");
                }

                idProperty.SetValue(target, value);
                return;
            }

            FieldInfo targetField = dataType.GetField(field.Name, BindingFlags.Instance | BindingFlags.Public);
            if (targetField == null)
            {
                throw new MissingFieldException($"生成类型“{dataType.FullName}”缺少字段“{field.Name}”" + $"（CSV 第 {sourceRow} 行）。");
            }

            if (value != null && !targetField.FieldType.IsInstanceOfType(value))
            {
                throw new InvalidCastException($"字段“{dataType.Name}.{field.Name}”期望" + $"“{targetField.FieldType.FullName}”，实际收到" + $"“{value.GetType().FullName}”（CSV 第 {sourceRow} 行）。");
            }

            targetField.SetValue(target, value);
        }

        /// <summary>
        /// 在所有已加载程序集内按完整名称发现生成类型。
        /// 避免依赖固定的 Assembly-CSharp 名称，从而兼容后续 asmdef 拆分。
        /// </summary>
        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(fieldName, DeclaredInstanceFlags);
                if (field != null)
                {
                    return field;
                }
            }

            throw new MissingFieldException(type.FullName, fieldName);
        }

        /// <summary>
        /// 规范化并校验配置表 SO 输出目录，拒绝 Assets 目录之外的路径。
        /// </summary>
        private static string NormalizeAssetFolderPath(string assetFolderPath)
        {
            if (string.IsNullOrWhiteSpace(assetFolderPath))
            {
                throw new ArgumentException("配置表 SO 输出目录不能为空。", nameof(assetFolderPath));
            }

            string normalized = assetFolderPath.Replace('\\', '/').TrimEnd('/');
            if (!string.Equals(normalized, "Assets", StringComparison.Ordinal) &&
                !normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new ArgumentException($"配置表 SO 输出目录必须位于 Assets 下：{assetFolderPath}", nameof(assetFolderPath));
            }

            return normalized;
        }

        /// <summary>
        /// 逐级创建 Unity Asset 文件夹，确保 CreateAsset 的父路径已经注册。
        /// </summary>
        internal static void EnsureAssetFolder(string assetFolderPath)
        {
            string normalized = assetFolderPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            string[] segments = normalized.Split('/');
            if (segments.Length == 0 ||
                !string.Equals(segments[0], "Assets", StringComparison.Ordinal))
            {
                throw new ArgumentException($"资产目录必须位于 Assets 下：{assetFolderPath}", nameof(assetFolderPath));
            }

            string current = "Assets";
            for (int index = 1; index < segments.Length; index++)
            {
                string next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        private static string GetParentAssetPath(string assetPath)
        {
            int separatorIndex = assetPath.LastIndexOf('/');
            if (separatorIndex <= 0)
            {
                throw new ArgumentException($"无法确定资产父目录：{assetPath}", nameof(assetPath));
            }

            return assetPath.Substring(0, separatorIndex);
        }

        /// <summary>
        /// 恢复本轮修改前的内存值，并删除只在本轮创建的资产。
        /// </summary>
        private static void Rollback(
            IEnumerable<AssetBackup> backups,
            ConfigDatabaseSO database,
            List<ConfigTableSOBase> databaseBackup,
            IReadOnlyList<string> createdAssetPaths)
        {
            foreach (AssetBackup backup in backups)
            {
                backup.Asset.Release();
                backup.Field.SetValue(backup.Asset, backup.Value);
                EditorUtility.SetDirty(backup.Asset);
            }

            if (database != null && databaseBackup != null)
            {
                database.Release();
                FindField(typeof(ConfigDatabaseSO), "tables")
                    .SetValue(database, databaseBackup);
                EditorUtility.SetDirty(database);
            }

            for (int index = createdAssetPaths.Count - 1; index >= 0; index--)
            {
                AssetDatabase.DeleteAsset(createdAssetPaths[index]);
            }
        }

        private sealed class PreparedTable
        {
            public PreparedTable(
                ConfigTableDefinition definition,
                Type tableType,
                FieldInfo dataListField,
                IList typedRows,
                string assetPath,
                ConfigTableSOBase existingAsset)
            {
                Definition = definition;
                TableType = tableType;
                DataListField = dataListField;
                TypedRows = typedRows;
                AssetPath = assetPath;
                ExistingAsset = existingAsset;
            }

            public ConfigTableDefinition Definition { get; }

            public Type TableType { get; }

            public FieldInfo DataListField { get; }

            public IList TypedRows { get; }

            public string AssetPath { get; }

            public ConfigTableSOBase ExistingAsset { get; }

            public ConfigTableSOBase ResultAsset { get; set; }
        }

        private sealed class AssetBackup
        {
            public AssetBackup(Object asset, FieldInfo field, object value)
            {
                Asset = (ConfigTableSOBase)asset;
                Field = field;
                Value = value;
            }

            public ConfigTableSOBase Asset { get; }

            public FieldInfo Field { get; }

            public object Value { get; }
        }
    }
}
