using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameFramework.ConfigSystem
{
    /// <summary>
    /// 配置数据的统一入口资产，保存所有配置表 SO 的引用。
    /// </summary>
    [CreateAssetMenu(
        fileName = "ConfigDatabase",
        menuName = "GameFramework/Config Data/Config Database")]
    public sealed class ConfigDatabaseSO : ScriptableObject
    {
        [SerializeField]
        private List<ConfigTableSOBase> tables = new();

        [NonSerialized]
        private Dictionary<Type, ConfigTableSOBase> tableByType;

        [NonSerialized]
        private Dictionary<Type, ConfigTableSOBase> tableByDataType;

        [NonSerialized]
        private bool isInitialized;

        public bool IsInitialized => isInitialized;

        public int Count => tables.Count;

        public IReadOnlyList<ConfigTableSOBase> Tables => tables;

        public void Initialize()
        {
            if (isInitialized)
            {
                return;
            }

            tableByType = new Dictionary<Type, ConfigTableSOBase>(tables.Count);
            tableByDataType = new Dictionary<Type, ConfigTableSOBase>(tables.Count);

            try
            {
                for (int index = 0; index < tables.Count; index++)
                {
                    ConfigTableSOBase table = tables[index];
                    if (table == null)
                    {
                        throw new InvalidOperationException(
                            $"配置数据库“{name}”中索引为 {index} 的配置表为空。");
                    }

                    Type tableType = table.GetType();
                    if (!tableByType.TryAdd(tableType, table))
                    {
                        throw new InvalidOperationException(
                            $"配置数据库“{name}”中存在重复的配置表类型：" +
                            $"“{tableType.FullName}”。");
                    }

                    Type dataType = table.DataType;
                    if (!tableByDataType.TryAdd(dataType, table))
                    {
                        throw new InvalidOperationException(
                            $"配置数据库“{name}”中存在多个数据类型为" +
                            $"“{dataType.FullName}”的配置表。");
                    }

                    table.Initialize();
                }

                isInitialized = true;
            }
            catch
            {
                Release();
                throw;
            }
        }

        public bool TryGetTable<TTable>(out TTable table)
            where TTable : ConfigTableSOBase
        {
            EnsureInitialized();

            if (tableByType.TryGetValue(typeof(TTable), out ConfigTableSOBase value))
            {
                table = (TTable)value;
                return true;
            }

            table = null;
            return false;
        }

        public TTable GetTable<TTable>()
            where TTable : ConfigTableSOBase
        {
            if (TryGetTable(out TTable table))
            {
                return table;
            }

            throw new KeyNotFoundException(
                $"配置数据库“{name}”中不存在配置表“{typeof(TTable).FullName}”。");
        }

        public bool TryGetTableByData<TData>(out ConfigTableSO<TData> table)
            where TData : ConfigDataBase
        {
            EnsureInitialized();

            if (tableByDataType.TryGetValue(
                    typeof(TData),
                    out ConfigTableSOBase value))
            {
                table = (ConfigTableSO<TData>)value;
                return true;
            }

            table = null;
            return false;
        }

        public ConfigTableSO<TData> GetTableByData<TData>()
            where TData : ConfigDataBase
        {
            if (TryGetTableByData(out ConfigTableSO<TData> table))
            {
                return table;
            }

            throw new KeyNotFoundException(
                $"配置数据库“{name}”中不存在数据类型" +
                $"“{typeof(TData).FullName}”。");
        }

        public void Release()
        {
            for (int index = 0; index < tables.Count; index++)
            {
                ConfigTableSOBase table = tables[index];
                if (table != null)
                {
                    table.Release();
                }
            }

            tableByType?.Clear();
            tableByType = null;

            tableByDataType?.Clear();
            tableByDataType = null;

            isInitialized = false;
        }

        private void EnsureInitialized()
        {
            if (!isInitialized)
            {
                throw new InvalidOperationException(
                    $"配置数据库“{name}”尚未初始化。");
            }
        }
    }
}
