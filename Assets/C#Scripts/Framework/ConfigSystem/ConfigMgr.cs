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
                throw new InvalidOperationException("未能从 Resources 路径" + $"“{DefaultDatabaseResourcesPath}”加载配置数据库。" + "预期资产路径为“Assets/Resources/ConfigData/ConfigDatabase.asset”。");
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
            catch
            {
                database = null;
                isInitialized = false;
                throw;
            }
        }

        /// <summary>
        /// 尝试获取指定类型的配置表。
        /// </summary>
        public bool TryGetTable<TTable>(out TTable table)
            where TTable : ConfigTableSOBase
        {
            EnsureInitialized();
            return database.TryGetTable(out table);
        }

        /// <summary>
        /// 获取指定类型的配置表。
        /// </summary>
        public TTable GetTable<TTable>()
            where TTable : ConfigTableSOBase
        {
            EnsureInitialized();
            return database.GetTable<TTable>();
        }

        /// <summary>
        /// 尝试根据 ID 获取指定类型的配置数据。
        /// </summary>
        public bool TryGetDataById<TData>(int id, out TData data)
            where TData : ConfigDataBase
        {
            EnsureInitialized();

            if (!database.TryGetTableByData(
                    out ConfigTableSO<TData> table))
            {
                data = null;
                return false;
            }

            return table.TryGetData(id, out data);
        }

        /// <summary>
        /// 根据 ID 获取指定类型的配置数据。
        /// </summary>
        public TData GetDataById<TData>(int id)
            where TData : ConfigDataBase
        {
            EnsureInitialized();
            return database.GetTableByData<TData>().GetData(id);
        }

        /// <summary>
        /// 获取指定类型的全部配置数据，顺序与配置表中的数据顺序一致。
        /// </summary>
        public IReadOnlyList<TData> GetAllDataByType<TData>()
            where TData : ConfigDataBase
        {
            EnsureInitialized();
            return database.GetTableByData<TData>().DataList;
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
                throw new InvalidOperationException("ConfigMgr 尚未初始化，请在项目启动流程中调用 " + "ConfigMgr.Instance.OnInit()。");
            }
        }
    }
}
