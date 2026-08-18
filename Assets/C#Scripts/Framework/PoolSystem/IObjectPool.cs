using System;

namespace GameFramework.PoolSystem
{
    /// <summary>
    /// 引用对象池的最小公共接口。
    /// </summary>
    public interface IObjectPool<T> : IDisposable where T : class
    {
        /// <summary>
        /// 获取一个可用对象；没有空闲对象时由具体对象池创建。
        /// </summary>
        T Get();

        /// <summary>
        /// 将当前对象池借出的对象回收到空闲集合。
        /// </summary>
        void Release(T item);

        /// <summary>
        /// 销毁全部空闲对象，当前对象池仍可继续使用。
        /// </summary>
        void Clear();
    }
}
