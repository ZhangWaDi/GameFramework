using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameFramework.PoolSystem
{
    /// <summary>
    /// 一个预制体对应一个池的 GameObject 专用对象池。该类型不保证线程安全。
    /// </summary>
    public sealed class GameObjPool : IObjectPool<GameObject>
    {
        private readonly Stack<GameObject> availableItems = new();
        private readonly HashSet<GameObject> allItems = new(ReferenceEqualityComparer<GameObject>.Instance);
        private readonly HashSet<GameObject> availableItemSet = new(ReferenceEqualityComparer<GameObject>.Instance);
        private readonly GameObject prefab;
        private bool isDisposed;

        public GameObjPool(GameObject prefab)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab), "对象池预制体不能为空。");
            }
            this.prefab = prefab;
        }

        /// <summary>
        /// 获取一个可用 GameObject；没有空闲 GameObject 时由具体对象池创建。
        /// </summary>
        /// <returns>一个可用 GameObject。</returns>
        public GameObject Get()
        {
            ThrowIfDisposed();
            while (availableItems.Count > 0)
            {
                GameObject item = availableItems.Pop();
                if (!availableItemSet.Remove(item))
                {
                    throw new InvalidOperationException("对象池状态异常：取出的空闲对象未登记。");
                }
                if (item == null)
                {
                    allItems.Remove(item);
                    continue;
                }
                item.SetActive(true);
                return item;
            }

            if (prefab == null)
            {
                throw new InvalidOperationException("对象池预制体已经被销毁，无法创建对象。");
            }
            GameObject createdItem = Object.Instantiate(prefab);
            createdItem.SetActive(true);
            if (!allItems.Add(createdItem))
            {
                DestroyItem(createdItem);
                throw new InvalidOperationException("对象池创建了重复的 GameObject 实例。");
            }
            return createdItem;
        }

        /// <summary>
        /// 将当前对象池借出的 GameObject 回收到空闲集合。
        /// </summary>
        /// <param name="item">要回收的 GameObject。</param>
        public void Release(GameObject item)
        {
            ThrowIfDisposed();
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item), "不能回收空对象或已经销毁的对象。");
            }
            if (!allItems.Contains(item))
            {
                throw new InvalidOperationException("不能回收不是由当前对象池创建的对象。");
            }
            if (!availableItemSet.Add(item))
            {
                throw new InvalidOperationException("不能重复回收已经处于空闲状态的对象。");
            }
            item.SetActive(false);
            availableItems.Push(item);
        }

        /// <summary>
        /// 清空当前对象池中的所有 GameObject。
        /// </summary>
        public void Clear()
        {
            ThrowIfDisposed();
            while (availableItems.Count > 0)
            {
                GameObject item = availableItems.Pop();
                availableItemSet.Remove(item);
                allItems.Remove(item);
                DestroyItem(item);
            }
        }

        /// <summary>
        /// 销毁当前对象池中的所有 GameObject。
        /// </summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            GameObject[] items = new GameObject[allItems.Count];
            allItems.CopyTo(items);
            availableItems.Clear();
            availableItemSet.Clear();
            allItems.Clear();
            for (int i = 0; i < items.Length; i++)
            {
                DestroyItem(items[i]);
            }
        }

        #region 内部实现
        private static void DestroyItem(GameObject item)
        {
            if (item == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Object.Destroy(item);
            }
            else
            {
                Object.DestroyImmediate(item);
            }
        }

        private void ThrowIfDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(GameObjPool), "对象池已经销毁，不能继续使用。");
            }
        }
        #endregion
    }
}
