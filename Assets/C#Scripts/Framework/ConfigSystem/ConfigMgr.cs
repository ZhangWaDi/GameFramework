using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameFramework.ConfigSystem
{
    /// <summary>
    /// 所有配置数据的运行时访问入口。
    /// </summary>
    public sealed class ConfigMgr : Singleton<ConfigMgr>
    {
        public const string DefaultDatabaseResourcesPath = "ConfigData/ConfigDatabase";
        private ConfigDatabaseSO database;
        private bool isInitialized;
        public bool IsInitialized => isInitialized;
        public ConfigDatabaseSO Database
        {
            get
            {
                EnsureInitialized();
                return database;
            }
        }

        public override void OnInit()
        {
            if (isInitialized)
            {
                return;
            }

            ConfigDatabaseSO loadedDatabase = Resources.Load<ConfigDatabaseSO>(DefaultDatabaseResourcesPath);

            if (loadedDatabase == null)
            {
                string message = $"未能从 Resources 路径“{DefaultDatabaseResourcesPath}”加载配置数据库。预期资产路径为“Assets/Resources/ConfigData/ConfigDatabase.asset”。";
                Logger.Instance.LogError(message);
                throw new InvalidOperationException(message);
            }

            Initialize(loadedDatabase);
        }

        /// <summary>
        /// 使用指定的配置数据库初始化管理器。
        /// 该入口也可用于测试，或接入 Resources 之外的加载方案。
        /// </summary>
        public void Initialize(ConfigDatabaseSO targetDatabase)
        {
            if (targetDatabase == null)
            {
                throw new ArgumentNullException(nameof(targetDatabase));
            }

            if (isInitialized)
            {
                if (ReferenceEquals(database, targetDatabase))
                {
                    return;
                }

                database.Release();
                isInitialized = false;
            }

            database = targetDatabase;

            try
            {
                database.Initialize();
                isInitialized = true;
            }
            catch (Exception exception)
            {
                database = null;
                isInitialized = false;
                Logger.Instance.LogError($"初始化配置数据库“{targetDatabase.name}”失败：{exception.Message}");
                throw;
            }
        }

        /// <summary>
        /// 尝试获取指定类型的配置表。
        /// 获取失败时输出警告并返回 false。
        /// </summary>
        public bool TryGetTable<TTable>(out TTable table)
            where TTable : ConfigTableSOBase
        {
            EnsureInitialized();
            if (database.TryGetTable(out table))
            {
                return true;
            }

            Logger.Instance.LogWarning($"获取配置表失败：配置数据库“{database.name}”中不存在“{typeof(TTable).FullName}”。");
            return false;
        }

        /// <summary>
        /// 获取指定类型的配置表。
        /// 获取失败时输出错误并抛出 KeyNotFoundException。
        /// </summary>
        public TTable GetTable<TTable>()
            where TTable : ConfigTableSOBase
        {
            EnsureInitialized();
            if (database.TryGetTable(out TTable table))
            {
                return table;
            }

            string message = $"获取配置表失败：配置数据库“{database.name}”中不存在“{typeof(TTable).FullName}”。";
            Logger.Instance.LogError(message);
            throw new KeyNotFoundException(message);
        }

        /// <summary>
        /// 尝试根据 ID 获取指定类型的配置数据行。
        /// 配置表或 ID 不存在时输出警告并返回 false。
        /// </summary>
        public bool TryGetDataById<TRow>(int id, out TRow row) where TRow : ConfigDataRowBase
        {
            EnsureInitialized();

            if (!database.TryGetTableByRow(out ConfigTableSO<TRow> table))
            {
                row = null;
                Logger.Instance.LogWarning($"获取配置数据行失败：配置数据库“{database.name}”中不存在行类型“{typeof(TRow).FullName}”对应的配置表。");
                return false;
            }

            if (table.TryGetDataById(id, out row))
            {
                return true;
            }

            Logger.Instance.LogWarning($"获取配置数据行失败：配置表“{table.name}”中不存在 ID 为 {id} 的“{typeof(TRow).FullName}”。");
            return false;
        }

        /// <summary>
        /// 根据 ID 获取指定类型的配置数据行。
        /// 配置表或 ID 不存在时输出错误并抛出 KeyNotFoundException。
        /// </summary>
        public TRow GetDataById<TRow>(int id) where TRow : ConfigDataRowBase
        {
            EnsureInitialized();
            if (!database.TryGetTableByRow(out ConfigTableSO<TRow> table))
            {
                string tableMessage = $"获取配置数据行失败：配置数据库“{database.name}”中不存在行类型“{typeof(TRow).FullName}”对应的配置表。";
                Logger.Instance.LogError(tableMessage);
                throw new KeyNotFoundException(tableMessage);
            }

            if (table.TryGetDataById(id, out TRow row))
            {
                return row;
            }

            string rowMessage = $"获取配置数据行失败：配置表“{table.name}”中不存在 ID 为 {id} 的“{typeof(TRow).FullName}”。";
            Logger.Instance.LogError(rowMessage);
            throw new KeyNotFoundException(rowMessage);
        }

        /// <summary>
        /// 获取指定类型的全部配置数据行，顺序与配置表中的数据顺序一致。
        /// 对应配置表不存在时输出错误并抛出 KeyNotFoundException。
        /// </summary>
        public IReadOnlyList<TRow> GetAllDataByType<TRow>() where TRow : ConfigDataRowBase
        {
            EnsureInitialized();
            if (database.TryGetTableByRow(out ConfigTableSO<TRow> table))
            {
                return table.DataList;
            }

            string message = $"获取全部配置数据行失败：配置数据库“{database.name}”中不存在行类型“{typeof(TRow).FullName}”对应的配置表。";
            Logger.Instance.LogError(message);
            throw new KeyNotFoundException(message);
        }

        protected override void OnRelease()
        {
            if (database != null)
            {
                database.Release();
            }

            database = null;
            isInitialized = false;
        }

        /// <summary>
        /// 确保管理器已初始化。
        /// 如果未初始化，会抛出 InvalidOperationException 异常。
        /// </summary>
        private void EnsureInitialized()
        {
            if (!isInitialized)
            {
                const string message = "ConfigMgr 尚未初始化，请在项目启动流程中调用 ConfigMgr.Instance.OnInit()。";
                Logger.Instance.LogError(message);
                throw new InvalidOperationException(message);
            }
        }
    }
}
