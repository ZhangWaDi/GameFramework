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
        /// 尝试根据 ID 获取指定类型的配置数据行。
        /// </summary>
        public bool TryGetDataById<TRow>(int id, out TRow row) where TRow : ConfigDataRowBase
        {
            EnsureInitialized();

            if (!database.TryGetTableByRow(out ConfigTableSO<TRow> table))
            {
                row = null;
                return false;
            }

            return table.TryGetDataById(id, out row);
        }

        /// <summary>
        /// 根据 ID 获取指定类型的配置数据行。
        /// </summary>
        public TRow GetDataById<TRow>(int id) where TRow : ConfigDataRowBase
        {
            EnsureInitialized();
            return database.GetTableByRow<TRow>().GetData(id);
        }

        /// <summary>
        /// 获取指定类型的全部配置数据行，顺序与配置表中的数据顺序一致。
        /// </summary>
        public IReadOnlyList<TRow> GetAllDataByType<TRow>() where TRow : ConfigDataRowBase
        {
            EnsureInitialized();
            return database.GetTableByRow<TRow>().DataList;
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
