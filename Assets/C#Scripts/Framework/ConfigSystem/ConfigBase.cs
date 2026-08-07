using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameFramework.ConfigSystem
{
    /// <summary>
    /// 单行配置数据的基类。
    /// ID 由生成的配置数据行类型实现，并由编辑器导表流程写入。
    /// </summary>
    [Serializable]
    public abstract class ConfigDataRowBase
    {
        /// <summary>
        /// 获取或设置当前数据行的唯一 ID。
        /// setter 主要供编辑器导入器写入；运行时应将配置数据视为只读。
        /// </summary>
        public abstract int ID { get; set; }
    }

    /// <summary>
    /// 所有配置表 SO 的非泛型基类，供配置数据库统一管理。
    /// </summary>
    public abstract class ConfigTableSOBase : ScriptableObject
    {
        [NonSerialized]
        private bool isInitialized;

        public bool IsInitialized => isInitialized;

        /// <summary>
        /// 获取当前配置表保存的配置数据行类型。
        /// </summary>
        public abstract Type RowType { get; }

        public abstract int Count { get; }

        /// <summary>
        /// 构建当前配置表的运行时索引。
        /// 重复调用不会重复分配，构建失败时会清理未完成状态。
        /// </summary>
        public void Initialize()
        {
            if (isInitialized)
            {
                return;
            }

            try
            {
                BuildIndex();
                isInitialized = true;
            }
            catch
            {
                ClearIndex();
                throw;
            }
        }

        /// <summary>
        /// 清理非序列化运行时索引，使配置表可在下次使用时重新初始化。
        /// </summary>
        public void Release()
        {
            ClearIndex();
            isInitialized = false;
        }

        protected abstract void BuildIndex();

        protected abstract void ClearIndex();
    }

    /// <summary>
    /// 配置表的通用基类。
    /// 序列化保存配置数据行列表，并在运行时建立 ID 到数据行的索引。
    /// 具体配置表只需要继承当前泛型类，无需重复实现查询逻辑。
    /// </summary>
    /// <typeparam name="TRow">当前配置表保存的配置数据行类型。</typeparam>
    public abstract class ConfigTableSO<TRow> : ConfigTableSOBase where TRow : ConfigDataRowBase
    {
        [SerializeField]
        private List<TRow> dataList = new();

        [NonSerialized]
        private Dictionary<int, TRow> dataById;

        [NonSerialized]
        private List<int> dataIds;

        public override Type RowType => typeof(TRow);

        public override int Count => dataList.Count;

        public IReadOnlyList<TRow> DataList => dataList;

        /// <summary>
        /// 获取当前配置表中的全部 ID，顺序与序列化数据列表一致。
        /// </summary>
        public IReadOnlyList<int> DataIds
        {
            get
            {
                EnsureInitialized();
                return dataIds;
            }
        }

        /// <summary>
        /// 尝试通过 ID 读取一行配置数据。
        /// 调用前要求当前表已经由配置数据库初始化。
        /// </summary>
        public bool TryGetDataById(int id, out TRow row)
        {
            EnsureInitialized();
            return dataById.TryGetValue(id, out row);
        }

        /// <summary>
        /// 通过 ID 获取一行配置数据。
        /// ID 不存在时抛出 KeyNotFoundException。
        /// </summary>
        public TRow GetData(int id)
        {
            if (TryGetDataById(id, out TRow row))
            {
                return row;
            }

            throw new KeyNotFoundException($"配置表“{name}”中不存在 ID 为 {id} 的 {typeof(TRow).Name} 配置数据行。");
        }

        /// <summary>
        /// 根据序列化列表构建 ID 字典和稳定顺序的 ID 列表。
        /// </summary>
        protected override void BuildIndex()
        {
            dataById = new(dataList.Count);
            dataIds = new(dataList.Count);

            for (int index = 0; index < dataList.Count; index++)
            {
                TRow row = dataList[index];
                if (row == null)
                {
                    throw new InvalidOperationException($"配置表“{name}”中索引为 {index} 的配置数据行为空。");
                }

                if (!dataById.TryAdd(row.ID, row))
                {
                    throw new InvalidOperationException($"配置表“{name}”中存在重复 ID：{row.ID}。");
                }

                dataIds.Add(row.ID);
            }
        }

        protected override void ClearIndex()
        {
            dataById?.Clear();
            dataById = null;

            dataIds?.Clear();
            dataIds = null;
        }

        private void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException($"配置表“{name}”尚未初始化。");
            }
        }
    }
}
