using System;
using System.Collections.Generic;

namespace GameFramework.EventSystem
{
    /// <summary>
    /// 基于枚举键的进程内事件中心。
    /// 事件监听和触发可以来自不同线程，但监听回调会在触发事件的线程执行。
    /// </summary>
    public sealed class EventCenter : Singleton<EventCenter>
    {
        public int EventCount
        {
            get
            {
                lock (syncRoot)
                {
                    return eventSlots.Count;
                }
            }
        }
        private readonly object syncRoot = new();
        private readonly Dictionary<Enum, IEventSlot> eventSlots = new();
        protected override void OnRelease()
        {
            ClearAllEventListeners();
        }

        #region 添加事件监听
        public void AddEventListener(Enum eventName, Action listener, bool isOnce = false)
        {
            AddListener(eventName, listener, isOnce);
        }

        public void AddEventListener<T>(
            Enum eventName,
            Action<T> listener,
            bool isOnce = false)
        {
            AddListener(eventName, listener, isOnce);
        }

        public void AddEventListener<T1, T2>(
            Enum eventName,
            Action<T1, T2> listener,
            bool isOnce = false)
        {
            AddListener(eventName, listener, isOnce);
        }

        public void AddEventListener<T1, T2, T3>(
            Enum eventName,
            Action<T1, T2, T3> listener,
            bool isOnce = false)
        {
            AddListener(eventName, listener, isOnce);
        }

        public void AddOnceEventListener(Enum eventName, Action listener)
        {
            AddListener(eventName, listener, true);
        }

        public void AddOnceEventListener<T>(Enum eventName, Action<T> listener)
        {
            AddListener(eventName, listener, true);
        }

        public void AddOnceEventListener<T1, T2>(
            Enum eventName,
            Action<T1, T2> listener)
        {
            AddListener(eventName, listener, true);
        }

        public void AddOnceEventListener<T1, T2, T3>(
            Enum eventName,
            Action<T1, T2, T3> listener)
        {
            AddListener(eventName, listener, true);
        }
        #endregion

        #region 移除事件监听
        public void RemoveEventListener(Enum eventName, Action listener)
        {
            RemoveListener(eventName, listener);
        }

        public void RemoveEventListener<T>(Enum eventName, Action<T> listener)
        {
            RemoveListener(eventName, listener);
        }

        public void RemoveEventListener<T1, T2>(
            Enum eventName,
            Action<T1, T2> listener)
        {
            RemoveListener(eventName, listener);
        }

        public void RemoveEventListener<T1, T2, T3>(
            Enum eventName,
            Action<T1, T2, T3> listener)
        {
            RemoveListener(eventName, listener);
        }

        /// <summary>
        /// 移除指定事件的全部普通监听和一次性监听。
        /// </summary>
        public void RemoveAllEventListeners(Enum eventName)
        {
            ValidateEventName(eventName);

            lock (syncRoot)
            {
                eventSlots.Remove(eventName);
            }
        }

        /// <summary>
        /// 清空事件中心中的全部监听。
        /// </summary>
        public void ClearAllEventListeners()
        {
            lock (syncRoot)
            {
                eventSlots.Clear();
            }
        }
        #endregion

        #region 触发事件
        public void EventTrigger(Enum eventName)
        {
            if (TryTakeListeners(
                    eventName,
                    out Action listeners,
                    out Action onceListeners))
            {
                listeners?.Invoke();
                onceListeners?.Invoke();
            }
        }

        public void EventTrigger<T>(Enum eventName, T arg)
        {
            if (TryTakeListeners(
                    eventName,
                    out Action<T> listeners,
                    out Action<T> onceListeners))
            {
                listeners?.Invoke(arg);
                onceListeners?.Invoke(arg);
            }
        }

        public void EventTrigger<T1, T2>(Enum eventName, T1 arg1, T2 arg2)
        {
            if (TryTakeListeners(
                    eventName,
                    out Action<T1, T2> listeners,
                    out Action<T1, T2> onceListeners))
            {
                listeners?.Invoke(arg1, arg2);
                onceListeners?.Invoke(arg1, arg2);
            }
        }

        public void EventTrigger<T1, T2, T3>(
            Enum eventName,
            T1 arg1,
            T2 arg2,
            T3 arg3)
        {
            if (TryTakeListeners(
                    eventName,
                    out Action<T1, T2, T3> listeners,
                    out Action<T1, T2, T3> onceListeners))
            {
                listeners?.Invoke(arg1, arg2, arg3);
                onceListeners?.Invoke(arg1, arg2, arg3);
            }
        }
        #endregion

        #region 内部实现
        private void AddListener<TDelegate>(
            Enum eventName,
            TDelegate listener,
            bool isOnce)
            where TDelegate : Delegate
        {
            ValidateEventName(eventName);
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            lock (syncRoot)
            {
                EventSlot<TDelegate> eventSlot = GetOrCreateEventSlot<TDelegate>(eventName);
                eventSlot.Add(listener, isOnce);
            }
        }

        private void RemoveListener<TDelegate>(Enum eventName, TDelegate listener)
            where TDelegate : Delegate
        {
            ValidateEventName(eventName);
            if (listener == null)
            {
                return;
            }

            lock (syncRoot)
            {
                if (!eventSlots.TryGetValue(eventName, out IEventSlot rawEventSlot))
                {
                    return;
                }

                EventSlot<TDelegate> eventSlot = GetTypedEventSlot<TDelegate>(eventName, rawEventSlot);
                eventSlot.Remove(listener);

                if (eventSlot.IsEmpty)
                {
                    eventSlots.Remove(eventName);
                }
            }
        }

        private bool TryTakeListeners<TDelegate>(
            Enum eventName,
            out TDelegate listeners,
            out TDelegate onceListeners)
            where TDelegate : Delegate
        {
            ValidateEventName(eventName);

            lock (syncRoot)
            {
                if (!eventSlots.TryGetValue(eventName, out IEventSlot rawEventSlot))
                {
                    listeners = null;
                    onceListeners = null;
                    return false;
                }

                EventSlot<TDelegate> eventSlot = GetTypedEventSlot<TDelegate>(eventName, rawEventSlot);
                eventSlot.TakeSnapshot(out listeners, out onceListeners);

                if (eventSlot.IsEmpty)
                {
                    eventSlots.Remove(eventName);
                }

                return listeners != null || onceListeners != null;
            }
        }

        private EventSlot<TDelegate> GetOrCreateEventSlot<TDelegate>(Enum eventName)
            where TDelegate : Delegate
        {
            if (eventSlots.TryGetValue(eventName, out IEventSlot rawEventSlot))
            {
                return GetTypedEventSlot<TDelegate>(eventName, rawEventSlot);
            }

            EventSlot<TDelegate> eventSlot = new();
            eventSlots.Add(eventName, eventSlot);
            return eventSlot;
        }

        private static EventSlot<TDelegate> GetTypedEventSlot<TDelegate>(
            Enum eventName,
            IEventSlot eventSlot)
            where TDelegate : Delegate
        {
            if (eventSlot is EventSlot<TDelegate> typedEventSlot)
            {
                return typedEventSlot;
            }

            throw new InvalidOperationException($"事件 {eventName.GetType().Name}.{eventName} 已使用委托签名 " + $"{eventSlot.DelegateType.Name} 注册，不能再作为 " + $"{typeof(TDelegate).Name} 使用。");
        }

        private static void ValidateEventName(Enum eventName)
        {
            if (eventName == null)
            {
                throw new ArgumentNullException(nameof(eventName));
            }
        }

        private interface IEventSlot
        {
            Type DelegateType { get; }
        }

        private sealed class EventSlot<TDelegate> : IEventSlot
            where TDelegate : Delegate
        {
            private TDelegate listeners;
            private TDelegate onceListeners;

            public Type DelegateType => typeof(TDelegate);

            public bool IsEmpty => listeners == null && onceListeners == null;

            public void Add(TDelegate listener, bool isOnce)
            {
                if (isOnce)
                {
                    onceListeners = (TDelegate)Delegate.Combine(onceListeners, listener);
                    return;
                }

                listeners = (TDelegate)Delegate.Combine(listeners, listener);
            }

            public void Remove(TDelegate listener)
            {
                listeners = (TDelegate)Delegate.Remove(listeners, listener);
                onceListeners = (TDelegate)Delegate.Remove(onceListeners, listener);
            }

            public void TakeSnapshot(
                out TDelegate currentListeners,
                out TDelegate currentOnceListeners)
            {
                currentListeners = listeners;
                currentOnceListeners = onceListeners;
                onceListeners = null;
            }
        }
        #endregion
    }
}
