using System;
using UnityEngine;

namespace GameFramework.UI.Virtualization
{
    /// <summary>
    /// 循环滚动列表 Item 基类。
    /// </summary>
    public abstract class LoopItemBase : MonoBehaviour
    {
        public LoopItemDataBase Data { get; private set; }

        /// <summary>
        /// 保存数据并刷新 Item 显示内容。
        /// </summary>
        public void RefreshByData(LoopItemDataBase data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data), "循环滚动项数据不能为空。");
            OnRefreshByData(data);
        }

        /// <summary>
        /// 子类应实现此方法，根据数据刷新 Item 显示内容。
        /// </summary>
        protected abstract void OnRefreshByData(LoopItemDataBase data);
    }
}
