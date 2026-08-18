using System;
using System.Collections.Generic;

namespace GameFramework.PoolSystem
{
    /// <summary>
    /// 面向普通引用类型的对象池。该类型不保证线程安全。
    /// </summary>
    public sealed class ObjPool<T> : IObjectPool<T> where T : class
    {
        private readonly Stack<T> availableItems = new();
        private readonly HashSet<T> allItems = new(ReferenceEqualityComparer<T>.Instance);
        private readonly HashSet<T> availableItemSet = new(ReferenceEqualityComparer<T>.Instance);
        private readonly Func<T> createFunc;
        private readonly Action<T> destroyAction;
        private bool isDisposed;

        public ObjPool(Func<T> createFunc, Action<T> destroyAction = null)
        {
            this.createFunc = createFunc ?? throw new ArgumentNullException(nameof(createFunc), "对象创建函数不能为空。");
            this.destroyAction = destroyAction;
        }

        /// <summary>
        /// 获取一个可用对象；没有空闲对象时由具体对象池创建。
        /// </summary>
        /// <returns>一个可用对象。</returns>
        public T Get()
        {
            ThrowIfDisposed();
            if (availableItems.Count > 0)
            {
                T item = availableItems.Pop();
                if (!availableItemSet.Remove(item))
                {
                    throw new InvalidOperationException("对象池状态异常：取出的空闲对象未登记。");
                }
                return item;
            }

            T createdItem = createFunc();
            if (createdItem is null)
            {
                throw new InvalidOperationException("对象创建函数不能返回空对象。");
            }
            if (!allItems.Add(createdItem))
            {
                throw new InvalidOperationException("对象创建函数返回了已归当前对象池所有的实例。");
            }
            return createdItem;
        }

        /// <summary>
        /// 将当前对象池借出的对象回收到空闲集合。
        /// </summary>
        /// <param name="item">要回收的对象。</param>
        public void Release(T item)
        {
            ThrowIfDisposed();
            if (item is null)
            {
                throw new ArgumentNullException(nameof(item), "不能回收空对象。");
            }
            if (!allItems.Contains(item))
            {
                throw new InvalidOperationException("不能回收不是由当前对象池创建的对象。");
            }
            if (!availableItemSet.Add(item))
            {
                throw new InvalidOperationException("不能重复回收已经处于空闲状态的对象。");
            }
            availableItems.Push(item);
        }

        /// <summary>
        /// 销毁全部空闲对象，当前对象池仍可继续使用。
        /// </summary>
        public void Clear()
        {
            ThrowIfDisposed();
            Exception firstException = null;
            while (availableItems.Count > 0)
            {
                T item = availableItems.Pop();
                availableItemSet.Remove(item);
                allItems.Remove(item);
                TryDestroyItem(item, ref firstException);
            }
            if (firstException != null)
            {
                throw firstException;
            }
        }

        /// <summary>
        /// 销毁对象池，释放所有对象。
        /// </summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            T[] items = new T[allItems.Count];
            allItems.CopyTo(items);
            availableItems.Clear();
            availableItemSet.Clear();
            allItems.Clear();

            Exception firstException = null;
            for (int i = 0; i < items.Length; i++)
            {
                TryDestroyItem(items[i], ref firstException);
            }
            if (firstException != null)
            {
                throw firstException;
            }
        }

        #region 内部实现
        private void TryDestroyItem(T item, ref Exception firstException)
        {
            if (destroyAction == null)
            {
                return;
            }
            try
            {
                destroyAction(item);
            }
            catch (Exception exception)
            {
                firstException ??= exception;
            }
        }

        private void ThrowIfDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(ObjPool<T>), "对象池已经销毁，不能继续使用。");
            }
        }
        #endregion
    }
}
