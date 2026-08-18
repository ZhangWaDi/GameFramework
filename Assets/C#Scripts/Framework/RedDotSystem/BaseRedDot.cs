using System;
using System.Collections.Generic;
using GameFramework.EventSystem;

namespace GameFramework.RedDotSystem
{
    /// <summary>
    /// 定义一种红点类型的事件监听、实例自检和父节点解析规则。
    /// 同一个规则实例由 RedDotMgr 统一管理该类型下的全部红点实例。
    /// </summary>
    public abstract class BaseRedDot
    {
        private readonly List<Action> removeEventListeners = new();
        private RedDotMgr manager;
        private Enum redDotType;
        private bool isInitialized;

        /// <summary>
        /// 当前规则绑定的红点类型。
        /// </summary>
        public Enum RedDotType
        {
            get
            {
                EnsureInitialized();
                return redDotType;
            }
        }

        internal void Initialize(RedDotMgr targetManager, Enum targetRedDotType)
        {
            if (targetManager == null)
            {
                throw new ArgumentNullException(nameof(targetManager));
            }

            if (targetRedDotType == null)
            {
                throw new ArgumentNullException(nameof(targetRedDotType));
            }

            if (isInitialized)
            {
                throw new InvalidOperationException($"红点规则“{GetType().Name}”已经绑定到类型“{redDotType}”，不能重复初始化。");
            }

            manager = targetManager;
            redDotType = targetRedDotType;
            isInitialized = true;

            try
            {
                OnRegisterEvents();
            }
            catch
            {
                Release();
                throw;
            }
        }

        internal void Release()
        {
            if (!isInitialized)
            {
                return;
            }

            try
            {
                OnRelease();
            }
            finally
            {
                for (int i = removeEventListeners.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        removeEventListeners[i]?.Invoke();
                    }
                    catch (Exception exception)
                    {
                        Logger.Instance.LogWarning($"解除红点规则“{GetType().Name}”的事件监听失败：{exception.Message}");
                    }
                }

                removeEventListeners.Clear();
                manager = null;
                redDotType = null;
                isInitialized = false;
            }
        }

        internal int Check(RedDotNodeKey nodeKey)
        {
            EnsureInitialized();
            int result = OnCheck(nodeKey);
            if (result < 0)
            {
                throw new InvalidOperationException($"红点规则“{GetType().Name}”检查节点“{nodeKey}”时返回了负数 {result}。");
            }

            return result;
        }

        internal IReadOnlyList<RedDotNodeKey> ResolveParentKeys(RedDotNodeKey nodeKey)
        {
            EnsureInitialized();
            return GetParentKeys(nodeKey) ?? Array.Empty<RedDotNodeKey>();
        }

        /// <summary>
        /// 使用节点中的实例 ID 执行当前红点实例的自检，返回非负红点数值。
        /// 返回 0 表示隐藏，大于 0 表示显示。
        /// </summary>
        protected abstract int OnCheck(RedDotNodeKey nodeKey);

        /// <summary>
        /// 返回当前实例的全部父节点完整 Key。没有父节点时返回空集合。
        /// </summary>
        protected virtual IReadOnlyList<RedDotNodeKey> GetParentKeys(RedDotNodeKey nodeKey)
        {
            return Array.Empty<RedDotNodeKey>();
        }

        /// <summary>
        /// 注册当前红点类型需要监听的事件。应使用 Listen 系列方法，以便自动解除监听。
        /// </summary>
        protected virtual void OnRegisterEvents()
        {
        }

        /// <summary>
        /// 释放不由 Listen 系列方法管理的外部资源。
        /// </summary>
        protected virtual void OnRelease()
        {
        }

        /// <summary>
        /// 监听无参事件，并在规则释放时自动解除监听。
        /// </summary>
        protected void Listen(Enum eventName, Action listener)
        {
            EnsureInitialized();
            EventCenter.Instance.AddEventListener(eventName, listener);
            removeEventListeners.Add(() =>
            {
                if (EventCenter.TryGetInstance(out EventCenter eventCenter))
                {
                    eventCenter.RemoveEventListener(eventName, listener);
                }
            });
        }

        protected void Listen<T>(Enum eventName, Action<T> listener)
        {
            EnsureInitialized();
            EventCenter.Instance.AddEventListener(eventName, listener);
            removeEventListeners.Add(() =>
            {
                if (EventCenter.TryGetInstance(out EventCenter eventCenter))
                {
                    eventCenter.RemoveEventListener(eventName, listener);
                }
            });
        }

        protected void Listen<T1, T2>(Enum eventName, Action<T1, T2> listener)
        {
            EnsureInitialized();
            EventCenter.Instance.AddEventListener(eventName, listener);
            removeEventListeners.Add(() =>
            {
                if (EventCenter.TryGetInstance(out EventCenter eventCenter))
                {
                    eventCenter.RemoveEventListener(eventName, listener);
                }
            });
        }

        protected void Listen<T1, T2, T3>(Enum eventName, Action<T1, T2, T3> listener)
        {
            EnsureInitialized();
            EventCenter.Instance.AddEventListener(eventName, listener);
            removeEventListeners.Add(() =>
            {
                if (EventCenter.TryGetInstance(out EventCenter eventCenter))
                {
                    eventCenter.RemoveEventListener(eventName, listener);
                }
            });
        }

        /// <summary>
        /// 将无参事件绑定为刷新当前类型的全部已知实例。
        /// </summary>
        protected void ListenRefreshAll(Enum eventName)
        {
            Listen(eventName, RefreshAllInstances);
        }

        /// <summary>
        /// 将带 int 实例 ID 的事件绑定为刷新当前类型的指定实例。
        /// </summary>
        protected void ListenRefreshInstance(Enum eventName)
        {
            Listen<int>(eventName, RefreshInstance);
        }

        /// <summary>
        /// 将事件参数转换为实例 ID 后刷新当前类型的指定实例。
        /// </summary>
        protected void ListenRefreshInstance<T>(Enum eventName, Func<T, int> instanceIdSelector)
        {
            if (instanceIdSelector == null)
            {
                throw new ArgumentNullException(nameof(instanceIdSelector));
            }

            Action<T> listener = eventArg => RefreshInstance(instanceIdSelector(eventArg));
            Listen(eventName, listener);
        }

        /// <summary>
        /// 刷新当前类型的全部已知实例。
        /// </summary>
        protected void RefreshAllInstances()
        {
            EnsureInitialized();
            manager.RefreshAll(redDotType);
        }

        /// <summary>
        /// 刷新当前类型的指定业务实例。
        /// 实例尚不存在时会自动创建。
        /// </summary>
        protected void RefreshInstance(int instanceId)
        {
            EnsureInitialized();
            manager.Refresh(RedDotNodeKey.Create(redDotType, instanceId));
        }

        /// <summary>
        /// 刷新当前类型的单例节点。
        /// </summary>
        protected void RefreshSingleton()
        {
            EnsureInitialized();
            manager.Refresh(RedDotNodeKey.Singleton(redDotType));
        }

        /// <summary>
        /// 根据业务 ID 集合同步当前类型的全部多实例节点。
        /// </summary>
        protected void SynchronizeInstances(IEnumerable<int> instanceIds)
        {
            EnsureInitialized();
            manager.SynchronizeInstances(redDotType, instanceIds);
        }

        private void EnsureInitialized()
        {
            if (!isInitialized)
            {
                throw new InvalidOperationException($"红点规则“{GetType().Name}”尚未由 RedDotMgr 注册。");
            }
        }
    }
}
