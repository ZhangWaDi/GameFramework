using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameFramework.ConfigSystem
{
    /// <summary>
    /// 所有配置数据的统一入口资产，保存所有配置表 SO 的引用。
    /// </summary>
    public sealed class ConfigDatabaseSO : ScriptableObject
    {
        [SerializeField]
        private List<ConfigTableSOBase> tables = new();

        [NonSerialized]
        private Dictionary<Type, ConfigTableSOBase> tableByType;

        [NonSerialized]
        private Dictionary<Type, ConfigTableSOBase> tableByRowType;

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

            tableByType = new(tables.Count);
            tableByRowType = new(tables.Count);

            try
            {
                for (int index = 0; index < tables.Count; index++)
                {
                    ConfigTableSOBase table = tables[index];
                    if (table == null)
                    {
                        throw new InvalidOperationException($"配置数据库“{name}”中索引为 {index} 的配置表为空。");
                    }

                    Type tableType = table.GetType();
                    if (!tableByType.TryAdd(tableType, table))
                    {
                        throw new InvalidOperationException($"配置数据库“{name}”中存在重复的配置表类型：" + $"“{tableType.FullName}”。");
                    }

                    Type rowType = table.RowType;
                    if (!tableByRowType.TryAdd(rowType, table))
                    {
                        throw new InvalidOperationException($"配置数据库“{name}”中存在多个配置数据行类型为“{rowType.FullName}”的配置表。");
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

            throw new KeyNotFoundException($"配置数据库“{name}”中不存在配置表“{typeof(TTable).FullName}”。");
        }

        /// <summary>
        /// 尝试根据配置数据行类型获取对应的配置表。
        /// </summary>
        public bool TryGetTableByRow<TRow>(out ConfigTableSO<TRow> table) where TRow : ConfigDataRowBase
        {
            EnsureInitialized();

            if (tableByRowType.TryGetValue(typeof(TRow), out ConfigTableSOBase value))
            {
                table = (ConfigTableSO<TRow>)value;
                return true;
            }

            table = null;
            return false;
        }

        /// <summary>
        /// 根据配置数据行类型获取对应的配置表，不存在时抛出异常。
        /// </summary>
        public ConfigTableSO<TRow> GetTableByRow<TRow>() where TRow : ConfigDataRowBase
        {
            if (TryGetTableByRow(out ConfigTableSO<TRow> table))
            {
                return table;
            }

            throw new KeyNotFoundException($"配置数据库“{name}”中不存在配置数据行类型“{typeof(TRow).FullName}”。");
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

            tableByRowType?.Clear();
            tableByRowType = null;

            isInitialized = false;
        }

        private void EnsureInitialized()
        {
            if (!isInitialized)
            {
                throw new InvalidOperationException($"配置数据库“{name}”尚未初始化。");
            }
        }
    }
}
